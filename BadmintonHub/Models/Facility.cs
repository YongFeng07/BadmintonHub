using System.ComponentModel.DataAnnotations;

namespace BadmintonHub.Models;

/// <summary>A badminton facility (venue). One facility owns many courts.</summary>
public class Facility
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(200)]
    public string Address { get; set; } = string.Empty;

    [Phone, StringLength(20)]
    public string? Phone { get; set; }

    [EmailAddress, StringLength(100)]
    public string? Email { get; set; }

    public TimeSpan OpeningTime { get; set; } = new(8, 0, 0);

    public TimeSpan ClosingTime { get; set; } = new(23, 0, 0);

    /// <summary>Comma-separated day abbreviations, e.g. "Mon,Tue,Wed,Thu,Fri,Sat,Sun".</summary>
    [Required, StringLength(50)]
    [Display(Name = "Operating Days")]
    public string OperatingDays { get; set; } = "Mon,Tue,Wed,Thu,Fri,Sat,Sun";

    [StringLength(1000)]
    public string? Rules { get; set; }

    public FacilityStatus Status { get; set; } = FacilityStatus.Open;

    public ICollection<Court> Courts { get; set; } = new List<Court>();
}
