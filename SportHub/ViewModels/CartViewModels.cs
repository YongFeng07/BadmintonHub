using SportHub.Models;

namespace SportHub.ViewModels;

public class CartIndexViewModel
{
    public List<CartItem> Items { get; set; } = new();

    /// <summary>Gross subtotal of all cart lines (rate × duration).</summary>
    public decimal Subtotal => Items.Sum(i => (i.Court?.HourlyRate ?? 0) * i.DurationHours);
}

/// <summary>Batch payment page for a completed cart checkout.</summary>
public class CheckoutPaymentViewModel
{
    public List<int> ReservationIds { get; set; } = new();

    public List<Reservation> Reservations { get; set; } = new();

    public PaymentMethod Method { get; set; } = PaymentMethod.OnlineTransfer;

    /// <summary>Sum of the pending payment amounts (already net of voucher discount).</summary>
    public decimal TotalDue => Reservations.Sum(r => r.Payment?.Amount ?? 0);
}
