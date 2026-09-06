using System.Security.Claims;
using SportHub.Controllers;
using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SportHub.Tests;

/// <summary>
/// P4 wishlist controller: add with safe return-url handling (open-redirect guard),
/// duplicate flash and ownership-scoped removal under the member's identity.
/// </summary>
public class WishlistControllerTests
{
    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static (WishlistController Controller, ApplicationDbContext Db, int MemberId, int OtherUserId, int CourtId)
        CreateController(string userEmail = "member@test.local", ApplicationDbContext? db = null)
    {
        db ??= TestDb.Create();
        // The wishlist is for unavailable courts; flip the seeded court so adds succeed.
        db.Courts.Single().Status = CourtStatus.Maintenance;
        db.SaveChanges();
        var courtService = new CourtService(db);
        var controller = new WishlistController(
            new WishlistService(db, courtService, new NoopEmailSender()), courtService);
        var httpContext = new DefaultHttpContext();
        // The Add action resolves Url.IsLocalUrl; give the context a real UrlHelperFactory.
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
            .BuildServiceProvider();
        var userId = db.Users.Single(u => u.Email == userEmail).Id;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        // .NET 10's ControllerContext() leaves RouteData null, which UrlHelperBase
        // dereferences — populate both so Url.IsLocalUrl / RedirectToAction can build a helper.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor { ControllerName = "Wishlist" }
        };
        controller.TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider());
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var otherUserId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        return (controller, db, memberId, otherUserId, db.Courts.Single().Id);
    }

    [Fact]
    public async Task Index_ListsUsersItems()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        db.WishlistItems.Add(new WishlistItem { UserId = memberId, CourtId = courtId });
        db.SaveChanges();

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ViewModels.WishlistIndexViewModel>(view.Model);
        var item = Assert.Single(model.Items);
        Assert.Equal(courtId, item.Item.CourtId);
        Assert.NotNull(item.Item.Court);
        Assert.False(item.HasOpenSlots); // no per-hour availability rows in the test db
    }

    [Fact]
    public async Task Add_ValidCourt_RedirectsToLocalReturnUrl()
    {
        var (controller, db, memberId, _, courtId) = CreateController();

        var result = await controller.Add(courtId, "/courts/details/1");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/courts/details/1", redirect.Url);
        Assert.Equal(memberId, db.WishlistItems.Single().UserId);
    }

    [Fact]
    public async Task Add_ExternalReturnUrl_IsIgnored()
    {
        var (controller, db, _, _, courtId) = CreateController();

        var result = await controller.Add(courtId, "https://evil.example/phish");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName); // open redirect guarded
        Assert.Single(db.WishlistItems);
    }

    [Fact]
    public async Task Add_Duplicate_FlashError()
    {
        var (controller, db, memberId, _, courtId) = CreateController();
        db.WishlistItems.Add(new WishlistItem { UserId = memberId, CourtId = courtId });
        db.SaveChanges();

        await controller.Add(courtId, null);

        Assert.Equal("This court is already in your wishlist.", controller.TempData["ErrorMessage"]);
        Assert.Single(db.WishlistItems);
    }

    [Fact]
    public async Task Add_UnknownCourt_FlashError()
    {
        var (controller, db, _, _, _) = CreateController();

        await controller.Add(9999, null);

        Assert.Equal("Court not found.", controller.TempData["ErrorMessage"]);
        Assert.Empty(db.WishlistItems);
    }

    [Fact]
    public async Task Remove_IsScopedToUser()
    {
        var (ownerController, db, memberId, _, courtId) = CreateController();
        db.WishlistItems.Add(new WishlistItem { UserId = memberId, CourtId = courtId });
        db.SaveChanges();
        var itemId = db.WishlistItems.Single().Id;

        var (foreignController, _, _, _, _) = CreateController("admin2@test.local", db);
        await foreignController.Remove(itemId);
        await ownerController.Remove(itemId);

        Assert.Equal("Wishlist item not found.", foreignController.TempData["ErrorMessage"]);
        Assert.Equal("Removed from your wishlist.", ownerController.TempData["SuccessMessage"]);
        Assert.Empty(db.WishlistItems);
    }
}
