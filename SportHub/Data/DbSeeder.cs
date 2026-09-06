using SportHub.Models;
using SportHub.Services;

namespace SportHub.Data;

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
        // created before the P3 migration converge to the full 11-category set; demo vouchers
        // are upserted by code for the same reason.
        EnsureDemoUsers(db, now);
        EnsureSystemSettings(db);
        EnsureCategories(db);
        EnsureVouchers(db);
        // G-M3: backfill atomic slot claims for active reservations in pre-G3 databases.
        // (Fresh databases get their rows seeded right after the reservations below.)
        EnsureReservationSlots(db);

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
                Email = "info@sporthub.my",
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
            "SportHub Main Facility",
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
            "SportHub Aquatics Centre",
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

        // ---------- 2. Court & facility photos ----------
        // Real-world photographs from Wikimedia Commons, committed under
        // wwwroot/images/courts/ (see docs/PHOTO_CREDITS.md for author/license).
        // One photo set per sport; if a file is missing the seeder falls back to
        // the next photo of the same sport, then to the badminton default, so
        // seeding never breaks over a deleted file.
        var photoSets = new Dictionary<string, string[]>
        {
            ["court"]  = new[] { "badminton-01", "badminton-02", "badminton-03", "badminton-04", "badminton-05", "badminton-06", "badminton-hall" },
            ["pool"]   = new[] { "pool-unit", "pool-facility", "pool-view" },
            ["gym"]    = new[] { "gym-unit", "gym-facility", "gym-view" },
            ["squash"] = new[] { "squash-unit", "squash-facility", "squash-view" },
            ["table"]  = new[] { "table-unit", "table-facility", "table-view" },
            ["futsal"] = new[] { "futsal-unit", "futsal-facility", "futsal-view" }
        };

        var photoBaseDir = Path.Combine(webRootPath ?? "", "images", "courts");
        string PhotoUrl(string kind, int preferredIndex)
        {
            var set = photoSets.TryGetValue(kind, out var photos) ? photos : photoSets["court"];
            for (var attempt = 0; attempt < set.Length; attempt++)
            {
                var file = set[(preferredIndex + attempt) % set.Length];
                if (File.Exists(Path.Combine(photoBaseDir, file + ".jpg")))
                    return $"/images/courts/{file}.jpg";
            }
            // Nothing of this sport exists on disk yet — fall back to the first
            // committed photo (or the legacy placeholder while it still exists).
            return File.Exists(Path.Combine(photoBaseDir, "badminton-01.jpg"))
                ? "/images/courts/badminton-01.jpg"
                : "/images/courts/court-01.svg";
        }

        string NamedPhoto(string kind, string name)
        {
            return File.Exists(Path.Combine(photoBaseDir, name + ".jpg"))
                ? $"/images/courts/{name}.jpg"
                : PhotoUrl(kind, 1);
        }

        var categoryNameOf = facilities.ToDictionary(f => f.Id,
            f => categories.First(kv => kv.Value.Id == f.CategoryId).Key);
        foreach (var facility in facilities)
        {
            var kind = facility.Id == badminton.Id ? "court"
                : facility.Id == pool.Id ? "pool"
                : facility.Id == gym.Id ? "gym"
                : facility.Id == squash.Id ? "squash"
                : facility.Id == tableTennis.Id ? "table"
                : "futsal";
            var facilityCourts = courts.Where(c => c.FacilityId == facility.Id).ToList();
            for (var i = 0; i < facilityCourts.Count; i++)
            {
                var court = facilityCourts[i];
                // Badminton courts get one numbered photo each; the other sports
                // share their unit photo, with a second view as the gallery image.
                var primary = kind == "court" ? PhotoUrl("court", i) : PhotoUrl(kind, 0);
                var secondary = kind == "court" ? NamedPhoto("court", "badminton-hall") : PhotoUrl(kind, 1);

                db.CourtPhotos.Add(new CourtPhoto
                {
                    CourtId = court.Id,
                    FilePath = primary,
                    Caption = $"{categoryNameOf[facility.Id]} {court.CourtNumber}",
                    DisplayOrder = 1,
                    IsPrimary = true
                });
                db.CourtPhotos.Add(new CourtPhoto
                {
                    CourtId = court.Id,
                    FilePath = secondary,
                    Caption = "Facility view",
                    DisplayOrder = 2
                });
            }
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

        AddFacilityPhotos(badminton, NamedPhoto("court", "badminton-hall"), PhotoUrl("court", 1));
        AddFacilityPhotos(pool, NamedPhoto("pool", "pool-facility"), PhotoUrl("pool", 2));
        AddFacilityPhotos(gym, NamedPhoto("gym", "gym-facility"), PhotoUrl("gym", 2));
        AddFacilityPhotos(squash, NamedPhoto("squash", "squash-facility"), PhotoUrl("squash", 2));
        AddFacilityPhotos(tableTennis, NamedPhoto("table", "table-facility"), PhotoUrl("table", 2));
        AddFacilityPhotos(futsal, NamedPhoto("futsal", "futsal-facility"), PhotoUrl("futsal", 2));

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
        string NextRef() => $"SH-{today.Year}-{++refCounter:000000}";

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
            MakeReservation(MemberByEmail("member@sporthub.my"), CourtOf(badminton, "01"), today.AddDays(-7), 18, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtOf(badminton, "02"), today.AddDays(-5), 20, 22, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtOf(badminton, "05"), today.AddDays(-4), 19, 21, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("john@example.com"), CourtOf(badminton, "03"), today.AddDays(-3), 10, 12, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtOf(badminton, "01"), today.AddDays(-2), 19, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(badminton, "06"), today.AddDays(-1), 20, 22, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("member@sporthub.my"), CourtOf(badminton, "04"), today.AddDays(-1), 8, 9, ReservationStatus.Completed)
        };

        var upcomingBadminton = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@sporthub.my"), CourtOf(badminton, "02"), today.AddDays(1), 18, 20, ReservationStatus.Confirmed, notes: "Weekly training session."),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtOf(badminton, "05"), today.AddDays(1), 20, 22, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtOf(badminton, "01"), today.AddDays(2), 9, 11, ReservationStatus.Pending, notes: "Family friendly game."),
            MakeReservation(MemberByEmail("john@example.com"), CourtOf(badminton, "06"), today.AddDays(2), 19, 21, ReservationStatus.Cancelled, cancelReason: "Player injured."),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtOf(badminton, "03"), today.AddDays(3), 17, 18, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(badminton, "04"), today.AddDays(5), 20, 22, ReservationStatus.Confirmed)
        };

        // Other facilities so the catalog's "Top 5 Popular" ranking has real data.
        var otherPast = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@sporthub.my"), CourtOf(pool, "01"), today.AddDays(-6), 18, 19, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtOf(pool, "02"), today.AddDays(-4), 19, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtOf(pool, "01"), today.AddDays(-3), 7, 8, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("john@example.com"), CourtOf(pool, "03"), today.AddDays(-2), 18, 19, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtOf(squash, "01"), today.AddDays(-5), 19, 20, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(squash, "01"), today.AddDays(-1), 18, 19, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("member@sporthub.my"), CourtOf(futsal, "01"), today.AddDays(-4), 20, 22, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("john@example.com"), CourtOf(futsal, "01"), today.AddDays(-2), 19, 21, ReservationStatus.Completed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(gym, "01"), today.AddDays(-7), 7, 9, ReservationStatus.Completed)
        };

        var otherUpcoming = new List<Reservation>
        {
            MakeReservation(MemberByEmail("member@sporthub.my"), CourtOf(pool, "02"), today.AddDays(1), 18, 19, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("david@example.com"), CourtOf(pool, "01"), today.AddDays(2), 19, 20, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("aiman@example.com"), CourtOf(pool, "04"), today.AddDays(5), 20, 21, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("member@sporthub.my"), CourtOf(squash, "02"), today.AddDays(1), 19, 20, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("priya@example.com"), CourtOf(futsal, "02"), today.AddDays(3), 20, 22, ReservationStatus.Confirmed),
            MakeReservation(MemberByEmail("nurul@example.com"), CourtOf(tableTennis, "01"), today.AddDays(2), 10, 11, ReservationStatus.Confirmed)
        };

        var allReservations = pastBadminton.Concat(upcomingBadminton).Concat(otherPast).Concat(otherUpcoming).ToList();
        db.Reservations.AddRange(allReservations);
        db.SaveChanges();

        // G-M3: claim the booked hours for the seeded active reservations. The seeded
        // Pending booking (priya, court 01) also has a Pending payment, so the 30-minute
        // payment-timeout worker cancels and releases it on the first boot — deliberate:
        // it demos the auto-release flow end to end.
        EnsureReservationSlots(db);

        // ---------- 7. Payments (one per reservation) ----------
        var paymentSpecs = new List<(Reservation R, string Email, PaymentMethod Method, PaymentStatus Status, string Ref, DateOnly? PaidOn)>();

        void Pay(Reservation r, string memberEmail, PaymentMethod method, PaymentStatus status,
            string reference, DateOnly? paidOn) =>
            paymentSpecs.Add((r, memberEmail, method, status, reference, paidOn));

        Pay(pastBadminton[0], "member@sporthub.my", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88210345", today.AddDays(-7));
        Pay(pastBadminton[1], "aiman@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-4491", today.AddDays(-5));
        Pay(pastBadminton[2], "priya@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88210811", today.AddDays(-4));
        Pay(pastBadminton[3], "john@example.com", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00821", today.AddDays(-3));
        Pay(pastBadminton[4], "nurul@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88211502", today.AddDays(-2));
        Pay(pastBadminton[5], "david@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-7812", today.AddDays(-1));
        Pay(pastBadminton[6], "member@sporthub.my", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00845", today.AddDays(-1));

        Pay(upcomingBadminton[0], "member@sporthub.my", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88213077", today);
        Pay(upcomingBadminton[1], "aiman@example.com", PaymentMethod.Card, PaymentStatus.Paid, "CARD-9930", today);
        Pay(upcomingBadminton[2], "priya@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Pending, "MB2U-AWAITING", null);
        Pay(upcomingBadminton[3], "john@example.com", PaymentMethod.Card, PaymentStatus.Refunded, "CARD-2201", today.AddDays(-2));
        Pay(upcomingBadminton[4], "nurul@example.com", PaymentMethod.Cash, PaymentStatus.Paid, "CASH-00859", today);
        Pay(upcomingBadminton[5], "david@example.com", PaymentMethod.OnlineTransfer, PaymentStatus.Paid, "MB2U-88214120", today);

        var otherPaid = otherPast.Concat(otherUpcoming).ToList();
        foreach (var (r, i) in otherPaid.Select((r, i) => (r, i)))
        {
            var payer = r.UserId == MemberByEmail("member@sporthub.my").Id ? "member@sporthub.my"
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
                Title = "Welcome to SportHub!",
                Message = "Your account is ready. Browse available courts and book your first game today.",
                Type = NotificationType.System,
                CreatedAt = m.CreatedAt
            });
        }
        db.Notifications.Add(new Notification
        {
            UserId = MemberByEmail("member@sporthub.my").Id,
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

        // ---------- 9. Wishlist demo (revised spec) ----------
        // member@ keeps two courts on the wishlist — squash "02" (already booked by this
        // member for tomorrow) and the gym cardio station. Idempotent by (UserId, CourtId).
        var wishlistOwner = MemberByEmail("member@sporthub.my");
        foreach (var wishCourt in new[] { CourtOf(squash, "02"), CourtOf(gym, "02") })
        {
            if (!db.WishlistItems.Any(w => w.UserId == wishlistOwner.Id && w.CourtId == wishCourt.Id))
            {
                db.WishlistItems.Add(new WishlistItem { UserId = wishlistOwner.Id, CourtId = wishCourt.Id });
            }
        }

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

        AddUser("Ahmad Faiz", "superadmin@sporthub.my", "012-345 6709", Role.SuperAdmin, "SuperAdmin@123");
        AddUser("Siti Aminah", "admin@sporthub.my", "012-345 6701", Role.Admin, "Admin@123");
        AddUser("Lim Wei Jian", "admin2@sporthub.my", "012-345 6702", Role.Admin, "Admin@123");
        AddUser("Tan Mei Ling", "member@sporthub.my", "012-345 6703", Role.Member, "Member@123");
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

        AddSetting("SiteName", "SportHub");
        AddSetting("SiteAnnouncement", "New season — book your court today! Open daily 8:00 AM to 11:00 PM.");
        db.SaveChanges();
    }

    // ---------- Idempotent demo vouchers (upserted by code) ----------

    /// <summary>
    /// Checkout discount vouchers for the revised spec. Idempotent by code so pre-P4
    /// databases converge on every start. Each demo voucher demonstrates one G-M2 rule:
    /// STUDENT5 the per-user limit (2 per member), MINSPEND50 the minimum-spend rule,
    /// CAPPED20 the discount cap, EXPIRED the expiry path (lazily flipped to Expired
    /// by validation and by VoucherExpiryWorker).
    /// </summary>
    private static void EnsureVouchers(ApplicationDbContext db)
    {
        var specs = new (string Code, string Desc, DiscountType Type, decimal Value, int DaysValid,
            int? Limit, int? PerUser, decimal MinSpend, decimal? MaxDiscount)[]
        {
            ("WELCOME10", "10% off your first checkout (demo voucher)", DiscountType.Percentage, 10m, 60, null, null, 0, null),
            ("STUDENT5", "RM 5 off any booking cart (demo voucher, 50 redemptions, 2 per member)",
                DiscountType.FixedAmount, 5m, 30, 50, 2, 0, null),
            ("MINSPEND50", "RM 8 off when you spend RM 50 or more (demo voucher)", DiscountType.FixedAmount, 8m, 45, null, null, 50, null),
            ("CAPPED20", "20% off, capped at RM 10 (demo voucher)", DiscountType.Percentage, 20m, 30, null, null, 0, 10m),
            ("EXPIRED", "Expired demo voucher — used to demonstrate expiry validation", DiscountType.Percentage, 20m, -1, null, null, 0, null)
        };

        foreach (var (code, desc, type, value, daysValid, limit, perUser, minSpend, maxDiscount) in specs)
        {
            if (db.Vouchers.Any(v => v.Code == code)) continue;
            db.Vouchers.Add(new Voucher
            {
                Code = code,
                Description = desc,
                DiscountType = type,
                DiscountValue = value,
                ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(daysValid),
                MinSpend = minSpend,
                MaxDiscount = maxDiscount,
                UsageLimit = limit,
                PerUserLimit = perUser,
                Status = VoucherStatus.Active
            });
        }
        db.SaveChanges();
    }

    // ---------- Idempotent atomic slot claims (G-M3) ----------

    /// <summary>
    /// Backfills ReservationSlot rows for active (Pending/Confirmed) reservations so the
    /// (CourtId, Date, StartTime) unique index covers bookings created before the G3
    /// migration. Idempotent: only inserts hours that are not already claimed.
    /// </summary>
    private static void EnsureReservationSlots(ApplicationDbContext db)
    {
        var active = db.Reservations
            .Where(r => r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed)
            .ToList();
        if (active.Count == 0) return;

        var existing = db.ReservationSlots.ToList();
        var added = 0;
        foreach (var reservation in active)
        {
            for (var i = 0; i < reservation.DurationHours; i++)
            {
                var slotStart = reservation.StartTime.AddHours(i);
                if (existing.Any(s => s.ReservationId == reservation.Id && s.StartTime == slotStart))
                    continue;
                db.ReservationSlots.Add(new ReservationSlot
                {
                    ReservationId = reservation.Id,
                    CourtId = reservation.CourtId,
                    Date = reservation.ReservationDate,
                    StartTime = slotStart
                });
                added++;
            }
        }
        if (added > 0) db.SaveChanges();
    }
}
