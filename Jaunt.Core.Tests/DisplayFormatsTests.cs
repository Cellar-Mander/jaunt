using System.Globalization;
using Jaunt.Core.Display;

namespace Jaunt.Core.Tests;

public class DisplayFormatsTests
{
    private static readonly DateTime Evening = new(2026, 11, 14, 20, 5, 0);
    private static readonly DateTime Morning = new(2026, 11, 14, 9, 5, 0);
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    [Theory]
    [InlineData(TimeFormat.TwelveHour, "8:05 PM", "9:05 AM")]
    [InlineData(TimeFormat.TwentyFourHour, "20:05", "09:05")]
    public void TimesFollowTheChosenClock(TimeFormat format, string evening, string morning)
    {
        // The locale doesn't matter once a clock is chosen
        Assert.Equal(evening, DisplayFormats.FormatTime(Evening, format, Uk));
        Assert.Equal(morning, DisplayFormats.FormatTime(Morning, format, Us));
    }

    [Theory]
    [InlineData("en-US", "8:05 PM")]
    [InlineData("en-GB", "20:05")]
    [InlineData("de-DE", "20:05")]
    public void SystemTimeFollowsTheLocale(string locale, string expected)
    {
        Assert.Equal(expected, DisplayFormats.FormatTime(Evening, TimeFormat.System, CultureInfo.GetCultureInfo(locale)));
    }

    [Theory]
    [InlineData(DateFormat.DayMonth, "Sat 14 Nov")]
    [InlineData(DateFormat.MonthDay, "Sat Nov 14")]
    [InlineData(DateFormat.Iso, "2026-11-14")]
    public void DatesFollowTheChosenFormat(DateFormat format, string expected)
    {
        Assert.Equal(expected, DisplayFormats.FormatDate(Evening, format, Us));
    }

    [Theory]
    [InlineData("en-US", "Sat Nov 14")]
    [InlineData("en-GB", "Sat 14 Nov")]
    [InlineData("de-DE", "Sat 14 Nov")]
    [InlineData("sv-SE", "2026-11-14")]
    [InlineData("ja-JP", "2026-11-14")]
    public void SystemDateFollowsTheLocale(string locale, string expected)
    {
        Assert.Equal(expected, DisplayFormats.FormatDate(Evening, DateFormat.System, CultureInfo.GetCultureInfo(locale)));
    }

    [Fact]
    public void SingleDigitDaysAreNotPadded()
    {
        var date = new DateTime(2026, 11, 2);

        Assert.Equal("Mon 2 Nov", DisplayFormats.FormatDate(date, DateFormat.DayMonth, Us));
        Assert.Equal("Mon Nov 2", DisplayFormats.FormatDate(date, DateFormat.MonthDay, Us));
    }
}
