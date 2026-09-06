using SportHub.Data;
using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Services;

/// <summary>
/// All reservation business rules live here so the booking flow, the staff
/// administration and the member pages share one consistent implementation.
/// </summary>
public class ReservationService : IReservationService
{
    private readonly ApplicationDbContext _db;
    private readonly ICourtService _courtService;
    private readonly IEmailService _emails;

    public ReservationService(ApplicationDbContext db, ICourtService courtService, IEmailService emails)
    {
        _db = db;
        _courtService = courtService;
        _emails = emails;
    }

    /// <summary>G-M5: the site footer line for the PDF e-receipt (admin-editable).</summary>
    private string ReceiptFooter =>
        _db.SystemSettings.FirstOrDefault(s => s.Key == "ReceiptFooter")?.Value
        ?? "SportHub · 12 Jalan Ampang, 50450 Kuala Lumpur · 03-4142 8899 · info@sporthub.my";

    public async Task<(bool Success, string? Error, Reservation? Reservation)> CreateAsync(
        int userId, int courtId, DateOnly date, TimeOnly startTime, int durationHours, string? notes)
    {
        if (durationHours < 1 || durationHours > 4)
            return (false, "Duration must be between 1 and 4 hours.", null);

        if (date < DateOnly.FromDateTime(DateTime.Today))
            return (false, "The selected date is in the past.", null);

        var court = await _db.Courts.FindAsync(courtId);
        if (court == null)
            return (false, "Court not found.", null);

        if (court.Status != CourtStatus.Available)
            return (false, "This court is currently not available for booking.", null);

        var endTime = startTime.AddHours(durationHours);

        // Business rule: the window must lie fully inside Open availability slots.
        if (!await _courtService.IsWindowWithinOpenSlotsAsync(courtId, date, startTime, endTime))
            return (false, "The selected time is outside the available booking hours.", null);

        // Business rule: no double booking.
        if (await _courtService.HasOverlappingReservationAsync(courtId, date, startTime, endTime))
            return (false, "That time slot has just been booked by someone else. Please choose another slot.", null);

        // G-M3: another member's active cart hold soft-blocks the direct booking too
        // (the same rule the cart and checkout paths enforce).
        if (await BookingRules.HasActiveHoldAsync(_db, courtId, date, startTime, endTime, userId))
            return (false, "That slot is currently being held in another member's cart. It is released automatically after 15 minutes if not checked out.", null);

        var maxId = await _db.Reservations.MaxAsync(r => (int?)r.Id) ?? 0;
        var reservation = new Reservation
        {
            ReservationReference = $"SH-{date.Year}-{maxId + 1:000000}",
            UserId = userId,
            CourtId = courtId,
            ReservationDate = date,
            StartTime = startTime,
            EndTime = endTime,
            DurationHours = durationHours,
            TotalAmount = court.HourlyRate * durationHours,
            Status = ReservationStatus.Pending,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        _db.Reservations.Add(reservation);

        // Payment starts Pending; it is confirmed (and the booking confirmed) on payment.
        _db.Payments.Add(new Payment
        {
            Reservation = reservation,
            UserId = userId,
            Amount = reservation.TotalAmount,
            Method = PaymentMethod.OnlineTransfer,
            Status = PaymentStatus.Pending
        });

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "Reservation created",
            Message = $"Booking {reservation.ReservationReference} for Court {court.CourtNumber} on {date:dd MMM yyyy} is awaiting payment.",
            Type = NotificationType.Reservation,
            TargetUrl = $"/Reservations/Details/{reservation.Id}"
        });

        // G-M5: staff are notified the moment a pending booking needs their attention.
        var adminIds = await _db.Users
            .Where(u => u.Role == Role.Admin || u.Role == Role.SuperAdmin)
            .Select(u => u.Id)
            .ToListAsync();
        foreach (var adminId in adminIds)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = adminId,
                Title = "New booking pending",
                Message = $"{reservation.ReservationReference} for Court {court.CourtNumber} on {date:dd MMM yyyy} is awaiting approval.",
                Type = NotificationType.Reservation,
                TargetUrl = $"/AdminReservations?search={reservation.ReservationReference}"
            });
        }

        // G-M4: booking a court removes it from the member's wishlist (it is clearly
        // bookable now — the wishlist's job is done). Same save as the reservation.
        var wishlisted = await _db.WishlistItems
            .FirstOrDefaultAsync(w => w.UserId == userId && w.CourtId == courtId);
        if (wishlisted != null) _db.WishlistItems.Remove(wishlisted);

        // G-M3: claim the hourly slots atomically. The unique (CourtId, Date,
        // StartTime) index turns a concurrent double booking into a DbUpdateException.
        BookingRules.ClaimWindow(_db, reservation);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The pre-check raced with another booking; the index caught the loser.
            _db.ChangeTracker.Clear();
            return (false, "That time slot has just been booked by someone else. Please choose another slot.", null);
        }

        // G-M5: booking-received email (demo sender when no SMTP is configured).
        var member = await _db.Users.FindAsync(userId);
        if (member != null && !string.IsNullOrWhiteSpace(member.Email))
        {
            await _emails.SendAsync(member.Email,
                $"Booking received — {reservation.ReservationReference}",
                EmailTemplates.BookingReceivedEmail(member.FullName, reservation.ReservationReference,
                    $"Court {court.CourtNumber}", $"{date:dd MMM yyyy}, {startTime:HH:mm}–{endTime:HH:mm}",
                    reservation.TotalAmount.ToString("0.00")));
        }

        return (true, null, reservation);
    }

    /// <summary>
    /// G-M3 payment timeout: cancels Pending reservations that were not paid within
    /// <paramref name="timeout"/>, fails their payment record, releases their slot
    /// claims and notifies the member. Returns how many were released.
    /// </summary>
    public async Task<int> ReleaseUnpaidPendingAsync(TimeSpan timeout)
    {
        var cutoff = DateTime.Now.Subtract(timeout);
        var stale = await _db.Reservations
            .Include(r => r.Payment)
            .Where(r => r.Status == ReservationStatus.Pending && r.CreatedAt < cutoff)
            .ToListAsync();

        foreach (var reservation in stale)
        {
            reservation.Status = ReservationStatus.Cancelled;
            reservation.CancellationReason = "Payment was not completed within 30 minutes, so the booking was released.";
            reservation.CancelledAt = DateTime.Now;
            reservation.UpdatedAt = DateTime.Now;

            if (reservation.Payment != null && reservation.Payment.Status == PaymentStatus.Pending)
                reservation.Payment.Status = PaymentStatus.Failed;

            await BookingRules.ReleaseWindowAsync(_db, reservation.Id);

            _db.Notifications.Add(new Notification
            {
                UserId = reservation.UserId,
                Title = "Booking released",
                Message = $"Booking {reservation.ReservationReference} was released because payment was not received within 30 minutes. The slots are available again.",
                Type = NotificationType.Reservation,
                TargetUrl = "/Reservations/MyReservations"
            });

            // G-M5: the member is told by email too (demo sender when no SMTP is configured).
            var member = await _db.Users.FindAsync(reservation.UserId);
            if (member != null && !string.IsNullOrWhiteSpace(member.Email))
            {
                await _emails.SendAsync(member.Email,
                    $"Booking released — {reservation.ReservationReference}",
                    EmailTemplates.BookingReleasedEmail(member.FullName, reservation.ReservationReference));
            }
        }

        if (stale.Count > 0)
            await _db.SaveChangesAsync();

        return stale.Count;
    }

    public async Task<(bool Success, string? Error)> MarkPaidAsync(
        int reservationId, int userId, PaymentMethod method, string? reference, bool isBackOffice = false,
        bool sendReceiptEmail = true)
    {
        var reservation = await _db.Reservations
            .Include(r => r.Payment)
            .Include(r => r.Court).ThenInclude(c => c!.Facility)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null)
            return (false, "Reservation not found.");

        if (reservation.UserId != userId && !isBackOffice)
            return (false, "You can only pay for your own reservations.");

        if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Rejected or ReservationStatus.Completed)
            return (false, "Payment is not allowed for this reservation status.");

        if (reservation.Payment == null)
            return (false, "Payment record not found.");

        if (reservation.Payment.Status == PaymentStatus.Paid)
            return (false, "This reservation is already paid.");

        reservation.Payment.Method = method;
        reservation.Payment.Status = PaymentStatus.Paid;
        reservation.Payment.PaidAt = DateTime.Now;
        reservation.Payment.PaymentReference = string.IsNullOrWhiteSpace(reference)
            ? $"REF-{reservation.ReservationReference}"
            : reference.Trim();

        // Business rule: payment and reservation status stay consistent.
        reservation.Status = ReservationStatus.Confirmed;
        reservation.UpdatedAt = DateTime.Now;

        _db.Notifications.Add(new Notification
        {
            UserId = reservation.UserId,
            Title = "Booking confirmed",
            Message = $"Payment received for {reservation.ReservationReference}. Your booking on Court {reservation.Court?.CourtNumber} is confirmed.",
            Type = NotificationType.Payment,
            TargetUrl = $"/Reservations/Details/{reservation.Id}"
        });

        await _db.SaveChangesAsync();

        // G-M5: PDF e-receipt by email on every single-booking payment (member
        // counter-pay, staff counter-pay and gateway callback). The checkout batch
        // sends one email with all receipts attached instead (sendReceiptEmail: false).
        if (sendReceiptEmail && reservation.User != null && !string.IsNullOrWhiteSpace(reservation.User.Email))
        {
            var pdf = ReceiptPdfGenerator.Generate(reservation, ReceiptFooter);
            await _emails.SendAsync(reservation.User.Email,
                $"E-receipt — {reservation.ReservationReference}",
                EmailTemplates.BookingConfirmedEmail(reservation.User.FullName, reservation.ReservationReference,
                    $"Court {reservation.Court?.CourtNumber}", $"{reservation.ReservationDate:dd MMM yyyy}, {reservation.StartTime:HH:mm}–{reservation.EndTime:HH:mm}"),
                new[] { new EmailAttachment($"Receipt-{reservation.ReservationReference}.pdf", pdf, "application/pdf") });
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CancelAsync(
        int reservationId, int userId, string? reason, bool isBackOffice = false)
    {
        var reservation = await _db.Reservations
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null)
            return (false, "Reservation not found.");

        if (reservation.UserId != userId && !isBackOffice)
            return (false, "You can only cancel your own reservations.");

        if (reservation.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
            return (false, $"A {reservation.Status.ToString().ToLowerInvariant()} reservation cannot be cancelled.");

        var startDateTime = reservation.ReservationDate.ToDateTime(reservation.StartTime);
        if (startDateTime <= DateTime.Now && !isBackOffice)
            return (false, "This reservation has already started and cannot be cancelled.");

        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        reservation.CancelledAt = DateTime.Now;
        reservation.UpdatedAt = DateTime.Now;

        // Business rule: a paid booking that is cancelled becomes a refund.
        if (reservation.Payment != null && reservation.Payment.Status == PaymentStatus.Paid)
            reservation.Payment.Status = PaymentStatus.Refunded;

        // G-M3: the cancelled booking's hourly claims go back on the market.
        await BookingRules.ReleaseWindowAsync(_db, reservation.Id);

        _db.Notifications.Add(new Notification
        {
            UserId = reservation.UserId,
            Title = "Reservation cancelled",
            Message = $"Booking {reservation.ReservationReference} was cancelled." +
                      (reservation.Payment?.Status == PaymentStatus.Refunded ? " Your payment has been refunded." : string.Empty),
            Type = NotificationType.Reservation,
            TargetUrl = "/Reservations/MyReservations"
        });

        await _db.SaveChangesAsync();

        // G-M5: cancellation email with the refund note (demo sender when no SMTP is configured).
        var member = await _db.Users.FindAsync(reservation.UserId);
        if (member != null && !string.IsNullOrWhiteSpace(member.Email))
        {
            await _emails.SendAsync(member.Email,
                $"Booking cancelled — {reservation.ReservationReference}",
                EmailTemplates.BookingCancelledEmail(member.FullName, reservation.ReservationReference,
                    reservation.Payment?.Status == PaymentStatus.Refunded, reservation.CancellationReason));
        }

        return (true, null);
    }
}
