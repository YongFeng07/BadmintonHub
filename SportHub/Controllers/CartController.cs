using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SportHub.Controllers;

/// <summary>
/// Member booking cart and cart checkout (revised spec). Cart lines are added from
/// the booking page, edited/removed here, and checked out transactionally with an
/// optional discount voucher; the batch-payment step then confirms all of the
/// checkout's bookings at once.
/// </summary>
[Authorize]
public class CartController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICartService _cartService;
    private readonly ICheckoutService _checkoutService;
    private readonly IToyyibPayService _toyyibPay;

    public CartController(
        ApplicationDbContext db,
        ICartService cartService,
        ICheckoutService checkoutService,
        IToyyibPayService toyyibPay)
    {
        _db = db;
        _cartService = cartService;
        _checkoutService = checkoutService;
        _toyyibPay = toyyibPay;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        var items = await _cartService.GetItemsAsync(CurrentUserId);
        return View(new CartIndexViewModel { Items = items });
    }

    /// <summary>
    /// Adds a slot to the cart. Bound to the booking page's field names (CourtId, Date,
    /// StartTime, DurationHours) so that page can post here directly via formaction.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(int courtId, DateOnly date, TimeOnly startTime, int durationHours)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Choose a date, court and open slot first.";
            return RedirectToAction("Create", "Reservations", new { courtId, date });
        }

        var (success, error, _) = await _cartService.AddAsync(CurrentUserId, courtId, date, startTime, durationHours);
        if (!success)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction("Create", "Reservations", new { courtId, date });
        }

        TempData["SuccessMessage"] = "Slot added to your cart.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int itemId, int durationHours)
    {
        var (success, error) = await _cartService.UpdateAsync(CurrentUserId, itemId, null, null, durationHours);
        if (!success) TempData["ErrorMessage"] = error;
        else TempData["SuccessMessage"] = "Cart updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int itemId)
    {
        var (success, error) = await _cartService.RemoveAsync(CurrentUserId, itemId);
        if (!success) TempData["ErrorMessage"] = error;
        else TempData["SuccessMessage"] = "Item removed from your cart.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchRemove(List<int> itemIds)
    {
        var removed = await _cartService.RemoveBatchAsync(CurrentUserId, itemIds);
        if (removed > 0) TempData["SuccessMessage"] = $"{removed} item(s) removed from your cart.";
        else TempData["InfoMessage"] = "Select at least one item to remove.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear()
    {
        var removed = await _cartService.ClearAsync(CurrentUserId);
        TempData["InfoMessage"] = removed > 0 ? "Your cart has been emptied." : "Your cart is already empty.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(List<int> itemIds, string? voucherCode)
    {
        var (success, error, reservations, warning) =
            await _checkoutService.CheckoutAsync(CurrentUserId, itemIds, voucherCode);
        if (!success)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Index));
        }

        var discount = reservations.Sum(r => r.DiscountAmount);
        var message = discount > 0
            ? $"Checkout created {reservations.Count} booking(s); voucher {voucherCode!.Trim().ToUpperInvariant()} saved you RM {discount:0.00}."
            : $"Checkout created {reservations.Count} booking(s). Complete the payment to confirm them.";
        if (!string.IsNullOrWhiteSpace(warning))
            message += $" {warning}";
        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(CheckoutComplete),
            new { reservationIds = reservations.Select(r => r.Id).ToList() });
    }

    /// <summary>
    /// AJAX endpoint (G-M2): live preview of a voucher code against the currently
    /// checked cart lines. Read-only — usage counting only happens at checkout.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VoucherPreview(List<int> itemIds, string? voucherCode)
    {
        var (success, error, _, subtotal, discount, netTotal, warning) =
            await _checkoutService.PreviewAsync(CurrentUserId, itemIds, voucherCode);
        return Json(new { success, error, subtotal, discount, netTotal, warning });
    }

    public async Task<IActionResult> CheckoutComplete(List<int> reservationIds)
    {
        var reservations = await _db.Reservations
            .Include(r => r.Court)
            .Include(r => r.Payment)
            .Where(r => reservationIds.Contains(r.Id) && r.UserId == CurrentUserId)
            .ToListAsync();

        if (reservations.Count != reservationIds.Distinct().Count())
            return Forbid();

        if (reservations.Count == 0)
        {
            TempData["InfoMessage"] = "Nothing to pay.";
            return RedirectToAction(nameof(Index));
        }

        return View(new CheckoutPaymentViewModel
        {
            ReservationIds = reservationIds,
            Reservations = reservations.OrderBy(r => r.ReservationDate).ThenBy(r => r.StartTime).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckoutComplete(CheckoutPaymentViewModel model)
    {
        // ToyyibPay: no payment is taken here. A bill is opened at the gateway
        // (or the simulated demo page) and the member is redirected to pay there;
        // the return endpoint marks the batch paid after verification.
        if (model.Method == PaymentMethod.ToyyibPay)
        {
            var pending = await _db.Payments
                .Where(p => model.ReservationIds.Contains(p.ReservationId)
                            && p.UserId == CurrentUserId
                            && p.Status == PaymentStatus.Pending)
                .OrderBy(p => p.Id)
                .ToListAsync();

            if (pending.Count == 0)
            {
                TempData["ErrorMessage"] = "There is nothing left to pay for these bookings.";
                return RedirectToAction(nameof(CheckoutComplete), new { reservationIds = model.ReservationIds });
            }

            var amount = pending.Sum(p => p.Amount);
            var returnUrl = Url.Action("ToyyibPayReturn", "Payments", new { }, Request.Scheme)!;
            var (ok, _, paymentUrl, billError) =
                await _toyyibPay.CreateBillAsync(CurrentUserId, pending, amount, returnUrl);

            if (!ok)
            {
                TempData["ErrorMessage"] = billError;
                return RedirectToAction(nameof(CheckoutComplete), new { reservationIds = model.ReservationIds });
            }

            // Real mode: hosted ToyyibPay checkout. Simulated mode: the local demo page.
            return Redirect(paymentUrl!);
        }

        var (success, error, paid) = await _checkoutService.MarkBatchPaidAsync(
            CurrentUserId, model.ReservationIds, model.Method);

        if (!success)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(CheckoutComplete), new { reservationIds = model.ReservationIds });
        }

        TempData["SuccessMessage"] = $"Payment recorded — {paid} booking(s) confirmed.";
        return RedirectToAction(nameof(Paid), new { reservationIds = model.ReservationIds });
    }

    /// <summary>Success page after the batch payment; lists the confirmed bookings.</summary>
    public async Task<IActionResult> Paid(List<int> reservationIds)
    {
        var reservations = await _db.Reservations
            .Include(r => r.Court)
            .ThenInclude(c => c!.Facility)
            .Include(r => r.Payment)
            .Where(r => reservationIds.Contains(r.Id) && r.UserId == CurrentUserId)
            .ToListAsync();

        if (reservations.Count == 0)
            return NotFound();

        return View(reservations.OrderBy(r => r.ReservationDate).ThenBy(r => r.StartTime).ToList());
    }
}
