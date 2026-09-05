using Microsoft.AspNetCore.Mvc;

namespace BadmintonHub.Controllers;

/// <summary>
/// Legacy route kept for bookmarks: the single-facility about page is now the
/// multi-facility catalog (revised spec).
/// </summary>
public class FacilityController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Catalog");
}
