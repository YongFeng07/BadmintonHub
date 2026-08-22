using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Models;

/// <summary>
/// One bookable time slot of one court on one date (e.g. Court 01, 2026-08-23, 18:00-19:00).
/// Staff manage these per date; a reservation must fall inside an Open slot.
/// </summary>
[Index(nameof(CourtId), nameof(Date), nameof(StartTime), IsUnique = true)]
public class CourtAvailability
{
    public int Id { get; set; }

    public int CourtId { get; set; }

    public Court? Court { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public AvailabilityStatus Status { get; set; } = AvailabilityStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }
}
