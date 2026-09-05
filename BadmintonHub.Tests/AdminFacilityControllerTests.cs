using BadmintonHub.Controllers;
using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Tests;

/// <summary>
/// P3 multi-facility maintenance: category assignment, settings editing and the
/// delete guard (a facility that still owns courts cannot be deleted; its photo
/// files are cleaned up through the image service).
/// </summary>
public class AdminFacilityControllerTests
{
    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class FakeImageService : IImageService
    {
        public List<string> DeletedFacilityPaths { get; } = new();

        public (string? Error, string? RelativePath) SaveProfilePhoto(IFormFile file, int userId)
            => (null, $"/uploads/profiles/test-{userId}.jpg");
        public void DeleteProfilePhoto(string? relativePath) { }
        public (string? Error, string? RelativePath) SaveFacilityPhoto(IFormFile file, int facilityId)
            => (null, $"/uploads/facilities/test-{facilityId}.jpg");
        public void DeleteFacilityPhoto(string? relativePath)
        {
            if (relativePath != null) DeletedFacilityPaths.Add(relativePath);
        }
    }

    private static (AdminFacilityController Controller, ApplicationDbContext Db, FakeImageService Images) CreateController()
    {
        var db = TestDb.Create();
        var httpContext = new DefaultHttpContext();
        var images = new FakeImageService();
        var controller = new AdminFacilityController(db, images)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider())
        };
        return (controller, db, images);
    }

    private static Category AddCategory(ApplicationDbContext db, string name)
    {
        var category = new Category { Name = name, UnitLabel = "Court", DisplayOrder = 1, Status = CategoryStatus.Active };
        db.Categories.Add(category);
        db.SaveChanges();
        return category;
    }

    private static FacilityEditViewModel ValidModel(int categoryId, string name = "New Facility") => new()
    {
        CategoryId = categoryId,
        Name = name,
        Address = "9 Test Avenue",
        Phone = "012-999 8888",
        Email = "venue@test.local",
        OpeningTime = new TimeSpan(7, 0, 0),
        ClosingTime = new TimeSpan(22, 0, 0),
        OperatingDays = new List<string> { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" },
        Status = FacilityStatus.Open
    };

    [Fact]
    public async Task Index_ListsFacilitiesWithCategory()
    {
        var (controller, db, _) = CreateController();
        var cat = AddCategory(db, "Squash Court");
        db.Facilities.Add(new Facility
        {
            CategoryId = cat.Id,
            Name = "Squash Den",
            Address = "2 Test Street",
            OperatingDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun"
        });
        db.SaveChanges();

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var facilities = Assert.IsAssignableFrom<List<Facility>>(view.Model);
        Assert.Equal("Squash Court", facilities.Single(f => f.Name == "Squash Den").Category!.Name);
    }

    [Fact]
    public async Task Create_ValidFacility_PersistsWithCategory()
    {
        var (controller, db, _) = CreateController();
        var cat = AddCategory(db, "Gymnasium");

        var result = await controller.Create(ValidModel(cat.Id, "Iron Works"));

        Assert.IsType<RedirectToActionResult>(result);
        var created = db.Facilities.Single(f => f.Name == "Iron Works");
        Assert.Equal(cat.Id, created.CategoryId);
        Assert.Equal("Mon,Tue,Wed,Thu,Fri,Sat,Sun", created.OperatingDays);
    }

    [Fact]
    public async Task Create_InvalidCategory_IsRejected()
    {
        var (controller, db, _) = CreateController();

        var result = await controller.Create(ValidModel(9999));

        var view = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(FacilityEditViewModel.CategoryId)));
        Assert.Single(db.Facilities); // only TestDb's base facility
    }

    [Fact]
    public async Task Edit_UpdatesFacilitySettings()
    {
        var (controller, db, _) = CreateController();
        var cat = AddCategory(db, "Pickleball");
        var facility = db.Facilities.Single();
        facility.CategoryId = cat.Id;
        db.SaveChanges();

        var result = await controller.Edit(new FacilityEditViewModel
        {
            Id = facility.Id,
            CategoryId = cat.Id,
            Name = "Pickle Palace",
            Address = facility.Address,
            OpeningTime = new TimeSpan(9, 0, 0),
            ClosingTime = new TimeSpan(21, 0, 0),
            OperatingDays = new List<string> { "Sat", "Sun" },
            Status = FacilityStatus.Closed
        });

        Assert.IsType<RedirectToActionResult>(result);
        var updated = db.Facilities.Single();
        Assert.Equal("Pickle Palace", updated.Name);
        Assert.Equal("Sat,Sun", updated.OperatingDays);
        Assert.Equal(FacilityStatus.Closed, updated.Status);
    }

    [Fact]
    public async Task Delete_BlockedWhenFacilityHasCourts()
    {
        var (controller, db, _) = CreateController();
        var facility = db.Facilities.Single(); // TestDb seeds one court into it

        var result = await controller.DeleteConfirmed(facility.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(db.Facilities); // still there
    }

    [Fact]
    public async Task Delete_RemovesFacilityAndDeletesPhotoFiles()
    {
        var (controller, db, images) = CreateController();
        var cat = AddCategory(db, "Volleyball Court");
        var facility = new Facility
        {
            CategoryId = cat.Id,
            Name = "Empty Hall",
            Address = "3 Test Street",
            OperatingDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun"
        };
        db.Facilities.Add(facility);
        db.SaveChanges();
        db.FacilityPhotos.Add(new FacilityPhoto { FacilityId = facility.Id, FilePath = "/uploads/facilities/3-abc.jpg", DisplayOrder = 1, IsPrimary = true });
        db.SaveChanges();

        var result = await controller.DeleteConfirmed(facility.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(db.FacilityPhotos);
        Assert.Contains("/uploads/facilities/3-abc.jpg", images.DeletedFacilityPaths);
        Assert.Null(await db.Facilities.SingleOrDefaultAsync(f => f.Id == facility.Id));
    }

    [Fact]
    public async Task UploadPhotos_SavesViaImageService()
    {
        var (controller, db, _) = CreateController();
        var facility = db.Facilities.Single();
        var file = new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "photo", "cover.png");

        var result = await controller.UploadPhotos(facility.Id, new List<IFormFile> { file });

        Assert.IsType<RedirectToActionResult>(result);
        var photo = db.FacilityPhotos.Single();
        Assert.Equal("/uploads/facilities/test-" + facility.Id + ".jpg", photo.FilePath);
        Assert.True(photo.IsPrimary); // first photo becomes the cover
    }
}
