using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>
/// One pending line of a member's booking cart (revised spec): the chosen court,
/// date and time window, before checkout. Checkout re-validates every line
/// server-side and converts it into a reservation — the cart is never trusted.
/// </summary>
[Index(nameof(UserId), nameof(CourtId), nameof(Date), nameof(StartTime), IsUnique = true)]
public class CartItem
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public int CourtId { get; set; }

    public Court? Court { get; set; }

    [Display(Name = "Date")]
    public DateOnly Date { get; set; }

    [Display(Name = "Start Time")]
    public TimeOnly StartTime { get; set; }

    [Range(1, 4)]
    [Display(Name = "Duration (hours)")]
    public int DurationHours { get; set; } = 1;

    public DateTime AddedAt { get; set; } = DateTime.Now;
}
