using SportHub.Models;

namespace SportHub.ViewModels;

/// <summary>Voucher management list (G-M6): AJAX search/sort/paging + batch delete.</summary>
public class AdminVoucherIndexViewModel
{
    public string? StatusFilter { get; set; }

    public AjaxListPage<Voucher> Page { get; set; } = new();
}
