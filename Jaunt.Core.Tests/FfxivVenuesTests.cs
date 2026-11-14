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

    [Fact]
    public void ReadsAVenuesNameAndAddress()
    {
        var venue = Single(VenueJson());

        Assert.Equal("abc123", venue.Id);
        Assert.Equal("The Test Lounge", venue.Name);
        Assert.Equal(new HousingAddress("Cuchulainn", WorldSource.Text, District.Mist, 14, 33, Subdivision: true), venue.Address);
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

        FfxivVenues.Record(cache, [new FfxivVenues.Venue("a", "New name", address)], Now);

        var listing = Assert.Single(cache.Listings);
        Assert.Equal(
            (ListingSource.FfxivVenues, "New name", address, Now, true, false),
            (listing.Source, listing.Title, listing.Address, listing.LastSeen, listing.Favourite, listing.OneOff));
    }

    [Fact]
    public void AVenueThatLosesItsAddressLosesItInTheCache()
    {
        var cache = new ListingCache();
        FfxivVenues.Record(cache, [new FfxivVenues.Venue("a", "A", new HousingAddress("Goblin", WorldSource.Text, District.Mist, 1, 2))], Now.AddDays(-1));

        FfxivVenues.Record(cache, [new FfxivVenues.Venue("a", "A", null)], Now);

        Assert.Null(Assert.Single(cache.Listings).Address);
    }
}
