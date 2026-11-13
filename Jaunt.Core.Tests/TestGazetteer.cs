using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>The worlds and district names the plugin reads from the game's data, as of patch 7.3.</summary>
internal static class TestGazetteer
{
    private static readonly (string Region, string DataCenter, string[] Worlds)[] DataCenters =
    [
        ("NA", "Aether", ["Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren"]),
        ("NA", "Crystal", ["Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera"]),
        ("NA", "Dynamis", ["Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph"]),
        ("NA", "Primal", ["Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros"]),
        ("EU", "Chaos", ["Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan"]),
        ("EU", "Light", ["Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark"]),
        ("OCE", "Materia", ["Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan"]),
        ("JP", "Elemental", ["Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon"]),
        ("JP", "Gaia", ["Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima"]),
        ("JP", "Mana", ["Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan"]),
        ("JP", "Meteor", ["Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus"]),
    ];

    // PlaceName for the five housing territories, in English, German, French and Japanese.
    private static readonly KeyValuePair<District, string>[] Districts =
    [
        new(District.Mist, "Mist"), new(District.Mist, "Dorf des Nebels"), new(District.Mist, "Brumée"), new(District.Mist, "ミスト・ヴィレッジ"),
        new(District.LavenderBeds, "The Lavender Beds"), new(District.LavenderBeds, "Lavendelbeete"),
        new(District.LavenderBeds, "Lavandière"), new(District.LavenderBeds, "ラベンダーベッド"),
        new(District.Goblet, "The Goblet"), new(District.Goblet, "Kelchkuppe"), new(District.Goblet, "La Coupe"),
        new(District.Goblet, "ゴブレットビュート"),
        new(District.Shirogane, "Shirogane"), new(District.Shirogane, "シロガネ"),
        new(District.Empyreum, "Empyreum"), new(District.Empyreum, "Empyrée"), new(District.Empyreum, "エンピレアム"),
    ];

    public static readonly Gazetteer Instance = new(
        DataCenters.SelectMany(dc => dc.Worlds.Select(w => new GameWorld(w, dc.DataCenter, dc.Region))),
        Districts);

    public static readonly AddressParser Parser = new(Instance);
}
