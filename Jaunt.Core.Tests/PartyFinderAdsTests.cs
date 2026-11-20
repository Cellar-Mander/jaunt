using Jaunt.Core.Browser;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class PartyFinderAdsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    private const int Cooldown = 30;

    private static CachedListing Ad(ListingSource source, string id, double minutesAgo, string? world = "Cerberus") =>
        new() { Source = source, Id = id, LastSeen = Now.AddMinutes(-minutesAgo), ListedOn = world };

    private static ulong? Openable(string? currentDataCentre, params CachedListing[] listings) =>
        PartyFinderAds.Openable(listings, Now, Cooldown, TestGazetteer.Instance, currentDataCentre);

    [Fact]
    public void AnInGameAdSeenRecentlyOpensByItsId() =>
        Assert.Equal(12345UL, Openable("Chaos", Ad(ListingSource.InGamePartyFinder, "12345", 10)));

    [Fact]
    public void AnXivpfAdTheGameNeverSentOpensByTheSameId() =>
        Assert.Equal(987654321UL, Openable("Chaos", Ad(ListingSource.Xivpf, "987654321", 80)));

    [Fact]
    public void AnAdNotSeenForAnHourIsNoLongerListed()
    {
        Assert.Equal(1UL, Openable("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 59)));
        Assert.Null(Openable("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 60)));
    }

    [Fact]
    public void AnXivpfAdStaysListedForTheCooldownLonger()
    {
        Assert.Equal(1UL, Openable("Chaos", Ad(ListingSource.Xivpf, "1", 89)));
        Assert.Null(Openable("Chaos", Ad(ListingSource.Xivpf, "1", 90)));
    }

    [Fact]
    public void AnAdOnAnotherDataCentreCantBeOpened() =>
        Assert.Null(Openable("Light", Ad(ListingSource.InGamePartyFinder, "1", 10)));

    [Theory]
    [InlineData("chaos ")]
    [InlineData(null)]
    public void TheDataCentreMatchesIgnoringCaseAndCountsAsAnyWhenUnknown(string? current) =>
        Assert.Equal(1UL, Openable(current, Ad(ListingSource.InGamePartyFinder, "1", 10)));

    [Theory]
    [InlineData(null)]
    [InlineData("Nowhere")]
    public void AnAdOnAnUnknownWorldCountsAsOnAnyDataCentre(string? world) =>
        Assert.Equal(1UL, Openable("Light", Ad(ListingSource.InGamePartyFinder, "1", 10, world)));

    [Fact]
    public void ASpacedWorldStillCountsForItsDataCentre() =>
        Assert.Null(Openable("Light", Ad(ListingSource.InGamePartyFinder, "1", 10, " Cerberus ")));

    [Fact]
    public void TheAdListedLongestIsOpened()
    {
        // The xivpf copy, seen 40 minutes ago, is listed until 50 minutes from now; the in-game one, seen 20 minutes ago, until 40
        var id = Openable("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 20), Ad(ListingSource.Xivpf, "2", 40), Ad(ListingSource.InGamePartyFinder, "3", 30));
        Assert.Equal(2UL, id);
    }

    [Fact]
    public void TheAdListedLongestDecidesEvenWhenItIsElsewhere() =>
        Assert.Null(Openable("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 20), Ad(ListingSource.Xivpf, "2", 10, "Odin")));

    [Fact]
    public void OnATieTheFirstIsOpened() =>
        Assert.Equal(1UL, Openable("Chaos", Ad(ListingSource.InGamePartyFinder, "1", 20), Ad(ListingSource.InGamePartyFinder, "2", 20)));

    [Fact]
    public void OtherSourcesAndIdsThatArentNumbersHaveNoAd()
    {
        Assert.Null(Openable("Chaos"));
        Assert.Null(Openable("Chaos", Ad(ListingSource.Shout, "1", 1), Ad(ListingSource.Partake, "2", 1)));
        Assert.Null(Openable("Chaos", Ad(ListingSource.Xivpf, "-1", 1), Ad(ListingSource.Xivpf, "1 ", 1), Ad(ListingSource.Xivpf, "", 1)));
    }

    [Fact]
    public void AnAdWithABadIdIsSkippedForOneWithAGoodOne() =>
        Assert.Equal(7UL, Openable("Chaos", Ad(ListingSource.Xivpf, "x", 1), Ad(ListingSource.InGamePartyFinder, "7", 50)));
}
