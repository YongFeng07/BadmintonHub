using SportHub.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace SportHub.Tests;

/// <summary>
/// G-M6 shared AJAX-list infrastructure: the request sanitizer clamps hostile
/// query-string values, the pager clamps pages to the last page and every link
/// it builds carries the search/filter/sort state through.
/// </summary>
public class AjaxListTests
{
    private static IQueryCollection Query(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, StringValues>();
        foreach (var (key, value) in pairs)
            dict[key] = value;
        return new QueryCollection(dict);
    }

    [Fact]
    public void From_ClampsPageAndWhitelistsPageSize()
    {
        var request = AjaxListRequest.From(Query(("page", "-3"), ("size", "7")));

        Assert.Equal(1, request.Page);
        Assert.Equal(10, request.PageSize); // 7 is not whitelisted → default
    }

    [Fact]
    public void From_AcceptsWhitelistedPageSizes()
    {
        Assert.Equal(25, AjaxListRequest.From(Query(("size", "25"))).PageSize);
        Assert.Equal(50, AjaxListRequest.From(Query(("size", "50"))).PageSize);
        Assert.Equal(10, AjaxListRequest.From(Query(("size", "999"))).PageSize);
    }

    [Fact]
    public void From_TrimsSearchAndParsesSortState()
    {
        var request = AjaxListRequest.From(Query(("search", "  court 01 "), ("sort", "rate"), ("dir", "desc")));

        Assert.Equal("court 01", request.Search);
        Assert.Equal("rate", request.Sort);
        Assert.True(request.Descending);
    }

    [Fact]
    public void From_IgnoresGarbageNumericValues()
    {
        var request = AjaxListRequest.From(Query(("page", "abc"), ("size", "lots")));

        Assert.Equal(1, request.Page);
        Assert.Equal(10, request.PageSize);
    }

    [Fact]
    public void For_ClampsPageToLastPage()
    {
        var request = AjaxListRequest.From(Query(("page", "9"), ("size", "10")));

        var pager = AjaxPager.For(request, totalCount: 25);

        Assert.Equal(3, pager.Page); // stale deep link never renders an empty page
        Assert.Equal(3, pager.TotalPages);
    }

    [Fact]
    public void FromTo_ReflectsCurrentPage()
    {
        var pager = AjaxPager.For(AjaxListRequest.From(Query(("page", "2"), ("size", "10"))), 25);

        Assert.Equal(11, pager.From);
        Assert.Equal(20, pager.To);
    }

    [Fact]
    public void FromTo_ZeroWhenNothingMatches()
    {
        var pager = AjaxPager.For(AjaxListRequest.From(Query()), 0);

        Assert.Equal(0, pager.From);
        Assert.Equal(0, pager.To);
    }

    [Fact]
    public void SortLink_FlipsDirectionAndResetsPage()
    {
        var pager = AjaxPager.For(AjaxListRequest.From(Query(("sort", "code"), ("page", "3"))), 30);

        var link = pager.SortLink("code");

        Assert.Contains("sort=code", link);
        Assert.Contains("dir=desc", link); // same column again flips asc → desc
        Assert.Contains("page=1", link);
    }

    [Fact]
    public void SortLink_NewColumnStartsAscending()
    {
        var pager = AjaxPager.For(AjaxListRequest.From(Query(("sort", "code"), ("dir", "desc"))), 30);

        Assert.Contains("dir=asc", pager.SortLink("discount"));
    }

    [Fact]
    public void Links_CarrySearchAndExtraFiltersThrough()
    {
        var pager = AjaxPager.For(AjaxListRequest.From(Query(("search", "summer"), ("size", "25"))), 60);
        pager.Extra["status"] = "Active";
        pager.Extra["facilityId"] = "3";

        var sortLink = pager.SortLink("code");
        var pageLink = pager.PageLink(2);

        Assert.Contains("search=summer", sortLink);
        Assert.Contains("size=25", sortLink);
        Assert.Contains("status=Active", sortLink);
        Assert.Contains("status=Active", pageLink);
        Assert.Contains("facilityId=3", pageLink);
        Assert.Contains("page=2", pageLink);
    }

    [Fact]
    public void Arrows_OnlyMarkTheActiveColumn()
    {
        var pager = AjaxPager.For(AjaxListRequest.From(Query(("sort", "rate"), ("dir", "desc"))), 10);

        Assert.Equal("↓", pager.ArrowFor("rate"));
        Assert.Equal("", pager.ArrowFor("type"));
        Assert.Equal("descending", pager.AriaSortFor("rate"));
        Assert.Equal("none", pager.AriaSortFor("type"));
    }
}
