using System.ComponentModel.DataAnnotations;

namespace BadmintonHub.Models;

/// <summary>Audit record of every login attempt, successful or not.</summary>
public class LoginAttempt
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public User? User { get; set; }

    [Required, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [StringLength(45)]
    public string? IpAddress { get; set; }

    public bool Success { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.Now;
}
