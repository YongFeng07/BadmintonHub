using BadmintonHub.Data;
using BadmintonHub.Models;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _db;

    public AuthService(ApplicationDbContext db) => _db = db;

    public async Task<(User? User, string? Error)> AuthenticateAsync(string email, string password, string? ipAddress)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        // Unknown email: record the attempt but reveal no detail (prevents user enumeration).
        if (user == null)
        {
            _db.LoginAttempts.Add(new LoginAttempt { Email = email.Trim(), IpAddress = ipAddress, Success = false });
            await _db.SaveChangesAsync();
            return (null, "Invalid email or password.");
        }

        if (user.Status != UserStatus.Active)
            return (null, "This account is not active. Please contact the facility admin.");

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.Now)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((user.LockoutEnd.Value - DateTime.Now).TotalMinutes));
            return (null, $"Too many failed login attempts. The account is locked for {minutes} more minute(s).");
        }

        if (!PasswordHelper.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTime.Now.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;
            }
            _db.LoginAttempts.Add(new LoginAttempt { UserId = user.Id, Email = user.Email, IpAddress = ipAddress, Success = false });
            await _db.SaveChangesAsync();
            return (null, "Invalid email or password.");
        }

        // Success: reset the counter/lockout and stamp last login.
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTime.Now;
        _db.LoginAttempts.Add(new LoginAttempt { UserId = user.Id, Email = user.Email, IpAddress = ipAddress, Success = true });
        await _db.SaveChangesAsync();

        return (user, null);
    }
}
