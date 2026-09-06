using SportHub.Data;
using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Services;

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
    /// <paramref name="currentUserId"/> enables the G-M3 cart-hold check: another
    /// member's active hold on the window blocks the booking.
    /// </summary>
    public static async Task<string?> ValidateWindowAsync(
        ApplicationDbContext db, ICourtService courts,
        int courtId, DateOnly date, TimeOnly startTime, int durationHours,
        IReadOnlyList<(int CourtId, DateOnly Date, TimeOnly Start, TimeOnly End)>? windowsToAvoid = null,
        int? currentUserId = null)
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

        // G-M3: another member's active cart hold acts as a soft lock. Own holds are
        // fine (own cart lines are covered by windowsToAvoid above).
        if (currentUserId.HasValue &&
            await HasActiveHoldAsync(db, courtId, date, startTime, endTime, currentUserId.Value))
            return "That slot is currently being held in another member's cart. It is released automatically after 15 minutes if not checked out.";

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

    /// <summary>
    /// Whether another member's cart currently holds (any hour of) the window. Holds
    /// are time-boxed by CartItem.HeldUntil, so a stale hold never blocks forever.
    /// </summary>
    public static async Task<bool> HasActiveHoldAsync(
        ApplicationDbContext db, int courtId, DateOnly date, TimeOnly start, TimeOnly end, int currentUserId)
    {
        var now = DateTime.Now;
        return await db.CartItems.AnyAsync(i =>
            i.CourtId == courtId &&
            i.Date == date &&
            i.UserId != currentUserId &&
            i.HeldUntil != null && i.HeldUntil > now &&
            i.StartTime < end && i.StartTime.AddHours(i.DurationHours) > start);
    }

    /// <summary>
    /// Inserts one ReservationSlot row per claimed hour. Must be called inside the
    /// same transaction as the reservation insert; the unique (CourtId, Date,
    /// StartTime) index is the atomic backstop against concurrent checkouts — the
    /// caller catches the resulting DbUpdateException and reports a friendly error.
    /// </summary>
    public static void ClaimWindow(ApplicationDbContext db, Reservation reservation)
    {
        for (var h = 0; h < reservation.DurationHours; h++)
        {
            db.ReservationSlots.Add(new ReservationSlot
            {
                CourtId = reservation.CourtId,
                Date = reservation.ReservationDate,
                StartTime = reservation.StartTime.AddHours(h),
                Reservation = reservation
            });
        }
    }

    /// <summary>Releases every hourly claim of a reservation (cancel/reject/timeout).</summary>
    public static async Task ReleaseWindowAsync(ApplicationDbContext db, int reservationId)
    {
        var slots = await db.ReservationSlots
            .Where(s => s.ReservationId == reservationId)
            .ToListAsync();
        db.ReservationSlots.RemoveRange(slots);
    }
}
