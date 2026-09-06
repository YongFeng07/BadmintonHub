using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// P4 + G-M4 wishlist: idempotent adds for unavailable courts only, ownership-scoped
/// single/batch removal, and the notify-when-available worker logic (one-shot per
/// reopening, only when real open slots exist).
/// </summary>
public class WishlistServiceTests
{
    private static (WishlistService Service, ApplicationDbContext Db, int MemberId, int OtherUserId, int CourtId) Create()
    {
        var db = TestDb.Create();
        var service = new WishlistService(db, new CourtService(db), new NoopEmailSender());
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var otherUserId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        var court = db.Courts.Single();
        // The wishlist is for unavailable courts; flip the seeded court so adds succeed.
        court.Status = CourtStatus.Maintenance;
        db.SaveChanges();
        return (service, db, memberId, otherUserId, court.Id);
    }

    // The slot grid matches per-hour availability rows (as seeded in production);
    // TestDb's single 08:00-23:00 span row does not drive GetAvailabilityForDateAsync.
    private static void AddHourlyAvailability(ApplicationDbContext db, int courtId, DateOnly date, int from, int to)
    {
        for (var h = from; h < to; h++)
        {
            db.CourtAvailabilities.Add(new CourtAvailability
            {
                CourtId = courtId,
                Date = date,
                StartTime = new TimeOnly(h, 0),
                EndTime = new TimeOnly(h + 1, 0),
                Status = AvailabilityStatus.Open
            });
        }
    }

    private static DateOnly FutureDate(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(days));

    // TestDb seeds one 08:00-23:00 span row per day, which the slot grid treats as a
    // single open hour (08:00). The notify tests control availability exactly, so
    // drop the seeded rows first.
    private static void ClearAvailability(ApplicationDbContext db)
    {
        db.CourtAvailabilities.RemoveRange(db.CourtAvailabilities);
        db.SaveChanges();
    }

    [Fact]
    public async Task Add_ValidCourt_Persists()
    {
        var (service, db, memberId, _, courtId) = Create();

        var (success, error) = await service.AddAsync(memberId, courtId);

        Assert.True(success, error);
        Assert.Equal(1, await db.WishlistItems.CountAsync());
    }

    [Fact]
    public async Task Add_AvailableCourt_RejectedFriendly()
    {
        var (service, db, memberId, _, courtId) = Create();
        db.Courts.Single().Status = CourtStatus.Available;
        await db.SaveChangesAsync();

        var (success, error) = await service.AddAsync(memberId, courtId);

        Assert.False(success);
        Assert.Equal("This court is available now — book it directly instead of wishlisting it.", error);
        Assert.Equal(0, await db.WishlistItems.CountAsync());
    }

    [Fact]
    public async Task Add_Duplicate_IsRejectedFriendly()
    {
        var (service, db, memberId, _, courtId) = Create();
        await service.AddAsync(memberId, courtId);

        var (success, error) = await service.AddAsync(memberId, courtId);

        Assert.False(success);
        Assert.Equal("This court is already in your wishlist.", error);
        Assert.Equal(1, await db.WishlistItems.CountAsync());
    }

    [Fact]
    public async Task Add_UnknownCourt_Fails()
    {
        var (service, _, memberId, _, _) = Create();

        var (success, error) = await service.AddAsync(memberId, 9999);

        Assert.False(success);
        Assert.Equal("Court not found.", error);
    }

    [Fact]
    public async Task IsWishlisted_ReflectsState()
    {
        var (service, _, memberId, _, courtId) = Create();
        Assert.False(await service.IsWishlistedAsync(memberId, courtId));

        await service.AddAsync(memberId, courtId);

        Assert.True(await service.IsWishlistedAsync(memberId, courtId));
    }

    [Fact]
    public async Task GetItems_IncludesCourtAndFacility()
    {
        var (service, _, memberId, _, courtId) = Create();
        await service.AddAsync(memberId, courtId);

        var items = await service.GetItemsAsync(memberId);

        var item = Assert.Single(items);
        Assert.NotNull(item.Court);
        Assert.Equal(courtId, item.Court!.Id);
        Assert.Equal("Test Facility", item.Court!.Facility!.Name);
    }

    [Fact]
    public async Task Remove_OwnItem_Succeeds()
    {
        var (service, db, memberId, _, courtId) = Create();
        var item = await AddAndGetItemAsync(service, memberId, courtId);

        var (success, error) = await service.RemoveAsync(memberId, item.Id);

        Assert.True(success, error);
        Assert.Equal(0, await db.WishlistItems.CountAsync());
    }

    [Fact]
    public async Task Remove_AnotherUsersItem_Fails()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        var item = await AddAndGetItemAsync(service, memberId, courtId);

        var (success, error) = await service.RemoveAsync(otherUserId, item.Id);

        Assert.False(success);
        Assert.Equal("Wishlist item not found.", error);
        Assert.Equal(1, await db.WishlistItems.CountAsync());
    }

    [Fact]
    public async Task RemoveBatch_RemovesOnlyOwnSelected()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        var own = await AddAndGetItemAsync(service, memberId, courtId);
        var theirs = await AddAndGetItemAsync(service, otherUserId, courtId);

        var removed = await service.RemoveBatchAsync(memberId, new[] { own.Id, theirs.Id });

        Assert.Equal(1, removed); // the other member's row is ignored, not deleted
        Assert.Equal(0, await db.WishlistItems.CountAsync(w => w.UserId == memberId));
        Assert.Equal(1, await db.WishlistItems.CountAsync(w => w.UserId == otherUserId));
    }

    [Fact]
    public async Task RemoveBatch_EmptySelection_RemovesNothing()
    {
        var (service, db, memberId, _, courtId) = Create();
        await AddAndGetItemAsync(service, memberId, courtId);

        var removed = await service.RemoveBatchAsync(memberId, new List<int>());

        Assert.Equal(0, removed);
        Assert.Equal(1, await db.WishlistItems.CountAsync());
    }

    [Fact]
    public async Task Count_ReturnsOnlyUsersItems()
    {
        var (service, _, memberId, otherUserId, courtId) = Create();
        await service.AddAsync(memberId, courtId);
        await service.AddAsync(otherUserId, courtId);

        Assert.Equal(1, await service.CountAsync(memberId));
        Assert.Equal(1, await service.CountAsync(otherUserId));
    }

    [Fact]
    public async Task NotifyForAvailableCourtsAsync_OpenSlots_NotifiesOnce()
    {
        var (service, db, memberId, _, courtId) = Create();
        ClearAvailability(db);
        await service.AddAsync(memberId, courtId);
        var court = db.Courts.Single();
        court.Status = CourtStatus.Available;
        AddHourlyAvailability(db, courtId, FutureDate(1), 8, 23);
        await db.SaveChangesAsync();

        var notified = await service.NotifyForAvailableCourtsAsync();
        var secondPass = await service.NotifyForAvailableCourtsAsync();

        Assert.Equal(1, notified);
        Assert.Equal(0, secondPass); // NotifiedAt makes it one-shot per reopening
        var item = await db.WishlistItems.SingleAsync();
        Assert.NotNull(item.NotifiedAt);
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == memberId &&
            n.Title == "Court available again" && n.Message.Contains("open slot")));
    }

    [Fact]
    public async Task NotifyForAvailableCourtsAsync_NoOpenSlots_Skips()
    {
        var (service, db, memberId, _, courtId) = Create();
        ClearAvailability(db);
        await service.AddAsync(memberId, courtId);
        var court = db.Courts.Single();
        court.Status = CourtStatus.Available; // back in service, but nothing bookable yet
        await db.SaveChangesAsync();

        var notified = await service.NotifyForAvailableCourtsAsync();

        Assert.Equal(0, notified);
        Assert.Null((await db.WishlistItems.SingleAsync()).NotifiedAt);
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task NotifyForAvailableCourtsAsync_StillUnavailable_Skips()
    {
        var (service, db, memberId, _, courtId) = Create();
        ClearAvailability(db);
        await service.AddAsync(memberId, courtId);
        AddHourlyAvailability(db, courtId, FutureDate(1), 8, 23);
        await db.SaveChangesAsync(); // court stays Maintenance

        var notified = await service.NotifyForAvailableCourtsAsync();

        Assert.Equal(0, notified);
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task ResetNotifiedForCourtAsync_ReArmsNotification()
    {
        var (service, db, memberId, _, courtId) = Create();
        ClearAvailability(db);
        await service.AddAsync(memberId, courtId);
        var court = db.Courts.Single();
        court.Status = CourtStatus.Available;
        AddHourlyAvailability(db, courtId, FutureDate(1), 8, 23);
        await db.SaveChangesAsync();
        Assert.Equal(1, await service.NotifyForAvailableCourtsAsync());

        // Court goes down again: the next reopening must notify afresh.
        court.Status = CourtStatus.Maintenance;
        await db.SaveChangesAsync();
        await service.ResetNotifiedForCourtAsync(courtId);

        Assert.Null((await db.WishlistItems.SingleAsync()).NotifiedAt);
    }

    /// <summary>Adds a wishlist row and returns it (the service tuple returns no item).</summary>
    private static async Task<Models.WishlistItem> AddAndGetItemAsync(
        WishlistService service, int userId, int courtId)
    {
        await service.AddAsync(userId, courtId);
        return (await service.GetItemsAsync(userId)).Single();
    }
}
