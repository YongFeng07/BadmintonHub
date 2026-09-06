namespace SportHub.Services;

/// <summary>
/// G-M3: releases expired cart holds on a fixed interval (every five minutes) so
/// held slots flow back to "Open" promptly. Expired holds are already ignored by
/// every availability query — this sweep only keeps the stored state honest.
/// </summary>
public class CartHoldWorker : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CartHoldWorker> _logger;

    public CartHoldWorker(IServiceScopeFactory scopeFactory, ILogger<CartHoldWorker> logger)
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
                var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
                var released = await cart.ReleaseExpiredHoldsAsync();
                if (released > 0)
                    _logger.LogInformation("CartHoldWorker released {Count} expired hold(s).", released);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CartHoldWorker pass failed.");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }
}
