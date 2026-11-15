using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class EntryMarksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly AddressKey Tavern = new("SIREN", District.Empyreum, 16, 20, null, false);
    private static readonly AddressKey NewHouse = new("SIREN", District.Mist, 3, 7, null, false);
    private static readonly AddressKey Elsewhere = new("SIREN", District.Goblet, 9, 1, null, false);

    private readonly ListingCache cache = new();

    private CachedListing Venue(string id = "v") => cache.Seen(ListingSource.FfxivVenues, id, "Tavern", Now);

    private CachedListing Event(ListingSource source = ListingSource.Partake, string id = "e") => cache.Seen(source, id, "Gala", Now, oneOff: true);

    private CachedListing Ad(string id) => cache.SeenAd(ListingSource.InGamePartyFinder, id, "Tavern Empyreum W16 P20", "Siren", Now);

    private static MarkEntry Entry(AddressKey? at, params CachedListing[] members) => Entry(at, [], members);

    private static MarkEntry Entry(AddressKey? at, string[] links, params CachedListing[] members) =>
        new(members, at is { } key ? [key] : [], "Tavern", "Siren, Empyreum, W16, P20", [.. links.Select(l => new Uri(l))]);

    private PlaceMark?[] Match(params MarkEntry[] entries) => PlaceMarks.Match(cache, entries);

    private PlaceMark? Toggle(MarkEntry entry, EntryMark mark, PlaceMark? place = null)
    {
        EntryMarks.Toggle(cache, entry, ref place, mark);
        return place;
    }

    // Targets

    [Theory]
    [InlineData(EntryMark.Favourite, MarkTarget.Place)]
    [InlineData(EntryMark.Hidden, MarkTarget.Place)]
    [InlineData(EntryMark.Visited, MarkTarget.Event)]
    public void AtAnAddressMarksGoOnThePlaceButVisitedGoesOnTheEvent(EntryMark mark, MarkTarget expected)
    {
        Assert.Equal(expected, EntryMarks.Target(Entry(Tavern, Venue(), Event(), Ad("a")), mark));
    }

    [Fact]
    public void VisitedGoesOnThePlaceWhenThereIsNoEvent()
    {
        Assert.Equal(MarkTarget.Place, EntryMarks.Target(Entry(Tavern, Ad("a")), EntryMark.Visited));
    }

    [Theory]
    [InlineData(EntryMark.Favourite)]
    [InlineData(EntryMark.Hidden)]
    public void WithNoPlotOrApartmentMarksGoOnTheEventElseEachListing(EntryMark mark)
    {
        Assert.Equal(MarkTarget.Event, EntryMarks.Target(Entry(null, Event(ListingSource.RpEventCalendar), Ad("a")), mark));
        Assert.Equal(MarkTarget.Listings, EntryMarks.Target(Entry(null, Ad("b"), Ad("c")), mark));
    }

    // Toggling

    [Fact]
    public void FavouritingMarksThePlaceNotTheListings()
    {
        var ad = Ad("a");
        var place = Toggle(Entry(Tavern, ad), EntryMark.Favourite);

        Assert.Same(cache.PlaceAt(Tavern), place);
        Assert.True(place!.Favourite);
        Assert.False(ad.Favourite);
        Assert.Equal("Tavern", place.Title);
        Assert.Equal("Siren, Empyreum, W16, P20", place.ShownAddress);
    }

    [Fact]
    public void VisitingAnEventMarksOnlyThatEvent()
    {
        var gala = Event();
        var place = Toggle(Entry(Tavern, Venue(), gala), EntryMark.Visited);

        Assert.Null(place);
        Assert.True(gala.Visited);
        Assert.Empty(cache.Places);
    }

    [Fact]
    public void AnEntryOfOnlyWardAdsMarksEveryAd()
    {
        var a = Ad("a");
        var b = Ad("b");

        Toggle(Entry(null, a, b), EntryMark.Hidden);

        Assert.True(a.Hidden && b.Hidden);
    }

    [Fact]
    public void TogglingAShownMarkClearsItFromThePlaceAndEveryListing()
    {
        var gala = Event();
        gala.Favourite = true;
        var entry = Entry(Tavern, gala);
        var place = cache.Place(Tavern);
        place.Favourite = true;
        place.Visited = true;

        Assert.True(EntryMarks.Has(entry, place, EntryMark.Favourite));
        Toggle(entry, EntryMark.Favourite, place);

        Assert.False(place.Favourite || gala.Favourite);
        Assert.True(place.Visited);
        Assert.False(EntryMarks.Has(entry, place, EntryMark.Favourite));
    }

    // Matching later listings

    [Fact]
    public void ALaterAdAtTheAddressCarriesTheMarkAfterTheFirstExpires()
    {
        Toggle(Entry(Tavern, Ad("first")), EntryMark.Hidden);
        cache.RemoveExpired(Now.AddMonths(2));

        var matched = Match(Entry(Tavern, Ad("reposted")), Entry(Elsewhere, Ad("other")));

        Assert.True(matched[0]!.Hidden);
        Assert.Null(matched[1]);
    }

    [Fact]
    public void AVenueThatMovesOnFfxivVenuesTakesItsMarksWithIt()
    {
        var venue = Venue();
        Toggle(Entry(Tavern, venue), EntryMark.Favourite);

        var matched = Match(Entry(NewHouse, venue), Entry(Tavern, Ad("new occupant")));

        Assert.True(matched[0]!.Favourite);
        Assert.Equal(NewHouse, matched[0]!.Address);
        Assert.Null(matched[1]);
        Assert.Null(cache.PlaceAt(Tavern));
    }

    [Fact]
    public void AWebsiteMovesTheMarksWhenNothingAtTheOldAddressShowsIt()
    {
        Toggle(Entry(Tavern, ["https://tavern.carrd.co/"], Ad("a")), EntryMark.Favourite);

        var matched = Match(Entry(NewHouse, ["https://www.Tavern.carrd.co"], Ad("b")), Entry(Tavern, ["https://other.carrd.co"], Ad("c")));

        Assert.True(matched[0]!.Favourite);
        Assert.Equal(NewHouse, matched[0]!.Address);
        Assert.Null(matched[1]);
    }

    [Fact]
    public void AWebsiteStillShownAtTheOldAddressMovesNothing()
    {
        Toggle(Entry(Tavern, ["https://tavern.carrd.co"], Ad("a")), EntryMark.Favourite);

        // Both orders, as a wrong move from one entry could be undone by the other later in the pass
        var oldFirst = Match(Entry(Tavern, ["https://tavern.carrd.co"], Ad("c")), Entry(NewHouse, ["https://tavern.carrd.co"], Ad("b")));
        var newFirst = Match(Entry(NewHouse, ["https://tavern.carrd.co"], Ad("b")), Entry(Tavern, ["https://tavern.carrd.co"], Ad("c")));

        Assert.Equal(Tavern, oldFirst[0]!.Address);
        Assert.Null(oldFirst[1]);
        Assert.Null(newFirst[0]);
        Assert.Equal(Tavern, newFirst[1]!.Address);
    }

    [Fact]
    public void AnAdWithoutLinksAtTheOldAddressMayBeTheVenueSoNothingMoves()
    {
        Toggle(Entry(Tavern, ["https://tavern.carrd.co"], Ad("a")), EntryMark.Favourite);

        var matched = Match(Entry(NewHouse, ["https://tavern.carrd.co"], Ad("b")), Entry(Tavern, Ad("c")));

        Assert.Null(matched[0]);
        Assert.Equal(Tavern, matched[1]!.Address);
    }

    [Theory]
    [InlineData("https://discord.gg/tavern")]
    [InlineData("https://twitch.tv/dj_somebody")]
    [InlineData("https://prtk.gg/e/123")]
    public void ADiscordInviteOrAPersonsOrEventsLinkMovesNothing(string link)
    {
        Toggle(Entry(Tavern, [link], Ad("a")), EntryMark.Favourite);

        var matched = Match(Entry(NewHouse, [link], Ad("b")));

        Assert.Null(matched[0]);
        Assert.NotNull(cache.PlaceAt(Tavern));
    }

    [Fact]
    public void AMoveOntoAMarkedAddressMergesTheMarks()
    {
        var venue = Venue();
        Toggle(Entry(Tavern, venue), EntryMark.Favourite);
        Toggle(Entry(NewHouse, Ad("there")), EntryMark.Hidden);

        var matched = Match(Entry(NewHouse, venue));

        Assert.True(matched[0]!.Favourite && matched[0]!.Hidden);
        Assert.Single(cache.Places);
    }

    [Fact]
    public void ListingMarksSetBeforePlacesMoveToThePlaceExceptAnEventsVisited()
    {
        var ad = Ad("a");
        ad.Favourite = true;
        var gala = Event();
        gala.Visited = true;
        gala.Hidden = true;

        var place = Match(Entry(Tavern, ad, gala))[0];

        Assert.True(place!.Favourite && place.Hidden);
        Assert.False(place.Visited);
        Assert.False(ad.Favourite || gala.Hidden);
        Assert.True(gala.Visited);
    }

    [Fact]
    public void AnyMarkedListingShowsTheMark()
    {
        var b = Ad("b");
        b.Favourite = true;

        Assert.True(EntryMarks.Has(Entry(null, Ad("a"), b), null, EntryMark.Favourite));
    }

    [Fact]
    public void ToggleSaysWhetherTheEntryShowsTheMarkAfterwards()
    {
        var entry = Entry(Tavern, Ad("a"));
        PlaceMark? place = null;

        Assert.True(EntryMarks.Toggle(cache, entry, ref place, EntryMark.Visited));
        Assert.False(EntryMarks.Toggle(cache, entry, ref place, EntryMark.Visited));
    }

    [Fact]
    public void MarkingAnEntryWhosePlaceIsAtItsSecondAddressMarksThatPlace()
    {
        var there = cache.Place(Elsewhere);
        there.Visited = true;
        var entry = new MarkEntry([Ad("a")], [Tavern, Elsewhere], "Tavern", "", []);
        var place = Match(entry)[0];

        Assert.Same(there, place);
        Assert.Same(there, Toggle(entry, EntryMark.Favourite, place));
        Assert.Null(cache.PlaceAt(Tavern));
    }

    [Fact]
    public void AnEntryWithTwoMarkedAddressesTakesTheFirsts()
    {
        cache.Place(Tavern).Favourite = true;
        cache.Place(Elsewhere).Hidden = true;

        var place = Match(new MarkEntry([Ad("a")], [Tavern, Elsewhere], "Tavern", "", []))[0];

        Assert.Equal(Tavern, place!.Address);
    }

    [Fact]
    public void MarkingAgainTakesTheEntrysTitleAndAddress()
    {
        var place = Toggle(Entry(Tavern, Ad("a")), EntryMark.Visited);
        Toggle(new MarkEntry([Ad("b")], [Tavern], "Renamed", "Siren, Empyreum, W16, P20 (renamed)", []), EntryMark.Favourite, place);

        Assert.Equal("Renamed", place!.Title);
        Assert.Equal("Siren, Empyreum, W16, P20 (renamed)", place.ShownAddress);
    }

    [Fact]
    public void MatchingFillsInATitleButDoesNotChangeOne()
    {
        var place = cache.Place(Tavern);
        place.Favourite = true;
        Assert.Equal("", place.Title);
        Assert.Equal("", place.ShownAddress);

        Match(Entry(Tavern, Ad("a")));
        Match(new MarkEntry([Ad("b")], [Tavern], "Another night", "other", []));

        Assert.Equal("Tavern", place.Title);
        Assert.Equal("Siren, Empyreum, W16, P20", place.ShownAddress);
    }

    [Fact]
    public void AMovedPlaceTakesTheTitleWhereItMovedTo()
    {
        var venue = Venue();
        var place = Toggle(Entry(Tavern, venue), EntryMark.Favourite);

        Match(new MarkEntry([venue], [NewHouse], "Tavern by the Sea", "Siren, Mist, W3, P7", []));

        Assert.Equal("Tavern by the Sea", place!.Title);
        Assert.Equal("Siren, Mist, W3, P7", place.ShownAddress);
    }

    [Fact]
    public void AVenueEntryWithNoPlotOrAnUnknownVenueMovesNothing()
    {
        var venue = Venue();
        Toggle(Entry(Tavern, venue), EntryMark.Favourite);

        var matched = Match(Entry(null, venue), Entry(NewHouse, Venue("unknown")));

        Assert.Equal([null, null], matched);
        Assert.NotNull(cache.PlaceAt(Tavern));
    }

    [Fact]
    public void AnEntryHoldingTwoMarkedVenuesMovesOnlyTheFirst()
    {
        var first = Venue("1");
        var second = Venue("2");
        Toggle(Entry(Tavern, first), EntryMark.Favourite);
        Toggle(Entry(Elsewhere, second), EntryMark.Hidden);

        var place = Match(Entry(NewHouse, first, second))[0];

        Assert.True(place!.Favourite);
        Assert.False(place.Hidden);
        Assert.True(cache.PlaceAt(Elsewhere)!.Hidden);
    }

    [Fact]
    public void AnEntryWithItsOwnPlaceNeverTakesAnothersByWebsite()
    {
        Toggle(Entry(Tavern, ["https://tavern.carrd.co"], Ad("a")), EntryMark.Favourite);
        cache.Place(NewHouse).Visited = true;

        var place = Match(Entry(NewHouse, ["https://tavern.carrd.co"], Ad("b")))[0];

        Assert.Equal(NewHouse, place!.Address);
        Assert.False(place.Favourite);
        Assert.NotNull(cache.PlaceAt(Tavern));
    }

    [Fact]
    public void AnEntryShowingTwoMarkedWebsitesTakesTheFirstsPlace()
    {
        Toggle(Entry(Tavern, ["https://tavern.carrd.co"], Ad("a")), EntryMark.Favourite);
        Toggle(Entry(Elsewhere, ["https://other.carrd.co"], Ad("b")), EntryMark.Hidden);

        var place = Match(Entry(NewHouse, ["https://tavern.carrd.co", "https://other.carrd.co"], Ad("c")))[0];

        Assert.True(place!.Favourite);
        Assert.False(place.Hidden);
        Assert.NotNull(cache.PlaceAt(Elsewhere));
    }

    [Theory]
    [InlineData("https://twitch.tv/dj")]
    [InlineData("https://m.twitch.tv/dj")]
    [InlineData("https://kick.com/dj")]
    [InlineData("https://youtube.com/@dj")]
    [InlineData("https://youtu.be/abc")]
    [InlineData("https://x.com/dj")]
    [InlineData("https://twitter.com/dj")]
    [InlineData("https://instagram.com/dj")]
    [InlineData("https://tiktok.com/@dj")]
    [InlineData("https://bsky.app/profile/dj")]
    [InlineData("https://soundcloud.com/dj")]
    [InlineData("https://mixcloud.com/dj")]
    [InlineData("https://partake.gg/events/1")]
    [InlineData("https://prtk.gg/e/1")]
    public void LinksToPeopleAndEventsAreNotWebsites(string link)
    {
        Assert.Null(PlaceMarks.WebsiteKey(new Uri(link)));
    }

    [Fact]
    public void AHostThatOnlyEndsLikeAnExcludedOneIsAWebsite()
    {
        Assert.Equal("fantwitch.tv/club", PlaceMarks.WebsiteKey(new Uri("https://FanTwitch.tv/Club/")));
    }

    [Fact]
    public void MatchingSavesOnlyWhenItLearnsSomething()
    {
        var venue = Venue();
        var place = Toggle(Entry(Tavern, ["https://tavern.carrd.co"], venue), EntryMark.Favourite);
        cache.TakeChanges();

        Match(Entry(Tavern, ["https://tavern.carrd.co"], venue));
        Assert.False(cache.MarksUnsaved);

        place!.Favourite = true;
        Assert.False(cache.MarksUnsaved);

        foreach (var (links, members) in new (string[], CachedListing[])[]
        {
            (["https://discord.gg/tavern"], [venue]),
            (["https://tavern.example"], [venue]),
            ([], [venue, Venue("second")]),
        })
        {
            Match(Entry(Tavern, links, members));
            Assert.True(cache.MarksUnsaved);
            cache.TakeChanges();
        }
    }

    [Theory]
    [InlineData("", "Siren, Empyreum, W16, P20", true)]
    [InlineData("Tavern", "", true)]
    [InlineData("", "", false)]
    public void FillingInATitleOrAddressIsSaved(string title, string shown, bool unsaved)
    {
        cache.Place(Tavern).Favourite = true;
        cache.TakeChanges();

        Match(new MarkEntry([Ad("a")], [Tavern], title, shown, []));

        Assert.Equal(unsaved, cache.MarksUnsaved);
    }

    [Fact]
    public void ATitleAndAVenueLearnedTogetherAreSaved()
    {
        var place = cache.Place(Tavern);
        place.Favourite = true;
        cache.TakeChanges();

        Match(Entry(Tavern, Venue()));

        Assert.True(cache.MarksUnsaved);
        Assert.Contains("v", place.VenueIds);
    }

    [Fact]
    public void ChangingAPlacesMarkIsSaved()
    {
        var place = cache.Place(Tavern);
        cache.TakeChanges();

        place.Hidden = true;

        Assert.True(cache.MarksUnsaved);
    }

    [Fact]
    public void MarkedListingsAreAdoptedWhenAnyIsMarkedAndNotWithoutAPlot()
    {
        var ward = Ad("ward");
        ward.Hidden = true;
        var visited = Ad("visited");
        visited.Visited = true;

        var matched = Match(Entry(null, ward), Entry(Tavern, Ad("unmarked"), visited));

        Assert.Null(matched[0]);
        Assert.True(ward.Hidden);
        Assert.True(matched[1]!.Visited);
        Assert.False(visited.Visited);
    }

    // Saving

    [Fact]
    public void PlacesAndWhatTheySawSurviveASaveAndReload()
    {
        var venue = Venue();
        Toggle(Entry(Tavern, ["https://tavern.carrd.co"], venue), EntryMark.Favourite);
        Toggle(Entry(Elsewhere, Ad("x")), EntryMark.Hidden);

        var back = SavedFiles.RoundTrip(cache);

        Assert.True(back.PlaceAt(Tavern)!.Favourite);
        Assert.Equal("Tavern", back.PlaceAt(Tavern)!.Title);
        Assert.True(back.PlaceAt(Elsewhere)!.Hidden);
        // Moves still work after a reload, so the IDs and websites were saved too
        Assert.Equal(NewHouse, PlaceMarks.Match(back, [Entry(NewHouse, back.Find(ListingSource.FfxivVenues, "v")!)])[0]!.Address);
        var again = SavedFiles.RoundTrip(cache);
        Assert.Equal(NewHouse, PlaceMarks.Match(again, [Entry(NewHouse, ["https://tavern.carrd.co"], Ad("y"))])[0]!.Address);
    }

    [Fact]
    public void APlaceWithNoMarkLeftIsNotSaved()
    {
        var entry = Entry(Tavern, Ad("a"));
        var place = Toggle(entry, EntryMark.Favourite);
        Toggle(entry, EntryMark.Favourite, place);

        var back = SavedFiles.RoundTrip(cache);

        Assert.Empty(back.Places);
    }
}
