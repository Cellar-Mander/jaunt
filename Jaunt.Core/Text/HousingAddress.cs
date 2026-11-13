namespace Jaunt.Core.Text;

/// <summary>A residential district.</summary>
public enum District
{
    Mist,
    LavenderBeds,
    Goblet,
    Shirogane,
    Empyreum,
}

/// <summary>Where an address's world came from.</summary>
public enum WorldSource
{
    /// <summary>The ad's text names it.</summary>
    Text,

    /// <summary>
    /// The text names none, so it is the world the ad is listed on. Lower confidence: ads are often
    /// listed on a different world from the venue's.
    /// </summary>
    Listing,
}

/// <summary>A housing address read from an ad.</summary>
/// <param name="World">The world's name; null only when the text names none and the listing world is unknown.</param>
/// <param name="WorldFrom">Whether the text named the world or it is the listing's.</param>
/// <param name="District">Null when the text names no district.</param>
/// <param name="Ward">1 to 30.</param>
/// <param name="Plot">1 to 60; null for an apartment or a ward-only address.</param>
/// <param name="Subdivision">True or false only when the text says so.</param>
/// <param name="Apartment">A room number in an apartment building.</param>
/// <param name="Room">A private chamber or room inside a house, as written ("3", "001", "B1").</param>
public sealed record HousingAddress(
    string? World,
    WorldSource WorldFrom,
    District? District,
    int Ward,
    int? Plot,
    bool? Subdivision = null,
    int? Apartment = null,
    string? Room = null);

/// <summary>A public game world.</summary>
/// <param name="Name">The world's name, as the game writes it.</param>
/// <param name="DataCenter">Its data centre's name.</param>
/// <param name="Region">Its region; only compared for equality, to resolve abbreviations.</param>
public sealed record GameWorld(string Name, string DataCenter, string Region);
