using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SportHub.Controllers;

[Authorize]
public class ReservationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICourtService _courtService;
    private readonly IReservationService _reservationService;
    private readonly IEmailService _emails;

    public ReservationsController(ApplicationDbContext db, ICourtService courtService,
        IReservationService reservationService, IEmailService emails)
    {
        _db = db;
        _courtService = courtService;
        _reservationService = reservationService;
        _emails = emails;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsBackOffice => User.IsInRole(nameof(Role.Admin)) || User.IsInRole(nameof(Role.SuperAdmin));

    // GET /Reservations/Create?courtId=3&date=2026-08-23
    public async Task<IActionResult> Create(int? courtId, DateOnly? date)
    {
        var model = new ReservationCreateViewModel
        {
            CourtId = courtId ?? 0,
            Date = date ?? DateOnly.FromDateTime(DateTime.Today)
        };

        ViewBag.Courts = await _db.Courts
            .Where(c => c.Status == CourtStatus.Available)
            .OrderBy(c => c.CourtNumber)
            .Select(c => new { c.Id, c.CourtNumber, c.CourtType, c.HourlyRate })
            .ToListAsync();

        return View(model);
    }

    /// <summary>AJAX endpoint feeding the booking slot grid (same availability logic as the public page).</summary>
    [HttpGet]
    public async Task<IActionResult> GetSlots(int courtId, DateOnly date)
    {
        var court = await _db.Courts.FindAsync(courtId);
        if (court == null)
            return Json(new { success = false, message = "Court not found." });

        if (date < DateOnly.FromDateTime(DateTime.Today))
            return Json(new { success = false, message = "The selected date is in the past." });

        var slots = await _courtService.GetAvailabilityForDateAsync(date, courtId);
        return Json(new
        {
            success = true,
            court = new { court.Id, court.CourtNumber, court.CourtType, court.HourlyRate },
            slots
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReservationCreateViewModel model)
    {
        ViewBag.Courts = await _db.Courts
            .Where(c => c.Status == CourtStatus.Available)
            .OrderBy(c => c.CourtNumber)
            .Select(c => new { c.Id, c.CourtNumber, c.CourtType, c.HourlyRate })
            .ToListAsync();

        // TimeOnly is not supported by RangeAttribute; enforce a real selection here.
        if (ModelState.IsValid && model.StartTime == TimeOnly.MinValue)
            ModelState.AddModelError(nameof(model.StartTime), "Please select a start time.");

        if (!ModelState.IsValid)
            return View(model);

        var (success, error, reservation) = await _reservationService.CreateAsync(
            CurrentUserId, model.CourtId, model.Date, model.StartTime, model.DurationHours, model.Notes);

        if (!success || reservation == null)
        {
            ModelState.AddModelError(string.Empty, error ?? "Could not create the reservation.");
            return View(model);
        }

        TempData["SuccessMessage"] = $"Reservation {reservation.ReservationReference} created. Complete the payment to confirm your booking.";
        return RedirectToAction(nameof(Pay), new { id = reservation.Id });
    }

    // GET /Reservations/MyReservations?date=2026-08-23&tab=upcoming
    public async Task<IActionResult> MyReservations(DateOnly? date, string? tab)
    {
        var userId = CurrentUserId;
        var today = DateOnly.FromDateTime(DateTime.Today);

        var view = new MyReservationsViewModel
        {
            Tab = tab is "completed" or "cancelled" or "insights" ? tab : "upcoming",
            FilterDate = date,
            Year = (date ?? today).Year,
            Month = (date ?? today).Month
        };

        var query = _db.Reservations
            .Include(r => r.Court).ThenInclude(c => c!.Facility).ThenInclude(f => f!.Category)
            .Include(r => r.Payment)
            .Where(r => r.UserId == userId);

        if (date.HasValue)
            query = query.Where(r => r.ReservationDate == date.Value);

        var all = await query.OrderBy(r => r.ReservationDate).ThenBy(r => r.StartTime).ToListAsync();

        view.Upcoming = all
            .Where(r => r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed && r.ReservationDate >= today)
            .OrderBy(r => r.ReservationDate).ThenBy(r => r.StartTime).ToList();
        view.Completed = all
            .Where(r => r.Status == ReservationStatus.Completed)
            .OrderByDescending(r => r.ReservationDate).ThenByDescending(r => r.StartTime).ToList();
        view.Cancelled = all
            .Where(r => r.Status is ReservationStatus.Cancelled or ReservationStatus.Rejected)
            .OrderByDescending(r => r.CancelledAt ?? r.UpdatedAt).ToList();

        // Booking insights (Phase E): monthly bookings/spend, category split and
        // cancellation summary — computed from the member's full history.
        var (monthLabels, monthlyBookings) = ChartAggregations.MonthlyBookings(all, today, 6);
        view.InsightMonthLabels = monthLabels;
        view.InsightMonthlyBookings = monthlyBookings;

        var paidPayments = all
            .Where(r => r.Payment?.Status == PaymentStatus.Paid && r.Payment.PaidAt.HasValue)
            .Select(r => r.Payment!)
            .ToList();
        var (_, monthlySpend) = ChartAggregations.MonthlyRevenue(paidPayments, today, 6);
        view.InsightMonthlySpend = monthlySpend;

        var (categoryLabels, categoryCounts) = ChartAggregations.ByCategory(all);
        view.InsightCategoryLabels = categoryLabels;
        view.InsightCategoryCounts = categoryCounts;

        var (cancelled, completed, active, rate) = ChartAggregations.CancellationSummary(all);
        view.InsightCancelled = cancelled;
        view.InsightCompleted = completed;
        view.InsightActive = active;
        view.InsightCancellationRate = rate;

        // Interactive calendar: own reservations per day in the displayed month.
        var monthStart = new DateOnly(view.Year, view.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        view.ReservationsPerDay = await _db.Reservations
            .Where(r => r.UserId == userId && r.ReservationDate >= monthStart && r.ReservationDate < nextMonth)
            .GroupBy(r => r.ReservationDate.Day)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Day, x => x.Count);

        return View(view);
    }

    // Resource-level authorization: members can only open their own reservations.
    public async Task<IActionResult> Details(int id)
    {
        var reservation = await _db.Reservations
            .Include(r => r.Court).ThenInclude(c => c!.Facility)
            .Include(r => r.User)
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (reservation == null)
            return NotFound();

        if (reservation.UserId != CurrentUserId && !IsBackOffice)
            return Forbid();

        ViewBag.IsPrivileged = reservation.UserId == CurrentUserId || IsBackOffice;
        ViewBag.CanCancel = reservation.Status is ReservationStatus.Pending or ReservationStatus.Confirmed &&
            (IsBackOffice || reservation.ReservationDate.ToDateTime(reservation.StartTime) > DateTime.Now);
        ViewBag.CanPay = reservation.Status == ReservationStatus.Pending &&
            reservation.Payment != null && reservation.Payment.Status != PaymentStatus.Paid;

        // QR confirmation carries only the safe public reservation reference (never personal data).
        if (reservation.Status == ReservationStatus.Confirmed)
        {
            var verifyUrl = Url.Action(nameof(Verify), "Reservations",
                new { reference = reservation.ReservationReference }, Request.Scheme);
            ViewBag.QrDataUri = QrCodeHelper.GenerateDataUri(verifyUrl!);
        }

        return View(reservation);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var (success, error) = await _reservationService.CancelAsync(id, CurrentUserId, reason, IsBackOffice);

        if (!success)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["SuccessMessage"] = "Reservation cancelled successfully.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Pay(int id)
    {
        var reservation = await _db.Reservations
            .Include(r => r.Court)
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (reservation == null)
            return NotFound();

        if (reservation.UserId != CurrentUserId && !IsBackOffice)
            return Forbid();

        if (reservation.Status != ReservationStatus.Pending)
        {
            TempData["InfoMessage"] = "This reservation no longer requires payment.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(new ReservationPaymentViewModel { ReservationId = id, Reservation = reservation });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(ReservationPaymentViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var (success, error) = await _reservationService.MarkPaidAsync(
            model.ReservationId, CurrentUserId, model.Method, model.PaymentReference, IsBackOffice);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "Payment failed.");
            var reservation = await _db.Reservations
                .Include(r => r.Court)
                .Include(r => r.Payment)
                .FirstOrDefaultAsync(r => r.Id == model.ReservationId);
            model.Reservation = reservation;
            return View(model);
        }

        TempData["SuccessMessage"] = "Payment recorded. Your booking is confirmed.";
        return RedirectToAction(nameof(Details), new { id = model.ReservationId });
    }

    /// <summary>
    /// PDF e-receipt for a paid booking (M3). Owner or staff only; requires a
    /// Paid/Refunded payment record.
    /// </summary>
    public async Task<IActionResult> Receipt(int id)
    {
        var reservation = await _db.Reservations
            .Include(r => r.Court).ThenInclude(c => c!.Facility)
            .Include(r => r.User)
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (reservation == null)
            return NotFound();

        if (reservation.UserId != CurrentUserId && !IsBackOffice)
            return Forbid();

        if (reservation.Payment == null || reservation.Payment.Status is not (PaymentStatus.Paid or PaymentStatus.Refunded))
        {
            TempData["ErrorMessage"] = "A receipt is only available for paid bookings.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var pdf = ReceiptPdfGenerator.Generate(reservation, ReceiptFooter);
        return File(pdf, "application/pdf", $"Receipt-{reservation.ReservationReference}.pdf");
    }

    /// <summary>
    /// G-M5: emails the PDF e-receipt to the booking owner. Owner or staff only;
    /// requires a Paid/Refunded payment record. Idempotent — safe to click again.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmailReceipt(int id)
    {
        var reservation = await _db.Reservations
            .Include(r => r.Court).ThenInclude(c => c!.Facility)
            .Include(r => r.User)
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (reservation == null)
            return NotFound();

        if (reservation.UserId != CurrentUserId && !IsBackOffice)
            return Forbid();

        if (reservation.Payment == null || reservation.Payment.Status is not (PaymentStatus.Paid or PaymentStatus.Refunded))
        {
            TempData["ErrorMessage"] = "A receipt is only available for paid bookings.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var member = reservation.User;
        if (member == null || string.IsNullOrWhiteSpace(member.Email))
        {
            TempData["ErrorMessage"] = "This member has no email address on file.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var pdf = ReceiptPdfGenerator.Generate(reservation, ReceiptFooter);
        await _emails.SendAsync(member.Email,
            $"Your e-receipt — {reservation.ReservationReference}",
            EmailTemplates.ReceiptEmail(member.FullName, reservation.ReservationReference),
            new[] { new EmailAttachment($"Receipt-{reservation.ReservationReference}.pdf", pdf, "application/pdf") });

        TempData["SuccessMessage"] = $"Receipt emailed to {MaskEmail(member.Email)}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>G-M5: the site footer line for the PDF e-receipt (admin-editable).</summary>
    private string ReceiptFooter =>
        _db.SystemSettings.FirstOrDefault(s => s.Key == "ReceiptFooter")?.Value
        ?? "SportHub · 12 Jalan Ampang, 50450 Kuala Lumpur · 03-4142 8899 · info@sporthub.my";

    /// <summary>Shows only the first characters of an email address in flash messages.</summary>
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return email;
        return email[..2] + new string('•', Math.Min(6, at - 2)) + email[at..];
    }

    /// <summary>
    /// QR scan landing page. Anonymous scans only ever see the reference, status,
    /// court and time — no personal data unless the viewer is the owner or staff.
    /// </summary>
    [AllowAnonymous]
    public async Task<IActionResult> Verify(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return BadRequest();

        var reservation = await _db.Reservations
            .Include(r => r.Court).ThenInclude(c => c!.Facility)
            .Include(r => r.User)
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.ReservationReference == reference);

        if (reservation == null)
            return NotFound();

        var isPrivileged = User.Identity?.IsAuthenticated == true &&
            (reservation.UserId == CurrentUserId || IsBackOffice);
        ViewBag.IsPrivileged = isPrivileged;

        return View(reservation);
    }
}
