using BadmintonHub.Services;

namespace BadmintonHub.Tests;

public class PasswordHelperTests
{
    [Fact]
    public void HashPassword_RoundTrip_VerifiesTrue()
    {
        var (hash, salt) = PasswordHelper.HashPassword("Member@123");

        Assert.True(PasswordHelper.Verify("Member@123", hash, salt));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var (hash, salt) = PasswordHelper.HashPassword("Member@123");

        Assert.False(PasswordHelper.Verify("Member@124", hash, salt));
        Assert.False(PasswordHelper.Verify("member@123", hash, salt)); // case-sensitive
        Assert.False(PasswordHelper.Verify("", hash, salt));
    }

    [Fact]
    public void HashPassword_TwoCalls_ProduceDifferentSalts()
    {
        var (hash1, salt1) = PasswordHelper.HashPassword("SamePassword1");
        var (hash2, salt2) = PasswordHelper.HashPassword("SamePassword1");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
        // Both hashes verify against their own salt.
        Assert.True(PasswordHelper.Verify("SamePassword1", hash1, salt1));
        Assert.True(PasswordHelper.Verify("SamePassword1", hash2, salt2));
    }

    [Fact]
    public void Verify_CorruptStoredHash_FailsClosed()
    {
        Assert.False(PasswordHelper.Verify("anything1", "not-base64!!", "also-not-base64!!"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short7x")]          // 7 chars: too short
    [InlineData("12345678")]         // no letter
    [InlineData("abcdefgh")]         // no digit
    public void ValidatePassword_InvalidInputs_ReturnError(string? password)
    {
        Assert.NotNull(PasswordHelper.ValidatePassword(password!));
    }

    [Theory]
    [InlineData("Member@123")]
    [InlineData("a1b2c3d4e5f6g7h8i9j0")] // 20 chars with letter+digit
    public void ValidatePassword_ValidInputs_ReturnNull(string password)
    {
        Assert.Null(PasswordHelper.ValidatePassword(password));
    }
}
