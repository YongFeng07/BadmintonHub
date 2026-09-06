namespace SportHub.Services;

/// <summary>
/// Chooses the outbound email implementation from configuration: real SMTP when
/// Email:Smtp:Host is set, otherwise the in-app demo capture. The user's
/// appsettings only needs a Host to switch to real delivery.
/// </summary>
public static class EmailServiceFactory
{
    public static IEmailService Create(IConfiguration config, Data.ApplicationDbContext db)
    {
        var host = config["Email:Smtp:Host"];
        return string.IsNullOrWhiteSpace(host) ? new DemoEmailSender(db) : new SmtpEmailSender(config, db);
    }
}
