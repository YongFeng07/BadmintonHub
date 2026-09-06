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
    /// Validates a code against a subtotal WITHOUT side effects. Usage counting only
    /// happens inside the checkout transaction.
    /// </summary>
    Task<(bool Success, string? Error, decimal Discount, Voucher? Voucher)> ValidateAsync(string? code, decimal subtotal);
}
