using SportHub.Data;
using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Services;

/// <summary>
/// G-M5: sends each member a reminder (email + in-app notification) once their
/// confirmed booking starts within the next 24 hours. ReminderSentAt makes it a
/// one-shot per booking. Called periodically by ReservationReminderWorker.
/// </summary>
public class ReminderService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emails;

    public ReminderService(ApplicationDbContext db, IEmailService emails)
    {
        _db = db;
        _emails = emails;
    }

    public async Task<int> SendRemindersAsync()
    {
        var now = DateTime.Now;
        var windowEnd = now.AddHours(24);

        var upcoming = await _db.Reservations
            .Include(r => r.Court).ThenInclude(c => c!.Facility)
            .Include(r => r.User)
            .Where(r => r.Status == ReservationStatus.Confirmed && r.ReminderSentAt == null)
            .ToListAsync();

        var reminded = 0;
        foreach (var reservation in upcoming)
        {
            var start = reservation.ReservationDate.ToDateTime(reservation.StartTime);
            if (start < now || start > windowEnd)
                continue; // outside the 24-hour window — check again on a later pass

            reservation.ReminderSentAt = now;

            _db.Notifications.Add(new Notification
            {
                UserId = reservation.UserId,
                Title = "Booking starts soon",
                Message = $"Your booking {reservation.ReservationReference} on Court {reservation.Court?.CourtNumber} starts " +
                          $"{reservation.ReservationDate:dd MMM yyyy} at {reservation.StartTime:HH:mm}.",
                Type = NotificationType.Reservation,
                TargetUrl = $"/Reservations/Details/{reservation.Id}"
            });

            var member = reservation.User;
            if (member != null && !string.IsNullOrWhiteSpace(member.Email))
            {
                await _emails.SendAsync(member.Email,
                    $"Reminder — {reservation.ReservationReference} starts soon",
                    EmailTemplates.BookingReminderEmail(member.FullName, reservation.ReservationReference,
                        $"Court {reservation.Court?.CourtNumber} ({reservation.Court?.Facility?.Name ?? "SportHub"})",
                        $"{reservation.ReservationDate:dd MMM yyyy}, {reservation.StartTime:HH:mm}–{reservation.EndTime:HH:mm}"));
            }

            reminded++;
        }

        if (reminded > 0)
            await _db.SaveChangesAsync();

        return reminded;
    }
}
