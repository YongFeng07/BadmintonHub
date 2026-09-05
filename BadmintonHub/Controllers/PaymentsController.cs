using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BadmintonHub.Controllers;

/// <summary>
/// Member payment history plus the ToyyibPay gateway pages: the simulated
/// fallback "payment page" and the return endpoint the real gateway (or the
/// simulated page) redirects back to.
/// </summary>
[Authorize]
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IToyyibPayService _toyyibPay;
    private readonly ICheckoutService _checkout;
    private readonly ToyyibPayOptions _toyyibPayOptions;

    public PaymentsController(
        ApplicationDbContext db,
        IToyyibPayService toyyibPay,
        ICheckoutService checkout,
        ToyyibPayOptions toyyibPayOptions)
    {
        _db = db;
        _toyyibPay = toyyibPay;
        _checkout = checkout;
        _toyyibPayOptions = toyyibPayOptions;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        var payments = await _db.Payments
            .Include(p => p.Reservation).ThenInclude(r => r!.Court)
            .Where(p => p.UserId == CurrentUserId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return View(payments);
    }

    /// <summary>
    /// Simulated fallback "gateway" page for SIM- bills (demo mode, clearly
    /// labelled). Plays the role of the hosted ToyyibPay payment page when no
    /// credentials are configured.
    /// </summary>
    public async Task<IActionResult> ToyyibPaySimulated(string billCode)
    {
        if (!_toyyibPayOptions.IsSimulated)
            return NotFound();

        var payments = await _db.Payments
            .Include(p => p.Reservation).ThenInclude(r => r!.Court)
            .Where(p => p.GatewayBillCode == billCode && p.UserId == CurrentUserId)
            .OrderBy(p => p.Id)
            .ToListAsync();

        if (payments.Count == 0)
            return NotFound();

        return View(new ToyyibPaySimulatedViewModel
        {
            BillCode = billCode,
            Amount = payments.Sum(p => p.Amount),
            Reservations = payments.Select(p => p.Reservation!).ToList()
        });
    }

    /// <summary>Simulated payment result: "pay" = success (status_id 1), "cancel" = failed (3).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ToyyibPaySimulate(string billCode, string action)
    {
        var statusId = action == "pay" ? "1" : "3";
        return RedirectToAction(nameof(ToyyibPayReturn),
            new { billcode = billCode, status_id = statusId, order_id = billCode });
    }

    /// <summary>
    /// Return endpoint after the gateway redirect (real or simulated). Real mode
    /// re-verifies the bill against the ToyyibPay API before marking anything
    /// paid, so a forged status_id is harmless.
    /// </summary>
    public async Task<IActionResult> ToyyibPayReturn(string? billcode, string? status_id, string? order_id)
    {
        if (string.IsNullOrWhiteSpace(billcode))
            return NotFound();

        var (success, error, reservationIds, statusId) =
            await _toyyibPay.ProcessReturnAsync(billcode, status_id);

        if (!success)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Index));
        }

        if (statusId == "1")
        {
            var (paid, paidError, count) = await _checkout.MarkBatchPaidAsync(
                CurrentUserId, reservationIds, PaymentMethod.ToyyibPay, billcode);

            if (!paid)
            {
                TempData["ErrorMessage"] = paidError;
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = $"ToyyibPay payment recorded — {count} booking(s) confirmed.";
            return RedirectToAction("Paid", "Cart", new { reservationIds });
        }

        // status_id 2 = still pending at the bank; 3 = failed/cancelled.
        if (statusId == "2")
            TempData["InfoMessage"] = "Your payment is still being processed by the bank. We will update the booking once it completes.";
        else
            TempData["ErrorMessage"] = "The payment was not completed. You can try again from the payment page.";

        return RedirectToAction("CheckoutComplete", "Cart", new { reservationIds });
    }
}
