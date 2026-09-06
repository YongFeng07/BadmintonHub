namespace SportHub.Services;

public interface IEmailService
{
    /// <summary>
    /// Sends an HTML email. Returns true when it was actually delivered over SMTP;
    /// false when it was captured by the demo fallback instead (nothing was sent —
    /// the message stays visible on the admin demo mail page).
    /// </summary>
    Task<bool> SendAsync(string to, string subject, string htmlBody);
}
