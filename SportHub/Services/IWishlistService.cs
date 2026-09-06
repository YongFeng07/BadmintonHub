using SportHub.Models;

namespace SportHub.Services;

public interface IWishlistService
{
    /// <summary>The member's wishlist, newest first, with court and facility loaded.</summary>
    Task<List<WishlistItem>> GetItemsAsync(int userId);

    Task<int> CountAsync(int userId);

    Task<bool> IsWishlistedAsync(int userId, int courtId);

    Task<(bool Success, string? Error)> AddAsync(int userId, int courtId);

    Task<(bool Success, string? Error)> RemoveAsync(int userId, int itemId);

    /// <summary>G-M4: removes the member's own selected items; returns the number removed.</summary>
    Task<int> RemoveBatchAsync(int userId, IEnumerable<int> itemIds);

    /// <summary>G-M4 admin hook: re-arms notifications when a court leaves Available.</summary>
    Task ResetNotifiedForCourtAsync(int courtId);

    /// <summary>G-M4: notifies members whose wishlisted court just became bookable. Returns the number notified.</summary>
    Task<int> NotifyForAvailableCourtsAsync();
}
