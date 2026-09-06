using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace SportHub.Models;

/// <summary>
/// Facility category (revised spec): Badminton Court, Indoor Basketball Court,
/// Volleyball Court, Table Tennis Room, Football / Futsal Pitch, Tennis Court,
/// Squash Court, Olympic-sized Swimming Pool, Gymnasium, Pickleball, Indoor
/// Running Track. One category owns many facilities; <see cref="UnitLabel"/>
/// names the bookable unit inside them ("Court", "Lane", "Table", "Track") —
/// the Court entity remains the single bookable unit across categories.
/// </summary>
[Index(nameof(Name), IsUnique = true)]
public class Category
{
    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Description { get; set; }

    /// <summary>Display label of one bookable unit, e.g. "Court", "Lane", "Table".</summary>
    [Required, StringLength(20)]
    [Display(Name = "Unit Label")]
    public string UnitLabel { get; set; } = "Court";

    /// <summary>Emoji shown on catalog cards (optional).</summary>
    [StringLength(10)]
    public string? Icon { get; set; }

    public CategoryStatus Status { get; set; } = CategoryStatus.Active;

    public int DisplayOrder { get; set; }

    public ICollection<Facility> Facilities { get; set; } = new List<Facility>();
}
