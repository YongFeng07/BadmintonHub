using BadmintonHub.Data;
using BadmintonHub.Services;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Tests;

/// <summary>
/// The demo mail fallback: when no SMTP host is configured, outgoing mail is
/// captured in the DemoEmails table (viewable on the admin Demo Mail page) and
/// SendAsync reports false so the UI can show the demo verification link.
/// </summary>
public class DemoEmailSenderTests
{
    [Fact]
    public async Task SendAsync_CapturesMailAndReturnsFalse()
    {
        using var db = TestDb.Create();
        var sender = new DemoEmailSender(db);

        var delivered = await sender.SendAsync("new@test.local", "Verify your account", "<b>Hello</b>");

        Assert.False(delivered); // nothing was really mailed
        var mail = db.DemoEmails.Single();
        Assert.Equal("new@test.local", mail.To);
        Assert.Equal("Verify your account", mail.Subject);
        Assert.Contains("<b>Hello</b>", mail.BodyHtml);
        Assert.True(mail.CreatedAt > DateTime.Now.AddMinutes(-1));
    }
}
