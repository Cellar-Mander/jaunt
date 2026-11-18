using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class PlaceholdersTests
{
    [Theory]
    [InlineData(Interests.None, Interests.None)]
    [InlineData(Interests.Chill, Interests.Chill)]
    [InlineData(Interests.Tarot, Interests.Tarot)]
    [InlineData(Interests.Cafes | Interests.Roleplay, Interests.Cafes)]
    [InlineData(Interests.Bars | Interests.Clubs | Interests.LiveShows, Interests.Clubs)]
    [InlineData(Interests.Bars | Interests.Cafes, Interests.Bars)]
    [InlineData(Interests.Cafes | Interests.Spa, Interests.Cafes)]
    [InlineData(Interests.Spa | Interests.Gambling, Interests.Spa)]
    [InlineData(Interests.Gambling | Interests.LiveShows, Interests.Gambling)]
    [InlineData(Interests.LiveShows | Interests.Games, Interests.LiveShows)]
    [InlineData(Interests.Games | Interests.Art, Interests.Games)]
    [InlineData(Interests.Art | Interests.Photography, Interests.Art)]
    [InlineData(Interests.Photography | Interests.Tarot, Interests.Photography)]
    [InlineData(Interests.Tarot | Interests.Roleplay, Interests.Tarot)]
    [InlineData(Interests.Roleplay | Interests.Chill, Interests.Roleplay)]
    public void IconInterestPicksVenueTypeFirst(Interests interests, Interests expected) =>
        Assert.Equal(expected, Placeholders.IconInterest(interests));

    [Fact]
    public void IconInterestIgnoresUnknownBits() => Assert.Equal(Interests.None, Placeholders.IconInterest((Interests)(1 << 20)));

    [Fact]
    public void EveryInterestHasAnIcon()
    {
        foreach (var (interest, _) in ListingFilters.Labels)
            Assert.Equal(interest, Placeholders.IconInterest(interest));
    }

    private static readonly Backdrops Art = new(
        new Dictionary<District, string> { [District.Mist] = "limsa", [District.Shirogane] = "kugane" },
        [
            ("Limsa Lominsa Lower Decks", "limsa"), ("Mist", "limsa"), ("Central Shroud", "gridania"), ("East Shroud", "gridania"),
            ("Central Thanalan", "uldah"), ("Eastern Thanalan", "uldah-east"), ("Ul'dah - Steps of Nald", "uldah"),
            ("The Pillars", "ishgard"), ("Coerthas Central Highlands", "coerthas"), ("Coerthas", "too-short"), ("Kugane", "kugane"), ("", "nameless"),
        ],
        "map");

    private static HousingAddress Address(District? district) => new("Alpha", WorldSource.Text, district, 3, 4);

    [Fact]
    public void BackdropIsTheFirstDistrictWithArt() =>
        Assert.Equal("kugane", Art.For([Address(null), Address(District.Goblet), Address(District.Shirogane), Address(District.Mist)], ["The Pillars"]));

    [Fact]
    public void BackdropFallsBackToTheLocation()
    {
        Assert.Equal("gridania", Art.For([Address(District.Goblet)], [null, "  ", "See URL", "Central Shroud, The Bannock"]));
        Assert.Equal("gridania", Art.For([], ["Central Shroud"]));
    }

    [Theory]
    [InlineData("Central Shroud, The Bannock", "gridania")]
    [InlineData("central shroud (21, 25)", "gridania")]
    [InlineData("The Pillars, Saint Reymanaud's Cathedral", "ishgard")]
    [InlineData("Limsa Lominsa Lower Decks, The Astalicia (10, 29)", "limsa")]
    [InlineData("Kugane, See URL for Specifics", "kugane")]
    [InlineData("Kugane", "kugane")]
    [InlineData("Ul’dah - Steps of Nald", "uldah")]
    [InlineData("Ul'dah: Steps of Nald", "uldah")]
    [InlineData("Cen. Thanalan (21, 25)", "uldah")]
    [InlineData("E. Thanalan", "uldah-east")]
    [InlineData("Coerthas Central Highlands, Camp Dragonhead", "coerthas")]
    [InlineData("Coerthas Western Highlands", "too-short")]
    public void BackdropMatchesTheZoneTheLocationStartsWith(string location, string expected) => Assert.Equal(expected, Art.For([], [location]));

    [Theory]
    [InlineData("See URL for Location")]
    [InlineData("Shroud")]
    [InlineData("The Bannock, Central Shroud")]
    [InlineData("Central")]
    [InlineData("Cen Thanalan")]
    [InlineData("Kuganes")]
    [InlineData("Limsa Lominsa")]
    [InlineData("")]
    public void BackdropIsTheMapForOtherLocations(string location) => Assert.Equal("map", Art.For([Address(District.Goblet)], [location]));

    [Fact]
    public void BackdropIsTheMapWithoutAPlace() => Assert.Equal("map", Art.For([], []));

    [Fact]
    public void BackdropIsNullWithoutAnyArt() =>
        Assert.Null(new Backdrops(new Dictionary<District, string>(), [], null).For([Address(District.Mist)], ["Central Shroud"]));
}
