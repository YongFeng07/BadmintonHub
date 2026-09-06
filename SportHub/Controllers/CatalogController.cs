using SportHub.Data;
using SportHub.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Controllers;

/// <summary>
/// Public facility catalog (revised spec): all facilities across the 11 categories,
/// with category/name filters, top-5 popularity and low-availability alerts.
/// </summary>
public class CatalogController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICatalogService _catalogService;

    public CatalogController(ApplicationDbContext db, ICatalogService catalogService)
    {
        _db = db;
        _catalogService = catalogService;
    }

    public async Task<IActionResult> Index(int? categoryId, string? search) =>
        View(await _catalogService.GetCatalogAsync(categoryId, search));

    public async Task<IActionResult> Details(int id)
    {
        var facility = await _db.Facilities
            .Include(f => f.Category)
            .Include(f => f.Photos.OrderBy(p => p.DisplayOrder))
            .Include(f => f.Courts.OrderBy(c => c.CourtNumber))
            .FirstOrDefaultAsync(f => f.Id == id);
        if (facility == null) return NotFound();

        // Link each unit to its public details/booking page.
        ViewBag.CourtIds = facility.Courts.Select(c => c.Id).ToList();
        return View(facility);
    }
}
