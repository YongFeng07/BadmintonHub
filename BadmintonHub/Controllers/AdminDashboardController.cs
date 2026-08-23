using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

/// <summary>Staff operational dashboard with real-data KPIs and charts (M4).</summary>
[Authorize(Roles = "Admin,Staff")]
public class AdminDashboardController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminDashboardController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var revenue = await _db.Payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var todayReservations = await _db.Reservations
            .CountAsync(r => r.ReservationDate == today &&
                             (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed));

        var upcomingReservations = await _db.Reservations
            .CountAsync(r => r.ReservationDate >= today &&
                             (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed));

        var totalMembers = await _db.Users.CountAsync(u => u.Role == Role.Member);
        var activeCourts = await _db.Courts.CountAsync(c => c.Status == CourtStatus.Available);

        // Today's utilisation: booked hours / open hours across all courts.
        var facility = await _db.Facilities.FirstAsync();
        var openHoursPerCourt = facility.ClosingTime.Hours - facility.OpeningTime.Hours;
        var openSlots = await _db.CourtAvailabilities
            .CountAsync(a => a.Date == today && a.Status == AvailabilityStatus.Open);
        var bookedSlots = await _db.Reservations
            .Where(r => r.ReservationDate == today &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .SumAsync(r => (double?)r.DurationHours) ?? 0d;
        var utilization = openSlots > 0
            ? Math.Round(bookedSlots / openSlots * 100d, 1)
            : 0d;

        // Revenue per day, last 7 days.
        var sevenDaysAgo = DateTime.Today.AddDays(-6);
        var revenueRows = await _db.Payments
            .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt >= sevenDaysAgo)
            .GroupBy(p => p.PaidAt!.Value.Date)
            .Select(g => new { Day = g.Key, Total = g.Sum(p => p.Amount) })
            .ToListAsync();
        var revenueLabels = new List<string>();
        var revenueData = new List<decimal>();
        for (var i = 6; i >= 0; i--)
        {
            var day = DateTime.Today.AddDays(-i);
            revenueLabels.Add(day.ToString("dd MMM"));
            revenueData.Add(revenueRows.FirstOrDefault(r => r.Day == day)?.Total ?? 0m);
        }

        // Reservations per court and per starting hour (last 30 days).
        var monthAgo = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var perCourt = await _db.Reservations
            .Where(r => r.ReservationDate >= monthAgo)
            .GroupBy(r => r.CourtId)
            .Select(g => new { CourtId = g.Key, Count = g.Count() })
            .ToListAsync();
        var courts = await _db.Courts.OrderBy(c => c.CourtNumber).ToListAsync();
        var courtLabels = courts.Select(c => $"C{c.CourtNumber}").ToList();
        var courtData = courts.Select(c => perCourt.FirstOrDefault(x => x.CourtId == c.Id)?.Count ?? 0).ToList();

        var perHour = await _db.Reservations
            .Where(r => r.ReservationDate >= monthAgo)
            .GroupBy(r => r.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync();
        var peakHourLabels = new List<string>();
        var peakHourData = new List<int>();
        for (var h = facility.OpeningTime.Hours; h < facility.ClosingTime.Hours; h++)
        {
            peakHourLabels.Add($"{h:00}:00");
            peakHourData.Add(perHour.FirstOrDefault(x => x.Hour == h)?.Count ?? 0);
        }

        var recent = await _db.Reservations
            .Include(r => r.Court)
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .Take(8)
            .ToListAsync();

        return View(new AdminDashboardViewModel
        {
            TotalRevenue = revenue,
            TodayReservations = todayReservations,
            UpcomingReservations = upcomingReservations,
            TotalMembers = totalMembers,
            ActiveCourts = activeCourts,
            TodayUtilizationPercent = utilization,
            RevenueLabels = revenueLabels,
            RevenueData = revenueData,
            CourtLabels = courtLabels,
            CourtData = courtData,
            PeakHourLabels = peakHourLabels,
            PeakHourData = peakHourData,
            RecentReservations = recent
        });
    }
}
