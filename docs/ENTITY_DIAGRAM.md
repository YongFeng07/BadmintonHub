# BadmintonHub — Entity Class Diagram

11 entities + 8 enums, EF Core Code First (Data Annotations). Generated from
the actual `BadmintonHub/Models/*.cs` sources, not from a tool — the diagram
below is the authoritative shape of the schema.

```mermaid
classDiagram
    class User {
        int Id
        string FullName
        string Email  «unique»
        string? Phone
        string PasswordHash
        string PasswordSalt
        Role Role
        UserStatus Status
        int FailedLoginAttempts
        DateTime? LockoutEnd
        DateTime? LastLoginAt
        DateTime CreatedAt
    }
    class Facility {
        int Id
        string Name
        string? Description
        string Address
        string? Phone
        string? Email
        TimeSpan OpeningTime
        TimeSpan ClosingTime
        string OperatingDays
        string? Rules
        FacilityStatus Status
    }
    class Court {
        int Id
        int FacilityId
        string CourtNumber  «unique per facility»
        CourtType CourtType
        CourtStatus Status
        decimal HourlyRate
        string? Description
        DateTime CreatedAt
    }
    class CourtPhoto {
        int Id
        int CourtId
        string FilePath
        string? Caption
        int DisplayOrder
        bool IsPrimary
    }
    class CourtAvailability {
        int Id
        int CourtId
        DateOnly Date
        TimeOnly StartTime  «unique (Court, Date, Start)»
        TimeOnly EndTime
        AvailabilityStatus Status
    }
    class Reservation {
        int Id
        string ReservationReference  «unique»
        int UserId
        int CourtId
        DateOnly ReservationDate
        TimeOnly StartTime
        TimeOnly EndTime
        decimal DurationHours
        decimal TotalAmount
        ReservationStatus Status
        string? Notes
        string? CancellationReason
        DateTime? CancelledAt
    }
    class Payment {
        int Id
        int ReservationId  «unique: 1:1»
        int UserId
        decimal Amount
        PaymentMethod Method
        PaymentStatus Status
        string? PaymentReference
        DateTime? PaidAt
    }
    class Notification {
        int Id
        int UserId
        string Title
        string Message
        NotificationType Type
        bool IsRead
    }
    class PasswordResetToken {
        int Id
        int UserId
        string TokenHash
        DateTime ExpiresAt
        bool IsUsed
    }
    class LoginAttempt {
        int Id
        int? UserId
        string Email
        string? IpAddress
        bool Success
        DateTime AttemptedAt
    }

    class Role {
        <<enumeration>>
        Admin
        Staff
        Member
    }
    class UserStatus {
        <<enumeration>>
        Active
        Blocked
        Deactivated
    }
    class CourtType {
        <<enumeration>>
        Standard
        VIP
        Premium
    }
    class CourtStatus {
        <<enumeration>>
        Available
        Unavailable
        Maintenance
    }
    class AvailabilityStatus {
        <<enumeration>>
        Open
        Maintenance
        Blocked
    }
    class ReservationStatus {
        <<enumeration>>
        Pending
        Confirmed
        Completed
        Cancelled
        Rejected
    }
    class PaymentStatus {
        <<enumeration>>
        Pending
        Paid
        Failed
        Refunded
    }
    class PaymentMethod {
        <<enumeration>>
        Cash
        OnlineTransfer
        Card
    }

    Facility "1" --> "*" Court
    Court "1" --> "*" CourtPhoto : cascade delete
    Court "1" --> "*" CourtAvailability : cascade delete
    Court "1" --> "*" Reservation : restrict delete
    User "1" --> "*" Reservation : restrict delete
    User "1" --> "*" Payment : restrict delete
    User "1" --> "*" Notification
    User "1" --> "*" PasswordResetToken
    User "0..1" --> "*" LoginAttempt
    Reservation "1" --> "0..1" Payment
```

## Design decisions

| Decision | Why |
|---|---|
| Single `User` table for all roles (`Role` enum) | Manual cookie auth without Identity; one profile record per person |
| Passwords as base64 PBKDF2 hash + salt columns | Assignment rule: never store plain-text passwords |
| `Payment` 1:1 with `Reservation` | Each booking has exactly one payment record; statuses stay consistent (paid ⇔ confirmed, reject ⇒ failed, cancel of paid ⇒ refunded) |
| `ReservationReference` `BH-{year}-{seq:000000}` | Human-friendly, **public-safe**: the QR code carries only this reference |
| Cascade vs restrict deletes | Photos/availability die with their court; courts and users with reservation history are protected (financial/audit trail) |
| `PasswordResetToken.TokenHash` single-use, 30-min expiry | Reset links never store the raw token |
| Unique index `(CourtId, Date, StartTime)` on availability | One slot row per court/date/hour |
