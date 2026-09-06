using System.Net.Http.Json;
using SportHub.Data;
using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Services;

/// <summary>
/// ToyyibPay integration with a simulated fallback. Real mode POSTs
/// createBill / getBillTransactions to the configured API base URL and trusts
/// only the verified transaction status. Simulated mode (empty key placeholders)
/// mints a local SIM-{guid} bill so the checkout → gateway page → return →
/// confirmed flow is fully demonstrable without credentials.
/// </summary>
public class ToyyibPayService : IToyyibPayService
{
    private readonly ApplicationDbContext _db;
    private readonly ToyyibPayOptions _options;
    private readonly HttpClient _http;

    public ToyyibPayService(ApplicationDbContext db, ToyyibPayOptions options, HttpClient? http = null)
    {
        _db = db;
        _options = options;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<(bool Success, string? BillCode, string? PaymentUrl, string? Error)> CreateBillAsync(
        int userId, List<Payment> payments, decimal amount, string returnUrl)
    {
        if (payments.Count == 0)
            return (false, null, null, "There is nothing to pay.");

        if (payments.Any(p => p.UserId != userId))
            return (false, null, null, "You can only pay for your own reservations.");

        if (payments.Any(p => p.Status != PaymentStatus.Pending))
            return (false, null, null, "Only pending payments can be sent to the gateway.");

        var billCode = _options.IsSimulated
            ? $"SIM-{Guid.NewGuid():N}"
            : await CreateRealBillAsync(payments, amount, returnUrl);

        if (billCode == null)
            return (false, null, null, "The payment gateway could not create a bill. Please try again.");

        foreach (var payment in payments)
        {
            payment.GatewayBillCode = billCode;
            payment.GatewayStatus = "2"; // ToyyibPay status_id 2 = pending
        }

        await _db.SaveChangesAsync();

        // In simulated mode the "gateway" is our local demo page; in real mode
        // the member is redirected to the hosted ToyyibPay payment page.
        var paymentUrl = _options.IsSimulated
            ? $"/Payments/ToyyibPaySimulated?billCode={Uri.EscapeDataString(billCode)}"
            : $"{_options.ApiBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(billCode)}";

        return (true, billCode, paymentUrl, null);
    }

    public async Task<(bool Success, string? Error, List<int> ReservationIds, string StatusId)> ProcessReturnAsync(
        string billCode, string? statusId)
    {
        var payments = await _db.Payments
            .Where(p => p.GatewayBillCode == billCode)
            .ToListAsync();

        if (payments.Count == 0)
            return (false, "Unknown payment bill.", new List<int>(), statusId ?? "");

        statusId = string.IsNullOrWhiteSpace(statusId) ? "3" : statusId.Trim();

        // Real mode: never trust the browser-echoed status — ask the gateway.
        if (!_options.IsSimulated && statusId == "1")
        {
            var verified = await VerifyRealBillAsync(billCode);
            if (!verified)
                statusId = "3";
        }

        foreach (var payment in payments)
            payment.GatewayStatus = statusId;

        await _db.SaveChangesAsync();

        var reservationIds = payments
            .Where(p => p.Status == PaymentStatus.Pending)
            .Select(p => p.ReservationId)
            .Distinct()
            .ToList();

        return (true, null, reservationIds, statusId);
    }

    /// <summary>POST createBill and parse the returned BillCode; null on failure.</summary>
    private async Task<string?> CreateRealBillAsync(List<Payment> payments, decimal amount, string returnUrl)
    {
        var first = payments.First();
        var user = await _db.Users.FindAsync(first.UserId);

        var form = new Dictionary<string, string>
        {
            ["userSecretKey"] = _options.UserSecretKey,
            ["categoryCode"] = _options.CategoryCode,
            ["billName"] = "SportHub Booking",
            ["billDescription"] = $"SportHub booking payment ({payments.Count} reservation(s))",
            ["billPriceSetting"] = "0", // use billAmount
            ["billPayorInfo"] = "1",
            ["billAmount"] = amount.ToString("0.00"),
            ["billReturnUrl"] = returnUrl,
            ["billCallbackUrl"] = _options.CallbackUrl ?? returnUrl,
            ["billExternalReferenceNo"] = $"SH-{Guid.NewGuid():N}"[..37], // order_id (max 40 chars), no sensitive data
            ["billTo"] = user?.FullName ?? "SportHub Member",
            ["billEmail"] = user?.Email ?? "",
            ["billPhone"] = user?.Phone ?? ""
        };

        using var response = await _http.PostAsync(
            $"{_options.ApiBaseUrl.TrimEnd('/')}/index.php/api/createBill",
            new FormUrlEncodedContent(form));
        if (!response.IsSuccessStatusCode)
            return null;

        var bills = await response.Content.ReadFromJsonAsync<List<ToyyibBillResponse>>();
        return bills?.FirstOrDefault()?.BillCode;
    }

    /// <summary>True when the gateway reports a successful transaction for the bill.</summary>
    private async Task<bool> VerifyRealBillAsync(string billCode)
    {
        var form = new Dictionary<string, string>
        {
            ["userSecretKey"] = _options.UserSecretKey,
            ["billCode"] = billCode
        };

        using var response = await _http.PostAsync(
            $"{_options.ApiBaseUrl.TrimEnd('/')}/index.php/api/getBillTransactions",
            new FormUrlEncodedContent(form));
        if (!response.IsSuccessStatusCode)
            return false;

        var transactions = await response.Content.ReadFromJsonAsync<List<ToyyibTransactionResponse>>();
        // billpaymentstatus "1" = success ("2" = pending, "3" = failed)
        return transactions?.Any(t => t.BillPaymentStatus == "1") ?? false;
    }

    private sealed class ToyyibBillResponse
    {
        public string? BillCode { get; set; }
    }

    private sealed class ToyyibTransactionResponse
    {
        public string? BillPaymentStatus { get; set; }
    }
}
