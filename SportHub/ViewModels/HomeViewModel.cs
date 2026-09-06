using SportHub.Models;

namespace SportHub.ViewModels;

public class HomeViewModel
{
    public Facility? Facility { get; set; }

    public List<Court> FeaturedCourts { get; set; } = new();

    public int CourtCount { get; set; }

    public int MemberCount { get; set; }

    public int TodayReservations { get; set; }
}
