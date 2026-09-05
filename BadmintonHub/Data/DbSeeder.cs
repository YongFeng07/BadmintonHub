using BadmintonHub.Models;
using BadmintonHub.Services;

namespace BadmintonHub.Data;

/// <summary>
/// Seeds realistic demo data on first run (idempotent: skips when the database already has data).
/// All dates are relative to "today" so the demo always shows upcoming/past bookings.
/// Demo credentials are documented in the README — these are demo-only accounts.
/// The multi-sport demo seeds six facilities across five of the eleven categories
/// (revised spec): badminton, swimming, gymnasium, squash, table tennis and futsal.
/// </summary>
public static class DbSeeder
{
    public static void Seed(ApplicationDbContext db, string? webRootPath)
    {
        var now = DateTime.Now;

        // Demo accounts and system settings are kept in sync on every start (idempotent by
        // email/key), so databases created before the revised-spec roles pick up the new
        // SuperAdmin account without a reseed. Categories are upserted by name so databases
        // created before the P3 migration converge to the full 11-category set.
        EnsureDemoUsers(db, now);
        EnsureSystemSettings(db);
        EnsureCategories(db);

        if (db.Facilities.Any()) return;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var categories = db.Categories.ToDictionary(c => c.Name);

        // ---------- 1. Facilities + bookable units (multi-sport centre) ----------
        var courts = new List<Court>();
        var facilities = new List<Facility>();

        Facility AddFacility(string categoryName, string name, string address, string phone,
            string desc, TimeSpan open, TimeSpan close, string rules,
            (string Number, CourtType Type, decimal Rate, string Desc)[] unitSpecs)
        {
            var facility = new Facility
            {
                CategoryId = categories[categoryName].Id,
                Name = name,
                Description = desc,
                Address = address,
                Phone = phone,
                Email = "info@badmintonhub.my",
                OpeningTime = open,
                ClosingTime = close,
                OperatingDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
                Rules = rules,
                Status = FacilityStatus.Open
            };
            db.Facilities.Add(facility);
            db.SaveChanges();

            foreach (var spec in unitSpecs)
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
            facilities.Add(facility);
            return facility;
        }

        var badminton = AddFacility("Badminton Court",
            "BadmintonHub Main Facility",
            "12, Jalan Sukan 1, Taman Sukan, 53300 Kuala Lumpur", "03-4142 8899",
            "Purpose-built badminton centre in Kuala Lumpur with six indoor courts, professional-grade vinyl flooring, air-conditioning, shower rooms, a pro shop and free parking.",
            new TimeSpan(8, 0, 0), new TimeSpan(23, 0, 0),
            "1. All players must wear non-marking indoor court shoes.\r\n2. Please arrive 10 minutes before your booked time.\r\n3. Cancellations made at least 24 hours in advance receive a full refund.\r\n4. Rackets and shuttlecocks are available for rent at the counter.\r\n5. Management is not responsible for personal belongings left unattended.",
            new[]
            {
                ("01", CourtType.Standard, 25.00m, "Competition-grade vinyl flooring with anti-glare LED lighting and full air-conditioning."),
                ("02", CourtType.Standard, 25.00m, "Standard court with vinyl flooring, ideal for casual and social games."),
                ("03", CourtType.Standard, 25.00m, "Standard court near the entrance with natural ventilation and ceiling fans."),
                ("04", CourtType.Standard, 25.00m, "Freshly resurfaced court with extra spectator seating beside the entrance."),
                ("05", CourtType.VIP, 35.00m, "VIP court with premium vinyl flooring, exclusive lighting and VIP lounge access."),
                ("06", CourtType.Premium, 50.00m, "Premium court with international competition flooring and professional lighting, suitable for tournaments.")
            });

        var pool = AddFacility("Olympic-sized Swimming Pool",
            "BadmintonHub Aquatics Centre",
            "8, Jalan Akuatik, Taman Tasik, 50000 Kuala Lumpur", "03-4142 8801",
            "50 m Olympic-sized pool with eight lap lanes, a heated indoor training pool, sauna and steam room. Lifeguards on duty during all opening hours.",
            new TimeSpan(6, 0, 0), new TimeSpan(22, 0, 0),
            "1. Proper swimwear and swim caps are required.\r\n2. Shower before entering the pool.\r\n3. Children under 12 must be supervised by an adult.",
            new[]
            {
                ("01", CourtType.Standard, 20.00m, "Standard lap lane, shallow end for casual swimming."),
                ("02", CourtType.Standard, 20.00m, "Standard lap lane, medium pace swimmers."),
                ("03", CourtType.VIP, 25.00m, "VIP lane with pace clock and lane rope, deep end only."),
                ("04", CourtType.Premium, 30.00m, "Premium training lane with backstroke flags and video recording mount.")
            });

        var gym = AddFacility("Gymnasium",
            "FitZone Gymnasium",
            "45, Jalan Kekuatan, 51200 Kuala Lumpur", "03-4142 8802",
            "Fully equipped gym with strength, cardio and functional training zones, certified trainers on site and complimentary lockers.",
            new TimeSpan(6, 0, 0), new TimeSpan(23, 0, 0),
            "1. Bring a towel and use it on all equipment.\r\n2. Return weights to the racks after use.\r\n3. Booking covers one training station for the selected hour.",
            new[]
            {
                ("01", CourtType.Standard, 15.00m, "Strength station — free weights, benches and squat rack."),
                ("02", CourtType.Standard, 15.00m, "Cardio station — treadmills, bikes and rowers."),
                ("03", CourtType.Premium, 25.00m, "Premium station — functional rig, plyo boxes and PT consultation.")
            });

        var squash = AddFacility("Squash Court",
            "RacquetHub Squash Centre",
            "22, Jalan Raket, 50470 Kuala Lumpur", "03-4142 8803",
            "Three standard squash courts with glass back walls, competition lighting and a members' lounge with viewing gallery.",
            new TimeSpan(7, 0, 0), new TimeSpan(23, 0, 0),
            "1. Non-marking indoor court shoes only.\r\n2. Eye protection is recommended.\r\n3. Courts must be vacated at the end of the booked hour.",
            new[]
            {
                ("01", CourtType.Standard, 20.00m, "Standard squash court with glass back wall."),
                ("02", CourtType.Standard, 20.00m, "Standard squash court, closest to the lounge."),
                ("03", CourtType.VIP, 30.00m, "VIP show court with viewing gallery and competition lighting.")
            });

        var tableTennis = AddFacility("Table Tennis Room",
            "SpinSmash Table Tennis Hall",
            "7, Jalan Pingpong, 52100 Kuala Lumpur", "03-4142 8804",
            "Air-conditioned hall with four tournament-grade table tennis tables, ball feeders and a pro shop for rubbers and blades.",
            new TimeSpan(8, 0, 0), new TimeSpan(22, 0, 0),
            "1. Use the provided ball pickers to collect stray balls.\r\n2. Tables must not be moved.\r\n3. Reservations are per table per hour.",
            new[]
            {
                ("01", CourtType.Standard, 10.00m, "Tournament table with anti-glare lighting."),
                ("02", CourtType.Standard, 10.00m, "Tournament table near the ball feeders."),
                ("03", CourtType.Standard, 10.00m, "Standard table with extra spectator space."),
                ("04", CourtType.VIP, 20.00m, "VIP table with robot ball feeder and premium lighting.")
            });

        var futsal = AddFacility("Football / Futsal Pitch",
            "Goal Arena Futsal",
            "88, Jalan Gol, 53300 Kuala Lumpur", "03-4142 8805",
            "Roofed 5-a-side futsal pitches with FIFA-approved artificial turf, floodlights and changing rooms with showers.",
            new TimeSpan(8, 0, 0), new TimeSpan(23, 0, 0),
            "1. Flat-soled indoor football shoes or turf shoes only.\r\n2. Shin pads are recommended.\r\n3. Pitch bookings are per pitch per hour.",
            new[]
            {
                ("01", CourtType.Standard, 80.00m, "Standard 5-a-side pitch, artificial turf."),
                ("02", CourtType.VIP, 120.00m, "VIP pitch with camera gantry and premium turf.")
            });

        // ---------- 2. Court photos (demo placeholder images generated into wwwroot) ----------
        EnsurePlaceholderImages(webRootPath);
        var categoryNameOf = facilities.ToDictionary(f => f.Id,
            f => categories.First(kv => kv.Value.Id == f.CategoryId).Key);
        foreach (var court in courts)
        {
            var kind = court.FacilityId switch
            {
                _ when court.FacilityId == badminton.Id => "court",
                _ when court.FacilityId == pool.Id => "pool",
                _ when court.FacilityId == gym.Id => "gym",
                _ when court.FacilityId == squash.Id => "squash",
                _ when court.FacilityId == tableTennis.Id => "table",
                _ => "futsal"
            };
            var photoPath = kind == "court" ? $"/images/courts/court-{court.CourtNumber}.svg" : $"/images/courts/unit-{kind}.svg";

            db.CourtPhotos.Add(new CourtPhoto
            {
                CourtId = court.Id,
                FilePath = photoPath,
                Caption = $"{categoryNameOf[court.FacilityId]} {court.CourtNumber}",
                DisplayOrder = 1,
                IsPrimary = true
            });
            db.CourtPhotos.Add(new CourtPhoto
            {
                CourtId = court.Id,
                FilePath = $"/images/courts/facility-{kind}.svg",
                Caption = "Facility view",
                DisplayOrder = 2
            });
        }

        // ---------- 3. Facility photos ----------
        void AddFacilityPhotos(Facility facility, string primary, string secondary)
        {
            db.FacilityPhotos.Add(new FacilityPhoto
            {
                FacilityId = facility.Id,
                FilePath = primary,
                Caption = $"{facility.Name} — main view",
                DisplayOrder = 1,
                IsPrimary = true
            });
            db.FacilityPhotos.Add(new FacilityPhoto
            {
                FacilityId = facility.Id,
                FilePath = secondary,
                Caption = $"{facility.Name} — inside view",
                DisplayOrder = 2
            });
        }

        AddFacilityPhotos(badminton, "/images/courts/facility.svg", "/images/courts/court-01.svg");
        AddFacilityPhotos(pool, "/images/courts/facility-pool.svg", "/images/courts/unit-pool.svg");
        AddFacilityPhotos(gym, "/images/courts/facility-gym.svg", "/images/courts/unit-gym.svg");
        AddFacilityPhotos(squash, "/images/courts/facility-squash.svg", "/images/courts/unit-squash.svg");
        AddFacilityPhotos(tableTennis, "/images/courts/facility-table.svg", "/images/courts/unit-table.svg");
        AddFacilityPhotos(futsal, "/images/courts/facility-futsal.svg", "/images/courts/unit-futsal.svg");

        // ---------- 4. Availability: next 14 days, hourly slots per facility hours ----------
        foreach (var facility in facilities)
        {
            var facilityCourts = courts.Where(c => c.FacilityId == facility.Id).ToList();
            for (var day = 0; day < 14; day++)
            {
                var date = today.AddDays(day);
                foreach (var court in facilityCourts)
                {
                    for (var hour = facility.OpeningTime.Hours; hour < facility.ClosingTime.Hours; hour++)
                    {
                        var status = AvailabilityStatus.Open;

                        // Demo maintenance: Court 06 closed for maintenance in 3 days.
                        if (facility.Id == badminton.Id && court.CourtNumber == "06" && day == 3)
                            status = AvailabilityStatus.Maintenance;

                        // Demo blocked slot: Court 03, 18:00-19:00 tomorrow.
                        if (facility.Id == badminton.Id && court.CourtNumber == "03" && day == 1 && hour == 18)
                            status = AvailabilityStatus.Blocked;

                        // Pool lane 04 maintenance in 2 days; squash show court in 5 days.
                        if (facility.Id == pool.Id && court.CourtNumber == "04" && day == 2)
                            status = AvailabilityStatus.Maintenance;
                        if (facility.Id == squash.Id && court.CourtNumber == "03" && day == 5)
                            status = AvailabilityStatus.Maintenance;

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
        }
        db.SaveChanges();

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

        Court CourtOf(Facility facility, string number) =>
            courts.First(c => c.FacilityId == facility.Id && c.CourtNumber == number);
        User MemberByEmail(string email) => members.First(m => m.Email == email);

        var pastBadminton = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtOf(badminton, "01"), today.AddDays(-7), 18, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtOf(badminton, "02"), today.AddDays(-5), 20, 22, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtOf(badminton, "05"), today.AddDays(-4), 19, 21, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("john@example.com"), CourtOf(badminton, "03"), today.AddDays(-3), 10, 12, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtOf(badminton, "01"), today.AddDays(-2), 19, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(badminton, "06"), today.AddDays(-1), 20, 22, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtOf(badminton, "04"), today.AddDays(-1), 8, 9, ReservationStatus.Completed)
        };

        var upcomingBadminton = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtOf(badminton, "02"), today.AddDays(1), 18, 20, ReservationStatus.Confirmed, notes: "Weekly training session."),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtOf(badminton, "05"), today.AddDays(1), 20, 22, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtOf(badminton, "01"), today.AddDays(2), 9, 11, ReservationStatus.Pending, notes: "Family friendly game."),
            MakeReservation(MemberByEmail("john@example.com"), CourtOf(badminton, "06"), today.AddDays(2), 19, 21, ReservationStatus.Cancelled, cancelReason: "Player injured."),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtOf(badminton, "03"), today.AddDays(3), 17, 18, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(badminton, "04"), today.AddDays(5), 20, 22, ReservationStatus.Confirmed)
        };

        // Other facilities so the catalog's "Top 5 Popular" ranking has real data.
        var otherPast = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtOf(pool, "01"), today.AddDays(-6), 18, 19, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtOf(pool, "02"), today.AddDays(-4), 19, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtOf(pool, "01"), today.AddDays(-3), 7, 8, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("john@example.com"), CourtOf(pool, "03"), today.AddDays(-2), 18, 19, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtOf(squash, "01"), today.AddDays(-5), 19, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(squash, "01"), today.AddDays(-1), 18, 19, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtOf(futsal, "01"), today.AddDays(-4), 20, 22, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("john@example.com"), CourtOf(futsal, "01"), today.AddDays(-2), 19, 21, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(gym, "01"), today.AddDays(-7), 7, 9, ReservationStatus.Completed)
        };

        var otherUpcoming = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtOf(pool, "02"), today.AddDays(1), 18, 19, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(pool, "01"), today.AddDays(2), 19, 20, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtOf(pool, "04"), today.AddDays(5), 20, 21, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("member@badmintonhub.my"), CourtOf(squash, "02"), today.AddDays(1), 19, 20, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtOf(futsal, "02"), today.AddDays(3), 20, 22, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtOf(tableTennis, "01"), today.AddDays(2), 10, 11, ReservationStatus.Confirmed)
        };

        var allReservations = pastBadminton.Concat(upcomingBadminton).Concat(otherPast).Concat(otherUpcoming).ToList();
        db.Reservations.AddRange(allReservations);
        db.SaveChanges();

        // ---------- 7. Payments (one per reservation) ----------
        var paymentSpecs = new List<(Reservation R, string Email, PaymentMethod Method, PaymentStatus Status, string Ref, DateOnly? PaidOn)>();

        void Pay(Reservation r, string memberEmail, PaymentMethod method, PaymentStatus status,
            string reference, DateOnly? paidOn) =>
            paymentSpecs.Add((r, memberEmail, method, status, reference, paidOn));

        Pay(pastBadminton[0], "member@badmintonhub.my", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88210345", today.AddDays(-7));
        Pay(pastBadminton[1], "aiman@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-4491", today.AddDays(-5));
        Pay(pastBadminton[2], "priya@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88210811", today.AddDays(-4));
        Pay(pastBadminton[3], "john@example.com", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00821", today.AddDays(-3));
        Pay(pastBadminton[4], "nurul@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88211502", today.AddDays(-2));
        Pay(pastBadminton[5], "david@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-7812", today.AddDays(-1));
        Pay(pastBadminton[6], "member@badmintonhub.my", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00845", today.AddDays(-1));

        Pay(upcomingBadminton[0], "member@badmintonhub.my", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88213077", today);
        Pay(upcomingBadminton[1], "aiman@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-9930", today);
        Pay(upcomingBadminton[2], "priya@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Pending, "MB2U-AWAITING", null);
        Pay(upcomingBadminton[3], "john@example.com", PaymentMethod.Card, PaymentStatus.Refunded, "CARD-2201", today.AddDays(-2));
        Pay(upcomingBadminton[4], "nurul@example.com", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00859", today);
        Pay(upcomingBadminton[5], "david@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88214120", today);

        var otherPaid = otherPast.Concat(otherUpcoming).ToList();
        foreach (var (r, i) in otherPaid.Select((r, i) => (r, i)))
        {
            var payer = r.UserId == MemberByEmail("member@badmintonhub.my").Id ? "member@badmintonhub.my"
                : r.UserId == MemberByEmail("aiman@example.com").Id ? "aiman@example.com"
                : r.UserId == MemberByEmail("priya@example.com").Id ? "priya@example.com"
                : r.UserId == MemberByEmail("john@example.com").Id ? "john@example.com"
                : r.UserId == MemberByEmail("nurul@example.com").Id ? "nurul@example.com"
                : "david@example.com";
            var paidOn = r.ReservationDate > today ? today : r.ReservationDate.AddDays(-1);
            Pay(r, payer, PaymentMethod.OnlineTransfer, PaymentStatus.Paid, $"MB2U-8822{i:0000}", paidOn);
        }

        foreach (var (r, email, method, status, reference, paidOn) in paymentSpecs)
        {
            db.Payments.Add(new Payment
            {
                ReservationId = r.Id,
                UserId = MemberByEmail(email).Id,
                Amount = r.TotalAmount,
                Method = method,
                Status = status,
                PaymentReference = reference,
                PaidAt = paidOn.HasValue ? paidOn.Value.ToDateTime(new TimeOnly(12, 0)) : null,
                CreatedAt = r.CreatedAt
            });
        }
        db.SaveChanges();

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
            Message = $"Your booking {upcomingBadminton[0].ReservationReference} on Court 02 is confirmed and paid.",
            Type = NotificationType.Reservation,
            CreatedAt = now.AddDays(-1)
        });
        db.Notifications.Add(new Notification
        {
            UserId = MemberByEmail("john@example.com").Id,
            Title = "Reservation cancelled",
            Message = $"Booking {upcomingBadminton[3].ReservationReference} was cancelled and your payment has been refunded.",
            Type = NotificationType.Reservation,
            CreatedAt = now.AddDays(-1)
        });

        db.SaveChanges();
    }

    // ---------- Idempotent categories (upserted by name) ----------

    /// <summary>
    /// The eleven facility categories from the revised spec. Upserted by name so both
    /// fresh databases and pre-P3 databases (which the migration gives a single default
    /// category) converge to the full set.
    /// </summary>
    private static void EnsureCategories(ApplicationDbContext db)
    {
        var specs = new (string Name, string UnitLabel, string Icon, int Order, string Desc)[]
        {
            ("Badminton Court", "Court", "🏸", 1, "Indoor badminton courts with competition-grade flooring and lighting."),
            ("Indoor Basketball Court", "Court", "🏀", 2, "Full-size indoor basketball courts with sprung wooden flooring."),
            ("Volleyball Court", "Court", "🏐", 3, "Indoor volleyball courts with official net heights and markings."),
            ("Table Tennis Room", "Table", "🏓", 4, "Air-conditioned rooms with tournament-grade table tennis tables."),
            ("Football / Futsal Pitch", "Pitch", "⚽", 5, "Roofed 5-a-side futsal pitches with artificial turf."),
            ("Tennis Court", "Court", "🎾", 6, "Tennis courts with floodlights for evening play."),
            ("Squash Court", "Court", "🥍", 7, "Standard squash courts with glass back walls."),
            ("Olympic-sized Swimming Pool", "Lane", "🏊", 8, "50 m Olympic-sized pools with lap lanes for training."),
            ("Gymnasium", "Station", "🏋️", 9, "Fully equipped gyms with strength, cardio and functional zones."),
            ("Pickleball", "Court", "🟢", 10, "Pickleball courts; paddles and balls available for rent."),
            ("Indoor Running Track", "Track", "🏃", 11, "200 m indoor running tracks with banked curves.")
        };

        foreach (var (name, label, icon, order, desc) in specs)
        {
            var existing = db.Categories.FirstOrDefault(c => c.Name == name);
            if (existing == null)
            {
                db.Categories.Add(new Category
                {
                    Name = name,
                    UnitLabel = label,
                    Icon = icon,
                    DisplayOrder = order,
                    Description = desc,
                    Status = CategoryStatus.Active
                });
            }
            else
            {
                existing.UnitLabel = label;
                existing.Icon = icon;
                existing.DisplayOrder = order;
                existing.Description = desc;
                existing.Status = CategoryStatus.Active;
            }
        }
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

        // Unit + facility images for the other seeded categories (P3).
        var kinds = new (string Kind, string Label, string Colour)[]
        {
            ("pool", "SWIMMING POOL", "#1f6f8b"),
            ("gym", "GYMNASIUM", "#7a5c1e"),
            ("squash", "SQUASH COURT", "#5d3a6b"),
            ("table", "TABLE TENNIS", "#8b2f2f"),
            ("futsal", "FUTSAL PITCH", "#2f6b35")
        };
        foreach (var (kind, label, colour) in kinds)
        {
            var unitPath = Path.Combine(dir, $"unit-{kind}.svg");
            if (!File.Exists(unitPath))
                File.WriteAllText(unitPath, UnitSvg(kind, label, colour));
            var facilityKindPath = Path.Combine(dir, $"facility-{kind}.svg");
            if (!File.Exists(facilityKindPath))
                File.WriteAllText(facilityKindPath, FacilityKindSvg(kind, label, colour));
        }
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

    private static string UnitSvg(string kind, string label, string colour) =>
        $@"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 640 400' role='img' aria-label='{label}'>
  <rect width='640' height='400' fill='{colour}'/>
  <rect x='90' y='60' width='460' height='280' fill='#f2efe6' stroke='#ffffff' stroke-width='5'/>
  <rect x='90' y='60' width='460' height='280' fill='none' stroke='#d9d2c0' stroke-width='2'/>
  <text x='320' y='385' text-anchor='middle' font-family='Segoe UI, Arial, sans-serif' font-size='24' font-weight='700' fill='#ffffff'>BADMINTONHUB • {label}</text>
</svg>";

    private static string FacilityKindSvg(string kind, string label, string colour) =>
        $@"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 640 400' role='img' aria-label='{label} facility'>
  <rect width='640' height='400' fill='#eef4f0'/>
  <rect x='70' y='70' width='500' height='260' fill='{colour}' stroke='#ffffff' stroke-width='5'/>
  <rect x='250' y='130' width='140' height='200' fill='#ffffff' opacity='0.35'/>
  <rect x='110' y='110' width='90' height='70' fill='#ffffff' opacity='0.55'/>
  <rect x='440' y='110' width='90' height='70' fill='#ffffff' opacity='0.55'/>
  <text x='320' y='370' text-anchor='middle' font-family='Segoe UI, Arial, sans-serif' font-size='26' font-weight='700' fill='{colour}'>{label} FACILITY</text>
</svg>";
}
