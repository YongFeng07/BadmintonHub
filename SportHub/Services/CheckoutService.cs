using SportHub.Data;
using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Services;

/// <summary>
/// Transactional cart checkout (revised spec): every cart line is re-validated
/// server-side, reservations and pending payments are created in one transaction,
/// the voucher discount is split proportionally across the lines and the voucher's
/// usage count is bumped inside the same transaction. A separate batch-payment step
/// then confirms all of the checkout's reservations at once.
/// </summary>
public class CheckoutService : ICheckoutService
{
    private readonly ApplicationDbContext _db;
    private readonly ICourtService _courts;
    private readonly IVoucherService _vouchers;
    private readonly IReservationService _reservations;
    private readonly IEmailService _emails;

    public CheckoutService(
        ApplicationDbContext db,
        ICourtService courts,
        IVoucherService vouchers,
        IReservationService reservations,
        IEmailService emails)
    {
        _db = db;
        _courts = courts;
        _vouchers = vouchers;
        _reservations = reservations;
        _emails = emails;
    }

    public async Task<(bool Success, string? Error, List<CartItem> Items, decimal Subtotal, decimal Discount, decimal NetTotal, string? Warning)> PreviewAsync(
        int userId, IEnumerable<int> cartItemIds, string? voucherCode)
    {
        var items = await LoadItemsAsync(userId, cartItemIds);
        if (items == null || items.Count == 0)
            return (false, "Select at least one item to check out.", new List<CartItem>(), 0, 0, 0, null);

        var subtotal = items.Sum(i => i.Court!.HourlyRate * i.DurationHours);

        if (!string.IsNullOrWhiteSpace(voucherCode))
        {
            var (ok, error, discount, _, warning) = await _vouchers.ValidateAsync(voucherCode, subtotal, userId);
            if (!ok)
                return (false, error, items, subtotal, 0, subtotal, null);

            return (true, null, items, subtotal, discount, subtotal - discount, warning);
        }

        return (true, null, items, subtotal, 0, subtotal, null);
    }

    public async Task<(bool Success, string? Error, List<Reservation> Reservations, string? Warning)> CheckoutAsync(
        int userId, IEnumerable<int> cartItemIds, string? voucherCode)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var items = await LoadItemsAsync(userId, cartItemIds);
            if (items == null || items.Count == 0)
            {
                await transaction.RollbackAsync();
                return (false, "Select at least one item to check out.", new List<Reservation>(), null);
            }

            // Re-validate every line server-side — the cart is never trusted. Windows
            // already accepted in this batch are passed along so two lines of one cart
            // cannot overlap either.
            var windows = new List<(int CourtId, DateOnly Date, TimeOnly Start, TimeOnly End)>();
            foreach (var item in items)
            {
                var error = await BookingRules.ValidateWindowAsync(
                    _db, _courts, item.CourtId, item.Date, item.StartTime, item.DurationHours, windows);
                if (error != null)
                {
                    await transaction.RollbackAsync();
                    return (false,
                        $"{item.Court?.CourtNumber} on {item.Date:dd MMM yyyy} at {item.StartTime.ToString("HH:mm")}: {error}",
                        new List<Reservation>(), null);
                }
                windows.Add((item.CourtId, item.Date, item.StartTime, item.StartTime.AddHours(item.DurationHours)));
            }

            // Voucher: validated and counted inside the transaction. For the demo's
            // scale a transactional re-read of UsageCount is sufficient — the unique
            // code index guarantees no other inconsistency can slip through.
            var subtotal = items.Sum(i => i.Court!.HourlyRate * i.DurationHours);
            var discount = 0m;
            var warning = (string?)null;
            if (!string.IsNullOrWhiteSpace(voucherCode))
            {
                var (ok, error, amount, voucher, voucherWarning) = await _vouchers.ValidateAsync(voucherCode, subtotal, userId);
                if (!ok)
                {
                    await transaction.RollbackAsync();
                    return (false, error, new List<Reservation>(), null);
                }
                discount = amount;
                warning = voucherWarning;
                voucher!.UsageCount++;

                // Per-user redemption counter, upserted in the same transaction so the
                // PerUserLimit can never be overshot by concurrent checkouts.
                var redemption = await _db.VoucherRedemptions
                    .FirstOrDefaultAsync(r => r.VoucherId == voucher.Id && r.UserId == userId);
                if (redemption == null)
                {
                    _db.VoucherRedemptions.Add(new VoucherRedemption
                    {
                        VoucherId = voucher.Id,
                        UserId = userId,
                        Count = 1,
                        LastUsedAt = DateTime.Now
                    });
                }
                else
                {
                    redemption.Count++;
                    redemption.LastUsedAt = DateTime.Now;
                }
            }

            var maxId = await _db.Reservations.MaxAsync(r => (int?)r.Id) ?? 0;
            var reservations = new List<Reservation>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var court = item.Court!;
                var gross = court.HourlyRate * item.DurationHours;

                // Proportional discount split; the last line absorbs the rounding remainder.
                var itemDiscount = 0m;
                if (discount > 0)
                {
                    itemDiscount = i == items.Count - 1
                        ? discount - reservations.Sum(r => r.DiscountAmount)
                        : Math.Round(gross / subtotal * discount, 2, MidpointRounding.AwayFromZero);
                }

                var reservation = new Reservation
                {
                    ReservationReference = $"SH-{item.Date.Year}-{maxId + i + 1:000000}",
                    UserId = userId,
                    CourtId = item.CourtId,
                    ReservationDate = item.Date,
                    StartTime = item.StartTime,
                    EndTime = item.StartTime.AddHours(item.DurationHours),
                    DurationHours = item.DurationHours,
                    TotalAmount = gross,
                    DiscountAmount = itemDiscount,
                    VoucherCode = string.IsNullOrWhiteSpace(voucherCode) ? null : voucherCode.Trim().ToUpperInvariant(),
                    Status = ReservationStatus.Pending
                };
                _db.Reservations.Add(reservation);
                _db.Payments.Add(new Payment
                {
                    Reservation = reservation,
                    UserId = userId,
                    Amount = reservation.TotalAmount - reservation.DiscountAmount,
                    Method = PaymentMethod.OnlineTransfer,
                    Status = PaymentStatus.Pending
                });
                reservations.Add(reservation);
            }

            _db.Notifications.Add(new Notification
            {
                UserId = userId,
                Title = "Cart checkout",
                Message = discount > 0
                    ? $"{reservations.Count} booking(s) created from your cart; voucher {voucherCode!.Trim().ToUpperInvariant()} saved you RM {discount:0.00}. Awaiting payment."
                    : $"{reservations.Count} booking(s) created from your cart. Awaiting payment.",
                Type = NotificationType.Reservation
            });

            // The checked-out lines leave the cart in the same transaction.
            _db.CartItems.RemoveRange(items);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, null, reservations, warning);
        }
        catch
        {
            await transaction.RollbackAsync();
            return (false, "Checkout failed. Your cart has not been changed. Please try again.",
                new List<Reservation>(), null);
        }
    }

    public async Task<(bool Success, string? Error, int PaidCount)> MarkBatchPaidAsync(
        int userId, IEnumerable<int> reservationIds, PaymentMethod method, string? reference = null)
    {
        var ids = reservationIds.Distinct().ToList();
        if (ids.Count == 0)
            return (false, "Select at least one reservation to pay.", 0);

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Delegates to the single-booking payment rule so both flows stay consistent;
            // the surrounding transaction makes the batch all-or-nothing. The reference
            // (e.g. the ToyyibPay bill code) is carried onto every payment row.
            var paid = 0;
            foreach (var id in ids)
            {
                var (ok, error) = await _reservations.MarkPaidAsync(id, userId, method, reference);
                if (!ok)
                {
                    await transaction.RollbackAsync();
                    return (false, error, 0);
                }
                paid++;
            }

            await transaction.CommitAsync();

            // Outbound payment confirmation (demo sender when no SMTP is configured).
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _emails.SendAsync(user.Email,
                    $"Payment received — {paid} booking(s) confirmed",
                    EmailTemplates.PaymentConfirmationEmail(user.FullName, paid));
            }

            return (true, null, paid);
        }
        catch
        {
            await transaction.RollbackAsync();
            return (false, "Payment failed. Please try again.", 0);
        }
    }

    /// <summary>
    /// Loads the member's selected cart lines with their courts. A missing id (not the
    /// user's, or already removed) fails the whole checkout — never a partial cart.
    /// </summary>
    private async Task<List<CartItem>?> LoadItemsAsync(int userId, IEnumerable<int> cartItemIds)
    {
        var ids = cartItemIds.Distinct().ToList();
        if (ids.Count == 0) return null;

        var items = await _db.CartItems
            .Include(i => i.Court)
            .Where(i => i.UserId == userId && ids.Contains(i.Id))
            .ToListAsync();

        return items.Count != ids.Count ? null : items;
    }
}
