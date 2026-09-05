# BadmintonHub — Team Report (AMIT2014)

> Names marked "Student A–D" are placeholders: **replace them with the real
> team member names** before submission. Nothing in this report claims work
> that was not done; see `docs/AUDIT.md` for the pass/fail audit.

---

## 4.1 Module ownership (PIC)

| Module | Scope | Person-in-charge |
|---|---|---|
| M1 — Facility, Courts & Availability | Facility settings; court CRUD with multi-photo upload; availability management (AJAX slot grid, bulk generation); public browse/detail pages with live availability | **Student A** *(replace)* |
| M2 — Reservation & Scheduling | Booking flow (AJAX slot picker, server-side double-booking protection); My Reservations (month calendar, cancel with refund rule); payment confirmation; QR confirmation code; automatic status updater | **Student B** *(replace)* |
| M3 — Member, Security & Payment | Registration with auto sign-in; profile and change password; failed-login lockout + admin unlock; password reset (hashed single-use 30-min tokens); payment history + PDF e-receipt; notification bell; role-based landing | **Student C** *(replace)* |
| M4 — Reservation Admin, User Admin & Reports | Dashboard KPIs + Chart.js charts; reservation administration (AJAX search/filter/sort/pagination, whitelisted status transitions, counter payment, cancellations); business reports (utilisation, popular courts, peak hours, CSV export); user administration (lockout unlock, member activation) | **Student D** *(replace)* |
| P6 — Multi-language & calendar (shared) | en-US / zh-CN / ms-MY switching with cookie persistence; Monday-first localized calendars | Team |

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
