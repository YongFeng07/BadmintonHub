namespace BadmintonHub.Services;

public interface IAccountService
{
    /// <summary>
    /// Registers a new Member account (registration never creates privileged roles).
    /// The account starts unverified; a 24-hour verification token is returned so the
    /// caller can email the confirmation link.
    /// </summary>
    Task<(bool Success, string? Error, string? VerificationToken)> RegisterAsync(
        string fullName, string email, string? phone, string password);

    /// <summary>Confirms a mailbox via its verification token (single-use, 24 h).</summary>
    Task<(bool Success, string? Error)> VerifyEmailAsync(int userId, string token);

    /// <summary>
    /// Issues a fresh verification token for an existing account. Neutral result when
    /// the email is unknown (anti-enumeration); null token when already verified.
    /// </summary>
    Task<(bool Success, string? Error, string? VerificationToken)> RequestVerificationEmailAsync(string email);

    /// <summary>Changes a user's password after verifying the current one.</summary>
    Task<(bool Success, string? Error)> ChangePasswordAsync(int userId, string currentPassword, string newPassword);

    /// <summary>
    /// Creates a single-use, expiring reset token. The reset link is only returned when the
    /// email exists, but the UI always shows the same neutral message (anti-enumeration).
    /// </summary>
    Task<(bool Success, string? Error, string? ResetLink)> RequestPasswordResetAsync(string email, string resetUrlBase);

    /// <summary>
    /// Validates a reset token and sets the new password. Tokens are single-use; a
    /// successful reset also confirms the mailbox (the link arrived by email).
    /// </summary>
    Task<(bool Success, string? Error)> ResetPasswordAsync(int userId, string token, string newPassword);

    /// <summary>Admin action: clears a locked account (failed-login lockout management).</summary>
    Task<(bool Success, string? Error)> UnlockAsync(int userId);
}
