using System.ComponentModel.DataAnnotations;

namespace SportHub.Models;

/// <summary>
/// Outbound email captured in-app. Rows are written only while no SMTP server is
/// configured (or a real send fails), so the assignment demo can show the exact
/// verification / reset emails without committing any mail credentials.
/// </summary>
public class DemoEmail
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string To { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string BodyHtml { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
