using BadmintonHub.Models;

namespace BadmintonHub.Services;

/// <summary>
/// ToyyibPay payment gateway (revised spec): creates a bill for a batch of
/// pending payments, and verifies the gateway return. With no credentials
/// configured it runs the SIMULATED fallback so the whole flow is demonstrable
/// offline — see <see cref="ToyyibPayOptions"/>.
/// </summary>
public interface IToyyibPayService
{
    /// <summary>
    /// Opens one ToyyibPay bill covering the given pending payments (they must
    /// all belong to <paramref name="userId"/>). Stores the bill code on each
    /// payment row and returns the URL the member is redirected to.
    /// </summary>
    Task<(bool Success, string? BillCode, string? PaymentUrl, string? Error)> CreateBillAsync(
        int userId, List<Payment> payments, decimal amount, string returnUrl);

    /// <summary>
    /// Handles the gateway redirect back (or the simulated page's equivalent).
    /// In real mode the bill status is verified against the ToyyibPay API, so a
    /// forged status_id cannot mark anything paid. Returns the reservation ids
    /// of the bill (for the batch mark-paid) and the gateway status id.
    /// </summary>
    Task<(bool Success, string? Error, List<int> ReservationIds, string StatusId)> ProcessReturnAsync(
        string billCode, string? statusId);
}
