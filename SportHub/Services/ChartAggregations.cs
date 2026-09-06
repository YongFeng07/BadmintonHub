using SportHub.Models;

namespace SportHub.Services;

/// <summary>
/// Pure aggregation helpers behind the Phase E charts (member booking insights
/// and the admin booking/revenue reports). Static so both callers share one
/// implementation of each metric.
/// </summary>
public static class ChartAggregations
{
    /// <summary>"MMM yyyy" labels for the last <paramref name="months"/> calendar months ending at <paramref name="today"/>.</summary>
    public static List<string> MonthLabels(DateOnly today, int months)
    {
        var cursor = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var labels = new List<string>();
        for (var i = 0; i < months; i++)
        {
            labels.Add(cursor.ToString("MMM yyyy"));
            cursor = cursor.AddMonths(1);
        }
        return labels;
    }

    private static int MonthKey(int year, int month) => year * 12 + (month - 1);

    /// <summary>Reservation counts per calendar month over the last <paramref name="months"/> months.</summary>
    public static (List<string> Labels, List<int> Counts) MonthlyBookings(
        IEnumerable<Reservation> reservations, DateOnly today, int months)
    {
        var floor = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var buckets = reservations
            .Where(r => r.ReservationDate >= floor)
            .GroupBy(r => MonthKey(r.ReservationDate.Year, r.ReservationDate.Month))
            .ToDictionary(g => g.Key, g => g.Count());

        var labels = MonthLabels(today, months);
        var counts = new List<int>();
        var cursor = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        for (var i = 0; i < months; i++)
        {
            counts.Add(buckets.GetValueOrDefault(MonthKey(cursor.Year, cursor.Month)));
            cursor = cursor.AddMonths(1);
        }
        return (labels, counts);
    }

    /// <summary>Paid amounts per calendar month (by payment date) over the last <paramref name="months"/> months.</summary>
    public static (List<string> Labels, List<decimal> Amounts) MonthlyRevenue(
        IEnumerable<Payment> paidPayments, DateOnly today, int months)
    {
        var floor = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var buckets = paidPayments
            .Where(p => p.PaidAt.HasValue && DateOnly.FromDateTime(p.PaidAt.Value) >= floor)
            .GroupBy(p => MonthKey(p.PaidAt!.Value.Year, p.PaidAt.Value.Month))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var labels = MonthLabels(today, months);
        var amounts = new List<decimal>();
        var cursor = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        for (var i = 0; i < months; i++)
        {
            amounts.Add(buckets.GetValueOrDefault(MonthKey(cursor.Year, cursor.Month)));
            cursor = cursor.AddMonths(1);
        }
        return (labels, amounts);
    }

    /// <summary>Booking counts grouped by facility category (court → facility → category).</summary>
    public static (List<string> Labels, List<int> Counts) ByCategory(IEnumerable<Reservation> reservations)
    {
        var grouped = reservations
            .GroupBy(r => r.Court?.Facility?.Category?.Name ?? "Other")
            .OrderByDescending(g => g.Count())
            .ToList();

        return (grouped.Select(g => g.Key).ToList(), grouped.Select(g => g.Count()).ToList());
    }

    /// <summary>
    /// Cancellation summary: cancelled = Cancelled + Rejected; the rate is the
    /// share of all reservations those represent (percent, 1 decimal).
    /// </summary>
    public static (int Cancelled, int Completed, int Active, double Rate) CancellationSummary(
        IEnumerable<Reservation> reservations)
    {
        var list = reservations.ToList();
        var cancelled = list.Count(r => r.Status is ReservationStatus.Cancelled or ReservationStatus.Rejected);
        var completed = list.Count(r => r.Status == ReservationStatus.Completed);
        var active = list.Count - cancelled - completed;
        var rate = list.Count > 0 ? Math.Round(cancelled / (double)list.Count * 100d, 1) : 0d;
        return (cancelled, completed, active, rate);
    }
}
