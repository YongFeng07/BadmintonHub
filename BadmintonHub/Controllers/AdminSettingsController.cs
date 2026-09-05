using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

/// <summary>
/// System settings (revised spec: a SuperAdmin duty). Admin accounts are
/// explicitly excluded — SuperAdmin and Admin really do have different permissions.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class AdminSettingsController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminSettingsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var model = new SystemSettingsViewModel
        {
            SiteName = await GetSettingAsync("SiteName") ?? "BadmintonHub",
            SiteAnnouncement = await GetSettingAsync("SiteAnnouncement")
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SystemSettingsViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await SetSettingAsync("SiteName", model.SiteName.Trim());
        await SetSettingAsync("SiteAnnouncement",
            string.IsNullOrWhiteSpace(model.SiteAnnouncement) ? null : model.SiteAnnouncement.Trim());
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "System settings saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string?> GetSettingAsync(string key) =>
        (await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key))?.Value;

    private async Task SetSettingAsync(string key, string? value)
    {
        var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
        {
            _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTime.Now;
        }
    }
}
