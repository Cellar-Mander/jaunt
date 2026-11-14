using System.Text.Json;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class ListingCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SeeingAListingAgainUpdatesItInsteadOfAddingOne()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "Old title", Now.AddDays(-2));

        var listing = cache.Seen(ListingSource.Xivpf, "1", "New title", Now);

        Assert.Equal(1, cache.Count);
        Assert.Equal("New title", listing.Title);
        Assert.Equal(Now, listing.LastSeen);
    }

    [Fact]
    public void TheSameIdFromAnotherSourceIsAnotherListing()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "A", Now);
        cache.Seen(ListingSource.InGamePartyFinder, "1", "B", Now);

        Assert.Equal(2, cache.Count);
        Assert.Equal("B", cache.Find(ListingSource.InGamePartyFinder, "1")!.Title);
    }

    [Fact]
    public void AnOlderSightingDoesNotMoveLastSeenBack()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "A", Now);

        var listing = cache.Seen(ListingSource.Xivpf, "1", "A", Now.AddHours(-1));

        Assert.Equal(Now, listing.LastSeen);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void ClearRemovesOnlyUnmarkedListings(bool favourite, bool visited, bool hidden, bool removed)
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.Xivpf, "1", "A", Now);
        listing.Favourite = favourite;
        listing.Visited = visited;
        listing.Hidden = hidden;

        var count = cache.ClearUnmarked();

        Assert.Equal(removed ? 1 : 0, count);
        Assert.Equal(removed, cache.Find(ListingSource.Xivpf, "1") is null);
    }

    [Fact]
    public void ClearSaysHowManyItRemoved()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "A", Now);
        cache.Seen(ListingSource.Xivpf, "2", "B", Now);
        cache.Seen(ListingSource.Xivpf, "3", "C", Now).Hidden = true;

        Assert.Equal(2, cache.ClearUnmarked());
        Assert.Equal(1, cache.Count);
    }

    [Theory]
    // One calendar month: 14 Oct 20:00 expires at 14 Nov 20:00
    [InlineData("2026-10-14T20:00:00Z", true)]
    [InlineData("2026-10-14T20:00:01Z", false)]
    [InlineData("2026-09-01T00:00:00Z", true)]
    [InlineData("2026-11-13T00:00:00Z", false)]
    public void UnmarkedListingsExpireAMonthAfterTheyWereLastSeen(string lastSeen, bool expired)
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "A", DateTimeOffset.Parse(lastSeen, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expired ? 1 : 0, cache.RemoveExpired(Now));
        Assert.Equal(expired ? 0 : 1, cache.Count);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void MarkedListingsNeverExpire(bool favourite, bool visited, bool hidden)
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.Xivpf, "1", "A", Now.AddYears(-1));
        listing.Favourite = favourite;
        listing.Visited = visited;
        listing.Hidden = hidden;

        Assert.Equal(0, cache.RemoveExpired(Now));
        Assert.Equal(1, cache.Count);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void MarkedOneOffsExpireAMonthAfterTheyWereLastSeen(bool favourite, bool visited, bool hidden)
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.Partake, "1", "Gala", Now.AddMonths(-1), oneOff: true);
        listing.Favourite = favourite;
        listing.Visited = visited;
        listing.Hidden = hidden;

        Assert.Equal(1, cache.RemoveExpired(Now));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void AMarkedOneOffSeenWithinTheMonthIsKept()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "Gala", Now.AddMonths(-1).AddSeconds(1), oneOff: true).Favourite = true;

        Assert.Equal(0, cache.RemoveExpired(Now));
    }

    [Fact]
    public void ClearKeepsMarkedOneOffs()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "Gala", Now, oneOff: true).Visited = true;

        Assert.Equal(0, cache.ClearUnmarked());
    }

    [Fact]
    public void TheLatestSightingSaysWhetherItIsAOneOff()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "Gala", Now.AddDays(-1), oneOff: true);

        Assert.False(cache.Seen(ListingSource.Partake, "1", "Gala", Now).OneOff);
    }

    [Fact]
    public void HiddenListingsAreListedByTitle()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "zebra lounge", Now).Hidden = true;
        cache.Seen(ListingSource.Xivpf, "2", "Not hidden", Now);
        cache.Seen(ListingSource.FfxivVenues, "3", "Amber Room", Now).Hidden = true;

        var hidden = cache.HiddenListings();

        Assert.Equal(["Amber Room", "zebra lounge"], hidden.Select(l => l.Title));
    }

    [Fact]
    public void SavedCacheLoadsBackWithItsMarks()
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.Partake, "42", "ラベンダー Café", Now, oneOff: true);
        listing.Favourite = true;
        listing.Visited = true;
        cache.Seen(ListingSource.Xivpf, "7", "Hidden one", Now.AddDays(-3)).Hidden = true;

        var loaded = ListingCache.FromJson(cache.ToJson());

        Assert.Equal(2, loaded.Count);
        var back = loaded.Find(ListingSource.Partake, "42")!;
        Assert.Equal("ラベンダー Café", back.Title);
        Assert.Equal(Now, back.LastSeen);
        Assert.True(back.OneOff);
        Assert.False(loaded.Find(ListingSource.Xivpf, "7")!.OneOff);
        Assert.True(back.Favourite);
        Assert.True(back.Visited);
        Assert.False(back.Hidden);
        Assert.True(loaded.Find(ListingSource.Xivpf, "7")!.Hidden);
    }

    [Fact]
    public void ASourceNeverFetchedHasNoFetchTime() => Assert.Null(new ListingCache().LastFetched(ListingSource.Xivpf));

    [Fact]
    public void TheLatestFetchIsRecordedPerSource()
    {
        var cache = new ListingCache();
        cache.Fetched(ListingSource.Xivpf, Now.AddHours(-1));
        cache.Fetched(ListingSource.Xivpf, Now);
        cache.Fetched(ListingSource.Partake, Now.AddHours(-3));

        Assert.Equal(Now, cache.LastFetched(ListingSource.Xivpf));
        Assert.Equal(Now.AddHours(-3), cache.LastFetched(ListingSource.Partake));
        Assert.Null(cache.LastFetched(ListingSource.FfxivVenues));
    }

    [Fact]
    public void TheInGamePartyFinderCannotBeRecordedAsFetched() =>
        Assert.Throws<ArgumentException>(() => new ListingCache().Fetched(ListingSource.InGamePartyFinder, Now));

    [Fact]
    public void ShouldFetchUsesTheRecordedTime()
    {
        var cache = new ListingCache();
        Assert.True(cache.ShouldFetch(ListingSource.FfxivVenues, 10, Now));

        cache.Fetched(ListingSource.FfxivVenues, Now.AddHours(-7));

        Assert.False(cache.ShouldFetch(ListingSource.FfxivVenues, 10, Now));
        Assert.True(cache.ShouldFetch(ListingSource.FfxivVenues, 10, Now.AddHours(1)));
    }

    [Fact]
    public void ClearingListingsKeepsFetchTimes()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "A", Now);
        cache.Fetched(ListingSource.FfxivVenues, Now);

        cache.ClearUnmarked();
        cache.RemoveExpired(Now.AddYears(1));

        Assert.Equal(Now, cache.LastFetched(ListingSource.FfxivVenues));
        Assert.False(cache.ShouldFetch(ListingSource.FfxivVenues, 10, Now.AddHours(1)));
    }

    [Fact]
    public void FetchTimesLoadBack()
    {
        var cache = new ListingCache();
        cache.Fetched(ListingSource.Xivpf, Now);
        cache.Fetched(ListingSource.RpEventCalendar, Now.AddHours(-2));

        var loaded = ListingCache.FromJson(cache.ToJson());

        Assert.Equal(Now, loaded.LastFetched(ListingSource.Xivpf));
        Assert.Equal(Now.AddHours(-2), loaded.LastFetched(ListingSource.RpEventCalendar));
        Assert.Null(loaded.LastFetched(ListingSource.Partake));
    }

    [Fact]
    public void FetchTimesAreSavedBySourceName()
    {
        var cache = new ListingCache();
        cache.Fetched(ListingSource.Partake, Now);

        Assert.Contains("\"Partake\":", cache.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingDropsFetchTimesForUnknownOrNeverFetchedSources()
    {
        const string json = """
            {"lastFetched": {
              "Xivpf": "2026-11-14T20:00:00+00:00",
              "NotASource": "2026-11-14T20:00:00+00:00",
              "InGamePartyFinder": "2026-11-14T20:00:00+00:00",
              "7": "2026-11-14T20:00:00+00:00",
              "xivpf": "2026-11-13T20:00:00+00:00"
            }}
            """;

        var loaded = ListingCache.FromJson(json);

        Assert.Equal(Now, loaded.LastFetched(ListingSource.Xivpf));
        Assert.Null(loaded.LastFetched(ListingSource.InGamePartyFinder));
        Assert.Equal(0, loaded.Count);
    }

    [Fact]
    public void LoadingAMinimalFileGivesAnEmptyCache()
    {
        var loaded = ListingCache.FromJson("{}");

        Assert.Equal(0, loaded.Count);
        Assert.Null(loaded.LastFetched(ListingSource.Xivpf));
    }

    [Fact]
    public void LoadingDropsEntriesWithoutAnIdAndKeepsTheLastRepeat()
    {
        const string json = """
            {"listings": [
              {"source": 1, "id": "", "title": "no id"},
              null,
              {"source": 1, "id": "5", "title": "first"},
              {"source": 1, "id": "5", "title": "second"}
            ]}
            """;

        var loaded = ListingCache.FromJson(json);

        Assert.Equal(1, loaded.Count);
        Assert.Equal("second", loaded.Find(ListingSource.Xivpf, "5")!.Title);
    }

    [Theory]
    [InlineData("""{"listings": [{"source": 1, "id": "5"}]}""")]
    [InlineData("""{"listings": [{"source": 1, "id": "5", "title": null}]}""")]
    public void AMissingOrNullTitleLoadsAsEmpty(string json)
    {
        Assert.Equal("", ListingCache.FromJson(json).Find(ListingSource.Xivpf, "5")!.Title);
    }

    [Theory]
    [InlineData("""{"listings": [{"source": 0, "id": "5"}]}""")]
    [InlineData("""{"listings": [{"source": 0, "id": "5", "description": null}]}""")]
    public void AMissingOrNullDescriptionLoadsAsEmpty(string json)
    {
        var listing = ListingCache.FromJson(json).Find(ListingSource.InGamePartyFinder, "5")!;
        Assert.Equal("", listing.Description);
        Assert.Null(listing.ListedOn);
    }

    [Fact]
    public void AdTextAndListingWorldSurviveSaving()
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.InGamePartyFinder, "123", "Café", Now);
        listing.Description = "Café  Siren Mist W5 P12";
        listing.ListedOn = "Siren";

        var back = ListingCache.FromJson(cache.ToJson()).Find(ListingSource.InGamePartyFinder, "123")!;

        Assert.Equal("Café  Siren Mist W5 P12", back.Description);
        Assert.Equal("Siren", back.ListedOn);
    }

    [Fact]
    public void LoadingNullGivesAnEmptyCache() => Assert.Equal(0, ListingCache.FromJson("null").Count);

    [Fact]
    public void LoadingSomethingElseThrows() => Assert.ThrowsAny<JsonException>(() => ListingCache.FromJson("[1, 2]"));
}
