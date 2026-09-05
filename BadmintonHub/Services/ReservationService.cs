using BadmintonHub.Data;
using BadmintonHub.Models;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Services;

/// <summary>
/// All reservation business rules live here so the booking flow, the staff
/// administration and the member pages share one consistent implementation.
/// </summary>
public class ReservationService : IReservationService
{
    private readonly ApplicationDbContext _db;
    private readonly ICourtService _courtService;

    public ReservationService(ApplicationDbContext db, ICourtService courtService)
    {
        _db = db;
        _courtService = courtService;
    }

    public async Task<(bool Success, string? Error, Reservation? Reservation)> CreateAsync(
        int userId, int courtId, DateOnly date, TimeOnly startTime, int durationHours, string? notes)
    {
        if (durationHours < 1 || durationHours > 4)
            return (false, "Duration must be between 1 and 4 hours.", null);

        if (date < DateOnly.FromDateTime(DateTime.Today))
            return (false, "The selected date is in the past.", null);

        var court = await _db.Courts.FindAsync(courtId);
        if (court == null)
            return (false, "Court not found.", null);

        if (court.Status != CourtStatus.Available)
            return (false, "This court is currently not available for booking.", null);

        var endTime = startTime.AddHours(durationHours);

        // Business rule: the window must lie fully inside Open availability slots.
        if (!await _courtService.IsWindowWithinOpenSlotsAsync(courtId, date, startTime, endTime))
            return (false, "The selected time is outside the available booking hours.", null);

        // Business rule: no double booking.
        if (await _courtService.HasOverlappingReservationAsync(courtId, date, startTime, endTime))
            return (false, "That time slot has just been booked by someone else. Please choose another slot.", null);

        var maxId = await _db.Reservations.MaxAsync(r => (int?)r.Id) ?? 0;
        var reservation = new Reservation
        {
            ReservationReference = $"BH-{date.Year}-{maxId + 1:000000}",
            UserId = userId,
            CourtId = courtId,
            ReservationDate = date,
            StartTime = startTime,
            EndTime = endTime,
            DurationHours = durationHours,
            TotalAmount = court.HourlyRate * durationHours,
            Status = ReservationStatus.Pending,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        _db.Reservations.Add(reservation);

        // Payment starts Pending; it is confirmed (and the booking confirmed) on payment.
        _db.Payments.Add(new Payment
        {
            Reservation = reservation,
            UserId = userId,
            Amount = reservation.TotalAmount,
            Method = PaymentMethod.OnlineTransfer,
            Status = PaymentStatus.Pending
        });

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "Reservation created",
            Message = $"Booking {reservation.ReservationReference} for Court {court.CourtNumber} on {date:dd MMM yyyy} is awaiting payment.",
            Type = NotificationType.Reservation
        });

        await _db.SaveChangesAsync();
        return (true, null, reservation);
    }

    public async Task<(bool Success, string? Error)> MarkPaidAsync(
        int reservationId, int userId, PaymentMethod method, string? reference, bool isBackOffice = false)
    {
        var reservation = await _db.Reservations
            .Include(r => r.Payment)
            .Include(r => r.Court)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null)
            return (false, "Reservation not found.");

        if (reservation.UserId != userId && !isBackOffice)
            return (false, "You can only pay for your own reservations.");

        if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Rejected or ReservationStatus.Completed)
            return (false, "Payment is not allowed for this reservation status.");

        if (reservation.Payment == null)
            return (false, "Payment record not found.");

        if (reservation.Payment.Status == PaymentStatus.Paid)
            return (false, "This reservation is already paid.");

        reservation.Payment.Method = method;
        reservation.Payment.Status = PaymentStatus.Paid;
        reservation.Payment.PaidAt = DateTime.Now;
        reservation.Payment.PaymentReference = string.IsNullOrWhiteSpace(reference)
            ? $"REF-{reservation.ReservationReference}"
            : reference.Trim();

        // Business rule: payment and reservation status stay consistent.
        reservation.Status = ReservationStatus.Confirmed;
        reservation.UpdatedAt = DateTime.Now;

        _db.Notifications.Add(new Notification
        {
            UserId = reservation.UserId,
            Title = "Booking confirmed",
            Message = $"Payment received for {reservation.ReservationReference}. Your booking on Court {reservation.Court?.CourtNumber} is confirmed.",
            Type = NotificationType.Payment
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CancelAsync(
        int reservationId, int userId, string? reason, bool isBackOffice = false)
    {
        var reservation = await _db.Reservations
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null)
            return (false, "Reservation not found.");

        if (reservation.UserId != userId && !isBackOffice)
            return (false, "You can only cancel your own reservations.");

        if (reservation.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
            return (false, $"A {reservation.Status.ToString().ToLowerInvariant()} reservation cannot be cancelled.");

        var startDateTime = reservation.ReservationDate.ToDateTime(reservation.StartTime);
        if (startDateTime <= DateTime.Now && !isBackOffice)
            return (false, "This reservation has already started and cannot be cancelled.");

        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        reservation.CancelledAt = DateTime.Now;
        reservation.UpdatedAt = DateTime.Now;

        // Business rule: a paid booking that is cancelled becomes a refund.
        if (reservation.Payment != null && reservation.Payment.Status == PaymentStatus.Paid)
            reservation.Payment.Status = PaymentStatus.Refunded;

        _db.Notifications.Add(new Notification
        {
            UserId = reservation.UserId,
            Title = "Reservation cancelled",
            Message = $"Booking {reservation.ReservationReference} was cancelled." +
                      (reservation.Payment?.Status == PaymentStatus.Refunded ? " Your payment has been refunded." : string.Empty),
            Type = NotificationType.Reservation
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }
}
