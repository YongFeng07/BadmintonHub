using BadmintonHub.Data;
using BadmintonHub.Models;

namespace BadmintonHub.Services;

/// <summary>
/// Shared booking-rule checks used by the cart and checkout paths. Mirrors the rules
/// applied by ReservationService.CreateAsync so every entry point to a booking
/// (direct booking, cart add, checkout) enforces the same constraints.
/// </summary>
public static class BookingRules
{
    /// <summary>
    /// Validates a candidate window end-to-end. <paramref name="windowsToAvoid"/> are
    /// windows the candidate must not overlap — other cart lines of the same user, or
    /// reservations already created in the current checkout batch.
    /// </summary>
    public static async Task<string?> ValidateWindowAsync(
        ApplicationDbContext db, ICourtService courts,
        int courtId, DateOnly date, TimeOnly startTime, int durationHours,
        IReadOnlyList<(int CourtId, DateOnly Date, TimeOnly Start, TimeOnly End)>? windowsToAvoid = null)
    {
        if (durationHours < 1 || durationHours > 4)
            return "Duration must be between 1 and 4 hours.";

        if (date < DateOnly.FromDateTime(DateTime.Today))
            return "The selected date is in the past.";

        var court = await db.Courts.FindAsync(courtId);
        if (court == null)
            return "Court not found.";

        if (court.Status != CourtStatus.Available)
            return "This court is currently not available for booking.";

        var endTime = startTime.AddHours(durationHours);

        // Business rule: the window must lie fully inside Open availability slots.
        if (!await courts.IsWindowWithinOpenSlotsAsync(courtId, date, startTime, endTime))
            return "The selected time is outside the available booking hours.";

        // Business rule: no double booking.
        if (await courts.HasOverlappingReservationAsync(courtId, date, startTime, endTime))
            return "That time slot has just been booked by someone else. Please choose another slot.";

        if (windowsToAvoid != null)
        {
            foreach (var (wCourtId, wDate, wStart, wEnd) in windowsToAvoid)
            {
                if (wCourtId == courtId && wDate == date && startTime < wEnd && endTime > wStart)
                    return "This time window overlaps another item in your selection.";
            }
        }

        return null;
    }
}
