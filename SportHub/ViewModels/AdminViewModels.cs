using SportHub.Models;
using System.ComponentModel.DataAnnotations;

namespace SportHub.ViewModels;

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

    // Booking/revenue reports (Phase E): monthly trends over the last 12 months
    // (independent of the range filter), the category split and the cancellation
    // rate within the selected range.

    public List<string> MonthlyBookingLabels { get; set; } = new();

    public List<int> MonthlyBookingData { get; set; } = new();

    public List<string> MonthlyRevenueLabels { get; set; } = new();

    public List<decimal> MonthlyRevenueData { get; set; } = new();

    public List<string> CategoryLabels { get; set; } = new();

    public List<int> CategoryData { get; set; } = new();

    public double CancellationRate { get; set; }

    public int CancelledTotal { get; set; }

    public int ActiveTotal { get; set; }

    public int CompletedTotal { get; set; }
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

/// <summary>Admin-side member maintenance form (P2): basic profile fields only —
/// passwords are never edited in-place; a reset link flow or SuperAdmin reset applies.</summary>
public class AdminUserEditViewModel
{
    public int Id { get; set; }

    [Required, StringLength(100), Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(20)]
    public string? Phone { get; set; }

    /// <summary>Read-only context shown next to the form.</summary>
    public Role Role { get; set; }

    public UserStatus Status { get; set; }

    public string? PhotoUrl { get; set; }
}

/// <summary>Member details page (P2): the account plus its booking/payment stats.</summary>
public class AdminUserDetailsViewModel
{
    public User User { get; set; } = null!;

    public int ReservationCount { get; set; }

    public int ConfirmedCount { get; set; }

    public int CancelledCount { get; set; }

    public decimal TotalPaid { get; set; }
}

/// <summary>SuperAdmin-only admin-account listing (P2).</summary>
public class AdminAccountIndexViewModel
{
    public List<User> Accounts { get; set; } = new();

    public string? Search { get; set; }
}

/// <summary>SuperAdmin-only admin-account creation form (P2).</summary>
public class AdminAccountCreateViewModel
{
    [Required, StringLength(100), Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(20)]
    public string? Phone { get; set; }

    [Required, DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public Role Role { get; set; } = Role.Admin;
}

/// <summary>SuperAdmin-only admin-account edit form (P2).</summary>
public class AdminAccountEditViewModel
{
    public int Id { get; set; }

    [Required, StringLength(100), Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(20)]
    public string? Phone { get; set; }

    public Role Role { get; set; }

    public UserStatus Status { get; set; }

    public string? PhotoUrl { get; set; }
}

/// <summary>SuperAdmin-only password reset form (P2).</summary>
public class AdminResetPasswordViewModel
{
    public int Id { get; set; }

    [Required, DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
