using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace BadmintonHub.Controllers;

/// <summary>
/// Business reports (M4): revenue, court utilisation, popular courts and peak hours
/// over a selectable date range, with charts and CSV export.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminReportsController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminReportsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to)
    {
        var toDate = to ?? DateOnly.FromDateTime(DateTime.Today);
        var fromDate = from ?? toDate.AddDays(-13);
        if (fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        var model = await BuildReportAsync(fromDate, toDate);
        return View(model);
    }

    /// <summary>CSV export of the daily revenue breakdown for the selected range.</summary>
    public async Task<IActionResult> ExportCsv(DateOnly from, DateOnly to)
    {
        if (from > to) (from, to) = (to, from);

        var rows = await _db.Payments
            .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt >= from.ToDateTime(TimeOnly.MinValue) && p.PaidAt < to.AddDays(1).ToDateTime(TimeOnly.MinValue))
            .GroupBy(p => p.PaidAt!.Value.Date)
            .Select(g => new { Day = g.Key, Revenue = g.Sum(p => p.Amount), Bookings = g.Count() })
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Date,Revenue (RM),Bookings");
        foreach (var row in rows.OrderBy(r => r.Day))
            sb.AppendLine($"{row.Day:yyyy-MM-dd},{row.Revenue.ToString("0.00", CultureInfo.InvariantCulture)},{row.Bookings}");

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
            $"revenue-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    private async Task<AdminReportsViewModel> BuildReportAsync(DateOnly fromDate, DateOnly toDate)
    {
        var fromStart = fromDate.ToDateTime(TimeOnly.MinValue);
        var toEnd = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var payments = await _db.Payments
            .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt >= fromStart && p.PaidAt < toEnd)
            .ToListAsync();

        var reservations = await _db.Reservations
            .Include(r => r.Court)
            .Include(r => r.User)
            .Include(r => r.Payment)
            .Where(r => r.ReservationDate >= fromDate && r.ReservationDate <= toDate)
            .ToListAsync();

        var model = new AdminReportsViewModel
        {
            FromDate = fromDate,
            ToDate = toDate,
            Revenue = payments.Sum(p => p.Amount),
            BookingCount = reservations.Count,
            AverageBookingValue = reservations.Count > 0 ? Math.Round(reservations.Sum(r => r.TotalAmount) / reservations.Count, 2) : 0m,
            CancellationCount = reservations.Count(r => r.Status == ReservationStatus.Cancelled)
        };

        // Daily revenue series.
        var revenueByDay = payments
            .GroupBy(p => p.PaidAt!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        for (var day = fromDate; day <= toDate; day = day.AddDays(1))
        {
            model.RevenueByDayLabels.Add(day.ToString("dd MMM"));
            model.RevenueByDayData.Add(revenueByDay.TryGetValue(day.ToDateTime(TimeOnly.MinValue), out var v) ? v : 0m);
        }

        // Court utilisation: booked hours / open hours in the range.
        var facility = await _db.Facilities.FirstAsync();
        var openHoursPerDay = facility.ClosingTime.Hours - facility.OpeningTime.Hours;
        var daysInRange = toDate.DayNumber - fromDate.DayNumber + 1;
        var courts = await _db.Courts.OrderBy(c => c.CourtNumber).ToListAsync();
        var bookedByCourt = reservations
            .Where(r => r.Status is ReservationStatus.Confirmed or ReservationStatus.Completed)
            .GroupBy(r => r.CourtId)
            .ToDictionary(g => g.Key, g => g.Sum(r => (double)r.DurationHours));
        foreach (var court in courts)
        {
            model.UtilizationCourtLabels.Add($"C{court.CourtNumber}");
            var booked = bookedByCourt.TryGetValue(court.Id, out var hours) ? hours : 0d;
            var available = openHoursPerDay * daysInRange;
            model.UtilizationCourtData.Add(available > 0 ? Math.Round(booked / available * 100d, 1) : 0d);
        }

        // Popular courts: booking count per court.
        var countByCourt = reservations
            .GroupBy(r => r.CourtId)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var court in courts)
        {
            model.PopularCourtLabels.Add($"C{court.CourtNumber}");
            model.PopularCourtData.Add(countByCourt.TryGetValue(court.Id, out var c) ? c : 0);
        }

        // Peak hours: bookings per starting hour.
        var countByHour = reservations
            .GroupBy(r => r.StartTime.Hour)
            .ToDictionary(g => g.Key, g => g.Count());
        for (var h = facility.OpeningTime.Hours; h < facility.ClosingTime.Hours; h++)
        {
            model.PeakHourLabels.Add($"{h:00}:00");
            model.PeakHourData.Add(countByHour.TryGetValue(h, out var c) ? c : 0);
        }

        model.RevenueRows = reservations
            .Where(r => r.Payment?.Status == PaymentStatus.Paid)
            .OrderByDescending(r => r.Payment!.PaidAt)
            .Take(20)
            .ToList();

        return model;
    }
}
