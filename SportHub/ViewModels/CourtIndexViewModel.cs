using SportHub.Models;

namespace SportHub.ViewModels;

/// <summary>
/// Public court catalog (G-M6): AJAX search/sort/paging over the paged court list.
/// </summary>
public class CourtIndexViewModel
{
    public string? Search { get; set; }

    public CourtType? Type { get; set; }

    public AjaxListPage<Court> Page { get; set; } = new();
}
