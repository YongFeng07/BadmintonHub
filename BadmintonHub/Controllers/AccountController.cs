using System.Security.Claims;
using BadmintonHub.Data;
using BadmintonHub.Infrastructure;
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
    private readonly IEmailService _emailService;
    private readonly IImageService _imageService;
    private readonly ApplicationDbContext _db;

    public AccountController(IAuthService authService, IAccountService accountService, IEmailService emailService, IImageService imageService, ApplicationDbContext db)
    {
        _authService = authService;
        _accountService = accountService;
        _emailService = emailService;
        _imageService = imageService;
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ---------- Login ----------

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
    [ValidateCaptcha]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (user, error, verificationRequired) = await _authService.AuthenticateAsync(model.Email, model.Password, ip);

        if (user == null)
        {
            ViewData["ErrorMessage"] = error ?? "Invalid email or password.";
            ViewData["ShowResendVerification"] = verificationRequired;
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

        // Remember Me (revised spec): a persistent cookie that survives the browser
        // being closed, up to a 30-day absolute cap; without it the cookie follows the
        // normal 8-hour sliding expiry configured in Program.cs.
        var authProperties = new AuthenticationProperties { IsPersistent = model.RememberMe };
        if (model.RememberMe)
            authProperties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        // Role-based landing page after login.
        return user.Role switch
        {
            Role.Admin or Role.SuperAdmin => RedirectToAction("Index", "AdminDashboard"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    // ---------- Register (email verification, revised spec) ----------

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateCaptcha]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var (success, error, verificationToken) = await _accountService.RegisterAsync(model.FullName, model.Email, model.Phone, model.Password);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "Registration failed.");
            return View(model);
        }

        // New accounts must confirm their mailbox before signing in — no auto sign-in.
        var user = await _db.Users.FirstAsync(u => u.Email == model.Email.Trim());
        var verifyUrl = Url.Action(nameof(VerifyEmail), "Account", new { userId = user.Id, token = verificationToken }, Request.Scheme)!;
        var sent = await _emailService.SendAsync(user.Email, "Verify your BadmintonHub account",
            EmailTemplates.VerificationEmail(user.FullName, verifyUrl));

        return View("VerifyEmailSent", new VerifyEmailSentViewModel
        {
            Email = user.Email,
            Link = sent ? null : verifyUrl // demo fallback: show the link only when nothing was really sent
        });
    }

    // ---------- Email verification ----------

    [HttpGet]
    public async Task<IActionResult> VerifyEmail(int userId, string token)
    {
        var (success, error) = await _accountService.VerifyEmailAsync(userId, token);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] =
            success ? "Your email address is verified. You can now sign in." : (error ?? "Verification failed.");
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ResendVerification(string? email = null)
    {
        var model = new ResendVerificationViewModel { Email = email ?? string.Empty };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateCaptcha]
    public async Task<IActionResult> ResendVerification(ResendVerificationViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var (_, _, verificationToken) = await _accountService.RequestVerificationEmailAsync(model.Email);

        // Neutral outcome either way (anti-enumeration): unknown emails get the same page.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.Trim().ToLowerInvariant());
        if (user != null && verificationToken != null)
        {
            var verifyUrl = Url.Action(nameof(VerifyEmail), "Account", new { userId = user.Id, token = verificationToken }, Request.Scheme)!;
            var sent = await _emailService.SendAsync(user.Email, "Verify your BadmintonHub account",
                EmailTemplates.VerificationEmail(user.FullName, verifyUrl));
            return View("VerifyEmailSent", new VerifyEmailSentViewModel
            {
                Email = user.Email,
                Link = sent ? null : verifyUrl
            });
        }

        return View("VerifyEmailSent", new VerifyEmailSentViewModel
        {
            Email = user?.Email ?? model.Email.Trim(),
            AlreadyVerified = user?.EmailVerified == true
        });
    }

    // ---------- Profile ----------

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

    // ---------- Profile photo (P2 pipeline) ----------

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPhoto(IFormFile photo)
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        var (error, path) = _imageService.SaveProfilePhoto(photo, user.Id);
        if (error != null)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Profile));
        }

        _imageService.DeleteProfilePhoto(user.PhotoUrl); // remove the previous file
        user.PhotoUrl = path;
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Profile photo updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePhoto()
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        _imageService.DeleteProfilePhoto(user.PhotoUrl);
        user.PhotoUrl = null;
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Profile photo removed.";
        return RedirectToAction(nameof(Profile));
    }

    // ---------- Password reset ----------

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ValidateCaptcha]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var resetUrlBase = Url.Action(nameof(ResetPassword), "Account", null, Request.Scheme)!;
        var (_, _, resetLink) = await _accountService.RequestPasswordResetAsync(model.Email, resetUrlBase);

        // Neutral message shown whether or not the email exists (anti-enumeration).
        // The link is displayed only as a demo fallback when no real mail was sent.
        var sent = false;
        if (resetLink != null)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.Trim().ToLowerInvariant());
            if (user != null)
                sent = await _emailService.SendAsync(user.Email, "Reset your BadmintonHub password",
                    EmailTemplates.PasswordResetEmail(user.FullName, resetLink));
        }

        ViewBag.ResetLinkSent = true;
        ViewBag.ResetLink = sent ? null : resetLink;
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

    // ---------- Logout ----------

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
