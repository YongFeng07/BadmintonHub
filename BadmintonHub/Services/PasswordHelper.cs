using System.Security.Cryptography;

namespace BadmintonHub.Services;

/// <summary>
/// PBKDF2 password hashing implemented manually with a random salt.
/// The assignment forbids ASP.NET Core Identity, so no Identity hashing package is used.
/// </summary>
public static class PasswordHelper
{
    private const int Iterations = 100_000;

    /// <summary>Returns (base64 hash, base64 salt) for a new password.</summary>
    public static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>Constant-time verification of a password against a stored hash/salt pair.</summary>
    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        try
        {
            var salt = Convert.FromBase64String(storedSalt);
            var expected = Convert.FromBase64String(storedHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            // Corrupt stored hash: fail closed.
            return false;
        }
    }

    /// <summary>Shared password policy for registration, reset and change-password. Null when valid.</summary>
    public static string? ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return "Password must be at least 8 characters long.";
        if (password.Length > 100)
            return "Password must be at most 100 characters long.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Password must contain at least one letter and one digit.";
        return null;
    }
}
