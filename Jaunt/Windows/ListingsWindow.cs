using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;
using Jaunt.Travel;

namespace Jaunt.Windows;

/// <summary>
/// The MVP listing window: Party Finder venue ads with a parsed address, newest first, each address
/// with Travel and Copy address and each link with Open. The two-pane browser (cdd9e95f) replaces it.
/// </summary>
internal sealed class ListingsWindow : Window
{
    private static readonly TimeSpan RebuildInterval = TimeSpan.FromSeconds(1);

    private readonly ListingCache cache;
    private readonly AddressParser parser;
    private readonly Lifestream lifestream;
    private readonly INotificationManager notifications;

    // Parsed once per ad text, so drawing doesn't re-parse every frame
    private readonly Dictionary<(ListingSource, string), (string Description, ListingDetails Details)> parsed = [];
    private List<(CachedListing Listing, ListingDetails Details)> rows = [];
    private DateTimeOffset nextRebuild;

    public ListingsWindow(ListingCache cache, AddressParser parser, Lifestream lifestream, INotificationManager notifications)
        : base("Jaunt###JauntListings")
    {
        this.cache = cache;
        this.parser = parser;
        this.lifestream = lifestream;
        this.notifications = notifications;
        Size = new Vector2(520, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 240), MaximumSize = new Vector2(1600, 1600) };
    }

    public override void OnOpen() => nextRebuild = DateTimeOffset.MinValue;

    public override void Draw()
    {
        var now = DateTimeOffset.UtcNow;
        if (now >= nextRebuild)
        {
            Rebuild();
            nextRebuild = now + RebuildInterval;
        }

        if (rows.Count == 0)
        {
            ImGui.TextWrapped("No venue ads with an address yet. Open the Party Finder and browse the None category to load them.");
            return;
        }

        var travelAvailable = lifestream.Available;
        foreach (var (listing, details) in rows)
        {
            using var id = ImRaii.PushId($"{(int)listing.Source}:{listing.Id}");
            ImGui.TextUnformatted(listing.Title.Length > 0 ? listing.Title : "(untitled)");
            ImGui.SameLine();
            ImGui.TextDisabled(SeenAgo(now - listing.LastSeen));

            using (ImRaii.PushIndent())
            {
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

    /// <summary>Lists the visible ads that have an address, newest first, parsing only text that changed.</summary>
    private void Rebuild()
    {
        var next = new List<(CachedListing, ListingDetails)>();
        foreach (var listing in cache.Listings)
        {
            if (listing.Hidden || listing.Source != ListingSource.InGamePartyFinder)
                continue;

            var key = (listing.Source, listing.Id);
            if (!parsed.TryGetValue(key, out var entry) || !ReferenceEquals(entry.Description, listing.Description))
            {
                entry = (listing.Description, ListingDetails.Parse(parser, listing.Description, listing.ListedOn));
                parsed[key] = entry;
            }

            if (entry.Details.Addresses.Count > 0)
                next.Add((listing, entry.Details));
        }

        rows = [.. next.OrderByDescending(r => r.Item1.LastSeen)];

        // Forget ads the cache no longer holds (cleared or expired)
        if (parsed.Count > cache.Count)
        {
            foreach (var key in parsed.Keys.Where(k => cache.Find(k.Item1, k.Item2) is null).ToList())
                parsed.Remove(key);
        }
    }

    private void Notify(string text, NotificationType type) =>
        notifications.AddNotification(new Notification { Title = "Jaunt", Content = text, Type = type });

    private static string SeenAgo(TimeSpan ago) =>
        ago.TotalMinutes < 1 ? "seen just now"
        : ago.TotalHours < 1 ? $"seen {(int)ago.TotalMinutes} min ago"
        : ago.TotalDays < 1 ? $"seen {(int)ago.TotalHours} h ago"
        : $"seen {(int)ago.TotalDays} d ago";
}
