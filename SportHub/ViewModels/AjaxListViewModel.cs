using Microsoft.AspNetCore.Http;

namespace SportHub.ViewModels;

/// <summary>
/// G-M6 shared AJAX-list infrastructure. One sanitised request object, one pager
/// and one AJAX detector used by every searchable / sortable / paged list in the
/// app (member court catalog, MyReservations, admin courts/users/vouchers/mails).
/// The client engine is wwwroot/js/ajax-list.js; without JavaScript every control
/// still works as a plain GET link.
/// </summary>
public class AjaxListRequest
{
    public string Search { get; init; } = "";
    public string Sort { get; init; } = "";
    public bool Descending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;

    /// <summary>
    /// Reads the list parameters from the query string and clamps them: page ≥ 1,
    /// page size only 10/25/50, search trimmed. Sort keys are whitelisted by each
    /// controller, never passed to a query directly.
    /// </summary>
    public static AjaxListRequest From(IQueryCollection query, int defaultPageSize = 10)
    {
        var search = (query["search"].ToString() ?? "").Trim();
        var sort = (query["sort"].ToString() ?? "").Trim();
        var descending = query["dir"].ToString() == "desc";

        if (!int.TryParse(query["page"], out var page) || page < 1)
            page = 1;
        if (!int.TryParse(query["size"], out var size) || size is not (10 or 25 or 50))
            size = defaultPageSize;

        return new AjaxListRequest
        {
            Search = search,
            Sort = sort,
            Descending = descending,
            Page = page,
            PageSize = size
        };
    }

    /// <summary>Query string for pager/sort links; kept in sync with the address bar.</summary>
    public string ToQuery() =>
        $"?search={Uri.EscapeDataString(Search)}&sort={Uri.EscapeDataString(Sort)}&dir={(Descending ? "desc" : "asc")}&page={Page}&size={PageSize}";

    /// <summary>Where to start taking items (Skip) for the current page.</summary>
    public int Skip => (Page - 1) * PageSize;
}

public class AjaxPager
{
    public int TotalCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string Search { get; init; } = "";
    public string Sort { get; init; } = "";
    public bool Descending { get; init; }

    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
    public int From => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int To => Math.Min(Page * PageSize, TotalCount);

    /// <summary>Direction arrow for a sortable header ("↑" / "↓" / none).</summary>
    public string ArrowFor(string column) => Sort != column ? "" : Descending ? "↓" : "↑";

    public string AriaSortFor(string column) => Sort != column ? "none" : Descending ? "descending" : "ascending";

    /// <summary>
    /// Extra filter pairs (e.g. facilityId/type/status) that every pager and
    /// sort link must carry through — this fixes the old pagination bug where
    /// paging dropped the facility filter.
    /// </summary>
    public Dictionary<string, string> Extra { get; set; } = new();

    /// <summary>Href for a sortable header link (same column flips direction).</summary>
    public string SortLink(string column) => BuildQuery(page: 1, sort: column,
        descending: Sort == column && !Descending);

    public string PageLink(int page) => BuildQuery(page: page, sort: null, descending: null);

    private string BuildQuery(int? page, string? sort, bool? descending)
    {
        var pairs = new List<string>();
        if (Search.Length > 0) pairs.Add($"search={Uri.EscapeDataString(Search)}");
        var sortKey = sort ?? Sort;
        if (sortKey.Length > 0) pairs.Add($"sort={Uri.EscapeDataString(sortKey)}");
        pairs.Add($"dir={((descending ?? Descending) ? "desc" : "asc")}");
        pairs.Add($"page={page ?? Page}");
        pairs.Add($"size={PageSize}");
        foreach (var (key, value) in Extra)
        {
            if (!string.IsNullOrEmpty(value))
                pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }
        return "?" + string.Join("&", pairs);
    }

    /// <summary>
    /// Builds a pager from a request and the total matching rows. The page is
    /// clamped to the last page so a stale deep link (or a filter change while
    /// on a high page) never renders an empty page.
    /// </summary>
    public static AjaxPager For(AjaxListRequest request, int totalCount)
    {
        var totalPages = Math.Max(1, (totalCount + request.PageSize - 1) / request.PageSize);
        return new AjaxPager
        {
            TotalCount = totalCount,
            Page = Math.Min(request.Page, totalPages),
            PageSize = request.PageSize,
            Search = request.Search,
            Sort = request.Sort,
            Descending = request.Descending
        };
    }
}

public class AjaxListPage<T>
{
    public List<T> Items { get; init; } = new();
    public AjaxPager Pager { get; init; } = new();
}

public static class AjaxRequestExtensions
{
    /// <summary>True when the request wants the list region only (fetch/XHR).</summary>
    public static bool IsAjaxListRequest(this HttpRequest request) =>
        request.Headers.XRequestedWith.ToString() == "XMLHttpRequest";
}
