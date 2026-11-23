using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class AdParsesTests
{
    // Saturday
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static AdParses.Parse Single(string text, DateTimeOffset lastSeen, string? listedOn = "Siren") =>
        Assert.Single(AdParses.Run(
            [new AdParses.Ad(ListingSource.Xivpf, "1", text, listedOn, lastSeen, FirstSeen: null)],
            TestGazetteer.Parser, PartyFinderRegion.Europe, cooldownMinutes: 30, Now));

    [Fact]
    public void AStillListedAdGetsItsTimesAndItsAddressOnTheWorldItWasListedOn()
    {
        const string text = "Velvet Room Mist W5 P12 open 21:00-23:00 GMT";
        var parse = Single(text, Now.AddMinutes(-5));

        Assert.Equal(OpeningTimesFormat.Describe(Schedules.ParseAd(text, Now.AddMinutes(-5))), OpeningTimesFormat.Describe(parse.Times));
        var address = Assert.Single(parse.Details!.Addresses);
        Assert.Equal(District.Mist, address.District);
        Assert.Equal(5, address.Ward);
        Assert.Equal(12, address.Plot);
        Assert.Equal("Siren", address.World);
    }

    [Fact]
    public void AnAdNoLongerListedThatStatesALaterDateStillGetsItsAddress()
    {
        var parse = Single("Grand opening 20/11 21:00-23:00 GMT, Mist W5 P12", Now.AddDays(-3));

        Assert.Equal("2026-11-20 21:00-23:00 GMT", OpeningTimesFormat.Describe(parse.Times));
        Assert.Single(parse.Details!.Addresses);
    }

    [Fact]
    public void AnAdNoLongerListedThatListsNothingIsNotReadForAddresses()
    {
        var parse = Single("Velvet Room Mist W5 P12 open now", Now.AddDays(-3));

        Assert.Null(parse.Details);
        Assert.Equal(DateTimeOffset.MaxValue, parse.QuietUntil);
    }

    [Fact]
    public void AListedAdHasNoQuietTime() => Assert.Null(Single("Velvet Room Mist W5 P12 open now", Now.AddMinutes(-5)).QuietUntil);

    [Fact]
    public void AnAdStatingADateBeyondTheHorizonIsQuietUntilItComesWithinIt()
    {
        const string text = "Grand opening 20/12 21:00-23:00 GMT, Mist W5 P12";
        var seen = Now.AddDays(-3);
        var parse = Single(text, seen);
        var times = Schedules.ParseAd(text, seen);
        bool ListsNothing(DateTimeOffset at) =>
            Duplicates.ListsNothingAnywhere(times, Schedules.ForAd(times, seen, PartyFinderRegion.Europe, listed: false, at), at);

        var until = Assert.NotNull(parse.QuietUntil);
        Assert.Equal(new DateTimeOffset(2026, 12, 20, 21, 0, 0, TimeSpan.Zero) - Schedules.OneOffHorizon, until);
        Assert.True(ListsNothing(until.AddTicks(-1)));
        Assert.False(ListsNothing(until));
    }

    [Fact]
    public void AStillListedAdThatListsNothingIsQuietUntilItStopsCountingAsListed()
    {
        var seen = Now.AddMinutes(-10);

        Assert.Equal(
            Schedules.ListedUntil(ListingSource.Xivpf, seen, 30),
            AdParses.QuietUntil(ListingSource.Xivpf, seen, 30, ListingSchedule.None, Now));
    }

    [Fact]
    public void AnOpeningChangeBeforeTheAdStopsCountingAsListedComesFirst()
    {
        var seen = Now.AddMinutes(-10);
        var schedule = new ListingSchedule(false, true, [new Occurrence(Now.AddMinutes(5), Now.AddHours(2))]);

        Assert.Equal(Now.AddMinutes(5), AdParses.QuietUntil(ListingSource.Xivpf, seen, 30, schedule, Now));
    }

    [Fact]
    public void EachAdIsParsedFromItsOwnFieldsInOrder()
    {
        AdParses.Ad Ad(string id, string text) => new(ListingSource.InGamePartyFinder, id, text, "Odin", Now, FirstSeen: null);
        var ads = new[] { Ad("a", "Mist W1 P1"), Ad("b", "Goblet W2 P2") };

        var parses = AdParses.Run(ads, TestGazetteer.Parser, PartyFinderRegion.Europe, 30, Now);

        Assert.Equal(ads, parses.Select(p => p.Ad));
        Assert.Equal([District.Mist, District.Goblet], parses.Select(p => Assert.Single(p.Details!.Addresses).District));
    }

    [Fact]
    public void AnAdCopiesTheFieldsTheParsersRead()
    {
        var seen = Now.AddMinutes(-1);
        var listing = new CachedListing { Source = ListingSource.Shout, Id = "s", Description = "Mist W5 P12", ListedOn = "Odin", LastSeen = seen, FirstSeen = Now.AddHours(-1) };

        Assert.Equal(new AdParses.Ad(ListingSource.Shout, "s", "Mist W5 P12", "Odin", seen, Now.AddHours(-1)), AdParses.Ad.Of(listing));
    }
}
