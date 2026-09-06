using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Controllers;

/// <summary>
/// User administration (revised spec): search, filter by role/status, pagination,
/// activate/deactivate, email verification, and failed-login lockout management.
/// Member management is an Admin duty; the separate admin-account CRUD lives in
/// AdminAccountsController (SuperAdmin only).
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminUsersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAccountService _accountService;
    private readonly IImageService _imageService;

    public AdminUsersController(ApplicationDbContext db, IAccountService accountService, IImageService imageService)
    {
        _db = db;
        _accountService = accountService;
        _imageService = imageService;
    }

    // G-M6: AJAX search/sort/paging; role/status filters carried through every link.
    public async Task<IActionResult> Index(string? search, string? role, string? status,
        string? sort, string? dir, int page = 1, int size = 10)
    {
        var request = AjaxListRequest.From(Request.Query);
        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(u => u.FullName.Contains(request.Search) || u.Email.Contains(request.Search));

        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<Role>(role, true, out var parsedRole))
            query = query.Where(u => u.Role == parsedRole);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<UserStatus>(status, true, out var parsedStatus))
            query = query.Where(u => u.Status == parsedStatus);

        query = (request.Sort, request.Descending) switch
        {
            ("name", false) => query.OrderBy(u => u.FullName),
            ("name", true) => query.OrderByDescending(u => u.FullName),
            ("email", false) => query.OrderBy(u => u.Email),
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("role", false) => query.OrderBy(u => u.Role).ThenBy(u => u.FullName),
            ("role", true) => query.OrderByDescending(u => u.Role).ThenBy(u => u.FullName),
            ("status", false) => query.OrderBy(u => u.Status).ThenBy(u => u.FullName),
            ("status", true) => query.OrderByDescending(u => u.Status).ThenBy(u => u.FullName),
            ("failed", false) => query.OrderBy(u => u.FailedLoginAttempts).ThenBy(u => u.FullName),
            ("failed", true) => query.OrderByDescending(u => u.FailedLoginAttempts),
            ("lastlogin", false) => query.OrderBy(u => u.LastLoginAt).ThenBy(u => u.FullName),
            ("lastlogin", true) => query.OrderByDescending(u => u.LastLoginAt),
            _ => query.OrderBy(u => u.Role).ThenBy(u => u.FullName)
        };

        var total = await query.CountAsync();
        var pager = AjaxPager.For(request, total);
        pager.Extra["role"] = role ?? "";
        pager.Extra["status"] = status ?? "";

        var model = new AdminUsersIndexViewModel
        {
            RoleFilter = role,
            StatusFilter = status,
            LockedCount = await query.CountAsync(u => u.LockoutEnd.HasValue && u.LockoutEnd > DateTime.Now),
            Page = new AjaxListPage<User>
            {
                Items = await query.Skip((pager.Page - 1) * pager.PageSize).Take(pager.PageSize).ToListAsync(),
                Pager = pager
            }
        };

        if (Request.IsAjaxListRequest())
            return PartialView("_UserTable", model);
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

    /// <summary>Manually verifies a member's email (revised spec: email verification).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        user.EmailVerified = true;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationExpiresUtc = null;
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{user.FullName}'s email is now verified.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Activate or deactivate a member account (admin accounts cannot be deactivated).</summary>
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

    // ---------- Member maintenance (P2): edit + details ----------

    public async Task<IActionResult> Edit(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        return View(new AdminUserEditViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            Role = user.Role,
            Status = user.Status,
            PhotoUrl = user.PhotoUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AdminUserEditViewModel model)
    {
        var user = await _db.Users.FindAsync(model.Id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        var normalizedEmail = model.Email.Trim().ToLowerInvariant();
        var duplicate = await _db.Users
            .AnyAsync(u => u.Id != user.Id && u.Email.ToLower() == normalizedEmail);
        if (duplicate)
            ModelState.AddModelError(nameof(model.Email), "This email address is already used by another account.");

        if (!ModelState.IsValid)
        {
            model.Role = user.Role;
            model.Status = user.Status;
            model.PhotoUrl = user.PhotoUrl;
            return View(model);
        }

        user.FullName = model.FullName.Trim();
        user.Email = model.Email.Trim();
        user.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{user.FullName}'s profile was updated.";
        return RedirectToAction(nameof(Edit), new { id = user.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        var model = new AdminUserDetailsViewModel
        {
            User = user,
            ReservationCount = await _db.Reservations.CountAsync(r => r.UserId == id),
            ConfirmedCount = await _db.Reservations.CountAsync(r => r.UserId == id && r.Status == ReservationStatus.Confirmed),
            CancelledCount = await _db.Reservations.CountAsync(r => r.UserId == id && r.Status == ReservationStatus.Cancelled),
            TotalPaid = await _db.Payments
                .Where(p => p.UserId == id && p.Status == PaymentStatus.Paid)
                .SumAsync(p => (decimal?)p.Amount) ?? 0
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile photo)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        var (error, path) = _imageService.SaveProfilePhoto(photo, user.Id);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Edit), new { id });
        }

        _imageService.DeleteProfilePhoto(user.PhotoUrl);
        user.PhotoUrl = path;
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{user.FullName}'s photo was updated.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePhoto(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        _imageService.DeleteProfilePhoto(user.PhotoUrl);
        user.PhotoUrl = null;
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{user.FullName}'s photo was removed.";
        return RedirectToAction(nameof(Edit), new { id });
    }
}
