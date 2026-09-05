using BadmintonHub.ViewModels;

namespace BadmintonHub.Services;

public interface ICatalogService
{
    /// <summary>
    /// Builds the public facility catalog: open facilities (optionally filtered by
    /// category or name search) with cover photo, unit count, minimum hourly rate,
    /// top-5 popularity ranking (confirmed/completed bookings in the last 30 days)
    /// and tonight's 19:00 low-availability count.
    /// </summary>
    Task<FacilityCatalogViewModel> GetCatalogAsync(int? categoryId, string? search);
}
