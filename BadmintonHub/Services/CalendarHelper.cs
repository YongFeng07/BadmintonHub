using System.Globalization;

namespace BadmintonHub.Services;

/// <summary>
/// Calendar layout rules shared by the My Reservations view (P6).
/// Extracted from the view so they are unit-testable (BadmintonHub.Tests).
/// </summary>
public static class CalendarHelper
{
    /// <summary>Number of leading blank cells for a Monday-first month grid.</summary>
    public static int MondayFirstOffset(DateOnly monthStart) => ((int)monthStart.DayOfWeek + 6) % 7;

    /// <summary>Abbreviated day names of the culture, rotated to Monday-first order.</summary>
    public static string[] MondayFirstDayNames(CultureInfo culture)
    {
        var names = culture.DateTimeFormat.AbbreviatedDayNames; // index 0 = Sunday
        return new[] { names[1], names[2], names[3], names[4], names[5], names[6], names[0] };
    }
}
