using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// P4 discount vouchers: code normalisation/uniqueness and the checkout-side
/// validation matrix (status, expiry, redemption limit, discount maths), plus the
/// G-M2 rule set (minimum spend, discount cap, valid-from date, per-user limit,
/// lazy expiry, usage-limit guards and bulk code generation).
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
        int daysValid = 30, int? limit = null, VoucherStatus status = VoucherStatus.Active, int usageCount = 0,
        DateOnly? startDate = null, decimal minSpend = 0m, decimal? maxDiscount = null, int? perUserLimit = null)
    {
        var voucher = new Voucher
        {
            Code = code,
            Description = "Test voucher",
            DiscountType = type,
            DiscountValue = value,
            StartDate = startDate,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(daysValid),
            MinSpend = minSpend,
            MaxDiscount = maxDiscount,
            UsageLimit = limit,
            PerUserLimit = perUserLimit,
            UsageCount = usageCount,
            Status = status
        };
        db.Vouchers.Add(voucher);
        db.SaveChanges();
        return voucher;
    }

    // ---------- CRUD ----------

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
    public async Task Create_StartDateAfterExpiry_IsRejected()
    {
        var (service, db) = Create();

        var (success, error, _) = await service.CreateAsync(new Voucher
        {
            Code = "BADRANGE",
            Description = "Bad dates",
            DiscountValue = 10,
            StartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(10),
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(5)
        });

        Assert.False(success);
        Assert.Equal("The valid-from date must be before the expiry date.", error);
        Assert.Equal(0, await db.Vouchers.CountAsync());
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
        voucher.PerUserLimit = 3;
        voucher.MinSpend = 20m;
        voucher.MaxDiscount = 5m;
        voucher.Status = VoucherStatus.Inactive;

        var (success, error) = await service.UpdateAsync(voucher);

        Assert.True(success, error);
        var updated = db.Vouchers.Single();
        Assert.Equal("New description", updated.Description);
        Assert.Equal(DiscountType.FixedAmount, updated.DiscountType);
        Assert.Equal(8m, updated.DiscountValue);
        Assert.Equal(10, updated.UsageLimit);
        Assert.Equal(3, updated.PerUserLimit);
        Assert.Equal(20m, updated.MinSpend);
        Assert.Equal(5m, updated.MaxDiscount);
        Assert.Equal(VoucherStatus.Inactive, updated.Status);
    }

    [Fact]
    public async Task Update_PreservesUsageCountLedger()
    {
        var (service, db) = Create();
        var voucher = AddVoucher(db, usageCount: 7);

        voucher.Description = "Edited";
        var (success, error) = await service.UpdateAsync(voucher);

        Assert.True(success, error);
        Assert.Equal(7, db.Vouchers.AsNoTracking().Single().UsageCount);
    }

    [Fact]
    public async Task Update_UsageLimitBelowUsedCount_IsRejected()
    {
        var (service, db) = Create();
        var voucher = AddVoucher(db, limit: 10, usageCount: 7);

        voucher.UsageLimit = 5;
        var (success, error) = await service.UpdateAsync(voucher);

        Assert.False(success);
        Assert.Equal("Usage limit cannot be lower than the number of times already used (7).", error);
        Assert.Equal(10, db.Vouchers.AsNoTracking().Single().UsageLimit);
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

    // ---------- Validation matrix ----------

    [Fact]
    public async Task Validate_BlankCode_Fails()
    {
        var (service, _) = Create();

        var (success, error, discount, voucher, warning) = await service.ValidateAsync("  ", 50m);

        Assert.False(success);
        Assert.Equal("Please enter a voucher code.", error);
        Assert.Equal(0m, discount);
        Assert.Null(voucher);
        Assert.Null(warning);
    }

    [Fact]
    public async Task Validate_UnknownCode_Fails()
    {
        var (service, _) = Create();

        var (success, error, _, _, _) = await service.ValidateAsync("NOPE99", 50m);

        Assert.False(success);
        Assert.Equal("Voucher code not found.", error);
    }

    [Fact]
    public async Task Validate_InactiveVoucher_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db, status: VoucherStatus.Inactive);

        var (success, error, _, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Equal("This voucher is no longer active.", error);
    }

    [Fact]
    public async Task Validate_ExpiredVoucher_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db, daysValid: -1);

        var (success, error, _, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Equal("This voucher has expired.", error);
    }

    [Fact]
    public async Task Validate_ExpiredStatusVoucher_FailsWithoutTouch()
    {
        var (service, db) = Create();
        AddVoucher(db, daysValid: 30, status: VoucherStatus.Expired);

        var (success, error, _, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Equal("This voucher has expired.", error);
    }

    [Fact]
    public async Task Validate_OverdueVoucher_IsLazilyFlippedToExpired()
    {
        var (service, db) = Create();
        var overdue = AddVoucher(db, daysValid: -1, status: VoucherStatus.Active);

        var (success, error, _, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Equal("This voucher has expired.", error);
        Assert.Equal(VoucherStatus.Expired, db.Vouchers.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Validate_StartDateInFuture_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db, startDate: DateOnly.FromDateTime(DateTime.Today).AddDays(5));

        var (success, error, _, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Contains("only valid from", error);
    }

    [Fact]
    public async Task Validate_RedemptionLimitReached_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db, limit: 1, usageCount: 1);

        var (success, error, _, _, _) = await service.ValidateAsync("SUMMER10", 50m);

        Assert.False(success);
        Assert.Equal("This voucher has reached its redemption limit.", error);
    }

    [Fact]
    public async Task Validate_ZeroSubtotal_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db);

        var (success, error, _, _, _) = await service.ValidateAsync("SUMMER10", 0m);

        Assert.False(success);
        Assert.Equal("The cart subtotal must be greater than zero.", error);
    }

    [Fact]
    public async Task Validate_MinSpendNotMet_Fails()
    {
        var (service, db) = Create();
        AddVoucher(db, "BIGSPEND", DiscountType.FixedAmount, 8m, minSpend: 50m);

        var (success, error, _, _, _) = await service.ValidateAsync("BIGSPEND", 30m);

        Assert.False(success);
        Assert.Equal("This voucher requires a minimum spend of RM 50.00.", error);
    }

    [Fact]
    public async Task Validate_MinSpendMet_Applies()
    {
        var (service, db) = Create();
        AddVoucher(db, "BIGSPEND", DiscountType.FixedAmount, 8m, minSpend: 50m);

        var (success, error, discount, _, _) = await service.ValidateAsync("BIGSPEND", 60m);

        Assert.True(success, error);
        Assert.Equal(8m, discount);
    }

    [Fact]
    public async Task Validate_Percentage_ComputesDiscount()
    {
        var (service, db) = Create();
        AddVoucher(db, "WELCOME10", DiscountType.Percentage, 10m);

        var (success, error, discount, voucher, warning) = await service.ValidateAsync("welcome10", 75m);

        Assert.True(success, error);
        Assert.Equal(7.50m, discount);
        Assert.NotNull(voucher);
        Assert.Null(warning);
    }

    [Fact]
    public async Task Validate_FixedAmount_IsCappedToKeepNetPositive_WithWarning()
    {
        var (service, db) = Create();
        AddVoucher(db, "FLAT100", DiscountType.FixedAmount, 100m);

        var (success, error, discount, _, warning) = await service.ValidateAsync("FLAT100", 25m);

        Assert.True(success, error);
        Assert.Equal(24.99m, discount); // a payment can never be RM 0.00
        Assert.Equal("The discount exceeds the cart total, so the net total has been kept at RM 0.01.", warning);
    }

    [Fact]
    public async Task Validate_MaxDiscountCapsPercentage_WithWarning()
    {
        var (service, db) = Create();
        AddVoucher(db, "CAPPED", DiscountType.Percentage, 20m, maxDiscount: 10m);

        var (success, error, discount, _, warning) = await service.ValidateAsync("CAPPED", 100m);

        Assert.True(success, error);
        Assert.Equal(10m, discount); // 20% of 100 would be RM 20
        Assert.Equal("Discount capped at RM 10.00.", warning);
    }

    [Fact]
    public async Task Validate_MaxDiscountNotHit_NoWarning()
    {
        var (service, db) = Create();
        AddVoucher(db, "CAPPED", DiscountType.Percentage, 20m, maxDiscount: 10m);

        var (success, error, discount, _, warning) = await service.ValidateAsync("CAPPED", 30m);

        Assert.True(success, error);
        Assert.Equal(6m, discount);
        Assert.Null(warning);
    }

    [Fact]
    public async Task Validate_PerUserLimitReached_Fails()
    {
        var (service, db) = Create();
        var voucher = AddVoucher(db, "PERSONAL", perUserLimit: 2);
        db.VoucherRedemptions.Add(new VoucherRedemption { VoucherId = voucher.Id, UserId = 7, Count = 2 });
        db.SaveChanges();

        var (success, error, _, _, _) = await service.ValidateAsync("PERSONAL", 50m, userId: 7);

        Assert.False(success);
        Assert.Equal("You have already used this voucher the maximum 2 time(s).", error);
    }

    [Fact]
    public async Task Validate_PerUserLimit_UnderLimit_Succeeds()
    {
        var (service, db) = Create();
        var voucher = AddVoucher(db, "PERSONAL", perUserLimit: 2);
        db.VoucherRedemptions.Add(new VoucherRedemption { VoucherId = voucher.Id, UserId = 7, Count = 1 });
        db.SaveChanges();

        var (success, error, _, _, _) = await service.ValidateAsync("PERSONAL", 50m, userId: 7);

        Assert.True(success, error);
    }

    [Fact]
    public async Task Validate_PerUserLimit_WithoutUserId_IsSkipped()
    {
        var (service, db) = Create();
        var voucher = AddVoucher(db, "PERSONAL", perUserLimit: 2);
        db.VoucherRedemptions.Add(new VoucherRedemption { VoucherId = voucher.Id, UserId = 7, Count = 2 });
        db.SaveChanges();

        var (success, error, _, _, _) = await service.ValidateAsync("PERSONAL", 50m, userId: null);

        Assert.True(success, error);
    }

    // ---------- Bulk generation ----------

    [Fact]
    public async Task BulkGenerate_CreatesRequestedCount_WithUniquePrefixedCodes()
    {
        var (service, db) = Create();

        var (success, error, created, codes) = await service.BulkGenerateAsync(new Voucher
        {
            Description = "Event campaign",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30)
        }, 25, "event");

        Assert.True(success, error);
        Assert.Equal(25, created);
        Assert.Equal(25, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches(@"^EVENT-[A-HJ-NP-Z2-9]{8}$", c));
        Assert.Equal(25, await db.Vouchers.CountAsync());
    }

    [Fact]
    public async Task BulkGenerate_SecondBatchNeverCollidesWithFirst()
    {
        var (service, db) = Create();
        var template = new Voucher
        {
            Description = "Campaign",
            DiscountValue = 5m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30)
        };

        var (_, _, _, first) = await service.BulkGenerateAsync(template, 10, "DRAW");
        var (success, error, created, second) = await service.BulkGenerateAsync(template, 10, "DRAW");

        Assert.True(success, error);
        Assert.Equal(10, created);
        Assert.Empty(first.Intersect(second));
    }

    [Fact]
    public async Task BulkGenerate_SanitizesPrefix()
    {
        var (service, _) = Create();

        var (success, error, created, codes) = await service.BulkGenerateAsync(new Voucher
        {
            Description = "Campaign",
            DiscountValue = 5m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30)
        }, 3, "ev!ent ");

        Assert.True(success, error);
        Assert.Equal(3, created);
        Assert.All(codes, c => Assert.StartsWith("EVENT-", c));
    }

    [Fact]
    public async Task BulkGenerate_OutOfRangeCount_Fails()
    {
        var (service, _) = Create();

        var (success, error, created, codes) = await service.BulkGenerateAsync(new Voucher
        {
            Description = "Campaign",
            DiscountValue = 5m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30)
        }, 0, null);

        Assert.False(success);
        Assert.Equal("Generate between 1 and 200 vouchers at a time.", error);
        Assert.Equal(0, created);
    }

    // ---------- Expiry worker ----------

    [Fact]
    public async Task ExpireOverdue_FlipsOnlyOverdueActiveVouchers()
    {
        var (service, db) = Create();
        AddVoucher(db, "OLD", daysValid: -1);
        AddVoucher(db, "NEW", daysValid: 5);
        AddVoucher(db, "ALREADY", daysValid: -1, status: VoucherStatus.Expired);

        var flipped = await service.ExpireOverdueAsync();

        Assert.Equal(1, flipped);
        Assert.Equal(VoucherStatus.Expired, db.Vouchers.AsNoTracking().Single(v => v.Code == "OLD").Status);
        Assert.Equal(VoucherStatus.Active, db.Vouchers.AsNoTracking().Single(v => v.Code == "NEW").Status);
        Assert.Equal(VoucherStatus.Expired, db.Vouchers.AsNoTracking().Single(v => v.Code == "ALREADY").Status);
    }

    [Fact]
    public async Task ExpireOverdue_NoOverdue_ReturnsZero()
    {
        var (service, db) = Create();
        AddVoucher(db, "NEW", daysValid: 5);

        var flipped = await service.ExpireOverdueAsync();

        Assert.Equal(0, flipped);
        Assert.Equal(1, await db.Vouchers.CountAsync());
    }

    // ---------- Listing ----------

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
