namespace Jaunt.Core;

/// <summary>
/// The plugin's settings, kept free of Dalamud so their save and load can be tested here. The plugin's
/// Configuration adds only Dalamud's interface.
/// </summary>
/// <remarks>
/// Json.NET adds loaded items to a collection a property already holds, so start collections empty
/// and fill in defaults in <see cref="Normalize"/>. Save enums by number and only ever append to
/// them, or save them by name when the list can change.
/// </remarks>
public abstract class ConfigurationBase
{
    public const int MinExampleValue = 0;
    public const int MaxExampleValue = 100;

    /// <summary>Replace with the plugin's first real setting.</summary>
    public int ExampleValue { get; set; } = 50;

    /// <summary>Fills in defaults and pulls hand-edited values back into range; call after loading.</summary>
    public void Normalize()
    {
        ExampleValue = Math.Clamp(ExampleValue, MinExampleValue, MaxExampleValue);
    }
}
