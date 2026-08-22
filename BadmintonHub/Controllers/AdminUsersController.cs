using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

/// <summary>
/// M3 security feature: failed-login lockout management (unlock).
/// The full user administration (search/filter/pagination) is added in the M4 phase.
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

    public async Task<IActionResult> Index()
    {
        var users = await _db.Users
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToListAsync();
        return View(users);
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
}
