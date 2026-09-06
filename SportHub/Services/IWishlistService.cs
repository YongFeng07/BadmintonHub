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
}
