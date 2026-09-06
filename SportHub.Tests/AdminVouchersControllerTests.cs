using SportHub.Controllers;
using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// P4 admin voucher maintenance: CRUD, code uniqueness, invalid-model re-render
/// and system-owned stats (UsageCount/CreatedAt) kept out of the create copy.
/// </summary>
public class AdminVouchersControllerTests
{
    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static (AdminVouchersController Controller, ApplicationDbContext Db) CreateController(string query = "")
    {
        var db = TestDb.Create();
        var httpContext = new DefaultHttpContext();
        // G-M6 list actions read AjaxListRequest.From(Request.Query), so the
        // query string is where filters/sorts must live in tests too. The
        // explicit QueryFeature is the recipe that makes Request.Query parse it.
        httpContext.Features.Set<IQueryFeature>(new QueryFeature(httpContext.Features));
        httpContext.Request.QueryString = QueryString.FromUriComponent(query);
        var controller = new AdminVouchersController(new VoucherService(db), db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider())
        };
        return (controller, db);
    }

    private static Voucher AddVoucher(ApplicationDbContext db, string code = "SUMMER10")
    {
        var voucher = new Voucher
        {
            Code = code,
            Description = "Test voucher",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30)
        };
        db.Vouchers.Add(voucher);
        db.SaveChanges();
        return voucher;
    }

    private static Voucher ValidModel(string code = "SUMMER10") => new()
    {
        Code = code,
        Description = "Summer deal",
        DiscountType = DiscountType.FixedAmount,
        DiscountValue = 5m,
        ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30),
        Status = VoucherStatus.Active
    };

    [Fact]
    public async Task Index_ListsVouchersNewestFirst()
    {
        var (controller, db) = CreateController();
        AddVoucher(db, "OLDER1");
        AddVoucher(db, "NEWER1");

        var result = await controller.Index(null, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminVoucherIndexViewModel>(view.Model);
        Assert.Equal(new[] { "NEWER1", "OLDER1" }, vm.Page.Items.Select(v => v.Code));
    }

    [Fact]
    public async Task Index_SearchFiltersByCodeOrDescription()
    {
        // Note: SQL Server collation makes the live search case-insensitive;
        // the InMemory provider compares exactly, so match case here.
        var (controller, db) = CreateController("?search=SUMMER");
        AddVoucher(db, "SUMMER10");
        AddVoucher(db, "WINTER5");

        var result = await controller.Index(null, null, null, null);

        var vm = Assert.IsType<AdminVoucherIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("SUMMER10", Assert.Single(vm.Page.Items).Code);
    }

    [Fact]
    public async Task DeleteBatch_RemovesOnlySelectedVouchers()
    {
        var (controller, db) = CreateController();
        var keep = AddVoucher(db, "KEEP10");
        var drop1 = AddVoucher(db, "DROP1");
        var drop2 = AddVoucher(db, "DROP2");

        var result = await controller.DeleteBatch(new List<int> { drop1.Id, drop2.Id });

        Assert.IsType<RedirectToActionResult>(result);
        var remaining = db.Vouchers.Select(v => v.Id).ToList();
        Assert.Equal(new[] { keep.Id }, remaining);
    }

    [Fact]
    public async Task DeleteBatch_EmptySelection_RemovesNothing()
    {
        var (controller, db) = CreateController();
        AddVoucher(db);

        var result = await controller.DeleteBatch(new List<int>());

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(db.Vouchers);
    }

    [Fact]
    public async Task Create_ValidVoucher_PersistsNormalizedAndRedirects()
    {
        var (controller, db) = CreateController();

        var result = await controller.Create(ValidModel(" summer "));

        Assert.IsType<RedirectToActionResult>(result);
        var created = db.Vouchers.Single();
        Assert.Equal("SUMMER", created.Code); // trimmed + uppercased
        Assert.Equal(DiscountType.FixedAmount, created.DiscountType);
        Assert.Equal(5m, created.DiscountValue);
        Assert.Equal(0, created.UsageCount); // system-owned, never copied from the form
    }

    [Fact]
    public async Task Create_DuplicateCode_ShowsModelError()
    {
        var (controller, db) = CreateController();
        AddVoucher(db, "SUMMER10");

        var result = await controller.Create(ValidModel("summer10"));

        var view = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        Assert.Single(db.Vouchers);
    }

    [Fact]
    public async Task Create_InvalidModel_ReRendersView()
    {
        var (controller, _) = CreateController();
        controller.ModelState.AddModelError(nameof(Voucher.Code), "Required");

        var result = await controller.Create(new Voucher());

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Edit_UpdatesEditableFields()
    {
        var (controller, db) = CreateController();
        var voucher = AddVoucher(db);

        var result = await controller.Edit(new Voucher
        {
            Id = voucher.Id,
            Code = "NEWCODE",
            Description = "Changed",
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 8m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(60),
            UsageLimit = 10,
            Status = VoucherStatus.Inactive
        });

        Assert.IsType<RedirectToActionResult>(result);
        var updated = db.Vouchers.Single();
        Assert.Equal("NEWCODE", updated.Code);
        Assert.Equal("Changed", updated.Description);
        Assert.Equal(8m, updated.DiscountValue);
        Assert.Equal(10, updated.UsageLimit);
        Assert.Equal(VoucherStatus.Inactive, updated.Status);
    }

    [Fact]
    public async Task Edit_UnknownId_ReturnsNotFound()
    {
        var (controller, _) = CreateController();

        var result = await controller.Edit(new Voucher { Id = 9999, Code = "X", Description = "X" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_CodeCollidingWithAnotherVoucher_ShowsModelError()
    {
        var (controller, db) = CreateController();
        var keep = AddVoucher(db, "KEEP10");
        AddVoucher(db, "TAKEN5");

        var result = await controller.Edit(new Voucher
        {
            Id = keep.Id,
            Code = "taken5",
            Description = "Collision",
            DiscountValue = 10m,
            ExpiryDate = keep.ExpiryDate
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        // NoTracking: the failed update already mutated the tracked instance in memory.
        Assert.Equal("KEEP10", db.Vouchers.AsNoTracking().Single(v => v.Id == keep.Id).Code);
    }

    [Fact]
    public async Task Delete_Confirmed_RemovesAndRedirects()
    {
        var (controller, db) = CreateController();
        var voucher = AddVoucher(db);

        var result = await controller.DeleteConfirmed(voucher.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(db.Vouchers);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        var (controller, _) = CreateController();

        var result = await controller.DeleteConfirmed(9999);

        Assert.IsType<NotFoundResult>(result);
    }
}
