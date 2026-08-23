using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace BadmintonHub.Controllers;

/// <summary>
/// Language switcher (P6): stores the chosen culture in the localization cookie.
/// Only the three supported cultures are accepted; anything else is ignored.
/// </summary>
public class CultureController : Controller
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "en-US", "zh-CN", "ms-MY"
    };

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string? returnUrl)
    {
        if (Supported.Contains(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.Now.AddYears(1) });
        }

        // Open-redirect guard: only return to local addresses.
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }
}
