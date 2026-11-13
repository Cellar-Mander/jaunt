using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Jaunt.Core.Text;
using Lumina.Excel.Sheets;

namespace Jaunt.Text;

/// <summary>Builds the address parser's <see cref="Gazetteer"/> from the game's data.</summary>
internal static class GazetteerLoader
{
    // The five housing territories
    private static readonly (uint Territory, District District)[] Districts =
    [
        (339, District.Mist), (340, District.LavenderBeds), (341, District.Goblet), (641, District.Shirogane), (979, District.Empyreum),
    ];

    // Ads use the district names of every client language, not just the player's.
    private static readonly ClientLanguage[] Languages = [ClientLanguage.English, ClientLanguage.German, ClientLanguage.French, ClientLanguage.Japanese];

    public static Gazetteer Load(IDataManager data)
    {
        var worlds = data.GetExcelSheet<World>()
            .Where(w => w.IsPublic && w.DataCenter.IsValid)
            .Select(w => new GameWorld(
                w.Name.ExtractText(),
                w.DataCenter.Value.Name.ExtractText(),
                w.DataCenter.Value.Region.RowId.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        var territories = data.GetExcelSheet<TerritoryType>();
        var names = new List<KeyValuePair<District, string>>();
        foreach (var language in Languages)
        {
            var places = data.GetExcelSheet<PlaceName>(language);
            foreach (var (territory, district) in Districts)
            {
                var placeId = territories.GetRowOrDefault(territory)?.PlaceName.RowId;
                var name = placeId is { } id ? places.GetRowOrDefault(id)?.Name.ExtractText() : null;
                if (!string.IsNullOrEmpty(name))
                    names.Add(new(district, name));
            }
        }

        return new Gazetteer(worlds, names);
    }
}
