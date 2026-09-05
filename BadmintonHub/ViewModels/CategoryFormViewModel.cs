using BadmintonHub.Models;
using System.ComponentModel.DataAnnotations;

namespace BadmintonHub.ViewModels;

/// <summary>Shared by the category Create and Edit forms.</summary>
public class CategoryFormViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(60)]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Description { get; set; }

    [Required]
    [StringLength(20)]
    [Display(Name = "Unit Label")]
    public string UnitLabel { get; set; } = "Court";

    [StringLength(10)]
    [Display(Name = "Icon (emoji)")]
    public string? Icon { get; set; }

    public CategoryStatus Status { get; set; } = CategoryStatus.Active;

    [Range(1, 999)]
    [Display(Name = "Display Order")]
    public int DisplayOrder { get; set; } = 1;

    public List<Category> Categories { get; set; } = new();
}
