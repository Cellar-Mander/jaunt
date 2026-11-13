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

/// <summary>A public game world.</summary>
/// <param name="Name">The world's name, as the game writes it.</param>
/// <param name="DataCenter">Its data centre's name.</param>
/// <param name="Region">Its region; only compared for equality, to resolve abbreviations.</param>
public sealed record GameWorld(string Name, string DataCenter, string Region);
