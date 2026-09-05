namespace BadmintonHub.Models;

/// <summary>
/// System roles (revised spec). Authorization is enforced per-role at controller/action level:
/// SuperAdmin manages admin accounts and system settings, Admin manages members, categories,
/// facilities, reservations and payments, Member browses and books.
/// Numeric values keep the original Admin/Member storage values; SuperAdmin is new in the P1 migration
/// (existing Staff rows are converted to Admin).
/// </summary>
public enum Role
{
    SuperAdmin = 3,
    Admin = 0,
    Member = 2
}

public enum UserStatus
{
    Active,
    Blocked,
    Deactivated
}

public enum CourtType
{
    Standard,
    VIP,
    Premium
}

public enum CourtStatus
{
    Available,
    Unavailable,
    Maintenance
}

/// <summary>Status of a single court time slot on a given date.</summary>
public enum AvailabilityStatus
{
    Open,
    Maintenance,
    Blocked
}

public enum ReservationStatus
{
    Pending,
    Confirmed,
    Completed,
    Cancelled,
    Rejected
}

public enum PaymentMethod
{
    Cash,
    OnlineTransfer,
    Card
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Refunded
}

public enum NotificationType
{
    Reservation,
    Payment,
    System,
    Reminder
}

public enum FacilityStatus
{
    Open,
    Closed,
    Maintenance
}

public enum CategoryStatus
{
    Active,
    Inactive
}
