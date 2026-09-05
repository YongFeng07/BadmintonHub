using System.ComponentModel.DataAnnotations;
using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

/// <summary>Shared by the court Create and Edit forms.</summary>
public class CourtFormViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Facility")]
    public int FacilityId { get; set; }

    public List<Facility> Facilities { get; set; } = new();

    [Required]
    [StringLength(20)]
    [RegularExpression(@"^\d{2}$", ErrorMessage = "Court number must be 2 digits, e.g. 01.")]
    [Display(Name = "Court Number")]
    public string CourtNumber { get; set; } = string.Empty;

    [Display(Name = "Court Type")]
    public CourtType CourtType { get; set; } = CourtType.Standard;

    public CourtStatus Status { get; set; } = CourtStatus.Available;

    [Required]
    [Range(0.01, 1000.00, ErrorMessage = "Hourly rate must be between RM 0.01 and RM 1000.")]
    [Display(Name = "Hourly Rate (RM)")]
    public decimal HourlyRate { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }
}
