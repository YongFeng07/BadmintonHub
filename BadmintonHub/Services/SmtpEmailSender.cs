using BadmintonHub.Data;
using BadmintonHub.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BadmintonHub.Services;

/// <summary>
/// Real SMTP delivery via MailKit. Configuration comes from the "Email:Smtp"
/// settings; the factory only wires this class when a Host is configured, so the
/// repository never needs (and must not contain) real mail credentials.
/// </summary>
public class SmtpEmailSender : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ApplicationDbContext _db; // captures failed sends for the demo mail page

    public SmtpEmailSender(IConfiguration config, ApplicationDbContext db)
    {
        _config = config;
        _db = db;
    }

    public async Task<bool> SendAsync(string to, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _config["Email:Smtp:FromName"] ?? "BadmintonHub",
            _config["Email:Smtp:FromAddress"] ?? "noreply@badmintonhub.my"));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            var host = _config["Email:Smtp:Host"];
            if (string.IsNullOrEmpty(host))
                throw new InvalidOperationException("Email:Smtp:Host is not configured.");

            using var client = new SmtpClient();
            var port = _config.GetValue("Email:Smtp:Port", 587);
            var secureOptions = _config.GetValue("Email:Smtp:EnableSsl", true)
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;
            await client.ConnectAsync(host, port, secureOptions);

            var username = _config["Email:Smtp:Username"];
            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, _config["Email:Smtp:Password"] ?? string.Empty);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return true;
        }
        catch (Exception)
        {
            // A failed send must never break the flow (e.g. booking): capture the
            // message in-app so it is still demonstrable and retryable.
            _db.DemoEmails.Add(new DemoEmail { To = to, Subject = subject, BodyHtml = htmlBody });
            await _db.SaveChangesAsync();
            return false;
        }
    }
}
