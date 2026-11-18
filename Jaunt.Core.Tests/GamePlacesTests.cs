using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class GamePlacesTests
{
    private readonly GamePlaces places = TestGamePlaces.Create();

    // Event locations as the RP calendar sent them
    [Theory]
    [InlineData("Central Shroud, The Bannock", "Central Shroud · The Bannock (23.2, 19.8)")]
    [InlineData("North Shroud, Hyrstmill", "North Shroud · Hyrstmill (28.5, 19.7)")]
    [InlineData("North Shroud, Fallgourd Float", "North Shroud · Fallgourd Float (21.0, 26.8)")]
    [InlineData("Old Gridania, Apkallu Falls", "Old Gridania · Apkallu Falls (11.1, 6.4)")]
    [InlineData("The Pillars, Saint Reymanaud's Cathedral", "The Pillars · Saint Reymanaud's Cathedral (8.3, 7.8)")]
    // A label written without its leading "The"
    [InlineData("Western Thanalan, Silver Bazaar", "Western Thanalan · The Silver Bazaar (16.1, 29.9)")]
    // The longest label wins
    [InlineData("Western Thanalan, Silver Bazaar Docks", "Western Thanalan · Silver Bazaar Docks (15.4, 27.6)")]
    // A label over two lines is matched by its first, and shown on one
    [InlineData("Central Shroud - Everschade", "Central Shroud · Everschade (The Guardian Tree) (15.0, 25.2)")]
    // Coordinates win over the label's own position
    [InlineData("Limsa Lominsa Lower Decks, The Astalicia (10, 29)", "Limsa Lominsa Lower Decks · The Astalicia (10.0, 29.0)")]
    [InlineData("Cen. Thanalan (21, 25)", "Central Thanalan (21.0, 25.0)")]
    [InlineData("North Shroud (29, 23)", "North Shroud (29.0, 23.0)")]
    // A zone alone counts in a location, with nowhere on its map
    [InlineData("Kugane, See URL for Specifics", "Kugane")]
    [InlineData("Shaaloani (See URL)", "Shaaloani")]
    [InlineData("Pillars", "The Pillars")]
    // Text the place's label doesn't follow straight after isn't the label
    [InlineData("Central Shroud. The Bannock", "Central Shroud")]
    public void FindsThePlaceInALocation(string location, string expected) =>
        Assert.Equal([expected], places.InLocation(location, null).Select(p => p.Display()));

    [Theory]
    [InlineData("See URL")]
    [InlineData("See URL for Location")]
    [InlineData("")]
    [InlineData(null)]
    // A housing district is an address, not a place outside housing
    [InlineData("Mist W5 P12")]
    // A city's name inside a longer word
    [InlineData("Ishgard's finest pub")]
    // Only a leading "The" may be left out
    [InlineData("Shroud")]
    public void FindsNoPlaceInALocation(string? location) =>
        Assert.Empty(places.InLocation(location, "Lich"));

    [Fact]
    public void ALocationsWorldIsTheEventsWorld()
    {
        var place = Assert.Single(places.InLocation("North Shroud, Hyrstmill", "Lich"));
        Assert.Equal("Lich", place.World);
        Assert.False(place.ListedWorld);
        Assert.Equal("Lich · North Shroud · Hyrstmill (28.5, 19.7)", place.Display());
    }

    // Ad texts from the corpus and the cache
    [Theory]
    [InlineData("Hello!  Elezen  meet up @  Limsa Lominsa Lower Decks  X 9.2 Y 10.8. Bored?", "Limsa Lominsa Lower Decks (9.2, 10.8)")]
    [InlineData("Hello!  meet up @ LimsaLominsaLowerDecks X 9.2 Y 10.8. Bored? Come by", "Limsa Lominsa Lower Decks (9.2, 10.8)")]
    [InlineData("Come hang out with your fellow s! Adamantoise Limsa at X9.2 Y10.8 on the big rock!", "Limsa Lominsa Lower Decks (9.2, 10.8)")]
    [InlineData("please let me dote on you for 50k (Exo, limsa, 10.0, 12.0) </3", "Limsa Lominsa Lower Decks (10.0, 12.0)")]
    [InlineData(" ・ TONIGHT 7:30PM AEDT ・ Sophia, Limsa Aetheryte ・ Flex your brains", "Limsa Lominsa Lower Decks · Aetheryte (9.6, 11.2)")]
    [InlineData("Meet at the Gridania aetheryte plaza (12, 13) tonight", "New Gridania · Aetheryte (12.0, 13.0)")]
    [InlineData("Meet by the Ul'dah Aetheryte.", "Ul'dah - Steps of Nald · Aetheryte (8.4, 7.9)")]
    [InlineData("Picnic in Central Shroud, The Bannock! 8pm", "Central Shroud · The Bannock (23.2, 19.8)")]
    [InlineData("Mor Dhona: x 22 , y 8", "Mor Dhona (22.0, 8.0)")]
    // What players call a city
    [InlineData("Limsa Lominsa (9.2, 10.8)", "Limsa Lominsa Lower Decks (9.2, 10.8)")]
    [InlineData("Uldah aetheryte", "Ul'dah - Steps of Nald · Aetheryte (8.4, 7.9)")]
    [InlineData("Ishgard aetheryte", "Foundation · Aetheryte (10.0, 12.1)")]
    [InlineData("Kugane (X: 10.5, Y: 10.2) at 9pm", "Kugane (10.5, 10.2)")]
    [InlineData("LimsaLominsaLowerDecks X 9.2 Y 10.8", "Limsa Lominsa Lower Decks (9.2, 10.8)")]
    [InlineData("Limsa X 9.2 Y 10.", "Limsa Lominsa Lower Decks (9.2, 10.0)")]
    [InlineData("Kugane (1, 1)", "Kugane (1.0, 1.0)")]
    // A "p" alone isn't pm
    [InlineData("Kugane 9, 10p", "Kugane (9.0, 10.0)")]
    public void FindsThePlaceInAnAd(string text, string expected) =>
        Assert.Equal([expected], places.InAd(text, null).Select(p => p.Display()));

    [Theory]
    // A city named for a theme, a player's progress or an inn, with nowhere on its map
    [InlineData("[RP 18+] MIDNIGHT GOLD LOUNGE Ishgard's finest pub and smoking lounge! 9pm")]
    [InlineData("Wyrm's Den is open! Little fight cage and bar in Ishgard. Shy/New Welcome.")]
    [InlineData("Looking for help levelling Blue Mage. Mor Dhona is my highest point. 1000 Gil per level")]
    [InlineData("rie's weekly Lalafell march. tonight @ 830pm EST, Gridania inn | Falling into Fall!")]
    // Two bare numbers need a comma, and a time or a date isn't coordinates
    [InlineData("Kugane 10 12 tonight")]
    [InlineData("Kugane 9, 10pm")]
    [InlineData("Kugane 11/20 at 9")]
    [InlineData("Kugane 9:30, 10")]
    [InlineData("Kugane (10, 9:30)")]
    [InlineData("Kugane 9, 10 p.m.")]
    [InlineData("Kugane 9, 10 pm.")]
    // One label isn't enough without a comma, and a number needs digits after its point
    [InlineData("Kugane X 10 12")]
    [InlineData("Kugane (10. 12)")]
    // Text ending part way
    [InlineData("Meet in Kugane at")]
    [InlineData("Kugane 10")]
    [InlineData("Kugane x")]
    // Off the map
    [InlineData("Limsa (0, 12)")]
    [InlineData("Limsa (12, 50)")]
    [InlineData("Limsa (43, 12)")]
    [InlineData("Limsa (12, 43)")]
    // Housing, which addresses cover
    [InlineData("Hanging out in Lavender Beds - x10 y15 - Ward 2 - Omega")]
    // Aetheryte only for a zone whose aetheryte is on its own map
    [InlineData("Old Gridania aetheryte")]
    public void FindsNoPlaceInAnAd(string text) =>
        Assert.Empty(places.InAd(text, "Siren"));

    [Fact]
    public void AnAdsWorldIsTheOneItIsListedOn()
    {
        var place = Assert.Single(places.InAd("Limsa X 9.2 Y 10.8", "Siren"));
        Assert.Equal("Siren", place.World);
        Assert.True(place.ListedWorld);

        Assert.False(Assert.Single(places.InAd("Limsa X 9.2 Y 10.8", null)).ListedWorld);
    }

    [Fact]
    public void FindsEachPlaceOnce()
    {
        var found = places.InAd("Limsa X 9.2 Y 10.8! Again: Limsa Lominsa Lower Decks (9.2, 10.8). Or Kugane aetheryte", null);
        Assert.Equal(["Limsa Lominsa Lower Decks (9.2, 10.8)", "Kugane · Aetheryte (12.2, 10.5)"], found.Select(p => p.Display()));
    }

    [Fact]
    public void FindsSeveralPlacesInALocation()
    {
        var found = places.InLocation("North Shroud, Hyrstmill then Central Shroud", null);
        Assert.Equal(["North Shroud · Hyrstmill (28.5, 19.7)", "Central Shroud"], found.Select(p => p.Display()));
    }

    [Theory]
    [InlineData("Lich", "Central Shroud", "Lich, tp Central Shroud")]
    [InlineData(null, "Central Shroud", "tp Central Shroud")]
    [InlineData("", "Central Shroud", "tp Central Shroud")]
    // A zone without an aetheryte of its own teleports to the one serving it
    [InlineData("Lich", "Old Gridania, Apkallu Falls", "Lich, tp New Gridania")]
    [InlineData("Lich", "The Pillars", "Lich, tp Foundation")]
    public void CopiesWhatLifestreamReads(string? world, string location, string expected) =>
        Assert.Equal(expected, places.InLocation(location, world)[0].Copied());

    [Fact]
    public void ShowsTheZoneAloneWithoutASpotOrPoint() =>
        Assert.Equal("Odin · Kugane", new GamePlace(TestGamePlaces.Kugane, "Odin", false, null, null).Display());
}
