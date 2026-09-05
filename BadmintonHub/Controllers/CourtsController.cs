using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BadmintonHub.Controllers;

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

    public async Task<IActionResult> Index(string? search, CourtType? type, string? sort)
    {
        var query = _db.Courts
            .Include(c => c.Facility)
            .Include(c => c.Photos.OrderBy(p => p.DisplayOrder))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.CourtNumber.Contains(term) ||
                                     (c.Description != null && c.Description.Contains(term)));
        }

        if (type.HasValue)
            query = query.Where(c => c.CourtType == type);

        query = sort switch
        {
            "rate_asc" => query.OrderBy(c => c.HourlyRate),
            "rate_desc" => query.OrderByDescending(c => c.HourlyRate),
            _ => query.OrderBy(c => c.CourtNumber)
        };

        var vm = new CourtIndexViewModel
        {
            Search = search,
            Type = type,
            Sort = sort,
            Courts = await query.ToListAsync()
        };
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
