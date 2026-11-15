using System.Globalization;
using Jaunt.Core.Browser;
using Jaunt.Core.Display;

namespace Jaunt.Core.Tests;

public class StatusTextTests
{
    // 20:00 UTC Saturday is 15:00 at UTC-5
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo MinusFive = TimeZoneInfo.CreateCustomTimeZone("Test-5", TimeSpan.FromHours(-5), "Test-5", "Test-5");
    private static readonly CultureInfo Locale = CultureInfo.InvariantCulture;

    private static string Row(Occurrence when, bool always = false, TimeFormat time = TimeFormat.TwentyFourHour) =>
        StatusText.Row(when, always, Now, MinusFive, time, DateFormat.DayMonth, Locale);

    private static string Detail(Occurrence when, bool always = false) =>
        StatusText.Detail(when, always, Now, MinusFive, TimeFormat.TwentyFourHour, DateFormat.DayMonth, Locale);

    [Fact]
    public void OpenNowShowsTheLocalClosingTime() => Assert.Equal("Open now · until 18:00", Row(new Occurrence(Now.AddHours(-1), Now.AddHours(3))));

    [Fact]
    public void OpenNowWithoutAnEndSaysOnlyThat() => Assert.Equal("Open now", Row(new Occurrence(null, null)));

    [Fact]
    public void OpenNowClosingAfterADayGivesTheDate() =>
        Assert.Equal("Open now · until Mon 16 Nov 18:00", Row(new Occurrence(Now.AddHours(-1), Now.AddHours(51))));

    [Fact]
    public void OpenNowClosingADayAheadGivesTheDate() =>
        Assert.Equal("Open now · until Sun 15 Nov 15:00", Row(new Occurrence(Now.AddHours(-1), Now.AddDays(1))));

    // The list is worked out once a second, so an "open until" opening can end before it is next worked out
    [Fact]
    public void AnOpeningUnderWayThatHasEndedSaysClosed()
    {
        var ended = new Occurrence(null, Now.AddSeconds(-1));

        Assert.Equal("Closed", Row(ended));
        Assert.Equal("Closed", Detail(ended));
    }

    [Fact]
    public void AnEndedOpeningWithAStartSaysClosed()
    {
        var ended = new Occurrence(Now.AddHours(-3), Now);

        Assert.Equal("Closed", Row(ended));
        Assert.Equal("Closed", Detail(ended));
    }

    [Fact]
    public void AlwaysOpenSays247() => Assert.Equal("Open now · 24h", Row(new Occurrence(null, null), always: true));

    [Fact]
    public void UpcomingRowsShowLocalTimesOnly()
    {
        Assert.Equal("17:00–20:00", Row(new Occurrence(Now.AddHours(2), Now.AddHours(5))));
        Assert.Equal("5:00 PM–8:00 PM", Row(new Occurrence(Now.AddHours(2), Now.AddHours(5)), time: TimeFormat.TwelveHour));
        Assert.Equal("17:00", Row(new Occurrence(Now.AddHours(2), null)));
    }

    [Fact]
    public void TheDetailGivesTheDateOfAnUpcomingOpening()
    {
        Assert.Equal("Sun 15 Nov 17:00–20:00", Detail(new Occurrence(Now.AddHours(26), Now.AddHours(29))));
        Assert.Equal("Sun 15 Nov 17:00", Detail(new Occurrence(Now.AddHours(26), null)));
        Assert.Equal("Open now · until 18:00", Detail(new Occurrence(Now.AddHours(-1), Now.AddHours(3))));
        Assert.Equal("Open now · 24h", Detail(new Occurrence(null, null), always: true));
    }
}
