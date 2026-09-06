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
/// Reservation administration (M4): AJAX search/filter/sort/pagination,
/// status management, staff payment recording and cancellations.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminReservationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IReservationService _reservationService;
    private readonly IEmailService _emails;

    public AdminReservationsController(ApplicationDbContext db, IReservationService reservationService,
        IEmailService emails)
    {
        _db = db;
        _reservationService = reservationService;
        _emails = emails;
    }

    public async Task<IActionResult> Index(string? search, string? status, int? courtId, DateOnly? date, string? sort, int page = 1)
    {
        var model = await BuildModelAsync(search, status, courtId, date, sort, page);
        return View(model);
    }

    /// <summary>AJAX endpoint returning the filtered table partial only.</summary>
    [HttpGet]
    public async Task<IActionResult> Table(string? search, string? status, int? courtId, DateOnly? date, string? sort, int page = 1)
    {
        var model = await BuildModelAsync(search, status, courtId, date, sort, page);
        return PartialView("_Table", model);
    }

    private async Task<AdminReservationsIndexViewModel> BuildModelAsync(
        string? search, string? status, int? courtId, DateOnly? date, string? sort, int page)
    {
        page = Math.Max(1, page);
        var query = _db.Reservations
            .Include(r => r.Court)
            .Include(r => r.User)
            .Include(r => r.Payment)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.ReservationReference.Contains(term) ||
                                     r.User!.FullName.Contains(term) ||
                                     r.User.Email.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReservationStatus>(status, true, out var parsedStatus))
            query = query.Where(r => r.Status == parsedStatus);

        if (courtId.HasValue)
            query = query.Where(r => r.CourtId == courtId.Value);

        if (date.HasValue)
            query = query.Where(r => r.ReservationDate == date.Value);

        query = (sort ?? "date_desc") switch
        {
            "date_asc" => query.OrderBy(r => r.ReservationDate).ThenBy(r => r.StartTime),
            "amount_desc" => query.OrderByDescending(r => r.TotalAmount),
            "created_desc" => query.OrderByDescending(r => r.CreatedAt),
            _ => query.OrderByDescending(r => r.ReservationDate).ThenByDescending(r => r.StartTime)
        };

        var total = await query.CountAsync();
        var rows = await query
            .Skip((page - 1) * AdminReservationsIndexViewModel.PageSize)
            .Take(AdminReservationsIndexViewModel.PageSize)
            .ToListAsync();

        return new AdminReservationsIndexViewModel
        {
            Reservations = rows,
            Search = search,
            Status = status,
            CourtId = courtId,
            Date = date,
            Sort = sort ?? "date_desc",
            Page = page,
            TotalCount = total,
            Courts = await _db.Courts.OrderBy(c => c.CourtNumber).ToListAsync()
        };
    }

    /// <summary>Valid status transitions only; anything else is rejected.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status, string? returnUrl)
    {
        var reservation = await _db.Reservations
            .Include(r => r.User)
            .Include(r => r.Payment)
            .Include(r => r.Court).ThenInclude(c => c!.Facility)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (reservation == null)
        {
            TempData["ErrorMessage"] = "Reservation not found.";
            return RedirectToLocal(returnUrl);
        }

        if (!Enum.TryParse<ReservationStatus>(status, true, out var newStatus))
        {
            TempData["ErrorMessage"] = "Invalid status.";
            return RedirectToLocal(returnUrl);
        }

        var allowed = (reservation.Status, newStatus) switch
        {
            (ReservationStatus.Pending, ReservationStatus.Confirmed) => true,
            (ReservationStatus.Pending, ReservationStatus.Rejected) => true,
            (ReservationStatus.Confirmed, ReservationStatus.Completed) => true,
            _ => false
        };

        if (!allowed)
        {
            TempData["ErrorMessage"] = $"Changing a reservation from {reservation.Status} to {newStatus} is not allowed.";
            return RedirectToLocal(returnUrl);
        }

        reservation.Status = newStatus;
        reservation.UpdatedAt = DateTime.Now;

        // Business rule: rejecting a booking fails its pending payment record.
        if (newStatus == ReservationStatus.Rejected && reservation.Payment != null &&
            reservation.Payment.Status == PaymentStatus.Pending)
        {
            reservation.Payment.Status = PaymentStatus.Failed;
        }

        // G-M3: a rejected booking releases its hourly slot claims.
        if (newStatus == ReservationStatus.Rejected)
            await BookingRules.ReleaseWindowAsync(_db, reservation.Id);

        _db.Notifications.Add(new Notification
        {
            UserId = reservation.UserId,
            Title = "Reservation updated",
            Message = $"Booking {reservation.ReservationReference} is now {newStatus}.",
            Type = NotificationType.Reservation,
            TargetUrl = $"/Reservations/Details/{reservation.Id}"
        });

        await _db.SaveChangesAsync();

        // G-M5: lifecycle email to the member (demo sender when no SMTP is configured).
        var member = reservation.User;
        if (member != null && !string.IsNullOrWhiteSpace(member.Email))
        {
            var when = $"{reservation.ReservationDate:dd MMM yyyy}, {reservation.StartTime:HH:mm}–{reservation.EndTime:HH:mm}";
            var court = $"Court {reservation.Court?.CourtNumber}";
            switch (newStatus)
            {
                case ReservationStatus.Confirmed:
                    await _emails.SendAsync(member.Email,
                        $"Booking confirmed — {reservation.ReservationReference}",
                        EmailTemplates.BookingConfirmedEmail(member.FullName, reservation.ReservationReference,
                            court, when));
                    break;
                case ReservationStatus.Rejected:
                    await _emails.SendAsync(member.Email,
                        $"Booking not accepted — {reservation.ReservationReference}",
                        EmailTemplates.BookingRejectedEmail(member.FullName, reservation.ReservationReference));
                    break;
                case ReservationStatus.Completed:
                    await _emails.SendAsync(member.Email,
                        $"Booking completed — {reservation.ReservationReference}",
                        EmailTemplates.BookingCompletedEmail(member.FullName, reservation.ReservationReference));
                    break;
            }
        }

        TempData["SuccessMessage"] = $"{reservation.ReservationReference} updated to {newStatus}.";
        return RedirectToLocal(returnUrl);
    }

    /// <summary>Admin records a payment received at the counter; the booking is confirmed.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPaid(int id, PaymentMethod method, string? reference, string? returnUrl)
    {
        var backOfficeUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var (success, error) = await _reservationService.MarkPaidAsync(id, backOfficeUserId, method, reference, isBackOffice: true);

        TempData[success ? "SuccessMessage" : "ErrorMessage"] =
            success ? "Payment recorded. The booking is confirmed." : (error ?? "Could not record the payment.");
        return RedirectToLocal(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason, string? returnUrl)
    {
        var backOfficeUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var (success, error) = await _reservationService.CancelAsync(id, backOfficeUserId, reason, isBackOffice: true);

        TempData[success ? "SuccessMessage" : "ErrorMessage"] =
            success ? "Reservation cancelled." : (error ?? "Could not cancel the reservation.");
        return RedirectToLocal(returnUrl);
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }
}
