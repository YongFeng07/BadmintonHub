using BadmintonHub.Data;
using BadmintonHub.Services;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Tests;

/// <summary>
/// P4 wishlist ("currently unavailable — add to wishlist"): idempotent adds,
/// ownership-scoped removal and the court/facility include used by the card grid.
/// </summary>
public class WishlistServiceTests
{
    private static (WishlistService Service, ApplicationDbContext Db, int MemberId, int OtherUserId, int CourtId) Create()
    {
        var db = TestDb.Create();
        var service = new WishlistService(db);
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var otherUserId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        var courtId = db.Courts.Single().Id;
        return (service, db, memberId, otherUserId, courtId);
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
    public async Task Count_ReturnsOnlyUsersItems()
    {
        var (service, _, memberId, otherUserId, courtId) = Create();
        await service.AddAsync(memberId, courtId);
        await service.AddAsync(otherUserId, courtId);

        Assert.Equal(1, await service.CountAsync(memberId));
        Assert.Equal(1, await service.CountAsync(otherUserId));
    }

    /// <summary>Adds a wishlist row and returns it (the service tuple returns no item).</summary>
    private static async Task<Models.WishlistItem> AddAndGetItemAsync(
        WishlistService service, int userId, int courtId)
    {
        await service.AddAsync(userId, courtId);
        return (await service.GetItemsAsync(userId)).Single();
    }
}
