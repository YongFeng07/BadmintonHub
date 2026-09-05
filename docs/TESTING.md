# BadmintonHub — Testing Plan & Evidence

This document is the systematic test plan for BadmintonHub. All testing is done
**the Visual Studio way**: the primary mechanism is the `BadmintonHub.Tests`
xUnit project, which runs in **Visual Studio Test Explorer** (Test → Test
Explorer → Run All). A supplementary bash end-to-end script (`tests/e2e.sh`)
exercises the running web app over HTTP and is documented in section 4.

> Honesty note: tests prove the behaviours listed below; they do not prove
> "full marks". The Section 41 audit (`docs/AUDIT.md`) records what is still
> missing.

---

## 1. How to run the tests

### In Visual Studio (primary)

1. Open `BadmintonHub.sln` in Visual Studio 2022 (17.14+) or 2026.
2. **Build → Build Solution**.
3. **Test → Test Explorer** — the `BadmintonHub.Tests` project is listed.
4. Click **Run All** (or right-click the project → **Run Tests**).
5. All 54 tests run against an in-memory database — no LocalDB, no web server
   needed, and they cannot touch the real `App_Data/BadmintonHub.mdf`.

The same suite can be run from the command line (the CLI underneath Test
Explorer is identical — VSTest):

```powershell
dotnet test BadmintonHub.Tests\BadmintonHub.Tests.csproj
```

### Supplementary: end-to-end script

```powershell
# 1. start the app
dotnet run --project BadmintonHub\BadmintonHub.csproj --no-launch-profile --urls http://localhost:5080

# 2. in a second terminal
bash tests/e2e.sh
```

The script drives a real HTTP session: login cookies, antiforgery tokens,
AJAX endpoints, PDF/QR downloads and the language switcher.

---

## 2. Unit test suite (54 tests, all passing)

Each test class uses a **fresh isolated in-memory database** (`TestDb.Create()`)
seeded with one facility (open daily 08:00–23:00), one court at RM 25/hour with
14 days of open availability, and member/staff/admin users whose passwords are
real PBKDF2 hashes.

| Test class | What it proves | Assignment rubric area |
|---|---|---|
| `PasswordHelperTests` (9) | PBKDF2 hashing round-trip, wrong/case-different passwords rejected, unique salts per hash, corrupt stored hash fails closed, password policy (≥8 chars, ≤100, letter+digit) | Security — "never store plain-text passwords"; validation |
| `AuthServiceTests` (6) | Unknown email → generic error **and** attempt logged (anti-enumeration), failed counter increments, **lockout after 5 failures** (15 min, correct password refused), deactivated account blocked, successful login resets counter and stamps `LastLoginAt` | Manual authentication (no Identity), lockout, status control |
| `ReservationServiceTests` (13) | Booking creation (Pending + Pending payment + `BH-yyyy-######` reference + notification), duration 1–4 rule, past-date rule, unknown court, maintenance court, window outside opening hours, **server-side double-booking protection** (identical + partial overlap), payment → Confirmed consistency, already-paid guard, ownership guard, cancel → refund rule, completed reservations cannot be cancelled | Core business process: reservation + payment |
| `AdminReservationsControllerTests` (9) | Not-found/invalid-status/illegal-transition errors (TempData), **whitelisted transitions only** (Pending→Confirmed, Pending→Rejected, Confirmed→Completed), rejection fails the pending payment, staff counter payment, staff cancellation with refund, index model | M4 reservation administration |
| `CultureControllerTests` (6) | All three supported cultures stored in a 1-year localization cookie, unsupported culture ignored, local return URL honoured, **external return URL ignored (open-redirect guard)** | P6 multi-language + security |
| `QrCodeHelperTests` (2) | Output is a valid PNG data URI (magic bytes verified) and content-dependent | QR confirmation code |
| `ReceiptPdfGeneratorTests` (1) | A paid booking produces a real PDF document (`%PDF` magic bytes) | M3 PDF e-receipt |
| `CalendarHelperTests` (7) | Monday-first offsets (Sat=5, Sun=6, Mon=0, Tue=1) and culture-aware day headers (en `Mon–Sun`, zh `周一–周日`, ms `Isn–Ahd`) | P6 calendar |

### Latest run evidence

```
Passed!  -  Failed: 0, Passed: 54, Skipped: 0, Total: 54
```
(2026-09-05, Debug build, `dotnet test` — the same VSTest engine Visual Studio
Test Explorer uses.)

---

## 3. What the unit tests deliberately do NOT cover

Controller authorization filters (`[Authorize(Roles=...)]`), cookie security
flags, antiforgery, the full login round-trip and the AJAX/PDF/QR endpoints are
covered by the **end-to-end script** below, because they need a real HTTP
pipeline.

---

## 4. End-to-end script (`tests/e2e.sh`)

11 test groups (T1–T11) against a running app. Highlights:

| # | Scenario | Checks |
|---|---|---|
| T1 | Public pages | Home, courts, court detail, login/register pages return 200 |
| T2 | Multi-language | `?culture=zh-CN/ms-MY` works, switcher sets a persistent cookie, invalid culture ignored |
| T3 | Authentication | Wrong password → generic error; correct login → dashboard; logout |
| T4 | Role enforcement | Anon redirected to login; MEMBER blocked from admin pages (403) |
| T5 | Booking flow | Create → Pay; double-booking rejected ("just been booked"); reference format; receipt PDF downloads |
| T6 | QR verify | QR content carries only the public reference (member email never inside) |
| T7 | Admin reports | Dashboard canvas renders, reservation table AJAX, CSV export |
| T8 | Lockout / admin unlock / deactivate | 5 failures → locked; admin unlock; deactivated user blocked; reactivation |
| T9 | Password reset | Forgot-password → token link → reset → new password works |
| T10 | Notifications | Bell counter, mark-read AJAX |
| T11 | Chinese calendar | zh-CN month/day headers render correctly |

---

## 5. Test data safety

- Unit tests use `UseInMemoryDatabase` — they **never** touch
  `App_Data/BadmintonHub.mdf`.
- `e2e.sh` creates only throwaway accounts (`e2e.<timestamp>@example.com`).
- The seeded database contains **demo accounts only** (see README); no real
  credentials exist anywhere in the repository (verified by secret scan in
  `docs/AUDIT.md`).
