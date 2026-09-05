using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;

    public HomeController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var vm = new HomeViewModel
        {
            Facility = await _db.Facilities.FirstOrDefaultAsync(),
            FeaturedCourts = await _db.Courts
                .Include(c => c.Photos.OrderBy(p => p.DisplayOrder))
                .OrderBy(c => c.FacilityId).ThenBy(c => c.CourtNumber)
                .Take(6)
                .ToListAsync(),
            CourtCount = await _db.Courts.CountAsync(),
            MemberCount = await _db.Users.CountAsync(u => u.Role == Role.Member),
            TodayReservations = await _db.Reservations.CountAsync(r =>
                r.ReservationDate == today &&
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
        };
        return View(vm);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
