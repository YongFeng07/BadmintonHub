using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Tests;

/// <summary>
/// G-M3: the availability grid reflects active cart holds as "Held" so members see
/// why a slot is temporarily not bookable, and the slot returns to "Open" once the
/// hold expires.
/// </summary>
public class CourtServiceTests
{
    private static (CourtService Service, ApplicationDbContext Db, int CourtId) Create()
    {
        var db = TestDb.Create();
        return (new CourtService(db), db, db.Courts.Single().Id);
    }

    private static DateOnly FutureDate(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(days));

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

    private static CartItem AddHold(ApplicationDbContext db, int userId, int courtId, DateOnly date,
        TimeOnly start, int duration, DateTime heldUntil)
    {
        var item = new CartItem
        {
            UserId = userId,
            CourtId = courtId,
            Date = date,
            StartTime = start,
            DurationHours = duration,
            HeldUntil = heldUntil
        };
        db.CartItems.Add(item);
        return item;
    }

    [Fact]
    public async Task GetAvailability_ActiveHold_MarksWholeWindowHeld()
    {
        var (service, db, courtId) = Create();
        var date = FutureDate(3);
        var holder = db.Users.Single(u => u.Email == "member@test.local").Id;
        AddHourlyAvailability(db, courtId, date, 8, 23);
        AddHold(db, holder, courtId, date, new TimeOnly(9, 0), 2, DateTime.Now.AddMinutes(10));
        await db.SaveChangesAsync();

        var slots = await service.GetAvailabilityForDateAsync(date, courtId);

        var byTime = slots.ToDictionary(s => s.StartTime);
        Assert.Equal("held", byTime["09:00"].Status);
        Assert.Equal("Held", byTime["09:00"].Label);
        Assert.Equal("held", byTime["10:00"].Status); // hold spans the whole duration
        Assert.Equal("open", byTime["11:00"].Status);
        Assert.Equal("open", byTime["08:00"].Status);
    }

    [Fact]
    public async Task GetAvailability_StaleHold_ShowsOpen()
    {
        var (service, db, courtId) = Create();
        var date = FutureDate(3);
        var holder = db.Users.Single(u => u.Email == "member@test.local").Id;
        AddHourlyAvailability(db, courtId, date, 8, 23);
        AddHold(db, holder, courtId, date, new TimeOnly(9, 0), 1, DateTime.Now.Subtract(TimeSpan.FromMinutes(1)));
        await db.SaveChangesAsync();

        var slots = await service.GetAvailabilityForDateAsync(date, courtId);

        Assert.Equal("open", slots.Single(s => s.StartTime == "09:00").Status);
    }

    [Fact]
    public async Task GetAvailability_BookedWinsOverHold()
    {
        var (service, db, courtId) = Create();
        var date = FutureDate(3);
        var holder = db.Users.Single(u => u.Email == "member@test.local").Id;
        AddHourlyAvailability(db, courtId, date, 8, 23);
        AddHold(db, holder, courtId, date, new TimeOnly(9, 0), 1, DateTime.Now.AddMinutes(10));
        db.Reservations.Add(new Reservation
        {
            UserId = db.Users.Single(u => u.Email == "admin@test.local").Id,
            CourtId = courtId,
            ReservationDate = date,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            DurationHours = 1,
            TotalAmount = 25m,
            Status = ReservationStatus.Confirmed,
            ReservationReference = "SH-TEST-000001"
        });
        await db.SaveChangesAsync();

        var slots = await service.GetAvailabilityForDateAsync(date, courtId);

        Assert.Equal("booked", slots.Single(s => s.StartTime == "09:00").Status);
    }
}
