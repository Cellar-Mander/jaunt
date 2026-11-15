using System.Text.Json;
using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class PlaceMarkCacheTests
{
    private static readonly AddressKey Tavern = new("SIREN", District.Empyreum, 16, 20, null, false);
    private static readonly AddressKey NewHouse = new("SIREN", District.Mist, 3, 7, null, false);

    private readonly ListingCache cache = new();

    [Fact]
    public void MovingAPlaceTakesItsMarksToTheNewAddressAndIsSaved()
    {
        var place = cache.Place(Tavern);
        place.Favourite = true;
        cache.TakeChanges();

        Assert.Same(place, cache.Move(place, NewHouse));

        Assert.Same(place, cache.PlaceAt(NewHouse));
        Assert.Null(cache.PlaceAt(Tavern));
        Assert.True(cache.MarksUnsaved);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MovingOntoAMarkedPlaceKeepsEveryMarkOfEither(bool moving, bool there)
    {
        var from = cache.Place(Tavern);
        (from.Favourite, from.Visited, from.Hidden) = (moving, moving, moving);
        from.VenueIds.Add("v");
        from.Websites.Add("tavern.carrd.co");
        from.Discords.Add("tavern");
        var to = cache.Place(NewHouse);
        (to.Favourite, to.Visited, to.Hidden) = (there, there, there);

        var merged = cache.Move(from, NewHouse);

        Assert.Same(to, merged);
        Assert.True(merged.Favourite && merged.Visited && merged.Hidden);
        Assert.Equal(["v"], merged.VenueIds);
        Assert.Equal(["tavern.carrd.co"], merged.Websites);
        Assert.Equal(["tavern"], merged.Discords);
        Assert.Null(from.Owner);
        Assert.Equal(NewHouse, from.Address);
    }

    [Fact]
    public void HiddenPlacesAreSortedByTitleIgnoringCase()
    {
        var b = cache.Place(Tavern);
        b.Hidden = true;
        b.Title = "bar";
        var a = cache.Place(NewHouse);
        a.Hidden = true;
        a.Title = "Arbour";
        cache.Place(new AddressKey("SIREN", District.Goblet, 1, 1, null, false)).Favourite = true;

        Assert.Equal([a, b], cache.HiddenPlaces());
    }

    [Fact]
    public void AnUnmarkedPlaceIsForgottenAtTheNextSave()
    {
        var place = cache.Place(Tavern);
        place.Favourite = true;
        place.Websites.Add("tavern.carrd.co");
        place.Favourite = false;

        cache.TakeChanges();

        Assert.Null(place.Owner);
        Assert.Empty(cache.Place(Tavern).Websites);
    }

    [Fact]
    public void ThePlacesFileListsWhatEachSawInOrder()
    {
        var place = cache.Place(Tavern);
        place.Favourite = true;
        place.VenueIds.UnionWith(["b", "a"]);
        place.Websites.UnionWith(["b.example", "a.example"]);
        place.Discords.UnionWith(["b", "a"]);

        var saved = JsonDocument.Parse(SavedFiles.All(cache)[ListingCache.MarksFile]).RootElement.GetProperty("places")[0];

        foreach (var name in (string[])["venueIds", "websites", "discords"])
            Assert.Equal("a", saved.GetProperty(name)[0].GetString()![..1]);
    }

    [Theory]
    [InlineData("""{"world":"Siren","district":"Mist","ward":1,"plot":60}""", "SIREN Mist W1 P60")]
    [InlineData("""{"world":"Siren","district":"Mist","ward":30,"plot":1,"subdivision":true}""", "SIREN Mist W30 P1")]
    [InlineData("""{"world":"Siren","district":"Mist","ward":5,"apartment":1,"subdivision":true}""", "SIREN Mist W5 A1 sub")]
    [InlineData("""{"world":"Siren","district":"Mist","ward":5,"apartment":2}""", "SIREN Mist W5 A2")]
    [InlineData("""{"world":"Siren","district":"Mist","ward":0,"plot":1}""", null)]
    [InlineData("""{"world":"Siren","district":"Mist","ward":31,"plot":1}""", null)]
    [InlineData("""{"world":"Siren","district":"Mist","ward":1,"plot":61}""", null)]
    [InlineData("""{"world":"Siren","district":"Mist","ward":1,"plot":0}""", null)]
    [InlineData("""{"world":"Siren","district":"Mist","ward":1,"apartment":0}""", null)]
    [InlineData("""{"world":"Siren","district":"Mist","ward":1}""", null)]
    [InlineData("""{"world":"Siren","district":"Mist","ward":1,"plot":1,"apartment":1}""", null)]
    [InlineData("""{"world":" ","district":"Mist","ward":1,"plot":1}""", null)]
    [InlineData("""{"world":"Siren","district":"Moon","ward":1,"plot":1}""", null)]
    [InlineData("""{"world":"Siren","district":"1","ward":1,"plot":1}""", null)]
    public void AHandEditedPlaceLoadsOnlyWithAWholeAddress(string address, string? expected)
    {
        var json = $$"""{"version":1,"marks":[],"places":[{{address[..^1]}},"favourite":true}]}""";

        var places = SavedFiles.Load(new Dictionary<string, string> { [ListingCache.MarksFile] = json }).Places.ToList();

        Assert.Equal(expected, places.SingleOrDefault()?.Address.ToString());
    }

    [Fact]
    public void ASavedPlaceWithNoMarkIsDropped()
    {
        const string json = """{"version":1,"places":[{"world":"Siren","district":"Mist","ward":1,"plot":1}]}""";

        Assert.Empty(SavedFiles.Load(new Dictionary<string, string> { [ListingCache.MarksFile] = json }).Places);
    }
}
