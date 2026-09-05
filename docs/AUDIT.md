# BadmintonHub — Final Audit (Section 41)

PASS/FAIL audit across the twelve rubric areas, with evidence for every
verdict. **This audit deliberately does not claim a mark.** It records what
was verified and what remains outstanding so the team can close the gaps.

Evidence keys: file paths are repo-relative; commits are on `develop`.

---

## 1. Architecture — PASS

| Check | Verdict | Evidence |
|---|---|---|
| ASP.NET Core **MVC** (not Razor Pages / React / other backends) | PASS | `BadmintonHub/` classic MVC layout: Controllers/ Views/ Models/ Services/ Data/; 23 controllers in [Controllers/](BadmintonHub/Controllers/) |
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
| Authorization at controller/action/resource level | PASS | `[Authorize(Roles = "Admin")]` on admin controllers, `SuperAdmin` on System Settings, `Member` on member areas; resource-level ownership checks in `ReservationService`; verified by e2e T4 |
| Role model per revised spec | PASS | `SuperAdmin / Admin / Member` (Staff removed); migration converts the seeded staff account to a second admin (`admin2@`); seeded SuperAdmin owns system settings |
| Image captcha on login/register/reset | PASS | DNTCaptcha.Core rendered on all three forms (partial `_Captcha`); server-side validation via `ValidateCaptchaAttribute`, toggleable with `Security:EnableCaptcha` so the scripted e2e can run |
| E-mail verification before sign-in | PASS | Registration creates unverified members; sign-in gated until the 24 h hashed-token link is followed; resend flow with anti-enumeration neutral responses; admin manual verify; successful password reset auto-verifies |
| Remember Me | PASS | Opt-in 30-day persistent cookie via the manual cookie scheme (session cookie otherwise) |
| **QR code carries only a safe identifier** | PASS | [QrCodeHelper.cs](BadmintonHub/Services/QrCodeHelper.cs) + `ReservationsController` passes only the public `ReservationReference`; e2e T6 asserts the member's e-mail never appears in QR content |
| **No real credentials / secrets in the repository** | PASS | All credentials are seeded demo accounts; secret scan in §12 found none |
| Anti-enumeration login | PASS | Generic "Invalid email or password." + attempt logged — [AuthService.cs](BadmintonHub/Services/AuthService.cs); unit-tested |
| Failed-login lockout + admin unlock | PASS | 3 attempts → 15-minute `LockoutEnd`; `AdminUsersController.Unlock`; unit + e2e tested |
| Password reset tokens hashed, single-use, 30 min | PASS | `PasswordResetToken.TokenHash` (raw token never stored); e2e T9 |
| Antiforgery tokens on all POSTs | PASS | `[ValidateAntiForgeryToken]` on state-changing actions; e2e drives real tokens |
| Open-redirect guard on language switcher | PASS | `Url.IsLocalUrl` in [CultureController.cs](BadmintonHub/Controllers/CultureController.cs); unit-tested |
| Open-redirect guard on wishlist return URL | PASS | `WishlistController.Add` honours only `Url.IsLocalUrl(returnUrl)` values and falls back to the wishlist index otherwise; unit + e2e tested |
| Checkout resource ownership | PASS | `CheckoutComplete` refuses reservation ids owned by another user (Forbid); checkout re-validates every line server-side in a transaction; voucher usage is incremented inside the same transaction |
| Uploaded images validated by decoded format | PASS | [ImageService.cs](BadmintonHub/Services/ImageService.cs) decodes with ImageSharp and trusts the *decoded* format (a renamed executable is rejected), 5 MB cap, re-encoded as 256×256 JPEG so raw uploads are never served; unit-tested |
| Profile-photo deletes stay inside `/uploads/profiles` | PASS | `DeleteProfilePhoto` refuses null/foreign/traversal paths; unit-tested |
| Admin-account guard rails | PASS | Nobody can deactivate their own account; the last active SuperAdmin cannot be deactivated (system stays administrable); unit + e2e tested |

## 5. Core modules (badminton facility reservation) — PASS

| Module | Verdict | Evidence |
|---|---|---|
| M1 Facility / Courts / Availability | PASS | Commits `29cbace`; admin + public pages, photo upload, slot grid, bulk generation |
| M2 Reservation / Scheduling | PASS | Commit `ddd4a92`; booking with server-side double-booking protection, My Reservations calendar, cancel/refund, QR confirm, status updater |
| M3 Member / Security / Payment | PASS | Commit `fe39018`; registration, lockout, reset, payments, PDF receipt, notifications |
| M4 Admin / Reports | PASS | Commit `f1457c6`; dashboard, reservation admin, reports + CSV, user admin |
| Revised spec (P7) | PASS | SuperAdmin/Admin/Member roles, captcha, e-mail verification, Remember Me, system settings, demo mailbox — see §4 and §6 for evidence |
| Admin & Member Maintenance (P2) | PASS | Profile photos (ImageSharp pipeline, member self-service + admin upload); admin edit of member profiles with e-mail uniqueness; member activity details; SuperAdmin-only admin-account CRUD with guard rails — unit + e2e tested |
| Category / Facility Maintenance & Catalog (P3) | PASS | `Category` + `FacilityPhoto` entities (migration `P3_CategoryAndFacilityPhotos`); 11 seeded categories; 6 facilities with per-facility hours and photo manager (800×450, cover); category + facility delete guards; public catalog with filters, Top-5 ranking and low-availability alert; `CatalogServiceTests` / `AdminCategoriesControllerTests` / `AdminFacilityControllerTests` + e2e T13 |
| Booking Cart + Checkout + Wishlist + Vouchers (P4) | PASS | `CartItem` (unique user/court/date/start), `WishlistItem`, `Voucher` + reservation discount fields (migration `P4_CartWishlistVoucher`); DB-backed cart with duration update, batch remove/clear and owner scoping; checkout creates pending reservations + payments in one transaction with server-side re-validation and rollback; discount split proportionally (last line absorbs rounding remainder); **all-or-nothing batch payment**; single-use redemption limits; wishlist entry point on unavailable courts with local-return-url guard; admin voucher CRUD with duplicate-code rejection; `CartServiceTests` / `CheckoutServiceTests` / `VoucherServiceTests` / `WishlistServiceTests` + controller tests + e2e T14–T17 |

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
| Image captcha (login/register/reset) | Screenshots `05`, `06` show the rendered captcha; `Security:EnableCaptcha` toggles the server check |
| SuperAdmin system settings + announcement banner | `AdminSettingsController`; screenshot `24`; site name/announcement rendered from `SystemSettings` in the layout |
| Demo mail inbox (no SMTP) | `AdminEmailsController` + `DemoEmailSender`; screenshot `23`; verification/reset links viewable by admins |
| Profile photos + avatars | ImageSharp upload pipeline with 256×256 crop; avatars in the navbar dropdown and user tables; screenshots `27`, `28`; e2e T12 |
| SuperAdmin admin-account CRUD | `AdminAccountsController` (create/edit/reset password/activate/deactivate); screenshots `25`, `26`; e2e T12 |
| Facility category maintenance | `AdminCategoriesController` (CRUD, name-unique, delete guard); screenshots `30`, `31`; e2e T13 |
| Multi-facility + photo manager | `AdminFacilityController` (6 seeded facilities, per-facility hours, 800×450 photo gallery); screenshots `32`, `33`; e2e T13 |
| Public facility catalog | `CatalogController` + `CatalogService` (chips, search, Top-5 badge, 19:00 low-availability alert, localized details); screenshots `04`, `29`; e2e T13 |
| Booking cart | `CartController` + `CartService` — add to cart, duration update, batch remove, clear, running subtotal; screenshots `34`; e2e T14 |
| Checkout with voucher + batch payment | `CheckoutService` — transactional checkout, voucher discount on the payment page, pay all selected reservations at once; screenshot `35`; e2e T15 |
| Wishlist for unavailable courts | `WishlistController` — "Currently unavailable — Add to Wishlist" on court details, wishlist grid, per-user removal; screenshot `36`; e2e T17 |
| Voucher administration | `AdminVouchersController` — CRUD with code normalisation, duplicate rejection, expiry/limit/percentage-fixed options, usage counters; screenshots `37`, `38`; e2e T16 |

## 7. Report — PARTIAL

| Check | Verdict | Note |
|---|---|---|
| Team report 4.1–4.4 | PARTIAL | [docs/TEAM_REPORT.md](docs/TEAM_REPORT.md) complete **except team member names** — "Student A–D" placeholders must be replaced before submission |
| Entity diagram | PASS | [docs/ENTITY_DIAGRAM.md](docs/ENTITY_DIAGRAM.md) — 11 entities, 8 enums, generated from actual model classes |

## 8. Individual contributions — PASS (evidence structure)

| Check | Verdict | Evidence |
|---|---|---|
| Per-module ownership evidence | PASS | Feature branches at each module completion: `feature/member1-court` (`29cbace`), `feature/member2-reservation` (`ddd4a92`), `feature/member3-member` (`fe39018`), `feature/member4-admin` (`f1457c6`), `feature/p4-booking-cart-wishlist-voucher` |

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
| Multiple copies on one machine | PASS | Catalog name derived from the data-file path (SHA-256 prefix, commit `2d8a796`); verified working copy + clean clone running side by side with distinct registrations (`BadmintonHub_d8927285` / `BadmintonHub_54ba4478`) |

## 11. Testing — PASS

| Check | Verdict | Evidence |
|---|---|---|
| Test project runs in **Visual Studio Test Explorer** | PASS | `BadmintonHub.Tests` (xUnit) in the solution; 193 tests |
| Unit coverage of business rules | PASS | [docs/TESTING.md](docs/TESTING.md) §2 — passwords, 3-strike lockout, e-mail verification, booking/double-booking/payment/refund, admin transitions, culture switcher, QR, PDF, calendar, photo pipeline, admin-account guard rails, catalog service (filters/top-5/low-availability), category + facility maintenance CRUD with delete guards, cart service (overlap/owner scoping), checkout (transaction, proportional discount split, batch payment, re-validation rollback), voucher validation + limits, wishlist (open-redirect guard, owner scoping) |
| Latest run | PASS | `Passed! Failed: 0, Passed: 193` (2026-09-06) |
| End-to-end evidence | PASS | `tests/e2e.sh` (T1–T17) — HTTP-level checks incl. role matrix, register → verify flow, lockout, antiforgery, receipts, admin-account CRUD, photo upload, catalog filters + Top-5 badge + low-availability alert, category/facility CRUD + delete guards, cart arithmetic + batch remove, checkout with WELCOME10 + batch payment, voucher admin CRUD + single-use limits, wishlist round trip |

## 12. Documentation — PASS

README (setup, F5, demo accounts, PIC, limitations), TESTING.md, ENTITY_DIAGRAM.md, TEAM_REPORT.md, AUDIT.md, screenshots (38 captures with re-runnable script).

---

## Remaining issues (honest list — fix before submission)

1. ~~**[Action needed] Team member names** — replace "Student A–D" in
   `README.md` and `docs/TEAM_REPORT.md` with real names.~~ Done 2026-09-05:
   real names (LIM LI ZHE / YAP SJ / WONG YONG FENG / LEE XH) now in both
   documents, mapped to the built modules.
2. **DataAnnotation validation messages are English in all cultures** — the
   per-view resource files localize views; model-level messages (e.g.
   "Password must be at least 8 characters long.") stay English. Acceptable
   if the rubric only requires UI localization, but do not claim otherwise.
3. **No SMTP e-mail by default** — password-reset and verification e-mails are
   captured in the in-app demo mailbox (admin → Demo Mail) when no SMTP server
   is configured; set `Email:Smtp:Host` (MailKit) to send real mail. No SMTP
   credentials are committed. Assignment permits demo behaviour; do not
   present the demo mailbox as real e-mail delivery.
4. **Payments are simulated** — payment recording flows (no payment gateway).
5. **QR verify page is a demo counter-verification screen** — it looks up the
   public reference and shows status; it is not a cryptographic gatekeeper.
6. **Demo data only** — the seeded facility/rates/reservations are fictional
   demo content.
7. `BadmintonHub/wwwroot/images/` is intentionally not committed — the seeder
   regenerates the placeholder SVGs at first run.
