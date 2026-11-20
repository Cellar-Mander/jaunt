using Jaunt.Core.Browser;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class ShoutsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private const string Advert = "Moonlit Lounge is OPEN NOW! Mist W14 P33 — DJ Saber on the decks till 1am ST, come dance!";

    [Theory]
    [InlineData(Advert)]
    [InlineData("Club Nine opening at 9pm ST ~ Gilgamesh Goblet Ward 5 Plot 12 ~ drinks, gamba and DJs!")]
    // An apartment counts; so does an address naming only the ward
    [InlineData("Starlight Bar in Empyreum W20 Apt 14 open now")]
    [InlineData("Cosy tea party tonight in Lavender Beds ward 8, look for the lanterns")]
    public void AShoutGivingADistrictAndWardIsAnAdvert(string text) =>
        Assert.True(Shouts.IsAdvert(TestGazetteer.Parser, text, "Cuchulainn"));

    [Theory]
    [InlineData("S rank Burfurlur up in Urqopacha (x:22 y:31), pulling in 5!")]
    [InlineData("WTS crafted gear, PST for prices")]
    [InlineData("LF2M for Unreal, PF is up")]
    [InlineData("Anyone know where the Mist aetheryte vendor is?")]
    [InlineData("")]
    public void AShoutWithoutAHousingAddressIsNotAnAdvert(string text) =>
        Assert.False(Shouts.IsAdvert(TestGazetteer.Parser, text, "Cuchulainn"));

    [Fact]
    public void AnAddressNamingNoWorldIsOnTheWorldTheShoutWasHeardOn()
    {
        var cache = new ListingCache();

        var listing = Shouts.Record(cache, TestGazetteer.Parser, "a", Advert, "Cuchulainn", Now)!;

        Assert.Equal("Cuchulainn", listing.ListedOn);
        var details = ListingDetails.Parse(TestGazetteer.Parser, listing.Description, listing.ListedOn);
        Assert.Equal(new AddressKey("CUCHULAINN", Text.District.Mist, 14, 33, null, false), AddressKey.Of(details.Addresses[0]));
    }

    [Fact]
    public void AnAdvertIsRecordedAsAnAdWithItsTextAndTitle()
    {
        var cache = new ListingCache();

        var listing = Shouts.Record(cache, TestGazetteer.Parser, "a", Advert, "Cuchulainn", Now);

        Assert.Same(listing, cache.Find(ListingSource.Shout, "a"));
        Assert.Equal((Advert, Now, Now), (listing!.Description, listing.LastSeen, listing.FirstSeen));
        Assert.Equal(ListingDetails.Title(Advert), listing.Title);
        Assert.Equal("Saber", Assert.Single(ListingDetails.Parse(TestGazetteer.Parser, Advert, "Cuchulainn").Performers).Name);
    }

    [Fact]
    public void AShoutThatIsNotAnAdvertIsNotRecordedAndKeepsTheShoutersLastAdvert()
    {
        var cache = new ListingCache();
        Shouts.Record(cache, TestGazetteer.Parser, "a", Advert, "Cuchulainn", Now.AddMinutes(-10));

        Assert.Null(Shouts.Record(cache, TestGazetteer.Parser, "a", "Thanks for coming everyone!", "Cuchulainn", Now));
        Assert.Null(Shouts.Record(cache, TestGazetteer.Parser, "b", "WTS crafted gear", "Cuchulainn", Now));

        var kept = Assert.Single(cache.Listings);
        Assert.Equal((Advert, Now.AddMinutes(-10)), (kept.Description, kept.LastSeen));
    }

    [Fact]
    public void AShoutersNewAdvertReplacesTheirLastOne()
    {
        var cache = new ListingCache();
        Shouts.Record(cache, TestGazetteer.Parser, "a", Advert, "Cuchulainn", Now.AddMinutes(-10));

        Shouts.Record(cache, TestGazetteer.Parser, "a", "Club Nine, Goblet W5 P12, open now", "Cuchulainn", Now);

        var listing = Assert.Single(cache.Listings);
        Assert.Equal(("Club Nine, Goblet W5 P12, open now", Now, Now), (listing.Description, listing.LastSeen, listing.FirstSeen));
    }

    [Fact]
    public void AShoutersIdIsStableAndKeepsNoName()
    {
        var id = Shouts.IdOf("Some Player", "Cuchulainn");

        Assert.Equal(id, Shouts.IdOf(" Some Player ", "Cuchulainn"));
        Assert.Matches("^[0-9a-f]{16}$", id);
        Assert.NotEqual(id, Shouts.IdOf("Some Player", "Gilgamesh"));
        Assert.NotEqual(id, Shouts.IdOf("Some Player", null));
        Assert.NotEqual(id, Shouts.IdOf("Other Player", "Cuchulainn"));
    }

    [Fact]
    public void ShoutsAreSavedAndLoadedLikeAds()
    {
        var cache = new ListingCache();
        Shouts.Record(cache, TestGazetteer.Parser, "a", Advert, "Cuchulainn", Now.AddMinutes(-30));
        Shouts.Record(cache, TestGazetteer.Parser, "a", Advert, "Cuchulainn", Now);
        cache.Find(ListingSource.Shout, "a")!.Favourite = true;

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.Shout, "a")!;

        Assert.Equal((Advert, "Cuchulainn", Now, Now.AddMinutes(-30), true), (back.Description, back.ListedOn, back.LastSeen, back.FirstSeen, back.Favourite));
        Assert.Contains(ListingCache.SourceFile(ListingSource.Shout), SavedFiles.All(cache).Keys);
    }

    [Fact]
    public void ClearingRemovesUnmarkedShoutsLikeOtherListings()
    {
        var cache = new ListingCache();
        Shouts.Record(cache, TestGazetteer.Parser, "a", Advert, "Cuchulainn", Now);
        Shouts.Record(cache, TestGazetteer.Parser, "b", "Club Nine, Goblet W5 P12, open now", "Cuchulainn", Now)!.Hidden = true;

        Assert.Equal(1, cache.ClearUnmarked());
        Assert.Equal(["b"], cache.Listings.Select(l => l.Id));
    }

    [Fact]
    public void AShoutAndAPartyFinderAdForTheSameOpeningAreOneEntry()
    {
        var cache = new ListingCache();
        var shout = Shouts.Record(cache, TestGazetteer.Parser, "a", Advert, "Cuchulainn", Now)!;
        var ad = cache.SeenAd(ListingSource.InGamePartyFinder, "7", "Moonlit Lounge ~ Mist W14 P33 ~ open now", "Cuchulainn", Now);

        var merged = Duplicates.Merge([Candidate(shout), Candidate(ad)], Now);

        Assert.Equal([shout, ad], Assert.Single(merged).Members);
    }

    // Shapes from captured shouts, plus a joined advert tagging each part
    [Theory]
    [InlineData("VENUEAD: " + Advert, Advert)]
    [InlineData("VENUEAD :  " + Advert, Advert)]
    [InlineData("[VENUEAD] " + Advert, Advert)]
    [InlineData("  VENUEAD:" + Advert, Advert)]
    [InlineData("VENUEAD: Moonlit Lounge Mist W14 P33\nVENUEAD: DJ Saber till 1am", "Moonlit Lounge Mist W14 P33\nDJ Saber till 1am")]
    public void TheVenueAdTagIsLeftOutOfTheRecordedText(string shout, string recorded)
    {
        var cache = new ListingCache();

        var listing = Shouts.Record(cache, TestGazetteer.Parser, "a", shout, "Cuchulainn", Now)!;

        Assert.Equal(recorded, listing.Description);
        Assert.Equal(ListingDetails.Title(recorded), listing.Title);
    }

    // Only a tag starting a line, and only the whole word
    [Theory]
    [InlineData("Moonlit Lounge VENUEAD: Mist W14 P33")]
    [InlineData("VENUEADS Mist W14 P33")]
    [InlineData("VENUEAD2 Mist W14 P33")]
    [InlineData("venuead: Mist W14 P33")]
    public void TextThatOnlyLooksLikeTheTagIsKept(string shout)
    {
        // revert-check: not revert-sensitive (guards against stripping too much; the old code strips nothing)
        Assert.Equal(shout, Shouts.Record(new ListingCache(), TestGazetteer.Parser, "a", shout, "Cuchulainn", Now)!.Description);
    }

    private static MergeCandidate<CachedListing> Candidate(CachedListing listing)
    {
        var details = ListingDetails.Parse(TestGazetteer.Parser, listing.Description, listing.ListedOn);
        var schedule = Schedules.ForAd(Schedules.ParseAd(listing.Description, listing.LastSeen), listing.LastSeen, Text.PartyFinderRegion.NorthAmerica,
            listed: true, Now, firstSeen: listing.FirstSeen);
        return new MergeCandidate<CachedListing>(listing, AddressKey.Of(details.Addresses[0]), schedule, Schedules.Listed(schedule, Now), listing.Description);
    }
}
