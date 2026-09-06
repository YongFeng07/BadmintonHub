using System.ComponentModel.DataAnnotations;

namespace SportHub.Models;

/// <summary>In-app notification shown in the user's notification centre.</summary>
public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    [Required, StringLength(100)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Message { get; set; } = string.Empty;

    public NotificationType Type { get; set; } = NotificationType.System;

    /// <summary>
    /// G-M5: deep link the notification centre row to the relevant page
    /// (reservation detail, wishlist, admin list…). Null shows a plain row.
    /// </summary>
    [StringLength(300)]
    public string? TargetUrl { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
