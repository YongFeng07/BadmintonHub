using BadmintonHub.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

/// <summary>Public facility information page (about, hours, rules, contact).</summary>
public class FacilityController : Controller
{
    private readonly ApplicationDbContext _db;

    public FacilityController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var facility = await _db.Facilities
            .Include(f => f.Courts)
            .FirstOrDefaultAsync();

        if (facility == null)
            return NotFound();

        return View(facility);
    }
}
