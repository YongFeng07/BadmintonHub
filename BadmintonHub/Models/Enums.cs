namespace BadmintonHub.Models;

/// <summary>System roles. Authorization is enforced per-role at controller/action level.</summary>
public enum Role
{
    Admin,
    Staff,
    Member
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
