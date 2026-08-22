using System.ComponentModel.DataAnnotations;

namespace BadmintonHub.Models;

/// <summary>One photo of a court; courts can have several photos for the gallery.</summary>
public class CourtPhoto
{
    public int Id { get; set; }

    public int CourtId { get; set; }

    public Court? Court { get; set; }

    /// <summary>Site-relative path, e.g. "/images/courts/court-01.jpg".</summary>
    [Required, StringLength(255)]
    public string FilePath { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Caption { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.Now;
}
