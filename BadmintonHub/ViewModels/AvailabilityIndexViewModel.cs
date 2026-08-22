using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

public class AvailabilityIndexViewModel
{
    public DateOnly Date { get; set; }

    public Facility Facility { get; set; } = null!;

    public List<Court> Courts { get; set; } = new();

    public List<SlotStatusViewModel> Slots { get; set; } = new();
}
