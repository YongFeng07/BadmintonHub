using BadmintonHub.Data;
using BadmintonHub.Models;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Services;

/// <summary>
/// Member booking cart (revised spec): pending booking lines stored per user until
/// checkout. Every change is re-validated against the same rules as a direct booking,
/// so the cart can never hold an unbookable line for long.
/// </summary>
public class CartService : ICartService
{
    private readonly ApplicationDbContext _db;
    private readonly ICourtService _courts;

    public CartService(ApplicationDbContext db, ICourtService courts)
    {
        _db = db;
        _courts = courts;
    }

    public async Task<List<CartItem>> GetItemsAsync(int userId)
    {
        return await _db.CartItems
            .Include(i => i.Court!)
            .ThenInclude(c => c.Facility)
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.AddedAt)
            .ToListAsync();
    }

    public async Task<int> CountAsync(int userId)
    {
        return await _db.CartItems.CountAsync(i => i.UserId == userId);
    }

    public async Task<(bool Success, string? Error, CartItem? Item)> AddAsync(
        int userId, int courtId, DateOnly date, TimeOnly startTime, int durationHours)
    {
        // A duplicate line (same court, date and start time) is a user error, not a
        // crash: the unique index backs this up, but we want a friendly message.
        if (await _db.CartItems.AnyAsync(i =>
                i.UserId == userId && i.CourtId == courtId && i.Date == date && i.StartTime == startTime))
            return (false, "This slot is already in your cart.", null);

        var error = await BookingRules.ValidateWindowAsync(
            _db, _courts, courtId, date, startTime, durationHours, await OtherWindowsAsync(userId));
        if (error != null)
            return (false, error, null);

        var item = new CartItem
        {
            UserId = userId,
            CourtId = courtId,
            Date = date,
            StartTime = startTime,
            DurationHours = durationHours
        };
        _db.CartItems.Add(item);
        await _db.SaveChangesAsync();
        return (true, null, item);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        int userId, int itemId, DateOnly? date, TimeOnly? startTime, int? durationHours)
    {
        var item = await _db.CartItems.FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId);
        if (item == null)
            return (false, "Cart item not found.");

        var newDate = date ?? item.Date;
        var newStart = startTime ?? item.StartTime;
        var newDuration = durationHours ?? item.DurationHours;

        var error = await BookingRules.ValidateWindowAsync(
            _db, _courts, item.CourtId, newDate, newStart, newDuration, await OtherWindowsAsync(userId, itemId));
        if (error != null)
            return (false, error);

        item.Date = newDate;
        item.StartTime = newStart;
        item.DurationHours = newDuration;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RemoveAsync(int userId, int itemId)
    {
        var item = await _db.CartItems.FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId);
        if (item == null)
            return (false, "Cart item not found.");

        _db.CartItems.Remove(item);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<int> RemoveBatchAsync(int userId, IEnumerable<int> itemIds)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        var items = await _db.CartItems
            .Where(i => i.UserId == userId && ids.Contains(i.Id))
            .ToListAsync();
        _db.CartItems.RemoveRange(items);
        await _db.SaveChangesAsync();
        return items.Count;
    }

    public async Task<int> ClearAsync(int userId)
    {
        var items = await _db.CartItems.Where(i => i.UserId == userId).ToListAsync();
        _db.CartItems.RemoveRange(items);
        await _db.SaveChangesAsync();
        return items.Count;
    }

    /// <summary>All other cart lines of the user as (court, date, start, end) windows.</summary>
    private async Task<List<(int CourtId, DateOnly Date, TimeOnly Start, TimeOnly End)>> OtherWindowsAsync(
        int userId, int? excludeItemId = null)
    {
        var others = await _db.CartItems
            .Where(i => i.UserId == userId && (excludeItemId == null || i.Id != excludeItemId))
            .ToListAsync();
        return others
            .Select(i => (i.CourtId, i.Date, i.StartTime, i.StartTime.AddHours(i.DurationHours)))
            .ToList();
    }
}
