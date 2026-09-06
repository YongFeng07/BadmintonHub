namespace SportHub.Services;

/// <summary>
/// G-M4: checks for wishlisted courts that became bookable again and notifies the
/// waiting members (in-app notification + email, one-shot per reopening). Runs on
/// a fixed interval alongside the other maintenance workers.
/// </summary>
public class WishlistNotifyWorker : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WishlistNotifyWorker> _logger;

    public WishlistNotifyWorker(IServiceScopeFactory scopeFactory, ILogger<WishlistNotifyWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var wishlist = scope.ServiceProvider.GetRequiredService<IWishlistService>();
                var notified = await wishlist.NotifyForAvailableCourtsAsync();
                if (notified > 0)
                    _logger.LogInformation("WishlistNotifyWorker notified {Count} member(s) of reopened courts.", notified);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WishlistNotifyWorker pass failed.");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }
}
