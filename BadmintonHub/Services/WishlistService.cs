using BadmintonHub.Data;
using BadmintonHub.Models;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Services;

/// <summary>
/// "Currently unavailable — Add to Wishlist" (revised spec): a member saves an
/// unavailable court and can book it from the wishlist once it opens up again.
/// </summary>
public class WishlistService : IWishlistService
{
    private readonly ApplicationDbContext _db;

    public WishlistService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<WishlistItem>> GetItemsAsync(int userId)
    {
        return await _db.WishlistItems
            .Include(w => w.Court!)
            .ThenInclude(c => c.Facility)
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.AddedAt)
            .ToListAsync();
    }

    public async Task<int> CountAsync(int userId)
    {
        return await _db.WishlistItems.CountAsync(w => w.UserId == userId);
    }

    public async Task<bool> IsWishlistedAsync(int userId, int courtId)
    {
        return await _db.WishlistItems.AnyAsync(w => w.UserId == userId && w.CourtId == courtId);
    }

    public async Task<(bool Success, string? Error)> AddAsync(int userId, int courtId)
    {
        if (!await _db.Courts.AnyAsync(c => c.Id == courtId))
            return (false, "Court not found.");

        if (await _db.WishlistItems.AnyAsync(w => w.UserId == userId && w.CourtId == courtId))
            return (false, "This court is already in your wishlist.");

        _db.WishlistItems.Add(new WishlistItem { UserId = userId, CourtId = courtId });
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RemoveAsync(int userId, int itemId)
    {
        var item = await _db.WishlistItems.FirstOrDefaultAsync(w => w.Id == itemId && w.UserId == userId);
        if (item == null)
            return (false, "Wishlist item not found.");

        _db.WishlistItems.Remove(item);
        await _db.SaveChangesAsync();
        return (true, null);
    }
}
