using BadmintonHub.Models;

namespace BadmintonHub.ViewModels;

public class AdminCourtIndexViewModel
{
    public string? Search { get; set; }

    public CourtType? Type { get; set; }

    public CourtStatus? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 8;

    public int TotalCount { get; set; }

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public List<Court> Courts { get; set; } = new();
}
