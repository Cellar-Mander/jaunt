using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class SchedulesTests
{
    // Saturday
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static Occurrence At(double startHours, double endHours) => new(Now.AddHours(startHours), Now.AddHours(endHours));

    private static ListingSchedule Recurring(params Occurrence[] occurrences) => new(false, false, occurrences);

    private static ListingSchedule OneOff(params Occurrence[] occurrences) => new(false, true, occurrences);

    // Listed: recurring

    [Fact]
    public void ARecurringListingOpenNowShowsTheCurrentAndNextOpenings()
    {
        var past = At(-25, -22);
        var current = At(-1, 2);
        var next = At(23, 26);
        var later = At(47, 50);
        Assert.Equal([current, next], Schedules.Listed(Recurring(past, current, next, later), Now));
    }

    [Fact]
    public void ARecurringListingNotOpenShowsOnlyTheNextOpening()
    {
        var next = At(1, 4);
        Assert.Equal([next], Schedules.Listed(Recurring(At(-25, -22), next, At(25, 28)), Now));
    }

    [Fact]
    public void ARecurringOpeningEndingNowIsOver()
    {
        var next = At(24, 27);
        Assert.Equal([next], Schedules.Listed(Recurring(At(-3, 0), next), Now));
    }

    [Fact]
    public void ARecurringListingOpenNowWithNoLaterOpeningShowsOnlyTheCurrentOne()
    {
        var current = At(-1, 2);
        Assert.Equal([current], Schedules.Listed(Recurring(current), Now));
    }

    [Fact]
    public void ARecurringListingWithNothingLeftShowsNothing() => Assert.Empty(Schedules.Listed(Recurring(At(-5, -2)), Now));

    // Listed: one-off

    [Fact]
    public void OneOffsShowEveryOpeningStartingWithin30Days()
    {
        var current = At(-1, 2);
        var soon = At(48, 51);
        var lastDay = new Occurrence(Now.AddDays(30), Now.AddDays(30).AddHours(3));
        var tooFar = new Occurrence(Now.AddDays(30).AddMinutes(1), Now.AddDays(30).AddHours(3));
        Assert.Equal([current, soon, lastDay], Schedules.Listed(OneOff(At(-5, -2), current, soon, lastDay, tooFar), Now));
    }

    [Fact]
    public void AnOpenEndedOneOffIsListed()
    {
        var open = new Occurrence(null, null);
        Assert.Equal([open], Schedules.Listed(OneOff(open), Now));
    }

    [Fact]
    public void AlwaysOpenIsListedOnceAsOpen()
    {
        var listed = Assert.Single(Schedules.Listed(ListingSchedule.Always, Now));
        Assert.True(listed.IsOpen(Now));
        Assert.Null(listed.Start);
        Assert.Null(listed.End);
    }

    // Occurrence

    [Fact]
    public void AnOccurrenceIsOpenFromItsStartUntilItsEnd()
    {
        Assert.True(At(0, 1).IsOpen(Now));
        Assert.False(At(-1, 0).IsOpen(Now));
        Assert.False(At(0.01, 1).IsOpen(Now));
        Assert.True(new Occurrence(null, Now.AddHours(1)).IsOpen(Now));
        Assert.True(new Occurrence(Now.AddHours(-1), null).IsOpen(Now));
    }

    // Sources

    [Fact]
    public void APartakeEventIsOneOff()
    {
        var schedule = Schedules.ForEvent(Now, Now.AddHours(2));
        Assert.True(schedule.OneOff);
        Assert.False(schedule.AlwaysOpen);
        Assert.Equal([new Occurrence(Now, Now.AddHours(2))], schedule.Occurrences);
    }

    [Fact]
    public void AnEventWithoutTimesHasNoOpenings() => Assert.Empty(Schedules.ForEvent(Now, null).Occurrences);

    [Fact]
    public void CalendarOpeningsKeepTheirOneOffFlag()
    {
        var openings = new[] { new ListingOpening(Now, Now.AddHours(3)) };
        Assert.False(Schedules.ForOpenings(openings, oneOff: false).OneOff);
        Assert.False(Schedules.ForOpenings(openings, oneOff: false).AlwaysOpen);
        var oneOff = Schedules.ForOpenings(openings, oneOff: true);
        Assert.True(oneOff.OneOff);
        Assert.Equal([new Occurrence(Now, Now.AddHours(3))], oneOff.Occurrences);
    }

    // FFXIV Venues

    private static FfxivVenues.Opening Entry(DateTimeOffset start, DateTimeOffset end, int type = 0, int argument = 1) =>
        new(0, new FfxivVenues.Time(start.Hour, start.Minute, false), new FfxivVenues.Time(end.Hour, end.Minute, false),
            new FfxivVenues.Interval(type, argument), new FfxivVenues.Resolution(start, end));

    private static FfxivVenues.Fields Venue(params FfxivVenues.Opening?[] schedule) => new(null, null, null, schedule);

    [Fact]
    public void AVenueResolutionStillToComeIsUsedWithTheOneAWeekLater()
    {
        var start = Now.AddHours(2);
        var schedule = Schedules.ForVenue(Venue(Entry(start, start.AddHours(3))), Now);
        Assert.False(schedule.OneOff);
        Assert.Equal([new Occurrence(start, start.AddHours(3)), new Occurrence(start.AddDays(7), start.AddDays(7).AddHours(3))], schedule.Occurrences);
    }

    [Fact]
    public void AnOldWeeklyResolutionIsCarriedForwardToTheCurrentWeek()
    {
        // Resolved three weeks ago, ended 2 hours later; this week's is in progress
        var start = Now.AddDays(-21).AddHours(-1);
        var schedule = Schedules.ForVenue(Venue(Entry(start, start.AddHours(3))), Now);
        Assert.Equal(new Occurrence(Now.AddHours(-1), Now.AddHours(2)), schedule.Occurrences[0]);
    }

    [Fact]
    public void AResolutionEndingExactlyAWholeNumberOfWeeksAgoStepsPastNow()
    {
        var start = Now.AddDays(-14).AddHours(-3);
        var schedule = Schedules.ForVenue(Venue(Entry(start, Now.AddDays(-14))), Now);
        Assert.Equal(new Occurrence(Now.AddDays(7).AddHours(-3), Now.AddDays(7)), schedule.Occurrences[0]);
    }

    [Fact]
    public void AFortnightlyResolutionStepsByTwoWeeks()
    {
        var start = Now.AddDays(-7);
        var schedule = Schedules.ForVenue(Venue(Entry(start, start.AddHours(3), argument: 2)), Now);
        Assert.Equal(
            [new Occurrence(Now.AddDays(7), Now.AddDays(7).AddHours(3)), new Occurrence(Now.AddDays(21), Now.AddDays(21).AddHours(3))],
            schedule.Occurrences);
    }

    [Fact]
    public void AMonthlyResolutionIsKeptOnlyUntilItEnds()
    {
        var soon = Now.AddDays(3);
        Assert.Equal([new Occurrence(soon, soon.AddHours(3))], Schedules.ForVenue(Venue(Entry(soon, soon.AddHours(3), type: 1, argument: 2)), Now).Occurrences);
        var past = Now.AddDays(-3);
        Assert.Empty(Schedules.ForVenue(Venue(Entry(past, past.AddHours(3), type: 1, argument: 2)), Now).Occurrences);
    }

    [Fact]
    public void VenueEntriesAreMergedEarliestFirst()
    {
        var friday = Now.AddDays(6);
        var tomorrow = Now.AddDays(1);
        var schedule = Schedules.ForVenue(Venue(Entry(friday, friday.AddHours(3)), null, Entry(tomorrow, tomorrow.AddHours(3))), Now);
        Assert.Equal([tomorrow, friday, tomorrow.AddDays(7), friday.AddDays(7)], schedule.Occurrences.Select(o => o.Start!.Value));
    }

    [Fact]
    public void AVenueWithoutResolutionsOrWithAnInvertedOneHasNoOpenings()
    {
        var unresolved = new FfxivVenues.Opening(0, new FfxivVenues.Time(20, 0, false), new FfxivVenues.Time(23, 0, false), new FfxivVenues.Interval(0, 1));
        Assert.Empty(Schedules.ForVenue(Venue(unresolved), Now).Occurrences);
        Assert.Empty(Schedules.ForVenue(Venue(Entry(Now.AddHours(3), Now.AddHours(1))), Now).Occurrences);
        Assert.Empty(Schedules.ForVenue(null, Now).Occurrences);
    }

    [Fact]
    public void AVenueOpenAroundTheClockIsAlwaysOpen()
    {
        var day = new FfxivVenues.Time(0, 0, false);
        var night = new FfxivVenues.Time(23, 59, false);
        var week = Enumerable.Range(0, 7).Select(d => (FfxivVenues.Opening?)new FfxivVenues.Opening(d, day, night, new FfxivVenues.Interval(0, 1))).ToArray();
        Assert.True(Schedules.ForVenue(Venue(week), Now).AlwaysOpen);
    }

    // Party Finder ads

    [Fact]
    public void AnAdWithNoTimeIsOpenNowWhileListed()
    {
        var listed = Schedules.ForAd("Come to the Moonlit Bar, Mist W5 P12", Now, PartyFinderRegion.Europe, listed: true, Now);
        Assert.Equal([new Occurrence(null, null)], listed.Occurrences);
        Assert.True(listed.OneOff);
        Assert.False(listed.AlwaysOpen);

        Assert.Empty(Schedules.ForAd("Come to the Moonlit Bar, Mist W5 P12", Now, PartyFinderRegion.Europe, listed: false, Now).Occurrences);
    }

    [Fact]
    public void AnAdOpenNowUntilATimeEndsThen()
    {
        var schedule = Schedules.ForAd("Open now until 23:00 ST", Now, PartyFinderRegion.Europe, listed: true, Now);
        Assert.Equal([new Occurrence(null, new DateTimeOffset(2026, 11, 14, 23, 0, 0, TimeSpan.Zero))], schedule.Occurrences);
    }

    [Fact]
    public void AClosingTimeAloneCountsFromWhenTheAdWasFirstSeen()
    {
        // First seen at 23:30 EST and still listed at 00:23 EST: midnight has passed, it isn't tomorrow's
        var firstSeen = new DateTimeOffset(2026, 11, 15, 4, 30, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 11, 15, 5, 23, 0, TimeSpan.Zero);
        var times = Schedules.ParseAd("[RP] Himbo Hooters open until 12am EST", now);

        var schedule = Schedules.ForAd(times, now, PartyFinderRegion.NorthAmerica, listed: true, now, firstSeen: firstSeen);

        Assert.Equal([new Occurrence(null, new DateTimeOffset(2026, 11, 15, 5, 0, 0, TimeSpan.Zero))], schedule.Occurrences);
        Assert.Empty(Schedules.Listed(schedule, now));
    }

    [Fact]
    public void AStartedOpeningStillCountsFromWhenTheAdWasLastSeen()
    {
        // Only a closing time alone counts from the first sighting; "today" is the day it was last seen
        var times = Schedules.ParseAd("Today 10pm-11pm ST", Now);

        var schedule = Schedules.ForAd(times, Now, PartyFinderRegion.Europe, listed: true, Now, firstSeen: Now.AddDays(-1));

        Assert.Equal([new Occurrence(Now.AddHours(2), Now.AddHours(3))], schedule.Occurrences);
    }

    [Fact]
    public void AnAdOpenNowNoLongerListedIsNotOpen() =>
        Assert.Empty(Schedules.ForAd("Open now until 23:00 ST", Now, PartyFinderRegion.Europe, listed: false, Now).Occurrences);

    [Fact]
    public void AnAdsStatedTimesStandWhenItIsNoLongerListed()
    {
        var schedule = Schedules.ForAd("Tonight 10pm-2am ST", Now, PartyFinderRegion.Europe, listed: false, Now);
        Assert.Equal(
            [new Occurrence(new DateTimeOffset(2026, 11, 14, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 15, 2, 0, 0, TimeSpan.Zero))],
            schedule.Occurrences);
    }

    [Fact]
    public void AnAdWithoutAZoneUsesItsRegionsDefault()
    {
        // November in New York is UTC-5
        var schedule = Schedules.ForAd("Tonight 10pm-2am", Now, PartyFinderRegion.NorthAmerica, listed: true, Now);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 3, 0, 0, TimeSpan.Zero), schedule.Occurrences[0].Start);
    }

    [Fact]
    public void AStartedOpeningWithNoEndLastsOnlyWhileListed()
    {
        var seen = Now.AddHours(-1);
        Assert.Single(Schedules.ForAd("Tonight from 7pm ST", seen, PartyFinderRegion.Europe, listed: true, Now).Occurrences);
        Assert.Empty(Schedules.ForAd("Tonight from 7pm ST", seen, PartyFinderRegion.Europe, listed: false, Now).Occurrences);
        Assert.Single(Schedules.ForAd("Tonight from 11pm ST", seen, PartyFinderRegion.Europe, listed: false, Now).Occurrences);
    }

    [Fact]
    public void AStartExactlyNowWithNoEndNeedsTheAdListed()
    {
        // Seen at 19:00 for "8pm": it starts at now, so it has started and lasts only while listed
        var seen = Now.AddHours(-1);
        Assert.Empty(Schedules.ForAd("Tonight from 8pm ST", seen, PartyFinderRegion.Europe, listed: false, Now).Occurrences);
    }

    [Fact]
    public void AnAdOpenNowWithNoEndIsOpenWhileListed() =>
        Assert.Equal([new Occurrence(null, null)], Schedules.ForAd("OPEN NOW! Mist W5 P12", Now, PartyFinderRegion.Europe, listed: true, Now).Occurrences);

    [Fact]
    public void AnAdsOpeningsAreEarliestFirst()
    {
        var schedule = Schedules.ForAd("Sunday 9pm ST and Saturday 9pm ST", Now.AddHours(-12), PartyFinderRegion.Europe, listed: false, Now);
        Assert.Equal(
            [new DateTimeOffset(2026, 11, 14, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 15, 21, 0, 0, TimeSpan.Zero)],
            schedule.Occurrences.Select(o => o.Start!.Value));
    }

    [Fact]
    public void AnAdThatIs247IsAlwaysOpenWhileListed()
    {
        Assert.True(Schedules.ForAd("Open 24/7! Mist W5 P12", Now, PartyFinderRegion.Europe, listed: true, Now).AlwaysOpen);
        var unlisted = Schedules.ForAd("Open 24/7! Mist W5 P12", Now, PartyFinderRegion.Europe, listed: false, Now);
        Assert.False(unlisted.AlwaysOpen);
        Assert.Empty(unlisted.Occurrences);
    }

    [Fact]
    public void AnInGameAdIsStillListedForAnHourAfterItWasSeen()
    {
        Assert.True(Schedules.StillListed(ListingSource.InGamePartyFinder, Now.AddMinutes(-59), Now, 30));
        Assert.False(Schedules.StillListed(ListingSource.InGamePartyFinder, Now.AddHours(-1), Now, 30));
    }

    [Fact]
    public void AnXivpfAdIsStillListedForAnHourPlusTheCooldown()
    {
        Assert.True(Schedules.StillListed(ListingSource.Xivpf, Now.AddMinutes(-89), Now, 30));
        Assert.False(Schedules.StillListed(ListingSource.Xivpf, Now.AddMinutes(-90), Now, 30));
    }

    [Fact]
    public void AnAdIsListedUntilAnHourAfterItWasSeenPlusAFetchedSourcesCooldown()
    {
        Assert.Equal(Now.AddHours(1), Schedules.ListedUntil(ListingSource.InGamePartyFinder, Now, 30));
        Assert.Equal(Now.AddMinutes(90), Schedules.ListedUntil(ListingSource.Xivpf, Now, 30));
    }

    [Theory]
    [InlineData("Aether", PartyFinderRegion.NorthAmerica)]
    [InlineData("Crystal", PartyFinderRegion.NorthAmerica)]
    [InlineData("Dynamis", PartyFinderRegion.NorthAmerica)]
    [InlineData("primal ", PartyFinderRegion.NorthAmerica)]
    [InlineData("Chaos", PartyFinderRegion.Europe)]
    [InlineData("Light", PartyFinderRegion.Europe)]
    [InlineData("Shadow", PartyFinderRegion.Europe)]
    [InlineData("Materia", PartyFinderRegion.Oceania)]
    [InlineData("Elemental", PartyFinderRegion.Japan)]
    [InlineData("Gaia", PartyFinderRegion.Japan)]
    [InlineData("Mana", PartyFinderRegion.Japan)]
    [InlineData("Meteor", PartyFinderRegion.Japan)]
    public void DataCentresMapToTheirRegion(string dataCentre, PartyFinderRegion region) => Assert.Equal(region, Schedules.RegionOf(dataCentre));

    [Fact]
    public void AnUnknownDataCentreHasNoRegion() => Assert.Null(Schedules.RegionOf("Nowhere"));
}
