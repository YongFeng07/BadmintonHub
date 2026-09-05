namespace BadmintonHub.ViewModels;

/// <summary>Model for the simulated ToyyibPay fallback page.</summary>
public class ToyyibPaySimulatedViewModel
{
    public string BillCode { get; set; } = "";

    public decimal Amount { get; set; }

    public List<Models.Reservation> Reservations { get; set; } = new();
}
