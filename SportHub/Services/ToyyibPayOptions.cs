namespace SportHub.Services;

/// <summary>
/// ToyyibPay gateway settings (appsettings "ToyyibPay" section). The key fields
/// are EMPTY PLACEHOLDERS by design: with no UserSecretKey configured the
/// service runs its simulated fallback, so the demo works without real
/// credentials and none are ever committed.
/// </summary>
public class ToyyibPayOptions
{
    /// <summary>e.g. https://dev.toyyibpay.com (sandbox) or https://toyyibpay.com (production).</summary>
    public string ApiBaseUrl { get; set; } = "https://dev.toyyibpay.com";

    /// <summary>Empty = simulated fallback (demo mode).</summary>
    public string UserSecretKey { get; set; } = "";

    /// <summary>Empty = simulated fallback (demo mode).</summary>
    public string CategoryCode { get; set; } = "";

    /// <summary>Optional absolute URL override for the return URL; otherwise built from the request.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Optional absolute URL override for the callback URL; otherwise built from the request.</summary>
    public string? CallbackUrl { get; set; }

    public bool IsSimulated => string.IsNullOrWhiteSpace(UserSecretKey) || string.IsNullOrWhiteSpace(CategoryCode);
}
