using System.Security.Cryptography;
using System.Text;
using BadmintonHub.Data;
using BadmintonHub.Models;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Services;

public class AccountService : IAccountService
{
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(30);

    private readonly ApplicationDbContext _db;

    public AccountService(ApplicationDbContext db) => _db = db;

    public async Task<(bool Success, string? Error)> RegisterAsync(string fullName, string email, string? phone, string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            return (false, "This email address is already registered.");

        var passwordError = PasswordHelper.ValidatePassword(password);
        if (passwordError != null)
            return (false, passwordError);

        var (hash, salt) = PasswordHelper.HashPassword(password);
        var user = new User
        {
            FullName = fullName.Trim(),
            Email = email.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            // Registration always creates Members; Admin/Staff accounts are provisioned by seed only.
            Role = Role.Member,
            Status = UserStatus.Active
        };
        _db.Users.Add(user);

        _db.Notifications.Add(new Notification
        {
            User = user,
            Title = "Welcome to BadmintonHub",
            Message = "Your member account is ready. Browse the courts and book your first session!",
            Type = NotificationType.System
        });

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        if (!PasswordHelper.Verify(currentPassword, user.PasswordHash, user.PasswordSalt))
            return (false, "Current password is incorrect.");

        var passwordError = PasswordHelper.ValidatePassword(newPassword);
        if (passwordError != null)
            return (false, passwordError);

        (user.PasswordHash, user.PasswordSalt) = PasswordHelper.HashPassword(newPassword);
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, string? ResetLink)> RequestPasswordResetAsync(string email, string resetUrlBase)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        // Same response whether or not the account exists: prevents email enumeration.
        if (user == null)
            return (true, null, null);

        // Invalidate any previously issued unused tokens.
        await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.IsUsed)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsUsed, true));

        // Only the SHA-256 hash of the raw token is stored.
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTime.Now.Add(ResetTokenLifetime)
        });
        await _db.SaveChangesAsync();

        var link = $"{resetUrlBase}?userId={user.Id}&token={rawToken}";
        return (true, null, link);
    }

    public async Task<(bool Success, string? Error)> ResetPasswordAsync(int userId, string token, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return (false, "This reset link is invalid or has expired. Please request a new one.");

        var tokenHash = HashToken(token);
        var tokenRecord = await _db.PasswordResetTokens.FirstOrDefaultAsync(t =>
            t.UserId == userId && t.TokenHash == tokenHash && !t.IsUsed && t.ExpiresAt > DateTime.Now);

        if (tokenRecord == null)
            return (false, "This reset link is invalid or has expired. Please request a new one.");

        var passwordError = PasswordHelper.ValidatePassword(newPassword);
        if (passwordError != null)
            return (false, passwordError);

        (user.PasswordHash, user.PasswordSalt) = PasswordHelper.HashPassword(newPassword);
        user.UpdatedAt = DateTime.Now;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null; // a successful reset also clears a lockout

        tokenRecord.IsUsed = true; // single-use
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UnlockAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        user.LockoutEnd = null;
        user.FailedLoginAttempts = 0;
        user.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
