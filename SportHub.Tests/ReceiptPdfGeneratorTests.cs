using SportHub.Models;
using SportHub.Services;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

namespace SportHub.Tests;

/// <summary>
/// M3 PDF e-receipt: a paid booking produces a real PDF document.
/// (The community licence is also activated in Program.cs for the web app.)
/// </summary>
public class ReceiptPdfGeneratorTests
{
    static ReceiptPdfGeneratorTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task Generate_PaidReservation_ReturnsPdfBytes()
    {
        using var db = TestDb.Create();
        var service = new ReservationService(db, new CourtService(db), new NoopEmailSender());
        var memberId = db.Users.Single(u => u.Email == "member@test.local").Id;
        var courtId = db.Courts.Single().Id;
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(2));

        var (_, _, created) = await service.CreateAsync(memberId, courtId, date, new TimeOnly(9, 0), 1, null);
        await service.MarkPaidAsync(created!.Id, memberId, PaymentMethod.OnlineTransfer, "E2E-1");

        var reservation = db.Reservations
            .Include(r => r.Court)
            .Include(r => r.User)
            .Include(r => r.Payment)
            .Single();

        var bytes = ReceiptPdfGenerator.Generate(reservation);

        Assert.True(bytes.Length > 1000, "Expected a real PDF document, not an empty stub.");
        Assert.Equal(0x25, bytes[0]); // '%'
        Assert.Equal(0x50, bytes[1]); // 'P'
        Assert.Equal(0x44, bytes[2]); // 'D'
        Assert.Equal(0x46, bytes[3]); // 'F'
    }
}
