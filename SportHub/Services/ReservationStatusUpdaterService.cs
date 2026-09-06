using SportHub.Data;
using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Services;

/// <summary>
/// M2 additional feature: automatic reservation status update.
/// Every 30 minutes, bookings whose date has passed move from Pending/Confirmed to
/// Completed, keeping reservation statuses consistent without manual work.
/// G-M3 adds the payment timeout on the same schedule: Pending reservations not
/// paid within 30 minutes are cancelled, their payment failed, their slot claims
/// released and the member notified.
/// </summary>
public class ReservationStatusUpdaterService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationStatusUpdaterService> _logger;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(30);

    /// <summary>G-M3: unpaid Pending reservations are released after this long.</summary>
    public static readonly TimeSpan PaymentTimeout = TimeSpan.FromMinutes(30);

    public ReservationStatusUpdaterService(IServiceScopeFactory scopeFactory, ILogger<ReservationStatusUpdaterService> logger)
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
                await UpdateOverdueReservationsAsync(stoppingToken);
                await ReleaseTimedOutPaymentsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic reservation status update failed.");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }

    private async Task UpdateOverdueReservationsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var updated = await db.Reservations
            .Where(r => r.ReservationDate < today &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReservationStatus.Completed)
                .SetProperty(r => r.UpdatedAt, DateTime.Now),
                stoppingToken);

        if (updated > 0)
            _logger.LogInformation("Auto-completed {Count} overdue reservation(s).", updated);
    }

    private async Task ReleaseTimedOutPaymentsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();
        var released = await reservations.ReleaseUnpaidPendingAsync(PaymentTimeout);
        if (released > 0)
            _logger.LogInformation("Released {Count} unpaid pending reservation(s) (30-minute timeout).", released);
    }
}
