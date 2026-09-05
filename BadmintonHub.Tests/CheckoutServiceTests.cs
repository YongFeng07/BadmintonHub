using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Tests;

/// <summary>
/// P4 transactional checkout: server-side re-validation of every cart line,
/// proportional voucher split (last line absorbs the rounding remainder),
/// voucher usage counted inside the transaction and all-or-nothing batch payment.
/// </summary>
public class CheckoutServiceTests
{
    private static (
        CheckoutService Checkout, CartService Cart, ReservationService Reservations,
        ApplicationDbContext Db, int MemberId, int OtherUserId, int CourtId) Create()
    {
        var db = TestDb.Create();
        var courts = new CourtService(db);
        var reservations = new ReservationService(db, courts);
        var checkout = new CheckoutService(db, courts, new VoucherService(db), reservations, new NoopEmailSender());
        var cart = new CartService(db, courts);
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var otherUserId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        var courtId = db.Courts.Single().Id;
        return (checkout, cart, reservations, db, memberId, otherUserId, courtId);
    }

    private static DateOnly FutureDate(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(days));

    private static Voucher AddVoucher(ApplicationDbContext db, string code, decimal value, int? limit = null)
    {
        var voucher = new Voucher
        {
            Code = code,
            Description = "Test voucher",
            DiscountType = DiscountType.Percentage,
            DiscountValue = value,
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30),
            UsageLimit = limit
        };
        db.Vouchers.Add(voucher);
        db.SaveChanges();
        return voucher;
    }

    // ---------- Preview ----------

    [Fact]
    public async Task Preview_EmptySelection_Fails()
    {
        var (checkout, _, _, _, memberId, _, _) = Create();

        var (success, error, items, _, _, _) = await checkout.PreviewAsync(memberId, new List<int>(), null);

        Assert.False(success);
        Assert.Equal("Select at least one item to check out.", error);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Preview_ComputesSubtotal()
    {
        var (checkout, cart, _, _, memberId, _, courtId) = Create();
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);  // 25
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 2); // 50
        var ids = await cart.GetItemsAsync(memberId);

        var (success, error, items, subtotal, discount, net) =
            await checkout.PreviewAsync(memberId, ids.Select(i => i.Id), null);

        Assert.True(success, error);
        Assert.Equal(2, items.Count);
        Assert.Equal(75m, subtotal);
        Assert.Equal(0m, discount);
        Assert.Equal(75m, net);
    }

    [Fact]
    public async Task Preview_AppliesVoucherDiscount()
    {
        var (checkout, cart, _, db, memberId, _, courtId) = Create();
        AddVoucher(db, "WELCOME10", 10m);
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);  // 25
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 2); // 50
        var ids = await cart.GetItemsAsync(memberId);

        var (success, error, _, subtotal, discount, net) =
            await checkout.PreviewAsync(memberId, ids.Select(i => i.Id), "WELCOME10");

        Assert.True(success, error);
        Assert.Equal(75m, subtotal);
        Assert.Equal(7.50m, discount);
        Assert.Equal(67.50m, net);
    }

    [Fact]
    public async Task Preview_UnknownItemId_Fails()
    {
        var (checkout, cart, _, _, memberId, _, courtId) = Create();
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);

        var (success, error, _, _, _, _) = await checkout.PreviewAsync(memberId, new[] { 9999 }, null);

        Assert.False(success);
        Assert.Equal("Select at least one item to check out.", error);
    }

    // ---------- Checkout ----------

    [Fact]
    public async Task Checkout_CreatesPendingReservationsWithPayments_AndClearsCart()
    {
        var (checkout, cart, _, db, memberId, _, courtId) = Create();
        var date = FutureDate(3);
        await cart.AddAsync(memberId, courtId, date, new TimeOnly(9, 0), 1);
        await cart.AddAsync(memberId, courtId, date, new TimeOnly(11, 0), 2);
        var ids = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);

        var (success, error, reservations) = await checkout.CheckoutAsync(memberId, ids, null);

        Assert.True(success, error);
        Assert.Equal(2, reservations.Count);
        Assert.All(reservations, r => Assert.Equal(ReservationStatus.Pending, r.Status));
        Assert.Equal($"BH-{date.Year}-000001", reservations[0].ReservationReference);
        Assert.Equal($"BH-{date.Year}-000002", reservations[1].ReservationReference);
        Assert.Equal(2, await db.Payments.CountAsync(p => p.Status == PaymentStatus.Pending));
        Assert.Equal(0, await db.CartItems.CountAsync()); // checked-out lines leave the cart
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task Checkout_SplitsDiscountProportionally_LastLineAbsorbsRemainder()
    {
        var (checkout, cart, _, db, memberId, _, courtId) = Create();
        AddVoucher(db, "WELCOME10", 10m);
        var date = FutureDate(3);
        await cart.AddAsync(memberId, courtId, date, new TimeOnly(9, 0), 1);  // gross 25
        await cart.AddAsync(memberId, courtId, date, new TimeOnly(11, 0), 2); // gross 50
        var ids = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);

        var (success, error, reservations) = await checkout.CheckoutAsync(memberId, ids, "WELCOME10");

        Assert.True(success, error);
        Assert.Equal(2, reservations.Count);
        Assert.Equal(2.50m, reservations[0].DiscountAmount); // round(25/75 × 7.50)
        Assert.Equal(5.00m, reservations[1].DiscountAmount); // remainder
        Assert.Equal(7.50m, reservations.Sum(r => r.DiscountAmount));
        Assert.Equal(25m, reservations[0].TotalAmount); // gross stays gross
        Assert.All(reservations, r => Assert.Equal("WELCOME10", r.VoucherCode));
        Assert.Equal(67.50m, await db.Payments.SumAsync(p => p.Amount)); // net across payments
    }

    [Fact]
    public async Task Checkout_InvalidVoucher_FailsAndKeepsCart()
    {
        var (checkout, cart, _, db, memberId, _, courtId) = Create();
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        var ids = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);

        var (success, error, reservations) = await checkout.CheckoutAsync(memberId, ids, "NOPE99");

        Assert.False(success);
        Assert.Equal("Voucher code not found.", error);
        Assert.Empty(reservations);
        Assert.Equal(1, await db.CartItems.CountAsync()); // untouched
        Assert.Equal(0, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task Checkout_RevalidatesAgainstNewReservations_FailsAndRollsBack()
    {
        var (checkout, cart, reservations, db, memberId, _, courtId) = Create();
        var date = FutureDate(3);
        await cart.AddAsync(memberId, courtId, date, new TimeOnly(10, 0), 2);
        var ids = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);
        // Someone else books the same window directly after the line was added to the cart.
        await reservations.CreateAsync(memberId, courtId, date, new TimeOnly(10, 0), 1, null);

        var (success, error, created) = await checkout.CheckoutAsync(memberId, ids, null);

        Assert.False(success);
        Assert.Contains("has just been booked", error);
        Assert.Empty(created);
        Assert.Equal(1, await db.CartItems.CountAsync()); // cart not changed
        Assert.Equal(1, await db.Reservations.CountAsync()); // only the direct booking
    }

    [Fact]
    public async Task Checkout_OverlappingTamperedCartLines_AreRejected()
    {
        var (checkout, _, _, db, memberId, _, courtId) = Create();
        // Bypass the cart service to simulate two overlapping lines in one cart.
        var date = FutureDate(3);
        db.CartItems.AddRange(
            new CartItem { UserId = memberId, CourtId = courtId, Date = date, StartTime = new TimeOnly(9, 0), DurationHours = 2 },
            new CartItem { UserId = memberId, CourtId = courtId, Date = date, StartTime = new TimeOnly(10, 0), DurationHours = 2 });
        await db.SaveChangesAsync();

        var (success, error, created) = await checkout.CheckoutAsync(memberId, db.CartItems.Select(i => i.Id), null);

        Assert.False(success);
        Assert.Contains("overlaps another item", error);
        Assert.Empty(created);
        Assert.Equal(0, await db.Reservations.CountAsync());
        Assert.Equal(2, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Checkout_AnotherUsersItemIds_Fails()
    {
        var (checkout, cart, _, db, memberId, otherUserId, courtId) = Create();
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        var ids = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);

        var (success, error, _) = await checkout.CheckoutAsync(otherUserId, ids, null);

        Assert.False(success); // a missing id never checks out a partial cart
        Assert.Equal("Select at least one item to check out.", error);
        Assert.Equal(1, await db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Checkout_IncrementsVoucherUsage_AndRefusesSecondUse()
    {
        var (checkout, cart, _, db, memberId, _, courtId) = Create();
        var voucher = AddVoucher(db, "ONCE10", 10m, limit: 1);
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        var firstIds = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);

        var first = await checkout.CheckoutAsync(memberId, firstIds, "ONCE10");

        Assert.True(first.Success, first.Error);
        Assert.Equal(1, db.Vouchers.Single(v => v.Id == voucher.Id).UsageCount);

        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 1);
        var secondIds = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);
        var second = await checkout.CheckoutAsync(memberId, secondIds, "ONCE10");

        Assert.False(second.Success);
        Assert.Equal("This voucher has reached its redemption limit.", second.Error);
        Assert.Equal(1, await db.Reservations.CountAsync()); // nothing new was created
        Assert.Equal(1, await db.CartItems.CountAsync());    // cart kept for retry
    }

    // ---------- Batch payment ----------

    [Fact]
    public async Task MarkBatchPaid_ConfirmsAllReservationsAtOnce()
    {
        var (checkout, cart, _, db, memberId, _, courtId) = Create();
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 1);
        var ids = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);
        var (_, _, reservations) = await checkout.CheckoutAsync(memberId, ids, null);

        var (success, error, paid) = await checkout.MarkBatchPaidAsync(
            memberId, reservations.Select(r => r.Id), PaymentMethod.Card);

        Assert.True(success, error);
        Assert.Equal(2, paid);
        Assert.All(reservations, r => Assert.Equal(ReservationStatus.Confirmed, r.Status));
        Assert.Equal(2, await db.Payments.CountAsync(p => p.Status == PaymentStatus.Paid));
    }

    [Fact]
    public async Task MarkBatchPaid_OneAlreadyPaid_FailsWholeBatch()
    {
        var (checkout, cart, reservations, db, memberId, _, courtId) = Create();
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1);
        await cart.AddAsync(memberId, courtId, FutureDate(3), new TimeOnly(11, 0), 1);
        var ids = (await cart.GetItemsAsync(memberId)).Select(i => i.Id);
        var (_, _, created) = await checkout.CheckoutAsync(memberId, ids, null);
        await reservations.MarkPaidAsync(created[0].Id, memberId, PaymentMethod.Cash, null);

        var (success, error, paid) = await checkout.MarkBatchPaidAsync(
            memberId, created.Select(r => r.Id), PaymentMethod.Card);

        Assert.False(success); // all-or-nothing: the batch is refused
        Assert.Equal("This reservation is already paid.", error);
        Assert.Equal(0, paid);
        Assert.Equal(ReservationStatus.Pending, created[1].Status);
        Assert.Equal(PaymentStatus.Pending, db.Payments.Single(p => p.ReservationId == created[1].Id).Status);
    }

    [Fact]
    public async Task MarkBatchPaid_EmptySelection_Fails()
    {
        var (checkout, _, _, _, memberId, _, _) = Create();

        var (success, error, paid) = await checkout.MarkBatchPaidAsync(memberId, new List<int>(), PaymentMethod.Card);

        Assert.False(success);
        Assert.Equal("Select at least one reservation to pay.", error);
        Assert.Equal(0, paid);
    }
}
