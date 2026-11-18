using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Jaunt.Core.Text;
using Lumina.Excel.Sheets;

namespace Jaunt.Text;

/// <summary>Builds <see cref="GamePlaces"/> from the game's data: each town and field zone, its aetheryte and its map's labels.</summary>
internal static class GamePlacesLoader
{
    // TerritoryIntendedUse rows
    private const uint Town = 0;
    private const uint Field = 1;

    // MapMarker data types: a plain label, and an aetheryte (its DataKey the Aetheryte row)
    private const byte Label = 0;
    private const byte AetheryteMarker = 3;

    public static GamePlaces Load(IDataManager data)
    {
        var territories = data.GetExcelSheet<TerritoryType>();
        var names = data.GetExcelSheet<PlaceName>(ClientLanguage.English);
        var markers = data.GetSubrowExcelSheet<MapMarker>();
        string NameOf(uint placeName) => names.GetRowOrDefault(placeName)?.Name.ExtractText() ?? "";

        // A housing district's zone has a town twin by the same name ("Mist"), which addresses cover
        var taken = new HashSet<string>(
            GazetteerLoader.Districts.Select(d => territories.GetRowOrDefault(d.Territory) is { } t ? NameOf(t.PlaceName.RowId) : "").Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var zones = new List<GameZone>();
        foreach (var territory in territories)
        {
            if (territory.TerritoryIntendedUse.RowId is not (Town or Field) || !territory.Map.IsValid)
                continue;
            var name = NameOf(territory.PlaceName.RowId);
            // Rows after the first by a name are copies of the zone for quests
            if (name.Length == 0 || !taken.Add(name))
                continue;

            var map = territory.Map.Value;
            var aetheryte = territory.Aetheryte.IsValid && territory.Aetheryte.RowId != 0 ? territory.Aetheryte.Value : (Aetheryte?)null;
            var teleport = aetheryte is { Territory.IsValid: true } a ? NameOf(a.Territory.Value.PlaceName.RowId) : "";
            MapPoint? aetheryteAt = null;
            var spots = new List<ZoneSpot>();
            if (map.SizeFactor > 0 && markers.GetRowOrDefault(map.MapMarkerRange) is { } row)
            {
                var spotNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var marker in row)
                {
                    if (marker.X == 0 && marker.Y == 0)
                        continue;
                    // The marker's position is in the map texture's 2048 pixels; the game shows 2·pixel/scale + 1
                    var point = new MapPoint(2f * marker.X / map.SizeFactor + 1, 2f * marker.Y / map.SizeFactor + 1);
                    if (marker.DataType == AetheryteMarker && aetheryte is not null && marker.DataKey.RowId == territory.Aetheryte.RowId
                        && aetheryte.Value.Territory.RowId == territory.RowId)
                    {
                        aetheryteAt ??= point;
                    }
                    else if (marker.DataType == Label && NameOf(marker.PlaceNameSubtext.RowId) is { Length: > 0 } spot && spotNames.Add(spot))
                    {
                        spots.Add(new ZoneSpot(spot, point));
                    }
                }
            }

            zones.Add(new GameZone(territory.RowId, territory.Map.RowId, name, teleport.Length > 0 ? teleport : name, aetheryteAt, spots));
        }

        return new GamePlaces(zones);
    }
}
