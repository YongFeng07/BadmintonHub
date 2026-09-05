using BadmintonHub.Services;
using System.Globalization;

namespace BadmintonHub.Tests;

/// <summary>
/// P6 calendar: Monday-first week layout and culture-aware day headers.
/// </summary>
public class CalendarHelperTests
{
    [Theory]
    [InlineData(2026, 8, 1, 5)]  // Saturday -> offset 5 (calendar grid starts Monday)
    [InlineData(2026, 8, 2, 6)]  // Sunday   -> offset 6
    [InlineData(2026, 8, 3, 0)]  // Monday   -> offset 0
    [InlineData(2026, 9, 1, 1)]  // Tuesday  -> offset 1 (September 2026)
    public void MondayFirstOffset_PlacesWeekdaysCorrectly(int year, int month, int day, int expected)
    {
        var monthStart = new DateOnly(year, month, day);

        Assert.Equal(expected, CalendarHelper.MondayFirstOffset(monthStart));
    }

    [Fact]
    public void MondayFirstDayNames_English_StartsMondayEndsSunday()
    {
        var names = CalendarHelper.MondayFirstDayNames(new CultureInfo("en-US"));

        Assert.Equal(new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }, names);
    }

    [Fact]
    public void MondayFirstDayNames_Chinese_StartsMondayEndsSunday()
    {
        var names = CalendarHelper.MondayFirstDayNames(new CultureInfo("zh-CN"));

        Assert.Equal(7, names.Length);
        Assert.Equal("周一", names[0]);
        Assert.Equal("周日", names[6]);
    }

    [Fact]
    public void MondayFirstDayNames_Malay_StartsMondayEndsSunday()
    {
        var names = CalendarHelper.MondayFirstDayNames(new CultureInfo("ms-MY"));

        Assert.Equal(7, names.Length);
        Assert.Equal("Isn", names[0]);
        Assert.Equal("Ahd", names[6]);
    }
}
