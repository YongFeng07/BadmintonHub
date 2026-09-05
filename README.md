# BadmintonHub — Badminton Sport Facility Reservation System

A web-based badminton court booking system built for the **AMIT2014 Web and
Mobile Systems** assignment. Members browse courts, book hourly slots, pay and
cancel with refunds; staff run the counter and administer reservations; admins
manage facilities, courts, availability, users and business reports. The whole
system is developed and run **the Visual Studio way**.

## Technology stack (assignment-mandated)

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core MVC on **.NET 10** (C#) |
| ORM / database | Entity Framework Core 10 **Code First** migrations, **SQL Server LocalDB** (file-based `App_Data/BadmintonHub.mdf`) |
| Authentication | **Manually implemented cookie authentication** (PBKDF2, no ASP.NET Core Identity — assignment requirement) |
| Validation | Data Annotations (attributes on model classes) |
| Front-end | Razor Views, HTML/CSS/JS, jQuery, Bootstrap 5, Chart.js |
| Extras | QRCoder (booking QR), QuestPDF (e-receipt) |
| Tests | xUnit test project — runs in **Visual Studio Test Explorer** |
| Languages | English / 中文 / Bahasa Melayu (en-US, zh-CN, ms-MY) |

## Prerequisites

1. **Visual Studio 2022** (17.14 or newer) or **Visual Studio 2026** with the
   *ASP.NET and web development* workload.
2. **.NET 10 SDK** (installed with VS 2026; VS 2022 17.14+ supports it).
3. **SQL Server LocalDB** — included with Visual Studio. No manual database
   setup is needed: on first run the app creates the database at
   `BadmintonHub\App_Data\BadmintonHub.mdf`, applies EF Core migrations and
   seeds the demo data automatically.

## Run it (Visual Studio)

1. Open `BadmintonHub.sln`.
2. Build: **Build → Build Solution**.
3. Press **F5** (default launch profile: HTTPS, `https://localhost:7153`).
4. Log in with one of the demo accounts below.

Command line equivalent (optional): `dotnet run --project BadmintonHub`.

## Demo accounts (seeded on first run — demo only, no real credentials)

| Role | Email | Password |
|---|---|---|
| Admin | `admin@badmintonhub.my` | `Admin@123` |
| Staff | `staff@badmintonhub.my` | `Staff@123` |
| Member | `member@badmintonhub.my` | `Member@123` |
| Member | `aiman@example.com` | `Member@123` |
| Member | `priya@example.com` | `Member@123` |
| Member | `john@example.com` | `Member@123` |
| Member | `nurul@example.com` | `Member@123` |
| Member | `david@example.com` | `Member@123` |

The seed also creates 6 courts (RM 25–50/hour), 14 days of hourly availability
(including a maintenance day and a blocked slot), and 13 reservations in
various statuses so every screen has data.

## Feature overview by module

| Module | Owner | Features |
|---|---|---|
| M1 Court / Facility / Availability | Student A *(replace with name)* | Facility settings; court CRUD with multi-photo upload; availability grid with AJAX editing and bulk generation; public browse/detail pages |
| M2 Reservation / Scheduling | Student B *(replace with name)* | AJAX slot picker with server-side double-booking protection; My Reservations with month calendar; payment confirmation; cancel with refund rule; QR confirmation (safe verify link only); automatic status updater |
| M3 Member / Auth / Payment | Student C *(replace with name)* | Registration with auto sign-in; profile and change password; failed-login lockout with admin unlock; password reset with hashed single-use 30-minute tokens; payment history; PDF e-receipt; notification bell with AJAX mark-read; role-based landing pages |
| M4 Admin / Reports | Student D *(replace with name)* | Dashboard KPIs with Chart.js; reservation administration (AJAX search/filter/sort/pagination, whitelisted status transitions, counter payment, cancellations); business reports with CSV export; user administration with lockout unlock and member activation control |
| Shared (P6) | Team | Multi-language (en-US / zh-CN / ms-MY) with cookie-based switcher; Monday-first localized calendars |

## Testing

All tests run in **Visual Studio**: open the solution, then **Test → Test
Explorer → Run All**. The `BadmintonHub.Tests` xUnit project has 54 tests
covering password policy, login lockout, booking/double-booking/payment/refund
rules, admin status transitions, the culture switcher, QR and PDF generation —
all against an isolated in-memory database (it never touches the real data
file). See [docs/TESTING.md](docs/TESTING.md) for the full plan, rubric mapping
and the supplementary end-to-end script (`tests/e2e.sh`).

## Known limitations (honest list)

- The QR check-in "verify" page verifies the booking exists and shows its
  public status; it is a demo counter-verification screen, not a signed-token
  gatekeeper.
- DataAnnotation validation messages are in English in all cultures (the
  per-view resource files localize the views themselves).
- Password reset and registration e-mails are simulated in-app (no SMTP
  server), as permitted for the assignment demo.
- Payments are simulated payment-recording flows (no real payment gateway).
- The seeded placeholder court images are generated SVGs.

## Documentation

- [docs/TESTING.md](docs/TESTING.md) — test plan, how to run, evidence
- [docs/AUDIT.md](docs/AUDIT.md) — final section-by-section PASS/FAIL audit
- [docs/](docs/) — additional artifacts (entity diagram, screenshots, report)
