using SportHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace SportHub.Controllers;

/// <summary>
/// Member wishlist (revised spec): "Currently unavailable — Add to Wishlist" lets a
/// member save an unavailable court and book it from the wishlist once it reopens.
/// </summary>
[Authorize]
public class WishlistController : Controller
{
    private readonly IWishlistService _wishlistService;

    public WishlistController(IWishlistService wishlistService)
    {
        _wishlistService = wishlistService;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        return View(await _wishlistService.GetItemsAsync(CurrentUserId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int courtId, string? returnUrl)
    {
        var (success, error) = await _wishlistService.AddAsync(CurrentUserId, courtId);
        if (!success) TempData["ErrorMessage"] = error;
        else TempData["SuccessMessage"] = "Court added to your wishlist.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int itemId)
    {
        var (success, error) = await _wishlistService.RemoveAsync(CurrentUserId, itemId);
        if (!success) TempData["ErrorMessage"] = error;
        else TempData["SuccessMessage"] = "Removed from your wishlist.";
        return RedirectToAction(nameof(Index));
    }
}
