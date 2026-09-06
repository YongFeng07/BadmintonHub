using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Controllers;

/// <summary>
/// Admin-account management (revised spec, P2): SuperAdmin-only CRUD for the
/// Admin/SuperAdmin roles — create, edit profile, reset password, activate /
/// deactivate. Member accounts are maintained in AdminUsersController instead.
/// Guard rails: nobody can deactivate themselves, and the last active
/// SuperAdmin can never be deactivated (that would lock the system out).
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class AdminAccountsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IImageService _imageService;

    public AdminAccountsController(ApplicationDbContext db, IImageService imageService)
    {
        _db = db;
        _imageService = imageService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    public async Task<IActionResult> Index(string? search)
    {
        var query = _db.Users
            .Where(u => u.Role == Role.Admin || u.Role == Role.SuperAdmin)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        }

        var model = new AdminAccountIndexViewModel
        {
            Accounts = await query.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync(),
            Search = search
        };
        return View(model);
    }

    public IActionResult Create() => View(new AdminAccountCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminAccountCreateViewModel model)
    {
        var normalizedEmail = model.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            ModelState.AddModelError(nameof(model.Email), "This email address is already registered.");

        var passwordError = PasswordHelper.ValidatePassword(model.Password);
        if (passwordError != null)
            ModelState.AddModelError(nameof(model.Password), passwordError);

        if (!ModelState.IsValid)
            return View(model);

        var (hash, salt) = PasswordHelper.HashPassword(model.Password);
        _db.Users.Add(new User
        {
            FullName = model.FullName.Trim(),
            Email = model.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = model.Role,
            Status = UserStatus.Active,
            // Admin-provisioned accounts are trusted — no verification gate.
            EmailVerified = true
        });
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Admin account {model.Email.Trim()} was created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var account = await AdminAccountOrDefault(id);
        if (account == null) return NotFound();

        return View(new AdminAccountEditViewModel
        {
            Id = account.Id,
            FullName = account.FullName,
            Email = account.Email,
            Phone = account.Phone,
            Role = account.Role,
            Status = account.Status,
            PhotoUrl = account.PhotoUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AdminAccountEditViewModel model)
    {
        var account = await AdminAccountOrDefault(model.Id);
        if (account == null) return NotFound();

        var normalizedEmail = model.Email.Trim().ToLowerInvariant();
        var duplicate = await _db.Users
            .AnyAsync(u => u.Id != account.Id && u.Email.ToLower() == normalizedEmail);
        if (duplicate)
            ModelState.AddModelError(nameof(model.Email), "This email address is already used by another account.");

        if (!ModelState.IsValid)
        {
            model.Role = account.Role;
            model.Status = account.Status;
            model.PhotoUrl = account.PhotoUrl;
            return View(model);
        }

        account.FullName = model.FullName.Trim();
        account.Email = model.Email.Trim();
        account.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        account.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Account {account.Email} was updated.";
        return RedirectToAction(nameof(Edit), new { id = account.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(AdminResetPasswordViewModel model)
    {
        var account = await AdminAccountOrDefault(model.Id);
        if (account == null) return NotFound();

        var passwordError = PasswordHelper.ValidatePassword(model.NewPassword);
        if (!ModelState.IsValid || passwordError != null)
        {
            TempData["ErrorMessage"] = passwordError ?? "The new password is not valid.";
            return RedirectToAction(nameof(Edit), new { id = account.Id });
        }

        var (hash, salt) = PasswordHelper.HashPassword(model.NewPassword);
        account.PasswordHash = hash;
        account.PasswordSalt = salt;
        account.FailedLoginAttempts = 0;
        account.LockoutEnd = null;
        account.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"The password for {account.Email} was reset.";
        return RedirectToAction(nameof(Edit), new { id = account.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, string status)
    {
        var account = await AdminAccountOrDefault(id);
        if (account == null) return NotFound();

        if (!Enum.TryParse<UserStatus>(status, true, out var newStatus))
        {
            TempData["ErrorMessage"] = "Invalid status.";
            return RedirectToAction(nameof(Index));
        }

        // Guard rails: you cannot deactivate yourself, and the last active
        // SuperAdmin must always survive so the system stays administrable.
        if (newStatus == UserStatus.Deactivated && account.Id == CurrentUserId)
        {
            TempData["ErrorMessage"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Index));
        }

        if (newStatus == UserStatus.Deactivated && account.Role == Role.SuperAdmin)
        {
            var activeSuperAdmins = await _db.Users.CountAsync(u =>
                u.Role == Role.SuperAdmin && u.Status == UserStatus.Active);
            if (activeSuperAdmins <= 1)
            {
                TempData["ErrorMessage"] = "The last active SuperAdmin account cannot be deactivated.";
                return RedirectToAction(nameof(Index));
            }
        }

        account.Status = newStatus;
        account.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{account.FullName} is now {newStatus}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile photo)
    {
        var account = await AdminAccountOrDefault(id);
        if (account == null) return NotFound();

        var (error, path) = _imageService.SaveProfilePhoto(photo, account.Id);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Edit), new { id });
        }

        _imageService.DeleteProfilePhoto(account.PhotoUrl);
        account.PhotoUrl = path;
        account.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{account.FullName}'s photo was updated.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePhoto(int id)
    {
        var account = await AdminAccountOrDefault(id);
        if (account == null) return NotFound();

        _imageService.DeleteProfilePhoto(account.PhotoUrl);
        account.PhotoUrl = null;
        account.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{account.FullName}'s photo was removed.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<User?> AdminAccountOrDefault(int id) =>
        await _db.Users.FirstOrDefaultAsync(u =>
            u.Id == id && (u.Role == Role.Admin || u.Role == Role.SuperAdmin));
}
