# BadmintonHub — Team Report (AMIT2014)

> Names marked "Student A–D" are placeholders: **replace them with the real
> team member names** before submission. Nothing in this report claims work
> that was not done; see `docs/AUDIT.md` for the pass/fail audit.

---

## 4.1 Module ownership (PIC) — mapped against the official feature list

Legend: ✅ implemented · ⚠️ partially covered · ❌ not implemented (honest gap)

### MEMBER 1 — Court & Facility Management (Student A, *replace name*)

| Official feature | Status | Where |
|---|---|---|
| Add / Edit / Delete / View / Search / Filter Court | ✅ | `AdminCourtsController` (Index search+type+status filters, Create, Edit, Details, Delete); public `CourtsController` (search, type filter, sort) |
| Court Number / Type / Status / Description / Pricing | ✅ | `Court` model: CourtNumber (2-digit rule), CourtType (Standard/VIP/Premium), CourtStatus, Description, HourlyRate (RM 0.01–1000) |
| Facility Information / Opening Hours / Operating Days / Status / Rules | ✅ | `AdminFacilityController.Edit` + `Facility` model |
| Facility Maintenance Status | ✅ | `FacilityStatus` { Open, Closed, Maintenance } |
| Create / Edit Time Slots | ✅ | `AdminAvailabilityController`: hourly Generate (bulk, 1–14 days) + AJAX UpdateSlot + SetRange |
| Unavailable Time Slots / Maintenance Period | ✅ | `AvailabilityStatus` { Open, Blocked, Maintenance } per slot; demo: Court 06 maintenance day, Court 03 blocked hour |
| Date-based & Time-based Availability | ✅ | Slots are (Date, StartTime, EndTime) rows, unique index per court/date/hour |
| Interactive Availability Calendar | ⚠️ | Date-navigable slot grid (admin) + month calendar on My Reservations — not a month-style availability calendar |
| AJAX Real-time Availability Checking | ✅ | `CourtsController.CheckAvailability` + `ReservationsController.GetSlots` |
| Multiple Court Photos | ✅ | `CourtPhoto` gallery, multi-upload, primary photo, display order |
| Advanced Court Search & Filtering | ✅ | Public search + type + sort; admin search + type + status + paging |

### MEMBER 2 — Reservation Management (Student B, *replace name*)

| Official feature | Status | Where |
|---|---|---|
| Create / View / Cancel Reservation | ✅ | `ReservationsController` Create (AJAX slot picker), Details, Cancel |
| Update Reservation | ❌ | No rescheduling (date/time change) flow — status changes only, via admin. Honest gap. |
| Reservation Status / Details / History | ✅ | Status lifecycle Pending→Confirmed→Completed/Cancelled/Rejected; My Reservations tabs: Upcoming / Completed / Cancelled |
| Reservation Confirmation | ✅ | Booking only Confirmed on payment; public verify page; QR code |
| Reservation Conflict Checking | ✅ | Server-side double-booking protection (`CourtService.HasOverlappingReservationAsync`) — identical AND partial overlaps refused |
| My Reservations / Upcoming / Completed / Cancelled | ✅ | Tabbed My Reservations page |
| Select Date / Time / Court | ✅ | Booking wizard |
| Check Availability | ✅ | AJAX `GetSlots` returns real slot status |
| Prevent Double Booking | ✅ | See conflict checking; unit-tested |
| Calculate Reservation Duration | ✅ | 1–4 hours, TotalAmount = rate × duration |
| Interactive Reservation Calendar | ✅ | Monday-first month calendar, localized |
| AJAX Reservation Availability | ✅ | Slot picker + calendar reload |
| QR Code Reservation Confirmation | ✅ | QR carries only the public reference |
| Automatic Reservation Status Update | ✅ | `ReservationStatusUpdaterService` background service |

### MEMBER 3 — Member, Authentication & Payment (Student C, *replace name*)

| Official feature | Status | Where |
|---|---|---|
| Member Registration / Profile / Edit / View | ✅ | Register with auto sign-in; Profile view + edit; change password |
| Member Status | ✅ | Active/Blocked/Deactivated; deactivated users cannot log in |
| Booking History | ✅ | My Reservations + Payment history |
| Account Management | ✅ | Profile, change password, reset password |
| Registration / Login / Logout | ✅ | `AccountController` |
| Cookie-based Authentication (NO ASP.NET Core Identity) | ✅ | Manual cookie auth + PBKDF2 password hashing — assignment requirement |
| Role-based Authorization / Access Control | ✅ | `[Authorize(Roles=...)]` on controllers/actions + resource-level ownership checks |
| Session Management | ✅ | HttpOnly, SameSite=Lax, 8 h sliding-expiry auth cookie |
| Roles: ADMIN / MEMBER / STAFF | ✅ | `Role` enum |
| Payment Record / Method / Amount / Status / Date / Reference | ✅ | `Payment` entity, 1:1 with reservation |
| Reservation Payment | ✅ | Pending payment created with booking; paid ⇔ confirmed |
| Failed Login Attempt Blocking | ✅ | 5 attempts → 15-minute lockout; admin unlock |
| Password Reset | ✅ | Hashed single-use 30-min tokens; anti-enumeration messaging |
| PDF E-Receipt | ✅ | QuestPDF receipt, owner/staff only, paid bookings only |
| Member Notification | ✅ | Bell + AJAX mark-read centre |

### MEMBER 4 — Admin & Reservation Operations (Student D, *replace name*)

| Official feature | Status | Where |
|---|---|---|
| View All / Search / Filter Reservations | ✅ | AJAX search (reference/name/email), status/court/date filters, sort, paging |
| Update Reservation Status | ✅ | Whitelisted transitions only (Pending→Confirmed/Rejected, Confirmed→Completed) |
| Approve / Reject Reservation | ✅ | Pending → Confirmed / Rejected; rejection fails the pending payment |
| Cancel Reservation | ✅ | Staff cancellation; paid booking → refund |
| Reservation Management / History | ✅ | All statuses visible incl. cancelled with reasons |
| View / Search / Filter Members | ✅ | Role + status filters, search |
| Edit Member | ❌ | No admin edit-member form — only Unlock and Activate/Deactivate. Honest gap. |
| Activate / Deactivate Member | ✅ | `SetStatus` |
| Reservation Summary / Daily / Weekly / Monthly | ⚠️ | Dashboard KPIs (today) + 7-day trend + 30-day window; reports use a date range (default 14 days) with daily breakdown — no explicit weekly/monthly buckets |
| Revenue Summary | ✅ | Dashboard + reports + CSV export |
| Court Utilization / Popular Courts / Peak Reservation Times | ✅ | Reports charts |
| Interactive Dashboard Charts | ✅ | Chart.js (revenue, per-court, peak hours) |
| AJAX Search / Filter / Sort / Paging | ✅ | Reservation admin table |
| Reservation Analytics | ✅ | Reports module |
| Multi-language Support | ✅ | Shared P6 (en-US / zh-CN / ms-MY) |

Each module's work is preserved as a feature branch at its completion commit:
`feature/member1-court`, `feature/member2-reservation`,
`feature/member3-member`, `feature/member4-admin`.

## 4.2 Entity Relationship Diagram

Full diagram: [docs/ENTITY_DIAGRAM.md](ENTITY_DIAGRAM.md) (11 entities, 8
enums, generated from the actual model classes).

Key relationships:

- **Facility → Courts → Availability/Photos**: one facility owns 6 courts;
  each court has hourly availability slots (unique per court/date/hour) and a
  photo gallery.
- **User → Reservations → Payment**: a member owns reservations; each
  reservation has exactly one payment record. Deletes are *restricted* on both
  sides so booking/payment history can never be destroyed.
- **Security entities**: `PasswordResetToken` (hashed, single-use, 30-minute
  expiry) and `LoginAttempt` (audit log incl. IP, drives the lockout) hang off
  `User`; `Notification` powers the bell.

## 4.3 Monetisation model

**Implemented in the system (fact):**

1. **Hourly court rental** — `Court.HourlyRate × Reservation.DurationHours` is
   computed automatically into `TotalAmount`; booking cannot proceed unpaid
   (payment record created with the booking, booking only confirmed on
   payment).
2. **Refund policy** — a paid booking that is cancelled becomes a refund
   (`PaymentStatus.Refunded`), enforced by the shared service so member,
   staff and admin flows behave identically.
3. **Tiered court pricing** — court *type* drives the rate (Standard / VIP /
   Premium), which is the mechanism for premium pricing.

**Assumptions (ASSUMPTION — seeded demo values, editable by admin):**

- Standard RM 25/hour, VIP RM 35/hour, Premium RM 50/hour. These are demo
  rates chosen for the seed data; the facility admin can change any rate.
- 24-hour advance cancellation for full refund — stated in the facility rules
  page.

**Planned but NOT implemented (PLANNING ASSUMPTION — do not claim as built):**

- Membership subscriptions / monthly packages (the current system has roles
  and accounts but no recurring fees).
- Equipment rental and pro-shop sales (mentioned in facility rules as
  available at the counter, no sales module).
- Peak-hour dynamic pricing (peak-hour analytics exist in M4 reports, but
  pricing is fixed per court type).

## 4.4 Screenshots per student

All screenshots were captured from the running app with the headless Edge
script `docs/screenshots/capture.sh` (re-runnable: start the app, run the
script). File names below map to the screenshot files in
[docs/screenshots/](screenshots/).

| Student | Module | Screenshots to include in the individual report |
|---|---|---|
| Student A | M1 | `02-courts.png`, `03-court-detail.png`, `04-facility.png`, `19-admin-facility.png`, `20-admin-courts.png`, `21-admin-court-create.png`, `22-admin-availability.png` |
| Student B | M2 | `10-member-booking.png`, `11-member-myreservations.png`, `12-member-calendar-zh.png`, `09-verify.png` |
| Student C | M3 | `05-login.png`, `06-register.png`, `13-member-payments.png`, `14-member-profile.png` |
| Student D | M4 | `15-staff-reservations-admin.png`, `16-admin-dashboard.png`, `17-admin-reports.png`, `18-admin-users.png` |
| Shared P6 | Team | `07-home-zh.png`, `08-home-ms.png`, `12-member-calendar-zh.png` |
