using System.ComponentModel.DataAnnotations;

namespace SportHub.ViewModels;

/// <summary>SuperAdmin-only system settings form (revised spec).</summary>
public class SystemSettingsViewModel
{
    [Required, StringLength(100)]
    [Display(Name = "Site Name")]
    public string SiteName { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Site Announcement")]
    public string? SiteAnnouncement { get; set; }
}
