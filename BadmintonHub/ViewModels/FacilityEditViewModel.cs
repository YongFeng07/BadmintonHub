using System.ComponentModel.DataAnnotations;
using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

public class FacilityEditViewModel : IValidatableObject
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

    [Display(Name = "Opening Time")]
    [DataType(DataType.Time)]
    public TimeSpan OpeningTime { get; set; }

    [Display(Name = "Closing Time")]
    [DataType(DataType.Time)]
    public TimeSpan ClosingTime { get; set; }

    /// <summary>Checkbox values "Mon"…"Sun"; joined into a comma string when saved.</summary>
    [Display(Name = "Operating Days")]
    public List<string> OperatingDays { get; set; } = new();

    [StringLength(1000)]
    public string? Rules { get; set; }

    public FacilityStatus Status { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ClosingTime <= OpeningTime)
            yield return new ValidationResult("Closing time must be after opening time.", new[] { nameof(ClosingTime) });

        if (OperatingDays.Count == 0)
            yield return new ValidationResult("Select at least one operating day.", new[] { nameof(OperatingDays) });
    }
}
