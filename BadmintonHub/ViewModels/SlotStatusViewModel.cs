namespace BadmintonHub.ViewModels;

/// <summary>
/// One time-slot status for the availability grid.
/// Status values: open | booked | maintenance | blocked | closed | unavailable | past
/// </summary>
public class SlotStatusViewModel
{
    public int CourtId { get; set; }

    public string CourtNumber { get; set; } = string.Empty;

    public string CourtType { get; set; } = string.Empty;

    public decimal HourlyRate { get; set; }

    public string StartTime { get; set; } = string.Empty;

    public string EndTime { get; set; } = string.Empty;

    public string Status { get; set; } = "open";

    public string Label { get; set; } = "Open";
}
