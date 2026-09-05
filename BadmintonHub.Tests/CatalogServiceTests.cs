using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Tests;

/// <summary>
/// P3 catalog service: open-facility listing, category/name filters, top-5
/// popularity ranking (30-day confirmed/completed bookings), minimum hourly
/// rate and the 19:00 low-availability signal.
/// </summary>
public class CatalogServiceTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"test-catalog-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Category AddCategory(ApplicationDbContext db, string name, int order = 1)
    {
        var category = new Category { Name = name, UnitLabel = "Court", DisplayOrder = order, Status = CategoryStatus.Active };
        db.Categories.Add(category);
        db.SaveChanges();
        return category;
    }

    private static Facility AddFacility(ApplicationDbContext db, int categoryId, string name,
        FacilityStatus status = FacilityStatus.Open)
    {
        var facility = new Facility
        {
            CategoryId = categoryId,
            Name = name,
            Address = "1 Test Street",
            OperatingDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
            OpeningTime = new TimeSpan(8, 0, 0),
            ClosingTime = new TimeSpan(23, 0, 0),
            Status = status
        };
        db.Facilities.Add(facility);
        db.SaveChanges();
        return facility;
    }

    private static Court AddCourt(ApplicationDbContext db, int facilityId, string number, decimal rate = 25m)
    {
        var court = new Court
        {
            FacilityId = facilityId,
            CourtNumber = number,
            CourtType = CourtType.Standard,
            Status = CourtStatus.Available,
            HourlyRate = rate
        };
        db.Courts.Add(court);
        db.SaveChanges();
        return court;
    }

    private static void AddBooking(ApplicationDbContext db, Court court, DateOnly date, ReservationStatus status)
    {
        db.Reservations.Add(new Reservation
        {
            ReservationReference = $"BH-{Guid.NewGuid():N}"[..12],
            UserId = 1,
            CourtId = court.Id,
            ReservationDate = date,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            DurationHours = 1,
            TotalAmount = court.HourlyRate,
            Status = status
        });
        db.SaveChanges();
    }

    private static void AddOpenSlot(ApplicationDbContext db, Court court, DateOnly date, TimeOnly start)
    {
        db.CourtAvailabilities.Add(new CourtAvailability
        {
            CourtId = court.Id,
            Date = date,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = AvailabilityStatus.Open
        });
        db.SaveChanges();
    }

    private ICatalogService CreateService(ApplicationDbContext db) => new CatalogService(db);

    [Fact]
    public async Task GetCatalog_ListsOnlyOpenFacilities()
    {
        var db = CreateDb();
        var cat = AddCategory(db, "Test Category");
        AddFacility(db, cat.Id, "Open Facility");
        AddFacility(db, cat.Id, "Closed Facility", FacilityStatus.Closed);
        var service = CreateService(db);

        var vm = await service.GetCatalogAsync(null, null);

        Assert.Single(vm.Cards);
        Assert.Equal("Open Facility", vm.Cards[0].Facility.Name);
    }

    [Fact]
    public async Task GetCatalog_FiltersByCategory()
    {
        var db = CreateDb();
        var catA = AddCategory(db, "Category A", 1);
        var catB = AddCategory(db, "Category B", 2);
        AddFacility(db, catA.Id, "Facility A");
        AddFacility(db, catB.Id, "Facility B");
        var service = CreateService(db);

        var vm = await service.GetCatalogAsync(catB.Id, null);

        Assert.Single(vm.Cards);
        Assert.Equal("Facility B", vm.Cards[0].Facility.Name);
    }

    [Fact]
    public async Task GetCatalog_FiltersByName()
    {
        var db = CreateDb();
        var cat = AddCategory(db, "Test Category");
        AddFacility(db, cat.Id, "Alpha Arena");
        AddFacility(db, cat.Id, "Beta Bowl");
        var service = CreateService(db);

        var vm = await service.GetCatalogAsync(null, "alpha");

        Assert.Single(vm.Cards);
        Assert.Equal("Alpha Arena", vm.Cards[0].Facility.Name);
    }

    [Fact]
    public async Task GetCatalog_RanksTopFiveByRecentBookings()
    {
        var db = CreateDb();
        var cat = AddCategory(db, "Test Category");
        var today = DateOnly.FromDateTime(DateTime.Today);

        var busy = AddFacility(db, cat.Id, "Busy");
        var busyCourt = AddCourt(db, busy.Id, "01");
        AddBooking(db, busyCourt, today.AddDays(-3), ReservationStatus.Completed);
        AddBooking(db, busyCourt, today.AddDays(-1), ReservationStatus.Confirmed);
        AddBooking(db, busyCourt, today.AddDays(1), ReservationStatus.Confirmed);

        var mid = AddFacility(db, cat.Id, "Mid");
        var midCourt = AddCourt(db, mid.Id, "01");
        AddBooking(db, midCourt, today.AddDays(-2), ReservationStatus.Completed);

        // Old booking outside the 30-day window must not count.
        AddBooking(db, midCourt, today.AddDays(-45), ReservationStatus.Completed);

        var quiet = AddFacility(db, cat.Id, "Quiet");
        AddCourt(db, quiet.Id, "01");
        var service = CreateService(db);

        var vm = await service.GetCatalogAsync(null, null);

        Assert.Equal(1, vm.Cards.Single(c => c.Facility.Name == "Busy").PopularityRank);
        Assert.Equal(2, vm.Cards.Single(c => c.Facility.Name == "Mid").PopularityRank);
        Assert.Null(vm.Cards.Single(c => c.Facility.Name == "Quiet").PopularityRank);
    }

    [Fact]
    public async Task GetCatalog_ComputesMinHourlyRateAndUnitCount()
    {
        var db = CreateDb();
        var cat = AddCategory(db, "Test Category");
        var facility = AddFacility(db, cat.Id, "Rates");
        AddCourt(db, facility.Id, "01", 40m);
        AddCourt(db, facility.Id, "02", 25m);
        var service = CreateService(db);

        var vm = await service.GetCatalogAsync(null, null);

        var card = vm.Cards.Single();
        Assert.Equal(2, card.UnitCount);
        Assert.Equal(25m, card.MinHourlyRate);
    }

    [Fact]
    public async Task GetCatalog_AvailableAt1900_SubtractsActiveBookings()
    {
        var db = CreateDb();
        var cat = AddCategory(db, "Test Category");
        var facility = AddFacility(db, cat.Id, "Evening");
        var court1 = AddCourt(db, facility.Id, "01");
        var court2 = AddCourt(db, facility.Id, "02");
        var court3 = AddCourt(db, facility.Id, "03");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var evening = new TimeOnly(19, 0);

        AddOpenSlot(db, court1, today, evening);
        AddOpenSlot(db, court2, today, evening);
        AddOpenSlot(db, court3, today, evening);

        // Court 1 is booked 18:30-20:00 tonight → only 2 of 3 remain.
        db.Reservations.Add(new Reservation
        {
            ReservationReference = "BH-EVENING-1",
            UserId = 1,
            CourtId = court1.Id,
            ReservationDate = today,
            StartTime = new TimeOnly(18, 30),
            EndTime = new TimeOnly(20, 0),
            DurationHours = 2,
            TotalAmount = 50m,
            Status = ReservationStatus.Confirmed
        });
        db.SaveChanges();
        var service = CreateService(db);

        var vm = await service.GetCatalogAsync(null, null);

        Assert.Equal(2, vm.Cards.Single().AvailableAt1900); // triggers the ≤2 alert
    }

    [Fact]
    public async Task GetCatalog_PrefersPrimaryPhotoAsCover()
    {
        var db = CreateDb();
        var cat = AddCategory(db, "Test Category");
        var facility = AddFacility(db, cat.Id, "Photos");
        db.FacilityPhotos.AddRange(
            new FacilityPhoto { FacilityId = facility.Id, FilePath = "/a.jpg", DisplayOrder = 1 },
            new FacilityPhoto { FacilityId = facility.Id, FilePath = "/b.jpg", DisplayOrder = 2, IsPrimary = true });
        db.SaveChanges();
        var service = CreateService(db);

        var vm = await service.GetCatalogAsync(null, null);

        Assert.Equal("/b.jpg", vm.Cards.Single().PrimaryPhotoPath);
    }
}
