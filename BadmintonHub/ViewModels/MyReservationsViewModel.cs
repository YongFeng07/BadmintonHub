using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

public class MyReservationsViewModel
{
    public List<Reservation> Upcoming { get; set; } = new();

    public List<Reservation> Completed { get; set; } = new();

    public List<Reservation> Cancelled { get; set; } = new();

    public DateOnly? FilterDate { get; set; }

    public string Tab { get; set; } = "upcoming";

    /// <summary>Calendar month currently displayed.</summary>
    public int Year { get; set; }

    public int Month { get; set; }

    /// <summary>Calendar day number → count of own reservations on that day.</summary>
    public Dictionary<int, int> ReservationsPerDay { get; set; } = new();
}
