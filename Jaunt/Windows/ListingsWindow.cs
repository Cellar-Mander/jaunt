using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Jaunt.Core.Display;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;
using Jaunt.Travel;

namespace Jaunt.Windows;

/// <summary>
/// The MVP listing window: Party Finder venue ads (in-game and from xivpf) with a parsed address,
/// FFXIV Venues venues with an address and Partake events that haven't ended, newest first, each
/// address with Travel and Copy address and each link with Open. The two-pane browser (cdd9e95f) replaces it.
/// </summary>
internal sealed class ListingsWindow : Window
{
    private static readonly TimeSpan RebuildInterval = TimeSpan.FromSeconds(1);

    private readonly ListingCache cache;
    private readonly AddressParser parser;
    private readonly Lifestream lifestream;
    private readonly INotificationManager notifications;
    private readonly Configuration configuration;
    private readonly Action fetchSources;

    // Parsed once per ad text or event location, so drawing doesn't re-parse every frame
    private readonly Dictionary<(ListingSource, string), (string Text, ListingDetails Details)> parsed = [];
    private List<(CachedListing Listing, ListingDetails Details)> rows = [];
    private readonly Dictionary<(ListingSource, string), (int Lines, float Height)> rowHeights = [];
    private DateTimeOffset nextRebuild;
    private bool showsPartake;

    /// <param name="fetchSources">Fetches each fetched source whose cooldown has passed; called on opening.</param>
    public ListingsWindow(ListingCache cache, AddressParser parser, Lifestream lifestream, INotificationManager notifications, Configuration configuration, Action fetchSources)
        : base("Jaunt###JauntListings")
    {
        this.fetchSources = fetchSources;
        this.cache = cache;
        this.parser = parser;
        this.lifestream = lifestream;
        this.notifications = notifications;
        this.configuration = configuration;
        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 240), MaximumSize = new Vector2(1600, 1600) };
    }

    public override void OnOpen()
    {
        fetchSources();
        nextRebuild = DateTimeOffset.MinValue;
    }

    public override void Draw()
    {
        var now = DateTimeOffset.UtcNow;
        if (now >= nextRebuild)
        {
            Rebuild(now);
            nextRebuild = now + RebuildInterval;
        }

        if (rows.Count == 0)
        {
            ImGui.TextWrapped("No venues with an address yet. Wait for xivpf, FFXIV Venues and Partake to load, or open the Party Finder and browse the None category.");
            return;
        }

        var travelAvailable = lifestream.Available;
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        foreach (var (listing, details) in rows)
        {
            // FFXIV Venues alone lists over a thousand venues, so rows out of view are only spaced, not drawn
            var key = (listing.Source, listing.Id);
            var top = ImGui.GetCursorPosY();
            var lines = 1 + EventLines(listing, details) + details.Addresses.Count + details.Links.Count;
            var height = rowHeights.TryGetValue(key, out var measured) && measured.Lines == lines
                ? measured.Height
                : lines * lineHeight;
            if (!ImGui.IsRectVisible(new Vector2(1, height)))
            {
                ImGui.SetCursorPosY(top + height);
                continue;
            }

            DrawRow(listing, details, now, travelAvailable);
            rowHeights[key] = (lines, ImGui.GetCursorPosY() - top);
        }

        // Partake's terms ask apps not to imply endorsement
        if (showsPartake)
            ImGui.TextDisabled("Events from Partake (partake.gg). Jaunt is not affiliated with or endorsed by Partake.");

        // Keeps the content height right when the last row was only spaced
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>The lines an event adds under its title: its times and world, and its location when no address was read from it.</summary>
    private static int EventLines(CachedListing listing, ListingDetails details) =>
        listing.StartsAt is null ? 0 : 1 + (details.Addresses.Count == 0 && !string.IsNullOrEmpty(listing.Location) ? 1 : 0);

    private void DrawRow(CachedListing listing, ListingDetails details, DateTimeOffset now, bool travelAvailable)
    {
        using var id = ImRaii.PushId($"{(int)listing.Source}:{listing.Id}");
        ImGui.TextUnformatted(ShownText.Title(listing.Title));
        ImGui.SameLine();
        ImGui.TextDisabled(SourceName(listing.Source));
        if (listing.AlwaysOpen)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("· open 24/7");
        }

        ImGui.SameLine();
        ImGui.TextDisabled("· " + SeenAgo(now - listing.LastSeen));

        using (ImRaii.PushIndent())
        {
            if (listing.StartsAt is { } start && listing.EndsAt is { } end)
                DrawEventLines(listing, details, start, end, now);

            for (var i = 0; i < details.Addresses.Count; i++)
            {
                using var addressId = ImRaii.PushId($"a{i}");
                DrawAddress(details.Addresses[i], travelAvailable);
            }

            for (var i = 0; i < details.Links.Count; i++)
            {
                using var linkId = ImRaii.PushId($"l{i}");
                DrawLink(details.Links[i]);
            }
        }

        ImGui.Separator();
    }

    private void DrawEventLines(CachedListing listing, ListingDetails details, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var times = DisplayFormats.FormatSpan(
            start.ToLocalTime().DateTime, end.ToLocalTime().DateTime, configuration.TimeFormat, configuration.DateFormat, CultureInfo.CurrentCulture);
        var world = listing.DataCentre is { } dataCentre ? $"{listing.ListedOn} ({dataCentre})" : listing.ListedOn;
        ImGui.TextUnformatted(start <= now ? $"{times} · on now · {world}" : $"{times} · {world}");
        if (details.Addresses.Count == 0 && !string.IsNullOrEmpty(listing.Location))
            ImGui.TextWrapped(ShownText.Title(listing.Location));
    }

    private void DrawAddress(HousingAddress address, bool travelAvailable)
    {
        var text = AddressText.Format(address);
        var request = TravelRequest.From(address);
        var whyNot = !travelAvailable ? "Install and enable Lifestream to travel from Jaunt."
            : request is null ? "This address is missing a world, district, plot or apartment, so Lifestream can't travel there."
            : null;

        using (ImRaii.Disabled(whyNot is not null))
        {
            if (ImGui.SmallButton("Travel") && request is not null)
            {
                if (lifestream.Travel(request) is { } error)
                    Notify(error, NotificationType.Warning);
            }
        }

        if (whyNot is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(whyNot);

        ImGui.SameLine();
        if (ImGui.SmallButton("Copy address"))
        {
            ImGui.SetClipboardText(text);
            Notify($"Copied {text}", NotificationType.Success);
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(text);
        if (address.WorldFrom == WorldSource.Listing)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(world the ad is listed on)");
        }
    }

    private static void DrawLink(WebLink link)
    {
        if (ImGui.SmallButton("Open"))
            Util.OpenLink(link.Uri.AbsoluteUri);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(link.Uri.AbsoluteUri);

        ImGui.SameLine();
        ImGui.TextUnformatted(link.Text);
    }

    /// <summary>
    /// Lists the visible listings that have an address, and Partake events that haven't ended, newest
    /// first and then by title, parsing only ad text and locations that changed. An xivpf ad the game
    /// has also sent shows once, as the in-game copy.
    /// </summary>
    private void Rebuild(DateTimeOffset now)
    {
        var next = new List<(CachedListing, ListingDetails)>();
        showsPartake = false;
        foreach (var listing in cache.Listings)
        {
            if (listing.Hidden)
                continue;

            if (listing.Source == ListingSource.FfxivVenues)
            {
                if (listing.Address is { } address)
                    next.Add((listing, new ListingDetails([address], [.. listing.Links.Select(uri => new WebLink(uri.OriginalString, uri))])));
                continue;
            }

            if (listing.Source == ListingSource.Partake)
            {
                // Shown without an address too: the location is free text the parser may not read
                if (!Partake.NotEnded(listing, now))
                    continue;
                var location = Parsed(listing, listing.Location ?? "");
                next.Add((listing, location with { Links = [.. listing.Links.Select(uri => new WebLink(uri.OriginalString, uri))] }));
                showsPartake = true;
                continue;
            }

            if (listing.Source is not (ListingSource.InGamePartyFinder or ListingSource.Xivpf) || Xivpf.ShownInGame(cache, listing))
                continue;

            var details = Parsed(listing, listing.Description);
            if (details.Addresses.Count > 0)
                next.Add((listing, details));
        }

        rows = [.. next.OrderByDescending(r => r.Item1.LastSeen).ThenBy(r => r.Item1.Title, StringComparer.CurrentCultureIgnoreCase)];

        // Forget ads the cache no longer holds (cleared or expired)
        if (parsed.Count > cache.Count)
        {
            foreach (var key in parsed.Keys.Where(k => cache.Find(k.Item1, k.Item2) is null).ToList())
                parsed.Remove(key);
        }

        if (rowHeights.Count > cache.Count)
        {
            foreach (var key in rowHeights.Keys.Where(k => cache.Find(k.Item1, k.Item2) is null).ToList())
                rowHeights.Remove(key);
        }
    }

    private ListingDetails Parsed(CachedListing listing, string text)
    {
        var key = (listing.Source, listing.Id);
        if (!parsed.TryGetValue(key, out var entry) || !ReferenceEquals(entry.Text, text))
        {
            entry = (text, ListingDetails.Parse(parser, text, listing.ListedOn));
            parsed[key] = entry;
        }

        return entry.Details;
    }

    private void Notify(string text, NotificationType type) =>
        notifications.AddNotification(new Notification { Title = "Jaunt", Content = text, Type = type });

    private static string SourceName(ListingSource source) => source switch
    {
        ListingSource.FfxivVenues => "FFXIV Venues",
        ListingSource.Xivpf => "Party Finder (xivpf)",
        ListingSource.Partake => "Partake",
        _ => "Party Finder",
    };

    private static string SeenAgo(TimeSpan ago) =>
        ago.TotalMinutes < 1 ? "seen just now"
        : ago.TotalHours < 1 ? $"seen {(int)ago.TotalMinutes} min ago"
        : ago.TotalDays < 1 ? $"seen {(int)ago.TotalHours} h ago"
        : $"seen {(int)ago.TotalDays} d ago";
}
