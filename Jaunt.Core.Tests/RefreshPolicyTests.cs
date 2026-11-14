using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class RefreshPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ListingSource.Xivpf, 10, 10)]
    [InlineData(ListingSource.Xivpf, 45, 45)]
    [InlineData(ListingSource.FfxivVenues, 10, 480)]
    [InlineData(ListingSource.Partake, 10, 480)]
    [InlineData(ListingSource.RpEventCalendar, 10, 480)]
    [InlineData(ListingSource.RpEventCalendar, 479, 480)]
    [InlineData(ListingSource.RpEventCalendar, 600, 600)]
    public void CooldownIsTheSettingOrTheSourceMinimumWhicheverIsLonger(ListingSource source, int setting, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), RefreshPolicy.Cooldown(source, setting));
    }

    [Fact]
    public void TheInGamePartyFinderHasNoCooldownAndIsNeverFetched()
    {
        Assert.Null(RefreshPolicy.Cooldown(ListingSource.InGamePartyFinder, 10));
        Assert.False(RefreshPolicy.ShouldFetch(ListingSource.InGamePartyFinder, 10, null, Now));
    }

    [Fact]
    public void ASourceNeverFetchedIsFetched() => Assert.True(RefreshPolicy.ShouldFetch(ListingSource.Xivpf, 10, null, Now));

    [Theory]
    [InlineData(9, false)]
    [InlineData(0, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void ASourceIsFetchedOnceItsCooldownHasPassed(int minutesAgo, bool fetch)
    {
        Assert.Equal(fetch, RefreshPolicy.ShouldFetch(ListingSource.Xivpf, 10, Now.AddMinutes(-minutesAgo), Now));
    }

    [Fact]
    public void AFetchTimeInTheFutureMeansTheClockMovedBackSoItFetches()
    {
        Assert.True(RefreshPolicy.ShouldFetch(ListingSource.FfxivVenues, 10, Now.AddMinutes(1), Now));
    }

    [Fact]
    public void TheSourceMinimumHoldsEvenWhenTheSettingIsShorter()
    {
        Assert.False(RefreshPolicy.ShouldFetch(ListingSource.FfxivVenues, 10, Now.AddHours(-7), Now));
        Assert.True(RefreshPolicy.ShouldFetch(ListingSource.FfxivVenues, 10, Now.AddHours(-8), Now));
    }

    [Fact]
    public void EveryFetchedSourceHasAMinimum()
    {
        Assert.All(RefreshPolicy.FetchedSources, source => Assert.NotNull(RefreshPolicy.Minimum(source)));
        Assert.DoesNotContain(ListingSource.InGamePartyFinder, RefreshPolicy.FetchedSources);
    }
}
