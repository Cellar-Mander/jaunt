using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Jaunt.Core.Browser;
using Jaunt.Core.Text;
using Jaunt.Text;

namespace Jaunt.Travel;

/// <summary>
/// The plot or apartment the player is at, read from the game's housing manager: the plot they stand
/// on outdoors, or the house or apartment they are inside. Use on the game thread.
/// </summary>
internal sealed class PlayerAddress(IPlayerState player, IClientState clientState, IPluginLog log)
{
    private (bool Inside, ulong House, uint Territory, int Ward, int Plot) lastRaw;

    /// <summary>Where the player is, or null outside housing, off a plot, in a lobby or a workshop, or while logged out.</summary>
    public unsafe AddressKey? Get()
    {
        if (!player.IsLoaded || !player.CurrentWorld.IsValid)
            return null;
        var housing = HousingManager.Instance();
        if (housing == null)
            return null;

        var world = player.CurrentWorld.Value.Name.ExtractText();
        if (housing->IsInside())
        {
            var house = housing->GetCurrentIndoorHouseId();
            var inside = AutoVisits.Inside(
                world, DistrictOf(house.TerritoryTypeId), house.WardIndex, house.IsApartment, house.ApartmentDivision, house.PlotIndex,
                house.RoomNumber, house.IsWorkshop);
            Log((true, house.Id, house.TerritoryTypeId, house.WardIndex, house.PlotIndex), inside);
            return inside;
        }

        if (!housing->IsOutside())
            return null;
        int ward = housing->GetCurrentWard();
        int plot = housing->GetCurrentPlot();
        var outside = AutoVisits.OnPlot(world, DistrictOf(clientState.TerritoryType), ward, plot);
        Log((false, 0, clientState.TerritoryType, ward, plot), outside);
        return outside;
    }

    private static District? DistrictOf(uint territory)
    {
        foreach (var (id, district) in GazetteerLoader.Districts)
        {
            if (id == territory)
                return district;
        }

        return null;
    }

    // The game's housing indexes aren't documented, so what it reports is logged when it changes, to check in-game
    private void Log((bool Inside, ulong House, uint Territory, int Ward, int Plot) raw, AddressKey? address)
    {
        if (raw == lastRaw)
            return;
        lastRaw = raw;
        log.Debug($"Housing location: inside={raw.Inside} house={raw.House:X16} territory={raw.Territory} ward={raw.Ward} plot={raw.Plot} → {address?.ToString() ?? "none"}");
    }
}
