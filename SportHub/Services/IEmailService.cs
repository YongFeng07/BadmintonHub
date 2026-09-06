namespace SportHub.Services;

/// <summary>An optional email attachment (G-M5), e.g. the PDF e-receipt.</summary>
public record EmailAttachment(string FileName, byte[] Content, string ContentType);

public interface IEmailService
{
    /// <summary>
    /// Sends an HTML email with any number of attachments (the PDF e-receipts).
    /// Returns true when it was actually delivered over SMTP; false when it was
    /// captured by the demo fallback instead (nothing was sent — the message stays
    /// visible on the admin demo mail page, attachments included).
    /// </summary>
    Task<bool> SendAsync(string to, string subject, string htmlBody,
        IReadOnlyCollection<EmailAttachment>? attachments = null);
}
