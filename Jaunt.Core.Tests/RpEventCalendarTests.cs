using System.Text.Json;
using Jaunt.Core.Listings;

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

    [Fact]
    public void ContactsAndTheDescriptionAreNotKept()
    {
        var cache = new ListingCache();
        var calendarEvent = Single(RowJson());
        RpEventCalendar.Record(cache, [calendarEvent], Now);

        Assert.DoesNotContain("Enhasa", calendarEvent.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Enhasa", cache.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("Thavnairian", cache.ToJson(), StringComparison.Ordinal);
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
    public void TheCacheKeepsTheNameAsSent() => Assert.Equal("[M] Coco's Oasis", Recorded(RowJson()).Title);

    [Fact]
    public void AnEventWithoutANameHasAnEmptyTitle()
    {
        var listing = Recorded(RowJson(name: "null"));

        Assert.Equal("", listing.Title);
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
}
