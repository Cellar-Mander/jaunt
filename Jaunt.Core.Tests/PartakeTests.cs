using System.Text.Json;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class PartakeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    /// <summary>One event in the API's shape, as the query asks for it.</summary>
    private static string EventJson(
        string id = "56564",
        string title = "\"Virtue - RITUAL\"",
        string startsAt = "\"2026-11-14T21:00:00.000Z\"",
        string endsAt = "\"2026-11-15T01:00:00.000Z\"",
        string location = "\"Lavender Beds, Ward 20, Plot 28\"",
        string server = """{"name":"Lich"}""",
        string dataCenter = """{"name":"Light"}""",
        string team = """{"websiteUrl":"https://virtue-xiv.club/","discordUrl":"discord.gg/xivvirtue"}""",
        string ageRating = "\"ADULT\"",
        string tags = """["Live DJs","Casino & Gambling"]""",
        string description = "null",
        string attachments = "[]") => $$"""
        {"id":{{id}},"title":{{title}},"description":{{description}},"attachments":{{attachments}},"startsAt":{{startsAt}},"endsAt":{{endsAt}},"location":{{location}},
         "server":{{server}},"dataCenter":{{dataCenter}},"team":{{team}},"ageRating":{{ageRating}},"tags":{{tags}} }
        """;

    private static string Response(params string[] events) => "{\"data\":{\"events\":[" + string.Join(",", events) + "]}}";

    private static Partake.Page Read(params string[] events) => Partake.Parse(Response(events));

    private static Partake.Event Single(string eventJson) => Assert.Single(Read(eventJson).Events);

    [Theory]
    [InlineData("Lich", "Light", true)]
    [InlineData("Lich", null, true)]
    [InlineData("Lichh", "Light", true)] // a world Jaunt doesn't know, on a data centre in the region
    [InlineData("Gilgamesh", "Aether", false)]
    [InlineData("Gilgamesh", null, false)]
    [InlineData("Lichh", "Aether", false)]
    public void RecordingForARegionKeepsEventsWhoseWorldOrDataCentreIsInIt(string world, string? dataCentre, bool kept)
    {
        var cache = new ListingCache();
        var item = Single(EventJson()) with { World = world, DataCentre = dataCentre };

        Partake.Record(cache, [item], Now, TestGazetteer.Instance.TravelRegion("Moogle"));

        Assert.Equal(kept, cache.Count == 1);
    }

    [Fact]
    public void RecordingWithoutARegionKeepsEventsAnywhere()
    {
        var cache = new ListingCache();

        Partake.Record(cache, [Single(EventJson()) with { World = "Gilgamesh", DataCentre = "Aether" }], Now);

        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ReadsAnEventsTitleTimesWorldLocationAndHostLinks()
    {
        var item = Single(EventJson());

        Assert.Equal("56564", item.Id);
        Assert.Equal("Virtue - RITUAL", item.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 21, 0, 0, TimeSpan.Zero), item.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 1, 0, 0, TimeSpan.Zero), item.EndsAt);
        Assert.Equal("Lich", item.World);
        Assert.Equal("Light", item.DataCentre);
        Assert.Equal("Lavender Beds, Ward 20, Plot 28", item.Location);
        Assert.Equal(["https://virtue-xiv.club/", "discord.gg/xivvirtue"], item.LinkTexts);
    }

    [Fact]
    public void AsksForTheAgeRatingAndTags()
    {
        var query = JsonDocument.Parse(Partake.RequestBody(Now, 0)).RootElement.GetProperty("query").GetString()!;

        Assert.Contains(" ageRating tags", query, StringComparison.Ordinal);
    }

    [Fact]
    public void AsksForTheDescription()
    {
        var query = JsonDocument.Parse(Partake.RequestBody(Now, 0)).RootElement.GetProperty("query").GetString()!;

        Assert.Contains(" description ", query, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheDescriptionAsPlainText()
    {
        var item = Single(EventJson(description: JsonSerializer.Serialize(
            "\n![](https://cdn.partake.gg/assets/a.webp)\n\n## **Welcome!**\n\nLive DJs all night.\n\nhttps://discord.gg/xivvirtue\n")));

        Assert.Equal("Welcome!\n\nLive DJs all night.\n\nhttps://discord.gg/xivvirtue", item.About!.Text);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"![](https://cdn.partake.gg/assets/a.webp)\"")]
    public void AnEventWithoutADescriptionHasNone(string description) =>
        Assert.Null(Single(EventJson(description: description)).About);

    [Fact]
    public void ADescriptionsInvitesJoinTheLinksOnce()
    {
        var cache = new ListingCache();
        var description = JsonSerializer.Serialize("Join discord.gg/xivvirtue or [our backup](https://discord.gg/backup)");
        Partake.Record(cache, [Single(EventJson(description: description))], Now);

        var shown = Partake.Show(Assert.Single(cache.Listings), TestGazetteer.Instance);

        Assert.Equal(
            [new Uri("https://virtue-xiv.club/"), new Uri("https://discord.gg/xivvirtue"), new Uri("https://discord.gg/backup")],
            shown.Links);
    }

    [Fact]
    public void KeepsTheDescriptionThroughTheCache()
    {
        var cache = new ListingCache();
        Partake.Record(cache, [Single(EventJson(description: "\"**Live DJs** at discord.gg/backup\""))], Now);

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.Partake, "56564")!;

        Assert.Equal(("Live DJs at discord.gg/backup", false), (back.About!.Text, back.About.Cut));
        Assert.Equal(["https://discord.gg/backup"], back.About.Invites);
    }

    [Fact]
    public void KeepsAttachmentsAndDescriptionImagesThroughTheCache()
    {
        var cache = new ListingCache();
        var description = JsonSerializer.Serialize("![](https://cdn.partake.gg/assets/b.webp)\n**Live DJs** ![poster](https://cdn.partake.gg/assets/c.webp \"Poster\")");
        Partake.Record(cache, [Single(EventJson(description: description, attachments: """["a.webp", null, "d"]"""))], Now);

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.Partake, "56564")!;

        Assert.Equal(["a.webp", "d"], back.Attachments);
        Assert.Equal(["https://cdn.partake.gg/assets/b.webp", "https://cdn.partake.gg/assets/c.webp"], back.DescriptionImages);
        Assert.Equal("Live DJs", back.About!.Text);
    }

    [Fact]
    public void RecordingAnEventWithoutImagesKeepsNone()
    {
        var cache = new ListingCache();
        Partake.Record(cache, [Single(EventJson()) with { Attachments = null, DescriptionImages = null }], Now);

        var listing = cache.Find(ListingSource.Partake, "56564")!;

        Assert.Empty(listing.Attachments);
        Assert.Empty(listing.DescriptionImages);
    }

    [Fact]
    public void AnEventWithoutImagesHasNone()
    {
        var cache = new ListingCache();
        Partake.Record(cache, [Single(EventJson(attachments: "null"))], Now);

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.Partake, "56564")!;

        Assert.Empty(back.Attachments);
        Assert.Empty(back.DescriptionImages);
    }

    [Theory]
    [InlineData("a21bc48c.webp", "https://cdn.partake.gg/assets/a21bc48c.webp")]
    [InlineData("a_6944ada3.gif", "https://cdn.partake.gg/assets/a_6944ada3.gif")]
    [InlineData("205b72ae", "https://cdn.partake.gg/assets/205b72ae.webp")] // sent without an extension; WebP there
    [InlineData("02fcdfdc-5c0d-41e9.jpeg", "https://cdn.partake.gg/assets/02fcdfdc-5c0d-41e9.jpeg")]
    [InlineData("", null)]
    [InlineData("../secret.png", null)]
    [InlineData(".hidden", null)]
    [InlineData("a/b.png", null)]
    [InlineData("a b.png", null)]
    [InlineData("a?x=1.png", null)]
    public void AnAttachmentIsAPlainFileOnPartakesCdn(string name, string? expected)
    {
        Assert.Equal(expected, Partake.AttachmentUri(name)?.AbsoluteUri);
    }

    [Fact]
    public void AnAttachmentNameOver100CharactersIsSkipped()
    {
        Assert.NotNull(Partake.AttachmentUri(new string('a', 100)));
        Assert.Null(Partake.AttachmentUri(new string('a', 101)));
    }

    [Fact]
    public void KeepsTheAgeRatingAndTagsThroughTheCache()
    {
        var cache = new ListingCache();
        Partake.Record(cache, [Single(EventJson(tags: """["Live DJs", null, "Spa & Relaxation"]"""))], Now);

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.Partake, "56564")!;

        Assert.Equal("ADULT", back.Rating);
        Assert.Equal(["Live DJs", "Spa & Relaxation"], back.SourceTags);
    }

    [Fact]
    public void AnEventWithoutAgeRatingOrTagsHasNeither()
    {
        var cache = new ListingCache();
        Partake.Record(cache, [Single(EventJson(ageRating: "null", tags: "null"))], Now);

        var listing = Assert.Single(cache.Listings);
        Assert.Null(listing.Rating);
        Assert.Empty(listing.SourceTags);
    }

    [Fact]
    public void RecordingAnEventMadeWithoutTagsKeepsNone()
    {
        var cache = new ListingCache();
        Partake.Record(cache, [Single(EventJson()) with { Tags = null }], Now);

        Assert.Empty(Assert.Single(cache.Listings).SourceTags);
    }

    [Fact]
    public void KeepsValuesAsSent()
    {
        var title = "  " + new string('x', 80) + " ";
        var item = Single(EventJson(
            title: JsonSerializer.Serialize(title),
            location: "\"  LB W19 P58 \"",
            server: """{"name":" bismark "}""",
            dataCenter: """{"name":" Light "}""",
            team: """{"websiteUrl":"javascript:alert(1)","discordUrl":"  "}"""));

        Assert.Equal(title, item.Title);
        Assert.Equal(" bismark ", item.World);
        Assert.Equal(" Light ", item.DataCentre);
        Assert.Equal("  LB W19 P58 ", item.Location);
        Assert.Equal(["javascript:alert(1)", "  "], item.LinkTexts);
    }

    [Fact]
    public void KeepsAnEventWithoutDataCentreLocationOrHost()
    {
        var item = Single(EventJson(location: "null", dataCenter: "null", team: "null"));

        Assert.Null(item.DataCentre);
        Assert.Null(item.Location);
        Assert.Empty(item.LinkTexts);
        Assert.Equal(["https://x.example/"], Single(EventJson(team: """{"websiteUrl":"https://x.example/","discordUrl":null}""")).LinkTexts);
    }

    private static CachedListing Cached(
        string title = "Gala", string? world = "Lich", string? location = "LB W19 P58", params string[] links) =>
        new() { Source = ListingSource.Partake, Id = "1", Title = title, ListedOn = world, DataCentre = "Light", Location = location, LinkTexts = links };

    private static Partake.Shown Show(CachedListing listing) => Partake.Show(listing, TestGazetteer.Instance);

    [Fact]
    public void ShowsTheValuesSent()
    {
        var shown = Show(Cached(links: ["https://virtue-xiv.club/", "discord.gg/xivvirtue"]));

        Assert.Equal(new Partake.Shown("Gala", "Lich", "LB W19 P58", shown.Links), shown);
        Assert.Equal([new Uri("https://virtue-xiv.club/"), new Uri("https://discord.gg/xivvirtue")], shown.Links);
    }

    [Theory]
    [InlineData(" cerberus ", "Cerberus")]
    [InlineData("Bismark", "Bismarck")]
    [InlineData(" Nowhere ", "Nowhere")]
    [InlineData(" ", "")]
    [InlineData(null, "")]
    public void ShowsTheWorldAsTheGameSpellsIt(string? sent, string expected)
    {
        Assert.Equal(expected, Show(Cached(world: sent)).World);
    }

    [Fact]
    public void AnAddressNamingNoWorldIsOnTheEventsWorldAsWritten()
    {
        Assert.Equal(
            [new HousingAddress("Lich", WorldSource.Text, District.LavenderBeds, 19, 58)],
            Partake.Addresses("LB W19 P58", "Lich", TestGazetteer.Parser));
    }

    [Fact]
    public void AnAddressNamingAWorldKeepsIt()
    {
        Assert.Equal(
            [new HousingAddress("Siren", WorldSource.Text, District.Mist, 5, 12)],
            Partake.Addresses("Siren Mist W5 P12", "Lich", TestGazetteer.Parser));
    }

    [Fact]
    public void ALocationWithoutAnAddressHasNone()
    {
        Assert.Empty(Partake.Addresses("Somewhere in Limsa", "Lich", TestGazetteer.Parser));
    }

    [Theory]
    [InlineData("  LB W19 P58 ", "LB W19 P58")]
    [InlineData(null, "")]
    public void ShowsTheLocationTrimmed(string? sent, string expected)
    {
        Assert.Equal(expected, Show(Cached(location: sent)).Location);
    }

    [Fact]
    public void ShowsOnlyWebLinks()
    {
        Assert.Empty(Show(Cached(links: ["javascript:alert(1)", "  "])).Links);
    }

    [Fact]
    public void ShowsALongTitleCut()
    {
        var title = Show(Cached(title: "  " + new string('x', 80) + " ")).Title;

        Assert.Equal(ListingDetails.MaxTitleLength, title.Length);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
        Assert.Equal("Gala", Partake.Title(" Gala "));
    }

    [Fact]
    public void HiddenEventsAreListedByTheirShownTitles()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "  zebra gala ", Now, oneOff: true).Hidden = true;
        cache.Seen(ListingSource.Partake, "2", "amber night", Now, oneOff: true).Hidden = true;

        Assert.Equal(["amber night", "zebra gala"], cache.HiddenListings().Select(l => l.ShownTitle));
    }

    [Theory]
    [InlineData("endsAt", "\"2025-05-05T21:00:00.000Z\"")] // before startsAt
    [InlineData("endsAt", "\"2026-11-14T21:00:00.000Z\"")] // equal to startsAt
    [InlineData("endsAt", "null")]
    [InlineData("startsAt", "null")]
    [InlineData("startsAt", "\"tonight\"")]
    [InlineData("server", "null")]
    [InlineData("server", """{"name":null}""")]
    [InlineData("server", """{"name":"  "}""")]
    [InlineData("id", "null")]
    [InlineData("id", "\"abc\"")]
    [InlineData("title", "null")]
    [InlineData("title", "\" \"")]
    public void SkipsAnEventItCantShowRightly(string field, string json)
    {
        string Field(string name, string fallback) => name == field ? json : fallback;
        var broken = EventJson(
            id: Field("id", "1"),
            title: Field("title", "\"Broken\""),
            startsAt: Field("startsAt", "\"2026-11-14T21:00:00.000Z\""),
            endsAt: Field("endsAt", "\"2026-11-15T01:00:00.000Z\""),
            server: Field("server", """{"name":"Lich"}"""));

        var page = Read(broken, EventJson());

        Assert.Equal("56564", Assert.Single(page.Events).Id);
        Assert.Equal(2, page.Received);
    }

    [Theory]
    [InlineData("""{"errors":[{"message":"Unknown argument"}],"data":null}""")]
    [InlineData("""{"errors":[{"message":"Rate limited"}],"data":{"events":[]}}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":{"events":null}}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""[]""")]
    public void FailsOnAResponseWithErrorsOrNoEvents(string json)
    {
        Assert.ThrowsAny<JsonException>(() => Partake.Parse(json));
    }

    [Fact]
    public void AnEmptyErrorListIsNoError()
    {
        var page = Partake.Parse("""{"errors":[],"data":{"events":[]}}""");

        Assert.Empty(page.Events);
    }

    [Theory]
    [InlineData(Partake.PageSize, 1, true)]
    [InlineData(Partake.PageSize, Partake.MaxPages - 1, true)]
    [InlineData(Partake.PageSize, Partake.MaxPages, false)]
    [InlineData(Partake.PageSize - 1, 1, false)]
    [InlineData(0, 1, false)]
    public void AsksForAnotherPageOnlyAfterAFullOne(int received, int pagesRead, bool expected)
    {
        Assert.Equal(expected, Partake.HasMore(new Partake.Page([], received), pagesRead));
    }

    [Fact]
    public void RequestAsksForFfxivEventsFromTheStartInUtc()
    {
        var start = new DateTimeOffset(2026, 11, 14, 9, 30, 15, TimeSpan.FromHours(13));

        using var body = JsonDocument.Parse(Partake.RequestBody(start, offset: 200));
        var variables = body.RootElement.GetProperty("variables");

        Assert.Equal("final-fantasy-xiv", variables.GetProperty("game").GetString());
        Assert.Equal("2026-11-13T20:30:15Z", variables.GetProperty("start").GetString());
        Assert.Equal(Partake.PageSize, variables.GetProperty("limit").GetInt32());
        Assert.Equal(200, variables.GetProperty("offset").GetInt32());
        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("startsBetween: { start: $start }", query, StringComparison.Ordinal);
        Assert.Contains("server { name }", query, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsEventsAsOneOffsWithTheirDetails()
    {
        var cache = new ListingCache();
        var item = Single(EventJson());

        Partake.Record(cache, [item], Now);

        var listing = cache.Find(ListingSource.Partake, "56564")!;
        Assert.Equal("Virtue - RITUAL", listing.Title);
        Assert.True(listing.OneOff);
        Assert.Equal(Now, listing.LastSeen);
        Assert.Equal(item.StartsAt, listing.StartsAt);
        Assert.Equal(item.EndsAt, listing.EndsAt);
        Assert.Equal("Lich", listing.ListedOn);
        Assert.Equal("Light", listing.DataCentre);
        Assert.Equal("Lavender Beds, Ward 20, Plot 28", listing.Location);
        Assert.Equal(item.LinkTexts, listing.LinkTexts);
        Assert.Equal("", listing.Description);
    }

    [Fact]
    public void RecordsAnEventUnderWayButNotOneThatHasEnded()
    {
        var cache = new ListingCache();
        var underWay = Single(EventJson(id: "1", startsAt: "\"2026-11-14T19:00:00Z\"", endsAt: "\"2026-11-14T20:00:01Z\""));
        var endedNow = Single(EventJson(id: "2", startsAt: "\"2026-11-14T18:00:00Z\"", endsAt: "\"2026-11-14T20:00:00Z\""));

        Partake.Record(cache, [underWay, endedNow], Now);

        Assert.NotNull(cache.Find(ListingSource.Partake, "1"));
        Assert.Null(cache.Find(ListingSource.Partake, "2"));
    }

    [Fact]
    public void AnEventShowsUntilItEnds()
    {
        var listing = new CachedListing { Source = ListingSource.Partake, Id = "1", EndsAt = Now };

        Assert.True(Partake.NotEnded(listing, Now.AddTicks(-1)));
        Assert.False(Partake.NotEnded(listing, Now));
        Assert.False(Partake.NotEnded(new CachedListing { Source = ListingSource.Partake, Id = "2" }, Now));
    }

    [Fact]
    public void ReadsTheLiveResponseShape()
    {
        // Two events from the 14 Nov 2026 snapshot, one without a server
        const string json = """
            {"data":{"events":[
              {"id":56576,"title":"SHORELINE ★ SLUMBER PARTY★","startsAt":"2026-11-13T09:00:00.000Z","endsAt":"2026-11-13T17:30:00.000Z",
               "location":"Sophia Mist W18 P60","server":{"name":"Sophia"},"dataCenter":{"name":"Materia"},
               "team":{"websiteUrl":"https://sunsetcoast.carrd.co","discordUrl":"https://discord.gg/Sunsetcoast"}},
              {"id":56001,"title":"Somewhere","startsAt":"2026-11-14T20:00:00.000Z","endsAt":"2026-11-14T23:00:00.000Z",
               "location":"TBA","server":null,"dataCenter":null,"team":{"websiteUrl":null,"discordUrl":null}}
            ]}}
            """;

        var page = Partake.Parse(json);

        Assert.Equal("SHORELINE ★ SLUMBER PARTY★", Assert.Single(page.Events).Title);
        Assert.Equal(2, page.Received);
    }
}
