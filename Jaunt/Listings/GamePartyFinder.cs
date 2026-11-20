using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Jaunt.Listings;

/// <summary>Opens an ad in the game's Party Finder, which shows only the data centre the character is on. Use on the game thread.</summary>
internal sealed class GamePartyFinder(IPlayerState player)
{
    /// <summary>The data centre the character is on, or null while none is logged in.</summary>
    public string? CurrentDataCentre =>
        player.IsLoaded && player.CurrentWorld.IsValid && player.CurrentWorld.Value.DataCenter.IsValid
            ? player.CurrentWorld.Value.DataCenter.Value.Name.ExtractText()
            : null;

    /// <summary>
    /// Asks the game to open the listing's details in its Party Finder, from which the player can join
    /// or message the host; false if the game wouldn't. The game itself says if the listing is gone.
    /// </summary>
    public unsafe bool Open(ulong listingId)
    {
        var agent = AgentLookingForGroup.Instance();
        return agent != null && agent->OpenListing(listingId);
    }
}
