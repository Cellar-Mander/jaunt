namespace Jaunt.Core.Text;

/// <summary>
/// The worlds and data centres a character can travel to: every public world in its home world's
/// region, as data-centre travel doesn't cross regions. Names match ignoring case and surrounding spaces.
/// </summary>
public sealed class TravelRegion
{
    private readonly HashSet<string> worlds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> dataCentres = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="worlds">The region's worlds; only those in <paramref name="region"/> are kept.</param>
    public TravelRegion(string region, IEnumerable<GameWorld> worlds)
    {
        Region = region;
        foreach (var world in worlds.Where(w => w.Region == region))
        {
            this.worlds.Add(world.Name);
            dataCentres.Add(world.DataCenter);
        }

        DataCentres = [.. dataCentres.Order(StringComparer.Ordinal)];
    }

    /// <summary>The region, as <see cref="GameWorld.Region"/> gives it.</summary>
    public string Region { get; }

    /// <summary>The region's data centres, in name order.</summary>
    public IReadOnlyList<string> DataCentres { get; }

    /// <summary>Whether the world is in the region; false for null or an unknown world.</summary>
    public bool IncludesWorld(string? world) => world is not null && worlds.Contains(world.Trim());

    /// <summary>Whether the data centre is in the region; false for null or an unknown data centre.</summary>
    public bool IncludesDataCentre(string? dataCentre) => dataCentre is not null && dataCentres.Contains(dataCentre.Trim());
}
