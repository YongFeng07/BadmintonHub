using System.ComponentModel.DataAnnotations;
using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

public class AvailabilityGenerateViewModel : IValidatableObject
{
    public List<int> CourtIds { get; set; } = new();

    [Display(Name = "From Date")]
    public DateOnly FromDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Display(Name = "To Date")]
    public DateOnly ToDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(7));

    public AvailabilityStatus Status { get; set; } = AvailabilityStatus.Open;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CourtIds.Count == 0)
            yield return new ValidationResult("Select at least one court.", new[] { nameof(CourtIds) });

        if (ToDate < FromDate)
            yield return new ValidationResult("To Date cannot be before From Date.", new[] { nameof(ToDate) });

        if (FromDate < DateOnly.FromDateTime(DateTime.Today))
            yield return new ValidationResult("From Date cannot be in the past.", new[] { nameof(FromDate) });

        if (ToDate > DateOnly.FromDateTime(DateTime.Today.AddDays(60)))
            yield return new ValidationResult("Availability can be generated up to 60 days ahead.", new[] { nameof(ToDate) });
    }
}
