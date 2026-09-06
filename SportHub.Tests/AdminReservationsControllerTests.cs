using SportHub.Controllers;
using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SportHub.Tests;

/// <summary>
/// M4 reservation administration: whitelisted status transitions and
/// admin counter-payment/cancellation actions.
/// </summary>
public class AdminReservationsControllerTests
{
    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static (AdminReservationsController Controller, ApplicationDbContext Db, ReservationService Service)
        CreateController(bool withAdminClaims)
    {
        var db = TestDb.Create();
        var service = new ReservationService(db, new CourtService(db));
        var httpContext = new DefaultHttpContext();
        var controller = new AdminReservationsController(db, service)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider())
        };

        if (withAdminClaims)
        {
            var adminId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, adminId.ToString())
            }));
        }

        return (controller, db, service);
    }

    private static async Task<Reservation> CreateReservationAsync(ApplicationDbContext db, ReservationService service,
        ReservationStatus status = ReservationStatus.Pending)
    {
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var courtId = db.Courts.Single().Id;
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));

        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(9, 0), 1, null);
        if (status == ReservationStatus.Confirmed)
            await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.Cash, null);
        else if (status != ReservationStatus.Pending)
        {
            reservation!.Status = status;
            await db.SaveChangesAsync();
        }
        return reservation!;
    }

    [Fact]
    public async Task UpdateStatus_UnknownReservation_SetsErrorAndRedirects()
    {
        var (controller, _, _) = CreateController(false);

        var result = await controller.UpdateStatus(9999, "Confirmed", null);

        Assert.Equal("Reservation not found.", controller.TempData["ErrorMessage"]);
        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task UpdateStatus_InvalidStatusString_SetsError()
    {
        var (controller, db, service) = CreateController(false);
        var reservation = await CreateReservationAsync(db, service);

        await controller.UpdateStatus(reservation.Id, "Bogus", null);

        Assert.Equal("Invalid status.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task UpdateStatus_IllegalTransition_IsRejected()
    {
        var (controller, db, service) = CreateController(false);
        var reservation = await CreateReservationAsync(db, service, ReservationStatus.Confirmed);

        await controller.UpdateStatus(reservation.Id, "Cancelled", null);

        Assert.Equal("Changing a reservation from Confirmed to Cancelled is not allowed.",
            controller.TempData["ErrorMessage"]);
        Assert.Equal(ReservationStatus.Confirmed, db.Reservations.Single().Status); // unchanged
    }

    [Fact]
    public async Task UpdateStatus_PendingToConfirmed_IsAllowed()
    {
        var (controller, db, service) = CreateController(false);
        var reservation = await CreateReservationAsync(db, service);

        await controller.UpdateStatus(reservation.Id, "Confirmed", null);

        Assert.Equal(ReservationStatus.Confirmed, db.Reservations.Single().Status);
        Assert.Equal($"{reservation.ReservationReference} updated to Confirmed.", controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task UpdateStatus_PendingToRejected_FailsPendingPayment()
    {
        var (controller, db, service) = CreateController(false);
        var reservation = await CreateReservationAsync(db, service);

        await controller.UpdateStatus(reservation.Id, "Rejected", null);

        Assert.Equal(ReservationStatus.Rejected, db.Reservations.Single().Status);
        Assert.Equal(PaymentStatus.Failed, db.Payments.Single().Status); // business rule
        Assert.Single(db.Notifications, n => n.Title == "Reservation updated"); // member is notified
    }

    [Fact]
    public async Task UpdateStatus_ConfirmedToCompleted_IsAllowed()
    {
        var (controller, db, service) = CreateController(false);
        var reservation = await CreateReservationAsync(db, service, ReservationStatus.Confirmed);

        await controller.UpdateStatus(reservation.Id, "Completed", null);

        Assert.Equal(ReservationStatus.Completed, db.Reservations.Single().Status);
    }

    [Fact]
    public async Task MarkPaid_AdminRecordsCounterPayment_ConfirmsBooking()
    {
        var (controller, db, service) = CreateController(true);
        var reservation = await CreateReservationAsync(db, service);

        var result = await controller.MarkPaid(reservation.Id, PaymentMethod.Cash, "COUNTER-1", null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Payment recorded. The booking is confirmed.", controller.TempData["SuccessMessage"]);
        var payment = db.Payments.Single();
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal("COUNTER-1", payment.PaymentReference);
        Assert.Equal(ReservationStatus.Confirmed, db.Reservations.Single().Status);
    }

    [Fact]
    public async Task Cancel_AdminCancelsOnBehalf_IsRecorded()
    {
        var (controller, db, service) = CreateController(true);
        var reservation = await CreateReservationAsync(db, service, ReservationStatus.Confirmed);

        await controller.Cancel(reservation.Id, "Court closed", null);

        Assert.Equal("Reservation cancelled.", controller.TempData["SuccessMessage"]);
        Assert.Equal(ReservationStatus.Cancelled, db.Reservations.Single().Status);
        Assert.Equal(PaymentStatus.Refunded, db.Payments.Single().Status); // paid booking -> refund
        Assert.Equal("Court closed", db.Reservations.Single().CancellationReason);
    }

    [Fact]
    public async Task Index_BuildsFilteredModel()
    {
        var (controller, db, service) = CreateController(false);
        await CreateReservationAsync(db, service);

        var result = await controller.Index(null, null, null, null, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        Assert.NotNull(view.Model);
        Assert.Single(db.Reservations);
    }
}
