using SportHub.Data;
using SportHub.Models;

namespace SportHub.Services;

/// <summary>
/// Fallback used while no SMTP host is configured: the email is stored in the
/// DemoEmail table and shown on the admin demo mail page. The assignment demo
/// can therefore demonstrate the verification/reset emails without committing
/// any real mail credentials. Always returns false (nothing was delivered).
/// </summary>
public class DemoEmailSender : IEmailService
{
    private readonly ApplicationDbContext _db;

    public DemoEmailSender(ApplicationDbContext db) => _db = db;

    public async Task<bool> SendAsync(string to, string subject, string htmlBody,
        IReadOnlyCollection<EmailAttachment>? attachments = null)
    {
        _db.DemoEmails.Add(new DemoEmail
        {
            To = to,
            Subject = subject,
            BodyHtml = htmlBody,
            Attachments = attachments?
                .Select(a => new DemoEmailAttachment
                {
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    Data = a.Content
                })
                .ToList() ?? new List<DemoEmailAttachment>()
        });
        await _db.SaveChangesAsync();
        return false;
    }
}
