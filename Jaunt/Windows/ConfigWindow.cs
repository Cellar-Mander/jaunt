using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Jaunt.Core;
using Jaunt.Core.Display;
using Jaunt.Core.Listings;

namespace Jaunt.Windows;

/// <summary>
/// Settings, in collapsible groups: Data, Display, then Listings. The Calendar group (between Display and
/// Listings) and Listings' auto-mark visited switch come with the features they configure.
/// </summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly (TimeFormat Format, string Label)[] TimeFormats =
    [
        (TimeFormat.System, "System"),
        (TimeFormat.TwelveHour, "12-hour"),
        (TimeFormat.TwentyFourHour, "24-hour"),
    ];

    private static readonly (DateFormat Format, string Label)[] DateFormats =
    [
        (DateFormat.System, "System"),
        (DateFormat.DayMonth, "Sat 14 Nov"),
        (DateFormat.MonthDay, "Sat Nov 14"),
        (DateFormat.Iso, "2026-11-14"),
    ];

    private static readonly string CooldownHelp = BuildCooldownHelp();

    private const string ClearPopupId = "Clear cached listings?###JauntClearCache";

    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private string? clearResult;

    // Worked out at most once a second, or when a format or the minute changes, rather than every
    // frame, as formatting and listing the hidden venues allocate
    private static readonly TimeSpan HiddenRefreshInterval = TimeSpan.FromSeconds(1);
    private IReadOnlyList<PlaceMark> hiddenPlaces = [];
    private IReadOnlyList<CachedListing> hiddenListings = [];
    private DateTimeOffset nextHiddenRefresh;
    private (long Minute, TimeFormat Time, DateFormat Date) nowShownFor = (-1, default, default);
    private string nowText = "";

    /// <summary>What <see cref="Draw"/> allocates, for the self-test; see "Per-frame code" in CONTRIBUTING.md.</summary>
    internal AllocationWatch DrawAllocations { get; } = new();

    public ConfigWindow(Plugin plugin) : base("Jaunt Settings###JauntConfig")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
        Size = new Vector2(460, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 260), MaximumSize = new Vector2(1200, 1200) };
    }

    public void Dispose() { }

    public override void OnClose() => clearResult = null;

    public override void OnOpen() => nextHiddenRefresh = DateTimeOffset.MinValue;

    public override void Draw()
    {
        DrawAllocations.Start();
        try
        {
            DrawSections();
        }
        finally
        {
            DrawAllocations.Stop();
        }
    }

    private void DrawSections()
    {
        if (DrawSectionHeader(SettingsSection.Data, "Data###Data"))
            DrawData();
        if (DrawSectionHeader(SettingsSection.Display, "Display###Display"))
            DrawDisplay();
        if (DrawSectionHeader(SettingsSection.Listings, "Listings###Listings"))
            DrawListings();
    }

    /// <summary>Draws a section's collapsing header, open or closed as saved, and saves it when toggled.</summary>
    private bool DrawSectionHeader(SettingsSection section, string label)
    {
        var expanded = configuration.ExpandedSections!; // set by Normalize() on load
        var wasOpen = expanded.Contains(section);
        ImGui.SetNextItemOpen(wasOpen, ImGuiCond.Always);
        var open = ImGui.CollapsingHeader(label);
        if (open != wasOpen)
        {
            if (open)
                expanded.Add(section);
            else
                expanded.Remove(section);
            configuration.Save();
        }

        return open;
    }

    private void DrawData()
    {
        ImGui.TextUnformatted("Refresh cooldown (minutes)");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(CooldownHelp);
        DrawCooldownInput();

        ImGui.Spacing();
        if (ImGui.Button("Clear cached listings"))
            ImGui.OpenPopup(ClearPopupId);
        DrawClearConfirmation();

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Removes every cached listing that isn't favourited, visited or hidden.\n" +
            "Other listings are also dropped one month after any source last listed them.");

        if (clearResult is not null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(clearResult);
        }

        ImGui.Spacing();
        DrawSources();
    }

    /// <summary>Where listings come from, each linked to its site, with the credits sources ask for.</summary>
    private static void DrawSources()
    {
        ImGui.TextUnformatted("Listings come from");
        ImGui.BulletText(SourceName(ListingSource.InGamePartyFinder));
        Bulleted(SourceName(ListingSource.Xivpf), Xivpf.SiteUrl);
        Bulleted(SourceName(ListingSource.FfxivVenues), FfxivVenues.SiteUrl);
        Bulleted(SourceName(ListingSource.Partake), Partake.SiteUrl);
        ImGui.SameLine();
        ImGui.TextWrapped($"({Partake.Disclaimer})");
        Bulleted(SourceName(ListingSource.RpEventCalendar), RpEventCalendar.SiteUrl);
        ImGui.SameLine();
        ImGui.TextUnformatted("·");
        ImGui.SameLine();
        ListingsWindow.DrawLinkText(RpEventCalendar.LicenceName, RpEventCalendar.LicenceUrl);
    }

    private static void Bulleted(string text, Uri uri)
    {
        ImGui.Bullet();
        ListingsWindow.DrawLinkText(text, uri);
    }

    /// <summary>
    /// The cooldown as − [number] +, so minus sits on the left. Each button steps 5 minutes, or an
    /// hour with Ctrl held, like ImGui's own step buttons.
    /// </summary>
    private void DrawCooldownInput()
    {
        var minutes = configuration.RefreshCooldownMinutes;
        var step = ImGui.GetIO().KeyCtrl ? 60 : 5;
        var buttonSize = new Vector2(ImGui.GetFrameHeight());
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var changed = false;

        if (ImGui.Button("-##cooldownDown", buttonSize))
        {
            minutes -= step;
            changed = true;
        }

        ImGui.SameLine(0, spacing);
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 60f);
        changed |= ImGui.InputInt("##cooldown", ref minutes, 0, 0);

        ImGui.SameLine(0, spacing);
        if (ImGui.Button("+##cooldownUp", buttonSize))
        {
            minutes += step;
            changed = true;
        }

        if (changed)
        {
            configuration.RefreshCooldownMinutes = Math.Clamp(minutes, RefreshPolicy.MinCooldownMinutes, RefreshPolicy.MaxCooldownMinutes);
            configuration.Save();
        }
    }

    private void DrawClearConfirmation()
    {
        // Centred on the Settings window (the current window here), not the game window, so it opens
        // by the button that asked for it
        var settingsCentre = ImGui.GetWindowPos() + (ImGui.GetWindowSize() * 0.5f);
        ImGui.SetNextWindowPos(settingsCentre, ImGuiCond.Appearing, new Vector2(0.5f));
        // Passing open gives the title bar a close button; ImGui closes the popup when it's clicked
        var open = true;
        using var popup = ImRaii.PopupModal(ClearPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        if (!popup)
            return;

        ImGui.TextUnformatted("Remove every cached listing that isn't favourited, visited or hidden?");
        ImGui.Spacing();
        ImGui.TextUnformatted(
            "Some sources limit how often Jaunt may fetch them, so their listings may not\n" +
            "show in the listing browser again for several hours.");
        ImGui.Spacing();

        if (ImGui.Button("Clear"))
        {
            var removed = plugin.ListingCache.ClearUnmarked();
            plugin.SaveListingCache();
            clearResult = removed == 1 ? "Removed 1 listing." : $"Removed {removed} listings.";
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawDisplay()
    {
        var timeFormat = configuration.TimeFormat;
        if (Combo("Time format", TimeFormats, ref timeFormat))
        {
            configuration.TimeFormat = timeFormat;
            configuration.Save();
        }

        var dateFormat = configuration.DateFormat;
        if (Combo("Date format", DateFormats, ref dateFormat))
        {
            configuration.DateFormat = dateFormat;
            configuration.Save();
        }

        // The time shows minutes, so the line changes only with the minute or a format
        var now = DateTime.Now;
        var shownFor = (now.Ticks / TimeSpan.TicksPerMinute, configuration.TimeFormat, configuration.DateFormat);
        if (shownFor != nowShownFor)
        {
            AllocationWatch.Filled();
            var locale = CultureInfo.CurrentCulture;
            nowText = $"Now: {DisplayFormats.FormatDate(now, configuration.DateFormat, locale)} {DisplayFormats.FormatTime(now, configuration.TimeFormat, locale)}";
            nowShownFor = shownFor;
        }

        ImGui.TextDisabled(nowText);
    }

    private void DrawListings()
    {
        ImGui.TextUnformatted("Hidden venues");

        // The browser's Hide button and fetches change these, so they are listed again each second
        var now = DateTimeOffset.UtcNow;
        if (now >= nextHiddenRefresh)
        {
            AllocationWatch.Filled();
            hiddenPlaces = plugin.ListingCache.HiddenPlaces();
            hiddenListings = plugin.ListingCache.HiddenListings();
            nextHiddenRefresh = now + HiddenRefreshInterval;
        }

        var places = hiddenPlaces;
        var hidden = hiddenListings;
        if (places.Count == 0 && hidden.Count == 0)
        {
            ImGui.TextDisabled("None. Hide a venue from the listing browser to keep it out of the list.");
            return;
        }

        var unhid = false;
        using var indent = Scoped.Indent();
        // A hidden address hides every listing there, so it shows once, with where it is
        for (var i = 0; i < places.Count; i++)
        {
            using var id = Scoped.Id($"place{i}");
            if (ImGui.SmallButton("Un-hide"))
            {
                places[i].Hidden = false;
                unhid = true;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(ShownText.Title(places[i].Title));
            ImGui.SameLine();
            ImGui.TextDisabled(places[i].ShownAddress);
        }

        // Indexed, as enumerating through the list's interface allocates
        for (var i = 0; i < hidden.Count; i++)
        {
            var listing = hidden[i];
            using var id = Scoped.Id($"{(int)listing.Source}:{listing.Id}");
            if (ImGui.SmallButton("Un-hide"))
            {
                listing.Hidden = false;
                unhid = true;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(ShownText.Title(listing.ShownTitle));
            ImGui.SameLine();
            ImGui.TextDisabled(SourceName(listing.Source));
        }

        if (unhid)
        {
            plugin.SaveListingCache();
            nextHiddenRefresh = DateTimeOffset.MinValue;
        }
    }

    private static bool Combo<T>(string label, (T Value, string Label)[] options, ref T value) where T : struct, Enum
    {
        // Drawn every frame, so a loop rather than LINQ, and EqualityComparer, which doesn't box an enum as Equals(object) does
        var current = value;
        var comparer = EqualityComparer<T>.Default;
        string? preview = null;
        foreach (var (option, optionLabel) in options)
        {
            if (comparer.Equals(option, current))
                preview = optionLabel;
        }

        var changed = false;

        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 160f);
        if (ImGui.BeginCombo(label, preview ?? current.ToString()))
        {
            foreach (var (option, optionLabel) in options)
            {
                if (ImGui.Selectable(optionLabel, comparer.Equals(option, current)))
                {
                    value = option;
                    changed = !comparer.Equals(option, current);
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static string SourceName(ListingSource source) => source switch
    {
        ListingSource.InGamePartyFinder => "In-game Party Finder",
        ListingSource.Xivpf => "xivpf",
        ListingSource.FfxivVenues => "FFXIV Venues",
        ListingSource.Partake => "Partake",
        ListingSource.RpEventCalendar => "FFXIV RP Event Calendar",
        _ => source.ToString(),
    };

    private static string BuildCooldownHelp()
    {
        var lines = new StringBuilder()
            .AppendLine("The listing browser fetches each source when you open it, unless that source")
            .AppendLine("was fetched within its cooldown. Refresh obeys the cooldown too, and inside it")
            .AppendLine("the browser shows cached listings.")
            .AppendLine()
            .AppendLine($"Each source waits this long ({RefreshPolicy.MinCooldownMinutes} min to {RefreshPolicy.MaxCooldownMinutes / 60} h), or its own minimum if longer:");
        foreach (var source in RefreshPolicy.FetchedSources)
            lines.AppendLine($"  - {SourceName(source)}: {MinimumText(source)}");
        lines.Append($"  - {SourceName(ListingSource.InGamePartyFinder)}: no cooldown; recorded while you have it open");
        return lines.ToString();
    }

    private static string MinimumText(ListingSource source)
    {
        var minimum = RefreshPolicy.Minimum(source)!.Value;
        return minimum.TotalMinutes <= RefreshPolicy.MinCooldownMinutes
            ? "this setting"
            : $"this setting, but at least {minimum.TotalHours:0} h";
    }
}
