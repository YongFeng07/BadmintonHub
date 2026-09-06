using SportHub.Data;
using SportHub.Services;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// P4 booking cart: add/update validation against the shared booking rules,
/// duplicate-line protection and ownership-scoped remove/batch/clear.
/// </summary>
public class CartServiceTests
{
    private static (CartService Service, ApplicationDbContext Db, int MemberId, int OtherUserId, int CourtId) Create()
    {
        var db = TestDb.Create();
        var service = new CartService(db, new CourtService(db));
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var otherUserId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        var courtId = db.Courts.Single().Id;
        return (service, db, memberId, otherUserId, courtId);
    }

    private static DateOnly FutureDate(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(days));

    [Fact]
    public async Task Add_ValidSlot_PersistsItem()
    {
        var (service, db, memberId, _, courtId) = Create();

        var (success, error, item) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 2);

        Assert.True(success, error);
        Assert.NotNull(item);
        Assert.Equal(courtId, item!.CourtId);
        Assert.Equal(2, item.DurationHours);
        Assert.Equal(1, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Add_DuplicateLine_IsRejectedFriendly()
    {
        var (service, db, memberId, _, courtId) = Create();
        await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        var (success, error, _) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        Assert.False(success);
        Assert.Equal("This slot is already in your cart.", error);
        Assert.Equal(1, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Add_OverlappingOtherCartLine_Fails()
    {
        var (service, db, memberId, _, courtId) = Create();
        await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(10, 0), 2); // 10:00-12:00

        var (success, error, _) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 1);

        Assert.False(success);
        Assert.Contains("overlaps another item", error);
        Assert.Equal(1, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Add_OutsideOpenSlots_Fails()
    {
        var (service, _, memberId, _, courtId) = Create();

        var (success, error, _) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(22, 30), 1);

        Assert.False(success); // 22:30-23:30 exceeds the 23:00 closing time
        Assert.Contains("outside the available booking hours", error);
    }

    [Fact]
    public async Task Add_UnknownCourt_Fails()
    {
        var (service, _, memberId, _, _) = Create();

        var (success, error, _) = await service.AddAsync(memberId, 9999, FutureDate(3), new TimeOnly(9, 0), 1);

        Assert.False(success);
        Assert.Equal("Court not found.", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Add_InvalidDuration_Fails(int duration)
    {
        var (service, _, memberId, _, courtId) = Create();

        var (success, error, _) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), duration);

        Assert.False(success);
        Assert.Contains("Duration", error);
    }

    [Fact]
    public async Task Update_ValidChange_Persists()
    {
        var (service, db, memberId, _, courtId) = Create();
        var (_, _, item) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        var (success, error) = await service.UpdateAsync(memberId, item!.Id, null, null, 3);

        Assert.True(success, error);
        Assert.Equal(3, (await db.CartItems.SingleAsync()).DurationHours);
    }

    [Fact]
    public async Task Update_OverlapWithOtherLine_FailsAndKeepsOldValues()
    {
        var (service, db, memberId, _, courtId) = Create();
        await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 2);  // 09:00-11:00
        var (_, _, second) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 1); // 11:00-12:00

        var (success, error) = await service.UpdateAsync(memberId, second!.Id, null, new TimeOnly(10, 30), null);

        Assert.False(success); // 10:30-11:30 would overlap the first line
        Assert.Contains("overlaps another item", error);
        Assert.Equal(new TimeOnly(11, 0), (await db.CartItems.SingleAsync(i => i.Id == second.Id)).StartTime);
    }

    [Fact]
    public async Task Update_AnotherUsersItem_Fails()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        var (_, _, item) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        var (success, error) = await service.UpdateAsync(otherUserId, item!.Id, null, null, 2);

        Assert.False(success);
        Assert.Equal("Cart item not found.", error);
        Assert.Equal(1, (await db.CartItems.SingleAsync()).DurationHours); // untouched
    }

    [Fact]
    public async Task Remove_IsScopedToOwner()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        var (_, _, item) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        var (otherFails, otherError) = await service.RemoveAsync(otherUserId, item!.Id);
        var (ownerSucceeds, ownerError) = await service.RemoveAsync(memberId, item.Id);

        Assert.False(otherFails);
        Assert.Equal("Cart item not found.", otherError);
        Assert.True(ownerSucceeds, ownerError);
        Assert.Equal(0, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task RemoveBatch_RemovesOnlyOwnItems()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        var (_, _, first) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        var (_, _, second) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 1);
        var (_, _, foreign) = await service.AddAsync(otherUserId, courtId, FutureDate(3), new TimeOnly(13, 0), 1);

        var removed = await service.RemoveBatchAsync(memberId, new[] { first!.Id, second!.Id, foreign!.Id });

        Assert.Equal(2, removed);
        var remaining = await db.CartItems.SingleAsync();
        Assert.Equal(foreign.Id, remaining.Id);
    }

    [Fact]
    public async Task Clear_RemovesOnlyOwnItems()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 1);
        await service.AddAsync(otherUserId, courtId, FutureDate(3), new TimeOnly(13, 0), 1);

        var removed = await service.ClearAsync(memberId);

        Assert.Equal(2, removed);
        Assert.Equal(0, await db.CartItems.CountAsync(i => i.UserId == memberId));
        Assert.Equal(1, await db.CartItems.CountAsync(i => i.UserId == otherUserId));
    }

    [Fact]
    public async Task Count_ReturnsOnlyUsersItems()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        await service.AddAsync(otherUserId, courtId, FutureDate(3), new TimeOnly(13, 0), 1);

        Assert.Equal(1, await service.CountAsync(memberId));
        Assert.Equal(1, await service.CountAsync(otherUserId));
    }

    // ---------- G-M3: 15-minute cart holds ----------

    [Fact]
    public async Task Add_SetsFifteenMinuteHold()
    {
        var (service, db, memberId, _, courtId) = Create();
        var before = DateTime.Now;

        var (success, error, item) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        Assert.True(success, error);
        Assert.NotNull(item!.HeldUntil);
        Assert.True(item.HeldUntil > before);
        Assert.True(item.HeldUntil <= DateTime.Now.Add(CartService.HoldDuration));
        Assert.NotNull((await db.CartItems.SingleAsync()).HeldUntil);
    }

    [Fact]
    public async Task Add_OtherMembersActiveHold_Blocks()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        await service.AddAsync(otherUserId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        var (success, error, _) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        Assert.False(success);
        Assert.Contains("held in another member's cart", error);
        Assert.Equal(1, await db.CartItems.CountAsync()); // only the holder's line
    }

    [Fact]
    public async Task Add_ExpiredHold_DoesNotBlock()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        var (_, _, other) = await service.AddAsync(otherUserId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        other!.HeldUntil = DateTime.Now.Subtract(TimeSpan.FromMinutes(1)); // stale hold
        await db.SaveChangesAsync();

        var (success, error, _) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        Assert.True(success, error);
    }

    [Fact]
    public async Task Update_MoveOntoOwnHold_SucceedsAndRefreshesHold()
    {
        var (service, db, memberId, _, courtId) = Create();
        var (_, _, item) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        item!.HeldUntil = DateTime.Now.Subtract(TimeSpan.FromMinutes(1)); // own stale hold at 09:00
        await db.SaveChangesAsync();

        // Moving to 09:30 overlaps the item's own (stale) hold — own holds never block.
        var (success, error) = await service.UpdateAsync(memberId, item.Id, null, new TimeOnly(9, 30), 1);

        Assert.True(success, error);
        var updated = await db.CartItems.SingleAsync();
        Assert.Equal(new TimeOnly(9, 30), updated.StartTime);
        Assert.NotNull(updated.HeldUntil);
        Assert.True(updated.HeldUntil > DateTime.Now); // hold refreshed
    }

    [Fact]
    public async Task ReleaseExpiredHoldsAsync_ReleasesOnlyExpired()
    {
        var (service, db, memberId, otherUserId, courtId) = Create();
        var (_, _, expired) = await service.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        var (_, _, active) = await service.AddAsync(otherUserId, courtId, FutureDate(3), new TimeOnly(13, 0), 1);
        expired!.HeldUntil = DateTime.Now.Subtract(TimeSpan.FromSeconds(5));
        await db.SaveChangesAsync();

        var released = await service.ReleaseExpiredHoldsAsync();

        Assert.Equal(1, released);
        Assert.Null((await db.CartItems.SingleAsync(i => i.Id == expired.Id)).HeldUntil);
        Assert.NotNull((await db.CartItems.SingleAsync(i => i.Id == active!.Id)).HeldUntil);
    }
}
