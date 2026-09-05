using BadmintonHub.Models;

namespace BadmintonHub.Services;

public interface ICheckoutService
{
    /// <summary>Read-only checkout preview: items, subtotal and (validated) voucher discount.</summary>
    Task<(bool Success, string? Error, List<CartItem> Items, decimal Subtotal, decimal Discount, decimal NetTotal)> PreviewAsync(
        int userId, IEnumerable<int> cartItemIds, string? voucherCode);

    /// <summary>
    /// Transactional checkout: re-validates every line, creates reservations with pending
    /// payments, applies and counts the voucher, and clears the checked-out cart lines.
    /// </summary>
    Task<(bool Success, string? Error, List<Reservation> Reservations)> CheckoutAsync(
        int userId, IEnumerable<int> cartItemIds, string? voucherCode);

    /// <summary>Batch payment: pays every selected pending reservation of the member at once.</summary>
    Task<(bool Success, string? Error, int PaidCount)> MarkBatchPaidAsync(
        int userId, IEnumerable<int> reservationIds, PaymentMethod method);
}
