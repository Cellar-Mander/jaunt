using System.Globalization;
using Jaunt.Core.Browser;
using Jaunt.Core.Display;

namespace Jaunt.Core.Tests;

public class DayGroupsTests
{
    // Saturday 14 Nov 20:00 UTC, which is 15:00 in a UTC-5 zone
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo MinusFive = TimeZoneInfo.CreateCustomTimeZone("Test-5", TimeSpan.FromHours(-5), "Test-5", "Test-5");
    private static readonly TimeZoneInfo PlusTen = TimeZoneInfo.CreateCustomTimeZone("Test+10", TimeSpan.FromHours(10), "Test+10", "Test+10");

    private static BrowserEntry<string> Entry(string title, double? startHours, double? endHours, bool always = false) =>
        new(title, title, title,
            new Occurrence(startHours is { } s ? Now.AddHours(s) : null, endHours is { } e ? Now.AddHours(e) : null), always);

    private static string[] Titles(DayGroup<string> group) => [.. group.Entries.Select(e => e.Item)];

    [Fact]
    public void OpenNowComesFirstThenTodayTomorrowAndDates()
    {
        var groups = DayGroups.Group(
            [Entry("Sunday night", 28, 31), Entry("Monday", 45, 48), Entry("Tonight", 3, 6), Entry("On now", -1, 2)],
            Now, MinusFive);

        Assert.Equal([DayGroupKind.OpenNow, DayGroupKind.Today, DayGroupKind.Tomorrow, DayGroupKind.Date], groups.Select(g => g.Kind));
        Assert.Equal(["On now"], Titles(groups[0]));
        Assert.Equal(["Tonight"], Titles(groups[1]));
        Assert.Equal(["Sunday night"], Titles(groups[2]));
        Assert.Equal(new DateOnly(2026, 11, 16), groups[3].Date);
    }

    // Openings are grouped again on each filter change, against a list worked out earlier, so one can end in between
    [Fact]
    public void OpeningsThatHaveEndedAreLeftOut()
    {
        var groups = DayGroups.Group([Entry("Was open until now", null, 0), Entry("Ended", -3, -0.5), Entry("Tonight", 3, 6)], Now, MinusFive);

        Assert.Equal(["Tonight"], Titles(Assert.Single(groups)));
    }

    [Fact]
    public void DaysAreTheUsersLocalDays()
    {
        // 03:00 UTC on Sunday is still Saturday evening at UTC-5, but Sunday afternoon at UTC+10
        var late = Entry("Late", 7, 10);
        Assert.Equal(DayGroupKind.Today, Assert.Single(DayGroups.Group([late], Now, MinusFive)).Kind);
        var plusTen = Assert.Single(DayGroups.Group([late], Now, PlusTen));
        Assert.Equal(DayGroupKind.Today, plusTen.Kind);
        Assert.Equal(new DateOnly(2026, 11, 15), plusTen.Date);

        // At UTC+10 it is already 06:00 Sunday, so 05:00 UTC Monday (15:00) is tomorrow
        Assert.Equal(DayGroupKind.Tomorrow, Assert.Single(DayGroups.Group([Entry("Monday", 33, 36)], Now, PlusTen)).Kind);
    }

    [Fact]
    public void OpenNowListsKnownStartsThenUnknownThen247()
    {
        var group = Assert.Single(DayGroups.Group(
            [Entry("Always B", null, null, always: true), Entry("Always A", null, null, always: true), Entry("Ad", null, null),
             Entry("Later start", -1, 2), Entry("Earlier start", -3, 1)],
            Now, MinusFive));
        Assert.Equal(DayGroupKind.OpenNow, group.Kind);
        Assert.Equal(["Earlier start", "Later start", "Ad", "Always A", "Always B"], Titles(group));
    }

    [Fact]
    public void OpenNowListsTheSoonestToCloseFirst()
    {
        // Opened earliest but closes last; opened latest but closes first
        var group = Assert.Single(DayGroups.Group(
            [Entry("Closes in 4h", -3, 4), Entry("Closes in 1h", -1, 1), Entry("Closes in 2h", -2, 2)],
            Now, MinusFive));
        Assert.Equal(["Closes in 1h", "Closes in 2h", "Closes in 4h"], Titles(group));
    }

    [Fact]
    public void OpenNowSortsByTheFullDateAndTimeAcrossMidnight()
    {
        // Now is 15:00 at UTC-5: 23:30 tonight, then 00:30 tomorrow, then 01:00 the day after,
        // which a sort by time of day alone would put in the reverse order
        var group = Assert.Single(DayGroups.Group(
            [Entry("01:00 in two days", -1, 34), Entry("00:30 tomorrow", -1, 9.5), Entry("23:30 tonight", -1, 8.5)],
            Now, MinusFive));
        Assert.Equal(["23:30 tonight", "00:30 tomorrow", "01:00 in two days"], Titles(group));
    }

    [Fact]
    public void OpenNowTakesAnOpeningWithNoEndToCloseThreeHoursAfterItStarts()
    {
        // No end, started 1h ago: sorts as closing in 2h, between 1h 59m and 2h 1m
        var group = Assert.Single(DayGroups.Group(
            [Entry("Closes in 2h 1m", -4, 2 + 1 / 60.0), Entry("No end", -1, null), Entry("Closes in 1h 59m", -4, 2 - 1 / 60.0)],
            Now, MinusFive));
        Assert.Equal(["Closes in 1h 59m", "No end", "Closes in 2h 1m"], Titles(group));
    }

    [Fact]
    public void OpenNowSortsAKnownEndWithoutAStartByItsEnd()
    {
        var group = Assert.Single(DayGroups.Group(
            [Entry("Closes in 3h", -1, 3), Entry("Until 1h", null, 1), Entry("Neither", null, null)],
            Now, MinusFive));
        Assert.Equal(["Until 1h", "Closes in 3h", "Neither"], Titles(group));
    }

    [Fact]
    public void OpenNowClosingTogetherIsInStartOrder()
    {
        // revert-check: not revert-sensitive (the old start order gives the same tie-break; this pins it under close-time sorting)
        // Both sort as closing in 2h: one by its end, the other by the assumed length
        var group = Assert.Single(DayGroups.Group([Entry("No end", -1, null), Entry("Ends", -2, 2)], Now, MinusFive));
        Assert.Equal(["Ends", "No end"], Titles(group));
    }

    [Fact]
    public void EntriesStartingTogetherAreInTitleOrderThenKeyOrder()
    {
        var a = new BrowserEntry<string>("second", "Same", "b", new Occurrence(Now.AddHours(2), null), false);
        var b = new BrowserEntry<string>("first", "Same", "a", new Occurrence(Now.AddHours(2), null), false);
        var group = Assert.Single(DayGroups.Group([Entry("zed", 2, 3), a, Entry("alpha", 2, 3), b, Entry("early", 1, 3)], Now, MinusFive));
        Assert.Equal(["early", "alpha", "first", "second", "zed"], Titles(group));
    }

    [Fact]
    public void DatedGroupsAreInDateOrder()
    {
        var groups = DayGroups.Group([Entry("c", 24 * 9, 24 * 9 + 1), Entry("a", 24 * 3, 24 * 3 + 1), Entry("b", 24 * 5, 24 * 5 + 1)], Now, MinusFive);
        Assert.Equal(["a", "b", "c"], groups.Select(g => g.Entries[0].Item));
    }

    [Fact]
    public void NothingListedIsNoGroups() => Assert.Empty(DayGroups.Group<string>([], Now, MinusFive));

    [Fact]
    public void HeadingsNameTheDayWithTheWeekdayApart()
    {
        var groups = DayGroups.Group([Entry("now", -1, 1), Entry("today", 3, 4), Entry("tomorrow", 24, 25), Entry("tuesday", 72, 73)], Now, MinusFive);
        var locale = CultureInfo.InvariantCulture;
        Assert.Equal(("Open now", ""), DayGroups.Heading(groups[0], DateFormat.DayMonth, locale));
        Assert.Equal(("Today", "Saturday"), DayGroups.Heading(groups[1], DateFormat.DayMonth, locale));
        Assert.Equal(("Tomorrow", "Sunday"), DayGroups.Heading(groups[2], DateFormat.DayMonth, locale));
        Assert.Equal(("17 Nov", "Tuesday"), DayGroups.Heading(groups[3], DateFormat.DayMonth, locale));
        Assert.Equal(("Nov 17", "Tuesday"), DayGroups.Heading(groups[3], DateFormat.MonthDay, locale));
        Assert.Equal(("2026-11-17", "Tuesday"), DayGroups.Heading(groups[3], DateFormat.Iso, locale));
    }
}
