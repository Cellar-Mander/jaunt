using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class AutoVisitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 20, 0, 0, TimeSpan.Zero);

    private static readonly AddressKey Tavern = new("SIREN", District.Empyreum, 16, 20, null, false);
    private static readonly AddressKey NextDoor = new("SIREN", District.Empyreum, 16, 21, null, false);
    private static readonly AddressKey TavernWard = Tavern.InWard;
    private static readonly AddressKey Flat = new("SIREN", District.Mist, 3, null, 12, true);

    private static readonly IReadOnlyList<Occurrence> OpenNow = [new(Now.AddHours(-1), Now.AddHours(2))];
    private static readonly IReadOnlyList<Occurrence> OpensLater = [new(Now.AddHours(1), Now.AddHours(3))];
    private static readonly IReadOnlyList<Occurrence> AlwaysOpen = [new(null, null)];

    private readonly ListingCache cache = new();
    private readonly AutoVisits visits = new();

    private CachedListing Venue(string id = "v") => cache.Seen(ListingSource.FfxivVenues, id, "Tavern", Now);

    private CachedListing Event(string id = "e") => cache.Seen(ListingSource.Partake, id, "Gala", Now, oneOff: true);

    private static MarkEntry Entry(AddressKey? at, params CachedListing[] members) =>
        new(members, at is { } key ? [key] : [], "Tavern", "Siren, Empyreum, W16, P20", []);

    private bool Check(MarkEntry entry, IReadOnlyList<Occurrence> listed, ref PlaceMark? place, DateTimeOffset? now = null) =>
        visits.Check(cache, entry, ref place, listed, now ?? Now);

    private bool Check(MarkEntry entry, IReadOnlyList<Occurrence> listed, DateTimeOffset? now = null)
    {
        PlaceMark? place = null;
        return Check(entry, listed, ref place, now);
    }

    [Fact]
    public void AtTheAddressWhileOpenMarksThePlaceVisited()
    {
        var entry = Entry(Tavern, Venue());
        visits.At(Tavern);
        PlaceMark? place = null;

        Assert.True(Check(entry, OpenNow, ref place));

        Assert.NotNull(place);
        Assert.Equal(Tavern, place.Address);
        Assert.True(place.Visited);
        Assert.True(EntryMarks.Has(entry, place, EntryMark.Visited));
    }

    [Fact]
    public void ATwentyFourSevenVenueCountsAsOpen()
    {
        visits.At(Tavern);

        Assert.True(Check(Entry(Tavern, Venue()), AlwaysOpen));
    }

    [Fact]
    public void AnEventIsMarkedRatherThanThePlace()
    {
        var gala = Event();
        visits.At(Tavern);
        PlaceMark? place = null;

        Assert.True(Check(Entry(Tavern, Venue(), gala), OpenNow, ref place));

        Assert.True(gala.Visited);
        Assert.Null(place);
    }

    [Fact]
    public void AnApartmentCounts()
    {
        visits.At(Flat);

        Assert.True(Check(Entry(Flat, Venue()), OpenNow));
    }

    [Fact]
    public void OutsideOpeningHoursNothingIsMarked()
    {
        var entry = Entry(Tavern, Venue());
        visits.At(Tavern);

        Assert.False(Check(entry, OpensLater));
        Assert.False(Check(entry, [new(Now.AddHours(-3), Now)]));
        Assert.False(Check(entry, []));
        Assert.Empty(cache.Places);
    }

    [Fact]
    public void AnEntryThatOpensDuringTheStayIsMarkedOnceOpen()
    {
        var entry = Entry(Tavern, Venue());
        visits.At(Tavern);

        Assert.False(Check(entry, OpensLater));
        Assert.True(Check(entry, OpensLater, Now.AddHours(1)));
    }

    [Fact]
    public void AtADifferentAddressNothingIsMarked()
    {
        visits.At(NextDoor);

        Assert.False(Check(Entry(Tavern, Venue()), OpenNow));
        Assert.Empty(cache.Places);
    }

    [Fact]
    public void AnEntryNamingOnlyTheWardIsNeverMarked()
    {
        visits.At(Tavern);

        // An entry's places hold only plots and apartments, so a ward-only address leaves them empty
        Assert.False(Check(Entry(null, Venue()), OpenNow));
    }

    [Fact]
    public void BeingSomewhereThatNamesOnlyTheWardCountsAsNowhere()
    {
        Assert.False(visits.At(TavernWard));
        Assert.Null(visits.Here);
        Assert.False(visits.At(null));
    }

    [Fact]
    public void NowhereMarksNothing()
    {
        visits.At(null);

        Assert.False(Check(Entry(Tavern, Venue()), OpenNow));
    }

    [Fact]
    public void AnyOfTheEntrysPlacesCounts()
    {
        var entry = new MarkEntry([Venue()], [NextDoor, Tavern], "Tavern", "", []);
        visits.At(Tavern);

        Assert.True(Check(entry, OpenNow));
    }

    [Fact]
    public void MarkedNotVisitedDuringTheStayStaysThatWay()
    {
        var entry = Entry(Tavern, Venue());
        visits.At(Tavern);
        PlaceMark? place = null;
        Assert.True(Check(entry, OpenNow, ref place));

        EntryMarks.Toggle(cache, entry, ref place, EntryMark.Visited);
        visits.At(Tavern);

        Assert.False(Check(entry, OpenNow, ref place));
        Assert.False(place!.Visited);
    }

    [Fact]
    public void ARebuiltEntryWithANewTitleMemberIsStillHandled()
    {
        var venue = Venue();
        visits.At(Tavern);
        PlaceMark? place = null;
        Assert.True(Check(Entry(Tavern, venue), OpenNow, ref place));
        EntryMarks.Toggle(cache, Entry(Tavern, venue), ref place, EntryMark.Visited);

        // An ad listed later merges in and takes the title
        var ad = cache.SeenAd(ListingSource.InGamePartyFinder, "a", "Tavern Empyreum W16 P20", "Siren", Now);

        Assert.False(Check(Entry(Tavern, ad, venue), OpenNow, ref place));
    }

    [Fact]
    public void ANewStayMarksAgain()
    {
        var entry = Entry(Tavern, Venue());
        visits.At(Tavern);
        PlaceMark? place = null;
        Check(entry, OpenNow, ref place);
        EntryMarks.Toggle(cache, entry, ref place, EntryMark.Visited);

        visits.At(null);
        visits.At(Tavern);

        Assert.True(Check(entry, OpenNow, ref place));
    }

    [Fact]
    public void AnAlreadyVisitedEntryIsLeftAlone()
    {
        var entry = Entry(Tavern, Venue());
        PlaceMark? place = null;
        EntryMarks.Toggle(cache, entry, ref place, EntryMark.Visited);
        visits.At(Tavern);

        Assert.False(Check(entry, OpenNow, ref place));
        Assert.True(place!.Visited);
    }

    // Game indexes

    [Theory]
    [InlineData(15, 19, 16, 20)]
    [InlineData(0, 0, 1, 1)]
    public void APlotIsTheGamesIndexesPlusOne(int wardIndex, int plotIndex, int ward, int plot)
    {
        Assert.Equal(new AddressKey("SIREN", District.Empyreum, ward, plot, null, false), AutoVisits.OnPlot("Siren", District.Empyreum, wardIndex, plotIndex));
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(-128, 3)]
    [InlineData(5, -1)]
    public void OffAPlotIsNowhere(int plotIndex, int wardIndex)
    {
        Assert.Null(AutoVisits.OnPlot("Siren", District.Empyreum, wardIndex, plotIndex));
    }

    [Fact]
    public void WithoutAWorldOrDistrictIsNowhere()
    {
        Assert.Null(AutoVisits.OnPlot(null, District.Empyreum, 0, 0));
        Assert.Null(AutoVisits.OnPlot("Siren", null, 0, 0));
        Assert.Null(AutoVisits.Inside("Siren", null, 0, false, 0, 0, 0, false));
    }

    [Fact]
    public void InsideAHouseIsItsPlotWhicheverRoom()
    {
        Assert.Equal(Tavern, AutoVisits.Inside("Siren", District.Empyreum, 15, false, 0, 19, 0, false));
        Assert.Equal(Tavern, AutoVisits.Inside("Siren", District.Empyreum, 15, false, 0, 19, 3, false));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void InsideAnApartmentIsItsNumberAndDivision(int division, bool subdivision)
    {
        Assert.Equal(
            new AddressKey("SIREN", District.Mist, 3, null, 12, subdivision),
            AutoVisits.Inside("Siren", District.Mist, 2, true, division, 0, 12, false));
    }

    [Fact]
    public void AnApartmentLobbyOrAWorkshopIsNowhere()
    {
        Assert.Null(AutoVisits.Inside("Siren", District.Mist, 2, true, 0, 0, 0, false));
        Assert.Null(AutoVisits.Inside("Siren", District.Mist, 2, false, 0, 4, 0x3FF, true));
    }
}
