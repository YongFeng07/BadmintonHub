using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SportHub.Controllers;

/// <summary>Public-facing court browsing, details and AJAX availability checking.</summary>
public class CourtsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICourtService _courtService;

    public CourtsController(ApplicationDbContext db, ICourtService courtService)
    {
        _db = db;
        _courtService = courtService;
    }

    // G-M6: AJAX search/sort/paging. Sort keys are whitelisted below; the JS
    // engine (ajax-list.js) requests the list region only with X-Requested-With.
    public async Task<IActionResult> Index(string? search, CourtType? type, string? sort, string? dir,
        int page = 1, int size = 10)
    {
        var request = AjaxListRequest.From(Request.Query);

        var query = _db.Courts
            .Include(c => c.Facility)
            .Include(c => c.Photos.OrderBy(p => p.DisplayOrder))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search;
            query = query.Where(c => c.CourtNumber.Contains(term) ||
                                     (c.Description != null && c.Description.Contains(term)) ||
                                     (c.Facility != null && c.Facility.Name.Contains(term)));
        }

        if (type.HasValue)
            query = query.Where(c => c.CourtType == type);

        query = (request.Sort, request.Descending) switch
        {
            ("rate", false) => query.OrderBy(c => c.HourlyRate).ThenBy(c => c.CourtNumber),
            ("rate", true) => query.OrderByDescending(c => c.HourlyRate).ThenBy(c => c.CourtNumber),
            ("type", false) => query.OrderBy(c => c.CourtType).ThenBy(c => c.CourtNumber),
            ("type", true) => query.OrderByDescending(c => c.CourtType).ThenBy(c => c.CourtNumber),
            ("facility", false) => query.OrderBy(c => c.Facility!.Name).ThenBy(c => c.CourtNumber),
            ("facility", true) => query.OrderByDescending(c => c.Facility!.Name).ThenBy(c => c.CourtNumber),
            _ => request.Descending
                ? query.OrderByDescending(c => c.CourtNumber)
                : query.OrderBy(c => c.CourtNumber) // default: court number ascending
        };

        var total = await query.CountAsync();
        var pager = AjaxPager.For(request, total);
        // The type filter lives in the filter form, so every pager/sort link must
        // carry it through — otherwise sorting resets the filter silently.
        if (type.HasValue)
            pager.Extra["type"] = type.Value.ToString();

        var courts = await query.Skip((pager.Page - 1) * pager.PageSize)
            .Take(pager.PageSize)
            .ToListAsync();

        var vm = new CourtIndexViewModel
        {
            Search = request.Search,
            Type = type,
            Page = new AjaxListPage<Court> { Items = courts, Pager = pager }
        };

        if (Request.IsAjaxListRequest())
            return PartialView("_CourtGrid", vm.Page);
        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var court = await _db.Courts
            .Include(c => c.Facility)
            .Include(c => c.Photos.OrderBy(p => p.DisplayOrder))
            .FirstOrDefaultAsync(c => c.Id == id);

        if (court == null)
            return NotFound();

        // Wishlist entry point: the details page shows "Add to Wishlist" (or the
        // "already wishlisted" state) — especially for unavailable courts.
        ViewBag.IsWishlisted = User.Identity?.IsAuthenticated == true &&
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) &&
            await _db.WishlistItems.AnyAsync(w => w.UserId == userId && w.CourtId == court.Id);

        return View(court);
    }

    /// <summary>
    /// AJAX endpoint (M1 additional feature): returns the slot grid for one court
    /// on the chosen date without a full page reload. Reused by the booking flow.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CheckAvailability(int courtId, DateOnly date)
    {
        if (date < DateOnly.FromDateTime(DateTime.Today))
            return Json(new { success = false, message = "Past dates cannot be checked." });

        var slots = await _courtService.GetAvailabilityForDateAsync(date, courtId);
        return Json(new { success = true, date = date.ToString("yyyy-MM-dd"), slots });
    }
}
