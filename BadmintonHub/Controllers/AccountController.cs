using System.Security.Claims;
using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IAccountService _accountService;
    private readonly ApplicationDbContext _db;

    public AccountController(IAuthService authService, IAccountService accountService, ApplicationDbContext db)
    {
        _authService = authService;
        _accountService = accountService;
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (user, error) = await _authService.AuthenticateAsync(model.Email, model.Password, ip);

        if (user == null)
        {
            ViewData["ErrorMessage"] = error ?? "Invalid email or password.";
            return View(model);
        }

        // Manual cookie authentication: issue a signed cookie holding the user's claims.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties { IsPersistent = model.RememberMe };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        // Role-based landing page after login.
        return user.Role switch
        {
            Role.Admin or Role.Staff => RedirectToAction("Index", "AdminAvailability"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var (success, error) = await _accountService.RegisterAsync(model.FullName, model.Email, model.Phone, model.Password);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "Registration failed.");
            return View(model);
        }

        // Convenience: sign the new member in immediately.
        var user = await _db.Users.FirstAsync(u => u.Email == model.Email.Trim());
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        TempData["SuccessMessage"] = "Welcome to BadmintonHub! Your member account has been created.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        var model = new EditProfileViewModel { FullName = user.FullName, Phone = user.Phone };
        ViewBag.ChangePasswordModel = new ChangePasswordViewModel();
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(EditProfileViewModel model)
    {
        ViewBag.ChangePasswordModel = new ChangePasswordViewModel();
        if (!ModelState.IsValid)
            return View(model);

        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        user.FullName = model.FullName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        // Keep the name claim in the cookie up to date.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        TempData["SuccessMessage"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var user = await _db.Users.FindAsync(CurrentUserId);
            ViewBag.ChangePasswordModel = model;
            return View("Profile", new EditProfileViewModel { FullName = user?.FullName ?? string.Empty, Phone = user?.Phone });
        }

        var (success, error) = await _accountService.ChangePasswordAsync(CurrentUserId, model.CurrentPassword, model.NewPassword);
        if (!success)
        {
            var user = await _db.Users.FindAsync(CurrentUserId);
            ViewBag.ChangePasswordModel = model;
            ModelState.AddModelError(string.Empty, error ?? "Could not change the password.");
            return View("Profile", new EditProfileViewModel { FullName = user?.FullName ?? string.Empty, Phone = user?.Phone });
        }

        TempData["SuccessMessage"] = "Password changed successfully.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var resetUrlBase = Url.Action(nameof(ResetPassword), "Account", null, Request.Scheme)!;
        var (_, _, resetLink) = await _accountService.RequestPasswordResetAsync(model.Email, resetUrlBase);

        // Neutral message shown whether or not the email exists (anti-enumeration).
        ViewBag.ResetLinkSent = true;
        ViewBag.ResetLink = resetLink;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(int userId, string token)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return View("ResetPassword", new ResetPasswordViewModel { UserId = userId, Token = token });

        ViewBag.UserEmail = user.Email;
        return View("ResetPassword", new ResetPasswordViewModel { UserId = userId, Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var (success, error) = await _accountService.ResetPasswordAsync(model.UserId, model.Token, model.Password);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "Could not reset the password.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Password reset. You can now log in with your new password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
