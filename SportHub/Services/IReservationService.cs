using SportHub.Models;

namespace SportHub.Services;

public interface IReservationService
{
    /// <summary>
    /// Creates a reservation plus its pending payment record and a notification.
    /// Enforces every booking business rule: future date, court available,
    /// window inside open slots, and no double booking.
    /// </summary>
    Task<(bool Success, string? Error, Reservation? Reservation)> CreateAsync(
        int userId, int courtId, DateOnly date, TimeOnly startTime, int durationHours, string? notes);

    /// <summary>Marks the reservation's payment as paid and confirms the booking.</summary>
    Task<(bool Success, string? Error)> MarkPaidAsync(
        int reservationId, int userId, PaymentMethod method, string? reference, bool isBackOffice = false);

    /// <summary>Cancels an upcoming booking (owner or staff). Paid payments become refunded.</summary>
    Task<(bool Success, string? Error)> CancelAsync(
        int reservationId, int userId, string? reason, bool isBackOffice = false);

    /// <summary>
    /// G-M3: cancels Pending reservations whose payment was not received within
    /// <paramref name="timeout"/>, fails their payment record, releases their slot
    /// claims and notifies the member. Returns how many were released.
    /// Called periodically by ReservationStatusUpdaterService.
    /// </summary>
    Task<int> ReleaseUnpaidPendingAsync(TimeSpan timeout);
}
