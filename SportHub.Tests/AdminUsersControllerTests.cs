using SportHub.Controllers;
using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// P2 member maintenance: admin edit of member profiles (with email
/// uniqueness), member activity details, photo upload, and the rule that
/// only member accounts can be deactivated here.
/// </summary>
public class AdminUsersControllerTests
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

    private static (AdminUsersController Controller, ApplicationDbContext Db) CreateController()
    {
        var db = TestDb.Create();
        var httpContext = new DefaultHttpContext();
        var controller = new AdminUsersController(db, new AccountService(db), new FakeImageService())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider())
        };
        return (controller, db);
    }

    [Fact]
    public async Task Edit_UpdatesMemberProfile()
    {
        var (controller, db) = CreateController();
        var member = db.Users.Single(u => u.Email == "member@test.local");
        var model = new AdminUserEditViewModel
        {
            Id = member.Id,
            FullName = "Renamed Member",
            Email = "member-renamed@test.local",
            Phone = "012-777 8888",
            Role = member.Role,
            Status = member.Status,
            PhotoUrl = member.PhotoUrl
        };

        var result = await controller.Edit(model);

        Assert.IsType<RedirectToActionResult>(result);
        var updated = db.Users.Single(u => u.Id == member.Id);
        Assert.Equal("Renamed Member", updated.FullName);
        Assert.Equal("member-renamed@test.local", updated.Email);
        Assert.Equal("012-777 8888", updated.Phone);
    }

    [Fact]
    public async Task Edit_DuplicateEmail_ReturnsViewWithError()
    {
        var (controller, db) = CreateController();
        var member = db.Users.Single(u => u.Email == "member@test.local");
        var model = new AdminUserEditViewModel
        {
            Id = member.Id,
            FullName = member.FullName,
            Email = "admin@test.local", // taken by someone else
            Phone = member.Phone,
            Role = member.Role,
            Status = member.Status,
            PhotoUrl = member.PhotoUrl
        };

        var result = await controller.Edit(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Contains(nameof(model.Email), view.ViewData.ModelState.Keys);
        Assert.Equal("member@test.local", db.Users.Single(u => u.Id == member.Id).Email); // unchanged
    }

    [Fact]
    public async Task Details_ShowsMemberActivityStats()
    {
        var (controller, db) = CreateController();
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var courtId = db.Courts.Single().Id;
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var service = new ReservationService(db, new CourtService(db), new NoopEmailSender());

        var (_, _, confirmed) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(9, 0), 1, null);
        await service.MarkPaidAsync(confirmed!.Id, memberId, PaymentMethod.Cash, null);

        var (_, _, cancelled) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(11, 0), 1, null);
        cancelled!.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();

        var result = await controller.Details(memberId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminUserDetailsViewModel>(view.Model);
        Assert.Equal(2, model.ReservationCount);
        Assert.Equal(1, model.ConfirmedCount);
        Assert.Equal(1, model.CancelledCount);
        Assert.Equal(25m, model.TotalPaid);
    }

    [Fact]
    public async Task SetStatus_AdminAccount_IsRefused()
    {
        var (controller, db) = CreateController();
        var admin = db.Users.Single(u => u.Email == "admin@test.local");

        await controller.SetStatus(admin.Id, "Deactivated");

        Assert.Equal("Only member accounts can be deactivated.", controller.TempData["ErrorMessage"]);
        Assert.Equal(UserStatus.Active, db.Users.Single(u => u.Id == admin.Id).Status);
    }

    [Fact]
    public async Task UploadPhoto_SavesPhotoUrl()
    {
        var (controller, db) = CreateController();
        var member = db.Users.Single(u => u.Email == "member@test.local");

        await controller.UploadPhoto(member.Id, new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "photo", "a.png"));

        Assert.Equal("/uploads/profiles/test-" + member.Id + ".jpg",
            db.Users.Single(u => u.Id == member.Id).PhotoUrl);
        Assert.NotNull(controller.TempData["SuccessMessage"]);
    }
}
