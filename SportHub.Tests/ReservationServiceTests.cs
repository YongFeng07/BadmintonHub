using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// M2 core business rules: booking validation, server-side double-booking
/// protection, payment consistency and the cancel-with-refund rule.
/// </summary>
public class ReservationServiceTests
{
    private static (ReservationService Service, ApplicationDbContext Db, int MemberId, int CourtId) Create(
        NoopEmailSender? emails = null)
    {
        var db = TestDb.Create();
        var service = new ReservationService(db, new CourtService(db), emails ?? new NoopEmailSender());
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var courtId = db.Courts.Single().Id;
        return (service, db, memberId, courtId);
    }

    private static DateOnly FutureDate(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(days));

    [Fact]
    public async Task Create_ValidBooking_ReturnsPendingWithPaymentAndReference()
    {
        var (service, db, memberId, courtId) = Create();
        var date = FutureDate(3);

        var (success, error, reservation) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(9, 0), 1, null);

        Assert.True(success, error);
        Assert.NotNull(reservation);
        Assert.Equal(ReservationStatus.Pending, reservation!.Status);
        Assert.Equal(25m, reservation.TotalAmount); // rate 25 x 1 hour
        Assert.Matches(@"^SH-\d{4}-\d{6}$", reservation.ReservationReference);
        Assert.Equal("SH-" + date.Year + "-000001", reservation.ReservationReference); // first booking

        var payment = db.Payments.Single();
        Assert.Equal(reservation.Id, payment.ReservationId);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Single(db.Notifications, n => n.UserId == memberId && n.Title == "Reservation created");
        Assert.Equal(3, await db.Notifications.CountAsync(n => n.Title == "New booking pending")); // admins alerted
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Create_InvalidDuration_Fails(int duration)
    {
        var (service, _, memberId, courtId) = Create();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), duration, null);

        Assert.False(success);
        Assert.Contains("Duration", error);
    }

    [Fact]
    public async Task Create_PastDate_Fails()
    {
        var (service, _, memberId, courtId) = Create();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(-1), new TimeOnly(9, 0), 1, null);

        Assert.False(success);
        Assert.Contains("past", error);
    }

    [Fact]
    public async Task Create_UnknownCourt_Fails()
    {
        var (service, _, memberId, _) = Create();

        var (success, error, _) = await service.CreateAsync(memberId, 9999, FutureDate(3), new TimeOnly(9, 0), 1, null);

        Assert.False(success);
        Assert.Equal("Court not found.", error);
    }

    [Fact]
    public async Task Create_CourtUnderMaintenance_Fails()
    {
        var (service, db, memberId, courtId) = Create();
        db.Courts.Single().Status = CourtStatus.Maintenance;
        await db.SaveChangesAsync();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        Assert.False(success);
        Assert.Contains("not available", error);
    }

    [Fact]
    public async Task Create_WindowOutsideOpeningHours_Fails()
    {
        var (service, _, memberId, courtId) = Create();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(22, 30), 1, null);

        Assert.False(success); // 22:30-23:30 exceeds the 23:00 closing time
        Assert.Contains("available booking hours", error);
    }

    [Fact]
    public async Task Create_DoubleBookingSameSlot_SecondIsRejectedServerSide()
    {
        var (service, db, memberId, courtId) = Create();
        var date = FutureDate(3);

        var first = await service.CreateAsync(memberId, courtId, date, new TimeOnly(10, 0), 2, null);
        var second = await service.CreateAsync(memberId, courtId, date, new TimeOnly(10, 0), 1, null);
        var overlap = await service.CreateAsync(memberId, courtId, date, new TimeOnly(11, 30), 1, null);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Contains("just been booked", second.Error);
        Assert.False(overlap.Success); // partial overlap is also refused
        Assert.Equal(1, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task MarkPaid_PendingReservation_ConfirmsBooking()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        var (success, error) = await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.OnlineTransfer, "E2E-1");

        Assert.True(success, error);
        var payment = db.Payments.Single();
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal("E2E-1", payment.PaymentReference);
        Assert.NotNull(payment.PaidAt);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status); // consistency rule
        Assert.Single(db.Notifications, n => n.Type == NotificationType.Payment);
    }

    [Fact]
    public async Task MarkPaid_AlreadyPaid_Fails()
    {
        var (service, _, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.Cash, null);

        var (success, error) = await service.MarkPaidAsync(reservation.Id, memberId, PaymentMethod.Cash, null);

        Assert.False(success);
        Assert.Equal("This reservation is already paid.", error);
    }

    [Fact]
    public async Task MarkPaid_AnotherMemberCannotPay_Fails()
    {
        var (service, db, memberId, courtId) = Create();
        var otherId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        var (success, error) = await service.MarkPaidAsync(reservation!.Id, otherId, PaymentMethod.Cash, null);

        Assert.False(success); // another user's id — the back-office flag was not passed
        Assert.Equal("You can only pay for your own reservations.", error);
    }

    [Fact]
    public async Task Cancel_PaidBooking_BecomesRefund()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.Card, null);

        var (success, error) = await service.CancelAsync(reservation.Id, memberId, "No longer needed");

        Assert.True(success, error);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(PaymentStatus.Refunded, db.Payments.Single().Status); // refund rule
        Assert.Equal("No longer needed", reservation.CancellationReason);
        Assert.NotNull(reservation.CancelledAt);
    }

    [Fact]
    public async Task Cancel_CompletedBooking_Fails()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        reservation!.Status = ReservationStatus.Completed;
        await db.SaveChangesAsync();

        var (success, error) = await service.CancelAsync(reservation.Id, memberId, null);

        Assert.False(success);
        Assert.Contains("cannot be cancelled", error);
    }

    // ---------- G-M3: atomic slot claims, cart-hold soft locks, payment timeout ----------

    [Fact]
    public async Task Create_ClaimsOneSlotPerBookedHour()
    {
        var (service, db, memberId, courtId) = Create();
        var date = FutureDate(3);

        var (success, error, reservation) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(9, 0), 2, null);

        Assert.True(success, error);
        var slots = await db.ReservationSlots
            .Where(s => s.ReservationId == reservation!.Id)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
        Assert.Equal(2, slots.Count);
        Assert.All(slots, s => Assert.Equal(courtId, s.CourtId));
        Assert.All(slots, s => Assert.Equal(date, s.Date));
        Assert.Equal(new TimeOnly(9, 0), slots[0].StartTime);
        Assert.Equal(new TimeOnly(10, 0), slots[1].StartTime);
    }

    [Fact]
    public async Task Cancel_ReleasesClaimedSlots()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 2, null);

        var (success, error) = await service.CancelAsync(reservation!.Id, memberId, "Change of plan");

        Assert.True(success, error);
        Assert.Equal(0, await db.ReservationSlots.CountAsync());
    }

    [Fact]
    public async Task Create_OtherMembersCartHold_Blocks()
    {
        var (service, db, memberId, courtId) = Create();
        var otherUserId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        var date = FutureDate(3);
        db.CartItems.Add(new CartItem
        {
            UserId = otherUserId,
            CourtId = courtId,
            Date = date,
            StartTime = new TimeOnly(9, 0),
            DurationHours = 1,
            HeldUntil = DateTime.Now.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(9, 0), 1, null);

        Assert.False(success);
        Assert.Contains("held in another member's cart", error);
        Assert.Equal(0, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task Create_OwnCartHold_DoesNotBlock()
    {
        var (service, db, memberId, courtId) = Create();
        var date = FutureDate(3);
        db.CartItems.Add(new CartItem
        {
            UserId = memberId,
            CourtId = courtId,
            Date = date,
            StartTime = new TimeOnly(9, 0),
            DurationHours = 1,
            HeldUntil = DateTime.Now.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(9, 0), 1, null);

        Assert.True(success, error);
    }

    [Fact]
    public async Task ReleaseUnpaidPendingAsync_StalePending_CancelsAndReleases()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        reservation!.CreatedAt = DateTime.Now.Subtract(TimeSpan.FromMinutes(31));
        await db.SaveChangesAsync();

        var released = await service.ReleaseUnpaidPendingAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(1, released);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Contains("30 minutes", reservation.CancellationReason);
        Assert.NotNull(reservation.CancelledAt);
        Assert.Equal(PaymentStatus.Failed, db.Payments.Single().Status);
        Assert.Equal(0, await db.ReservationSlots.CountAsync());
        Assert.Contains(db.Notifications, n => n.Title == "Booking released" && n.UserId == memberId);
    }

    [Fact]
    public async Task ReleaseUnpaidPendingAsync_FreshPending_Untouched()
    {
        var (service, db, memberId, courtId) = Create();
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        var released = await service.ReleaseUnpaidPendingAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(0, released);
        Assert.Equal(ReservationStatus.Pending, reservation!.Status);
        Assert.Equal(PaymentStatus.Pending, db.Payments.Single().Status);
        Assert.Single(db.ReservationSlots); // claim survives
    }

    [Fact]
    public async Task Create_RemovesWishlistEntry_OnBooking()
    {
        var (service, db, memberId, courtId) = Create();
        db.WishlistItems.Add(new WishlistItem { UserId = memberId, CourtId = courtId });
        await db.SaveChangesAsync();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        Assert.True(success, error);
        Assert.Equal(0, await db.WishlistItems.CountAsync()); // booked — no longer wanted
    }

    [Fact]
    public async Task Create_LeavesOtherMembersWishlistEntries_Untouched()
    {
        var (service, db, memberId, courtId) = Create();
        var otherUserId = db.Users.Single(u => u.Email == "admin2@test.local").Id;
        db.WishlistItems.Add(new WishlistItem { UserId = otherUserId, CourtId = courtId });
        await db.SaveChangesAsync();

        var (success, error, _) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        Assert.True(success, error);
        var remaining = db.WishlistItems.Single();
        Assert.Equal(otherUserId, remaining.UserId);
    }

    // ---------- G-M5: e-receipt & lifecycle emails ----------

    [Fact]
    public async Task Create_SendsBookingReceivedEmail()
    {
        var emails = new NoopEmailSender();
        var (service, _, memberId, courtId) = Create(emails);

        var (success, error, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        Assert.True(success, error);
        var mail = Assert.Single(emails.Sent);
        Assert.Equal($"Booking received — {reservation!.ReservationReference}", mail.Subject);
        Assert.Equal("member@test.local", mail.To);
        Assert.Null(mail.Attachments);
    }

    [Fact]
    public async Task MarkPaidAsync_SendsEReceiptEmail_WithPdfAttachment()
    {
        var emails = new NoopEmailSender();
        var (service, _, memberId, courtId) = Create(emails);
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        var (success, error) = await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.Card, "REF-1");

        Assert.True(success, error);
        var receiptMail = emails.Sent.Single(m => m.Subject.StartsWith("E-receipt"));
        Assert.Equal($"E-receipt — {reservation.ReservationReference}", receiptMail.Subject);
        var attachment = Assert.Single(receiptMail.Attachments!);
        Assert.Equal($"Receipt-{reservation.ReservationReference}.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(attachment.Content.Take(4).ToArray()));
    }

    [Fact]
    public async Task MarkPaidAsync_SuppressedReceiptEmail_DoesNotSend()
    {
        var emails = new NoopEmailSender();
        var (service, _, memberId, courtId) = Create(emails);
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);

        var (success, error) = await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.Card, null,
            sendReceiptEmail: false);

        Assert.True(success, error);
        Assert.DoesNotContain(emails.Sent, m => m.Subject.StartsWith("E-receipt"));
    }

    [Fact]
    public async Task Cancel_PaidBooking_SendsCancellationEmailWithRefundNote()
    {
        var emails = new NoopEmailSender();
        var (service, _, memberId, courtId) = Create(emails);
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        await service.MarkPaidAsync(reservation!.Id, memberId, PaymentMethod.Card, null);

        var (success, error) = await service.CancelAsync(reservation.Id, memberId, "No longer needed");

        Assert.True(success, error);
        var mail = emails.Sent.Single(m => m.Subject.StartsWith("Booking cancelled"));
        Assert.Contains("refunded", mail.Body);
    }

    [Fact]
    public async Task ReleaseUnpaidPendingAsync_SendsReleasedEmail()
    {
        var emails = new NoopEmailSender();
        var (service, db, memberId, courtId) = Create(emails);
        var (_, _, reservation) = await service.CreateAsync(memberId, courtId, FutureDate(3), new TimeOnly(9, 0), 1, null);
        reservation!.CreatedAt = DateTime.Now.Subtract(TimeSpan.FromMinutes(31));
        await db.SaveChangesAsync();

        var released = await service.ReleaseUnpaidPendingAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(1, released);
        var mail = emails.Sent.Single(m => m.Subject.StartsWith("Booking released"));
        Assert.Contains(reservation.ReservationReference, mail.Subject);
    }
}
