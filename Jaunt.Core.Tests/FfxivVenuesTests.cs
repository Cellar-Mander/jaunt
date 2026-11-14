using System.Text.Json;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class FfxivVenuesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private const string Location = """
        {"dataCenter":"Dynamis","world":"Cuchulainn","district":"Mist","ward":14,"plot":33,"apartment":0,"room":0,"subdivision":true,"shard":null,"override":null}
        """;

    /// <summary>One venue in the API's shape, trimmed to the fields Jaunt reads plus a few it ignores.</summary>
    private static string VenueJson(
        string id = "abc123",
        string name = "The Test Lounge",
        string location = Location,
        string website = "\"https://lounge.carrd.co/\"",
        string discord = "\"https://discord.gg/lounge\"",
        string schedule = "[]",
        string approved = "true") => $$"""
        {"id":"{{id}}","name":{{JsonSerializer.Serialize(name)}},"location":{{location}},"website":{{website}},"discord":{{discord}},
         "hiring":false,"sfw":true,"schedule":{{schedule}},"scheduleOverrides":[],"managers":["1"],"tags":["Bar"],"approved":{{approved}}}
        """;

    private static FfxivVenues.Venue Single(string venueJson) =>
        Assert.Single(FfxivVenues.Parse($"[{venueJson}]", TestGazetteer.Instance));

    private static string Opening(int day, string start, string end, int type = 0, int argument = 1, bool nextDay = false) =>
        $$"""
        {"day":{{day}},"start":{"hour":{{int.Parse(start[..2])}},"minute":{{int.Parse(start[3..])}},"timeZone":"UTC","nextDay":false},
         "end":{"hour":{{int.Parse(end[..2])}},"minute":{{int.Parse(end[3..])}},"timeZone":"UTC","nextDay":{{(nextDay ? "true" : "false")}} },
         "interval":{"intervalType":{{type}},"intervalArgument":{{argument}} } }
        """;

    private static string Week(Func<int, string> opening) => "[" + string.Join(",", Enumerable.Range(0, 7).Select(opening)) + "]";

    [Fact]
    public void ReadsAVenuesNameAddressAndLinks()
    {
        var venue = Single(VenueJson());

        Assert.Equal("abc123", venue.Id);
        Assert.Equal("The Test Lounge", venue.Name);
        Assert.Equal(new HousingAddress("Cuchulainn", WorldSource.Text, District.Mist, 14, 33, Subdivision: true), venue.Address);
        Assert.Equal([new Uri("https://lounge.carrd.co/"), new Uri("https://discord.gg/lounge")], venue.Links);
        Assert.False(venue.AlwaysOpen);
    }

    [Fact]
    public void ReadsAnApartmentWithItsSubdivisionAndRoom()
    {
        var venue = Single(VenueJson(location: """
            {"world":"Goblin","district":"Lavender Beds","ward":5,"plot":0,"apartment":12,"room":3,"subdivision":true}
            """));

        Assert.Equal(new HousingAddress("Goblin", WorldSource.Text, District.LavenderBeds, 5, null, true, 12, "3"), venue.Address);
    }

    [Theory]
    [InlineData("Mist", District.Mist)]
    [InlineData("Lavender Beds", District.LavenderBeds)]
    [InlineData("The Lavender Beds", District.LavenderBeds)]
    [InlineData("Goblet", District.Goblet)]
    [InlineData("The Goblet", District.Goblet)]
    [InlineData("shirogane", District.Shirogane)]
    [InlineData(" Empyreum ", District.Empyreum)]
    public void ReadsEachDistrict(string written, District district)
    {
        var venue = Single(VenueJson(location: $$"""{"world":"Goblin","district":"{{written}}","ward":1,"plot":1}"""));

        Assert.Equal(district, venue.Address!.District);
    }

    [Theory]
    [InlineData("Cuchulainn", "Cuchulainn")]
    [InlineData("cuchulainn", "Cuchulainn")]
    [InlineData("Bismark", "Bismarck")] // a misspelling the site has
    [InlineData("Sagittarus", "Sagittarius")]
    [InlineData("Bisma", "Bisma")] // a prefix only: nothing left to correct
    [InlineData("Phoenx", "Phoenix")] // five letters in common
    [InlineData("Phoxnix", "Phoxnix")] // only three letters in common with Phoenix
    [InlineData("Novaworld", "Novaworld")] // a world the game data doesn't have yet
    public void UsesTheGamesSpellingOfTheWorld(string written, string world)
    {
        var venue = Single(VenueJson(location: $$"""{"world":"{{written}}","district":"Mist","ward":1,"plot":1}"""));

        Assert.Equal(world, venue.Address!.World);
    }

    [Theory]
    [InlineData("""{"world":null,"district":null,"ward":0,"plot":0,"override":"Gridania Plaza"}""")]
    [InlineData("""{"world":"","district":"Mist","ward":1,"plot":1}""")]
    [InlineData("""{"world":"Goblin","district":"Ul'dah","ward":1,"plot":1}""")]
    [InlineData("""{"world":"Goblin","district":null,"ward":1,"plot":1}""")]
    [InlineData("""{"world":"Goblin","district":"Mist","ward":0,"plot":1}""")]
    [InlineData("""{"world":"Goblin","district":"Mist","ward":31,"plot":1}""")]
    [InlineData("null")]
    public void AVenueWithoutAHousingAddressHasNone(string location)
    {
        var venue = Single(VenueJson(location: location));

        Assert.Null(venue.Address);
        Assert.Equal("The Test Lounge", venue.Name);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 60)]
    public void WardsAndPlotsAtTheirLimitsAreRead(int ward, int plot)
    {
        var venue = Single(VenueJson(location: $$"""{"world":"Goblin","district":"Mist","ward":{{ward}},"plot":{{plot}}}"""));

        Assert.Equal((ward, plot), (venue.Address!.Ward, venue.Address.Plot));
    }

    [Theory]
    [InlineData(0, 0, null, null)]
    [InlineData(61, 0, null, null)]
    [InlineData(61, 7, null, 7)]
    [InlineData(-1, -1, null, null)]
    [InlineData(20, 7, 20, null)] // a plot wins over an apartment
    public void APlotOutOfRangeIsLeftOut(int plot, int apartment, int? expectedPlot, int? expectedApartment)
    {
        var venue = Single(VenueJson(location: $$"""{"world":"Goblin","district":"Mist","ward":3,"plot":{{plot}},"apartment":{{apartment}}}"""));

        Assert.Equal((expectedPlot, expectedApartment), (venue.Address!.Plot, venue.Address.Apartment));
    }

    [Theory]
    [InlineData("null", "null")]
    [InlineData("\"\"", "\"  \"")]
    [InlineData("\"javascript:alert(1)\"", "\"ftp://files.example.com\"")]
    [InlineData("\"file:///C:/Windows\"", "\"not a link\"")]
    public void KeepsOnlyWebsiteLinks(string website, string discord)
    {
        var venue = Single(VenueJson(website: website, discord: discord));

        Assert.Empty(venue.Links);
    }

    [Fact]
    public void ALinkWithoutASchemeOpensAsHttps()
    {
        var venue = Single(VenueJson(website: "\"lounge.carrd.co\"", discord: "\"discord.gg/abc\""));

        Assert.Equal([new Uri("https://lounge.carrd.co"), new Uri("https://discord.gg/abc")], venue.Links);
    }

    [Fact]
    public void AnHttpLinkStaysHttp()
    {
        var venue = Single(VenueJson(website: "\"http://lounge.example.com/\"", discord: "null"));

        Assert.Equal([new Uri("http://lounge.example.com/")], venue.Links);
    }

    [Fact]
    public void TheSameLinkTwiceIsKeptOnce()
    {
        var venue = Single(VenueJson(website: "\"https://discord.gg/abc\"", discord: "\"https://discord.gg/abc\""));

        Assert.Equal([new Uri("https://discord.gg/abc")], venue.Links);
    }

    [Theory]
    [InlineData("00:00", "23:59", false)]
    [InlineData("00:00", "00:00", true)]
    [InlineData("12:00", "12:00", true)]
    [InlineData("12:00", "11:59", true)]
    [InlineData("20:00", "19:59", true)]
    public void AVenueOpenAWholeDayEveryDayIsAlwaysOpen(string start, string end, bool nextDay)
    {
        var venue = Single(VenueJson(schedule: Week(day => Opening(day, start, end, nextDay: nextDay))));

        Assert.True(venue.AlwaysOpen);
    }

    [Theory]
    [InlineData("00:00", "23:58", false)] // a minute short
    [InlineData("00:01", "23:59", false)]
    [InlineData("20:00", "02:00", true)]
    [InlineData("12:00", "11:58", true)]
    public void AShorterOpeningIsNotAlwaysOpen(string start, string end, bool nextDay)
    {
        var venue = Single(VenueJson(schedule: Week(day => Opening(day, start, end, nextDay: nextDay))));

        Assert.False(venue.AlwaysOpen);
    }

    [Fact]
    public void OneShortDayMeansNotAlwaysOpen()
    {
        var venue = Single(VenueJson(schedule: Week(day => Opening(day, "00:00", day == 3 ? "22:00" : "23:59"))));

        Assert.False(venue.AlwaysOpen);
    }

    [Fact]
    public void SixWholeDaysAreNotAlwaysOpenEvenIfOneIsListedTwice()
    {
        var days = Enumerable.Range(0, 6).Select(day => Opening(day, "00:00", "23:59")).Append(Opening(5, "00:00", "23:59"));

        var venue = Single(VenueJson(schedule: "[" + string.Join(",", days) + "]"));

        Assert.False(venue.AlwaysOpen);
    }

    [Theory]
    [InlineData(0, 2)] // every other week
    [InlineData(1, 1)] // the first week of each month
    [InlineData(1, -1)] // the last week of each month
    public void WholeDaysThatDontRepeatEveryWeekAreNotAlwaysOpen(int type, int argument)
    {
        var venue = Single(VenueJson(schedule: Week(day => Opening(day, "00:00", "23:59", type, argument))));

        Assert.False(venue.AlwaysOpen);
    }

    [Fact]
    public void DaysOutsideTheWeekDontCount()
    {
        var days = Enumerable.Range(1, 7).Select(day => Opening(day, "00:00", "23:59"));

        var venue = Single(VenueJson(schedule: "[" + string.Join(",", days) + "]"));

        Assert.False(venue.AlwaysOpen);
    }

    [Fact]
    public void AnOpeningWithNoEndIsNotAWholeDay()
    {
        var schedule = Week(day => $$"""
            {"day":{{day}},"start":{"hour":0,"minute":0,"nextDay":false},"end":null,"interval":{"intervalType":0,"intervalArgument":1} }
            """);

        var venue = Single(VenueJson(schedule: schedule));

        Assert.False(venue.AlwaysOpen);
    }

    [Fact]
    public void AVenueWithNoScheduleIsReadAndNotAlwaysOpen()
    {
        var venue = Single(VenueJson(schedule: "null"));

        Assert.False(venue.AlwaysOpen);
        Assert.NotNull(venue.Address);
    }

    [Fact]
    public void AMalformedVenueIsSkippedAndTheRestRead()
    {
        var venues = FfxivVenues.Parse(
            $"[{VenueJson(id: "a")}, {VenueJson(id: "b", location: """{"world":"Goblin","ward":"fourteen"}""")}, 42, null, {VenueJson(id: "c")}]",
            TestGazetteer.Instance);

        Assert.Equal(["a", "c"], venues.Select(v => v.Id));
    }

    [Theory]
    [InlineData("\"\"", "true")]
    [InlineData("\"  \"", "true")]
    [InlineData("null", "true")]
    [InlineData("\"x\"", "false")]
    public void AVenueWithoutAnIdOrNotApprovedIsSkipped(string id, string approved)
    {
        var json = VenueJson(approved: approved).Replace("\"id\":\"abc123\"", $"\"id\":{id}", StringComparison.Ordinal);

        Assert.Empty(FfxivVenues.Parse($"[{json}]", TestGazetteer.Instance));
    }

    [Fact]
    public void AVenueThatDoesntSayWhetherItIsApprovedIsRead()
    {
        var json = VenueJson().Replace(",\"approved\":true", "", StringComparison.Ordinal);

        Assert.Single(FfxivVenues.Parse($"[{json}]", TestGazetteer.Instance));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("\"venues\"")]
    [InlineData("<html>Too many requests</html>")]
    public void AResponseThatIsntAListOfVenuesIsAnError(string json) =>
        Assert.ThrowsAny<JsonException>(() => FfxivVenues.Parse(json, TestGazetteer.Instance));

    [Theory]
    [InlineData("  The Test Lounge  ", "The Test Lounge")]
    [InlineData("The Test Lounge\nBar & Grill", "The Test Lounge")]
    [InlineData("The Test Lounge \r\nBar", "The Test Lounge")]
    [InlineData("A very long venue name that goes on and on past sixty characters", "A very long venue name that goes on and on past sixty chara…")]
    public void TitlesAreTheNamesFirstLineCutToSixtyCharacters(string name, string title) =>
        Assert.Equal(title, Single(VenueJson(name: name)).Name);

    [Fact]
    public void AVenueWithNoNameHasAnEmptyTitle()
    {
        var json = VenueJson().Replace("\"name\":\"The Test Lounge\"", "\"name\":null", StringComparison.Ordinal);

        Assert.Equal("", Single(json).Name);
    }

    [Fact]
    public void RecordingUpdatesAVenueAlreadyCachedAndKeepsItsMarks()
    {
        var cache = new ListingCache();
        var address = new HousingAddress("Goblin", WorldSource.Text, District.Mist, 1, 2);
        cache.Seen(ListingSource.FfxivVenues, "a", "Old name", Now.AddDays(-1)).Favourite = true;

        FfxivVenues.Record(cache, [new FfxivVenues.Venue("a", "New name", address, [new Uri("https://a.example.com")], AlwaysOpen: true)], Now);

        var listing = Assert.Single(cache.Listings);
        Assert.Equal(
            (ListingSource.FfxivVenues, "New name", address, true, Now, true, false),
            (listing.Source, listing.Title, listing.Address, listing.AlwaysOpen, listing.LastSeen, listing.Favourite, listing.OneOff));
        Assert.Equal([new Uri("https://a.example.com")], listing.Links);
    }

    [Fact]
    public void AVenueThatLosesItsAddressAndLinksLosesThemInTheCache()
    {
        var cache = new ListingCache();
        var address = new HousingAddress("Goblin", WorldSource.Text, District.Mist, 1, 2);
        FfxivVenues.Record(cache, [new FfxivVenues.Venue("a", "A", address, [new Uri("https://a.example.com")], AlwaysOpen: true)], Now.AddDays(-1));

        FfxivVenues.Record(cache, [new FfxivVenues.Venue("a", "A", null, [], AlwaysOpen: false)], Now);

        var listing = Assert.Single(cache.Listings);
        Assert.Equal((null, 0, false), (listing.Address, listing.Links.Count, listing.AlwaysOpen));
    }
}
