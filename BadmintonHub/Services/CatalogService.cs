using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Services;

/// <summary>
/// Public facility catalog (revised spec): category/name filters, top-5 popularity
/// ranking over the last 30 days of confirmed/completed bookings, and a same-day
/// 19:00 low-availability signal per facility.
/// </summary>
public class CatalogService : ICatalogService
{
    private readonly ApplicationDbContext _db;

    public CatalogService(ApplicationDbContext db) => _db = db;

    public async Task<FacilityCatalogViewModel> GetCatalogAsync(int? categoryId, string? search)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var windowStart = today.AddDays(-29);
        var evening = new TimeOnly(19, 0);
        var eveningEnd = new TimeOnly(20, 0);

        var term = search?.Trim().ToLowerInvariant();
        var facilities = await _db.Facilities
            .Include(f => f.Category)
            .Include(f => f.Photos.OrderBy(p => p.DisplayOrder))
            .Include(f => f.Courts)
            .Where(f => f.Status == FacilityStatus.Open)
            .Where(f => !categoryId.HasValue || f.CategoryId == categoryId)
            .Where(f => string.IsNullOrWhiteSpace(term) || f.Name.ToLower().Contains(term))
            .ToListAsync();

        // Popularity: confirmed + completed bookings per facility over the last 30 days.
        var bookingCounts = await _db.Reservations
            .Where(r => r.ReservationDate >= windowStart &&
                        (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Completed))
            .GroupBy(r => r.Court!.FacilityId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // Tonight's 19:00 signal: open slots minus active bookings, per facility.
        var openAt1900 = await _db.CourtAvailabilities
            .Where(a => a.Date == today && a.StartTime == evening && a.Status == AvailabilityStatus.Open &&
                        a.Court!.Status == CourtStatus.Available)
            .GroupBy(a => a.Court!.FacilityId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var bookedAt1900 = await _db.Reservations
            .Where(r => r.ReservationDate == today &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
                        r.StartTime < eveningEnd && r.EndTime > evening)
            .GroupBy(r => r.Court!.FacilityId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // Rank 1–5 for the five facilities with the most bookings in the window.
        var topFive = bookingCounts
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select((kv, index) => new { kv.Key, Rank = index + 1 })
            .ToDictionary(x => x.Key, x => x.Rank);

        var cards = facilities
            .Select(f => new FacilityCardViewModel
            {
                Facility = f,
                PrimaryPhotoPath = f.Photos
                    .FirstOrDefault(p => p.IsPrimary)?.FilePath
                    ?? f.Photos.FirstOrDefault()?.FilePath,
                UnitCount = f.Courts.Count,
                MinHourlyRate = f.Courts.Count == 0 ? 0 : f.Courts.Min(c => c.HourlyRate),
                PopularityRank = topFive.TryGetValue(f.Id, out var rank) ? rank : null,
                AvailableAt1900 = Math.Max(0,
                    openAt1900.GetValueOrDefault(f.Id) - bookedAt1900.GetValueOrDefault(f.Id))
            })
            .OrderBy(c => c.Facility.Category!.DisplayOrder)
            .ThenBy(c => c.Facility.Name)
            .ToList();

        return new FacilityCatalogViewModel
        {
            Categories = await _db.Categories
                .Where(c => c.Status == CategoryStatus.Active)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync(),
            CategoryId = categoryId,
            Search = search,
            Cards = cards
        };
    }
}
