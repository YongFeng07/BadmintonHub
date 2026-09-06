using System.ComponentModel.DataAnnotations;

namespace SportHub.Models;

/// <summary>
/// Outbound email captured in-app. Rows are written only while no SMTP server is
/// configured (or a real send fails), so the assignment demo can show the exact
/// verification / reset / receipt emails without committing any mail credentials.
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

    /// <summary>G-M5: attachments (e.g. the PDF e-receipts) captured with the email.</summary>
    public List<DemoEmailAttachment> Attachments { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>One file attached to a captured demo email.</summary>
public class DemoEmailAttachment
{
    public int Id { get; set; }

    public int DemoEmailId { get; set; }

    public DemoEmail? DemoEmail { get; set; }

    [Required, StringLength(200)]
    public string FileName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    [Required]
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
