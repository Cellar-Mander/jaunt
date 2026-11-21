using Jaunt.Core.Browser;
using Jaunt.Core.Display;
using Jaunt.Core.Listings;

namespace Jaunt.Core;

/// <summary>A collapsible group in the Settings window. Saved by number, so only ever append.</summary>
public enum SettingsSection
{
    Data,
    Display,
    Listings,
    Calendar,
}

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
    /// <summary>Settings → Data: the shortest time between fetches of any source (<see cref="RefreshPolicy"/>).</summary>
    public int RefreshCooldownMinutes { get; set; } = RefreshPolicy.DefaultCooldownMinutes;

    /// <summary>Settings → Display.</summary>
    public TimeFormat TimeFormat { get; set; } = TimeFormat.System;

    /// <summary>Settings → Display.</summary>
    public DateFormat DateFormat { get; set; } = DateFormat.System;

    /// <summary>Settings → Calendar: how Add to calendar adds an opening when clicked (<see cref="CalendarExport"/>).</summary>
    public CalendarFormat CalendarFormat { get; set; } = CalendarFormat.Ics;

    /// <summary>Settings → Listings: mark an entry visited when the player is at its plot or apartment while it is open (<see cref="AutoVisits"/>).</summary>
    public bool AutoMarkVisited { get; set; } = true;

    /// <summary>The source of the listing last selected in the browser, or null when none is.</summary>
    public ListingSource? SelectedSource { get; set; }

    /// <summary>The ID within <see cref="SelectedSource"/> of the listing last selected in the browser.</summary>
    public string? SelectedId { get; set; }

    /// <summary>The listing browser's search and filter chips.</summary>
    public BrowserFilters Filters { get; set; } = new();

    /// <summary>Which Settings groups are expanded; null until <see cref="Normalize"/>, which opens them all.</summary>
    public HashSet<SettingsSection>? ExpandedSections { get; set; }

    /// <summary>Fills in defaults and pulls hand-edited values back into range; call after loading.</summary>
    public void Normalize()
    {
        RefreshCooldownMinutes = Math.Clamp(RefreshCooldownMinutes, RefreshPolicy.MinCooldownMinutes, RefreshPolicy.MaxCooldownMinutes);
        if (!Enum.IsDefined(TimeFormat))
            TimeFormat = TimeFormat.System;
        if (!Enum.IsDefined(DateFormat))
            DateFormat = DateFormat.System;
        if (!Enum.IsDefined(CalendarFormat))
            CalendarFormat = CalendarFormat.Ics;
        Filters ??= new BrowserFilters();
        Filters.Search ??= "";
        Filters.Tags &= ListingFilters.AllInterests;
        ExpandedSections ??= [.. Enum.GetValues<SettingsSection>()];
        ExpandedSections.RemoveWhere(section => !Enum.IsDefined(section));
        if (SelectedSource is { } source && !Enum.IsDefined(source) || SelectedSource is null != SelectedId is null)
        {
            SelectedSource = null;
            SelectedId = null;
        }
    }
}
