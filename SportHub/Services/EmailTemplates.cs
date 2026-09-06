using System.Net;

namespace SportHub.Services;

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
            This is an automated message from SportHub. Please do not reply to this email.
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
            <p>Welcome to SportHub! Please confirm your email address to activate your account and start booking.</p>
            {Button(link, "Verify my email")}
            <p style="font-size:12px;color:#6b7a72">If the button does not work, copy this link into your browser:<br />{link}</p>
            <p style="font-size:12px;color:#6b7a72">The link is valid for 24 hours.</p>
            """);
    }

    /// <summary>Payment confirmation sent after a checkout batch (or ToyyibPay bill) is paid.</summary>
    public static string PaymentConfirmationEmail(string fullName, int bookingCount)
    {
        var name = WebUtility.HtmlEncode(fullName);
        return Shell("Payment received", $"""
            <p>Hi {name},</p>
            <p>Thank you — we have received your payment. <strong>{bookingCount} booking(s)</strong> are now confirmed.</p>
            <p>You can see the details (including your QR entry code) on the <strong>My Reservations</strong> page.</p>
            <p>See you on court! 🏸</p>
            """);
    }

    /// <summary>Password reset mail with the single-use, 30-minute reset link.</summary>
    public static string PasswordResetEmail(string fullName, string resetUrl)
    {
        var name = WebUtility.HtmlEncode(fullName);
        var link = WebUtility.HtmlEncode(resetUrl);
        return Shell("Reset your password", $"""
            <p>Hi {name},</p>
            <p>We received a request to reset your SportHub password. The link below is valid for 30 minutes and can be used once.</p>
            {Button(link, "Reset my password")}
            <p style="font-size:12px;color:#6b7a72">If the button does not work, copy this link into your browser:<br />{link}</p>
            <p style="font-size:12px;color:#6b7a72">If you did not request this, you can safely ignore this email — your password will not change.</p>
            """);
    }

    // ---------- G-M5 booking lifecycle emails ----------

    /// <summary>Sent when a pending booking is created (payment still to come).</summary>
    public static string BookingReceivedEmail(string fullName, string reference, string court,
        string dateTime, string total)
    {
        var name = WebUtility.HtmlEncode(fullName);
        return Shell("Booking received", $"""
            <p>Hi {name},</p>
            <p>Your booking request <strong>{WebUtility.HtmlEncode(reference)}</strong> has been received.</p>
            <p>Court: <strong>{WebUtility.HtmlEncode(court)}</strong><br />
               When: <strong>{WebUtility.HtmlEncode(dateTime)}</strong><br />
               Total: <strong>RM {WebUtility.HtmlEncode(total)}</strong></p>
            <p>Complete the payment to confirm your booking — unpaid bookings are released automatically after 30 minutes.</p>
            """);
    }

    /// <summary>Sent when payment lands and the booking becomes confirmed.</summary>
    public static string BookingConfirmedEmail(string fullName, string reference, string court,
        string dateTime)
    {
        var name = WebUtility.HtmlEncode(fullName);
        return Shell("Booking confirmed", $"""
            <p>Hi {name},</p>
            <p>Payment received — booking <strong>{WebUtility.HtmlEncode(reference)}</strong> is now confirmed.</p>
            <p>Court: <strong>{WebUtility.HtmlEncode(court)}</strong><br />
               When: <strong>{WebUtility.HtmlEncode(dateTime)}</strong></p>
            <p>Your PDF e-receipt is attached to this email. See you on court! 🏸</p>
            """);
    }

    /// <summary>Sent when staff rejects a pending booking.</summary>
    public static string BookingRejectedEmail(string fullName, string reference)
    {
        var name = WebUtility.HtmlEncode(fullName);
        return Shell("Booking not accepted", $"""
            <p>Hi {name},</p>
            <p>Unfortunately booking <strong>{WebUtility.HtmlEncode(reference)}</strong> could not be accepted.
               No payment was taken for it.</p>
            <p>You can browse other available courts and book again anytime.</p>
            """);
    }

    /// <summary>Sent when a booking is cancelled (member or staff), with refund note.</summary>
    public static string BookingCancelledEmail(string fullName, string reference, bool refunded, string? reason)
    {
        var name = WebUtility.HtmlEncode(fullName);
        var refundLine = refunded
            ? "<p>Your payment has been <strong>refunded</strong>.</p>"
            : "<p>No payment was made for this booking.</p>";
        var reasonLine = string.IsNullOrWhiteSpace(reason) ? string.Empty :
            $"<p>Reason: {WebUtility.HtmlEncode(reason)}</p>";
        return Shell("Booking cancelled", $"""
            <p>Hi {name},</p>
            <p>Booking <strong>{WebUtility.HtmlEncode(reference)}</strong> has been cancelled.</p>
            {reasonLine}
            {refundLine}
            """);
    }

    /// <summary>Sent by the payment-timeout worker when an unpaid booking is released.</summary>
    public static string BookingReleasedEmail(string fullName, string reference)
    {
        var name = WebUtility.HtmlEncode(fullName);
        return Shell("Booking released", $"""
            <p>Hi {name},</p>
            <p>Booking <strong>{WebUtility.HtmlEncode(reference)}</strong> was released because payment was not
               received within 30 minutes. The slots are available again if you would like to rebook.</p>
            """);
    }

    /// <summary>Sent when staff complete a booking (session finished).</summary>
    public static string BookingCompletedEmail(string fullName, string reference)
    {
        var name = WebUtility.HtmlEncode(fullName);
        return Shell("Booking completed", $"""
            <p>Hi {name},</p>
            <p>Booking <strong>{WebUtility.HtmlEncode(reference)}</strong> has been marked as completed.
               Thank you for playing at SportHub — we hope to see you again soon! 🏸</p>
            """);
    }

    /// <summary>24-hour reminder for an upcoming confirmed booking (G-M5 reminder worker).</summary>
    public static string BookingReminderEmail(string fullName, string reference, string court,
        string dateTime)
    {
        var name = WebUtility.HtmlEncode(fullName);
        return Shell("Your booking starts soon", $"""
            <p>Hi {name},</p>
            <p>Just a reminder — your booking <strong>{WebUtility.HtmlEncode(reference)}</strong> starts within the next 24 hours.</p>
            <p>Court: <strong>{WebUtility.HtmlEncode(court)}</strong><br />
               When: <strong>{WebUtility.HtmlEncode(dateTime)}</strong></p>
            <p>Please arrive 10 minutes early and show your booking QR code at the counter.</p>
            """);
    }

    /// <summary>Cover mail for an attached PDF e-receipt ("email me my receipt").</summary>
    public static string ReceiptEmail(string fullName, string reference)
    {
        var name = WebUtility.HtmlEncode(fullName);
        return Shell("Your e-receipt", $"""
            <p>Hi {name},</p>
            <p>Here is the PDF e-receipt for booking <strong>{WebUtility.HtmlEncode(reference)}</strong>.
               It is attached to this email.</p>
            <p>You can also download it anytime from the reservation details page.</p>
            """);
    }
}
