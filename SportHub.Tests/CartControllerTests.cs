using System.Security.Claims;
using SportHub.Controllers;
using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SportHub.Tests;

/// <summary>
/// P4 cart controller flow: add/update/remove under the member's identity, the
/// checkout → payment → paid page round trip, and resource-level ownership
/// (another user's reservation ids are forbidden).
/// </summary>
public class CartControllerTests
{
    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static (CartController Controller, ApplicationDbContext Db, int MemberId, int OtherUserId, int CourtId)
        CreateController(string userEmail = "member@test.local", ApplicationDbContext? db = null)
    {
        db ??= TestDb.Create();
        var courts = new CourtService(db);
        var checkout = new CheckoutService(db, courts, new VoucherService(db), new ReservationService(db, courts), new NoopEmailSender());
        var controller = new CartController(
            db,
            new CartService(db, courts),
            checkout,
            new ToyyibPayService(db, new ToyyibPayOptions()));
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
            .BuildServiceProvider();
        var userId = db.Users.Single(u => u.Email == userEmail).Id;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        // .NET 10's ControllerContext() leaves RouteData null, which UrlHelperBase
        // dereferences — populate both so RedirectToAction can build a helper.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor { ControllerName = "Cart" }
        };
        controller.TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider());
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var otherUserId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        return (controller, db, memberId, otherUserId, db.Courts.Single().Id);
    }

    private static DateOnly FutureDate(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(days));

    private static CartItem AddCartLine(ApplicationDbContext db, int userId, int courtId,
        DateOnly? date = null, TimeOnly? start = null, int duration = 1)
    {
        var item = new CartItem
        {
            UserId = userId,
            CourtId = courtId,
            Date = date ?? FutureDate(3),
            StartTime = start ?? new TimeOnly(9, 0),
            DurationHours = duration
        };
        db.CartItems.Add(item);
        db.SaveChanges();
        return item;
    }

    [Fact]
    public async Task Index_ShowsUsersItemsWithSubtotal()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        AddCartLine(db, memberId, courtId, duration: 2);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CartIndexViewModel>(view.Model);
        var item = Assert.Single(vm.Items);
        Assert.NotNull(item.Court);
        Assert.Equal(50m, vm.Subtotal); // rate 25 × 2 hours
    }

    [Fact]
    public async Task AddItem_ValidSlot_RedirectsToCart()
    {
        var (controller, db, memberId, _, courtId) = CreateController();

        var result = await controller.AddItem(courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(1, await db.CartItems.CountAsync());
        Assert.Equal("Slot added to your cart.", controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task AddItem_InvalidModelState_RedirectsToBookingPage()
    {
        var (controller, _, _, _, courtId) = CreateController();
        controller.ModelState.AddModelError("courtId", "Required");

        var result = await controller.AddItem(courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Create", redirect.ActionName);
        Assert.Equal("Reservations", redirect.ControllerName);
    }

    [Fact]
    public async Task AddItem_OverlappingOtherLine_FlashErrorAndKeepsCart()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        AddCartLine(db, memberId, courtId, start: new TimeOnly(9, 0), duration: 2);

        var result = await controller.AddItem(courtId, FutureDate(3), new TimeOnly(10, 0), 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Create", redirect.ActionName);
        Assert.Contains("overlaps another item", (string)controller.TempData["ErrorMessage"]!);
        Assert.Equal(1, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Update_ChangesDuration()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        var item = AddCartLine(db, memberId, courtId, duration: 1);

        var result = await controller.Update(item.Id, 3);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(3, (await db.CartItems.SingleAsync()).DurationHours);
        Assert.Equal("Cart updated.", controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task Remove_IsScopedToUser()
    {
        var (ownerController, db, memberId, _, courtId) = CreateController();
        var item = AddCartLine(db, memberId, courtId);

        var (foreignController, _, _, _, _) = CreateController("admin2@test.local", db);
        await foreignController.Remove(item.Id);
        await ownerController.Remove(item.Id);

        Assert.Equal("Cart item not found.", foreignController.TempData["ErrorMessage"]);
        Assert.Equal("Item removed from your cart.", ownerController.TempData["SuccessMessage"]);
        Assert.Equal(0, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task BatchRemove_RemovesOnlySelected()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        var first = AddCartLine(db, memberId, courtId, start: new TimeOnly(9, 0));
        AddCartLine(db, memberId, courtId, start: new TimeOnly(11, 0));

        var result = await controller.BatchRemove(new List<int> { first.Id });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("1 item", (string)controller.TempData["SuccessMessage"]!);
        Assert.Equal(1, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Clear_EmptiesCart()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        AddCartLine(db, memberId, courtId, start: new TimeOnly(9, 0));
        AddCartLine(db, memberId, courtId, start: new TimeOnly(11, 0));

        var result = await controller.Clear();

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(0, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Checkout_ValidCart_RedirectsToCheckoutComplete()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        var item = AddCartLine(db, memberId, courtId);

        var result = await controller.Checkout(new List<int> { item.Id }, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("CheckoutComplete", redirect.ActionName);
        Assert.True(redirect!.RouteValues!.TryGetValue("reservationIds", out var idsObject));
        var ids = Assert.IsType<List<int>>(idsObject);
        var reservationId = Assert.Single(ids);
        Assert.Equal(1, await db.Reservations.CountAsync());
        Assert.Equal(reservationId, (await db.Reservations.SingleAsync()).Id);
        Assert.Equal(0, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Checkout_WithVoucher_FlashMentionsSavings()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        db.Vouchers.Add(new Voucher
        {
            Code = "WELCOME10",
            Description = "Welcome",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            ExpiryDate = FutureDate(30)
        });
        db.SaveChanges();
        var item = AddCartLine(db, memberId, courtId); // subtotal 25 → 2.50 off

        await controller.Checkout(new List<int> { item.Id }, "WELCOME10");

        Assert.Contains("saved you RM 2.50", (string)controller.TempData["SuccessMessage"]!);
        Assert.Equal(2.50m, (await db.Reservations.SingleAsync()).DiscountAmount);
    }

    [Fact]
    public async Task CheckoutComplete_OwnReservations_ReturnsPaymentView()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        var item = AddCartLine(db, memberId, courtId);
        await controller.Checkout(new List<int> { item.Id }, null);
        var reservationId = (await db.Reservations.SingleAsync()).Id;

        var result = await controller.CheckoutComplete(new List<int> { reservationId });

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CheckoutPaymentViewModel>(view.Model);
        var reservation = Assert.Single(vm.Reservations);
        Assert.Equal(reservationId, reservation.Id);
        Assert.Equal(25m, vm.TotalDue);
    }

    [Fact]
    public async Task CheckoutComplete_AnotherUsersReservations_Forbids()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        var item = AddCartLine(db, memberId, courtId);
        await controller.Checkout(new List<int> { item.Id }, null);
        var reservationId = (await db.Reservations.SingleAsync()).Id;

        var (foreignController, _, _, _, _) = CreateController("admin2@test.local", db);
        var result = await foreignController.CheckoutComplete(new List<int> { reservationId });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CheckoutCompletePost_PaysAndRedirectsToPaid()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        var item = AddCartLine(db, memberId, courtId);
        await controller.Checkout(new List<int> { item.Id }, null);
        var reservationId = (await db.Reservations.SingleAsync()).Id;

        var result = await controller.CheckoutComplete(new CheckoutPaymentViewModel
        {
            ReservationIds = new List<int> { reservationId },
            Method = PaymentMethod.OnlineTransfer
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Paid", redirect.ActionName);
        Assert.Equal(ReservationStatus.Confirmed, (await db.Reservations.SingleAsync()).Status);
        Assert.Equal(PaymentStatus.Paid, (await db.Payments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Paid_ListsConfirmedBookings()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        var item = AddCartLine(db, memberId, courtId);
        await controller.Checkout(new List<int> { item.Id }, null);
        var reservationId = (await db.Reservations.SingleAsync()).Id;
        await controller.CheckoutComplete(new CheckoutPaymentViewModel
        {
            ReservationIds = new List<int> { reservationId }
        });

        var result = await controller.Paid(new List<int> { reservationId });

        var view = Assert.IsType<ViewResult>(result);
        var reservations = Assert.IsType<List<Reservation>>(view.Model);
        var reservation = Assert.Single(reservations);
        Assert.NotNull(reservation.Court);
        Assert.NotNull(reservation.Court!.Facility);
    }
}
