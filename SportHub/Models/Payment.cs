using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>Payment record for one reservation (one-to-one).</summary>
[Index(nameof(ReservationId), IsUnique = true)]
public class Payment
{
    public int Id { get; set; }

    public int ReservationId { get; set; }

    public Reservation? Reservation { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    [Required]
    [Range(0.01, 100000.00, ErrorMessage = "Amount must be greater than 0.")]
    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.OnlineTransfer;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    [StringLength(50)]
    [Display(Name = "Payment Reference")]
    public string? PaymentReference { get; set; }

    /// <summary>
    /// ToyyibPay bill code (real gateway) or SIM-{guid} for the simulated
    /// fallback; shared by every payment of one batch bill. Null = not a gateway bill.
    /// </summary>
    [StringLength(50)]
    public string? GatewayBillCode { get; set; }

    /// <summary>
    /// ToyyibPay status_id echoed from the gateway return ("1" success, "2"
    /// pending, "3" failed) while the bill is open; null = not a gateway bill.
    /// </summary>
    [StringLength(10)]
    public string? GatewayStatus { get; set; }

    [Display(Name = "Paid At")]
    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
