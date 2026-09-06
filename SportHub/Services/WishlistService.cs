using SportHub.Data;
using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Services;

/// <summary>
/// "Currently unavailable — Add to Wishlist" (revised spec): a member saves an
/// unavailable court, gets notified when it opens up again (G-M4), and can book
/// it straight from the wishlist once real open slots exist.
/// </summary>
public class WishlistService : IWishlistService
{
    private readonly ApplicationDbContext _db;
    private readonly ICourtService _courtService;
    private readonly IEmailService _emailService;

    public WishlistService(ApplicationDbContext db, ICourtService courtService, IEmailService emailService)
    {
        _db = db;
        _courtService = courtService;
        _emailService = emailService;
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
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court == null)
            return (false, "Court not found.");

        // G-M4: the wishlist is for unavailable courts. An available court can be
        // booked directly — saving it instead would just sit there silently.
        if (court.Status == CourtStatus.Available)
            return (false, "This court is available now — book it directly instead of wishlisting it.");

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

    /// <summary>G-M4: removes the member's own selected items; returns the number removed.</summary>
    public async Task<int> RemoveBatchAsync(int userId, IEnumerable<int> itemIds)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        var items = await _db.WishlistItems
            .Where(w => w.UserId == userId && ids.Contains(w.Id))
            .ToListAsync();
        _db.WishlistItems.RemoveRange(items);
        await _db.SaveChangesAsync();
        return items.Count;
    }

    /// <summary>
    /// G-M4 admin hook: when a court leaves Available, its wishlist subscriptions are
    /// re-armed so the next re-opening notifies members again.
    /// </summary>
    public async Task ResetNotifiedForCourtAsync(int courtId)
    {
        var items = await _db.WishlistItems
            .Where(w => w.CourtId == courtId && w.NotifiedAt != null)
            .ToListAsync();
        foreach (var item in items) item.NotifiedAt = null;
        if (items.Count > 0) await _db.SaveChangesAsync();
    }

    /// <summary>
    /// G-M4 notify-when-available: notifies every member whose wishlisted court is
    /// bookable again (Available with real open slots in the next 3 days) and has not
    /// been notified for this opening yet. One in-app notification plus one email per
    /// member; NotifiedAt makes the notification one-shot per opening. Returns how
    /// many members were notified. Called by WishlistNotifyWorker.
    /// </summary>
    public async Task<int> NotifyForAvailableCourtsAsync()
    {
        var items = await _db.WishlistItems
            .Include(w => w.Court)
            .Include(w => w.User)
            .Where(w => w.NotifiedAt == null && w.Court!.Status == CourtStatus.Available)
            .ToListAsync();

        var notified = 0;
        foreach (var item in items)
        {
            var (firstOpenDate, openSlotCount) = await _courtService.GetUpcomingOpenSlotsAsync(item.CourtId);
            if (firstOpenDate == null || openSlotCount == 0)
                continue; // back in service but nothing bookable yet — keep waiting

            var court = item.Court!;
            var user = item.User!;
            var message = $"Court {court.CourtNumber} at {court.Facility?.Name ?? "SportHub"} is bookable again — " +
                          $"{openSlotCount} open slot(s) in the next 3 days, first on {firstOpenDate.Value:dd MMM yyyy}.";
            _db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Title = "Court available again",
                Message = message,
                Type = NotificationType.System,
                TargetUrl = "/Wishlist"
            });
            item.NotifiedAt = DateTime.Now;
            notified++;

            await _emailService.SendAsync(
                user.Email,
                "SportHub: a court on your wishlist is available again",
                $"<p>Good news, {System.Net.WebUtility.HtmlEncode(user.FullName)}!</p>" +
                $"<p>{System.Net.WebUtility.HtmlEncode(message)}</p>" +
                $"<p>Sign in to SportHub and open your wishlist to book it.</p>");
        }

        if (notified > 0) await _db.SaveChangesAsync();
        return notified;
    }
}
