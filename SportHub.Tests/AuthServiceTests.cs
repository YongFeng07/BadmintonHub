using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// M3 security rules: manual cookie authentication with PBKDF2 verification,
/// 3-strike failed-login lockout, account-status blocking and the
/// revised-spec email-verification gate.
/// </summary>
public class AuthServiceTests
{
    private static AuthService CreateService(ApplicationDbContext db) => new(db);

    [Fact]
    public async Task Authenticate_UnknownEmail_ReturnsGenericErrorAndLogsAttempt()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);

        var (user, error, verificationRequired) = await service.AuthenticateAsync("nobody@test.local", "Whatever1", null);

        Assert.Null(user);
        Assert.Equal("Invalid email or password.", error);
        Assert.False(verificationRequired);
        Assert.Single(db.LoginAttempts); // attempt recorded, no account detail leaked
        Assert.False(db.LoginAttempts.Single().Success);
    }

    [Fact]
    public async Task Authenticate_WrongPassword_IncrementsFailedCounter()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);

        var (user, error, verificationRequired) = await service.AuthenticateAsync("member@test.local", "WrongPass1", null);

        Assert.Null(user);
        Assert.Equal("Invalid email or password.", error);
        Assert.False(verificationRequired);
        Assert.Equal(1, db.Users.Single(u => u.Email == "member@test.local").FailedLoginAttempts);
    }

    [Fact]
    public async Task Authenticate_ThreeFailures_LocksAccountEvenWithCorrectPassword()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);

        for (var i = 0; i < 3; i++)
            await service.AuthenticateAsync("member@test.local", $"Wrong{i}Pass", null);

        var member = db.Users.Single(u => u.Email == "member@test.local");
        Assert.NotNull(member.LockoutEnd);
        Assert.True(member.LockoutEnd > DateTime.Now);

        // Correct password is refused while the lockout is active.
        var (user, error, _) = await service.AuthenticateAsync("member@test.local", "Member@123", null);
        Assert.Null(user);
        Assert.Contains("Too many failed login attempts", error);
    }

    [Fact]
    public async Task Authenticate_DeactivatedAccount_RefusedWithMessage()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        var member = db.Users.Single(u => u.Email == "member@test.local");
        member.Status = UserStatus.Deactivated;
        await db.SaveChangesAsync();

        var (user, error, _) = await service.AuthenticateAsync("member@test.local", "Member@123", null);

        Assert.Null(user);
        Assert.Contains("not active", error);
    }

    [Fact]
    public async Task Authenticate_UnverifiedEmail_ReturnsVerificationRequired()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        var member = db.Users.Single(u => u.Email == "member@test.local");
        member.EmailVerified = false;
        await db.SaveChangesAsync();

        var (user, error, verificationRequired) = await service.AuthenticateAsync("member@test.local", "Member@123", null);

        Assert.Null(user); // correct password, but the mailbox is not verified yet
        Assert.Contains("verify", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(verificationRequired); // login page offers the resend link
    }

    [Fact]
    public async Task Authenticate_CorrectPassword_ResetsCounterAndStampsLogin()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        await service.AuthenticateAsync("member@test.local", "WrongPass1", null); // 1 failure first

        var (user, error, verificationRequired) = await service.AuthenticateAsync("member@test.local", "Member@123", null);

        Assert.NotNull(user);
        Assert.Null(error);
        Assert.False(verificationRequired);
        Assert.Equal(0, user!.FailedLoginAttempts);
        Assert.Null(user.LockoutEnd);
        Assert.NotNull(user.LastLoginAt);
        Assert.Single(db.LoginAttempts, a => a.Success); // success attempt logged
    }
}
