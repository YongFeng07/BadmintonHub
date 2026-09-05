using BadmintonHub.Data;
using BadmintonHub.Models;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Services;

/// <summary>
/// Discount voucher administration and checkout validation (revised spec). Validation
/// here is side-effect free; the redemption count is only bumped inside the checkout
/// transaction (CheckoutService) so concurrent checkouts cannot overshoot a limit.
/// </summary>
public class VoucherService : IVoucherService
{
    private readonly ApplicationDbContext _db;

    public VoucherService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Voucher>> GetAllAsync()
    {
        return await _db.Vouchers
            .OrderByDescending(v => v.CreatedAt)
            .ThenBy(v => v.Code)
            .ToListAsync();
    }

    public async Task<Voucher?> GetByIdAsync(int id)
    {
        return await _db.Vouchers.FindAsync(id);
    }

    public async Task<(bool Success, string? Error, Voucher? Voucher)> CreateAsync(Voucher voucher)
    {
        voucher.Code = voucher.Code.Trim().ToUpperInvariant();
        if (await _db.Vouchers.AnyAsync(v => v.Code == voucher.Code))
            return (false, "A voucher with this code already exists.", null);

        _db.Vouchers.Add(voucher);
        await _db.SaveChangesAsync();
        return (true, null, voucher);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(Voucher voucher)
    {
        voucher.Code = voucher.Code.Trim().ToUpperInvariant();
        if (await _db.Vouchers.AnyAsync(v => v.Code == voucher.Code && v.Id != voucher.Id))
            return (false, "A voucher with this code already exists.");

        _db.Vouchers.Update(voucher);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id)
    {
        var voucher = await _db.Vouchers.FindAsync(id);
        if (voucher == null)
            return (false, "Voucher not found.");

        // Reservations keep a copy of the applied code (VoucherCode), so booking
        // history survives voucher deletion.
        _db.Vouchers.Remove(voucher);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, decimal Discount, Voucher? Voucher)> ValidateAsync(
        string? code, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (false, "Please enter a voucher code.", 0, null);

        var normalized = code.Trim().ToUpperInvariant();
        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Code == normalized);
        if (voucher == null)
            return (false, "Voucher code not found.", 0, null);

        if (voucher.Status != VoucherStatus.Active)
            return (false, "This voucher is no longer active.", 0, null);

        if (voucher.ExpiryDate < DateOnly.FromDateTime(DateTime.Today))
            return (false, "This voucher has expired.", 0, null);

        if (voucher.UsageLimit.HasValue && voucher.UsageCount >= voucher.UsageLimit.Value)
            return (false, "This voucher has reached its redemption limit.", 0, null);

        if (subtotal <= 0)
            return (false, "The cart subtotal must be greater than zero.", 0, null);

        var discount = voucher.DiscountType == DiscountType.Percentage
            ? Math.Round(subtotal * voucher.DiscountValue / 100m, 2, MidpointRounding.AwayFromZero)
            : voucher.DiscountValue;

        // The net total must stay positive (a payment can never be RM 0.00).
        if (discount >= subtotal)
            discount = subtotal - 0.01m;

        return (true, null, discount, voucher);
    }
}
