using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Models;

/// <summary>
/// Single user table covering all roles (Admin / Staff / Member).
/// The Role enum distinguishes access; a member's profile lives on the same record.
/// </summary>
[Index(nameof(Email), IsUnique = true)]
public class User
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(20)]
    public string? Phone { get; set; }

    /// <summary>Base64 PBKDF2 hash. Plain-text passwords are never stored.</summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Base64 random salt used with the PBKDF2 hash.</summary>
    [Required]
    public string PasswordSalt { get; set; } = string.Empty;

    public Role Role { get; set; } = Role.Member;

    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>Consecutive failed login count; drives the failed-login lockout feature.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>When set and in the future, the account is temporarily locked.</summary>
    public DateTime? LockoutEnd { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public ICollection<LoginAttempt> LoginAttempts { get; set; } = new List<LoginAttempt>();
}
