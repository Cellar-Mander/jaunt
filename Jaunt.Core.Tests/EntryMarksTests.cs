using Jaunt.Core.Browser;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class EntryMarksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private readonly ListingCache cache = new();

    private CachedListing Venue(string id = "v") => cache.Seen(ListingSource.FfxivVenues, id, "Lounge", Now);

    private CachedListing Event(ListingSource source = ListingSource.Partake, string id = "e") => cache.Seen(source, id, "Gala", Now, oneOff: true);

    private CachedListing Ad(string id) => cache.SeenAd(ListingSource.InGamePartyFinder, id, "Lounge Mist W5 P12", null, Now);

    [Theory]
    [InlineData(EntryMark.Favourite, ListingSource.FfxivVenues)]
    [InlineData(EntryMark.Hidden, ListingSource.FfxivVenues)]
    [InlineData(EntryMark.Visited, ListingSource.Partake)]
    public void AMarkGoesOnTheVenueOrTheEventAsTheButtonSays(EntryMark mark, ListingSource expected)
    {
        // Title first, as the browser orders members; the ad must never carry it
        IReadOnlyList<CachedListing> members = [Venue(), Event(), Ad("a")];

        var target = Assert.Single(EntryMarks.Targets(members, mark));

        Assert.Equal(expected, target.Source);
    }

    [Theory]
    [InlineData(EntryMark.Favourite)]
    [InlineData(EntryMark.Hidden)]
    public void FavouriteAndHideFallBackToTheEventWithoutAVenue(EntryMark mark)
    {
        var calendar = Event(ListingSource.RpEventCalendar);

        Assert.Same(calendar, Assert.Single(EntryMarks.Targets([Ad("a"), calendar], mark)));
    }

    [Fact]
    public void VisitedFallsBackToTheVenueWithoutAnEvent()
    {
        var venue = Venue();

        Assert.Same(venue, Assert.Single(EntryMarks.Targets([Ad("a"), venue], EntryMark.Visited)));
    }

    [Fact]
    public void AnEntryOfOnlyAdsMarksEveryAd()
    {
        IReadOnlyList<CachedListing> members = [Ad("a"), Ad("b")];

        Assert.True(EntryMarks.Toggle(members, EntryMark.Favourite));

        Assert.All(members, m => Assert.True(m.Favourite));
    }

    [Fact]
    public void TogglingSetsOnlyThatMarkOnTheTarget()
    {
        var venue = Venue();
        var gala = Event();

        Assert.True(EntryMarks.Toggle([venue, gala], EntryMark.Visited));

        Assert.True(gala.Visited);
        Assert.False(venue.Visited);
        Assert.False(gala.Favourite || gala.Hidden);
        Assert.True(EntryMarks.Has([venue, gala], EntryMark.Visited));
        Assert.False(EntryMarks.Has([venue, gala], EntryMark.Favourite));
    }

    [Fact]
    public void TogglingAShownMarkClearsItFromEveryMemberWhoeverSetIt()
    {
        // The ad was starred alone, then merged with the venue, which would be the target now
        var ad = Ad("a");
        ad.Favourite = true;
        var venue = Venue();
        venue.Visited = true;

        Assert.True(EntryMarks.Has([venue, ad], EntryMark.Favourite));
        Assert.False(EntryMarks.Toggle([venue, ad], EntryMark.Favourite));

        Assert.False(ad.Favourite);
        Assert.False(venue.Favourite);
        Assert.True(venue.Visited);
    }

    [Fact]
    public void MarksSetFromTheBrowserSurviveASaveAndReload()
    {
        var venue = Venue();
        var gala = Event();
        EntryMarks.Toggle([venue, gala], EntryMark.Favourite);
        EntryMarks.Toggle([venue, gala], EntryMark.Visited);
        EntryMarks.Toggle([Venue("other")], EntryMark.Hidden);

        var back = SavedFiles.RoundTrip(cache);

        Assert.True(back.Find(ListingSource.FfxivVenues, "v")!.Favourite);
        Assert.True(back.Find(ListingSource.Partake, "e")!.Visited);
        Assert.True(back.Find(ListingSource.FfxivVenues, "other")!.Hidden);
        Assert.False(back.Find(ListingSource.FfxivVenues, "v")!.Visited);
    }
}
