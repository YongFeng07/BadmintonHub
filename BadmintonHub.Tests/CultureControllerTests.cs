using BadmintonHub.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace BadmintonHub.Tests;

/// <summary>
/// P6 language switcher: only the three supported cultures are stored in the
/// localization cookie; return URLs are guarded against open redirects.
/// </summary>
public class CultureControllerTests
{
    private sealed class FakeUrlHelper(bool isLocal) : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => null;
        public string? Content(string? contentPath) => null;
        public bool IsLocalUrl(string? url) => isLocal;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private static CultureController CreateController(bool isLocalUrl = true)
    {
        var httpContext = new DefaultHttpContext();
        return new CultureController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            Url = new FakeUrlHelper(isLocalUrl)
        };
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("ms-MY")]
    public void SetLanguage_SupportedCulture_SetsOneYearCookie(string culture)
    {
        var controller = CreateController();

        var result = controller.SetLanguage(culture, null);

        Assert.IsType<RedirectToActionResult>(result);
        var setCookie = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(CookieRequestCultureProvider.DefaultCookieName, setCookie);
        Assert.Contains(culture, setCookie);
        Assert.Contains("expires", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetLanguage_UnsupportedCulture_SetsNoCookie()
    {
        var controller = CreateController();

        var result = controller.SetLanguage("fr-FR", null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.DoesNotContain(controller.HttpContext.Response.Headers,
            h => h.Value.ToString().Contains(CookieRequestCultureProvider.DefaultCookieName));
    }

    [Fact]
    public void SetLanguage_LocalReturnUrl_RedirectsLocally()
    {
        var controller = CreateController(isLocalUrl: true);

        var result = controller.SetLanguage("zh-CN", "/Courts");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Courts", redirect.Url);
    }

    [Fact]
    public void SetLanguage_ExternalReturnUrl_IgnoresIt()
    {
        var controller = CreateController(isLocalUrl: false);

        var result = controller.SetLanguage("zh-CN", "https://evil.example.com/phish");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }
}
