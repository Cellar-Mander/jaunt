using Jaunt.Core.Browser;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class PartyFinderAdsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    private const int Cooldown = 30;

    private static CachedListing Ad(ListingSource source, string id, double minutesAgo, string? world = "Cerberus") =>
        new() { Source = source, Id = id, LastSeen = Now.AddMinutes(-minutesAgo), ListedOn = world };

    private static PartyFinderAd? Choose(string? currentDataCentre, params CachedListing[] listings) =>
        PartyFinderAds.Choose(listings, Now, Cooldown, TestGazetteer.Instance, currentDataCentre);

    [Fact]
    public void AnInGameAdSeenRecentlyOpensByItsId()
    {
        var ad = Choose("Chaos", Ad(ListingSource.InGamePartyFinder, "12345", 10));
        Assert.Equal(new PartyFinderAd(12345, PartyFinderState.Openable, Now.AddMinutes(-10), "Chaos"), ad);
    }

    [Fact]
    public void AnXivpfAdTheGameNeverSentOpensByTheSameId()
    {
        var ad = Choose("Chaos", Ad(ListingSource.Xivpf, "987654321", 80));
        Assert.Equal(987654321UL, ad?.ListingId);
        Assert.Equal(PartyFinderState.Openable, ad?.State);
    }

    [Fact]
    public void AnAdNotSeenForAnHourIsNoLongerListed()
    {
        Assert.Equal(PartyFinderState.Openable, Choose("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 59))?.State);
        Assert.Equal(PartyFinderState.NotListed, Choose("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 60))?.State);
    }

    [Fact]
    public void AnXivpfAdStaysListedForTheCooldownLonger()
    {
        Assert.Equal(PartyFinderState.Openable, Choose("Chaos", Ad(ListingSource.Xivpf, "1", 89))?.State);
        Assert.Equal(PartyFinderState.NotListed, Choose("Chaos", Ad(ListingSource.Xivpf, "1", 90))?.State);
    }

    [Fact]
    public void AnAdOnAnotherDataCentreCantBeOpened()
    {
        var ad = Choose("Light", Ad(ListingSource.InGamePartyFinder, "1", 10));
        Assert.Equal(PartyFinderState.OtherDataCentre, ad?.State);
        Assert.Equal("Chaos", ad?.DataCentre);
    }

    [Theory]
    [InlineData("chaos ")]
    [InlineData(null)]
    public void TheDataCentreMatchesIgnoringCaseAndCountsAsAnyWhenUnknown(string? current) =>
        Assert.Equal(PartyFinderState.Openable, Choose(current, Ad(ListingSource.InGamePartyFinder, "1", 10))?.State);

    [Theory]
    [InlineData(null)]
    [InlineData("Nowhere")]
    public void AnAdOnAnUnknownWorldCountsAsOnAnyDataCentre(string? world)
    {
        var ad = Choose("Light", Ad(ListingSource.InGamePartyFinder, "1", 10, world));
        Assert.Equal(PartyFinderState.Openable, ad?.State);
        Assert.Null(ad?.DataCentre);
    }

    [Fact]
    public void ANotListedAdSaysSoEvenOnAnotherDataCentre() =>
        Assert.Equal(PartyFinderState.NotListed, Choose("Light", Ad(ListingSource.InGamePartyFinder, "1", 120))?.State);

    [Fact]
    public void TheAdListedLongestIsOpened()
    {
        // The xivpf copy, seen 40 minutes ago, is listed until 50 minutes from now; the in-game one, seen 20 minutes ago, until 40
        var ad = Choose("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 20), Ad(ListingSource.Xivpf, "2", 40), Ad(ListingSource.InGamePartyFinder, "3", 30));
        Assert.Equal(2UL, ad?.ListingId);
    }

    [Fact]
    public void OnATieTheFirstIsOpened() =>
        Assert.Equal(1UL, Choose("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 20), Ad(ListingSource.InGamePartyFinder, "2", 20))?.ListingId);

    [Fact]
    public void OtherSourcesAndIdsThatArentNumbersHaveNoAd()
    {
        Assert.Null(Choose("Chaos"));
        Assert.Null(Choose("Chaos", Ad(ListingSource.Shout, "1", 1), Ad(ListingSource.Partake, "2", 1)));
        Assert.Null(Choose("Chaos", Ad(ListingSource.Xivpf, "-1", 1), Ad(ListingSource.Xivpf, "1 ", 1), Ad(ListingSource.Xivpf, "", 1)));
    }

    [Fact]
    public void AnAdWithABadIdIsSkippedForOneWithAGoodOne() =>
        Assert.Equal(7UL, Choose("Chaos", Ad(ListingSource.Xivpf, "x", 1), Ad(ListingSource.InGamePartyFinder, "7", 50))?.ListingId);
}
