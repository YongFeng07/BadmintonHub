using System.ComponentModel.DataAnnotations;
using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

public class ReservationPaymentViewModel
{
    public int ReservationId { get; set; }

    [Display(Name = "Payment Method")]
    public PaymentMethod Method { get; set; } = PaymentMethod.OnlineTransfer;

    [StringLength(50)]
    [Display(Name = "Payment Reference (optional)")]
    public string? PaymentReference { get; set; }

    /// <summary>Display-only reservation summary for the payment page.</summary>
    public Reservation? Reservation { get; set; }
}
