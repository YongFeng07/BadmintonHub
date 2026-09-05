using BadmintonHub.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

/// <summary>
/// Demo mail viewer: shows the emails captured in-app while no SMTP server is
/// configured (or when a real send failed). Lets the assignment demo present the
/// verification / reset emails exactly as they would arrive in a real inbox.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminEmailsController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminEmailsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var mails = await _db.DemoEmails
            .OrderByDescending(e => e.CreatedAt)
            .Take(50)
            .ToListAsync();
        return View(mails);
    }

    public async Task<IActionResult> Details(int id)
    {
        var mail = await _db.DemoEmails.FindAsync(id);
        if (mail == null) return NotFound();
        return View(mail);
    }
}
