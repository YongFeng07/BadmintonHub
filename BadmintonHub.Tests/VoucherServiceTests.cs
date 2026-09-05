using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Tests;

/// <summary>
/// P4 discount vouchers: code normalisation/uniqueness and the checkout-side
/// validation matrix (status, expiry, redemption limit, discount maths).
/// </summary>
public class VoucherServiceTests
{
    private static (VoucherService Service, ApplicationDbContext Db) Create()
    {
        var db = TestDb.Create();
        return (new VoucherService(db), db);
    }

    private static Voucher AddVoucher(ApplicationDbContext db, string code = "SUMMER10",
        DiscountType type = DiscountType.Percentage, decimal value = 10m,
        int daysValid = 30, int? limit = null, VoucherStatus status = VoucherStatus.Active, int usageCount = 0)
    {
        var voucher = new Voucher
        {
            Code = code,
            Description = "Test voucher",
            DiscountType = type,
            DiscountValue = value,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(daysValid),
            UsageLimit = limit,
            UsageCount = usageCount,
            Status = status
        };
        db.Vouchers.Add(voucher);
        db.SaveChanges();
        return voucher;
    }

    [Fact]
    public async Task Create_NormalizesCodeToUppercase()
    {
        var (service, db) = Create();

        var (success, error, voucher) = await service.CreateAsync(new Voucher
        {
            Code = " summer  ",
            Description = "Summer deal",
            DiscountValue = 10,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30)
        });

        Assert.True(success, error);
        Assert.Equal("SUMMER", voucher!.Code);
        Assert.Equal("SUMMER", db.Vouchers.Single().Code);
    }

    [Fact]
    public async Task Create_DuplicateCode_IsRejected()
    {
        var (service, db) = Create();
        AddVoucher(db, "SUMMER10");

        var (success, error, _) = await service.CreateAsync(new Voucher
        {
            Code = "summer10", // same code after normalisation
            Description = "Duplicate",
            DiscountValue = 5,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30)
        });

        Assert.False(success);
        Assert.Equal("A voucher with this code already exists.", error);
        Assert.Equal(1, await db.Vouchers.CountAsync());
    }

    [Fact]
    public async Task Update_ChangesEditableFields()
    {
        var (service, db) = Create();
        var voucher = AddVoucher(db, "SUMMER10");

        voucher.Description = "New description";
        voucher.DiscountType = DiscountType.FixedAmount;
        voucher.DiscountValue = 8m;
        voucher.ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(60);
        voucher.UsageLimit = 10;
        voucher.Status = VoucherStatus.Inactive;

        var (success, error) = await service.UpdateAsync(voucher);

        Assert.True(success, error);
        var updated = db.Vouchers.Single();
        Assert.Equal("New description", updated.Description);
        Assert.Equal(DiscountType.FixedAmount, updated.DiscountType);
        Assert.Equal(8m, updated.DiscountValue);
        Assert.Equal(10, updated.UsageLimit);
        Assert.Equal(VoucherStatus.Inactive, updated.Status);
    }

    [Fact]
    public async Task Update_CodeCollidingWithAnotherVoucher_IsRejected()
    {
        var (service, db) = Create();
        var keep = AddVoucher(db, "KEEP10");
        AddVoucher(db, "TAKEN5");

        keep.Code = "taken5";
        var (success, error) = await service.UpdateAsync(keep);

        Assert.False(success);
        Assert.Equal("A voucher with this code already exists.", error);
        // NoTracking: the failed update already mutated the tracked instance in memory.
        Assert.Equal("KEEP10", db.Vouchers.AsNoTracking().Single(v => v.Id == keep.Id).Code); // untouched
    }

    [Fact]
    public async Task Delete_RemovesVoucher()
    {
        var (service, db) = Create();
        var voucher = AddVoucher(db);

        var (success, error) = await service.DeleteAsync(voucher.Id);

        Assert.True(success, error);
        Assert.Equal(0, await db.Vouchers.CountAsync());
    }

    [Fact]
    public async Task Delete_UnknownVoucher_Fails()
    {
        var (service, _) = Create();

        var (success, error) = await service.DeleteAsync(9999);

        Assert.False(success);
        Assert.Equal("Voucher not found.", error);
    }

    [Fact]
    public async Task Validate_BlankCode_Fails()
    {
        var (service, _) = Create();

        var (success, error, discount, voucher) = await service.ValidateAsync("  ", 50m);

        Assert.False(success);
        Assert.Equal("Please enter a voucher code.", error);
        Assert.Equal(0m, discount);
        Assert.Null(voucher);
    }

    [Fact]
    public async Task Validate_UnknownCode_Fails()
    {
        var (service, _) = Create();

        var (success, error, _, _) = await service.ValidateAsync("NOPE99", 50m);

        Assert.False(success);
        Assert.Equal("Voucher code not found.", error);
    }

    [Fact]
    public async Task Validate_InactiveVoucher_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db, status: VoucherStatus.Inactive);

        var (success, error, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Equal("This voucher is no longer active.", error);
    }

    [Fact]
    public async Task Validate_ExpiredVoucher_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db, daysValid: -1);

        var (success, error, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Equal("This voucher has expired.", error);
    }

    [Fact]
    public async Task Validate_RedemptionLimitReached_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db, limit: 1, usageCount: 1);

        var (success, error, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Equal("This voucher has reached its redemption limit.", error);
    }

    [Fact]
    public async Task Validate_ZeroSubtotal_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db);

        var (success, error, _, _) = await service.ValidateAsync("SUMMER10", 0m);

        Assert.False(success);
        Assert.Equal("The cart subtotal must be greater than zero.", error);
    }

    [Fact]
    public async Task Validate_Percentage_ComputesDiscount()
    {
        var (service, db) = Create();
        AddVoucher(db, "WELCOME10", DiscountType.Percentage, 10m);

        var (success, error, discount, voucher) = await service.ValidateAsync("welcome10", 75m);

        Assert.True(success, error);
        Assert.Equal(7.50m, discount);
        Assert.NotNull(voucher);
    }

    [Fact]
    public async Task Validate_FixedAmount_IsCappedToKeepNetPositive()
    {
        var (service, db) = Create();
        AddVoucher(db, "FLAT100", DiscountType.FixedAmount, 100m);

        var (success, error, discount, _) = await service.ValidateAsync("FLAT100", 25m);

        Assert.True(success, error);
        Assert.Equal(24.99m, discount); // a payment can never be RM 0.00
    }

    [Fact]
    public async Task GetAll_OrdersNewestFirst()
    {
        var (service, db) = Create();
        var older = AddVoucher(db, "OLDER1");
        var newer = AddVoucher(db, "NEWER1");
        older.CreatedAt = DateTime.Now.AddHours(-2);
        newer.CreatedAt = DateTime.Now;
        db.SaveChanges();

        var vouchers = await service.GetAllAsync();

        Assert.Equal(new[] { "NEWER1", "OLDER1" }, vouchers.Select(v => v.Code));
    }
}
