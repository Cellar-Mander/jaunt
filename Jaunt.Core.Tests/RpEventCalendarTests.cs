using System.Text.Json;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class RpEventCalendarTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 1, 0, 0, TimeSpan.Zero);

    private const string Contacts = "Coco Shev'rin, Enhasa#1319";

    /// <summary>One occurrence in the API's shape, with every field it sends.</summary>
    private static string RowJson(
        string uId = "\"abc_R20260830T160000@google.com\"",
        string name = "\"[M] Coco's Oasis\"",
        string server = "\"Alpha\"",
        string location = "\"Goblet Ward 2, Plot 8\"",
        string url = "\"https://xiv.fyi/ZE2J\"",
        string start = "\"2026-11-15T01:00:00Z\"",
        string end = "\"2026-11-15T04:00:00Z\"",
        string recurring = "true",
        string validated = "\"2026-08-30T00:00:00\"",
        string rating = "\"Mature\"") => $$"""
        {"datacenter":"Light","server":{{server}},"serverId":402,"uId":{{uId}},"eventName":{{name}},
         "location":{{location}},"eventURL":{{url}},"esrbRating":{{rating}},
         "description":"https://xiv.fyi/ZE2J \nGoblet Ward 2, Plot 8\nContacts: {{Contacts}}\nLast Validated: 2026.08.30",
         "shortDescription":"Thavnairian restaurant and bar.","eventCategory":"Bar/Tavern","contacts":"{{Contacts}}",
         "startTimeUTC":{{start}},"endTimeUTC":{{end}},"isRecurring":{{recurring}},"language":"English","lastValidated":{{validated}}}
        """;

    private static IReadOnlyList<RpEventCalendar.Event> Parse(params string[] rows) =>
        RpEventCalendar.Parse($"[{string.Join(",", rows)}]");

    private static RpEventCalendar.Event Single(string row) => Assert.Single(Parse(row));

    /// <summary>Records one event as a fetch would, and returns its cached listing.</summary>
    private static CachedListing Recorded(string row)
    {
        var cache = new ListingCache();
        var calendarEvent = Single(row);
        RpEventCalendar.Record(cache, [calendarEvent], Now);
        return cache.Find(ListingSource.RpEventCalendar, calendarEvent.Id)!;
    }

    /// <summary>What the list shows for one event, after recording it.</summary>
    private static RpEventCalendar.Shown Shown(string row) => RpEventCalendar.Show(Recorded(row), TestGazetteer.Parser);

    [Fact]
    public void TheCreditNamesTheSiteAndItsCcBySaLicence()
    {
        // The calendar's About page puts its listings under CC BY-SA 4.0, which needs both shown
        Assert.Equal("https://ffxiv-rp.org/", RpEventCalendar.SiteUrl.AbsoluteUri);
        Assert.Equal("https://creativecommons.org/licenses/by-sa/4.0/", RpEventCalendar.LicenceUrl.AbsoluteUri);
        Assert.Equal("CC BY-SA 4.0", RpEventCalendar.LicenceName);
    }

    [Fact]
    public void ReadsAnEventsFieldsAsSent()
    {
        var calendarEvent = Single(RowJson(name: "\" [M] Coco's Oasis \"", server: "\" Alpha \"", location: "\" Goblet Ward 2, Plot 8 \"", rating: "\" Mature \""));

        Assert.Equal("abc_R20260830T160000@google.com", calendarEvent.Id);
        Assert.Equal(" [M] Coco's Oasis ", calendarEvent.Name);
        Assert.Equal(" Alpha ", calendarEvent.Server);
        Assert.Equal(" Goblet Ward 2, Plot 8 ", calendarEvent.Location);
        Assert.Equal(" Mature ", calendarEvent.Rating);
        Assert.Equal(["https://xiv.fyi/ZE2J"], calendarEvent.LinkTexts);
        Assert.Equal([new ListingOpening(Start, Start.AddHours(3))], calendarEvent.Openings);
        Assert.False(calendarEvent.OneOff);
        Assert.Equal(new DateOnly(2026, 8, 30), calendarEvent.LastValidated);
    }

    [Fact]
    public void MissingFieldsAreNull()
    {
        var calendarEvent = Single(RowJson(name: "null", server: "null", location: "null", rating: "null", url: "null"));

        Assert.Equal((null, null, null, null), (calendarEvent.Name, calendarEvent.Server, calendarEvent.Location, calendarEvent.Rating));
        Assert.Empty(calendarEvent.LinkTexts);
    }

    [Fact]
    public void ShowsAnEvent() =>
        Assert.Equal(
            new RpEventCalendar.Shown("Coco's Oasis", new HousingAddress("Alpha", WorldSource.Text, District.Goblet, 2, 8), null, "Mature", []),
            Shown(RowJson()) with { Links = [] });

    [Fact]
    public void GroupsOccurrencesOfOneEventEarliestFirst()
    {
        var events = Parse(
            RowJson(start: "\"2026-11-22T01:00:00Z\"", end: "\"2026-11-22T04:00:00Z\""),
            RowJson(uId: "\"other@google.com\""),
            RowJson(),
            RowJson());

        Assert.Equal(["abc_R20260830T160000@google.com", "other@google.com"], events.Select(e => e.Id));
        Assert.Equal(
            [new ListingOpening(Start, Start.AddHours(3)), new ListingOpening(Start.AddDays(7), Start.AddDays(7).AddHours(3))],
            events[0].Openings);
    }

    [Theory]
    [InlineData("Mists Ward 6, Plot 29", District.Mist, 6, 29)]
    [InlineData("Emperyum Ward 13, Plot 56", District.Empyreum, 13, 56)]
    [InlineData("Mist, Plot 5, Ward 10", District.Mist, 10, 5)]
    [InlineData("Empyreum Ward 17,  Plot 22", District.Empyreum, 17, 22)]
    [InlineData("Shirogane Ward 26, Plot 44.", District.Shirogane, 26, 44)]
    [InlineData("The Goblet Ward 16, Plot 5", District.Goblet, 16, 5)]
    public void ShowsHousingAddressesDespiteTyposAndSwaps(string location, District district, int ward, int plot) =>
        Assert.Equal(
            new HousingAddress("Alpha", WorldSource.Text, district, ward, plot),
            Shown(RowJson(location: JsonSerializer.Serialize(location))).Address);

    [Fact]
    public void ShowsAnApartment() =>
        Assert.Equal(
            new HousingAddress("Alpha", WorldSource.Text, District.Shirogane, 25, null, null, 29),
            Shown(RowJson(location: "\"Shirogane Ward 25, Apt. 29\"")).Address);

    [Fact]
    public void AWorldInTheLocationWinsOverTheServer() =>
        Assert.Equal("Siren", Shown(RowJson(location: "\"Siren Mist Ward 5, Plot 12\"")).Address?.World);

    [Fact]
    public void TheServerIsTrimmedForTheAddress() =>
        Assert.Equal("Alpha", Shown(RowJson(server: "\" Alpha \"")).Address?.World);

    [Theory]
    [InlineData("North Shroud, Hyrstmill")]
    [InlineData("Cen. Thanalan (21, 25)")]
    [InlineData("See URL for Location")]
    [InlineData("Emperyum Ward, Plot 17")]
    public void ShowsALocationWithoutAnAddressAsText(string location)
    {
        var shown = Shown(RowJson(location: JsonSerializer.Serialize("  " + location + " ")));

        Assert.Null(shown.Address);
        Assert.Equal(location, shown.Location);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    public void AnEmptyLocationIsNeitherAddressNorText(string location)
    {
        var shown = Shown(RowJson(location: location));

        Assert.Equal((null, null), (shown.Address, shown.Location));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"  \"")]
    [InlineData("null")]
    public void WithoutAServerAnAddressHasNoWorld(string server) =>
        Assert.Equal(
            new HousingAddress(null, WorldSource.Listing, District.Goblet, 2, 8),
            Shown(RowJson(server: server)).Address);

    [Fact]
    public void ShowingReadsTheCachedFieldsEachTime()
    {
        // Nothing derived is kept, so what is shown follows the cached fields as they are now
        var listing = Recorded(RowJson());
        listing.Title = "[T] Teatime";
        listing.Location = "North Shroud, Hyrstmill";
        listing.Rating = null;
        listing.LinkTexts = ["teatime.carrd.co"];

        var shown = RpEventCalendar.Show(listing, TestGazetteer.Parser);

        Assert.Equal(("Teatime", null, "North Shroud, Hyrstmill", "Teen"), (shown.Title, shown.Address, shown.Location, shown.Rating));
        Assert.Equal([new Uri("https://teatime.carrd.co")], shown.Links);
    }

    [Fact]
    public void ContactsAndTheDescriptionAreNotKept()
    {
        var cache = new ListingCache();
        var calendarEvent = Single(RowJson());
        RpEventCalendar.Record(cache, [calendarEvent], Now);

        Assert.DoesNotContain("Enhasa", calendarEvent.ToString(), StringComparison.Ordinal);
        var saved = string.Concat(SavedFiles.All(cache).Values);
        Assert.DoesNotContain("Enhasa", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("Thavnairian", saved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("false", true)]
    [InlineData("true", false)]
    [InlineData("null", false)]
    public void AnEventThatDoesntRecurIsAOneOff(string recurring, bool oneOff) =>
        Assert.Equal(oneOff, Single(RowJson(recurring: recurring)).OneOff);

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"not a date\"")]
    public void AnEventNeverValidatedHasNoValidationDate(string validated) =>
        Assert.Null(Single(RowJson(validated: validated)).LastValidated);

    [Fact]
    public void ShowsThePageLink() => Assert.Equal([new Uri("https://xiv.fyi/ZE2J")], Shown(RowJson()).Links);

    [Theory]
    [InlineData("\"javascript:alert(1)\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void ShowsOnlyWebLinks(string url)
    {
        // revert-check: not revert-sensitive (asserts links are dropped; ShowsThePageLink checks a good one is kept)
        Assert.Empty(Shown(RowJson(url: url)).Links);
    }

    [Fact]
    public void TitlesAreTheFirstLineCutLikeAVenuesName()
    {
        var name = new string('a', 70) + "\nsecond line";

        Assert.Equal(ListingDetails.Shorten(new string('a', 70)), Shown(RowJson(name: JsonSerializer.Serialize(name))).Title);
    }

    [Theory]
    [InlineData("[M] Coco's Oasis", "Coco's Oasis")]
    [InlineData("  [m]Coco's Oasis", "Coco's Oasis")]
    [InlineData("[T] Teatime", "Teatime")]
    [InlineData("[AO] After Dark", "After Dark")]
    [InlineData("[M]", "")]
    [InlineData("Coco's Oasis [M]", "Coco's Oasis [M]")]
    [InlineData("[Event] Coco's Oasis", "[Event] Coco's Oasis")]
    [InlineData("[MO] Coco's Oasis", "[MO] Coco's Oasis")]
    public void TheRatingTagIsNotPartOfTheShownTitle(string name, string title)
    {
        var listing = Recorded(RowJson(name: JsonSerializer.Serialize(name)));

        Assert.Equal(title, RpEventCalendar.Show(listing, TestGazetteer.Parser).Title);
        Assert.Equal(title, listing.ShownTitle);
        Assert.Equal(title, RpEventCalendar.Title(name));
    }

    [Fact]
    public void TheCacheKeepsTheNameAsSent() => Assert.Equal("[M] Coco's Oasis", Recorded(RowJson()).Title);

    [Fact]
    public void OtherSourcesShowTheirTitleAsKept()
    {
        var listing = new ListingCache().Seen(ListingSource.FfxivVenues, "v1", "[M] Lounge", Now);

        Assert.Equal("[M] Lounge", listing.ShownTitle);
    }

    [Fact]
    public void TheTagIsRemovedBeforeTheTitleIsCut()
    {
        var name = new string('a', 60);

        Assert.Equal(name, Shown(RowJson(name: JsonSerializer.Serialize("[M] " + name))).Title);
    }

    [Theory]
    [InlineData("\"Teen\"", "[M] Oasis", "Teen")]
    [InlineData("\" Mature \"", "Oasis", "Mature")]
    [InlineData("null", "[M] Oasis", "Mature")]
    [InlineData("\"\"", "[T] Oasis", "Teen")]
    [InlineData("null", "[AO] Oasis", "Adults Only")]
    [InlineData("null", "Oasis", null)]
    [InlineData("\"  \"", "Oasis", null)]
    public void TheRatingIsTheCalendarsElseTheTags(string rating, string name, string? expected) =>
        Assert.Equal(expected, Shown(RowJson(rating: rating, name: JsonSerializer.Serialize(name))).Rating);

    [Fact]
    public void AnEventWithoutANameIsUntitled()
    {
        var listing = Recorded(RowJson(name: "null"));

        Assert.Equal("", listing.Title);
        Assert.Equal("", RpEventCalendar.Show(listing, TestGazetteer.Parser).Title);
    }

    [Theory]
    [InlineData("null", "\"2026-11-15T04:00:00Z\"")]
    [InlineData("\"2026-11-15T01:00:00Z\"", "null")]
    [InlineData("\"2026-11-15T01:00:00Z\"", "\"2026-11-15T01:00:00Z\"")]
    [InlineData("\"2026-11-15T04:00:00Z\"", "\"2026-11-15T01:00:00Z\"")]
    [InlineData("\"tomorrow\"", "\"2026-11-15T04:00:00Z\"")]
    public void SkipsOccurrencesWithoutAStartBeforeTheEnd(string start, string end) =>
        Assert.Empty(Parse(RowJson(start: start, end: end)));

    [Fact]
    public void AnEventKeepsItsGoodOccurrencesWhenOthersAreSkipped() =>
        Assert.Single(Single(RowJson(start: "null") + "," + RowJson()).Openings);

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"  \"")]
    [InlineData("42")]
    public void SkipsRowsWithoutAnId(string uId) => Assert.Empty(Parse(RowJson(uId: uId)));

    [Fact]
    public void TrimsTheId() => Assert.Equal("abc", Single(RowJson(uId: "\" abc \"")).Id);

    [Fact]
    public void ARowOfTheWrongShapeDoesntStopTheOthers() =>
        Assert.Single(Parse("1", "null", "[]", RowJson(recurring: "\"yes\""), RowJson()));

    [Theory]
    [InlineData("{}")]
    [InlineData("\"events\"")]
    [InlineData("null")]
    public void AResponseThatIsntAListThrows(string json) =>
        Assert.ThrowsAny<JsonException>(() => RpEventCalendar.Parse(json));

    [Fact]
    public void RecordingKeepsEachEventsFieldsAsSent()
    {
        var cache = new ListingCache();
        var calendarEvent = Single(RowJson(recurring: "false"));

        RpEventCalendar.Record(cache, [calendarEvent], Now);

        var listing = cache.Find(ListingSource.RpEventCalendar, calendarEvent.Id)!;
        Assert.Equal("[M] Coco's Oasis", listing.Title);
        Assert.Equal("Alpha", listing.ListedOn);
        Assert.Equal("Goblet Ward 2, Plot 8", listing.Location);
        Assert.Equal("Mature", listing.Rating);
        Assert.Equal(["https://xiv.fyi/ZE2J"], listing.LinkTexts);
        Assert.Equal(calendarEvent.Openings, listing.Openings);
        Assert.Equal(calendarEvent.LastValidated, listing.LastValidated);
        Assert.True(listing.OneOff);
        Assert.Equal(Now, listing.LastSeen);
    }

    [Fact]
    public void RecordingAgainReplacesFieldsAndKeepsMarks()
    {
        var cache = new ListingCache();
        RpEventCalendar.Record(cache, [Single(RowJson())], Now.AddDays(-1));
        var listing = cache.Find(ListingSource.RpEventCalendar, "abc_R20260830T160000@google.com")!;
        listing.Favourite = true;

        RpEventCalendar.Record(cache, [Single(RowJson(name: "null", server: "null", location: "\"See URL\"", validated: "null", rating: "null"))], Now);

        Assert.Equal(1, cache.Count);
        Assert.Equal(("", null, "See URL", null, null), (listing.Title, listing.ListedOn, listing.Location, listing.Rating, listing.LastValidated));
        Assert.True(listing.Favourite);
        Assert.Equal(Now, listing.LastSeen);
    }

    [Fact]
    public void ShownAgainAfterSavingAndLoading()
    {
        var cache = new ListingCache();
        RpEventCalendar.Record(cache, [Single(RowJson())], Now);

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.RpEventCalendar, "abc_R20260830T160000@google.com")!;

        var shown = RpEventCalendar.Show(back, TestGazetteer.Parser);

        Assert.Equal(Shown(RowJson()) with { Links = [] }, shown with { Links = [] });
        Assert.Equal(Shown(RowJson()).Links, shown.Links);
    }
}
