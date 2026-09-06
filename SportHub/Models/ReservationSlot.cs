using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>
/// Atomic claim of one hourly slot by a reservation (G-M3 stock deduction). Rows are
/// inserted inside the transaction that creates the booking and deleted when the
/// booking is cancelled or rejected. The unique (CourtId, Date, StartTime) index is
/// the database-level backstop: two concurrent checkouts of the same hour can never
/// both succeed — the loser hits the unique constraint and gets a friendly error.
/// </summary>
[Index(nameof(CourtId), nameof(Date), nameof(StartTime), IsUnique = true)]
public class ReservationSlot
{
    public int Id { get; set; }

    public int CourtId { get; set; }

    public Court? Court { get; set; }

    [Display(Name = "Date")]
    public DateOnly Date { get; set; }

    [Display(Name = "Start Time")]
    public TimeOnly StartTime { get; set; }

    /// <summary>The reservation that owns this hour; slots are released with it.</summary>
    public int ReservationId { get; set; }

    public Reservation Reservation { get; set; } = null!;
}
