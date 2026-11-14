using Jaunt.Core.Listings;

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

    /// <summary>
    /// Whether a cached listing is in the region, by the same fields its source is filtered on when
    /// recorded: the world it is listed on, or for Partake that world or its data centre. An FFXIV
    /// Venues venue is matched by its shown address instead; see <see cref="Includes(FfxivVenues.Shown)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The listing is an FFXIV Venues venue.</exception>
    public bool Includes(CachedListing listing) => listing.Source switch
    {
        // Stryker disable once String : the message is for a developer reading the exception; no behaviour depends on it
        ListingSource.FfxivVenues => throw new ArgumentException("Match a venue by its shown address", nameof(listing)),
        ListingSource.Partake => IncludesWorld(listing.ListedOn) || IncludesDataCentre(listing.DataCentre),
        _ => IncludesWorld(listing.ListedOn),
    };

    /// <summary>
    /// Whether an FFXIV Venues venue is in the region, by its address's world with misspellings
    /// corrected as shown; false for a venue without an address.
    /// </summary>
    public bool Includes(FfxivVenues.Shown venue) => IncludesWorld(venue.Address?.World);
}
