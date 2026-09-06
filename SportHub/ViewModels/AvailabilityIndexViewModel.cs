using SportHub.Models;

namespace SportHub.ViewModels;

public class AvailabilityIndexViewModel
{
    public DateOnly Date { get; set; }

    public int FacilityId { get; set; }

    public Facility? Facility { get; set; }

    public List<Facility> Facilities { get; set; } = new();

    public List<Court> Courts { get; set; } = new();

    public List<SlotStatusViewModel> Slots { get; set; } = new();
}
