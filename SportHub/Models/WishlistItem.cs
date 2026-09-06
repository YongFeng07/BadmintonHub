using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>
/// "Currently unavailable — Add to Wishlist" (revised spec): a member saves an
/// unavailable court and can book it straight from the wishlist once it opens up.
/// </summary>
[Index(nameof(UserId), nameof(CourtId), IsUnique = true)]
public class WishlistItem
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public int CourtId { get; set; }

    public Court? Court { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// G-M4: when the member was last notified that this court is bookable again.
    /// Cleared when the court becomes unavailable again, so each re-opening
    /// triggers a fresh notification instead of spamming every worker pass.
    /// </summary>
    public DateTime? NotifiedAt { get; set; }
}
