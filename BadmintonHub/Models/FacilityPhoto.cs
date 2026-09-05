using System.ComponentModel.DataAnnotations;

namespace BadmintonHub.Models;

/// <summary>A facility photo (revised spec: facility CRUD with photos).</summary>
public class FacilityPhoto
{
    public int Id { get; set; }

    public int FacilityId { get; set; }

    public Facility? Facility { get; set; }

    [Required]
    public string FilePath { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Caption { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }
}
