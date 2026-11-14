using Dalamud.Plugin.Services;
using Jaunt.Core.Text;

namespace Jaunt.Travel;

/// <summary>
/// The worlds the logged-in character can travel to, from its home world, worked out again only when
/// the home world changes. Use on the game thread.
/// </summary>
internal sealed class CurrentRegion
{
    private readonly IPlayerState player;
    private readonly Gazetteer gazetteer;
    private string? homeWorld;
    private TravelRegion? region;

    public CurrentRegion(IPlayerState player, Gazetteer gazetteer)
    {
        this.player = player;
        this.gazetteer = gazetteer;
    }

    /// <summary>The region, or null while no character is logged in or its home world is unknown.</summary>
    public TravelRegion? Get()
    {
        if (!player.IsLoaded || !player.HomeWorld.IsValid)
            return null;

        var name = player.HomeWorld.Value.Name.ExtractText();
        if (name != homeWorld)
        {
            homeWorld = name;
            region = gazetteer.TravelRegion(name);
        }

        return region;
    }
}
