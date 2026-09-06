using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>
/// The heart of the system: a member's booking of one court for a date and time window.
/// A court cannot be double-booked: overlapping windows for the same court are rejected.
/// </summary>
[Index(nameof(ReservationReference), IsUnique = true)]
[Index(nameof(CourtId), nameof(ReservationDate), nameof(StartTime), nameof(EndTime))]
public class Reservation
{
    public int Id { get; set; }

    /// <summary>Human-friendly reference, e.g. "SH-2026-000123".</summary>
    [Required, StringLength(20)]
    [Display(Name = "Reservation Reference")]
    public string ReservationReference { get; set; } = string.Empty;

    public int UserId { get; set; }

    public User? User { get; set; }

    public int CourtId { get; set; }

    public Court? Court { get; set; }

    [Display(Name = "Date")]
    public DateOnly ReservationDate { get; set; }

    [Display(Name = "Start Time")]
    public TimeOnly StartTime { get; set; }

    [Display(Name = "End Time")]
    public TimeOnly EndTime { get; set; }

    [Column(TypeName = "decimal(4,1)")]
    [Display(Name = "Duration (hours)")]
    public decimal DurationHours { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Display(Name = "Total Amount (RM)")]
    public decimal TotalAmount { get; set; }

    /// <summary>Voucher discount deducted at checkout; 0 when no voucher was applied.</summary>
    [Column(TypeName = "decimal(10,2)")]
    [Display(Name = "Discount (RM)")]
    public decimal DiscountAmount { get; set; }

    /// <summary>Code of the voucher applied at checkout (audit trail; survives voucher deletion).</summary>
    [StringLength(20)]
    public string? VoucherCode { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    [StringLength(500)]
    public string? Notes { get; set; }

    [StringLength(200)]
    [Display(Name = "Cancellation Reason")]
    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// G-M5: when the 24-hour "your booking starts soon" reminder was sent.
    /// Null = not yet sent; the reminder worker sends exactly one per booking.
    /// </summary>
    public DateTime? ReminderSentAt { get; set; }

    public Payment? Payment { get; set; }
}
