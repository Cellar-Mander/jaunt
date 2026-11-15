using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Jaunt.Core.Browser;
using Jaunt.Core.Display;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;
using Jaunt.Travel;

namespace Jaunt.Windows;

/// <summary>
/// The listing browser: the day-grouped list of listings on the left and the selected listing's
/// details on the right. Lists in-game and xivpf Party Finder ads with an address, FFXIV Venues venues
/// with an address, Partake events and FFXIV RP Event Calendar events with an address or location, on
/// worlds the character can travel to, at the openings <see cref="Schedules.Listed"/> picks. Listings at
/// one address with overlapping openings show as one entry (<see cref="Duplicates"/>).
/// </summary>
internal sealed class ListingsWindow : Window
{
    private static readonly TimeSpan RebuildInterval = TimeSpan.FromSeconds(1);

    /// <summary>Shown when there is nothing to list; one is picked each time the list becomes empty.</summary>
    private static readonly string[] EmptyLines =
    [
        "No one here but us chickens.",
        "The wards are quiet. Too quiet.",
        "Even the bartenders have gone home.",
        "Nothing on tonight. Perhaps host something?",
        "Tumbleweeds roll through the plaza.",
    ];

    /// <summary>Lines of About shown before "Read more".</summary>
    private const int AboutLines = 6;

    private readonly ListingCache cache;
    private readonly Gazetteer gazetteer;
    private readonly AddressParser parser;
    private readonly Lifestream lifestream;
    private readonly INotificationManager notifications;
    private readonly Configuration configuration;
    private readonly Action fetchSources;
    private readonly Func<TravelRegion?> region;
    private readonly IFontHandle titleFont;

    // Parsed once per ad text or event location and world, and derived once per venue's fields, so
    // drawing doesn't re-parse every frame
    private readonly Dictionary<(ListingSource, string), (string Text, string? ListedOn, ListingDetails Details)> parsed = [];
    private readonly Dictionary<(ListingSource, string), (string Text, DateOnly Seen, IReadOnlyList<OpeningTime> Times)> adTimes = [];
    private readonly Dictionary<(ListingSource, string), (string Text, string About)> abouts = [];
    private readonly Dictionary<string, (string Name, FfxivVenues.Fields? Fields, FfxivVenues.Shown Shown)> shownVenues = [];
    private readonly Dictionary<string, (CalendarFields Fields, RpEventCalendar.Shown Shown)> shownEvents = [];

    private IReadOnlyList<DayGroup<Row>> groups = [];
    // Entries listed, each once however many of its openings are listed
    private int listingCount;
    // Each listed row by its listing, for the detail pane; a listing listed twice (open now and next) is one row
    private readonly Dictionary<(ListingSource, string), Row> rowsByKey = [];
    private DateTimeOffset nextRebuild;
    private bool loggedOut;
    private int emptyLine = -1;
    private bool aboutExpanded;

    // Texts cut to a width, worked out once per text and width rather than every frame
    private readonly Dictionary<(string Text, int Width, int FontSize), string> fitted = [];
    private readonly Dictionary<(string Text, int Width), string> aboutCuts = [];

    /// <summary>
    /// One entry as the browser shows it: its title, addresses and links, world, schedule, what is
    /// shown of a Partake or RP calendar event, and an ad's text for About. A merged entry's
    /// <see cref="Listing"/> is the member whose title it shows, and <see cref="Members"/> lists them all.
    /// </summary>
    private sealed record Row(
        CachedListing Listing,
        string Title,
        ListingDetails Details,
        string? World,
        ListingSchedule Schedule,
        string? About,
        Partake.Shown? Event = null,
        RpEventCalendar.Shown? CalendarEvent = null)
    {
        public (ListingSource, string) Key => (Listing.Source, Listing.Id);

        /// <summary>The rows of the listings shown as this entry, title first; set when the list is rebuilt.</summary>
        public IReadOnlyList<Row> Members { get; init; } = [];

        /// <summary>The openings listed, earliest first; set when the list is rebuilt.</summary>
        public IReadOnlyList<Occurrence> Listed { get; init; } = [];
    }

    /// <summary>
    /// A calendar event's fields that <see cref="RpEventCalendar.Show"/> reads. Recording a fetch assigns
    /// new objects, so a reference check finds every change.
    /// </summary>
    private readonly record struct CalendarFields(string Title, string? Location, string? ListedOn, string? Rating, IReadOnlyList<string> LinkTexts)
    {
        public static CalendarFields Of(CachedListing listing) => new(listing.Title, listing.Location, listing.ListedOn, listing.Rating, listing.LinkTexts);

        public bool Same(CalendarFields other) =>
            ReferenceEquals(Title, other.Title) && ReferenceEquals(Location, other.Location) && ReferenceEquals(ListedOn, other.ListedOn)
            && ReferenceEquals(Rating, other.Rating) && ReferenceEquals(LinkTexts, other.LinkTexts);
    }

    /// <param name="gazetteer">Worlds, for the address parser and to correct venues' and events' worlds when shown.</param>
    /// <param name="fetchSources">Fetches each fetched source whose cooldown has passed; called on opening.</param>
    /// <param name="region">The worlds the logged-in character can travel to, or null while none is; only listings on them are shown.</param>
    /// <param name="openSettings">Opens the Settings window, from the title bar's cog.</param>
    /// <param name="titleFont">The larger font listing titles are drawn in; owned by the caller.</param>
    public ListingsWindow(
        ListingCache cache, Gazetteer gazetteer, Lifestream lifestream, INotificationManager notifications, Configuration configuration, Action fetchSources,
        Func<TravelRegion?> region, Action openSettings, IFontHandle titleFont)
        : base("Jaunt###JauntListings")
    {
        this.titleFont = titleFont;
        this.region = region;
        this.fetchSources = fetchSources;
        this.cache = cache;
        this.gazetteer = gazetteer;
        parser = new AddressParser(gazetteer);
        this.lifestream = lifestream;
        this.notifications = notifications;
        this.configuration = configuration;
        Size = new Vector2(900, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        // Fits a Steam Deck (1280×800) at a raised font scale; the single-column layout for narrower windows is 2dce5769
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560, 320), MaximumSize = new Vector2(2400, 1600) };
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 1),
            Click = _ => openSettings(),
            ShowTooltip = () => ImGui.SetTooltip("Settings"),
        });
    }

    public override void OnOpen()
    {
        fetchSources();
        nextRebuild = DateTimeOffset.MinValue;
        aboutExpanded = false;
    }

    public override void Draw()
    {
        var now = DateTimeOffset.UtcNow;
        if (now >= nextRebuild)
        {
            Rebuild(now);
            nextRebuild = now + RebuildInterval;
        }

        if (loggedOut)
        {
            ImGui.TextWrapped("Log in to a character to see the venues it can travel to.");
            return;
        }

        // Filters and search (d68d407e) go on this line, left of the count
        var count = listingCount == 1 ? "1 listing" : $"{listingCount.ToString(CultureInfo.CurrentCulture)} listings";
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(count).X));
        ImGui.TextDisabled(count);

        var listWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * 0.4f, 240 * ImGuiHelpers.GlobalScale, 420 * ImGuiHelpers.GlobalScale);
        using (var list = ImRaii.Child("##list", new Vector2(listWidth, 0), true))
        {
            if (list.Success)
                DrawList(now);
        }

        ImGui.SameLine();
        using (var detail = ImRaii.Child("##detail", Vector2.Zero, true))
        {
            if (detail.Success)
                DrawDetail(now);
        }
    }

    private void DrawList(DateTimeOffset now)
    {
        if (groups.Count == 0)
        {
            ImGui.TextWrapped(EmptyLines[emptyLine]);
            return;
        }

        var locale = CultureInfo.CurrentCulture;
        var style = ImGui.GetStyle();
        float titleHeight;
        using (titleFont.Push())
            titleHeight = ImGui.GetTextLineHeight();
        var rowHeight = (ImGui.GetTextLineHeight() * 2) + titleHeight + (style.ItemSpacing.Y * 2);
        var selected = SelectedKey;
        foreach (var group in groups)
        {
            var (main, dimmed) = DayGroups.Heading(group, configuration.DateFormat, locale);
            ImGui.TextUnformatted(main);
            if (dimmed.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(dimmed);
            }

            ImGui.Separator();
            for (var i = 0; i < group.Entries.Count; i++)
            {
                // FFXIV Venues alone lists hundreds of venues, so rows out of view are only spaced, not drawn
                if (!ImGui.IsRectVisible(new Vector2(1, rowHeight)))
                {
                    ImGui.Dummy(new Vector2(1, rowHeight));
                    continue;
                }

                DrawRow(group.Entries[i], selected, rowHeight, now, locale);
            }

            ImGui.Spacing();
        }
    }

    /// <summary>A placeholder icon, then the status line, the title cut to fit and the world.</summary>
    private void DrawRow(BrowserEntry<Row> entry, (ListingSource, string)? selected, float rowHeight, DateTimeOffset now, CultureInfo locale)
    {
        var row = entry.Item;
        var start = ImGui.GetCursorScreenPos();
        using var id = ImRaii.PushId($"{(int)row.Listing.Source}:{row.Listing.Id}:{entry.When.Start?.UtcTicks}");
        var isSelected = selected is { } key && rowsByKey.TryGetValue(key, out var selectedRow) && ReferenceEquals(selectedRow, row);
        if (ImGui.Selectable("##row", isSelected, ImGuiSelectableFlags.None, new Vector2(0, rowHeight)))
            Select(row);
        var after = ImGui.GetCursorScreenPos();

        // Icons and images are 185b66ad; a plain square holds their place
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.GetWindowDrawList().AddRectFilled(start, start + new Vector2(rowHeight, rowHeight), ImGui.GetColorU32(ImGuiCol.FrameBg), 4 * ImGuiHelpers.GlobalScale);
        var textLeft = start.X + rowHeight + spacing;
        var width = ImGui.GetContentRegionMax().X + ImGui.GetWindowPos().X - textLeft;

        ImGui.SetCursorScreenPos(new Vector2(textLeft, start.Y));
        var status = StatusText.Row(entry.When, entry.AlwaysOpen, now, TimeZoneInfo.Local, configuration.TimeFormat, configuration.DateFormat, locale);
        if (entry.AlwaysOpen || entry.When.IsOpen(now))
            ImGui.TextColored(ImGuiColors.HealerGreen, Fit(status, width));
        else
            ImGui.TextUnformatted(Fit(status, width));
        ImGui.SetCursorScreenPos(new Vector2(textLeft, ImGui.GetCursorScreenPos().Y));
        using (titleFont.Push())
            ImGui.TextUnformatted(Fit(ShownText.Title(row.Title), width));
        ImGui.SetCursorScreenPos(new Vector2(textLeft, ImGui.GetCursorScreenPos().Y));
        ImGui.TextDisabled(Fit(row.World ?? "", width));

        ImGui.SetCursorScreenPos(after);
    }

    private void DrawDetail(DateTimeOffset now)
    {
        // With nothing listed, the list's own line says so and the help text would only add noise
        if (groups.Count == 0)
            return;

        if (SelectedKey is not { } key || !rowsByKey.TryGetValue(key, out var row))
        {
            ImGui.TextDisabled("Select a listing on the left to see its details.");
            return;
        }

        var locale = CultureInfo.CurrentCulture;
        var width = ImGui.GetContentRegionAvail().X;

        // Splash images are 185b66ad; a plain band holds their place
        var top = ImGui.GetCursorScreenPos();
        var splashHeight = Math.Min(width * 0.3f, 140 * ImGuiHelpers.GlobalScale);
        ImGui.GetWindowDrawList().AddRectFilled(top, top + new Vector2(width, splashHeight), ImGui.GetColorU32(ImGuiCol.FrameBg), 4 * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(width, splashHeight));

        using (titleFont.Push())
            ImGui.TextWrapped(ShownText.Title(row.Title));
        DrawDetailStatus(row, now, locale);
        if (row.World is { } world)
            ImGui.TextDisabled(world);

        ImGui.Spacing();
        DrawActions(row);

        DrawLocation(row);
        DrawAbout(row, width);
        DrawRating(row);
        DrawFooter(row);
    }

    /// <summary>The opening listed first, and for a recurring listing open now, the next.</summary>
    private void DrawDetailStatus(Row row, DateTimeOffset now, CultureInfo locale)
    {
        var listed = row.Listed;
        if (listed.Count == 0)
            return;

        var first = listed[0];
        var always = row.Schedule.AlwaysOpen;
        var text = StatusText.Detail(first, always, now, TimeZoneInfo.Local, configuration.TimeFormat, configuration.DateFormat, locale);
        if (always || first.IsOpen(now))
            ImGui.TextColored(ImGuiColors.HealerGreen, text);
        else
            ImGui.TextUnformatted(text);

        if (!row.Schedule.OneOff && listed.Count > 1)
            ImGui.TextDisabled("Next: " + StatusText.Detail(listed[1], false, now, TimeZoneInfo.Local, configuration.TimeFormat, configuration.DateFormat, locale));
    }

    /// <summary>Travel to the first address, and open the first link that isn't a Discord invite.</summary>
    private void DrawActions(Row row)
    {
        var address = row.Details.Addresses.Count > 0 ? row.Details.Addresses[0] : null;
        if (address is not null)
        {
            var request = TravelRequest.From(address);
            var whyNot = !lifestream.Available ? "Install and enable Lifestream to travel from Jaunt."
                : request is null ? "This address is missing a world, district, plot or apartment, so Lifestream can't travel there."
                : null;

            using (ImRaii.Disabled(whyNot is not null))
            {
                if (ImGui.Button("Travel") && request is not null && lifestream.Travel(request) is { } error)
                    Notify(error, NotificationType.Warning);
            }

            if (whyNot is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(whyNot);
        }

        if (PageIndex(row.Details.Links) is var page and >= 0)
        {
            if (address is not null)
                ImGui.SameLine();
            var link = row.Details.Links[page];
            if (ImGui.Button("Open page"))
                Util.OpenLink(link.Uri.AbsoluteUri);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(link.Uri.AbsoluteUri);
        }
    }

    /// <summary>
    /// Each address with Copy, a location the parser couldn't read as text, and any further links
    /// (all but Open page's and the footer's Discord).
    /// </summary>
    private void DrawLocation(Row row)
    {
        var freeText = row.Details.Addresses.Count == 0
            ? row.CalendarEvent?.Location ?? (row.Event is { Location.Length: > 0 } item ? item.Location : null)
            : null;
        var links = row.Details.Links;
        var page = PageIndex(links);
        var discord = DiscordIndex(links);
        var further = links.Count - (page >= 0 ? 1 : 0) - (discord >= 0 ? 1 : 0);
        if (row.Details.Addresses.Count == 0 && freeText is null && further == 0)
            return;

        Section("Location");
        for (var i = 0; i < row.Details.Addresses.Count; i++)
        {
            using var id = ImRaii.PushId($"a{i}");
            var address = row.Details.Addresses[i];
            var text = AddressText.Format(address);
            if (ImGui.SmallButton("Copy"))
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

        if (freeText is not null)
            ImGui.TextWrapped(ShownText.Fold(freeText));

        for (var i = 0; i < links.Count; i++)
        {
            if (i == page || i == discord)
                continue;
            using var id = ImRaii.PushId($"l{i}");
            var link = links[i];
            if (ImGui.SmallButton("Open"))
                Util.OpenLink(link.Uri.AbsoluteUri);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(link.Uri.AbsoluteUri);
            ImGui.SameLine();
            ImGui.TextUnformatted(link.Text);
        }
    }

    /// <summary>An ad's text, cut to <see cref="AboutLines"/> lines with "Read more" when longer.</summary>
    private void DrawAbout(Row row, float width)
    {
        if (row.About is not { Length: > 0 } about)
            return;

        Section("About");
        var cut = aboutExpanded ? about : CutToLines(about, width);
        ImGui.TextWrapped(cut);
        if (!ReferenceEquals(cut, about) || aboutExpanded)
        {
            if (ImGui.SmallButton(aboutExpanded ? "Show less" : "Read more"))
                aboutExpanded = !aboutExpanded;
        }
    }

    /// <summary>The content rating a calendar event gives, as information.</summary>
    private static void DrawRating(Row row)
    {
        if (row.CalendarEvent?.Rating is not { } rating)
            return;

        Section("Age / rating");
        ImGui.TextUnformatted(rating);
    }

    /// <summary>
    /// The Discord invite, then where the listing came from, with the credits its source needs.
    /// "Listed by" (the ad's poster) goes first once d12eb2c4 keeps it.
    /// </summary>
    private static void DrawFooter(Row row)
    {
        ImGui.Spacing();
        ImGui.Separator();
        if (DiscordIndex(row.Details.Links) is var discord and >= 0)
        {
            DrawLinkText("Discord", row.Details.Links[discord].Uri);
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
        }

        // Every source on one line, each linking to its site (the in-game Party Finder has none), with the calendar's licence after
        var sources = row.Members.Select(m => m.Listing.Source).Distinct().ToList();
        ImGui.TextDisabled("Sources:");
        for (var i = 0; i < sources.Count; i++)
        {
            ImGui.SameLine(0, 0);
            ImGui.TextDisabled(i == 0 ? " " : ", ");
            ImGui.SameLine(0, 0);
            if (SiteUrl(sources[i]) is { } site)
                DrawLinkText(SourceName(sources[i]), site);
            else
                ImGui.TextDisabled(SourceName(sources[i]));
        }

        if (sources.Contains(ListingSource.RpEventCalendar))
        {
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
            DrawLinkText(RpEventCalendar.LicenceName, RpEventCalendar.LicenceUrl);
        }

        if (row.Event is not null)
            ImGui.TextDisabled(Partake.Disclaimer);
    }

    /// <summary>The first link that isn't a Discord invite, for Open page, or -1.</summary>
    private static int PageIndex(IReadOnlyList<WebLink> links)
    {
        for (var i = 0; i < links.Count; i++)
        {
            if (!Core.Text.WebLinks.IsDiscordInvite(links[i].Uri))
                return i;
        }

        return -1;
    }

    /// <summary>The first Discord invite, for the footer, or -1.</summary>
    private static int DiscordIndex(IReadOnlyList<WebLink> links)
    {
        for (var i = 0; i < links.Count; i++)
        {
            if (Core.Text.WebLinks.IsDiscordInvite(links[i].Uri))
                return i;
        }

        return -1;
    }

    private static void Section(string name)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(name);
    }

    /// <summary>Text, dimmed unless asked otherwise, that shows its address on hover and opens it when clicked.</summary>
    internal static void DrawLinkText(string text, Uri uri, bool dimmed = true)
    {
        if (dimmed)
            ImGui.TextDisabled(text);
        else
            ImGui.TextUnformatted(text);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(uri.AbsoluteUri);
        }

        if (ImGui.IsItemClicked())
            Util.OpenLink(uri.AbsoluteUri);
    }

    private (ListingSource, string)? SelectedKey =>
        configuration.SelectedSource is { } source && configuration.SelectedId is { } id ? (source, id) : null;

    private void Select(Row row)
    {
        if (SelectedKey is { } key && row.Members.Any(m => m.Key == key))
            return;
        configuration.SelectedSource = row.Listing.Source;
        configuration.SelectedId = row.Listing.Id;
        configuration.Save();
        aboutExpanded = false;
    }

    private void ClearSelection()
    {
        configuration.SelectedSource = null;
        configuration.SelectedId = null;
        configuration.Save();
    }

    /// <summary>The text cut with "…" to fit <paramref name="width"/> pixels.</summary>
    private string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
            return text;

        // The title font is larger, so a cut fits one font only
        var key = (text, (int)width, (int)ImGui.GetFontSize());
        if (fitted.TryGetValue(key, out var shown))
            return shown;

        // The longest prefix that fits with the ellipsis, by binary search over its length
        int low = 0, high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid].TrimEnd() + "…").X <= width)
                low = mid;
            else
                high = mid - 1;
        }

        shown = text[..low].TrimEnd() + "…";
        Remember(fitted, key, shown);
        return shown;
    }

    /// <summary>The text cut with "…" to <see cref="AboutLines"/> wrapped lines, or the text itself when it fits.</summary>
    private string CutToLines(string text, float width)
    {
        var maxHeight = ImGui.GetTextLineHeight() * AboutLines;
        if (ImGui.CalcTextSize(text, false, width).Y <= maxHeight + 1)
            return text;

        var key = (text, (int)width);
        if (aboutCuts.TryGetValue(key, out var cut))
            return cut;

        int low = 0, high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid].TrimEnd() + "…", false, width).Y <= maxHeight + 1)
                low = mid;
            else
                high = mid - 1;
        }

        cut = text[..low].TrimEnd() + "…";
        Remember(aboutCuts, key, cut);
        return cut;
    }

    private static void Remember<TKey>(Dictionary<TKey, string> memo, TKey key, string value)
        where TKey : notnull
    {
        // Enough for every row at a few widths; past it the memo starts again rather than grow
        if (memo.Count >= 8192)
            memo.Clear();
        memo[key] = value;
    }

    /// <summary>
    /// Works out the listed rows and groups them by day, parsing only ad text, locations and venue and
    /// event fields that changed. An xivpf ad the game has also sent shows once, as the in-game copy.
    /// Forgets the selection when its listing is no longer listed.
    /// </summary>
    private void Rebuild(DateTimeOffset now)
    {
        rowsByKey.Clear();
        listingCount = 0;
        var entries = new List<BrowserEntry<Row>>();
        var travel = region();
        loggedOut = travel is null;
        if (travel is null)
        {
            groups = [];
            return;
        }

        var adRegion = travel.DataCentres.Select(Schedules.RegionOf).FirstOrDefault(r => r is not null) ?? PartyFinderRegion.Europe;
        var rows = new List<(Row Row, AddressKey? Address)>();
        foreach (var listing in cache.Listings)
        {
            // A venue's world is checked once it is shown, as that corrects misspellings
            if (listing.Hidden || (listing.Source != ListingSource.FfxivVenues && !travel.Includes(listing)))
                continue;

            if (RowFor(listing, travel, adRegion, now) is { } row)
                rows.Add((row, row.Details.Addresses.Count > 0 ? AddressKey.Of(row.Details.Addresses[0]) : null));
        }

        var candidates = new List<MergeCandidate<Row>>(rows.Count);
        foreach (var (row, address) in WithAddressZones(rows, adRegion, now))
        {
            var listed = Schedules.Listed(row.Schedule, now);
            if (listed.Count > 0)
                candidates.Add(new MergeCandidate<Row>(row, address, row.Schedule, listed));
        }

        foreach (var merged in Duplicates.Merge(candidates, now))
        {
            var row = Entry(merged);
            listingCount++;
            foreach (var member in row.Members)
                rowsByKey[member.Key] = row;
            var key = $"{(int)row.Listing.Source}:{row.Listing.Id}";
            foreach (var when in row.Listed)
                entries.Add(new BrowserEntry<Row>(row, row.Title, key, when, row.Schedule.AlwaysOpen));
        }

        groups = DayGroups.Group(entries, now, TimeZoneInfo.Local);
        if (groups.Count == 0)
        {
            if (emptyLine < 0)
                emptyLine = Random.Shared.Next(EmptyLines.Length);
        }
        else
        {
            emptyLine = -1;
        }

        if (SelectedKey is { } selected && !rowsByKey.ContainsKey(selected))
            ClearSelection();

        Forget();
    }

    /// <summary>
    /// The rows, with each ad's times that state no zone read in the zone of a venue or event at its
    /// address (<see cref="Duplicates.UnstatedZone"/>) rather than the region default.
    /// </summary>
    private IEnumerable<(Row Row, AddressKey? Address)> WithAddressZones(List<(Row Row, AddressKey? Address)> rows, PartyFinderRegion adRegion, DateTimeOffset now)
    {
        var venueZones = new Dictionary<AddressKey, TimeZoneInfo>();
        var starts = new Dictionary<AddressKey, HashSet<DateTimeOffset>>();
        foreach (var (row, address) in rows)
        {
            if (address is not { } key || row.Listing.Source is not (ListingSource.FfxivVenues or ListingSource.Partake))
                continue;
            if (row.Listing.Source == ListingSource.FfxivVenues && FfxivVenues.Zone(row.Listing.Venue) is { } zone)
                venueZones.TryAdd(key, zone);
            if (!starts.TryGetValue(key, out var set))
                starts[key] = set = [];
            foreach (var occurrence in row.Schedule.Occurrences)
            {
                if (occurrence.Start is { } start)
                    set.Add(start);
            }
        }

        foreach (var (row, address) in rows)
        {
            if (address is not { } key || !starts.TryGetValue(key, out var there)
                || row.Listing.Source is not (ListingSource.InGamePartyFinder or ListingSource.Xivpf))
            {
                yield return (row, address);
                continue;
            }

            var listing = row.Listing;
            var times = AdTimes(listing);
            if (Duplicates.UnstatedZone(times, listing.LastSeen, venueZones.GetValueOrDefault(key), there) is not { } unstated)
            {
                yield return (row, address);
                continue;
            }

            var stillListed = Schedules.StillListed(listing.Source, listing.LastSeen, now, configuration.RefreshCooldownMinutes);
            yield return (row with { Schedule = Schedules.ForAd(times, listing.LastSeen, adRegion, stillListed, now, unstated) }, address);
        }
    }

    /// <summary>
    /// The entry for listings shown as one: the title of the member <see cref="Duplicates.TitleRank"/>
    /// puts first, the addresses and world of the one <see cref="Duplicates.AddressRank"/> does among
    /// those naming a plot or apartment, every member's links once each, and the About of the ad that
    /// stops being listed soonest.
    /// </summary>
    private Row Entry(MergedListing<Row> merged)
    {
        if (merged.Members.Count == 1)
            return merged.Members[0] with { Members = merged.Members, Listed = merged.Listed };

        var byTitle = merged.Members.OrderBy(m => Duplicates.TitleRank(m.Listing.Source)).ToList();
        var byAddress = merged.Members
            .OrderBy(m => m.Details.Addresses.Count == 0 || AddressKey.Of(m.Details.Addresses[0]) is not { WardOnly: false })
            .ThenBy(m => Duplicates.AddressRank(m.Listing.Source))
            .First();
        var links = new List<WebLink>();
        foreach (var link in byTitle.SelectMany(m => m.Details.Links))
        {
            if (!links.Exists(l => l.Uri == link.Uri))
                links.Add(link);
        }

        var about = byTitle.Where(m => m.About is { Length: > 0 })
            .MinBy(m => Schedules.ListedUntil(m.Listing.Source, m.Listing.LastSeen, configuration.RefreshCooldownMinutes))?.About;
        var first = byTitle[0];
        return new Row(
            first.Listing, first.Title, byAddress.Details with { Links = links }, byAddress.World, merged.Schedule, about,
            byTitle.Select(m => m.Event).FirstOrDefault(e => e is not null),
            byTitle.Select(m => m.CalendarEvent).FirstOrDefault(e => e is not null))
        {
            Members = byTitle,
            Listed = merged.Listed,
        };
    }

    /// <summary>The row for a listing, or null when it isn't one the browser lists (such as an ad with no address).</summary>
    private Row? RowFor(CachedListing listing, TravelRegion travel, PartyFinderRegion adRegion, DateTimeOffset now)
    {
        switch (listing.Source)
        {
            case ListingSource.FfxivVenues:
                {
                    // Recording a fetch assigns new name and field objects, so a reference check finds every change
                    if (!shownVenues.TryGetValue(listing.Id, out var venue)
                        || !ReferenceEquals(venue.Name, listing.Title) || !ReferenceEquals(venue.Fields, listing.Venue))
                    {
                        venue = (listing.Title, listing.Venue, FfxivVenues.Show(listing, gazetteer));
                        shownVenues[listing.Id] = venue;
                    }

                    var shown = venue.Shown;
                    if (shown.Address is not { } address || !travel.Includes(shown))
                        return null;
                    return new Row(listing, shown.Title, new ListingDetails([address], WebLinks(shown.Links)), address.World,
                        Schedules.ForVenue(listing.Venue, now), About: null);
                }

            case ListingSource.Partake:
                {
                    // Shown without an address too: the location is free text the parser may not read
                    var item = Partake.Show(listing, gazetteer);
                    var location = Parsed(listing, item.Location, item.World);
                    var world = item.DataCentre is { } dataCentre ? $"{item.World} ({dataCentre})" : item.World;
                    return new Row(listing, item.Title, location with { Links = WebLinks(item.Links) }, world,
                        Schedules.ForEvent(listing.StartsAt, listing.EndsAt), About: null, Event: item);
                }

            case ListingSource.RpEventCalendar:
                {
                    var fields = CalendarFields.Of(listing);
                    if (!shownEvents.TryGetValue(listing.Id, out var calendar) || !calendar.Fields.Same(fields))
                    {
                        calendar = (fields, RpEventCalendar.Show(listing, parser));
                        shownEvents[listing.Id] = calendar;
                    }

                    // Events in the open world have only a location, which the detail shows as text
                    var calendarEvent = calendar.Shown;
                    if (calendarEvent.Address is null && calendarEvent.Location is null)
                        return null;
                    var details = new ListingDetails(calendarEvent.Address is { } at ? [at] : [], WebLinks(calendarEvent.Links));
                    return new Row(listing, calendarEvent.Title, details, calendarEvent.Address?.World ?? listing.ListedOn,
                        Schedules.ForOpenings(listing.Openings, listing.OneOff), About: null, CalendarEvent: calendarEvent);
                }

            case ListingSource.InGamePartyFinder or ListingSource.Xivpf:
                {
                    if (Xivpf.ShownInGame(cache, listing))
                        return null;
                    var adDetails = Parsed(listing, listing.Description, listing.ListedOn);
                    if (adDetails.Addresses.Count == 0)
                        return null;
                    var stillListed = Schedules.StillListed(listing.Source, listing.LastSeen, now, configuration.RefreshCooldownMinutes);
                    var schedule = Schedules.ForAd(AdTimes(listing), listing.LastSeen, adRegion, stillListed, now);
                    return new Row(listing, listing.Title, adDetails, adDetails.Addresses[0].World ?? listing.ListedOn, schedule, About(listing));
                }

            default:
                return null;
        }
    }

    /// <summary>Forgets ads, venues and events the cache no longer holds (cleared or expired).</summary>
    private void Forget()
    {
        ForgetMissing(parsed);
        ForgetMissing(adTimes);
        ForgetMissing(abouts);
        if (shownVenues.Count > cache.Count)
        {
            foreach (var key in shownVenues.Keys.Where(k => cache.Find(ListingSource.FfxivVenues, k) is null).ToList())
                shownVenues.Remove(key);
        }

        if (shownEvents.Count > cache.Count)
        {
            foreach (var key in shownEvents.Keys.Where(k => cache.Find(ListingSource.RpEventCalendar, k) is null).ToList())
                shownEvents.Remove(key);
        }
    }

    private void ForgetMissing<T>(Dictionary<(ListingSource, string), T> memo)
    {
        if (memo.Count <= cache.Count)
            return;
        foreach (var key in memo.Keys.Where(k => cache.Find(k.Item1, k.Item2) is null).ToList())
            memo.Remove(key);
    }

    // Equals checks the reference first, so an unchanged ad costs no character comparison
    private ListingDetails Parsed(CachedListing listing, string text, string? listedOn)
    {
        var key = (listing.Source, listing.Id);
        if (!parsed.TryGetValue(key, out var entry)
            || !string.Equals(entry.Text, text, StringComparison.Ordinal) || !string.Equals(entry.ListedOn, listedOn, StringComparison.Ordinal))
        {
            entry = (text, listedOn, ListingDetails.Parse(parser, text, listedOn));
            parsed[key] = entry;
        }

        return entry.Details;
    }

    /// <summary>An ad's opening times as written, parsed once per text and day seen.</summary>
    private IReadOnlyList<OpeningTime> AdTimes(CachedListing listing)
    {
        var key = (listing.Source, listing.Id);
        var seen = DateOnly.FromDateTime(listing.LastSeen.UtcDateTime);
        if (!adTimes.TryGetValue(key, out var entry) || entry.Seen != seen || !string.Equals(entry.Text, listing.Description, StringComparison.Ordinal))
        {
            entry = (listing.Description, seen, Schedules.ParseAd(listing.Description, listing.LastSeen));
            adTimes[key] = entry;
        }

        return entry.Times;
    }

    /// <summary>An ad's text for About, with the game's glyphs decoded, worked out once per text.</summary>
    private string About(CachedListing listing)
    {
        var key = (listing.Source, listing.Id);
        if (!abouts.TryGetValue(key, out var entry) || !string.Equals(entry.Text, listing.Description, StringComparison.Ordinal))
        {
            entry = (listing.Description, PartyFinderText.Normalise(listing.Description).Text);
            abouts[key] = entry;
        }

        return ShownText.Fold(entry.About);
    }

    private static IReadOnlyList<WebLink> WebLinks(IEnumerable<Uri> links) => [.. links.Select(uri => new WebLink(uri.OriginalString, uri))];

    private void Notify(string text, NotificationType type) =>
        notifications.AddNotification(new Notification { Title = "Jaunt", Content = text, Type = type });

    private static Uri? SiteUrl(ListingSource source) => source switch
    {
        ListingSource.FfxivVenues => FfxivVenues.SiteUrl,
        ListingSource.Xivpf => Xivpf.SiteUrl,
        ListingSource.Partake => Partake.SiteUrl,
        ListingSource.RpEventCalendar => RpEventCalendar.SiteUrl,
        _ => null,
    };

    private static string SourceName(ListingSource source) => source switch
    {
        ListingSource.FfxivVenues => "FFXIV Venues",
        ListingSource.Xivpf => "xivpf",
        ListingSource.Partake => "Partake",
        ListingSource.RpEventCalendar => "FFXIV RP Event Calendar",
        _ => "Party Finder",
    };
}
