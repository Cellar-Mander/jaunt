using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class DuplicatesTests
{
    // Saturday
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly AddressKey Lounge = AddressKey.Of(new HousingAddress("Twintania", WorldSource.Text, District.Mist, 5, 12))!.Value;
    private static readonly AddressKey Bar = AddressKey.Of(new HousingAddress("Twintania", WorldSource.Text, District.Mist, 5, 13))!.Value;

    private static Occurrence At(double startHours, double endHours) => new(Now.AddHours(startHours), Now.AddHours(endHours));

    private static Occurrence From(double startHours) => new(Now.AddHours(startHours), null);

    private static MergeCandidate<string> Ad(string name, AddressKey? at, params Occurrence[] listed) =>
        new(name, at, new ListingSchedule(false, true, listed), listed);

    private static MergeCandidate<string> Venue(string name, AddressKey? at, params Occurrence[] listed) =>
        new(name, at, new ListingSchedule(false, false, listed), listed);

    private static MergeCandidate<string> Always(string name, AddressKey? at) =>
        new(name, at, ListingSchedule.Always, [new Occurrence(null, null)]);

    private static IReadOnlyList<IReadOnlyList<string>> Groups(params MergeCandidate<string>[] candidates) =>
        [.. Duplicates.Merge(candidates, Now).Select(m => m.Members)];

    // AddressKey

    [Fact]
    public void AnAddressKeyIgnoresTheWorldsCaseTheRoomAndAPlotsSubdivision()
    {
        var written = new HousingAddress("twintania", WorldSource.Listing, District.Mist, 5, 12, Subdivision: true, Room: "B1");
        Assert.Equal(Lounge, AddressKey.Of(written));
    }

    [Fact]
    public void ApartmentsAreKeyedByBuilding()
    {
        var main = AddressKey.Of(new HousingAddress("Twintania", WorldSource.Text, District.Mist, 5, null, Apartment: 12));
        var unsaid = AddressKey.Of(new HousingAddress("Twintania", WorldSource.Text, District.Mist, 5, null, Subdivision: false, Apartment: 12));
        var subdivision = AddressKey.Of(new HousingAddress("Twintania", WorldSource.Text, District.Mist, 5, null, Subdivision: true, Apartment: 12));
        var plot = AddressKey.Of(new HousingAddress("Twintania", WorldSource.Text, District.Mist, 5, 12));

        Assert.Equal(new AddressKey("TWINTANIA", District.Mist, 5, null, 12, Subdivision: false), main);
        Assert.Equal(main, unsaid);
        Assert.Equal(new AddressKey("TWINTANIA", District.Mist, 5, null, 12, Subdivision: true), subdivision);
        Assert.NotEqual(main, plot);
    }

    [Theory]
    [InlineData(null, District.Mist)]
    [InlineData("Twintania", null)]
    public void AnAddressWithoutAWorldOrDistrictHasNoKey(string? world, District? district) =>
        Assert.Null(AddressKey.Of(new HousingAddress(world, WorldSource.Text, district, 5, 12)));

    [Fact]
    public void AnAddressNamingOnlyTheWardIsKeyedByIt()
    {
        var key = AddressKey.Of(new HousingAddress("faerie", WorldSource.Text, District.LavenderBeds, 4, null, Subdivision: true));

        Assert.Equal(new AddressKey("FAERIE", District.LavenderBeds, 4, null, null, Subdivision: false), key);
        Assert.True(key!.Value.WardOnly);
        Assert.False(Lounge.WardOnly);
        Assert.False(AddressKey.Of(new HousingAddress("Faerie", WorldSource.Text, District.LavenderBeds, 4, null, Apartment: 3))!.Value.WardOnly);
        Assert.Equal(key, AddressKey.Of(new HousingAddress("Faerie", WorldSource.Text, District.LavenderBeds, 4, 38))!.Value.InWard);
    }

    // Overlap

    [Theory]
    [InlineData(0, 3, 2, 5, true)]
    [InlineData(2, 5, 0, 3, true)]
    [InlineData(0, 6, 2, 3, true)]
    [InlineData(0, 2, 2, 4, false)]
    [InlineData(2, 4, 0, 2, false)]
    [InlineData(0, 1, 2, 3, false)]
    public void OpeningsOverlapWhenEachStartsBeforeTheOtherEnds(double aStart, double aEnd, double bStart, double bEnd, bool overlap)
    {
        Assert.Equal(overlap, Duplicates.Overlap(At(aStart, aEnd), At(bStart, bEnd), Now));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, true)]
    [InlineData(3, false)]
    [InlineData(-1, false)]
    public void AnOpeningWithoutAnEndOverlapsOneSpanningItsStart(double start, bool overlap)
    {
        Assert.Equal(overlap, Duplicates.Overlap(From(start), At(0, 3), Now));
        Assert.Equal(overlap, Duplicates.Overlap(At(0, 3), From(start), Now));
    }

    [Fact]
    public void OpeningsWithoutAnEndOverlapOnlyWhenTheyStartTogether()
    {
        Assert.True(Duplicates.Overlap(From(2), From(2), Now));
        Assert.False(Duplicates.Overlap(From(2), From(3), Now));
    }

    [Fact]
    public void AnOpeningWithoutAStartCountsAsStartingNow()
    {
        Assert.True(Duplicates.Overlap(new Occurrence(null, null), At(-1, 1), Now));
        Assert.True(Duplicates.Overlap(new Occurrence(null, null), new Occurrence(null, null), Now));
        Assert.True(Duplicates.Overlap(new Occurrence(null, Now.AddHours(2)), At(1, 3), Now));
        Assert.False(Duplicates.Overlap(new Occurrence(null, null), At(1, 3), Now));
    }

    // Merge

    [Fact]
    public void ListingsAtOneAddressWithOverlappingOpeningsAreOneEntry()
    {
        Assert.Equal([["ad", "venue"]], Groups(Ad("ad", Lounge, At(0, 3)), Venue("venue", Lounge, At(1, 5))));
    }

    [Fact]
    public void ARepostedAdIsOneEntryWithTheFirst()
    {
        var open = new Occurrence(null, null);
        Assert.Equal([["first", "repost"]], Groups(Ad("first", Lounge, open), Ad("repost", Lounge, open)));
    }

    [Fact]
    public void ListingsAtOneAddressAtDifferentTimesStaySeparate()
    {
        Assert.Equal([["tonight"], ["saturday"]], Groups(Ad("tonight", Lounge, At(0, 3)), Ad("saturday", Lounge, At(168, 171))));
    }

    [Fact]
    public void ListingsAtDifferentAddressesStaySeparate()
    {
        Assert.Equal([["lounge"], ["bar"]], Groups(Ad("lounge", Lounge, At(0, 3)), Ad("bar", Bar, At(0, 3))));
    }

    [Fact]
    public void ListingsWithoutAnAddressNeverMerge()
    {
        Assert.Equal([["a"], ["b"]], Groups(Ad("a", null, At(0, 3)), Ad("b", null, At(0, 3))));
    }

    [Fact]
    public void MergingIsTransitive()
    {
        // early and late don't overlap, but each overlaps middle
        Assert.Equal([["early", "late", "middle"]],
            Groups(Ad("early", Lounge, At(0, 2)), Ad("late", Lounge, At(3, 5)), Ad("middle", Lounge, At(1, 4))));
    }

    [Fact]
    public void AnyOverlappingPairOfOpeningsMerges()
    {
        // The venue's next opening overlaps the ad, its current one doesn't
        Assert.Equal([["venue", "ad"]], Groups(Venue("venue", Lounge, At(-1, 1), At(23, 26)), Ad("ad", Lounge, At(24, 27))));
    }

    // Ward-only addresses

    private static readonly AddressKey Ward5 = Lounge.InWard;

    [Fact]
    public void WardOnlyListingsInOneWardMerge()
    {
        Assert.Equal([["a", "b"]], Groups(Ad("a", Ward5, At(0, 3)), Ad("b", Ward5, At(1, 3))));
    }

    [Fact]
    public void AWardOnlyListingJoinsTheOnePlotInItsWardItOverlaps()
    {
        // The CAT SCRATCH CLUB: two xivpf ads naming only the ward, and the game's naming the plot
        Assert.Equal([["vague", "repost", "plot", "other"]],
            Groups(Ad("vague", Ward5, At(0, 4)), Ad("repost", Ward5, At(0, 4)), Ad("plot", Lounge, From(0)), Ad("other", Lounge, At(3, 5))));
    }

    [Fact]
    public void AWardOnlyListingIgnoresPlotsInItsWardItDoesntOverlap()
    {
        Assert.Equal([["vague", "lounge"], ["bar"]], Groups(Ad("vague", Ward5, At(0, 3)), Ad("lounge", Lounge, At(1, 2)), Ad("bar", Bar, At(5, 6))));
    }

    [Fact]
    public void AWardOnlyListingOverlappingTwoPlotsJoinsNeither()
    {
        Assert.Equal([["vague"], ["lounge"], ["bar"]], Groups(Ad("vague", Ward5, At(0, 3)), Ad("lounge", Lounge, At(1, 2)), Ad("bar", Bar, At(1, 2))));
    }

    [Fact]
    public void AWardOnlyListingDoesntJoinAPlotInAnotherWard()
    {
        var elsewhere = Lounge with { Ward = 6 };
        Assert.Equal([["vague"], ["lounge"]], Groups(Ad("vague", Ward5, At(0, 3)), Ad("lounge", elsewhere, At(1, 2))));
    }

    [Fact]
    public void AWardOnlyListingNotOverlappingThePlotStaysSeparate()
    {
        Assert.Equal([["vague"], ["lounge"]], Groups(Ad("vague", Ward5, At(0, 3)), Ad("lounge", Lounge, At(4, 5))));
    }

    [Fact]
    public void A24HourVenueMergesWithEveryListingAtItsAddress()
    {
        var merged = Assert.Single(Duplicates.Merge([Always("venue", Lounge), Ad("ad", Lounge, At(48, 50))], Now));

        Assert.Equal(["venue", "ad"], merged.Members);
        Assert.True(merged.Schedule.AlwaysOpen);
        Assert.Equal([new Occurrence(null, null)], merged.Listed);
    }

    private static MergeCandidate<string> Said(string name, string text, AddressKey? at, params Occurrence[] listed) =>
        Ad(name, at, listed) with { AdText = text };

    [Fact]
    public void AdsWithIdenticalTextMergeWhateverTheirAddressOrOpenings()
    {
        const string text = "Velvet Lounge is OPEN! Mist W5 P12, drinks and dancing";
        var merged = Assert.Single(Duplicates.Merge([Said("in game", text, Lounge, At(0, 3)), Said("xivpf", text, null, At(30, 32))], Now));

        Assert.Equal(["in game", "xivpf"], merged.Members);
        Assert.Equal([At(0, 3), At(30, 32)], merged.Listed);
    }

    [Fact]
    public void IdenticalTextChainsWithAddressMatches()
    {
        const string text = "Velvet Lounge is OPEN!";
        Assert.Equal(
            [["venue", "ad", "repost"]],
            Groups(Venue("venue", Lounge, At(-1, 2)), Said("ad", text, Lounge, At(0, 1)), Said("repost", text, Bar, At(40, 41))));
    }

    [Theory]
    [InlineData("Velvet Lounge is OPEN!", "Velvet Lounge is open!")]
    [InlineData("Velvet Lounge is OPEN!", "Velvet Lounge is OPEN! ")]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    public void AdsWhoseTextDiffersOrIsBlankDoNotMergeOnIt(string a, string b) =>
        Assert.Equal([["a"], ["b"]], Groups(Said("a", a, Lounge, At(0, 1)), Said("b", b, null, At(0, 1))));

    [Fact]
    public void ListingsWithoutAdTextNeverMergeOnIt() =>
        Assert.Equal([["a"], ["b"]], Groups(Ad("a", Lounge, At(0, 1)), Ad("b", null, At(0, 1))));

    [Fact]
    public void AMergedEntryListsEveryOpeningOverlappingOnesCombined()
    {
        var merged = Assert.Single(Duplicates.Merge(
            [Venue("venue", Lounge, At(-1, 2), At(23, 26)), Ad("ad", Lounge, At(1, 4)), Ad("open", Lounge, From(0))], Now));

        Assert.Equal([At(-1, 4), At(23, 26)], merged.Listed);
        Assert.Equal(merged.Listed, merged.Schedule.Occurrences);
        Assert.False(merged.Schedule.OneOff);
        Assert.False(merged.Schedule.AlwaysOpen);
    }

    [Fact]
    public void CombiningWithAnOpeningOfUnknownStartKeepsItUnknown()
    {
        var merged = Assert.Single(Duplicates.Merge([Ad("a", Lounge, new Occurrence(null, Now.AddHours(1))), Ad("b", Lounge, At(-2, 3))], Now));

        Assert.Equal([new Occurrence(null, Now.AddHours(3))], merged.Listed);
        Assert.True(merged.Schedule.OneOff);
    }

    [Fact]
    public void OpeningsWithoutAnEndCombineIntoOne()
    {
        var merged = Assert.Single(Duplicates.Merge([Ad("a", Lounge, From(2)), Ad("b", Lounge, From(2))], Now));
        Assert.Equal([From(2)], merged.Listed);
    }

    [Fact]
    public void AListingWithNoDuplicateIsKeptAsGiven()
    {
        var venue = Venue("venue", Lounge, At(-1, 2), At(23, 26));
        var merged = Assert.Single(Duplicates.Merge([venue], Now));

        Assert.Same(venue.Schedule, merged.Schedule);
        Assert.Same(venue.Listed, merged.Listed);
    }

    // Ranks

    [Fact]
    public void AMergedEntryTakesFfxivVenuesNameThenAnEventsThenAnAds()
    {
        ListingSource[] sources = [ListingSource.InGamePartyFinder, ListingSource.RpEventCalendar, ListingSource.Partake, ListingSource.FfxivVenues, ListingSource.Xivpf];
        Assert.Equal(
            [ListingSource.FfxivVenues, ListingSource.Partake, ListingSource.RpEventCalendar, ListingSource.InGamePartyFinder, ListingSource.Xivpf],
            sources.OrderBy(Duplicates.TitleRank));
    }

    [Fact]
    public void AMergedEntryTakesAnAdsAddressFirst()
    {
        ListingSource[] sources = [ListingSource.Partake, ListingSource.RpEventCalendar, ListingSource.FfxivVenues, ListingSource.Xivpf, ListingSource.InGamePartyFinder];
        Assert.Equal(
            [ListingSource.Xivpf, ListingSource.InGamePartyFinder, ListingSource.FfxivVenues, ListingSource.RpEventCalendar, ListingSource.Partake],
            sources.OrderBy(Duplicates.AddressRank));
    }

    // UnstatedZone

    [Fact]
    public void FindsAnyZoneTheRuntimeKnowsAndNoneForAnUnknownId()
    {
        Assert.Equal("Europe/Paris", OpeningTimeResolver.FindZone("Europe/Paris")?.Id);
        Assert.Null(OpeningTimeResolver.FindZone("Mars/Olympus"));
    }

    private static readonly TimeZoneInfo NewYork = OpeningTimeResolver.FindZone("America/New_York")!;

    // The Orchid House ad, seen on Twintania (EU) at 22:09Z: FFXIV Venues lists it at 19:00 New York time
    private static readonly DateTimeOffset OrchidSeen = new(2026, 11, 6, 22, 9, 0, TimeSpan.Zero);
    private const string OrchidAd = "Orchid House of Beauties TONIGHT 11/06 @7pm Bismarck Mist W1 P1";

    [Fact]
    public void AnAdWithoutAZoneAtAVenueTakesTheVenuesZone()
    {
        var times = Schedules.ParseAd(OrchidAd, OrchidSeen);

        var zone = Duplicates.UnstatedZone(times, OrchidSeen, NewYork, []);
        var schedule = Schedules.ForAd(times, OrchidSeen, PartyFinderRegion.Europe, listed: true, OrchidSeen, zone);

        Assert.Same(NewYork, zone);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero), Assert.Single(schedule.Occurrences).Start);
    }

    [Fact]
    public void AnAdThatStatesItsZoneKeepsIt()
    {
        var times = Schedules.ParseAd("TONIGHT 11/06 @7pm ST", OrchidSeen);
        Assert.Null(Duplicates.UnstatedZone(times, OrchidSeen, NewYork, []));
    }

    [Fact]
    public void AnAdWithoutAStartTimeNeedsNoZone()
    {
        Assert.Null(Duplicates.UnstatedZone(Schedules.ParseAd("Open now!", OrchidSeen), OrchidSeen, NewYork, []));
    }

    [Fact]
    public void WithoutAVenueZoneAnAdTakesTheZoneThatPutsItsStartOnAListedStart()
    {
        // Partake sends 00:00Z, which is 7pm in New York on 6 November
        var times = Schedules.ParseAd(OrchidAd, OrchidSeen);
        var partakeStart = new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

        var zone = Duplicates.UnstatedZone(times, OrchidSeen, venueZone: null, [Now, partakeStart]);

        Assert.NotNull(zone);
        Assert.Equal(partakeStart, OpeningTimeResolver.Resolve(Assert.Single(times), OrchidSeen, zone).Start);
    }

    [Fact]
    public void WithNoMatchingStartTheRegionDefaultStands()
    {
        var times = Schedules.ParseAd(OrchidAd, OrchidSeen);
        Assert.Null(Duplicates.UnstatedZone(times, OrchidSeen, venueZone: null, []));
        Assert.Null(Duplicates.UnstatedZone(times, OrchidSeen, venueZone: null, [Now]));
    }

    [Fact]
    public void AStatedZoneWinsOverTheUnstatedOne()
    {
        var times = Schedules.ParseAd("Tonight 7pm ST", OrchidSeen);
        var schedule = Schedules.ForAd(times, OrchidSeen, PartyFinderRegion.Europe, listed: true, OrchidSeen, NewYork);

        Assert.Equal(new DateTimeOffset(2026, 11, 6, 19, 0, 0, TimeSpan.Zero), Assert.Single(schedule.Occurrences).Start);
    }

    // Links

    private static WebLink Link(string url) => new(url, new Uri(url));

    [Fact]
    public void TheRpCalendarsForwarderIsDroppedWhenAnotherListingGivesAPage()
    {
        // VLVT DRMS: FFXIV Venues gives the site, the calendar a xiv.fyi link that forwards to it
        var links = Duplicates.Links([
            [Link("https://vlvt-drms.carrd.co/"), Link("https://discord.gg/vlvt-drms")],
            [Link("https://xiv.fyi/k1yK")]]);

        Assert.Equal(["https://vlvt-drms.carrd.co/", "https://discord.gg/vlvt-drms"], links.Select(l => l.Text));
    }

    [Fact]
    public void TheForwarderIsDroppedWhicheverListingComesFirst()
    {
        var links = Duplicates.Links([[Link("https://xiv.fyi/k1yK")], [Link("https://vlvt-drms.carrd.co/")]]);

        Assert.Equal(["https://vlvt-drms.carrd.co/"], links.Select(l => l.Text));
    }

    [Fact]
    public void TheForwarderStaysWhenNoOtherListingGivesAPage()
    {
        // A Discord invite isn't a page, so the forwarder is still the entry's Open page
        var links = Duplicates.Links([[Link("https://discord.gg/oasis")], [Link("https://xiv.fyi/ZE2J")]]);

        Assert.Equal(["https://discord.gg/oasis", "https://xiv.fyi/ZE2J"], links.Select(l => l.Text));
    }

    [Fact]
    public void ALinkGivenTwiceIsKeptOnce()
    {
        var links = Duplicates.Links([[Link("https://teatime.carrd.co/")], [Link("https://teatime.carrd.co/"), Link("https://x.com/teatime")]]);

        Assert.Equal(["https://teatime.carrd.co/", "https://x.com/teatime"], links.Select(l => l.Text));
    }

    [Theory]
    [InlineData("https://xiv.fyi/ZE2J", true)]
    [InlineData("http://XIV.FYI/ZE2J", true)]
    [InlineData("https://www.xiv.fyi/ZE2J", true)]
    [InlineData("https://notxiv.fyi/ZE2J", false)]
    [InlineData("https://xiv.fyi.example.com/ZE2J", false)]
    public void RecognisesTheRpCalendarsForwarder(string url, bool forwarder) =>
        Assert.Equal(forwarder, RpEventCalendar.IsForwarder(new Uri(url)));
}
