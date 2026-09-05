using BadmintonHub.Controllers;
using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BadmintonHub.Tests;

/// <summary>
/// P2 SuperAdmin account maintenance: create/edit/reset-password and the
/// deactivation guard rails (self-deactivation, last active SuperAdmin).
/// </summary>
public class AdminAccountsControllerTests
{
    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class FakeImageService : IImageService
    {
        public (string? Error, string? RelativePath) SaveProfilePhoto(IFormFile file, int userId)
            => (null, $"/uploads/profiles/test-{userId}.jpg");
        public void DeleteProfilePhoto(string? relativePath) { }
        public (string? Error, string? RelativePath) SaveFacilityPhoto(IFormFile file, int facilityId)
            => (null, $"/uploads/facilities/test-{facilityId}.jpg");
        public void DeleteFacilityPhoto(string? relativePath) { }
    }

    /// <summary>Creates the controller with the named user as the signed-in claimant.</summary>
    private static (AdminAccountsController Controller, ApplicationDbContext Db) CreateController(string claimantEmail)
    {
        var db = TestDb.Create();
        var httpContext = new DefaultHttpContext();
        var controller = new AdminAccountsController(db, new FakeImageService())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider())
        };

        var claimantId = db.Users.Single(u => u.Email == claimantEmail).Id;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, claimantId.ToString())
        }));

        return (controller, db);
    }

    private static AdminAccountCreateViewModel ValidCreateModel() => new()
    {
        FullName = "New Admin",
        Email = "newadmin@test.local",
        Phone = "012-111 2222",
        Role = Role.Admin,
        Password = "NewAdmin@123",
        ConfirmPassword = "NewAdmin@123"
    };

    [Fact]
    public async Task Create_ValidAdmin_AddsActiveVerifiedAccount()
    {
        var (controller, db) = CreateController("superadmin@test.local");

        var result = await controller.Create(ValidCreateModel());

        Assert.IsType<RedirectToActionResult>(result);
        var created = db.Users.Single(u => u.Email == "newadmin@test.local");
        Assert.Equal(Role.Admin, created.Role);
        Assert.Equal(UserStatus.Active, created.Status);
        Assert.True(created.EmailVerified); // provisioned by a SuperAdmin — no verification gate
        Assert.True(PasswordHelper.Verify("NewAdmin@123", created.PasswordHash, created.PasswordSalt));
    }

    [Fact]
    public async Task Create_DuplicateEmail_ReturnsViewWithError()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var model = ValidCreateModel();
        model.Email = "member@test.local";

        var result = await controller.Create(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Contains(nameof(model.Email), view.ViewData.ModelState.Keys);
        Assert.DoesNotContain(db.Users, u => u.FullName == "New Admin"); // nothing was added
    }

    [Fact]
    public async Task Create_WeakPassword_ReturnsViewWithError()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var model = ValidCreateModel();
        model.Password = model.ConfirmPassword = "short";

        var result = await controller.Create(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Contains(nameof(model.Password), view.ViewData.ModelState.Keys);
    }

    [Fact]
    public async Task Edit_UpdatesProfile_AndKeepsRoleAndStatus()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var target = db.Users.Single(u => u.Email == "admin2@test.local");
        var model = new AdminAccountEditViewModel
        {
            Id = target.Id,
            FullName = "Renamed Admin",
            Email = "admin2-renamed@test.local",
            Phone = "012-999 0000",
            Role = target.Role,
            Status = target.Status,
            PhotoUrl = target.PhotoUrl
        };

        var result = await controller.Edit(model);

        Assert.IsType<RedirectToActionResult>(result);
        var updated = db.Users.Single(u => u.Id == target.Id);
        Assert.Equal("Renamed Admin", updated.FullName);
        Assert.Equal("admin2-renamed@test.local", updated.Email);
        Assert.Equal("012-999 0000", updated.Phone);
        Assert.Equal(Role.Admin, updated.Role); // role is never editable here
    }

    [Fact]
    public async Task Edit_DuplicateEmail_ReturnsViewWithError()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var target = db.Users.Single(u => u.Email == "admin2@test.local");
        var model = new AdminAccountEditViewModel
        {
            Id = target.Id,
            FullName = target.FullName,
            Email = "admin@test.local", // belongs to someone else
            Phone = target.Phone,
            Role = target.Role,
            Status = target.Status,
            PhotoUrl = target.PhotoUrl
        };

        var result = await controller.Edit(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Contains(nameof(model.Email), view.ViewData.ModelState.Keys);
        Assert.Equal("admin2@test.local", db.Users.Single(u => u.Id == target.Id).Email); // unchanged
    }

    [Fact]
    public async Task ResetPassword_Valid_ResetsAndClearsLockout()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var target = db.Users.Single(u => u.Email == "admin2@test.local");
        target.FailedLoginAttempts = 3;
        target.LockoutEnd = DateTime.UtcNow.AddMinutes(10);
        await db.SaveChangesAsync();

        var result = await controller.ResetPassword(new AdminResetPasswordViewModel
        {
            Id = target.Id,
            NewPassword = "BrandNew@123",
            ConfirmPassword = "BrandNew@123"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var updated = db.Users.Single(u => u.Id == target.Id);
        Assert.True(PasswordHelper.Verify("BrandNew@123", updated.PasswordHash, updated.PasswordSalt));
        Assert.Equal(0, updated.FailedLoginAttempts);
        Assert.Null(updated.LockoutEnd);
    }

    [Fact]
    public async Task ResetPassword_Weak_IsRejected()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var target = db.Users.Single(u => u.Email == "admin2@test.local");
        var originalHash = target.PasswordHash;

        await controller.ResetPassword(new AdminResetPasswordViewModel
        {
            Id = target.Id,
            NewPassword = "weak",
            ConfirmPassword = "weak"
        });

        Assert.NotNull(controller.TempData["ErrorMessage"]);
        Assert.Equal(originalHash, db.Users.Single(u => u.Id == target.Id).PasswordHash); // untouched
    }

    [Fact]
    public async Task SetStatus_DeactivateSelf_IsRefused()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var self = db.Users.Single(u => u.Email == "superadmin@test.local");

        await controller.SetStatus(self.Id, "Deactivated");

        Assert.Equal("You cannot deactivate your own account.", controller.TempData["ErrorMessage"]);
        Assert.Equal(UserStatus.Active, db.Users.Single(u => u.Id == self.Id).Status);
    }

    [Fact]
    public async Task SetStatus_DeactivateLastActiveSuperAdmin_IsRefused()
    {
        // The controller action is [Authorize(SuperAdmin)] at runtime; the unit test
        // claims an ordinary admin so the last-SuperAdmin guard is what fires.
        var (controller, db) = CreateController("admin@test.local");
        var superAdmin = db.Users.Single(u => u.Email == "superadmin@test.local");

        await controller.SetStatus(superAdmin.Id, "Deactivated");

        Assert.Equal("The last active SuperAdmin account cannot be deactivated.", controller.TempData["ErrorMessage"]);
        Assert.Equal(UserStatus.Active, db.Users.Single(u => u.Id == superAdmin.Id).Status);
    }

    [Fact]
    public async Task SetStatus_DeactivateOtherAdmin_Works()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var target = db.Users.Single(u => u.Email == "admin2@test.local");

        await controller.SetStatus(target.Id, "Deactivated");

        Assert.NotNull(controller.TempData["SuccessMessage"]);
        Assert.Equal(UserStatus.Deactivated, db.Users.Single(u => u.Id == target.Id).Status);
    }

    [Fact]
    public async Task SetStatus_InvalidStatus_SetsError()
    {
        var (controller, db) = CreateController("superadmin@test.local");
        var target = db.Users.Single(u => u.Email == "admin2@test.local");

        await controller.SetStatus(target.Id, "Bogus");

        Assert.Equal("Invalid status.", controller.TempData["ErrorMessage"]);
        Assert.Equal(UserStatus.Active, db.Users.Single(u => u.Id == target.Id).Status);
    }
}
