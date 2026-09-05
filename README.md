# BadmintonHub — Badminton Sport Facility Reservation System

A web-based badminton court booking system built for the **AMIT2014 Web and
Mobile Systems** assignment. Members browse courts, book hourly slots, pay and
cancel with refunds; admins run the counter and administer reservations; admins
manage facilities, courts, availability, users and business reports, while the
Super Admin owns system settings and admin provisioning. The whole system is
developed and run **the Visual Studio way**.

## Technology stack (assignment-mandated)

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core MVC on **.NET 10** (C#) |
| ORM / database | Entity Framework Core 10 **Code First** migrations, **SQL Server LocalDB** (file-based `App_Data/BadmintonHub.mdf`) |
| Authentication | **Manually implemented cookie authentication** (PBKDF2, no ASP.NET Core Identity — assignment requirement) |
| Validation | Data Annotations (attributes on model classes) |
| Front-end | Razor Views, HTML/CSS/JS, jQuery, Bootstrap 5, Chart.js |
| Extras | QRCoder (booking QR), QuestPDF (e-receipt), DNTCaptcha.Core (image captcha), MailKit (optional SMTP) |
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
| Super Admin | `superadmin@badmintonhub.my` | `SuperAdmin@123` |
| Admin | `admin@badmintonhub.my` | `Admin@123` |
| Admin | `admin2@badmintonhub.my` | `Admin@123` |
| Member | `member@badmintonhub.my` | `Member@123` |
| Member | `aiman@example.com` | `Member@123` |
| Member | `priya@example.com` | `Member@123` |
| Member | `john@example.com` | `Member@123` |
| Member | `nurul@example.com` | `Member@123` |
| Member | `david@example.com` | `Member@123` |

The seed also creates 11 facility categories (Badminton, Swimming, Gym, Squash,
Table Tennis, Futsal, …), 6 multi-sport facilities across those categories
(24 courts/lanes/tables/pitches in total, RM 10–120/hour), 14 days of hourly
availability per unit (including maintenance days and blocked slots), and 28
reservations with payments and notifications in various statuses so every
screen has data.

## Feature overview by module

| Module | Owner | Features |
|---|---|---|
| M1 Court / Facility / Availability | YAP SJ | Facility settings; court CRUD with multi-photo upload; availability grid with AJAX editing and bulk generation; public browse/detail pages |
| M2 Reservation / Scheduling | WONG YONG FENG | AJAX slot picker with server-side double-booking protection; My Reservations with month calendar; payment confirmation; cancel with refund rule; QR confirmation (safe verify link only); automatic status updater |
| M3 Member / Auth / Payment | LIM LI ZHE | Registration with email verification; profile and change password; 3-strike failed-login lockout with admin unlock; password reset with hashed single-use 30-minute tokens; payment history; PDF e-receipt; notification bell with AJAX mark-read; role-based landing pages |
| M4 Admin / Reports | LEE XH | Dashboard KPIs with Chart.js; reservation administration (AJAX search/filter/sort/pagination, whitelisted status transitions, counter payment, cancellations); business reports with CSV export; user administration with lockout unlock and member activation control |
| Category / Facility Maintenance & Catalog (P3) | YAP SJ | 11-seed facility categories (name-unique, status, unit label, icon, display order); multi-facility CRUD with per-facility opening hours and drag-drop photo manager (800×450, cover photo); courts/availability scoped per facility; public catalog with category/name filters, Top-5 popularity ranking and same-evening low-availability alert; localized facility detail pages |
| Admin & Member Maintenance (P2) | LIM LI ZHE | Profile photos with real-format validation, 256×256 crop-resize (ImageSharp); member self-service avatar upload/remove; admin edit of member profiles with email uniqueness; member activity details (stats + total paid); SuperAdmin-only admin-account CRUD (create/edit/reset password/activate/deactivate) with guard rails (no self-deactivation, last active SuperAdmin protected) |
| Shared (P6) | Team | Multi-language (en-US / zh-CN / ms-MY) with cookie-based switcher; Monday-first localized calendars |
| Revised-spec security & roles (P7) | Team | SuperAdmin / Admin / Member roles (Staff removed, demo account migrated); SuperAdmin-only system settings (site name + announcement banner); image captcha on login/register/reset (DNTCaptcha.Core, toggleable via `Security:EnableCaptcha`); email verification flow with 24h hashed tokens, resend and admin manual verify; Remember Me (30-day persistent cookie); demo mail inbox for verification/reset emails when no SMTP is configured |

## Testing

All tests run in **Visual Studio**: open the solution, then **Test → Test
Explorer → Run All**. The `BadmintonHub.Tests` xUnit project has 113 tests
covering password policy, the 3-strike login lockout, email verification
(register/verify/expiry/anti-enumeration resend), booking/double-booking/
payment/refund rules, admin status transitions, the culture switcher, QR and
PDF generation, the photo-upload pipeline (size/format validation, 256×256
resize, safe delete), the admin-account CRUD guard rails, the facility-catalog
service (category/name filters, Top-5 ranking, low-availability signal) and the
category/facility maintenance CRUD with delete guards — all against an
isolated in-memory database (it never touches the real data file). See
[docs/TESTING.md](docs/TESTING.md) for the full plan, rubric mapping and the
supplementary end-to-end script (`tests/e2e.sh`).

## Known limitations (honest list)

- The QR check-in "verify" page verifies the booking exists and shows its
  public status; it is a demo counter-verification screen, not a signed-token
  gatekeeper.
- DataAnnotation validation messages are in English in all cultures (the
  per-view resource files localize the views themselves).
- Password reset and registration e-mails are captured in the in-app demo
  mail inbox (admin → Demo Mail) when no SMTP server is configured — as
  permitted for the assignment demo; set `Email:Smtp:Host` to send real mail.
- The automated e2e script runs with `Security__EnableCaptcha=false` (the
  captcha is still shown on the UI; only the server-side check is switched
  off so curl can drive the flows — see docs/TESTING.md).
- Payments are simulated payment-recording flows (no real payment gateway).
- The seeded placeholder court images are generated SVGs.

## Documentation

- [docs/TESTING.md](docs/TESTING.md) — test plan, how to run, evidence
- [docs/AUDIT.md](docs/AUDIT.md) — final section-by-section PASS/FAIL audit
- [docs/](docs/) — additional artifacts (entity diagram, screenshots, report)
