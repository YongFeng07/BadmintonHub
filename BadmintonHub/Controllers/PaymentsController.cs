using BadmintonHub.Data;
using BadmintonHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BadmintonHub.Controllers;

/// <summary>Member payment history (M3). Staff payment operations live in the M4 phase.</summary>
[Authorize]
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _db;

    public PaymentsController(ApplicationDbContext db) => _db = db;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> Index()
    {
        var payments = await _db.Payments
            .Include(p => p.Reservation).ThenInclude(r => r!.Court)
            .Where(p => p.UserId == CurrentUserId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return View(payments);
    }
}
