using SportHub.ViewModels;

namespace SportHub.Services;

public interface ICourtService
{
    /// <summary>
    /// Returns the status of every hourly slot of every court (or one court / one facility)
    /// for a date, combining availability records, reservations and court/facility status.
    /// </summary>
    Task<List<SlotStatusViewModel>> GetAvailabilityForDateAsync(DateOnly date, int? courtId = null, int? facilityId = null);

    /// <summary>True when every requested hour falls inside an Open availability slot for the court/date.</summary>
    Task<bool> IsWindowWithinOpenSlotsAsync(int courtId, DateOnly date, TimeOnly start, TimeOnly end);

    /// <summary>True when the court has no overlapping active reservation for the window.</summary>
    Task<bool> HasOverlappingReservationAsync(int courtId, DateOnly date, TimeOnly start, TimeOnly end, int? excludeReservationId = null);
}
