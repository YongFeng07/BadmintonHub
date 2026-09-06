using SportHub.Models;

namespace SportHub.Services;

public interface IVoucherService
{
    /// <summary>All vouchers for the admin list, newest first.</summary>
    Task<List<Voucher>> GetAllAsync();

    Task<Voucher?> GetByIdAsync(int id);

    Task<(bool Success, string? Error, Voucher? Voucher)> CreateAsync(Voucher voucher);

    Task<(bool Success, string? Error)> UpdateAsync(Voucher voucher);

    /// <summary>Deletes a voucher; reservations keep their VoucherCode copy for history.</summary>
    Task<(bool Success, string? Error)> DeleteAsync(int id);

    /// <summary>
    /// Validates a code against a subtotal WITHOUT side effects, except that an
    /// overdue voucher is lazily flipped to Expired. <paramref name="userId"/> enables
    /// the per-user redemption limit; pass null for anonymous previews.
    /// The Warning element carries non-fatal notes (e.g. a discount cap was applied).
    /// </summary>
    Task<(bool Success, string? Error, decimal Discount, Voucher? Voucher, string? Warning)> ValidateAsync(
        string? code, decimal subtotal, int? userId = null);

    /// <summary>Flips every overdue active voucher to Expired; returns how many changed.</summary>
    Task<int> ExpireOverdueAsync();

    /// <summary>
    /// Creates up to <paramref name="count"/> vouchers from a template with generated
    /// unique codes (<c>PREFIX-XXXXXX</c>). Collisions are skipped, so Created may be
    /// less than requested.
    /// </summary>
    Task<(bool Success, string? Error, int Created, List<string> Codes)> BulkGenerateAsync(
        Voucher template, int count, string? prefix);
}
