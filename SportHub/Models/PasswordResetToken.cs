using System.ComponentModel.DataAnnotations;

namespace SportHub.Models;

/// <summary>
/// Single-use password reset token. Only a SHA-256 hash of the token is stored,
/// so a database leak cannot be used to reset passwords.
/// </summary>
public class PasswordResetToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>SHA-256 hex of the reset token (the raw token is never stored).</summary>
    [Required, StringLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
