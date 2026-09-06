using System.ComponentModel.DataAnnotations;

namespace SportHub.ViewModels;

public class ReservationCreateViewModel
{
    [Required(ErrorMessage = "Please select a date.")]
    [Display(Name = "Date")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Range(1, int.MaxValue, ErrorMessage = "Please select a court.")]
    [Display(Name = "Court")]
    public int CourtId { get; set; }

    [Display(Name = "Start Time")]
    public TimeOnly StartTime { get; set; }

    [Range(1, 4, ErrorMessage = "Duration must be between 1 and 4 hours.")]
    [Display(Name = "Duration (hours)")]
    public int DurationHours { get; set; } = 1;

    [StringLength(500)]
    public string? Notes { get; set; }
}
