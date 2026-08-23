using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

/// <summary>Aggregated KPIs and chart series for the admin dashboard.</summary>
public class AdminDashboardViewModel
{
    public decimal TotalRevenue { get; set; }

    public int TodayReservations { get; set; }

    public int UpcomingReservations { get; set; }

    public int TotalMembers { get; set; }

    public int ActiveCourts { get; set; }

    /// <summary>Booked slots / open slots for today (0-100).</summary>
    public double TodayUtilizationPercent { get; set; }

    /// <summary>Revenue per day for the last 7 days (labels + data for Chart.js).</summary>
    public List<string> RevenueLabels { get; set; } = new();

    public List<decimal> RevenueData { get; set; } = new();

    /// <summary>Reservation count per court.</summary>
    public List<string> CourtLabels { get; set; } = new();

    public List<int> CourtData { get; set; } = new();

    /// <summary>Reservation count per starting hour (8-22).</summary>
    public List<string> PeakHourLabels { get; set; } = new();

    public List<int> PeakHourData { get; set; } = new();

    public List<Reservation> RecentReservations { get; set; } = new();
}

/// <summary>Filter/sort/pagination state for the reservation administration grid.</summary>
public class AdminReservationsIndexViewModel
{
    public List<Reservation> Reservations { get; set; } = new();

    public string? Search { get; set; }

    public string? Status { get; set; }

    public int? CourtId { get; set; }

    public DateOnly? Date { get; set; }

    public string Sort { get; set; } = "date_desc";

    public int Page { get; set; } = 1;

    public const int PageSize = 10;

    public int TotalCount { get; set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public List<Court> Courts { get; set; } = new();

    public bool HasFilter => !string.IsNullOrEmpty(Search) || !string.IsNullOrEmpty(Status) ||
                             CourtId.HasValue || Date.HasValue;
}

/// <summary>Report filters + aggregated results.</summary>
public class AdminReportsViewModel
{
    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public decimal Revenue { get; set; }

    public int BookingCount { get; set; }

    public decimal AverageBookingValue { get; set; }

    public int CancellationCount { get; set; }

    public List<string> RevenueByDayLabels { get; set; } = new();

    public List<decimal> RevenueByDayData { get; set; } = new();

    public List<string> UtilizationCourtLabels { get; set; } = new();

    public List<double> UtilizationCourtData { get; set; } = new();

    public List<string> PopularCourtLabels { get; set; } = new();

    public List<int> PopularCourtData { get; set; } = new();

    public List<string> PeakHourLabels { get; set; } = new();

    public List<int> PeakHourData { get; set; } = new();

    public List<Reservation> RevenueRows { get; set; } = new();
}

/// <summary>Filter/pagination state for the user administration grid.</summary>
public class AdminUsersIndexViewModel
{
    public List<User> Users { get; set; } = new();

    public string? Search { get; set; }

    public string? RoleFilter { get; set; }

    public string? StatusFilter { get; set; }

    public int Page { get; set; } = 1;

    public const int PageSize = 10;

    public int TotalCount { get; set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public int LockedCount => Users.Count(u => u.LockoutEnd.HasValue && u.LockoutEnd > DateTime.Now);
}
