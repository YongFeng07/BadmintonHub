using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

/// <summary>
/// User administration (M4): search, filter by role/status, pagination,
/// activate/deactivate, and failed-login lockout management (M3 security feature).
/// </summary>
[Authorize(Roles = "Admin")]
public class AdminUsersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAccountService _accountService;

    public AdminUsersController(ApplicationDbContext db, IAccountService accountService)
    {
        _db = db;
        _accountService = accountService;
    }

    public async Task<IActionResult> Index(string? search, string? role, string? status, int page = 1)
    {
        page = Math.Max(1, page);
        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<Role>(role, true, out var parsedRole))
            query = query.Where(u => u.Role == parsedRole);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<UserStatus>(status, true, out var parsedStatus))
            query = query.Where(u => u.Status == parsedStatus);

        var total = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .Skip((page - 1) * AdminUsersIndexViewModel.PageSize)
            .Take(AdminUsersIndexViewModel.PageSize)
            .ToListAsync();

        var model = new AdminUsersIndexViewModel
        {
            Users = users,
            Search = search,
            RoleFilter = role,
            StatusFilter = status,
            Page = page,
            TotalCount = total
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(int id)
    {
        var (success, error) = await _accountService.UnlockAsync(id);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] =
            success ? "Account unlocked." : (error ?? "Could not unlock the account.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Activate or deactivate a member account (admins/staff cannot be deactivated).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, string status)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        if (user.Role != Role.Member)
        {
            TempData["ErrorMessage"] = "Only member accounts can be deactivated.";
            return RedirectToAction(nameof(Index));
        }

        if (!Enum.TryParse<UserStatus>(status, true, out var newStatus))
        {
            TempData["ErrorMessage"] = "Invalid status.";
            return RedirectToAction(nameof(Index));
        }

        user.Status = newStatus;
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{user.FullName} is now {newStatus}.";
        return RedirectToAction(nameof(Index));
    }
}
