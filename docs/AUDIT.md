# BadmintonHub — Final Audit (Section 41)

PASS/FAIL audit across the twelve rubric areas, with evidence for every
verdict. **This audit deliberately does not claim a mark.** It records what
was verified and what remains outstanding so the team can close the gaps.

Evidence keys: file paths are repo-relative; commits are on `develop`.

---

## 1. Architecture — PASS

| Check | Verdict | Evidence |
|---|---|---|
| ASP.NET Core **MVC** (not Razor Pages / React / other backends) | PASS | `BadmintonHub/` classic MVC layout: Controllers/ Views/ Models/ Services/ Data/; 15 controllers in [Controllers/](BadmintonHub/Controllers/) |
| .NET 10 | PASS | `BadmintonHub/BadmintonHub.csproj` targets `net10.0` |
| Layered structure | PASS | Business rules isolated in [Services/](BadmintonHub/Services/) (`ReservationService`, `AuthService`, `CourtService`…), EF in [Data/](BadmintonHub/Data/), presentation in Views + ViewModels |
| Classic solution, Visual Studio workflow | PASS | [BadmintonHub.sln](BadmintonHub.sln) builds in VS 2022 17.14+/2026; F5 verified (see Build) |

## 2. Presentation — PASS

| Check | Verdict | Evidence |
|---|---|---|
| Razor Views + Bootstrap + custom theme | PASS | Shared layout `Views/Shared/_Layout.cshtml`, custom `wwwroot/css/site.css`, Bootstrap 5 via libman |
| Client-side interactivity (jQuery/JS) | PASS | AJAX slot picker, admin reservations table, notification mark-read — `wwwroot/js/*.js` |
| Responsive design | PASS | Bootstrap grid throughout; verified at 1440×900 captures and mobile breakpoints in layout |

## 3. Data — PASS

| Check | Verdict | Evidence |
|---|---|---|
| EF Core **Code First** | PASS | Entities in `Models/`, context in `Data/ApplicationDbContext.cs` |
| **Migrations** (not EnsureCreated) | PASS | `Migrations/` folder present; `Database.Migrate()` at startup (`Program.cs`) |
| **Data Annotations** validation | PASS | `[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`, `[EmailAddress]`, `[Phone]` on entities |
| SQL Server (LocalDB, file-based) | PASS | `Server=(localdb)\MSSQLLocalDB;AttachDbFilename={DbFile}` — DB file pinned to `App_Data/BadmintonHub.mdf` (commit `0a70a7c`) |

## 4. Security — PASS (with noted demo limitations)

| Check | Verdict | Evidence |
|---|---|---|
| **No ASP.NET Core Identity** — manually implemented cookie auth | PASS | `AuthService`, `AccountController`; `AddAuthentication(CookieAuthenticationDefaults…)` in `Program.cs` |
| Cookie hardening | PASS | HttpOnly, SameSite=Lax, 8 h sliding expiry |
| **No plain-text passwords** | PASS | PBKDF2-SHA256, 100k iterations, random 16-byte salt, constant-time compare — [PasswordHelper.cs](BadmintonHub/Services/PasswordHelper.cs); covered by unit tests |
| Authorization at controller/action/resource level | PASS | `[Authorize(Roles = "Admin,Staff")]` etc. on admin controllers; resource-level ownership checks in `ReservationService`; verified by e2e T4 |
| **QR code carries only a safe identifier** | PASS | [QrCodeHelper.cs](BadmintonHub/Services/QrCodeHelper.cs) + `ReservationsController` passes only the public `ReservationReference`; e2e T6 asserts the member's e-mail never appears in QR content |
| **No real credentials / secrets in the repository** | PASS | All credentials are seeded demo accounts; secret scan in §12 found none |
| Anti-enumeration login | PASS | Generic "Invalid email or password." + attempt logged — [AuthService.cs](BadmintonHub/Services/AuthService.cs); unit-tested |
| Failed-login lockout + admin unlock | PASS | 5 attempts → 15-minute `LockoutEnd`; `AdminUsersController.Unlock`; unit + e2e tested |
| Password reset tokens hashed, single-use, 30 min | PASS | `PasswordResetToken.TokenHash` (raw token never stored); e2e T9 |
| Antiforgery tokens on all POSTs | PASS | `[ValidateAntiForgeryToken]` on state-changing actions; e2e drives real tokens |
| Open-redirect guard on language switcher | PASS | `Url.IsLocalUrl` in [CultureController.cs](BadmintonHub/Controllers/CultureController.cs); unit-tested |

## 5. Core modules (badminton facility reservation) — PASS

| Module | Verdict | Evidence |
|---|---|---|
| M1 Facility / Courts / Availability | PASS | Commits `29cbace`; admin + public pages, photo upload, slot grid, bulk generation |
| M2 Reservation / Scheduling | PASS | Commit `ddd4a92`; booking with server-side double-booking protection, My Reservations calendar, cancel/refund, QR confirm, status updater |
| M3 Member / Security / Payment | PASS | Commit `fe39018`; registration, lockout, reset, payments, PDF receipt, notifications |
| M4 Admin / Reports | PASS | Commit `f1457c6`; dashboard, reservation admin, reports + CSV, user admin |

## 6. Additional features — PASS

| Feature | Evidence |
|---|---|
| Multi-language (en-US / zh-CN / ms-MY) | Request-localization cookie + switcher; e2e T2/T11; screenshots `07`, `08`, `12` |
| Monday-first localized calendar | `CalendarHelper`; unit-tested; screenshot `11`, `12` |
| QR confirmation code | Screenshot `09`; e2e T6 |
| PDF e-receipt | `ReceiptPdfGenerator` (QuestPDF, invariant English); e2e T5; unit-tested |
| Chart.js dashboard + reports | Screenshots `16`, `17` (chart regions pixel-verified rendered) |
| CSV export | e2e T7 |
| Notification centre | e2e T10 |

## 7. Report — PARTIAL

| Check | Verdict | Note |
|---|---|---|
| Team report 4.1–4.4 | PARTIAL | [docs/TEAM_REPORT.md](docs/TEAM_REPORT.md) complete **except team member names** — "Student A–D" placeholders must be replaced before submission |
| Entity diagram | PASS | [docs/ENTITY_DIAGRAM.md](docs/ENTITY_DIAGRAM.md) — 11 entities, 8 enums, generated from actual model classes |

## 8. Individual contributions — PASS (evidence structure)

| Check | Verdict | Evidence |
|---|---|---|
| Per-module ownership evidence | PASS | Feature branches at each module completion: `feature/member1-court` (`29cbace`), `feature/member2-reservation` (`ddd4a92`), `feature/member3-member` (`fe39018`), `feature/member4-admin` (`f1457c6`) |

## 9. Build — PASS

| Check | Verdict | Evidence |
|---|---|---|
| Visual Studio build | PASS | Verified: VS MSBuild solution build succeeds; F5 launch (https profile) serves the app; same verification passed via `dotnet build` (0 errors) |
| Clean-solution verification | PASS | Fresh clone build + run verified in §12 |

## 10. Database — PASS

| Check | Verdict | Evidence |
|---|---|---|
| Auto-migrate + seed at startup | PASS | `Program.cs`: `Database.Migrate()` + `DbSeeder.Seed()`; first F5 creates `App_Data/BadmintonHub.mdf` |
| VS F5 reliability | PASS | LocalDB file pinned to `App_Data` (commit `0a70a7c`) — fixes the "Cannot create file because it already exists" crash |

## 11. Testing — PASS

| Check | Verdict | Evidence |
|---|---|---|
| Test project runs in **Visual Studio Test Explorer** | PASS | `BadmintonHub.Tests` (xUnit) in the solution; 54 tests |
| Unit coverage of business rules | PASS | [docs/TESTING.md](docs/TESTING.md) §2 — passwords, lockout, booking/double-booking/payment/refund, admin transitions, culture switcher, QR, PDF, calendar |
| Latest run | PASS | `Passed! Failed: 0, Passed: 54` (2026-09-05) |
| End-to-end evidence | PASS | `tests/e2e.sh` (T1–T11) — HTTP-level checks incl. role enforcement, antiforgery, receipts |

## 12. Documentation — PASS

README (setup, F5, demo accounts, PIC, limitations), TESTING.md, ENTITY_DIAGRAM.md, TEAM_REPORT.md, AUDIT.md, screenshots (22 captures with re-runnable script).

---

## Remaining issues (honest list — fix before submission)

1. **[Action needed] Team member names** — replace "Student A–D" in
   `README.md` and `docs/TEAM_REPORT.md` with real names.
2. **DataAnnotation validation messages are English in all cultures** — the
   per-view resource files localize views; model-level messages (e.g.
   "Password must be at least 8 characters long.") stay English. Acceptable
   if the rubric only requires UI localization, but do not claim otherwise.
3. **No SMTP e-mail** — password-reset "e-mail" and registration mails are
   simulated in-app (token shown on a demo page). Assignment permits demo
   behaviour; do not present it as real e-mail delivery.
4. **Payments are simulated** — payment recording flows (no payment gateway).
5. **QR verify page is a demo counter-verification screen** — it looks up the
   public reference and shows status; it is not a cryptographic gatekeeper.
6. **Demo data only** — the seeded facility/rates/reservations are fictional
   demo content.
7. `BadmintonHub/wwwroot/images/` is intentionally not committed — the seeder
   regenerates the placeholder SVGs at first run.
