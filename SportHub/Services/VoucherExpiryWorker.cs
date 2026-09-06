namespace SportHub.Services;

/// <summary>
/// Background service (G-M2) that flips overdue vouchers from Active to Expired so
/// admin listings stay truthful without anyone having to open each voucher. Runs once
/// at startup and then every six hours; validation also lazy-expires on first touch,
/// so this is a safety net rather than the only mechanism.
/// </summary>
public class VoucherExpiryWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VoucherExpiryWorker> _logger;

    public VoucherExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<VoucherExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First pass immediately, then on a fixed interval for the process lifetime.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();
                var flipped = await vouchers.ExpireOverdueAsync();
                if (flipped > 0)
                    _logger.LogInformation("VoucherExpiryWorker expired {Count} voucher(s).", flipped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VoucherExpiryWorker pass failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
