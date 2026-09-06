using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>
/// Key/value system settings (revised spec: SuperAdmin-only "system settings" area).
/// Small enough to stay on one table; values are read by the SiteSettingsViewComponent.
/// </summary>
[Index(nameof(Key), IsUnique = true)]
public class SystemSetting
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Key { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
