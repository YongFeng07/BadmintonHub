using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

/// <summary>One facility card on the public catalog.</summary>
public class FacilityCardViewModel
{
    public Facility Facility { get; set; } = null!;

    /// <summary>Cover photo path (first primary photo, else first photo).</summary>
    public string? PrimaryPhotoPath { get; set; }

    public int UnitCount { get; set; }

    public decimal MinHourlyRate { get; set; }

    /// <summary>1–5 for the top five most-booked facilities in the last 30 days; null otherwise.</summary>
    public int? PopularityRank { get; set; }

    /// <summary>
    /// Units still open at the facility's 19:00 slot today (open slots minus active
    /// bookings). A value of 2 or less drives the "low availability" alert badge.
    /// </summary>
    public int AvailableAt1900 { get; set; }
}

/// <summary>The whole catalog page: filters plus the ranked/alerted cards.</summary>
public class FacilityCatalogViewModel
{
    public List<Category> Categories { get; set; } = new();

    public int? CategoryId { get; set; }

    public string? Search { get; set; }

    public List<FacilityCardViewModel> Cards { get; set; } = new();
}
