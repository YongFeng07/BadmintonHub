using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

public class MyReservationsViewModel
{
    public List<Reservation> Upcoming { get; set; } = new();

    public List<Reservation> Completed { get; set; } = new();

    public List<Reservation> Cancelled { get; set; } = new();

    public DateOnly? FilterDate { get; set; }

    public string Tab { get; set; } = "upcoming";

    /// <summary>Calendar month currently displayed.</summary>
    public int Year { get; set; }

    public int Month { get; set; }

    /// <summary>Calendar day number → count of own reservations on that day.</summary>
    public Dictionary<int, int> ReservationsPerDay { get; set; } = new();

    // Booking insights charts (Phase E): monthly trends over the last 6 months,
    // the category split and the cancellation summary.

    public List<string> InsightMonthLabels { get; set; } = new();

    public List<int> InsightMonthlyBookings { get; set; } = new();

    public List<decimal> InsightMonthlySpend { get; set; } = new();

    public List<string> InsightCategoryLabels { get; set; } = new();

    public List<int> InsightCategoryCounts { get; set; } = new();

    public int InsightCancelled { get; set; }

    public int InsightCompleted { get; set; }

    public int InsightActive { get; set; }

    public double InsightCancellationRate { get; set; }
}
