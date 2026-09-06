using SportHub.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Controllers;

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
        var mail = await _db.DemoEmails
            .Include(e => e.Attachments)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (mail == null) return NotFound();
        return View(mail);
    }

    /// <summary>G-M5: downloads one attachment (e.g. the PDF e-receipt) from a demo email.</summary>
    public async Task<IActionResult> Attachment(int id)
    {
        var attachment = await _db.DemoEmailAttachments.FindAsync(id);
        if (attachment == null) return NotFound();
        return File(attachment.Data, attachment.ContentType, attachment.FileName);
    }
}
