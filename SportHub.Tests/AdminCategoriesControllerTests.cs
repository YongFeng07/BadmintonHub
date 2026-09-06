using SportHub.Controllers;
using SportHub.Data;
using SportHub.Models;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace SportHub.Tests;

/// <summary>
/// P3 facility-category maintenance: CRUD, name uniqueness and the delete guard
/// (a category that owns facilities cannot be deleted).
/// </summary>
public class AdminCategoriesControllerTests
{
    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static (AdminCategoriesController Controller, ApplicationDbContext Db) CreateController()
    {
        var db = TestDb.Create();
        var httpContext = new DefaultHttpContext();
        var controller = new AdminCategoriesController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider())
        };
        return (controller, db);
    }

    private static CategoryFormViewModel ValidModel(string name = "Tennis Court") => new()
    {
        Name = name,
        UnitLabel = "Court",
        Icon = "🎾",
        DisplayOrder = 5,
        Status = CategoryStatus.Active
    };

    private static Category AddCategory(ApplicationDbContext db, string name, int order = 1)
    {
        var category = new Category { Name = name, UnitLabel = "Court", DisplayOrder = order, Status = CategoryStatus.Active };
        db.Categories.Add(category);
        db.SaveChanges();
        return category;
    }

    [Fact]
    public async Task Index_ListsCategoriesOrderedByDisplayOrder()
    {
        var (controller, db) = CreateController();
        AddCategory(db, "Zebra", 2);
        AddCategory(db, "Alpha", 1);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CategoryFormViewModel>(view.Model);
        Assert.Equal("Alpha", vm.Categories[0].Name);
        Assert.Equal("Zebra", vm.Categories[1].Name);
    }

    [Fact]
    public async Task Create_ValidCategory_PersistsAndRedirects()
    {
        var (controller, db) = CreateController();

        var result = await controller.Create(ValidModel());

        Assert.IsType<RedirectToActionResult>(result);
        var created = db.Categories.Single(c => c.Name == "Tennis Court");
        Assert.Equal("Court", created.UnitLabel);
        Assert.Equal("🎾", created.Icon);
        Assert.Equal(CategoryStatus.Active, created.Status);
    }

    [Fact]
    public async Task Create_DuplicateName_IsRejected()
    {
        var (controller, db) = CreateController();
        AddCategory(db, "Tennis Court");

        var result = await controller.Create(ValidModel());

        var view = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(CategoryFormViewModel.Name)));
        Assert.Single(db.Categories);
    }

    [Fact]
    public async Task Edit_UpdatesCategory()
    {
        var (controller, db) = CreateController();
        var category = AddCategory(db, "Old Name");

        var result = await controller.Edit(new CategoryFormViewModel
        {
            Id = category.Id,
            Name = "New Name",
            UnitLabel = "Lane",
            DisplayOrder = 9,
            Status = CategoryStatus.Inactive
        });

        Assert.IsType<RedirectToActionResult>(result);
        var updated = db.Categories.Single();
        Assert.Equal("New Name", updated.Name);
        Assert.Equal("Lane", updated.UnitLabel);
        Assert.Equal(9, updated.DisplayOrder);
        Assert.Equal(CategoryStatus.Inactive, updated.Status);
    }

    [Fact]
    public async Task Edit_DuplicateName_IsRejected()
    {
        var (controller, db) = CreateController();
        var category = AddCategory(db, "Keep Me");
        AddCategory(db, "Taken");

        var result = await controller.Edit(new CategoryFormViewModel
        {
            Id = category.Id,
            Name = "Taken",
            UnitLabel = "Court",
            DisplayOrder = 1
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(CategoryFormViewModel.Name)));
        Assert.Equal("Keep Me", db.Categories.Single(c => c.Id == category.Id).Name);
    }

    [Fact]
    public async Task Delete_BlockedWhenFacilitiesExist()
    {
        var (controller, db) = CreateController();
        var category = AddCategory(db, "Occupied");
        var facility = db.Facilities.Single(); // TestDb's base facility
        facility.CategoryId = category.Id;
        db.SaveChanges();

        var result = await controller.DeleteConfirmed(category.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(db.Categories); // still there
    }

    [Fact]
    public async Task Delete_WorksWhenCategoryIsEmpty()
    {
        var (controller, db) = CreateController();
        var category = AddCategory(db, "Lonely");

        var result = await controller.DeleteConfirmed(category.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(db.Categories);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        var (controller, _) = CreateController();

        var result = await controller.DeleteConfirmed(9999);

        Assert.IsType<NotFoundResult>(result);
    }
}
