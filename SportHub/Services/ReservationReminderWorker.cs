namespace SportHub.Services;

/// <summary>
/// G-M5: hourly pass over confirmed bookings to remind members whose booking
/// starts within the next 24 hours (one email + one notification per booking,
/// guarded by Reservation.ReminderSentAt).
/// </summary>
public class ReservationReminderWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ReservationReminderWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    public ReservationReminderWorker(IServiceProvider services, ILogger<ReservationReminderWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var reminder = scope.ServiceProvider.GetRequiredService<ReminderService>();
                var reminded = await reminder.SendRemindersAsync();
                if (reminded > 0)
                    _logger.LogInformation("Booking reminders sent to {Count} member(s).", reminded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Booking reminder pass failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
