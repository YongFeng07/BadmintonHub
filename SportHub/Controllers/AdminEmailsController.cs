using SportHub.Data;
using SportHub.Models;
using SportHub.ViewModels;
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

    // G-M6: AJAX search/sort/paging over the whole mailbox (was hard-limited to 50).
    public async Task<IActionResult> Index(string? search, string? sort, string? dir, int page = 1, int size = 10)
    {
        var request = AjaxListRequest.From(Request.Query);
        var query = _db.DemoEmails.Include(e => e.Attachments).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(e => e.To.Contains(request.Search) || e.Subject.Contains(request.Search));

        query = (request.Sort, request.Descending) switch
        {
            ("to", false) => query.OrderBy(e => e.To),
            ("to", true) => query.OrderByDescending(e => e.To),
            ("subject", false) => query.OrderBy(e => e.Subject),
            ("subject", true) => query.OrderByDescending(e => e.Subject),
            ("date", false) => query.OrderBy(e => e.CreatedAt),
            _ => query.OrderByDescending(e => e.CreatedAt) // newest first
        };

        var total = await query.CountAsync();
        var pager = AjaxPager.For(request, total);
        var model = new AjaxListPage<DemoEmail>
        {
            Items = await query.Skip((pager.Page - 1) * pager.PageSize).Take(pager.PageSize).ToListAsync(),
            Pager = pager
        };

        if (Request.IsAjaxListRequest())
            return PartialView("_MailTable", model);
        return View(model);
    }

    // G-M6 batch deletion: clear selected demo mails (the in-app mailbox only —
    // no real emails are involved).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBatch(List<int> itemIds)
    {
        var ids = itemIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            TempData["ErrorMessage"] = "Select at least one mail to delete.";
            return RedirectToAction(nameof(Index));
        }

        var mails = await _db.DemoEmails.Where(e => ids.Contains(e.Id)).ToListAsync();
        _db.DemoEmails.RemoveRange(mails);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{mails.Count} mail(s) deleted.";
        return RedirectToAction(nameof(Index));
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
