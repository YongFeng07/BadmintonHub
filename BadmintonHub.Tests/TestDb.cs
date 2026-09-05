using BadmintonHub.Data;
using BadmintonHub.Models;
using BadmintonHub.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BadmintonHub.Tests;

/// <summary>
/// Shared in-memory test database: one facility (open daily 08:00-23:00),
/// one court with 14 days of open availability, and three seeded users.
/// Each call returns a fresh isolated database.
/// </summary>
internal static class TestDb
{
    public static ApplicationDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"test-{Guid.NewGuid()}")
            // Checkout tests exercise the transactional path; the InMemory store has no
            // transactions, so silence the provider's "transaction ignored" throw.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new ApplicationDbContext(options);

        var facility = new Facility
        {
            Name = "Test Facility",
            Address = "1 Test Street",
            OperatingDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
            OpeningTime = new TimeSpan(8, 0, 0),
            ClosingTime = new TimeSpan(23, 0, 0)
        };
        db.Facilities.Add(facility);
        db.SaveChanges();

        var court = new Court
        {
            FacilityId = facility.Id,
            CourtNumber = "01",
            CourtType = CourtType.Standard,
            Status = CourtStatus.Available,
            HourlyRate = 25m
        };
        db.Courts.Add(court);
        db.SaveChanges();

        AddUser(db, "Test Member", "member@test.local", Role.Member, "Member@123");
        AddUser(db, "Test Admin", "admin@test.local", Role.Admin, "Admin@123");
        AddUser(db, "Test Admin 2", "admin2@test.local", Role.Admin, "Admin@123");
        AddUser(db, "Test SuperAdmin", "superadmin@test.local", Role.SuperAdmin, "SuperAdmin@123");
        db.SaveChanges();

        // Open availability 08:00-23:00 for the next 14 days (covers the booking tests).
        for (var d = 0; d < 14; d++)
        {
            db.CourtAvailabilities.Add(new CourtAvailability
            {
                CourtId = court.Id,
                Date = DateOnly.FromDateTime(DateTime.Today.AddDays(d)),
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(23, 0),
                Status = AvailabilityStatus.Open
            });
        }
        db.SaveChanges();
        return db;
    }

    /// <summary>Adds a seeded-style test user; public so individual tests can add extra accounts.</summary>
    public static User AddUser(ApplicationDbContext db, string name, string email, Role role, string password)
    {
        var (hash, salt) = PasswordHelper.HashPassword(password);
        var user = new User
        {
            FullName = name,
            Email = email,
            Phone = "012-000 0000",
            Role = role,
            PasswordHash = hash,
            PasswordSalt = salt,
            EmailVerified = true // seeded test accounts skip the verification gate
        };
        db.Users.Add(user);
        return user;
    }
}
