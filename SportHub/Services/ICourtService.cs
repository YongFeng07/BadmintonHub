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

    /// <summary>
    /// G-M4: first date with at least one open slot and the total open-slot count
    /// over the next <paramref name="days"/> days (held slots count as not open).
    /// </summary>
    Task<(DateOnly? FirstOpenDate, int OpenSlotCount)> GetUpcomingOpenSlotsAsync(int courtId, int days = 3);
}
