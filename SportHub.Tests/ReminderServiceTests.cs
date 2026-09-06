using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// G-M5 24-hour booking reminder: one-shot per confirmed booking, email +
/// in-app notification with a deep link to the booking details.
/// </summary>
public class ReminderServiceTests
{
    private static (ReminderService Service, ApplicationDbContext Db, NoopEmailSender Emails) Create()
    {
        var db = TestDb.Create();
        var emails = new NoopEmailSender();
        return (new ReminderService(db, emails), db, emails);
    }

    private static Reservation AddReservation(ApplicationDbContext db, DateTime startsAt,
        ReservationStatus status = ReservationStatus.Confirmed)
    {
        var reservation = new Reservation
        {
            ReservationReference = "SH-TEST-000009",
            UserId = db.Users.Single(u => u.Email == "member@test.local").Id,
            CourtId = db.Courts.Single().Id,
            ReservationDate = DateOnly.FromDateTime(startsAt),
            StartTime = TimeOnly.FromDateTime(startsAt),
            EndTime = TimeOnly.FromDateTime(startsAt).AddHours(1),
            DurationHours = 1,
            TotalAmount = 25m,
            Status = status
        };
        db.Reservations.Add(reservation);
        db.SaveChanges();
        return reservation;
    }

    [Fact]
    public async Task SendReminders_BookingWithin24Hours_RemindsOnce()
    {
        var (service, db, emails) = Create();
        var reservation = AddReservation(db, DateTime.Now.AddHours(20));

        var reminded = await service.SendRemindersAsync();

        Assert.Equal(1, reminded);
        Assert.NotNull(reservation.ReminderSentAt); // one-shot flag set
        var mail = Assert.Single(emails.Sent);
        Assert.Equal($"Reminder — {reservation.ReservationReference} starts soon", mail.Subject);
        Assert.Equal("member@test.local", mail.To);
        var notification = db.Notifications.Single(n => n.Title == "Booking starts soon");
        Assert.Equal(reservation.UserId, notification.UserId);
        Assert.Equal($"/Reservations/Details/{reservation.Id}", notification.TargetUrl);

        var second = await service.SendRemindersAsync();
        Assert.Equal(0, second); // ReminderSentAt makes it one-shot
        Assert.Single(emails.Sent);
    }

    [Fact]
    public async Task SendReminders_BookingBeyond24Hours_Skips()
    {
        var (service, db, emails) = Create();
        var reservation = AddReservation(db, DateTime.Now.AddHours(40));

        var reminded = await service.SendRemindersAsync();

        Assert.Equal(0, reminded);
        Assert.Null(reservation.ReminderSentAt);
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task SendReminders_BookingAlreadyStarted_Skips()
    {
        var (service, db, emails) = Create();
        var reservation = AddReservation(db, DateTime.Now.AddHours(-1));

        var reminded = await service.SendRemindersAsync();

        Assert.Equal(0, reminded);
        Assert.Null(reservation.ReminderSentAt);
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task SendReminders_PendingBooking_Skips()
    {
        var (service, db, emails) = Create();
        var reservation = AddReservation(db, DateTime.Now.AddHours(20), ReservationStatus.Pending);

        var reminded = await service.SendRemindersAsync();

        Assert.Equal(0, reminded);
        Assert.Null(reservation.ReminderSentAt);
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task SendReminders_MultipleBookings_RemindsEachWithinWindow()
    {
        var (service, db, emails) = Create();
        var soon = AddReservation(db, DateTime.Now.AddHours(5));
        AddReservation(db, DateTime.Now.AddHours(30)); // outside window
        AddReservation(db, DateTime.Now.AddHours(2), ReservationStatus.Pending); // not confirmed

        var reminded = await service.SendRemindersAsync();

        Assert.Equal(1, reminded);
        Assert.NotNull(soon.ReminderSentAt);
        var mail = Assert.Single(emails.Sent);
        Assert.Contains(soon.ReservationReference, mail.Subject);
    }
}
