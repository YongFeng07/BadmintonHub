using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

public class CourtIndexViewModel
{
    public string? Search { get; set; }

    public CourtType? Type { get; set; }

    public string? Sort { get; set; }

    public List<Court> Courts { get; set; } = new();
}
