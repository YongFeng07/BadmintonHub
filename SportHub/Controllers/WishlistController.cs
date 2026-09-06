using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace SportHub.Controllers;

/// <summary>
/// Member wishlist (revised spec): "Currently unavailable — Add to Wishlist" lets a
/// member save an unavailable court, get notified when it reopens (G-M4), and book
/// it straight from the wishlist once real open slots exist.
/// </summary>
[Authorize]
public class WishlistController : Controller
{
    private readonly IWishlistService _wishlistService;
    private readonly ICourtService _courtService;

    public WishlistController(IWishlistService wishlistService, ICourtService courtService)
    {
        _wishlistService = wishlistService;
        _courtService = courtService;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        var items = await _wishlistService.GetItemsAsync(CurrentUserId);
        var viewItems = new List<WishlistItemViewModel>();

        foreach (var item in items)
        {
            var (firstOpenDate, openSlotCount) = await _courtService.GetUpcomingOpenSlotsAsync(item.CourtId);
            viewItems.Add(new WishlistItemViewModel
            {
                Item = item,
                HasOpenSlots = firstOpenDate != null && openSlotCount > 0,
                OpenSlotCount = openSlotCount,
                FirstOpenDate = firstOpenDate
            });
        }

        return View(new WishlistIndexViewModel { Items = viewItems });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int courtId, string? returnUrl)
    {
        var (success, error) = await _wishlistService.AddAsync(CurrentUserId, courtId);
        if (!success) TempData["ErrorMessage"] = error;
        else TempData["SuccessMessage"] = "Court added to your wishlist. We'll notify you as soon as it opens up again.";

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

    /// <summary>G-M4: batch removal of the member's own selected items.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBatch(List<int> itemIds)
    {
        if (itemIds == null || itemIds.Count == 0)
        {
            TempData["ErrorMessage"] = "Select at least one item to remove.";
            return RedirectToAction(nameof(Index));
        }

        var removed = await _wishlistService.RemoveBatchAsync(CurrentUserId, itemIds);
        TempData["SuccessMessage"] = removed > 0
            ? $"Removed {removed} item(s) from your wishlist."
            : "No items were removed.";
        return RedirectToAction(nameof(Index));
    }
}
