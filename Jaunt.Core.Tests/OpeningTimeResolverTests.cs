using System.Globalization;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class OpeningTimeResolverTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    private static OpeningTime Time(OpeningDay day = OpeningDay.None, string? start = null, string? end = null,
        DayOfWeek? weekday = null, DateOnly? date = null, bool now = false, string? zone = null) =>
        new(now, false, day, weekday, date, start is null ? null : TimeOnly.Parse(start, CultureInfo.InvariantCulture),
            end is null ? null : TimeOnly.Parse(end, CultureInfo.InvariantCulture), zone);

    private static DateTimeOffset Utc(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static OpeningWindow Resolve(OpeningTime t, string seen, string? zone = null) =>
        OpeningTimeResolver.Resolve(t, Utc(seen), OpeningTimeResolver.Zone(zone ?? t.Zone, PartyFinderRegion.NorthAmerica, null));

    [Theory]
    [InlineData("ST", "UTC")]
    [InlineData("st", "UTC")]
    [InlineData("UTC", "UTC")]
    [InlineData("GMT", "UTC")]
    [InlineData("CEST", "Europe/Berlin")]
    [InlineData("CT", "America/Chicago")]
    [InlineData("CDT", "America/Chicago")]
    [InlineData("MT", "America/Denver")]
    [InlineData("MDT", "America/Denver")]
    [InlineData("PST", "America/Los_Angeles")]
    [InlineData("PDT", "America/Los_Angeles")]
    [InlineData("AET", "Australia/Sydney")]
    [InlineData("AEST", "Australia/Sydney")]
    [InlineData("ACST", "Australia/Adelaide")]
    [InlineData("NZST", "Pacific/Auckland")]
    [InlineData("EST", "America/New_York")]
    [InlineData("EDT", "America/New_York")]
    [InlineData("ET", "America/New_York")]
    [InlineData("CST", "America/Chicago")]
    [InlineData("MST", "America/Denver")]
    [InlineData("PT", "America/Los_Angeles")]
    [InlineData("AEDT", "Australia/Sydney")]
    [InlineData("ACDT", "Australia/Adelaide")]
    [InlineData("AWST", "Australia/Perth")]
    [InlineData("NZDT", "Pacific/Auckland")]
    [InlineData("JST", "Asia/Tokyo")]
    [InlineData("BST", "Europe/London")]
    [InlineData("CET", "Europe/Berlin")]
    public void WrittenZonesNameAPlace(string written, string id) =>
        Assert.Equal(id, OpeningTimeResolver.Zone(written, PartyFinderRegion.Japan, null).Id);

    [Fact]
    public void JstNamesTokyo() => Assert.Equal("Asia/Tokyo", OpeningTimeResolver.Zone("JST", PartyFinderRegion.NorthAmerica, null).Id);

    [Theory]
    [InlineData("UTC")]
    [InlineData("Europe/London")]
    [InlineData("Europe/Berlin")]
    [InlineData("America/New_York")]
    [InlineData("America/Chicago")]
    [InlineData("America/Denver")]
    [InlineData("America/Los_Angeles")]
    [InlineData("Australia/Sydney")]
    [InlineData("Australia/Adelaide")]
    [InlineData("Australia/Perth")]
    [InlineData("Pacific/Auckland")]
    [InlineData("Asia/Tokyo")]
    public void EveryZoneHasTheSameRulesUnderItsWindowsId(string ianaId)
    {
        // Where IANA IDs can't be looked up, the Windows ID must name the same clock. .NET maps
        // Windows IDs on Linux too, so compare a year of offsets.
        var iana = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        var windows = TimeZoneInfo.FindSystemTimeZoneById(OpeningTimeResolver.WindowsId(ianaId));
        for (var day = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); day.Year == 2026; day = day.AddDays(7))
            Assert.Equal(iana.GetUtcOffset(day), windows.GetUtcOffset(day));
    }

    [Theory]
    [InlineData(PartyFinderRegion.NorthAmerica, null, "America/New_York")]
    [InlineData(PartyFinderRegion.Oceania, null, "Australia/Sydney")]
    [InlineData(PartyFinderRegion.Japan, null, "Asia/Tokyo")]
    [InlineData(PartyFinderRegion.Europe, "de", "Europe/Berlin")]
    [InlineData(PartyFinderRegion.Europe, "fr", "Europe/Berlin")]
    [InlineData(PartyFinderRegion.Europe, "en", "Europe/London")]
    [InlineData(PartyFinderRegion.Europe, null, "Europe/London")]
    [InlineData(PartyFinderRegion.Europe, "ja", "Europe/London")]
    public void UnwrittenZonesUseTheRegionDefault(PartyFinderRegion region, string? language, string id)
    {
        Assert.Equal(id, OpeningTimeResolver.Zone(null, region, language).Id);
        Assert.Equal(id, OpeningTimeResolver.Zone("LT", region, language).Id); // a zone it doesn't know
    }

    [Fact]
    public void ServerTimeIsUtc()
    {
        var w = Resolve(Time(OpeningDay.Weekday, "17:00", weekday: DayOfWeek.Friday, zone: "ST"), "2026-11-05T12:00Z");

        Assert.Equal(Utc("2026-11-06T17:00Z"), w.Start);
        Assert.Equal(new DateOnly(2026, 11, 6), w.Day);
        Assert.Null(w.End);
    }

    [Theory]
    [InlineData("2026-07-10T15:00Z", "2026-07-11T01:00Z")] // "EST" in July is New York's daylight time, UTC-4
    [InlineData("2026-11-10T15:00Z", "2026-11-11T02:00Z")] // and in November standard time, UTC-5
    public void EstFollowsNewYorksClock(string seen, string start) =>
        Assert.Equal(Utc(start), Resolve(Time(OpeningDay.Today, "21:00", zone: "EST"), seen).Start);

    [Fact]
    public void EdtInWinterIsAlsoNewYork() =>
        Assert.Equal(Utc("2026-11-11T02:00Z"), Resolve(Time(OpeningDay.Today, "21:00", zone: "EDT"), "2026-11-10T15:00Z").Start);

    [Fact]
    public void TodayIsTheDayInTheAdsZone()
    {
        // 03:00 UTC on the 6th is still the evening of the 5th in New York
        var w = Resolve(Time(OpeningDay.Today, "23:00", zone: "EST"), "2026-11-06T03:00Z");

        Assert.Equal(new DateOnly(2026, 11, 5), w.Day);
        Assert.Equal(Utc("2026-11-06T04:00Z"), w.Start);
    }

    [Fact]
    public void OvernightEndsTheNextDay()
    {
        var w = Resolve(Time(OpeningDay.Today, "21:00", "02:00", zone: "EST"), "2026-11-05T15:00Z");

        Assert.Equal(Utc("2026-11-06T02:00Z"), w.Start);
        Assert.Equal(Utc("2026-11-06T07:00Z"), w.End);
    }

    [Fact]
    public void AnEndEqualToTheStartIsTheNextDay() =>
        Assert.Equal(Utc("2026-11-06T17:00Z"), Resolve(Time(OpeningDay.Today, "17:00", "17:00", zone: "ST"), "2026-11-05T12:00Z").End);

    [Theory]
    [InlineData("2026-11-06T06:00Z", "2026-11-06T02:00Z")] // 01:00 in New York: last night's 21:00-02:00 is still on
    [InlineData("2026-11-06T07:00Z", "2026-11-07T02:00Z")] // 02:00: it has ended, so tonight's
    [InlineData("2026-11-06T01:59Z", "2026-11-06T02:00Z")] // 20:59 on the 5th: tonight's, about to start
    [InlineData("2026-11-06T02:00Z", "2026-11-06T02:00Z")] // 21:00: just started
    public void AnUndatedOvernightOpeningStillRunningIsYesterdays(string seen, string start) =>
        Assert.Equal(Utc(start), Resolve(Time(start: "21:00", end: "02:00", zone: "EST"), seen).Start);

    [Fact]
    public void AnUndatedDaytimeOpeningIsToday()
    {
        var w = Resolve(Time(start: "12:00", end: "20:00", zone: "EST"), "2026-11-06T06:00Z");

        Assert.Equal(Utc("2026-11-06T17:00Z"), w.Start);
        Assert.Equal(Utc("2026-11-07T01:00Z"), w.End);
    }

    [Fact]
    public void AnUndatedWholeDayOpeningStillRunningIsYesterdays()
    {
        // 17:00-17:00 ST seen at 10:00: the one that started yesterday is still on
        var w = Resolve(Time(start: "17:00", end: "17:00", zone: "ST"), "2026-11-05T10:00Z");

        Assert.Equal(Utc("2026-11-04T17:00Z"), w.Start);
        Assert.Equal(Utc("2026-11-05T17:00Z"), w.End);
    }

    [Fact]
    public void AnUndatedStartIsTodayEvenOnceItHasPassed() =>
        Assert.Equal(Utc("2026-11-05T18:00Z"), Resolve(Time(start: "18:00", zone: "ST"), "2026-11-05T23:00Z").Start);

    [Theory]
    [InlineData(DayOfWeek.Thursday, "2026-11-05")] // today counts
    [InlineData(DayOfWeek.Friday, "2026-11-06")]
    [InlineData(DayOfWeek.Wednesday, "2026-11-11")]
    public void AWeekdayIsTheNextOne(DayOfWeek weekday, string day) =>
        Assert.Equal(DateOnly.Parse(day, CultureInfo.InvariantCulture),
            Resolve(Time(OpeningDay.Weekday, "20:00", weekday: weekday, zone: "ST"), "2026-11-05T12:00Z").Day);

    [Fact]
    public void TomorrowIsTheDayAfter() =>
        Assert.Equal(Utc("2026-11-06T18:00Z"), Resolve(Time(OpeningDay.Tomorrow, "18:00", zone: "ST"), "2026-11-05T12:00Z").Start);

    [Fact]
    public void ADateIsThatDate() =>
        Assert.Equal(Utc("2026-11-16T08:00Z"),
            Resolve(Time(OpeningDay.Date, "19:00", date: new DateOnly(2026, 11, 16), zone: "AEDT"), "2026-11-07T00:00Z").Start);

    [Theory]
    [InlineData("2026-11-06T02:00Z", "2026-11-06T04:00Z")] // 21:00 in New York, until 23:00 tonight
    [InlineData("2026-11-06T04:30Z", "2026-11-07T04:00Z")] // 23:30: past it, so tomorrow's
    [InlineData("2026-11-06T04:00Z", "2026-11-07T04:00Z")] // 23:00 exactly: closed, so tomorrow's
    public void AClosingTimeAloneIsTheNextOne(string seen, string end)
    {
        var w = Resolve(Time(end: "23:00", now: true, zone: "EST"), seen);

        Assert.True(w.Now);
        Assert.Null(w.Start);
        Assert.Null(w.Day);
        Assert.Equal(Utc(end), w.End);
    }

    [Theory]
    [InlineData("02:00", "2026-11-06T07:00Z")] // "tonight until 2am" is after midnight
    [InlineData("05:59", "2026-11-06T10:59Z")]
    [InlineData("06:00", "2026-11-05T11:00Z")] // from 06:00 it is the same day
    [InlineData("23:00", "2026-11-06T04:00Z")]
    public void AClosingTimeWithADayBeforeSixIsAfterMidnight(string end, string expected) =>
        Assert.Equal(Utc(expected), Resolve(Time(OpeningDay.Today, end: end, zone: "EST"), "2026-11-05T15:00Z").End);

    [Fact]
    public void ADayAloneHasNoTimes()
    {
        var w = Resolve(Time(OpeningDay.Today, zone: "EST"), "2026-11-05T15:00Z");

        Assert.Equal(new OpeningWindow(false, false, new DateOnly(2026, 11, 5), null, null), w);
    }

    [Fact]
    public void NowAndAlwaysPassThrough()
    {
        Assert.Equal(new OpeningWindow(true, false, null, null, null), Resolve(Time(now: true), "2026-11-05T15:00Z"));
        Assert.Equal(new OpeningWindow(false, true, null, null, null),
            OpeningTimeResolver.Resolve(new OpeningTime(false, true, OpeningDay.None, null, null, null, null, null), Utc("2026-11-05T15:00Z"), NewYork));
    }

    [Theory]
    [InlineData("02:30", "2026-03-08T07:30Z")] // skipped by the spring change: read as 03:30 EDT
    [InlineData("01:30", "2026-11-01T05:30Z")] // repeated in autumn: the first, 01:30 EDT
    [InlineData("03:00", "2026-11-01T08:00Z")]
    public void ClockChanges(string start, string expected)
    {
        var date = DateOnly.Parse(expected[..10], CultureInfo.InvariantCulture);
        Assert.Equal(Utc(expected), Resolve(Time(OpeningDay.Date, start, date: date, zone: "EST"), "2026-03-01T00:00Z").Start);
    }

    [Fact]
    public void SydneyDaylightTime() =>
        Assert.Equal(Utc("2026-11-07T09:00Z"),
            OpeningTimeResolver.Resolve(Time(OpeningDay.Today, "20:00"), Utc("2026-11-07T00:00Z"), Sydney).Start);

    [Fact]
    public void InShowsTheWindowInTheUsersZone()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var w = Resolve(Time(OpeningDay.Today, "21:00", "01:00", zone: "EST"), "2026-11-05T15:00Z").In(berlin);

        Assert.Equal(new DateTimeOffset(2026, 11, 6, 3, 0, 0, TimeSpan.FromHours(1)), w.Start);
        Assert.Equal(TimeSpan.FromHours(1), w.Start!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 7, 0, 0, TimeSpan.FromHours(1)), w.End);
        Assert.Equal(TimeSpan.FromHours(1), w.End!.Value.Offset);

        var nothing = new OpeningWindow(true, false, null, null, null).In(berlin);
        Assert.Null(nothing.Start);
        Assert.Null(nothing.End);
    }

    [Fact]
    public void EndToEnd()
    {
        // pf-0176, seen 2026-11-05 22:00 UTC (17:00 in New York)
        var text = PartyFinderText.Normalise("UNHOLY Pajama Night Open Tonight 9PM-1AM EST!!").Text;
        var time = Assert.Single(OpeningTimes.Parse(text, new DateOnly(2026, 11, 5)));
        var zone = OpeningTimeResolver.Zone(time.Zone, PartyFinderRegion.NorthAmerica, null);
        var w = OpeningTimeResolver.Resolve(time, Utc("2026-11-05T22:00Z"), zone).In(Sydney);

        Assert.Equal(new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.FromHours(11)), w.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 17, 0, 0, TimeSpan.FromHours(11)), w.End);
    }
}
