using System.Text.Json;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class XivpfTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private const string Venue = "Moonlit Lounge  Mist W14 P33 — open now!\nmoonlit.carrd.co";

    /// <summary>One listing in the API's shape, trimmed to the fields Jaunt reads plus a few it ignores.</summary>
    private static string ItemJson(
        string id = "270149",
        string category = "\"None\"",
        string? description = null,
        string world = "{\"id\":96,\"name\":\"Cuchulainn\"}") => $$"""
        {"created_at":"2026-11-14T19:44:13.692Z","time_left":40.05,
         "listing":{"id":{{id}},"recruiter":"Some Player","description":{{description ?? Texts(Venue)}},
          "created_world":{{world}},"home_world":{"id":1,"name":"Elsewhere"},"current_world":{"id":2,"name":"Anywhere"},
          "category":{{category}},"slots":[["SMN"]],"slots_filled":["SMN",null]} }
        """;

    private static string Texts(string en) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["en"] = en, ["ja"] = en, ["de"] = en, ["fr"] = en });

    private static Xivpf.Ad Single(string itemJson) => Assert.Single(Xivpf.Parse($"[{itemJson}]"));

    [Fact]
    public void RecordingForARegionLeavesOutAdsOnOtherWorldsOrNone()
    {
        var cache = new ListingCache();
        var region = TestGazetteer.Instance.TravelRegion("Gilgamesh");

        Xivpf.Record(cache, [new("1", Venue, "Cuchulainn"), new("2", Venue, "Moogle"), new("3", Venue, null)], Now, region);

        Assert.Equal(["1"], cache.Listings.Select(l => l.Id));
    }

    [Fact]
    public void RecordingWithoutARegionKeepsEveryAd()
    {
        var cache = new ListingCache();

        Xivpf.Record(cache, [new("1", Venue, "Cuchulainn"), new("2", Venue, "Moogle"), new("3", Venue, null)], Now);

        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void ReadsAnAdsIdTextAndWorld()
    {
        var ad = Single(ItemJson());

        Assert.Equal(new Xivpf.Ad("270149", Venue, "Cuchulainn"), ad);
    }

    [Theory]
    [InlineData("\"HighEndDuty\"")]
    [InlineData("\"none\"")]
    [InlineData("null")]
    public void KeepsOnlyTheNoneCategory(string category) =>
        Assert.Empty(Xivpf.Parse($"[{ItemJson(category: category)}]"));

    [Theory]
    [InlineData("""{"en":"English text","ja":"日本語のテキスト"}""")]
    [InlineData("""{"ja":"日本語のテキスト","de":"Deutscher Text","en":"English text"}""")]
    public void ReadsTheEnglishTextWhereverItIs(string texts) =>
        Assert.Equal("English text", Single(ItemJson(description: texts)).Description);

    [Theory]
    [InlineData("""{"ja":"日本語のテキスト"}""")]
    [InlineData("""{"en":"","ja":"日本語のテキスト"}""")]
    [InlineData("""{"en":"  ","ja":"日本語のテキスト"}""")]
    [InlineData("""{"en":null,"de":"","ja":"日本語のテキスト"}""")]
    public void FallsBackToAnotherLanguageWhenEnglishIsMissing(string texts) =>
        Assert.Equal("日本語のテキスト", Single(ItemJson(description: texts)).Description);

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"en":"","ja":" "}""")]
    public void SkipsAnAdWithNoText(string texts) =>
        Assert.Empty(Xivpf.Parse($"[{ItemJson(description: texts)}]"));

    [Theory]
    [InlineData("null")]
    [InlineData("{\"id\":96}")]
    [InlineData("{\"id\":96,\"name\":\" \"}")]
    public void AnAdWithNoWorldNameIsListedOnNoWorld(string world) =>
        Assert.Null(Single(ItemJson(world: world)).ListedOn);

    [Fact]
    public void TrimsTheWorldName() =>
        Assert.Equal("Cuchulainn", Single(ItemJson(world: "{\"name\":\" Cuchulainn \"}")).ListedOn);

    [Theory]
    [InlineData("null")]
    [InlineData("\"270149\"")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void SkipsAListingWithoutAWholeNumberIdWithoutFailingTheOthers(string id)
    {
        var ads = Xivpf.Parse($"[{ItemJson(id: id)},{ItemJson(id: "7")}]");

        Assert.Equal("7", Assert.Single(ads).Id);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"listing":null}""")]
    [InlineData("""{"listing":[]}""")]
    [InlineData("""{"listing":{"id":5,"category":"None","description":"text"}}""")]
    public void SkipsAnItemOfTheWrongShapeWithoutFailingTheOthers(string item)
    {
        var ads = Xivpf.Parse($"[{item},{ItemJson(id: "7")}]");

        Assert.Equal("7", Assert.Single(ads).Id);
    }

    [Fact]
    public void ReadsAListingIdBeyondTheIntRange() =>
        Assert.Equal("4294967295", Single(ItemJson(id: "4294967295")).Id);

    [Theory]
    [InlineData("{}")]
    [InlineData("\"listings\"")]
    public void ThrowsWhenTheResponseIsNotAList(string json) =>
        Assert.Throws<JsonException>(() => Xivpf.Parse(json));

    [Fact]
    public void ThrowsOnText() => Assert.ThrowsAny<JsonException>(() => Xivpf.Parse("<html>Bad gateway</html>"));

    [Fact]
    public void RecordsAdsWithTheirTextWorldAndTitle()
    {
        var cache = new ListingCache();

        Xivpf.Record(cache, [new Xivpf.Ad("12", Venue, "Cuchulainn")], Now);

        var listing = cache.Find(ListingSource.Xivpf, "12")!;
        Assert.Equal(ListingDetails.Title(Venue), listing.Title);
        Assert.Equal(Venue, listing.Description);
        Assert.Equal("Cuchulainn", listing.ListedOn);
        Assert.Equal(Now, listing.LastSeen);
        Assert.False(listing.OneOff);
    }

    [Fact]
    public void RecordingAgainUpdatesTheTextAndKeepsMarks()
    {
        var cache = new ListingCache();
        Xivpf.Record(cache, [new Xivpf.Ad("12", "Old text", "Cuchulainn")], Now.AddHours(-1));
        cache.Find(ListingSource.Xivpf, "12")!.Hidden = true;

        Xivpf.Record(cache, [new Xivpf.Ad("12", "New text", null)], Now);

        var listing = cache.Find(ListingSource.Xivpf, "12")!;
        Assert.Equal("New text", listing.Description);
        Assert.Equal("New text", listing.Title);
        Assert.Null(listing.ListedOn);
        Assert.Equal(Now, listing.LastSeen);
        Assert.True(listing.Hidden);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void AnAdTheGameAlsoSentCountsAsSeenNowInGame()
    {
        // revert-check: not revert-sensitive (pins that xivpf leaves the in-game copy's text, title and world alone, as before)
        var cache = new ListingCache();
        var inGame = cache.SeenAd(ListingSource.InGamePartyFinder, "12", "In-game text", "Siren", Now.AddDays(-2));

        Xivpf.Record(cache, [new Xivpf.Ad("12", "xivpf text", "Cuchulainn")], Now);

        Assert.Equal(Now, inGame.LastSeen);
        Assert.Equal(("In-game text", "In-game text", "Siren"), (inGame.Description, inGame.Title, inGame.ListedOn));
        Assert.NotNull(cache.Find(ListingSource.Xivpf, "12"));
    }

    [Fact]
    public void RecordingNeverMovesTheInGameLastSeenBack()
    {
        var cache = new ListingCache();
        var inGame = cache.Seen(ListingSource.InGamePartyFinder, "12", "In-game", Now);

        Xivpf.Record(cache, [new Xivpf.Ad("12", "xivpf text", null)], Now.AddMinutes(-5));

        Assert.Equal(Now, inGame.LastSeen);
    }

    [Fact]
    public void RecordingLeavesOtherInGameListingsAlone()
    {
        var cache = new ListingCache();
        var other = cache.Seen(ListingSource.InGamePartyFinder, "13", "Other", Now.AddDays(-2));

        Xivpf.Record(cache, [new Xivpf.Ad("12", "xivpf text", null)], Now);

        Assert.Equal(Now.AddDays(-2), other.LastSeen);
        Assert.Null(cache.Find(ListingSource.InGamePartyFinder, "12"));
    }

    [Fact]
    public void AnXivpfListingTheGameAlsoSentIsShownInGame()
    {
        var cache = new ListingCache();
        var xivpf = cache.Seen(ListingSource.Xivpf, "12", "A", Now);
        var inGame = cache.Seen(ListingSource.InGamePartyFinder, "12", "A", Now);

        Assert.True(Xivpf.ShownInGame(cache, xivpf));
        Assert.False(Xivpf.ShownInGame(cache, inGame));
    }

    [Fact]
    public void AnXivpfListingOnlyXivpfSentIsShownItself()
    {
        var cache = new ListingCache();
        var xivpf = cache.Seen(ListingSource.Xivpf, "12", "A", Now);
        cache.Seen(ListingSource.InGamePartyFinder, "13", "B", Now);
        cache.Seen(ListingSource.FfxivVenues, "12", "C", Now);

        Assert.False(Xivpf.ShownInGame(cache, xivpf));
    }

    [Fact]
    public void ListingsFromOtherSourcesAreNeverShownInGame()
    {
        var cache = new ListingCache();
        var venue = cache.Seen(ListingSource.FfxivVenues, "12", "A", Now);
        cache.Seen(ListingSource.InGamePartyFinder, "12", "B", Now);

        Assert.False(Xivpf.ShownInGame(cache, venue));
    }
}
