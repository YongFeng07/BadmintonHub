using SportHub.Models;

namespace SportHub.Services;

public interface ICartService
{
    /// <summary>The member's cart lines, oldest first, with court and facility loaded.</summary>
    Task<List<CartItem>> GetItemsAsync(int userId);

    Task<int> CountAsync(int userId);

    /// <summary>Adds a line after re-validating the window (same rules as a direct booking).</summary>
    Task<(bool Success, string? Error, CartItem? Item)> AddAsync(
        int userId, int courtId, DateOnly date, TimeOnly startTime, int durationHours);

    /// <summary>Changes date/start/duration (null = keep current) after re-validating the window.</summary>
    Task<(bool Success, string? Error)> UpdateAsync(
        int userId, int itemId, DateOnly? date, TimeOnly? startTime, int? durationHours);

    Task<(bool Success, string? Error)> RemoveAsync(int userId, int itemId);

    /// <summary>Removes the member's own selected lines; returns the number removed.</summary>
    Task<int> RemoveBatchAsync(int userId, IEnumerable<int> itemIds);

    /// <summary>Empties the member's cart; returns the number removed.</summary>
    Task<int> ClearAsync(int userId);
}
