using Dalamud.Plugin.Services;
using Jaunt.Core.Text;

namespace Jaunt.Travel;

/// <summary>
/// The worlds the logged-in character can travel to, from its home world, worked out again only when
/// the home world changes, so it is cheap to ask every frame. Use on the game thread.
/// </summary>
internal sealed class CurrentRegion
{
    private readonly IPlayerState player;
    private readonly Gazetteer gazetteer;
    private uint? homeWorld;
    private TravelRegion? region;

    public CurrentRegion(IPlayerState player, Gazetteer gazetteer)
    {
        this.player = player;
        this.gazetteer = gazetteer;
    }

    /// <summary>The region, or null while no character is logged in or its home world is unknown.</summary>
    public TravelRegion? Get()
    {
        if (!player.IsLoaded)
            return null;

        // Compared by row, so the sheet is looked up and the name read, which allocate, only when the world changes
        var world = player.HomeWorld;
        if (world.RowId != homeWorld)
        {
            homeWorld = world.RowId;
            region = world.IsValid ? gazetteer.TravelRegion(world.Value.Name.ExtractText()) : null;
        }

        return region;
    }
}
