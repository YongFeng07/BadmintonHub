using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>
/// Discount voucher applied at checkout (revised spec). Percentage or fixed-amount,
/// with an expiry date and an optional redemption cap. Usage is counted inside the
/// checkout transaction so a voucher can never overshoot its limit.
/// </summary>
[Index(nameof(Code), IsUnique = true)]
public class Voucher
{
    public int Id { get; set; }

    [Required, StringLength(20)]
    [RegularExpression(@"^[A-Z0-9-]+$", ErrorMessage = "Use uppercase letters, digits and dashes only.")]
    [Display(Name = "Voucher Code")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Description { get; set; } = string.Empty;

    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;

    /// <summary>Percent 1–100 for Percentage, or ringgit amount for FixedAmount.</summary>
    [Column(TypeName = "decimal(10,2)")]
    [Range(0.01, 100)]
    [Display(Name = "Discount")]
    public decimal DiscountValue { get; set; }

    /// <summary>First day the code can be redeemed; null = valid immediately.</summary>
    [Display(Name = "Valid From")]
    public DateOnly? StartDate { get; set; }

    /// <summary>Valid through this date (inclusive).</summary>
    [Display(Name = "Expiry Date")]
    public DateOnly ExpiryDate { get; set; }

    /// <summary>Cart subtotal must be at least this to redeem; 0 = no minimum.</summary>
    [Column(TypeName = "decimal(10,2)")]
    [Range(0, 100000)]
    [Display(Name = "Minimum Spend (RM)")]
    public decimal MinSpend { get; set; }

    /// <summary>Upper bound on the discount amount (percentage vouchers); null = uncapped.</summary>
    [Column(TypeName = "decimal(10,2)")]
    [Range(0, 100000)]
    [Display(Name = "Maximum Discount (RM)")]
    public decimal? MaxDiscount { get; set; }

    /// <summary>Maximum total redemptions; null = unlimited.</summary>
    [Display(Name = "Usage Limit")]
    public int? UsageLimit { get; set; }

    /// <summary>Maximum redemptions per member; null = unlimited. Counted in VoucherRedemption.</summary>
    [Display(Name = "Per-User Limit")]
    public int? PerUserLimit { get; set; }

    [Display(Name = "Times Used")]
    public int UsageCount { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Per-member redemption counters (upserted inside the checkout transaction).</summary>
    public ICollection<VoucherRedemption> Redemptions { get; set; } = new List<VoucherRedemption>();
}
