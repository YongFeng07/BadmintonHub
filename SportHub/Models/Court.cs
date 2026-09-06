using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>A bookable badminton court inside a facility.</summary>
[Index(nameof(CourtNumber), nameof(FacilityId), IsUnique = true)]
public class Court
{
    public int Id { get; set; }

    public int FacilityId { get; set; }

    public Facility? Facility { get; set; }

    /// <summary>Court number displayed to customers, e.g. "01".</summary>
    [Required, StringLength(20)]
    [Display(Name = "Court Number")]
    [RegularExpression(@"^\d{2}$", ErrorMessage = "Court number must be 2 digits, e.g. 01.")]
    public string CourtNumber { get; set; } = string.Empty;

    public CourtType CourtType { get; set; } = CourtType.Standard;

    public CourtStatus Status { get; set; } = CourtStatus.Available;

    [Required]
    [Range(0.01, 1000.00, ErrorMessage = "Hourly rate must be between RM 0.01 and RM 1000.")]
    [Column(TypeName = "decimal(10,2)")]
    [Display(Name = "Hourly Rate (RM)")]
    public decimal HourlyRate { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<CourtPhoto> Photos { get; set; } = new List<CourtPhoto>();
    public ICollection<CourtAvailability> Availabilities { get; set; } = new List<CourtAvailability>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
