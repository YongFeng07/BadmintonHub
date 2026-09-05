using System.Net;

namespace BadmintonHub.Services;

/// <summary>Small self-contained HTML templates for the account lifecycle emails.</summary>
public static class EmailTemplates
{
    private static string Shell(string title, string body) => $"""
        <div style="font-family:'Segoe UI',Arial,sans-serif;max-width:560px;margin:0 auto;border:1px solid #dde6e1;border-radius:12px;overflow:hidden">
          <div style="background:#0e3a2f;color:#ffffff;padding:18px 24px">
            <h2 style="margin:0;font-size:18px">🏸 {title}</h2>
          </div>
          <div style="padding:24px;color:#24312b">
            {body}
          </div>
          <div style="background:#f2f6f4;color:#6b7a72;padding:12px 24px;font-size:12px">
            This is an automated message from BadmintonHub. Please do not reply to this email.
          </div>
        </div>
        """;

    private static string Button(string link, string label) =>
        $"<p style='text-align:center;margin:20px 0'>" +
        $"<a href='{link}' style='display:inline-block;background:#17714f;color:#ffffff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600'>{label}</a></p>";

    /// <summary>Email verification mail: proves mailbox ownership before sign-in is allowed.</summary>
    public static string VerificationEmail(string fullName, string verifyUrl)
    {
        var name = WebUtility.HtmlEncode(fullName);
        var link = WebUtility.HtmlEncode(verifyUrl);
        return Shell("Verify your email address", $"""
            <p>Hi {name},</p>
            <p>Welcome to BadmintonHub! Please confirm your email address to activate your account and start booking.</p>
            {Button(link, "Verify my email")}
            <p style="font-size:12px;color:#6b7a72">If the button does not work, copy this link into your browser:<br />{link}</p>
            <p style="font-size:12px;color:#6b7a72">The link is valid for 24 hours.</p>
            """);
    }

    /// <summary>Password reset mail with the single-use, 30-minute reset link.</summary>
    public static string PasswordResetEmail(string fullName, string resetUrl)
    {
        var name = WebUtility.HtmlEncode(fullName);
        var link = WebUtility.HtmlEncode(resetUrl);
        return Shell("Reset your password", $"""
            <p>Hi {name},</p>
            <p>We received a request to reset your BadmintonHub password. The link below is valid for 30 minutes and can be used once.</p>
            {Button(link, "Reset my password")}
            <p style="font-size:12px;color:#6b7a72">If the button does not work, copy this link into your browser:<br />{link}</p>
            <p style="font-size:12px;color:#6b7a72">If you did not request this, you can safely ignore this email — your password will not change.</p>
            """);
    }
}
