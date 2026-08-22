using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Services;

/// <summary>
/// Central availability logic shared by the public booking flow, the court details
/// page (AJAX) and the staff availability management pages.
/// </summary>
public class CourtService : ICourtService
{
    private readonly ApplicationDbContext _db;

    public CourtService(ApplicationDbContext db) => _db = db;

    public async Task<List<SlotStatusViewModel>> GetAvailabilityForDateAsync(DateOnly date, int? courtId = null)
    {
        var courtsQuery = _db.Courts.AsQueryable();
        if (courtId.HasValue) courtsQuery = courtsQuery.Where(c => c.Id == courtId);
        var courts = await courtsQuery.OrderBy(c => c.CourtNumber).ToListAsync();
        if (courts.Count == 0) return new List<SlotStatusViewModel>();

        var facility = await _db.Facilities.FirstAsync();
        var availabilities = await _db.CourtAvailabilities
            .Where(a => a.Date == date && (courtId == null || a.CourtId == courtId))
            .ToListAsync();

        var reservations = await _db.Reservations
            .Where(r => r.ReservationDate == date &&
                        (courtId == null || r.CourtId == courtId) &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .Select(r => new { r.CourtId, r.StartTime, r.EndTime })
            .ToListAsync();

        var slots = new List<SlotStatusViewModel>();
        var openingHour = facility.OpeningTime.Hours;
        var closingHour = facility.ClosingTime.Hours;

        foreach (var court in courts)
        {
            for (var hour = openingHour; hour < closingHour; hour++)
            {
                var start = new TimeOnly(hour, 0);
                var end = new TimeOnly(hour + 1, 0);
                var availability = availabilities.FirstOrDefault(a => a.CourtId == court.Id && a.StartTime == start);

                string status;
                string label;

                if (date < DateOnly.FromDateTime(DateTime.Today))
                {
                    status = "past"; label = "Past";
                }
                else if (court.Status == CourtStatus.Maintenance)
                {
                    status = "maintenance"; label = "Maintenance";
                }
                else if (court.Status == CourtStatus.Unavailable)
                {
                    status = "unavailable"; label = "Unavailable";
                }
                else if (availability == null)
                {
                    status = "closed"; label = "Not Available";
                }
                else if (availability.Status == AvailabilityStatus.Maintenance)
                {
                    status = "maintenance"; label = "Maintenance";
                }
                else if (availability.Status == AvailabilityStatus.Blocked)
                {
                    status = "blocked"; label = "Blocked";
                }
                else if (reservations.Any(r => r.CourtId == court.Id && r.StartTime < end && r.EndTime > start))
                {
                    status = "booked"; label = "Booked";
                }
                else
                {
                    status = "open"; label = "Open";
                }

                slots.Add(new SlotStatusViewModel
                {
                    CourtId = court.Id,
                    CourtNumber = court.CourtNumber,
                    CourtType = court.CourtType.ToString(),
                    HourlyRate = court.HourlyRate,
                    StartTime = start.ToString("HH:mm"),
                    EndTime = end.ToString("HH:mm"),
                    Status = status,
                    Label = label
                });
            }
        }

        return slots;
    }

    public async Task<bool> IsWindowWithinOpenSlotsAsync(int courtId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var facility = await _db.Facilities.FirstAsync();

        // Within facility operating hours?
        if (start.ToTimeSpan() < facility.OpeningTime || end.ToTimeSpan() > facility.ClosingTime)
            return false;

        // Within operating days? (OperatingDays stores abbreviations like "Mon")
        var dayAbbreviation = date.ToString("ddd");
        if (!facility.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Contains(dayAbbreviation, StringComparer.OrdinalIgnoreCase))
            return false;

        var slots = await _db.CourtAvailabilities
            .Where(a => a.CourtId == courtId && a.Date == date && a.Status == AvailabilityStatus.Open)
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        var cursor = start;
        while (cursor < end)
        {
            var next = slots.FirstOrDefault(a => a.StartTime <= cursor && a.EndTime > cursor);
            if (next == null) return false;
            cursor = next.EndTime;
        }
        return true;
    }

    public async Task<bool> HasOverlappingReservationAsync(int courtId, DateOnly date, TimeOnly start, TimeOnly end, int? excludeReservationId = null)
    {
        var query = _db.Reservations.Where(r =>
            r.CourtId == courtId &&
            r.ReservationDate == date &&
            (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
            r.StartTime < end && r.EndTime > start);

        if (excludeReservationId.HasValue)
            query = query.Where(r => r.Id != excludeReservationId.Value);

        return await query.AnyAsync();
    }
}
