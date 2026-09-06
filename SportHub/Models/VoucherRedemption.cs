using Microsoft.EntityFrameworkCore;

namespace SportHub.Models;

/// <summary>
/// How many times each member has redeemed a voucher (G-M2 per-user limit).
/// One row per (voucher, member); the counter is bumped inside the checkout
/// transaction together with the voucher's UsageCount so concurrent checkouts
/// cannot overshoot either limit.
/// </summary>
[Index(nameof(VoucherId), nameof(UserId), IsUnique = true)]
public class VoucherRedemption
{
    public int Id { get; set; }

    public int VoucherId { get; set; }
    public Voucher Voucher { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int Count { get; set; }

    public DateTime? LastUsedAt { get; set; }
}
