using BadmintonHub.Models;
using BadmintonHub.Services;

namespace BadmintonHub.Data;

/// <summary>
/// Seeds realistic demo data on first run (idempotent: skips when the database already has data).
/// All dates are relative to "today" so the demo always shows upcoming/past bookings.
/// Demo credentials are documented in the README — these are demo-only accounts.
/// </summary>
public static class DbSeeder
{
    public static void Seed(ApplicationDbContext db, string? webRootPath)
    {
        var now = DateTime.Now;

        // Demo accounts and system settings are kept in sync on every start (idempotent by
        // email/key), so databases created before the revised-spec roles pick up the new
        // SuperAdmin account without a reseed.
        EnsureDemoUsers(db, now);
        EnsureSystemSettings(db);

        if (db.Facilities.Any()) return;

        var today = DateOnly.FromDateTime(DateTime.Today);

        // ---------- 1. Facility ----------
        var facility = new Facility
        {
            Name = "BadmintonHub Main Facility",
            Description = "Purpose-built badminton centre in Kuala Lumpur with six indoor courts, professional-grade vinyl flooring, air-conditioning, shower rooms, a pro shop and free parking. Open daily 8:00 AM to 11:00 PM.",
            Address = "12, Jalan Sukan 1, Taman Sukan, 53300 Kuala Lumpur",
            Phone = "03-4142 8899",
            Email = "info@badmintonhub.my",
            OpeningTime = new TimeSpan(8, 0, 0),
            ClosingTime = new TimeSpan(23, 0, 0),
            OperatingDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
            Rules = "1. All players must wear non-marking indoor court shoes.\r\n2. Please arrive 10 minutes before your booked time.\r\n3. Cancellations made at least 24 hours in advance receive a full refund.\r\n4. Rackets and shuttlecocks are available for rent at the counter.\r\n5. Management is not responsible for personal belongings left unattended.",
            Status = FacilityStatus.Open
        };
        db.Facilities.Add(facility);
        db.SaveChanges();

        // ---------- 2. Courts ----------
        var courtSpecs = new[]
        {
            new { Number = "01", Type = CourtType.Standard, Rate = 25.00m, Desc = "Competition-grade vinyl flooring with anti-glare LED lighting and full air-conditioning." },
            new { Number = "02", Type = CourtType.Standard, Rate = 25.00m, Desc = "Standard court with vinyl flooring, ideal for casual and social games." },
            new { Number = "03", Type = CourtType.Standard, Rate = 25.00m, Desc = "Standard court near the entrance with natural ventilation and ceiling fans." },
            new { Number = "04", Type = CourtType.Standard, Rate = 25.00m, Desc = "Freshly resurfaced court with extra spectator seating beside the entrance." },
            new { Number = "05", Type = CourtType.VIP, Rate = 35.00m, Desc = "VIP court with premium vinyl flooring, exclusive lighting and VIP lounge access." },
            new { Number = "06", Type = CourtType.Premium, Rate = 50.00m, Desc = "Premium court with international competition flooring and professional lighting, suitable for tournaments." }
        };

        var courts = new List<Court>();
        foreach (var spec in courtSpecs)
        {
            var court = new Court
            {
                FacilityId = facility.Id,
                CourtNumber = spec.Number,
                CourtType = spec.Type,
                Status = CourtStatus.Available,
                HourlyRate = spec.Rate,
                Description = spec.Desc,
                CreatedAt = now.AddDays(-120)
            };
            db.Courts.Add(court);
            courts.Add(court);
        }
        db.SaveChanges();

        // ---------- 3. Court photos (demo placeholder images generated into wwwroot) ----------
        EnsurePlaceholderImages(webRootPath);
        foreach (var court in courts)
        {
            db.CourtPhotos.Add(new CourtPhoto
            {
                CourtId = court.Id,
                FilePath = $"/images/courts/court-{court.CourtNumber}.svg",
                Caption = $"Court {court.CourtNumber} - {court.CourtType} court",
                DisplayOrder = 1,
                IsPrimary = true
            });
            db.CourtPhotos.Add(new CourtPhoto
            {
                CourtId = court.Id,
                FilePath = "/images/courts/facility.svg",
                Caption = "BadmintonHub facility view",
                DisplayOrder = 2
            });
        }

        // ---------- 4. Availability: next 14 days, hourly slots 08:00-23:00 ----------
        for (var day = 0; day < 14; day++)
        {
            var date = today.AddDays(day);
            foreach (var court in courts)
            {
                for (var hour = 8; hour < 23; hour++)
                {
                    var status = AvailabilityStatus.Open;

                    // Demo maintenance: Court 06 closed for maintenance in 3 days.
                    if (court.CourtNumber == "06" && day == 3)
                        status = AvailabilityStatus.Maintenance;

                    // Demo blocked slot: Court 03, 18:00-19:00 tomorrow.
                    if (court.CourtNumber == "03" && day == 1 && hour == 18)
                        status = AvailabilityStatus.Blocked;

                    db.CourtAvailabilities.Add(new CourtAvailability
                    {
                        CourtId = court.Id,
                        Date = date,
                        StartTime = new TimeOnly(hour, 0),
                        EndTime = new TimeOnly(hour + 1, 0),
                        Status = status,
                        CreatedAt = now
                    });
                }
            }
        }

        // ---------- 5. Users (demo accounts; EnsureDemoUsers ran above) ----------
        var members = db.Users.Where(u => u.Role == Role.Member).ToList();

        // ---------- 6. Reservations (past completed, upcoming, cancelled) ----------
        var refCounter = 100;
        string NextRef() => $"BH-{today.Year}-{++refCounter:000000}";

        Reservation MakeReservation(User member, Court court, DateOnly date, int startHour, int endHour,
            ReservationStatus status, string? notes = null, string? cancelReason = null)
        {
            var start = new TimeOnly(startHour, 0);
            var end = new TimeOnly(endHour, 0);
            return new Reservation
            {
                ReservationReference = NextRef(),
                UserId = member.Id,
                CourtId = court.Id,
                ReservationDate = date,
                StartTime = start,
                EndTime = end,
                DurationHours = endHour - startHour,
                TotalAmount = court.HourlyRate * (endHour - startHour),
                Status = status,
                Notes = notes,
                CancellationReason = cancelReason,
                CancelledAt = status == ReservationStatus.Cancelled ? now.AddDays(-1) : null,
                CreatedAt = date.ToDateTime(new TimeOnly(10, 0)).AddDays(-7),
                UpdatedAt = status == ReservationStatus.Cancelled ? now.AddDays(-1) : null
            };
        }

        Court CourtByNumber(string number) => courts.First(c => c.CourtNumber == number);
        User MemberByEmail(string email) => members.First(m => m.Email == email);

        var pastReservations = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtByNumber("01"), today.AddDays(-7), 18, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtByNumber("02"), today.AddDays(-5), 20, 22, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtByNumber("05"), today.AddDays(-4), 19, 21, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("john@example.com"), CourtByNumber("03"), today.AddDays(-3), 10, 12, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtByNumber("01"), today.AddDays(-2), 19, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("david@example.com"), CourtByNumber("06"), today.AddDays(-1), 20, 22, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtByNumber("04"), today.AddDays(-1), 8, 9, ReservationStatus.Completed)
        };

        var upcomingReservations = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtByNumber("02"), today.AddDays(1), 18, 20, ReservationStatus.Confirmed, notes: "Weekly training session."),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtByNumber("05"), today.AddDays(1), 20, 22, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtByNumber("01"), today.AddDays(2), 9, 11, ReservationStatus.Pending, notes: "Family friendly game."),
            MakeReservation(MemberByEmail("john@example.com"), CourtByNumber("06"), today.AddDays(2), 19, 21, ReservationStatus.Cancelled, cancelReason: "Player injured."),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtByNumber("03"), today.AddDays(3), 17, 18, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("david@example.com"), CourtByNumber("04"), today.AddDays(5), 20, 22, ReservationStatus.Confirmed)
        };

        var allReservations = pastReservations.Concat(upcomingReservations).ToList();
        db.Reservations.AddRange(allReservations);
        db.SaveChanges();

        // ---------- 7. Payments (one per reservation) ----------
        void Pay(Reservation r, string memberEmail, PaymentMethod method, PaymentStatus status,
            string reference, DateOnly? paidOn)
        {
            db.Payments.Add(new Payment
            {
                ReservationId = r.Id,
                UserId = MemberByEmail(memberEmail).Id,
                Amount = r.TotalAmount,
                Method = method,
                Status = status,
                PaymentReference = reference,
                PaidAt = paidOn.HasValue ? paidOn.Value.ToDateTime(new TimeOnly(12, 0)) : null,
                CreatedAt = r.CreatedAt
            });
        }

        Pay(pastReservations[0], "member@badmintonhub.my", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88210345", today.AddDays(-7));
        Pay(pastReservations[1], "aiman@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-4491", today.AddDays(-5));
        Pay(pastReservations[2], "priya@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88210811", today.AddDays(-4));
        Pay(pastReservations[3], "john@example.com", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00821", today.AddDays(-3));
        Pay(pastReservations[4], "nurul@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88211502", today.AddDays(-2));
        Pay(pastReservations[5], "david@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-7812", today.AddDays(-1));
        Pay(pastReservations[6], "member@badmintonhub.my", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00845", today.AddDays(-1));

        Pay(upcomingReservations[0], "member@badmintonhub.my", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88213077", today);
        Pay(upcomingReservations[1], "aiman@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-9930", today);
        Pay(upcomingReservations[2], "priya@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Pending, "MB2U-AWAITING", null);
        Pay(upcomingReservations[3], "john@example.com", PaymentMethod.Card, PaymentStatus.Refunded, "CARD-2201", today.AddDays(-2));
        Pay(upcomingReservations[4], "nurul@example.com", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00859", today);
        Pay(upcomingReservations[5], "david@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88214120", today);

        // ---------- 8. Notifications ----------
        foreach (var m in members)
        {
            db.Notifications.Add(new Notification
            {
                UserId = m.Id,
                Title = "Welcome to BadmintonHub!",
                Message = "Your account is ready. Browse available courts and book your first game today.",
                Type = NotificationType.System,
                CreatedAt = m.CreatedAt
            });
        }
        db.Notifications.Add(new Notification
        {
            UserId = MemberByEmail("member@badmintonhub.my").Id,
            Title = "Reservation confirmed",
            Message = $"Your booking {upcomingReservations[0].ReservationReference} on Court 02 is confirmed and paid.",
            Type = NotificationType.Reservation,
            CreatedAt = now.AddDays(-1)
        });
        db.Notifications.Add(new Notification
        {
            UserId = MemberByEmail("john@example.com").Id,
            Title = "Reservation cancelled",
            Message = $"Booking {upcomingReservations[3].ReservationReference} was cancelled and your payment has been refunded.",
            Type = NotificationType.Reservation,
            CreatedAt = now.AddDays(-1)
        });

        db.SaveChanges();
    }

    // ---------- Idempotent security accounts & system settings ----------

    /// <summary>
    /// Demo accounts for the revised-spec roles (SuperAdmin / Admin / Member).
    /// Idempotent by email so it also upgrades pre-existing databases.
    /// </summary>
    private static List<User> EnsureDemoUsers(ApplicationDbContext db, DateTime now)
    {
        var members = new List<User>();
        void AddUser(string name, string email, string phone, Role role, string password)
        {
            if (db.Users.Any(u => u.Email == email)) return;
            var (hash, salt) = PasswordHelper.HashPassword(password);
            var user = new User
            {
                FullName = name,
                Email = email,
                Phone = phone,
                Role = role,
                PasswordHash = hash,
                PasswordSalt = salt,
                Status = UserStatus.Active,
                EmailVerified = true, // demo accounts are pre-verified
                CreatedAt = now.AddDays(-120),
                LastLoginAt = now.AddDays(-1)
            };
            db.Users.Add(user);
            if (role == Role.Member) members.Add(user);
        }

        AddUser("Ahmad Faiz", "superadmin@badmintonhub.my", "012-345 6709", Role.SuperAdmin, "SuperAdmin@123");
        AddUser("Siti Aminah", "admin@badmintonhub.my", "012-345 6701", Role.Admin, "Admin@123");
        AddUser("Lim Wei Jian", "admin2@badmintonhub.my", "012-345 6702", Role.Admin, "Admin@123");
        AddUser("Tan Mei Ling", "member@badmintonhub.my", "012-345 6703", Role.Member, "Member@123");
        AddUser("Muhammad Aiman", "aiman@example.com", "012-345 6704", Role.Member, "Member@123");
        AddUser("Priya Nair", "priya@example.com", "012-345 6705", Role.Member, "Member@123");
        AddUser("John Wong", "john@example.com", "012-345 6706", Role.Member, "Member@123");
        AddUser("Nurul Huda", "nurul@example.com", "012-345 6707", Role.Member, "Member@123");
        AddUser("David Chen", "david@example.com", "012-345 6708", Role.Member, "Member@123");
        db.SaveChanges();
        return members;
    }

    private static void EnsureSystemSettings(ApplicationDbContext db)
    {
        void AddSetting(string key, string? value)
        {
            if (db.SystemSettings.Any(s => s.Key == key)) return;
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        }

        AddSetting("SiteName", "BadmintonHub");
        AddSetting("SiteAnnouncement", "New season — book your court today! Open daily 8:00 AM to 11:00 PM.");
        db.SaveChanges();
    }

    // ---------- Placeholder demo images ----------

    private static void EnsurePlaceholderImages(string? webRootPath)
    {
        if (string.IsNullOrEmpty(webRootPath)) return;
        var dir = Path.Combine(webRootPath, "images", "courts");
        Directory.CreateDirectory(dir);

        for (var i = 1; i <= 6; i++)
        {
            var path = Path.Combine(dir, $"court-{i:00}.svg");
            if (!File.Exists(path))
                File.WriteAllText(path, CourtSvg(i));
        }

        var facilityPath = Path.Combine(dir, "facility.svg");
        if (!File.Exists(facilityPath))
            File.WriteAllText(facilityPath, FacilitySvg);
    }

    private static string CourtSvg(int number) =>
        @"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 640 400' role='img' aria-label='Court " + $"{number:00}" + @"'>
  <rect width='640' height='400' fill='#0e3a2f'/>
  <rect x='60' y='40' width='520' height='320' fill='#17714f' stroke='#ffffff' stroke-width='4'/>
  <line x1='320' y1='40' x2='320' y2='360' stroke='#ffffff' stroke-width='6'/>
  <rect x='60' y='40' width='260' height='320' fill='none' stroke='#d9e9df' stroke-width='2'/>
  <rect x='320' y='40' width='260' height='320' fill='none' stroke='#d9e9df' stroke-width='2'/>
  <rect x='190' y='40' width='130' height='320' fill='none' stroke='#f2d13b' stroke-width='3'/>
  <rect x='320' y='40' width='130' height='320' fill='none' stroke='#f2d13b' stroke-width='3'/>
  <text x='320' y='390' text-anchor='middle' font-family='Segoe UI, Arial, sans-serif' font-size='26' font-weight='700' fill='#ffffff'>BADMINTONHUB • COURT " + $"{number:00}" + @"</text>
</svg>";

    private const string FacilitySvg =
        @"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 640 400' role='img' aria-label='BadmintonHub facility'>
  <rect width='640' height='400' fill='#dcefe6'/>
  <rect x='80' y='80' width='480' height='240' fill='#e8e2d5' stroke='#8a8a8a' stroke-width='3'/>
  <polygon points='80,80 320,20 560,80' fill='#b5453a' stroke='#7e2f26' stroke-width='3'/>
  <rect x='260' y='180' width='120' height='140' fill='#5a4a3a'/>
  <rect x='120' y='120' width='80' height='60' fill='#9fc5e8' stroke='#5b7d99' stroke-width='2'/>
  <rect x='440' y='120' width='80' height='60' fill='#9fc5e8' stroke='#5b7d99' stroke-width='2'/>
  <text x='320' y='365' text-anchor='middle' font-family='Segoe UI, Arial, sans-serif' font-size='26' font-weight='700' fill='#1f4d3a'>BADMINTONHUB MAIN FACILITY</text>
</svg>";
}
