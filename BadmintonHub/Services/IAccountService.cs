namespace BadmintonHub.Services;

public interface IAccountService
{
    /// <summary>Registers a new Member account (registration never creates privileged roles).</summary>
    Task<(bool Success, string? Error)> RegisterAsync(string fullName, string email, string? phone, string password);

    /// <summary>Changes a user's password after verifying the current one.</summary>
    Task<(bool Success, string? Error)> ChangePasswordAsync(int userId, string currentPassword, string newPassword);

    /// <summary>
    /// Creates a single-use, expiring reset token. The reset link is only returned when the
    /// email exists, but the UI always shows the same neutral message (anti-enumeration).
    /// </summary>
    Task<(bool Success, string? Error, string? ResetLink)> RequestPasswordResetAsync(string email, string resetUrlBase);

    /// <summary>Validates a reset token and sets the new password. Tokens are single-use.</summary>
    Task<(bool Success, string? Error)> ResetPasswordAsync(int userId, string token, string newPassword);

    /// <summary>Admin action: clears a locked account (failed-login lockout management).</summary>
    Task<(bool Success, string? Error)> UnlockAsync(int userId);
}
