using SportHub.Models;

namespace SportHub.ViewModels;

/// <summary>Court management list (G-M6): AJAX search/sort/paging with kept filters.</summary>
public class AdminCourtIndexViewModel
{
    public int? FacilityId { get; set; }

    public CourtType? Type { get; set; }

    public CourtStatus? Status { get; set; }

    public List<Facility> Facilities { get; set; } = new();

    public AjaxListPage<Court> Page { get; set; } = new();
}
