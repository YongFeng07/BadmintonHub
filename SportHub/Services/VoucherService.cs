using SportHub.Data;
using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Services;

/// <summary>
/// Discount voucher administration and checkout validation (revised spec). Validation
/// here is side-effect free apart from one deliberate exception: a voucher whose expiry
/// date has passed is lazily flipped to <see cref="VoucherStatus.Expired"/> when it is
/// checked, so the admin list reflects reality even before the background worker runs.
/// The redemption count is only bumped inside the checkout transaction (CheckoutService)
/// so concurrent checkouts cannot overshoot a limit.
/// </summary>
public class VoucherService : IVoucherService
{
    /// <summary>Uses the unambiguous alphabet: no I/O/0/1, so printed codes are never misread.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly Random Rng = new();
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

        if (!HasValidDateRange(voucher, out var dateError))
            return (false, dateError, null);

        _db.Vouchers.Add(voucher);
        await _db.SaveChangesAsync();
        return (true, null, voucher);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(Voucher voucher)
    {
        voucher.Code = voucher.Code.Trim().ToUpperInvariant();
        if (await _db.Vouchers.AnyAsync(v => v.Code == voucher.Code && v.Id != voucher.Id))
            return (false, "A voucher with this code already exists.");

        var existing = await _db.Vouchers.FindAsync(voucher.Id);
        if (existing == null)
            return (false, "Voucher not found.");

        if (voucher.UsageLimit.HasValue && voucher.UsageLimit.Value < existing.UsageCount)
            return (false, $"Usage limit cannot be lower than the number of times already used ({existing.UsageCount}).");

        if (!HasValidDateRange(voucher, out var dateError))
            return (false, dateError);

        // Copy only the editable fields; UsageCount is a ledger value and must never
        // be overwritten by the (zeroed) values that model binding produced.
        existing.Code = voucher.Code;
        existing.Description = voucher.Description;
        existing.DiscountType = voucher.DiscountType;
        existing.DiscountValue = voucher.DiscountValue;
        existing.StartDate = voucher.StartDate;
        existing.ExpiryDate = voucher.ExpiryDate;
        existing.MinSpend = voucher.MinSpend;
        existing.MaxDiscount = voucher.MaxDiscount;
        existing.UsageLimit = voucher.UsageLimit;
        existing.PerUserLimit = voucher.PerUserLimit;
        existing.Status = voucher.Status;

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

    public async Task<(bool Success, string? Error, decimal Discount, Voucher? Voucher, string? Warning)> ValidateAsync(
        string? code, decimal subtotal, int? userId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (false, "Please enter a voucher code.", 0, null, null);

        var normalized = code.Trim().ToUpperInvariant();
        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Code == normalized);
        if (voucher == null)
            return (false, "Voucher code not found.", 0, null, null);

        if (voucher.Status == VoucherStatus.Expired)
            return (false, "This voucher has expired.", 0, null, null);

        if (voucher.Status != VoucherStatus.Active)
            return (false, "This voucher is no longer active.", 0, null, null);

        // Lazy expiry: flip the row as soon as we notice, so admins see "Expired"
        // without waiting for the background worker.
        if (voucher.ExpiryDate < DateOnly.FromDateTime(DateTime.Today))
        {
            voucher.Status = VoucherStatus.Expired;
            await _db.SaveChangesAsync();
            return (false, "This voucher has expired.", 0, null, null);
        }

        if (voucher.StartDate.HasValue && voucher.StartDate.Value > DateOnly.FromDateTime(DateTime.Today))
            return (false, $"This voucher is only valid from {voucher.StartDate.Value:dd MMM yyyy}.", 0, null, null);

        if (voucher.UsageLimit.HasValue && voucher.UsageCount >= voucher.UsageLimit.Value)
            return (false, "This voucher has reached its redemption limit.", 0, null, null);

        if (userId.HasValue && voucher.PerUserLimit.HasValue)
        {
            var used = await _db.VoucherRedemptions
                .Where(r => r.VoucherId == voucher.Id && r.UserId == userId.Value)
                .Select(r => r.Count)
                .FirstOrDefaultAsync();
            if (used >= voucher.PerUserLimit.Value)
                return (false, $"You have already used this voucher the maximum {voucher.PerUserLimit.Value} time(s).", 0, null, null);
        }

        if (subtotal <= 0)
            return (false, "The cart subtotal must be greater than zero.", 0, null, null);

        if (subtotal < voucher.MinSpend)
            return (false, $"This voucher requires a minimum spend of RM {voucher.MinSpend:0.00}.", 0, null, null);

        var discount = voucher.DiscountType == DiscountType.Percentage
            ? Math.Round(subtotal * voucher.DiscountValue / 100m, 2, MidpointRounding.AwayFromZero)
            : voucher.DiscountValue;

        string? warning = null;
        if (voucher.MaxDiscount.HasValue && discount > voucher.MaxDiscount.Value)
        {
            discount = voucher.MaxDiscount.Value;
            warning = $"Discount capped at RM {voucher.MaxDiscount.Value:0.00}.";
        }

        // The net total must stay positive (a payment can never be RM 0.00). Say so
        // instead of silently adjusting the amount.
        if (discount >= subtotal)
        {
            discount = subtotal - 0.01m;
            warning = "The discount exceeds the cart total, so the net total has been kept at RM 0.01.";
        }

        return (true, null, discount, voucher, warning);
    }

    /// <summary>Flips every active voucher whose expiry date has passed to Expired. Returns how many were flipped.</summary>
    public async Task<int> ExpireOverdueAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var overdue = await _db.Vouchers
            .Where(v => v.Status == VoucherStatus.Active && v.ExpiryDate < today)
            .ToListAsync();

        foreach (var voucher in overdue)
            voucher.Status = VoucherStatus.Expired;

        if (overdue.Count > 0)
            await _db.SaveChangesAsync();

        return overdue.Count;
    }

    /// <summary>
    /// Creates <paramref name="count"/> vouchers from a template, each with a freshly
    /// generated unique code. Codes come from an unambiguous alphabet and take the form
    /// <c>PREFIX-XXXXXX</c> (or <c>XXXXXXXX</c> without a prefix). Collisions are skipped,
    /// so the created count may be lower than requested.
    /// </summary>
    public async Task<(bool Success, string? Error, int Created, List<string> Codes)> BulkGenerateAsync(
        Voucher template, int count, string? prefix)
    {
        if (count is < 1 or > 200)
            return (false, "Generate between 1 and 200 vouchers at a time.", 0, new List<string>());

        template.Code = (prefix ?? string.Empty).Trim().ToUpperInvariant();
        var cleanPrefix = new string(template.Code.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
        if (cleanPrefix.Length > 10)
            cleanPrefix = cleanPrefix[..10];

        // Ensure every generated code fits the [StringLength(20)] limit.
        var suffixLength = cleanPrefix.Length == 0 ? 8 : Math.Min(8, 20 - cleanPrefix.Length - 1);

        var existing = (await _db.Vouchers.Select(v => v.Code).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var created = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var suffix = new string(Enumerable.Range(0, suffixLength)
                    .Select(_ => CodeAlphabet[Rng.Next(CodeAlphabet.Length)]).ToArray());
                var code = cleanPrefix.Length == 0 ? suffix : $"{cleanPrefix}-{suffix}";
                if (!existing.Add(code))
                    continue;

                var voucher = new Voucher
                {
                    Code = code,
                    Description = template.Description,
                    DiscountType = template.DiscountType,
                    DiscountValue = template.DiscountValue,
                    StartDate = template.StartDate,
                    ExpiryDate = template.ExpiryDate,
                    MinSpend = template.MinSpend,
                    MaxDiscount = template.MaxDiscount,
                    UsageLimit = template.UsageLimit,
                    PerUserLimit = template.PerUserLimit,
                    Status = template.Status
                };
                _db.Vouchers.Add(voucher);
                created.Add(code);
                break;
            }
        }

        await _db.SaveChangesAsync();
        return (true, null, created.Count, created);
    }

    private static bool HasValidDateRange(Voucher voucher, out string? error)
    {
        if (voucher.StartDate.HasValue && voucher.StartDate.Value >= voucher.ExpiryDate)
        {
            error = "The valid-from date must be before the expiry date.";
            return false;
        }

        error = null;
        return true;
    }
}
