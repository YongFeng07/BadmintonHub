using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Tests;

/// <summary>
/// Revised-spec account flows: registration with email verification tokens
/// (hashed at rest, 24h expiry, anti-enumeration resend) and the password
/// reset that proves the mailbox and thereby verifies the account.
/// </summary>
public class AccountServiceTests
{
    private static AccountService Create(ApplicationDbContext db) => new(db);

    [Fact]
    public async Task RegisterAsync_CreatesUnverifiedMemberWithHashedToken()
    {
        using var db = TestDb.Create();
        var service = Create(db);

        var (success, error, token) = await service.RegisterAsync("New Member", "new@test.local", null, "Member@123");

        Assert.True(success, error);
        Assert.False(string.IsNullOrEmpty(token));

        var user = db.Users.Single(u => u.Email == "new@test.local");
        Assert.Equal(Role.Member, user.Role);
        Assert.False(user.EmailVerified); // no auto sign-in until the mailbox is confirmed
        Assert.NotNull(user.EmailVerificationTokenHash);
        Assert.NotEqual(token, user.EmailVerificationTokenHash); // only the SHA-256 hash is stored
        Assert.True(user.EmailVerificationExpiresUtc > DateTime.UtcNow.AddHours(23)); // ~24h window
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_Fails()
    {
        using var db = TestDb.Create();
        var service = Create(db);

        var (success, error, _) = await service.RegisterAsync("Again", "member@test.local", null, "Member@123");

        Assert.False(success);
        Assert.Equal("This email address is already registered.", error);
    }

    [Fact]
    public async Task VerifyEmailAsync_ValidToken_VerifiesAndClearsToken()
    {
        using var db = TestDb.Create();
        var service = Create(db);
        var (_, _, token) = await service.RegisterAsync("New Member", "new@test.local", null, "Member@123");
        var userId = db.Users.Single(u => u.Email == "new@test.local").Id;

        var (success, error) = await service.VerifyEmailAsync(userId, token!);

        Assert.True(success, error);
        var user = db.Users.AsNoTracking().Single(u => u.Id == userId);
        Assert.True(user.EmailVerified);
        Assert.Null(user.EmailVerificationTokenHash);
        Assert.Null(user.EmailVerificationExpiresUtc);

        // Idempotent: verifying again is still a success.
        var (again, _) = await service.VerifyEmailAsync(userId, token!);
        Assert.True(again);
    }

    [Fact]
    public async Task VerifyEmailAsync_WrongToken_FailsWithoutVerifying()
    {
        using var db = TestDb.Create();
        var service = Create(db);
        var (_, _, _) = await service.RegisterAsync("New Member", "new@test.local", null, "Member@123");
        var userId = db.Users.Single(u => u.Email == "new@test.local").Id;

        var (success, error) = await service.VerifyEmailAsync(userId, "bogus-token");

        Assert.False(success);
        Assert.Contains("invalid or has expired", error);
        Assert.False(db.Users.AsNoTracking().Single(u => u.Id == userId).EmailVerified);
    }

    [Fact]
    public async Task VerifyEmailAsync_ExpiredToken_Fails()
    {
        using var db = TestDb.Create();
        var service = Create(db);
        var (_, _, token) = await service.RegisterAsync("New Member", "new@test.local", null, "Member@123");
        var user = db.Users.Single(u => u.Email == "new@test.local");
        user.EmailVerificationExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var (success, error) = await service.VerifyEmailAsync(user.Id, token!);

        Assert.False(success);
        Assert.Contains("invalid or has expired", error);
    }

    [Fact]
    public async Task RequestVerificationEmailAsync_UnknownEmail_NeutralResponse()
    {
        using var db = TestDb.Create();
        var service = Create(db);

        var (success, error, token) = await service.RequestVerificationEmailAsync("ghost@test.local");

        Assert.True(success, error); // same shape as a known email — anti-enumeration
        Assert.Null(token);
    }

    [Fact]
    public async Task RequestVerificationEmailAsync_AlreadyVerified_ReturnsNoToken()
    {
        using var db = TestDb.Create();
        var service = Create(db); // TestDb seeds verified accounts

        var (success, _, token) = await service.RequestVerificationEmailAsync("member@test.local");

        Assert.True(success);
        Assert.Null(token); // nothing to send
    }

    [Fact]
    public async Task ResetPasswordAsync_ResetLinkProvesMailbox_VerifiesEmail()
    {
        using var db = TestDb.Create();
        var service = Create(db);
        var (_, _, _) = await service.RegisterAsync("New Member", "new@test.local", null, "Member@123");
        var user = db.Users.Single(u => u.Email == "new@test.local");

        var (_, _, resetLink) = await service.RequestPasswordResetAsync("new@test.local", "https://test/Account/ResetPassword");
        Assert.NotNull(resetLink);
        var query = resetLink!.Split('?')[1].Split('&')
            .ToDictionary(p => p.Split('=')[0], p => p.Split('=')[1]);

        var (success, error) = await service.ResetPasswordAsync(int.Parse(query["userId"]), query["token"], "NewPass@456");

        Assert.True(success, error);
        var updated = db.Users.AsNoTracking().Single(u => u.Id == user.Id);
        Assert.True(updated.EmailVerified); // receiving the reset email proves mailbox ownership
        Assert.Null(updated.EmailVerificationTokenHash);
        Assert.True(PasswordHelper.Verify("NewPass@456", updated.PasswordHash, updated.PasswordSalt));
    }
}
