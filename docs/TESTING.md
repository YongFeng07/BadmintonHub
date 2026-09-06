# SportHub — Testing Plan & Evidence

This document is the systematic test plan for SportHub. All testing is done
**the Visual Studio way**: the primary mechanism is the `SportHub.Tests`
xUnit project, which runs in **Visual Studio Test Explorer** (Test → Test
Explorer → Run All). A supplementary bash end-to-end script (`tests/e2e.sh`)
exercises the running web app over HTTP and is documented in section 4.

> Honesty note: tests prove the behaviours listed below; they do not prove
> "full marks". The Section 41 audit (`docs/AUDIT.md`) records what is still
> missing.

---

## 1. How to run the tests

### In Visual Studio (primary)

1. Open `SportHub.sln` in Visual Studio 2022 (17.14+) or 2026.
2. **Build → Build Solution**.
3. **Test → Test Explorer** — the `SportHub.Tests` project is listed.
4. Click **Run All** (or right-click the project → **Run Tests**).
5. All 113 tests run against an in-memory database — no LocalDB, no web server
   needed, and they cannot touch the real `App_Data/SportHub.mdf`.

The same suite can be run from the command line (the CLI underneath Test
Explorer is identical — VSTest):

```powershell
dotnet test SportHub.Tests\SportHub.Tests.csproj
```

### Supplementary: end-to-end script

```powershell
# 1. start the app (captcha server-side check off for scripted curl logins —
#    the captcha UI itself is exercised manually / in screenshots 05–06)
$env:Security__EnableCaptcha = "false"
dotnet run --project SportHub\SportHub.csproj --no-launch-profile --urls http://localhost:5080

# 2. in a second terminal
bash tests/e2e.sh
```

The script drives a real HTTP session: login cookies, antiforgery tokens,
AJAX endpoints, PDF/QR downloads, the register → verify-email → login flow
and the language switcher.

---

## 2. Unit test suite (193 tests, all passing)

Each test class uses a **fresh isolated in-memory database** (`TestDb.Create()`)
seeded with one facility (open daily 08:00–23:00), one court at RM 25/hour with
14 days of open availability, and member/admin/SuperAdmin users whose passwords
are real PBKDF2 hashes (all seeded users are treated as e-mail-verified).

| Test class | What it proves | Assignment rubric area |
|---|---|---|
| `PasswordHelperTests` (11) | PBKDF2 hashing round-trip, wrong/case-different passwords rejected, unique salts per hash, corrupt stored hash fails closed, password policy (≥8 chars, ≤100, letter+digit — 7 valid/invalid parameterised cases) | Security — "never store plain-text passwords"; validation |
| `AuthServiceTests` (6) | Unknown email → generic error **and** attempt logged (anti-enumeration), failed counter increments, **lockout after 3 failures** (15 min, correct password refused), deactivated account blocked, **unverified e-mail gated with a "verify your email" response**, successful login resets counter and stamps `LastLoginAt` | Manual authentication (no Identity), lockout, email-verification gate, status control |
| `AccountServiceTests` (8) | Registration creates an **unverified** member whose verification token is stored only as a SHA-256 hash (~24 h expiry); duplicate e-mail rejected; verification succeeds once (idempotent) and clears the token; wrong/expired tokens fail without verifying; verification resend answers neutrally for unknown/already-verified e-mails (anti-enumeration); **a successful password reset proves the mailbox and auto-verifies the account** | Revised spec: email verification, hashed single-use tokens |
| `DemoEmailSenderTests` (1) | With no SMTP configured, outgoing mail is captured in the in-app demo mailbox (and reported as not delivered) | Demo mail fallback |
| `ReservationServiceTests` (13) | Booking creation (Pending + Pending payment + `SH-yyyy-######` reference + notification), duration 1–4 rule, past-date rule, unknown court, maintenance court, window outside opening hours, **server-side double-booking protection** (identical + partial overlap), payment → Confirmed consistency, already-paid guard, ownership guard, cancel → refund rule, completed reservations cannot be cancelled | Core business process: reservation + payment |
| `AdminReservationsControllerTests` (9) | Not-found/invalid-status/illegal-transition errors (TempData), **whitelisted transitions only** (Pending→Confirmed, Pending→Rejected, Confirmed→Completed), rejection fails the pending payment, admin counter payment, admin cancellation with refund, index model | M4 reservation administration |
| `CultureControllerTests` (6) | All three supported cultures stored in a 1-year localization cookie, unsupported culture ignored, local return URL honoured, **external return URL ignored (open-redirect guard)** | P6 multi-language + security |
| `QrCodeHelperTests` (2) | Output is a valid PNG data URI (magic bytes verified) and content-dependent | QR confirmation code |
| `ReceiptPdfGeneratorTests` (1) | A paid booking produces a real PDF document (`%PDF` magic bytes) | M3 PDF e-receipt |
| `CalendarHelperTests` (7) | Monday-first offsets (Sat=5, Sun=6, Mon=0, Tue=1) and culture-aware day headers (en `Mon–Sun`, zh `周一–周日`, ms `Isn–Ahd`) | P6 calendar |
| `ImageServiceTests` (11) | Null/empty file and >5 MB rejected, unsupported extension rejected, **a renamed non-image is rejected by real-format validation** (decoded format, not the file name), a valid PNG is cropped to **256×256 and re-saved as JPEG** under `/uploads/profiles`, a valid facility PNG is cropped to **800×450 JPEG** under `/uploads/facilities`, delete removes only managed files (null/foreign/traversal paths are no-ops) | P2 profile-photo + P3 facility-photo pipeline |
| `CatalogServiceTests` (7) | Catalog lists open facilities with primary photo, unit count and min rate; category filter and case-insensitive name search; **Top-5 popularity ranking over confirmed+completed bookings of the last 30 days** (older bookings excluded); **the 19:00 low-availability signal subtracts active bookings** and triggers at ≤2 remaining; primary photo preferred over others | P3 facility catalog |
| `AdminCategoriesControllerTests` (8) | Categories ordered by DisplayOrder; create/edit persist; **duplicate names rejected** on create and edit; **delete blocked while the category owns facilities**, allowed when empty, unknown id → NotFound | P3 category maintenance |
| `AdminFacilityControllerTests` (7) | Facilities listed with category; create persists category + weekday-ordered operating days; **invalid category rejected**; edit updates settings; **delete blocked while the facility owns courts**; delete removes the facility, its photo rows and deletes the photo files via the image service; first uploaded photo becomes the cover | P3 facility maintenance |
| `AdminAccountsControllerTests` (11) | Create produces an **active, e-mail-verified** admin with a real PBKDF2 hash; duplicate e-mail and weak password rejected on create/edit; password reset sets a working password and clears lockout; weak reset rejected with hash untouched; **guard rails: self-deactivation refused and the last active SuperAdmin cannot be deactivated**; other-admin deactivation works; invalid status error | P2 admin-account maintenance |
| `AdminUsersControllerTests` (5) | Admin edit of a member profile persists (email uniqueness enforced, duplicate rejected); member Details aggregates reservation counts and **total paid (Paid payments only)**; only member accounts can be deactivated here; photo upload stores the returned path | P2 member maintenance |
| `CartServiceTests` (13) | Add persists with the court's current hourly rate; **duplicate line and overlap against the user's own cart lines rejected** (friendly error, cart untouched); slot outside the open-availability window rejected; invalid duration rejected (parameterised 1–4 rule); update persists or rolls back on overlap; remove / batch-remove / clear are **scoped to the owner**; count is per-user | P4 booking cart |
| `VoucherServiceTests` (15) | Code normalised to uppercase on create; duplicate code rejected (create and edit); edit changes only editable fields; **validate refuses blank / unknown / inactive / expired / limit-reached / zero-subtotal**; percentage discount computed; **fixed amount capped so the net total stays positive**; list ordered newest-first | P4 vouchers |
| `CheckoutServiceTests` (14) | Preview computes subtotal and voucher discount; checkout **creates pending reservations + pending payments in one transaction and clears the cart**; discount **split proportionally with the last line absorbing the rounding remainder**; invalid voucher fails and keeps the cart; **server-side re-validation against newly-created bookings (rolls back)**; tampered overlapping cart lines rejected; another user's item ids refused; **voucher usage incremented once and a second use refused**; batch payment confirms all reservations at once, one-already-paid fails the whole batch | P4 checkout + batch payment |
| `CartControllerTests` (14) | Add → cart redirect with flash, invalid model → booking page, overlap error keeps the cart; update/remove/batch-remove/clear flow with owner-scoped removal; checkout redirects to the payment step and mentions the savings flash; **CheckoutComplete forbids another user's reservation ids**; POST payment marks Confirmed/Paid and lands on the Paid page | P4 cart UI flow |
| `WishlistServiceTests` (8) | Idempotent adds with friendly duplicate error; unknown court fails; ownership-scoped remove; count per user; items include court + facility for the card grid | P4 wishlist |
| `WishlistControllerTests` (6) | Add honours a **local** return URL and **ignores an external one (open-redirect guard)**; duplicate/unknown-court flashes; remove scoped to the owner | P4 wishlist + security |
| `AdminVouchersControllerTests` (9) | Index newest-first; create normalises and redirects; duplicate code shown as a model error; edit updates editable fields; **edit colliding with another voucher's code rejected**; delete removes with flash; unknown ids → NotFound | P4 voucher administration |

### Latest run evidence

```
Passed!  -  Failed: 0, Passed: 193, Skipped: 0, Total: 193
```
(2026-09-06, Debug build, `dotnet test` — the same VSTest engine Visual Studio
Test Explorer uses.)

---

## 3. What the unit tests deliberately do NOT cover

Controller authorization filters (`[Authorize(Roles=...)]`), cookie security
flags, antiforgery, the full login round-trip and the AJAX/PDF/QR endpoints are
covered by the **end-to-end script** below, because they need a real HTTP
pipeline.

Phase E (ToyyibPay + booking/revenue reports) added **no new unit tests** —
the team decided to skip that milestone. In particular:

- `ToyyibPayService` has no automated coverage. Its simulated fallback was
  exercised end-to-end by a live smoke round trip (cart → bill → simulated
  gateway → return → payments marked paid → confirmation e-mail), but the
  **real ToyyibPay API path requires real credentials and has never been
  exercised against the live gateway**.
- `ChartAggregations` (monthly buckets, category split, cancellation rate)
  is pure aggregation logic verified by live page checks with seeded data;
  it has no unit tests.

---

## 4. End-to-end script (`tests/e2e.sh`)

17 test groups (T1–T17) against a running app. Highlights:

| # | Scenario | Checks |
|---|---|---|
| T1 | Public pages | Home, courts, court detail, login/register pages return 200; the facility catalog renders; the legacy `/Facility` URL redirects to it |
| T2 | Multi-language | `?culture=zh-CN/ms-MY` works, switcher sets a persistent cookie, invalid culture ignored |
| T3 | Authentication | Wrong password → generic error; member, admin, second admin and superadmin logins all redirect; admin sees dashboard |
| T4 | Role enforcement | Anon redirected to login; MEMBER blocked from admin pages; ADMIN blocked from SuperAdmin-only System Settings; SUPERADMIN allowed |
| T5 | Booking flow | Create → Pay; double-booking rejected ("just been booked"); reference format; receipt PDF downloads |
| T6 | QR verify | QR content carries only the public reference (member email never inside) |
| T7 | Admin reports | Dashboard canvas renders, reservation table AJAX, CSV export |
| T8 | Security cycle | Register → **email-verification page shown, demo link verifies**; 3 failures → locked; admin unlock; deactivated user blocked; reactivation |
| T9 | Password reset | Forgot-password → token link → reset → new password works |
| T10 | Notifications | Bell counter, mark-read AJAX |
| T11 | Chinese calendar | zh-CN month/day headers render correctly |
| T12 | Admin accounts + member edit + photo (P2) | Only SuperAdmin opens Admin Accounts; creating an admin provisions an account that **logs in immediately**; new admin is blocked from Admin Accounts; **SuperAdmin self-deactivation refused**; admin renames a member (visible in search); member uploads a photo (profile shows and serves it), then removes it |
| T13 | Category/facility maintenance + catalog (P3) | Catalog lists seeded facilities with the **Top-5 badge**; category-chip filter and name search narrow results; facility detail page links its units and photos; booking two table-tennis slots for tonight makes the **low-availability alert appear**; admin creates/edits a category and a facility in it (court 99 included); facility photo upload is listed and served; **delete guards: category blocked while it owns a facility, facility blocked while it owns courts**; cleanup deletes court → facility → category in dependency order |
| T14 | Booking cart (P4) | Two lines on one court; **subtotal equals the page's own line prices**; duration update re-prices (**line = 2×, subtotal = 3×**); batch-remove both selected lines → "2 item(s) removed" + empty-cart state |
| T15 | Checkout + batch payment (P4) | Checkout with `WELCOME10` redirects to the payment step; **total due = subtotal − round(10%)**; batch payment (two reservations, one transaction) lands on the paid page with both `SH-` references Confirmed |
| T16 | Voucher administration (P4) | Role guards on admin vouchers; seeded list; admin creates a **limit-1 voucher**; duplicate code refused; member redeems once (redirect), **second redemption refused with "reached its redemption limit" and the cart line kept**; usage column shows **1 / 1** + limit-reached badge; edit and delete; deleted voucher really gone from the index (edit link asserted, not the flash text) |
| T17 | Wishlist (P4) | Admin creates an **Unavailable** court; its detail page shows "Currently unavailable" + **Add to Wishlist**; add returns to the detail page (local return URL honoured — the suite fixes Git Bash path-mangling of `returnUrl=` so `Url.IsLocalUrl` sees the real value); detail page flips to the in-wishlist state; wishlist page lists the new court plus the **seeded demo rows**; removing the user's item leaves the seeded rows intact; cleanup deletes the court |

**Phase E manual verification** (no T18/T19 — see §3): a live smoke round trip
covered the simulated ToyyibPay flow — checkout with the ToyyibPay method
creates a `SIM-` bill, the simulated gateway page renders the amount and
reservation list, "Pay Now" returns to `ToyyibPayReturn` and the batch is
marked Paid (bill code stored as the payment reference, `GatewayStatus=1`,
confirmation e-mail captured in the demo mailbox), "Cancel" returns the user
to the payment step with a clear message. The member **Insights** tab and the
new admin report charts (monthly bookings, revenue by month, bookings by
category, cancellation rate) were verified to render with real serialized
data on a fresh seed.

---

## 5. Test data safety

- Unit tests use `UseInMemoryDatabase` — they **never** touch
  `App_Data/SportHub.mdf`.
- `e2e.sh` creates only throwaway accounts (`e2e.<timestamp>@example.com`),
  categories, facilities and courts (deleted again at the end of T13 in
  dependency order); it also books two table-tennis slots for tonight to make
  the low-availability alert deterministic. T16 creates and deletes a
  limit-1 voucher (`E2ELIMIT1`), and T17 creates and deletes the unavailable
  court used for the wishlist round trip.
- The seeded database contains **demo accounts only** (see README); no real
  credentials exist anywhere in the repository (verified by secret scan in
  `docs/AUDIT.md`).
