using BadmintonHub.Models;

namespace BadmintonHub.Services;

public interface IAuthService
{
    /// <summary>
    /// Validates credentials, applies the failed-login lockout policy and
    /// records a LoginAttempt audit row. Returns the user on success, or null
    /// with a user-friendly error message.
    /// </summary>
    Task<(User? User, string? Error)> AuthenticateAsync(string email, string password, string? ipAddress);
}
