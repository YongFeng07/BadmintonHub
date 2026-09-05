using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Tests;

/// <summary>
/// M2 core business rules: booking validation, server-side double-booking
/// protection, payment consistency and the cancel-with-refund rule.
/// </summary>
public class ReservationServiceTests
{
    private static (ReservationService Service, ApplicationDbContext Db, int MemberId, int CourtId) Create()
    {
        var db = TestDb.Create();
        var service = new ReservationService(db, new CourtService(db));
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var courtId = db.Courts.Single().Id;
        return (service, db, memberId, courtId);
    }

    private static DateOnly FutureDate(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(days));

    [Fact]
    public async Task Create_ValidBooking_ReturnsPendingWithPaymentAndReference()
    {
        var (service, db, memberId, courtId) = Create();
        var date = FutureDate(3);

        var (success, error, reservation) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(9, 0), 1, null);

        Assert.True(success, error);
        Assert.NotNull(reservation);
        Assert.Equal(ReservationStatus.Pending, reservation!.Status);
        Assert.Equal(25m, reservation.TotalAmount); // rate 25 x 1 hour
        Assert.Matches(@"^BH-\d{4}-\d{6}$", reservation.ReservationReference);
        Assert.Equal("BH-" + date.Year + "-000001", reservation.ReservationReference); // first booking

        var payment = db.Payments.Single();
        Assert.Equal(reservation.Id, payment.ReservationId);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Single(db.Notifications); // creation notification
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Create_InvalidDuration_Fails(int duration)
    {
        var (service, _, memberId, courtId) = Create();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), duration, null);

        Assert.False(success);
        Assert.Contains("Duration", error);
    }

    [Fact]
    public async Task Create_PastDate_Fails()
    {
        var (service, _, memberId, courtId) = Create();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(-1), new TimeOnly(9, 0), 1, null);

        Assert.False(success);
        Assert.Contains("past", error);
    }

    [Fact]
    public async Task Create_UnknownCourt_Fails()
    {
        var (service, _, memberId, _) = Create();

        var (success, error, _) = await service.CreateAsync(memberId, 9999, FutureDate(3), new TimeOnly(9, 0), 1, null);

        Assert.False(success);
        Assert.Equal("Court not found.", error);
    }

    [Fact]
    public async Task Create_CourtUnderMaintenance_Fails()
    {
        var (service, db, memberId, courtId) = Create();
        db.Courts.Single().Status = CourtStatus.Maintenance;
        await db.SaveChangesAsync();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        Assert.False(success);
        Assert.Contains("not available", error);
    }

    [Fact]
    public async Task Create_WindowOutsideOpeningHours_Fails()
    {
        var (service, _, memberId, courtId) = Create();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(22, 30), 1, null);

        Assert.False(success); // 22:30-23:30 exceeds the 23:00 closing time
        Assert.Contains("available booking hours", error);
    }

    [Fact]
    public async Task Create_DoubleBookingSameSlot_SecondIsRejectedServerSide()
    {
        var (service, db, memberId, courtId) = Create();
        var date = FutureDate(3);

        var first = await service.CreateAsync(memberId, courtId, date, new TimeOnly(10, 0), 2, null);
        var second = await service.CreateAsync(memberId, courtId, date, new TimeOnly(10, 0), 1, null);
        var overlap = await service.CreateAsync(memberId, courtId, date, new TimeOnly(11, 30), 1, null);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Contains("just been booked", second.Error);
        Assert.False(overlap.Success); // partial overlap is also refused
        Assert.Equal(1, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task MarkPaid_PendingReservation_ConfirmsBooking()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        var (success, error) = await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.OnlineTransfer, "E2E-1");

        Assert.True(success, error);
        var payment = db.Payments.Single();
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal("E2E-1", payment.PaymentReference);
        Assert.NotNull(payment.PaidAt);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status); // consistency rule
        Assert.Single(db.Notifications, n => n.Type == NotificationType.Payment);
    }

    [Fact]
    public async Task MarkPaid_AlreadyPaid_Fails()
    {
        var (service, _, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.Cash, null);

        var (success, error) = await service.MarkPaidAsync(reservation.Id, memberId, PaymentMethod.Cash, null);

        Assert.False(success);
        Assert.Equal("This reservation is already paid.", error);
    }

    [Fact]
    public async Task MarkPaid_AnotherMemberCannotPay_Fails()
    {
        var (service, db, memberId, courtId) = Create();
        var otherId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        var (success, error) = await service.MarkPaidAsync(reservation!.Id, otherId, PaymentMethod.Cash, null);

        Assert.False(success); // another user's id — the back-office flag was not passed
        Assert.Equal("You can only pay for your own reservations.", error);
    }

    [Fact]
    public async Task Cancel_PaidBooking_BecomesRefund()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.Card, null);

        var (success, error) = await service.CancelAsync(reservation.Id, memberId, "No longer needed");

        Assert.True(success, error);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(PaymentStatus.Refunded, db.Payments.Single().Status); // refund rule
        Assert.Equal("No longer needed", reservation.CancellationReason);
        Assert.NotNull(reservation.CancelledAt);
    }

    [Fact]
    public async Task Cancel_CompletedBooking_Fails()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        reservation!.Status = ReservationStatus.Completed;
        await db.SaveChangesAsync();

        var (success, error) = await service.CancelAsync(reservation.Id, memberId, null);

        Assert.False(success);
        Assert.Contains("cannot be cancelled", error);
    }
}
