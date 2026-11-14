using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

/// <summary>
/// Fixtures/smoke-test-cache holds the sample cache for the in-game Settings checks in
/// docs/smoke-test.md. These tests hold it to what that checklist expects, so a change to the cache
/// format or rules can't quietly break the checklist.
/// </summary>
public class SmokeTestListingsTests
{
    // Any day from now until 2099 gives the same result: the sample's dates are 2000 or 2099
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    // As the plugin does on load: read the files, then drop expired listings
    private static (ListingCache Cache, int Expired) LoadAsPlugin()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "smoke-test-cache");
        var unreadable = new List<string>();
        var cache = ListingCache.Load(
            path => File.Exists(Path.Combine(folder, path)) ? File.ReadAllText(Path.Combine(folder, path)) : null,
            (path, _) => unreadable.Add(path));
        Assert.Empty(unreadable);
        return (cache, cache.RemoveExpired(Now));
    }

    [Fact]
    public void LoadingDropsTheOldUnmarkedListingAndTheHiddenOneOff()
    {
        var (cache, expired) = LoadAsPlugin();

        Assert.Equal(2, expired);
        Assert.Null(cache.Find(ListingSource.Xivpf, "1006"));
        Assert.Null(cache.Find(ListingSource.Partake, "p-5"));
        Assert.NotNull(cache.Find(ListingSource.Xivpf, "1003")); // old, but a favourite and recurring
        Assert.Equal(6, cache.Count);
    }

    [Fact]
    public void HiddenVenuesListsTwo() =>
        Assert.Equal(["Another hidden", "Hidden lounge"], LoadAsPlugin().Cache.HiddenListings().Select(l => l.Title));

    [Fact]
    public void ClearRemovesTwo()
    {
        var (cache, _) = LoadAsPlugin();

        Assert.Equal(2, cache.ClearUnmarked());
        Assert.Equal(["Another hidden", "Favourite café", "Hidden lounge", "Visited night"], cache.Listings.Select(l => l.Title).Order());
    }

    [Fact]
    public void AfterUnhidingOneClearRemovesIt()
    {
        var (cache, _) = LoadAsPlugin();
        cache.ClearUnmarked();

        cache.Find(ListingSource.FfxivVenues, "v-8")!.Hidden = false;

        Assert.Equal(["Hidden lounge"], cache.HiddenListings().Select(l => l.Title));
        Assert.Equal(1, cache.ClearUnmarked());
    }

    [Fact]
    public void TheFetchTimeSurvivesClear()
    {
        var (cache, _) = LoadAsPlugin();
        cache.ChangedAll();
        cache.ClearUnmarked();

        Assert.Contains("\"FfxivVenues\":", SavedFiles.Written(cache)[ListingCache.FetchTimesFile], StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarksAndTitlesSurviveSavingAndLoading()
    {
        var (cache, _) = LoadAsPlugin();
        cache.ClearUnmarked();
        cache.Find(ListingSource.FfxivVenues, "v-8")!.Hidden = false;

        var saved = SavedFiles.Load(SavedFiles.All(cache));

        Assert.Equal(["Another hidden", "Favourite café", "Hidden lounge", "Visited night"], saved.Listings.Select(l => l.Title).Order());
        Assert.Equal(["Hidden lounge"], saved.HiddenListings().Select(l => l.Title));
        Assert.Equal(1, saved.ClearUnmarked());
    }
}
