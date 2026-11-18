using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>Some of the zones the plugin reads from the game's data, with a few of their map labels, as of patch 7.3.</summary>
internal static class TestGamePlaces
{
    public static readonly GameZone CentralShroud = new(148, 4, "Central Shroud", "Central Shroud", new(21.7f, 22.2f),
        [new("The Bannock", new(23.2f, 19.8f)), new("Bentbranch Meadows", new(21.1f, 22.0f)), new("Everschade\n(The Guardian Tree)", new(15.0f, 25.2f))]);

    public static readonly GameZone LimsaLowerDecks = new(129, 12, "Limsa Lominsa Lower Decks", "Limsa Lominsa Lower Decks", new(9.6f, 11.2f),
        [new("The Astalicia", new(5.9f, 15.1f))]);

    public static readonly GameZone NewGridania = new(132, 2, "New Gridania", "New Gridania", new(11.9f, 11.8f), []);

    public static readonly GameZone OldGridania = new(133, 3, "Old Gridania", "New Gridania", null, [new("Apkallu Falls", new(11.1f, 6.4f))]);

    public static readonly GameZone Foundation = new(418, 218, "Foundation", "Foundation", new(10.0f, 12.1f), []);

    public static readonly GameZone Pillars = new(419, 219, "The Pillars", "Foundation", null, [new("Saint Reymanaud's Cathedral", new(8.3f, 7.8f))]);

    public static readonly GameZone WesternThanalan = new(140, 20, "Western Thanalan", "Western Thanalan", new(22.8f, 16.9f),
        [new("The Silver Bazaar", new(16.1f, 29.9f)), new("Silver Bazaar Docks", new(15.4f, 27.6f))]);

    public static readonly GameZone CentralThanalan = new(141, 21, "Central Thanalan", "Central Thanalan", new(21.1f, 18.1f), []);

    public static readonly GameZone UldahNald = new(130, 13, "Ul'dah - Steps of Nald", "Ul'dah - Steps of Nald", new(8.4f, 7.9f), []);

    public static readonly GameZone NorthShroud = new(154, 7, "North Shroud", "North Shroud", new(20.6f, 26.0f),
        [new("Hyrstmill", new(28.5f, 19.7f)), new("Fallgourd Float", new(21.0f, 26.8f))]);

    public static readonly GameZone SouthShroud = new(153, 6, "South Shroud", "South Shroud", new(25.1f, 20.1f), []);

    public static readonly GameZone Kugane = new(628, 370, "Kugane", "Kugane", new(12.2f, 10.5f), []);

    public static readonly GameZone MorDhona = new(156, 25, "Mor Dhona", "Mor Dhona", new(22.3f, 8.1f), []);

    public static readonly GameZone Shaaloani = new(1190, 860, "Shaaloani", "Shaaloani", new(29.1f, 30.8f), []);

    // A new finder each time, so a mutant in its constructor is seen by every test rather than the first
    public static GamePlaces Create() => new(
    [
        CentralShroud, LimsaLowerDecks, NewGridania, OldGridania, Foundation, Pillars, WesternThanalan, CentralThanalan, UldahNald,
        NorthShroud, SouthShroud, Kugane, MorDhona, Shaaloani,
    ]);
}
