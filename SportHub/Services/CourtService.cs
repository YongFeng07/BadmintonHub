using SportHub.Data;
using SportHub.Models;
using SportHub.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace SportHub.Services;

/// <summary>
/// Central availability logic shared by the public booking flow, the court details
/// page (AJAX) and the staff availability management pages.
/// </summary>
public class CourtService : ICourtService
{
    private readonly ApplicationDbContext _db;

    public CourtService(ApplicationDbContext db) => _db = db;

    public async Task<List<SlotStatusViewModel>> GetAvailabilityForDateAsync(
        DateOnly date, int? courtId = null, int? facilityId = null)
    {
        var courtsQuery = _db.Courts.Include(c => c.Facility).AsQueryable();
        if (courtId.HasValue) courtsQuery = courtsQuery.Where(c => c.Id == courtId);
        if (facilityId.HasValue) courtsQuery = courtsQuery.Where(c => c.FacilityId == facilityId);
        var courts = await courtsQuery.OrderBy(c => c.CourtNumber).ToListAsync();
        if (courts.Count == 0) return new List<SlotStatusViewModel>();
        var availabilities = await _db.CourtAvailabilities
            .Where(a => a.Date == date && (courtId == null || a.CourtId == courtId))
            .ToListAsync();

        var reservations = await _db.Reservations
            .Where(r => r.ReservationDate == date &&
                        (courtId == null || r.CourtId == courtId) &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .Select(r => new { r.CourtId, r.StartTime, r.EndTime })
            .ToListAsync();

        // G-M3: active cart holds (any member) show as "Held" so nobody is surprised
        // when the slot is not bookable for the next few minutes.
        var holds = await _db.CartItems
            .Where(i => i.Date == date &&
                        (courtId == null || i.CourtId == courtId) &&
                        i.HeldUntil != null && i.HeldUntil > DateTime.Now)
            .Select(i => new { i.CourtId, i.StartTime, i.DurationHours })
            .ToListAsync();

        var slots = new List<SlotStatusViewModel>();

        foreach (var court in courts)
        {
            // Opening hours belong to the court's facility, not a global first facility.
            var facility = court.Facility!;
            var openingHour = facility.OpeningTime.Hours;
            var closingHour = facility.ClosingTime.Hours;

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
                else if (holds.Any(h => h.CourtId == court.Id &&
                                        h.StartTime < end && h.StartTime.AddHours(h.DurationHours) > start))
                {
                    status = "held"; label = "Held";
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

    /// <summary>
    /// G-M4: true bookability over the next <paramref name="days"/> days — the first
    /// date with at least one open slot and the total open-slot count. Held slots are
    /// not open (they are someone's 15-minute claim), so the summary reflects what a
    /// member can actually book right now. Used by the wishlist badge and the
    /// notify-when-available worker.
    /// </summary>
    public async Task<(DateOnly? FirstOpenDate, int OpenSlotCount)> GetUpcomingOpenSlotsAsync(int courtId, int days = 3)
    {
        DateOnly? firstOpenDate = null;
        var openSlotCount = 0;
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var d = 0; d < days; d++)
        {
            var date = today.AddDays(d);
            var slots = await GetAvailabilityForDateAsync(date, courtId);
            var open = slots.Count(s => s.Status == "open");
            if (open > 0)
            {
                firstOpenDate ??= date;
                openSlotCount += open;
            }
        }

        return (firstOpenDate, openSlotCount);
    }

    public async Task<bool> IsWindowWithinOpenSlotsAsync(int courtId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        // Facility rules come from the court's own facility (multi-facility).
        var court = await _db.Courts.Include(c => c.Facility).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court == null) return false;
        var facility = court.Facility!;

        // Within facility operating hours?
        if (start.ToTimeSpan() < facility.OpeningTime || end.ToTimeSpan() > facility.ClosingTime)
            return false;

        // Within operating days? (OperatingDays stores abbreviations like "Mon").
        // InvariantCulture: the seeded abbreviations are English; the server culture may not be.
        var dayAbbreviation = date.ToString("ddd", CultureInfo.InvariantCulture);
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
