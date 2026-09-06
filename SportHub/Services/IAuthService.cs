using SportHub.Models;

namespace SportHub.Services;

public interface IAuthService
{
    /// <summary>
    /// Validates credentials, applies the failed-login lockout policy (3 attempts,
    /// revised spec) and records a LoginAttempt audit row. Returns the user on
    /// success, or null with a user-friendly error message; EmailVerificationRequired
    /// is true when the password was correct but the email has not been verified yet.
    /// </summary>
    Task<(User? User, string? Error, bool EmailVerificationRequired)> AuthenticateAsync(
        string email, string password, string? ipAddress);
}
