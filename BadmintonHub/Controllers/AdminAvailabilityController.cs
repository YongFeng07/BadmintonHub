using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

/// <summary>
/// Admin/Staff availability management: interactive slot grid (AJAX), bulk slot
/// generation for a date range, and maintenance/block ranges with booking protection.
/// </summary>
[Authorize(Roles = "Admin,Staff")]
public class AdminAvailabilityController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICourtService _courtService;

    public AdminAvailabilityController(ApplicationDbContext db, ICourtService courtService)
    {
        _db = db;
        _courtService = courtService;
    }

    public async Task<IActionResult> Index(DateOnly? date)
    {
        var selectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var facility = await _db.Facilities.FirstAsync();
        var courts = await _db.Courts.OrderBy(c => c.CourtNumber).ToListAsync();
        var slots = await _courtService.GetAvailabilityForDateAsync(selectedDate);

        var vm = new AvailabilityIndexViewModel
        {
            Date = selectedDate,
            Facility = facility,
            Courts = courts,
            Slots = slots
        };
        return View(vm);
    }

    /// <summary>AJAX: set one slot's status (cycles Open → Maintenance → Blocked → Open).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSlot(int courtId, DateOnly date, TimeOnly startTime, AvailabilityStatus status)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (date < today)
            return Json(new { success = false, message = "Past dates cannot be changed." });

        var court = await _db.Courts.FindAsync(courtId);
        if (court == null)
            return Json(new { success = false, message = "Court not found." });

        var facility = await _db.Facilities.FirstAsync();
        if (startTime.ToTimeSpan() < facility.OpeningTime ||
            startTime.ToTimeSpan() >= facility.ClosingTime)
            return Json(new { success = false, message = "Time is outside facility opening hours." });

        // Protect bookings: a slot that has an active reservation cannot be closed/blocked.
        var endTime = startTime.AddHours(1);
        if (status != AvailabilityStatus.Open &&
            await _db.Reservations.AnyAsync(r =>
                r.CourtId == courtId && r.ReservationDate == date &&
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
                r.StartTime < endTime && r.EndTime > startTime))
            return Json(new { success = false, message = "Cannot change this slot: it has an active booking." });

        var slot = await _db.CourtAvailabilities.FirstOrDefaultAsync(a =>
            a.CourtId == courtId && a.Date == date && a.StartTime == startTime);

        if (slot == null)
        {
            _db.CourtAvailabilities.Add(new CourtAvailability
            {
                CourtId = courtId,
                Date = date,
                StartTime = startTime,
                EndTime = endTime,
                Status = status
            });
        }
        else
        {
            slot.Status = status;
            slot.UpdatedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, status = status.ToString().ToLowerInvariant(), label = status.ToString() });
    }

    /// <summary>Bulk-set all existing slots of a court for a date range (e.g. maintenance period).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRange(int courtId, DateOnly fromDate, DateOnly toDate, AvailabilityStatus status)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (toDate < fromDate || fromDate < today)
            return Json(new { success = false, message = "Invalid date range." });

        var court = await _db.Courts.FindAsync(courtId);
        if (court == null)
            return Json(new { success = false, message = "Court not found." });

        // Slots inside the range that already have bookings are protected.
        var bookedWindows = await _db.Reservations
            .Where(r => r.CourtId == courtId &&
                        r.ReservationDate >= fromDate && r.ReservationDate <= toDate &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .Select(r => new { r.ReservationDate, r.StartTime, r.EndTime })
            .ToListAsync();

        var slots = await _db.CourtAvailabilities
            .Where(a => a.CourtId == courtId && a.Date >= fromDate && a.Date <= toDate)
            .ToListAsync();

        var protectedCount = 0;
        foreach (var slot in slots)
        {
            var isBooked = bookedWindows.Any(b =>
                b.ReservationDate == slot.Date && b.StartTime < slot.EndTime && b.EndTime > slot.StartTime);
            if (isBooked)
            {
                protectedCount++;
                continue;
            }
            slot.Status = status;
            slot.UpdatedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return Json(new
        {
            success = true,
            message = $"Updated {slots.Count - protectedCount} slot(s) to {status}. {protectedCount} booked slot(s) were left unchanged."
        });
    }

    // ---------- Generate slots for a date range ----------

    [HttpGet]
    public async Task<IActionResult> Generate()
    {
        ViewBag.Courts = await _db.Courts.OrderBy(c => c.CourtNumber).ToListAsync();
        return View(new AvailabilityGenerateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(AvailabilityGenerateViewModel vm)
    {
        ViewBag.Courts = await _db.Courts.OrderBy(c => c.CourtNumber).ToListAsync();
        if (!ModelState.IsValid)
            return View(vm);

        var facility = await _db.Facilities.FirstAsync();
        var operatingDays = facility.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim()).ToList();

        var created = 0;
        foreach (var courtId in vm.CourtIds.Distinct())
        {
            var existing = await _db.CourtAvailabilities
                .Where(a => a.CourtId == courtId && a.Date >= vm.FromDate && a.Date <= vm.ToDate)
                .Select(a => new { a.Date, a.StartTime })
                .ToListAsync();
            var existingKeys = existing.Select(e => (e.Date, e.StartTime)).ToHashSet();

            for (var date = vm.FromDate; date <= vm.ToDate; date = date.AddDays(1))
            {
                // Skip non-operating days (consistent with facility settings).
                if (!operatingDays.Contains(date.ToString("ddd"), StringComparer.OrdinalIgnoreCase))
                    continue;

                for (var hour = facility.OpeningTime.Hours; hour < facility.ClosingTime.Hours; hour++)
                {
                    var start = new TimeOnly(hour, 0);
                    if (existingKeys.Contains((date, start)))
                        continue;

                    _db.CourtAvailabilities.Add(new CourtAvailability
                    {
                        CourtId = courtId,
                        Date = date,
                        StartTime = start,
                        EndTime = start.AddHours(1),
                        Status = vm.Status
                    });
                    created++;
                }
            }
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"{created} availability slot(s) created.";
        return RedirectToAction(nameof(Index), new { date = vm.FromDate.ToString("yyyy-MM-dd") });
    }
}
