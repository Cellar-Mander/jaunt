using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Jaunt.Core.Browser;
using Jaunt.Core.Display;
using Jaunt.Core.Images;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;
using Jaunt.Listings;
using Jaunt.Travel;

namespace Jaunt.Windows;

/// <summary>
/// The listing browser: the day-grouped list of listings on the left and the selected listing's
/// details on the right, or one column showing either when the window is narrow. Lists in-game and xivpf Party Finder ads with an address, FFXIV Venues venues
/// with an address, Partake events and FFXIV RP Event Calendar events with an address or location, on
/// worlds the character can travel to, at the openings <see cref="Schedules.Listed"/> picks. Listings at
/// one address with overlapping openings, and ads with identical text, show as one entry
/// (<see cref="Duplicates"/>). Search and
/// filter chips above the list narrow it (<see cref="ListingFilters"/>).
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
    private readonly GamePartyFinder partyFinder;
    private readonly HousingMap housingMap;
    private readonly GamePlaces places;
    private readonly INotificationManager notifications;
    private readonly Configuration configuration;
    private readonly Action fetchSources;
    private readonly Action saveCache;
    private readonly Func<TravelRegion?> region;
    private readonly HeldFont titleFont;
    private readonly HeldFont iconFont;
    private readonly ImageStore images;
    private readonly ITextureProvider textures;

    // Each icon's drawn shape (top left and bottom right) in the icon font at a size, or null if it lacks the icon,
    // with its text, as ToIconString makes a new string on every call
    private static readonly Dictionary<(FontAwesomeIcon, float), (Vector2 Min, Vector2 Max, string Text)?> IconShapes = [];

    // Parsed once per ad text or event location and world, and derived once per venue's fields, so
    // drawing doesn't re-parse every frame
    private readonly Dictionary<(ListingSource, string), (string Text, string? ListedOn, ListingDetails Details)> parsed = [];
    private readonly Dictionary<(ListingSource, string), (string Text, DateOnly Seen, IReadOnlyList<OpeningTime> Times)> adTimes = [];
    private readonly Dictionary<(ListingSource, string), (string Text, string About)> abouts = [];
    private readonly Dictionary<string, (string Name, FfxivVenues.Fields? Fields, FfxivVenues.Shown Shown)> shownVenues = [];
    private readonly Dictionary<string, (CalendarFields Fields, RpEventCalendar.Shown Shown)> shownEvents = [];
    private readonly Dictionary<(ListingSource, string), (TaggedFields Fields, Tagging Tagging)> taggings = [];

    // Every entry listed before the filters, hidden ones too, and the keys of their listings
    private readonly List<Row> unfiltered = [];
    private readonly HashSet<(ListingSource, string)> unfilteredKeys = [];
    // The search box's text last parsed, and what it parsed to
    private string? searchParsed;
    private SearchQuery query = SearchQuery.Empty;

    private IReadOnlyList<DayGroup<Row>> groups = [];
    // Entries listed, each once however many of its openings are listed
    private int listingCount;
    // Each listed row by its listing, for the detail pane; a listing listed twice (open now and next) is one row
    private readonly Dictionary<(ListingSource, string), Row> rowsByKey = [];
    private DateTimeOffset nextRebuild;
    private bool loggedOut;
    private int emptyLine = -1;
    private bool aboutExpanded;

    // In the single-column layout, whether the selected listing's details show in place of the list
    private bool showingDetail;

    // Texts cut to a width, worked out once per text and width rather than every frame
    private readonly Dictionary<(string Text, int Width, int FontSize), string> fitted = [];
    private readonly Dictionary<(string Text, int Width), string> aboutCuts = [];

    // Worked out once each time the list is worked out (at most once a second) rather than every
    // frame, as formatting allocates: each day's heading, each row's status line on its first draw,
    // the count, and what the detail pane shows of the selected entry
    private readonly List<(string Main, string Dimmed)> headings = [];
    private readonly Dictionary<BrowserEntry<Row>, (string Text, bool Open)> statuses = new(ReferenceEqualityComparer.Instance);
    private string countText = "0 listings";
    private DetailView? detail;

    // Each row's icon and splash image slots, worked out on its first draw after the list is, as
    // finding their sources allocates; and each kept image's texture, found once per file
    private readonly Dictionary<Row, (ImageSlot Icon, ImageSlot Splash, string? Backdrop)> imageSlots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, KeptTexture> imageTextures = new(StringComparer.Ordinal);

    // The game art standing in for missing splash images, and each loading screen's texture once first drawn, by its path
    private readonly Backdrops backdrops;
    private readonly Dictionary<string, KeptTexture> backdropTextures = new(StringComparer.Ordinal);

    // The part of a loading screen shown, leaving out its dark top edge and the fade along its bottom
    private static readonly Vector2 BackdropMin = new(0, 0.05f);
    private static readonly Vector2 BackdropMax = new(1, 0.85f);

    /// <summary>A texture drawn when kept or shown as a placeholder, and the wrapper Dalamud last gave for drawing it.</summary>
    private sealed class KeptTexture(ISharedImmediateTexture texture)
    {
        public ISharedImmediateTexture Texture { get; } = texture;

        public IDalamudTextureWrap? Wrap { get; set; }
    }

    // The removable chip for each tag picked, as ListingFilters.Labels lists them
    private static readonly string[] TagChipLabels = [.. ListingFilters.Labels.Select(l => $"{l.Label} ×")];
    private static readonly string[] TagChipTips = [.. ListingFilters.Labels.Select(l => $"Stop requiring {l.Label}")];

    // The single-column layout's Filters button for each count of filters on, so drawing it doesn't allocate
    private static readonly string[] FilterButtonLabels =
        [.. Enumerable.Range(0, ListingFilters.MaxOnCount + 1).Select(n => (n == 0 ? "Filters" : $"Filters · {n}") + "##filtersMenu")];

    // Below this many font heights of width the list and details share one column; in font heights, so a raised font scale switches at a wider window
    private const float TwoColumnFontHeights = 45;

    /// <summary>What <see cref="Draw"/> allocates, for the self-test; see "Per-frame code" in CONTRIBUTING.md.</summary>
    internal AllocationWatch DrawAllocations { get; } = new();

    /// <summary>
    /// One entry as the browser shows it: its title, addresses and links, world, schedule, what is
    /// shown of a Partake or RP calendar event, and an ad's text or a description for About, with
    /// <see cref="AboutCut"/> when the description was cut short. A merged entry's
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
        RpEventCalendar.Shown? CalendarEvent = null,
        bool AboutCut = false)
    {
        public (ListingSource, string) Key => (Listing.Source, Listing.Id);

        /// <summary>The rows of the listings shown as this entry, title first; set when the list is rebuilt.</summary>
        public IReadOnlyList<Row> Members { get; init; } = [];

        /// <summary>The openings listed, earliest first; set when the list is rebuilt.</summary>
        public IReadOnlyList<Occurrence> Listed { get; init; } = [];

        /// <summary>The entry as its marks see it; set when the list is rebuilt.</summary>
        public MarkEntry Marks { get; init; } = new([], [], "", "", []);

        /// <summary>The place whose marks the entry carries, or null; set when the list is rebuilt and when a button marks one.</summary>
        public PlaceMark? Place { get; set; }

        /// <summary>Its members' interests and NSFW combined; set when the list is rebuilt.</summary>
        public Tagging Tagging { get; init; }

        /// <summary>What search looks in: its titles as shown, About and interest labels, <see cref="SearchQuery.Fold"/>ed; set when the list is rebuilt.</summary>
        public IReadOnlyList<string> SearchFields { get; init; } = [];
    }

    /// <summary>
    /// The fields <see cref="ListingTags.Of"/> reads. Recording a fetch assigns new objects, so a
    /// reference check finds every change; an ad's title comes from its text.
    /// </summary>
    private readonly record struct TaggedFields(string Title, string Description, FfxivVenues.Fields? Venue, string? Rating, IReadOnlyList<string> SourceTags)
    {
        public static TaggedFields Of(CachedListing listing) =>
            new(listing.Source.IsAd() ? "" : listing.Title, listing.Description,
                listing.Venue, listing.Rating, listing.SourceTags);

        public bool Same(TaggedFields other) =>
            ReferenceEquals(Title, other.Title) && ReferenceEquals(Description, other.Description) && ReferenceEquals(Venue, other.Venue)
            && ReferenceEquals(Rating, other.Rating) && ReferenceEquals(SourceTags, other.SourceTags);
    }

    /// <summary>
    /// A calendar event's fields that <see cref="RpEventCalendar.Show"/> reads. Recording a fetch assigns
    /// new objects, so a reference check finds every change.
    /// </summary>
    private readonly record struct CalendarFields(
        string Title, string? Location, string? ListedOn, string? Rating, IReadOnlyList<string> LinkTexts, ListingAbout? About)
    {
        public static CalendarFields Of(CachedListing listing) =>
            new(listing.Title, listing.Location, listing.ListedOn, listing.Rating, listing.LinkTexts, listing.About);

        public bool Same(CalendarFields other) =>
            ReferenceEquals(Title, other.Title) && ReferenceEquals(Location, other.Location) && ReferenceEquals(ListedOn, other.ListedOn)
            && ReferenceEquals(Rating, other.Rating) && ReferenceEquals(LinkTexts, other.LinkTexts);
    }

    /// <summary>
    /// What the detail pane shows of <see cref="Row"/> that takes formatting or a search to work out:
    /// the status line of its first opening, its first <see cref="MaxAddresses"/> addresses and places as Location
    /// shows them, whether its free-text location named a place, the Lifestream request for the first, which link Open page and the footer's Discord take (or -1),
    /// its sources, its age or rating with SFW or NSFW ("18+ · SFW"), its interests' labels,
    /// its lineup, what Copy link copies, and the Party Finder ad the footer's Party Finder opens, if it can.
    /// </summary>
    private sealed record DetailView(
        Row Row, string? Status, bool Open, AddressLine[] Addresses, bool LocationPlaced, TravelRequest? Travel, int Page, int Discord,
        ListingSource[] Sources, string Rating, string[] Tags, LineupEntry[] Lineup, CopyLink Copy, ulong? PartyFinderAd);

    /// <summary>
    /// An address or place outside housing as Location shows it: the text shown, the text Copy copies
    /// (what Lifestream reads), whether the world is the one the ad is listed on, and where Map opens the
    /// map, if anywhere: a housing address's plot or building, or a place's zone, flagged at its point when known.
    /// </summary>
    private sealed record AddressLine(string Shown, string Copied, bool ListedWorld, MapSpot? Spot, GamePlace? Place = null);

    /// <summary>The most addresses and places Location lists for one entry, addresses first.</summary>
    private const int MaxAddresses = 6;

    /// <summary>A performer as the lineup shows them: name or handle, and their Twitch channel if the ad links it.</summary>
    private readonly record struct LineupEntry(string Text, Uri? Twitch);

    /// <param name="gazetteer">Worlds, for the address parser and to correct venues' and events' worlds when shown.</param>
    /// <param name="places">The zones outside housing that ads and locations name, for Location.</param>
    /// <param name="partyFinder">Opens Party Finder ads in the game, from the footer's Party Finder.</param>
    /// <param name="fetchSources">Fetches each fetched source whose cooldown has passed; called on opening.</param>
    /// <param name="saveCache">Saves the listing cache, after a mark changes.</param>
    /// <param name="region">The worlds the logged-in character can travel to, or null while none is; only listings on them are shown.</param>
    /// <param name="openSettings">Opens the Settings window, from the title bar's cog.</param>
    /// <param name="titleFont">The larger font listing titles are drawn in; owned by the caller.</param>
    /// <param name="iconFont">Dalamud's icon font, for the marks; owned by the caller.</param>
    /// <param name="images">Fetches and keeps icons and splash images; owned by the caller.</param>
    /// <param name="textures">Loads the kept images and the district art for drawing.</param>
    /// <param name="backdrops">The loading screens standing in for missing splash images (<see cref="PlaceholderArt.Backdrops"/>).</param>
    public ListingsWindow(
        ListingCache cache, Gazetteer gazetteer, GamePlaces places, Lifestream lifestream, GamePartyFinder partyFinder, HousingMap housingMap, INotificationManager notifications, Configuration configuration, Action fetchSources,
        Action saveCache, Func<TravelRegion?> region, Action openSettings, HeldFont titleFont, HeldFont iconFont, ImageStore images, ITextureProvider textures,
        Backdrops backdrops)
        : base("Jaunt###JauntListings")
    {
        this.titleFont = titleFont;
        this.iconFont = iconFont;
        this.images = images;
        this.textures = textures;
        this.backdrops = backdrops;
        this.region = region;
        this.fetchSources = fetchSources;
        this.saveCache = saveCache;
        this.cache = cache;
        this.gazetteer = gazetteer;
        parser = new AddressParser(gazetteer);
        this.lifestream = lifestream;
        this.partyFinder = partyFinder;
        this.housingMap = housingMap;
        this.places = places;
        this.notifications = notifications;
        this.configuration = configuration;
        Size = new Vector2(900, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(320, 320), MaximumSize = new Vector2(2400, 1600) };
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
        showingDetail = false;
    }

    public override void OnClose()
    {
        titleFont.Release();
        iconFont.Release();
    }

    public override void Draw()
    {
        DrawAllocations.Start();
        try
        {
            DrawContents();
        }
        finally
        {
            DrawAllocations.Stop();
        }
    }

    private void DrawContents()
    {
        titleFont.BeginFrame();
        iconFont.BeginFrame();
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

        if (ImGui.GetContentRegionAvail().X < TwoColumnFontHeights * ImGui.GetFontSize())
        {
            DrawSingleColumn(now);
            return;
        }

        DrawFilters(now);

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

    /// <summary>
    /// The narrow window's layout: the list under the search box and a Filters button, or the selected
    /// listing's details under a Back to listings button once one is picked.
    /// </summary>
    private void DrawSingleColumn(DateTimeOffset now)
    {
        if (showingDetail && groups.Count > 0 && SelectedKey is { } key && rowsByKey.ContainsKey(key))
        {
            if (IconButton("##back", FontAwesomeIcon.ChevronLeft, "Back to listings"))
                showingDetail = false;
            using var detailChild = ImRaii.Child("##detail", Vector2.Zero, true);
            if (detailChild.Success)
                DrawDetail(now);
            return;
        }

        // With nothing to show, such as the listing no longer being listed, it goes back to the list for good
        showingDetail = false;
        DrawFilterButton(now);
        ImGui.TextDisabled(countText);
        using var list = ImRaii.Child("##list", Vector2.Zero, true);
        if (list.Success)
            DrawList(now);
    }

    /// <summary>The search box, then a "Filters · n" button at the end of the line, opening a menu of every filter.</summary>
    private void DrawFilterButton(DateTimeOffset now)
    {
        var filters = configuration.Filters;
        var style = ImGui.GetStyle();
        var label = FilterButtonLabels[ListingFilters.OnCount(filters)];
        var buttonWidth = ImGui.CalcTextSize(label, true).X + (style.FramePadding.X * 2);
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - buttonWidth - style.ItemSpacing.X));
        DrawSearch(now);
        ImGui.SameLine();
        if (ImGui.Button(label))
            ImGui.OpenPopup("##filters");

        var changed = false;
        using (var popup = ImRaii.Popup("##filters"))
        {
            if (popup.Success)
            {
                var on = filters.SfwOnly;
                if (FilterCheckbox("SFW only", ref on, "Leave out listings marked NSFW or adults only"))
                    (changed, filters.SfwOnly) = (true, on);
                on = filters.Favourites;
                if (FilterCheckbox("Favourites", ref on, "List only favourites"))
                    (changed, filters.Favourites) = (true, on);
                on = filters.HideVisited;
                if (FilterCheckbox("Hide visited", ref on, "Leave out places and events marked visited"))
                    (changed, filters.HideVisited) = (true, on);
                on = filters.ShowHidden;
                if (FilterCheckbox("Show hidden", ref on, "List hidden venues too"))
                    (changed, filters.ShowHidden) = (true, on);
                ImGui.Separator();
                ImGui.TextDisabled("Tags");
                changed |= DrawTagCheckboxes(filters);
            }
        }

        if (changed)
        {
            configuration.Save();
            ApplyFilters(now);
        }
    }

    private static bool FilterCheckbox(string label, ref bool on, string tip)
    {
        var clicked = ImGui.Checkbox(label, ref on);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tip);
        return clicked;
    }

    /// <summary>A checkbox per interest, ticked when the filters require it; returns whether one changed.</summary>
    private static bool DrawTagCheckboxes(BrowserFilters filters)
    {
        var changed = false;
        for (var i = 0; i < ListingFilters.Labels.Count; i++)
        {
            var (interest, label) = ListingFilters.Labels[i];
            var on = (filters.Tags & interest) != 0;
            if (ImGui.Checkbox(label, ref on))
                (changed, filters.Tags) = (true, filters.Tags ^ interest);
        }

        return changed;
    }

    /// <summary>The search box at the next item's width; a change applies at once and is saved once the box loses focus.</summary>
    private void DrawSearch(DateTimeOffset now)
    {
        var filters = configuration.Filters;
        var search = filters.Search;
        if (ImGui.InputTextWithHint("##search", "Search", ref search, 200))
        {
            filters.Search = search;
            ApplyFilters(now);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
            configuration.Save();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Every word must match a name, text or tag.\n-word leaves out listings with it.\n\"two words\" matches the phrase.");
    }

    /// <summary>
    /// The search box and filter chips, wrapping onto more lines when the window is narrow, with the
    /// count of entries listed at the right. A change applies at once and is saved; search text is
    /// saved once the box loses focus.
    /// </summary>
    private void DrawFilters(DateTimeOffset now)
    {
        var filters = configuration.Filters;
        var changed = false;

        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        DrawSearch(now);

        if (Chip("SFW only", filters.SfwOnly, "Leave out listings marked NSFW or adults only"))
            (changed, filters.SfwOnly) = (true, !filters.SfwOnly);
        if (Chip("Favourites", filters.Favourites, "List only favourites"))
            (changed, filters.Favourites) = (true, !filters.Favourites);
        if (Chip("Hide visited", filters.HideVisited, "Leave out places and events marked visited"))
            (changed, filters.HideVisited) = (true, !filters.HideVisited);
        if (Chip("Show hidden", filters.ShowHidden, "List hidden venues too"))
            (changed, filters.ShowHidden) = (true, !filters.ShowHidden);

        if (MenuChip("##tagsMenu", "Tags", "List only listings with every tag picked"))
            ImGui.OpenPopup("##tags");

        // Indexed, as enumerating through the list's interface allocates
        for (var i = 0; i < ListingFilters.Labels.Count; i++)
        {
            var interest = ListingFilters.Labels[i].Interest;
            if ((filters.Tags & interest) != 0 && Chip(TagChipLabels[i], true, TagChipTips[i]))
                (changed, filters.Tags) = (true, filters.Tags & ~interest);
        }

        // At the right of the last line, or alone on the next when it doesn't fit
        var count = countText;
        var countWidth = ImGui.CalcTextSize(count).X;
        FlowNext(countWidth);
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetContentRegionMax().X - countWidth));
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(count);

        // Last, so its items don't count as the line's last item for the chips' wrapping
        using (var popup = ImRaii.Popup("##tags"))
        {
            if (popup.Success)
                changed |= DrawTagCheckboxes(filters);
        }

        if (changed)
        {
            configuration.Save();
            ApplyFilters(now);
        }
    }

    /// <summary>A rounded toggle on the filter line, filled while on; returns whether it was clicked.</summary>
    private static bool Chip(string label, bool on, string tip)
    {
        var style = ImGui.GetStyle();
        FlowNext(ImGui.CalcTextSize(label).X + (style.FramePadding.X * 2));
        bool clicked;
        using (Scoped.Style(ImGuiStyleVar.FrameRounding, ImGui.GetFrameHeight() / 2))
        using (Scoped.Style(ImGuiStyleVar.FrameBorderSize, Math.Max(1, ImGuiHelpers.GlobalScale)))
        using (Scoped.Color(ImGuiCol.Button, on ? style.Colors[(int)ImGuiCol.Header] : Vector4.Zero))
        using (Scoped.Color(ImGuiCol.Text, style.Colors[on ? (int)ImGuiCol.Text : (int)ImGuiCol.TextDisabled]))
            clicked = ImGui.Button(label);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tip);
        return clicked;
    }

    /// <summary>An off <see cref="Chip"/> that opens a menu, with a downward caret after its label to say so.</summary>
    /// <param name="id">The button's ID, hidden ("##tagsMenu").</param>
    private bool MenuChip(string id, string label, string tip)
    {
        var style = ImGui.GetStyle();
        var labelWidth = ImGui.CalcTextSize(label).X;
        var caretSize = ImGui.GetFontSize() * 0.6f;
        var width = labelWidth + style.ItemInnerSpacing.X + caretSize + (style.FramePadding.X * 2);
        FlowNext(width);
        bool clicked;
        using (Scoped.Style(ImGuiStyleVar.FrameRounding, ImGui.GetFrameHeight() / 2))
        using (Scoped.Style(ImGuiStyleVar.FrameBorderSize, Math.Max(1, ImGuiHelpers.GlobalScale)))
        using (Scoped.Color(ImGuiCol.Button, Vector4.Zero))
            clicked = ImGui.Button(id, new Vector2(width, ImGui.GetFrameHeight()));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tip);
        var min = ImGui.GetItemRectMin();
        var drawList = ImGui.GetWindowDrawList();
        var colour = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        drawList.AddText(min + style.FramePadding, colour, label);
        var caretX = min.X + style.FramePadding.X + labelWidth + style.ItemInnerSpacing.X + (caretSize / 2);
        DrawIcon(drawList, FontAwesomeIcon.CaretDown, new Vector2(caretX, (min.Y + ImGui.GetItemRectMax().Y) / 2), caretSize, colour);
        return clicked;
    }

    /// <summary>Keeps the next item on the line when <paramref name="width"/> more fits, else lets it start a new line.</summary>
    private static void FlowNext(float width)
    {
        var lineEnd = ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X;
        if (lineEnd + ImGui.GetStyle().ItemSpacing.X + width <= ImGui.GetContentRegionMax().X)
            ImGui.SameLine();
    }

    private void DrawList(DateTimeOffset now)
    {
        if (groups.Count == 0)
        {
            if (unfiltered.Count == 0)
            {
                ImGui.TextWrapped(EmptyLines[emptyLine]);
            }
            else
            {
                ImGui.TextWrapped("No listings match these filters.");
                ImGui.TextDisabled("Try removing some, or clearing the search.");
            }

            return;
        }

        var locale = CultureInfo.CurrentCulture;
        var style = ImGui.GetStyle();
        float titleHeight;
        using (titleFont.Push())
            titleHeight = ImGui.GetTextLineHeight();
        var rowHeight = (ImGui.GetTextLineHeight() * 2) + titleHeight + (style.ItemSpacing.Y * 2);
        var selected = SelectedKey;
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var (main, dimmed) = headings[g];
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

    /// <summary>The icon, then the status line, the title cut to fit and the world.</summary>
    /// <remarks>The status line is worked out on the row's first draw after the list is, so it may lag the clock by up to <see cref="RebuildInterval"/>.</remarks>
    private void DrawRow(BrowserEntry<Row> entry, (ListingSource, string)? selected, float rowHeight, DateTimeOffset now, CultureInfo locale)
    {
        var row = entry.Item;
        var start = ImGui.GetCursorScreenPos();
        using var id = Scoped.Id($"{(int)row.Listing.Source}:{row.Listing.Id}:{entry.When.Start?.UtcTicks ?? 0}");
        var isSelected = selected is { } key && rowsByKey.TryGetValue(key, out var selectedRow) && ReferenceEquals(selectedRow, row);
        if (ImGui.Selectable("##row", isSelected, ImGuiSelectableFlags.None, new Vector2(0, rowHeight)))
        {
            Select(row);
            showingDetail = true;
        }

        // The highlighted area, which reaches half the item spacing past the row on each side
        var highlight = (Min: ImGui.GetItemRectMin(), Max: ImGui.GetItemRectMax());
        var after = ImGui.GetCursorScreenPos();

        // A rounded square level with the status line; a pictogram of its leading interest until the icon is fetched, or if there is none
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var iconSize = Math.Min(rowHeight, 32 * ImGuiHelpers.GlobalScale);
        if (!DrawImage(SlotsOf(row).Icon, start, start + new Vector2(iconSize), 6 * ImGuiHelpers.GlobalScale))
        {
            var icon = PlaceholderArt.Icon(Placeholders.IconInterest(row.Tagging.Interests));
            DrawIcon(ImGui.GetWindowDrawList(), icon, start + new Vector2(iconSize / 2), iconSize * 0.55f, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        }

        var textLeft = start.X + iconSize + spacing;
        var width = ImGui.GetContentRegionMax().X + ImGui.GetWindowPos().X - textLeft;

        // Favourite and visited show as icons in the highlight's top right corner, and the status line is cut to leave room
        var marksLeft = DrawRowMarks(row, highlight.Min, highlight.Max);
        var statusWidth = Math.Min(width, marksLeft - spacing - textLeft);
        ImGui.SetCursorScreenPos(new Vector2(textLeft, start.Y));
        if (!statuses.TryGetValue(entry, out var status))
        {
            AllocationWatch.Filled();
            status = (StatusText.Row(entry.When, entry.AlwaysOpen, now, TimeZoneInfo.Local, configuration.TimeFormat, configuration.DateFormat, locale),
                entry.AlwaysOpen || entry.When.IsOpen(now));
            statuses[entry] = status;
        }

        if (status.Open)
            ImGui.TextColored(ImGuiColors.HealerGreen, Fit(status.Text, statusWidth));
        else
            ImGui.TextUnformatted(Fit(status.Text, statusWidth));
        ImGui.SetCursorScreenPos(new Vector2(textLeft, ImGui.GetCursorScreenPos().Y));
        using (titleFont.Push())
            ImGui.TextUnformatted(Fit(ShownText.Title(row.Title), width));
        ImGui.SetCursorScreenPos(new Vector2(textLeft, ImGui.GetCursorScreenPos().Y));
        ImGui.TextDisabled(Fit(row.World ?? "", width));

        ImGui.SetCursorScreenPos(after);
    }

    /// <summary>
    /// The favourite star and visited tick, inset from the top and right of the row's highlight by the
    /// same gap; returns their left edge, or the highlight's right edge for none.
    /// </summary>
    private float DrawRowMarks(Row row, Vector2 min, Vector2 max)
    {
        var favourite = EntryMarks.Has(row.Marks, row.Place, EntryMark.Favourite);
        var visited = EntryMarks.Has(row.Marks, row.Place, EntryMark.Visited);
        if (!favourite && !visited)
            return max.X;

        var size = ImGui.GetFontSize();
        var gap = ImGui.GetStyle().ItemSpacing.Y;
        var drawList = ImGui.GetWindowDrawList();
        var right = max.X - gap;
        if (visited)
        {
            DrawIcon(drawList, FontAwesomeIcon.Check, new Vector2(right - (size / 2), min.Y + gap + (size / 2)), size, ImGui.GetColorU32(ImGuiColors.HealerGreen));
            right -= size + gap;
        }

        if (favourite)
        {
            DrawIcon(drawList, FontAwesomeIcon.Star, new Vector2(right - (size / 2), min.Y + gap + (size / 2)), size, ImGui.GetColorU32(ImGuiColors.DalamudYellow));
            right -= size + gap;
        }

        return right + gap;
    }

    /// <summary>
    /// The entry's icon and splash image slots, and the game art standing in for its splash image, worked out on
    /// its first draw after the list is.
    /// </summary>
    private (ImageSlot Icon, ImageSlot Splash, string? Backdrop) SlotsOf(Row row)
    {
        if (!imageSlots.TryGetValue(row, out var slots))
        {
            AllocationWatch.Filled();
            var listings = row.Members.Select(m => m.Listing).ToList();
            var links = row.Details.Links.Select(l => l.Uri).ToList();
            var locations = row.Members.SelectMany(m => new[] { m.CalendarEvent?.Location, m.Event?.Location });
            slots = (images.Slot(ImageKind.Icon, ImageSources.For(ImageKind.Icon, listings, links)),
                images.Slot(ImageKind.Splash, ImageSources.For(ImageKind.Splash, listings, links)),
                backdrops.For(row.Details.Addresses, locations));
            imageSlots[row] = slots;
        }

        return slots;
    }

    /// <summary>
    /// A plain rounded box, with the slot's image over it once kept, cropped to the box's shape about its
    /// middle. Asks for the image, as it is in view.
    /// </summary>
    /// <returns>Whether the image was drawn; false while it is fetched or loaded, or if there is none.</returns>
    private bool DrawImage(ImageSlot slot, Vector2 min, Vector2 max, float rounding)
    {
        // Starting a fetch allocates its queue entry
        if (images.Want(slot))
            AllocationWatch.Filled();
        ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), rounding);
        if (slot.Path is not { } path)
            return false;

        if (!imageTextures.TryGetValue(path, out var kept))
        {
            AllocationWatch.Filled();
            kept = new KeptTexture(textures.GetFromFileAbsolute(path));
            imageTextures[path] = kept;
        }

        return DrawCover(kept, min, max, Vector2.Zero, Vector2.One, Vector4.One, rounding);
    }

    /// <summary>
    /// A loading screen, dimmed so it reads as a backdrop rather than the entry's own image, over the box
    /// <see cref="DrawImage"/> left. Nothing for no path.
    /// </summary>
    private void DrawBackdrop(string? path, Vector2 min, Vector2 max, float rounding)
    {
        if (path is null)
            return;
        if (!backdropTextures.TryGetValue(path, out var kept))
        {
            AllocationWatch.Filled();
            kept = new KeptTexture(textures.GetFromGame(path));
            backdropTextures[path] = kept;
        }

        DrawCover(kept, min, max, BackdropMin, BackdropMax, new Vector4(0.7f, 0.7f, 0.7f, 1), rounding);
    }

    /// <summary>
    /// The texture's region from <paramref name="regionMin"/> to <paramref name="regionMax"/> (in texture
    /// coordinates) covering the box: the region's middle part with the box's shape fills it.
    /// </summary>
    /// <returns>Whether it was drawn; false while Dalamud loads it, or if loading failed.</returns>
    private static bool DrawCover(KeptTexture kept, Vector2 min, Vector2 max, Vector2 regionMin, Vector2 regionMax, Vector4 tint, float rounding)
    {
        if (!kept.Texture.TryGetWrap(out var wrap, out var failed) || wrap.Width <= 0 || wrap.Height <= 0)
        {
            // Dalamud loads the file over the next frames, which may allocate; a failed load stays failed
            if (failed is null)
                AllocationWatch.Filled();
            return false;
        }

        // Dalamud makes a new wrapper the first frame a texture is ready, and again once it reloads after a while out of view
        if (!ReferenceEquals(wrap, kept.Wrap))
        {
            AllocationWatch.Filled();
            kept.Wrap = wrap;
        }

        var box = max - min;
        var region = regionMax - regionMin;
        var image = wrap.Size * region;
        var scale = Math.Max(box.X / image.X, box.Y / image.Y);
        var shown = box / scale / image * region;
        var uvMin = regionMin + ((region - shown) / 2);
        ImGui.GetWindowDrawList().AddImageRounded(wrap.Handle, min, max, uvMin, uvMin + shown, ImGui.GetColorU32(tint), rounding);
        return true;
    }

    /// <summary>
    /// An icon centred on <paramref name="centre"/> by its drawn shape rather than its advance, which
    /// for FontAwesome glyphs is wider than the shape and leaves it off centre.
    /// </summary>
    /// <param name="size">The size of the box it fits in; the icon's larger side is drawn this long.</param>
    private void DrawIcon(ImDrawListPtr drawList, FontAwesomeIcon icon, Vector2 centre, float size, uint colour)
    {
        // Held rather than UiBuilder.IconFont, which locks the font anew, allocating, on every use
        if (iconFont.Font is not { } font)
            return;
        if (!IconShapes.TryGetValue((icon, font.FontSize), out var bounds))
        {
            // Found once per icon and font size, as the icon font holds over a thousand glyphs
            AllocationWatch.Filled();
            bounds = null;
            var glyphs = font.Glyphs;
            for (var i = 0; i < glyphs.Size; i++)
            {
                var glyph = glyphs[i];
                if (glyph.Codepoint == (uint)icon)
                {
                    bounds = (new Vector2(glyph.X0, glyph.Y0), new Vector2(glyph.X1, glyph.Y1), icon.ToIconString());
                    break;
                }
            }

            IconShapes[(icon, font.FontSize)] = bounds;
        }

        if (bounds is not var (min, max, text))
            return;
        var shape = max - min;
        var scale = size / Math.Max(shape.X, shape.Y);
        drawList.AddText(font, font.FontSize * scale, centre - ((min + max) * (scale / 2)), colour, text);
    }

    /// <summary>
    /// A button with an icon before its label, both drawn over it, as Dalamud's
    /// <c>ImGuiComponents.IconButtonWithText</c> allocates on every call. Dimmed with the button when disabled.
    /// </summary>
    /// <param name="id">The button's ID, hidden ("##travel").</param>
    private bool IconButton(string id, FontAwesomeIcon icon, string label)
    {
        var style = ImGui.GetStyle();
        var iconSize = ImGui.GetFontSize();
        var labelX = style.FramePadding.X + iconSize + style.ItemInnerSpacing.X;
        var clicked = ImGui.Button(id, new Vector2(labelX + ImGui.CalcTextSize(label).X + style.FramePadding.X, ImGui.GetFrameHeight()));
        var min = ImGui.GetItemRectMin();
        var drawList = ImGui.GetWindowDrawList();
        var colour = ImGui.GetColorU32(ImGuiCol.Text);
        DrawIcon(drawList, icon, new Vector2(min.X + style.FramePadding.X + (iconSize / 2), (min.Y + ImGui.GetItemRectMax().Y) / 2), iconSize * 0.85f, colour);
        drawList.AddText(new Vector2(min.X + labelX, min.Y + style.FramePadding.Y), colour, label);
        return clicked;
    }

    /// <summary>A square button the height of a line of text with only an icon, drawn over it like <see cref="IconButton"/>.</summary>
    /// <param name="id">The button's ID, hidden ("##copy").</param>
    private bool SmallIconButton(string id, FontAwesomeIcon icon)
    {
        var size = ImGui.GetFrameHeight();
        var clicked = ImGui.Button(id, new Vector2(size));
        var min = ImGui.GetItemRectMin();
        DrawIcon(ImGui.GetWindowDrawList(), icon, min + new Vector2(size / 2), ImGui.GetFontSize() * 0.85f, ImGui.GetColorU32(ImGuiCol.Text));
        return clicked;
    }

    private void DrawDetail(DateTimeOffset now)
    {
        // With nothing listed, or nothing the filters let through, the list's own line says so and the help text would only add noise
        if (groups.Count == 0)
            return;

        if (SelectedKey is not { } key || !rowsByKey.TryGetValue(key, out var row))
        {
            ImGui.TextDisabled("Select a listing on the left to see its details.");
            return;
        }

        if (!ReferenceEquals(detail?.Row, row))
            detail = DetailOf(row, now, CultureInfo.CurrentCulture);
        var view = detail!;
        var width = ImGui.GetContentRegionAvail().X;

        // Its district's or zone's art until the splash image is fetched, or if there is none; a plain band without either
        var top = ImGui.GetCursorScreenPos();
        var splashHeight = Math.Min(width * 0.3f, 140 * ImGuiHelpers.GlobalScale);
        var splashMax = top + new Vector2(width, splashHeight);
        var slots = SlotsOf(row);
        if (!DrawImage(slots.Splash, top, splashMax, 4 * ImGuiHelpers.GlobalScale))
            DrawBackdrop(slots.Backdrop, top, splashMax, 4 * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(width, splashHeight));

        // The mark buttons sit at the right of the title, which wraps short of them
        var titleTop = ImGui.GetCursorPos();
        var buttonsWidth = DrawMarkButtons(row);
        ImGui.SetCursorPos(titleTop);
        using (titleFont.Push())
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionMax().X - buttonsWidth - ImGui.GetStyle().ItemSpacing.X);
            ImGui.TextUnformatted(ShownText.Title(row.Title));
            ImGui.PopTextWrapPos();
        }

        ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY(), titleTop.Y + ImGui.GetFrameHeightWithSpacing()));
        if (view.Status is { } status)
        {
            if (view.Open)
                ImGui.TextColored(ImGuiColors.HealerGreen, status);
            else
                ImGui.TextUnformatted(status);
        }

        if (row.World is { } world)
            ImGui.TextDisabled(world);

        ImGui.Spacing();
        DrawActions(view);

        DrawLocation(view);
        DrawAbout(view, width);
        DrawLineup(view);
        DrawRatingAndTags(view);
        DrawFooter(view);
    }

    /// <summary>Works out <see cref="DetailView"/> for the entry; the status line is of the opening listed first.</summary>
    private DetailView DetailOf(Row row, DateTimeOffset now, CultureInfo locale)
    {
        AllocationWatch.Filled();
        string? status = null;
        var open = false;
        if (row.Listed.Count > 0)
        {
            var first = row.Listed[0];
            var always = row.Schedule.AlwaysOpen;
            status = StatusText.Detail(first, always, now, TimeZoneInfo.Local, configuration.TimeFormat, configuration.DateFormat, locale);
            open = always || first.IsOpen(now);
        }

        var addresses = row.Details.Addresses;
        var links = row.Details.Links;
        var page = PageIndex(links);
        var discord = DiscordIndex(links);
        LineupEntry[] lineup = [.. row.Details.Performers.Select(p => new LineupEntry(ShownText.Fold(p.Shown), p.Twitch?.Uri))];
        var (found, locationPlaced) = PlacesOf(row);
        var members = row.Members.Count > 0 ? row.Members : [row];
        var ad = PartyFinderAds.Openable(
            members.Select(m => m.Listing), now, configuration.RefreshCooldownMinutes, gazetteer, partyFinder.CurrentDataCentre);
        var sources = members.Select(m => m.Listing.Source).Distinct().ToList();
        // An ad xivpf collected is in the game's Party Finder too, which the footer opens from there while it can
        if (ad is not null && !sources.Contains(ListingSource.InGamePartyFinder))
            sources.Insert(Math.Max(0, sources.IndexOf(ListingSource.Xivpf)), ListingSource.InGamePartyFinder);
        return new DetailView(
            row, status, open,
            [
                .. addresses.Take(MaxAddresses).Select(a => new AddressLine(AddressText.Display(a), AddressText.Format(a), a.WorldFrom == WorldSource.Listing, housingMap.SpotOf(a))),
                .. found.Take(MaxAddresses - Math.Min(addresses.Count, MaxAddresses)).Select(p => new AddressLine(p.Display(), p.Copied(), p.ListedWorld, null, p)),
            ],
            locationPlaced,
            addresses.Count > 0 ? TravelRequest.From(addresses[0]) : null,
            page, discord, [.. sources], RatingOf(row),
            [.. ListingFilters.LabelsOf(row.Tagging.Interests)], lineup,
            CopyLinks.Choose(links, [.. row.Members.Select(m => (m.Listing.Source, m.Listing.Id))]), ad);
    }

    /// <summary>
    /// The places outside housing the entry's members name, each once, in member order: in a venue's or
    /// event's location, or in an ad's text; and whether the location shown as text (with no address)
    /// named any, as the places then stand in for it.
    /// </summary>
    private (List<GamePlace> Found, bool LocationPlaced) PlacesOf(Row row)
    {
        var found = new List<GamePlace>();
        var locationPlaced = false;
        var freeText = FreeLocation(row);
        foreach (var member in row.Members.Count > 0 ? row.Members : [row])
        {
            IReadOnlyList<GamePlace> named;
            if (member.CalendarEvent is { } calendarEvent)
                named = places.InLocation(calendarEvent.Location, member.World);
            else if (member.Event is { } item)
                named = places.InLocation(item.Location, item.World);
            else if (member.Listing.Source.IsAd())
                named = places.InAd(PartyFinderText.Normalise(member.Listing.Description).Text, member.Listing.ListedOn);
            else
                continue;

            if (named.Count > 0 && freeText is not null && ReferenceEquals(freeText, member.CalendarEvent?.Location ?? member.Event?.Location))
                locationPlaced = true;
            foreach (var place in named.Where(p => !found.Contains(p)))
                found.Add(place);
        }

        return (found, locationPlaced);
    }

    /// <summary>A location the parser couldn't read as an address, which Location shows as text; null when there are addresses.</summary>
    private static string? FreeLocation(Row row) =>
        row.Details.Addresses.Count == 0
            ? row.CalendarEvent?.Location ?? (row.Event is { Location.Length: > 0 } item ? item.Location : null)
            : null;

    /// <summary>
    /// Favourite, Visited and Hide as square icon buttons at the top right, each lit while the entry
    /// shows its mark; returns their width. Hide takes the entry out of the list at once.
    /// </summary>
    private float DrawMarkButtons(Row row)
    {
        var size = new Vector2(ImGui.GetFrameHeight());
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = (size.X * 3) + (spacing * 2);
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - width);
        var changed = MarkButton(row, EntryMark.Favourite, FontAwesomeIcon.Star, ImGuiColors.DalamudYellow, size, "Remove from favourites");
        ImGui.SameLine();
        changed |= MarkButton(row, EntryMark.Visited, FontAwesomeIcon.Check, ImGuiColors.HealerGreen, size, "Mark not visited");
        ImGui.SameLine();
        if (MarkButton(row, EntryMark.Hidden, FontAwesomeIcon.EyeSlash, ImGuiColors.DalamudGrey, size, "Un-hide"))
        {
            changed = true;
            Notify($"Hid {ShownText.Title(row.Title)}. Un-hide it in Settings under Hidden venues.", NotificationType.Info, $"Hid {ShownText.Title(row.Title)}");
        }

        if (changed)
        {
            // Other entries at the same place show the change too, and a hidden one leaves the list
            nextRebuild = DateTimeOffset.MinValue;
            saveCache();
        }

        return width;
    }

    /// <summary>One mark's button: its icon in <paramref name="on"/> while the entry shows the mark; returns whether it was clicked.</summary>
    /// <param name="clearTip">The tooltip while marked; while unmarked it says what the mark goes on (<see cref="SetTip"/>).</param>
    private bool MarkButton(Row row, EntryMark mark, FontAwesomeIcon icon, Vector4 on, Vector2 size, string clearTip)
    {
        var marked = EntryMarks.Has(row.Marks, row.Place, mark);
        bool clicked;
        using (Scoped.Id((int)mark))
            clicked = ImGui.Button("##mark", size);
        // Drawn over the button, centred by shape and a little over half its height
        var centre = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        DrawIcon(ImGui.GetWindowDrawList(), icon, centre, size.Y * 0.55f, marked ? ImGui.GetColorU32(on) : ImGui.GetColorU32(ImGuiCol.Text));
        if (ImGui.IsItemHovered())
        {
            if (marked)
                ImGui.SetTooltip(clearTip);
            else
                SetTip(row, mark);
        }
        if (!clicked)
            return false;

        var place = row.Place;
        EntryMarks.Toggle(cache, row.Marks, ref place, mark);
        row.Place = place;
        return true;
    }

    /// <summary>
    /// What a button will mark, as its tooltip, so it is clear which later listings will carry it:
    /// everything at an address names the address. Formatted straight into the tooltip, as building a
    /// string would allocate on every frame the button is hovered.
    /// </summary>
    private static void SetTip(Row row, EntryMark mark)
    {
        var (verb, after) = mark switch
        {
            EntryMark.Favourite => ("Favourite", ""),
            EntryMark.Visited => ("Mark", " visited"),
            _ => ("Hide", ""),
        };
        switch (EntryMarks.Target(row.Marks, mark))
        {
            case MarkTarget.Place:
                ImGui.SetTooltip($"{verb} {row.Marks.ShownAddress}{after}: every listing there, from any source, now or later");
                break;
            case MarkTarget.Event:
                ImGui.SetTooltip($"{verb} this event{after}");
                break;
            default:
                if (row.Marks.Members.Count == 1)
                    ImGui.SetTooltip($"{verb} this ad{after}");
                else
                    ImGui.SetTooltip($"{verb} these ads{after}");
                break;
        }
    }

    /// <summary>Travel to the first address, and open the first link that isn't a Discord invite.</summary>
    private void DrawActions(DetailView view)
    {
        var row = view.Row;
        var address = row.Details.Addresses.Count > 0 ? row.Details.Addresses[0] : null;
        if (address is not null)
        {
            var request = view.Travel;
            var whyNot = !lifestream.Available ? "Install and enable Lifestream to travel from Jaunt."
                : request is null ? "This address is missing a world, district, plot or apartment, so Lifestream can't travel there."
                : null;

            using (Scoped.Disabled(whyNot is not null))
            {
                if (IconButton("##travel", FontAwesomeIcon.Compass, "Travel") && request is not null && lifestream.Travel(request) is { } error)
                    Notify(error, NotificationType.Warning, "Couldn't travel");
            }

            if (whyNot is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(whyNot);
        }

        if (view.Page is var page and >= 0)
        {
            if (address is not null)
                ImGui.SameLine();
            var link = row.Details.Links[page];
            if (IconButton("##openPage", FontAwesomeIcon.ExternalLinkAlt, "Open page"))
                Util.OpenLink(LinkAddress(link.Uri));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(LinkAddress(link.Uri));
        }

        // Copy link: the entry's own website, else an event or venue page, its address in the tooltip
        if (view.Copy.Uri is { } uri)
        {
            if (address is not null || view.Page >= 0)
                ImGui.SameLine();
            if (IconButton("##copyLink", FontAwesomeIcon.Clone, "Copy link"))
            {
                ImGui.SetClipboardText(LinkAddress(uri));
                Notify($"Copied {LinkAddress(uri)}", NotificationType.Success, "Copied the link");
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Copy {LinkAddress(uri)}");
        }
    }

    /// <summary>
    /// Each address and place outside housing with Copy and Map, and a location the parser couldn't read as
    /// an address or a place as text. Never links: Location is only for where in the game the listing is.
    /// </summary>
    private void DrawLocation(DetailView view)
    {
        var row = view.Row;
        var freeText = view.LocationPlaced ? null : FreeLocation(row);
        if (view.Addresses.Length == 0 && freeText is null)
            return;

        Section("Location");
        for (var i = 0; i < view.Addresses.Length; i++)
        {
            using var id = Scoped.Id($"a{i}");
            var line = view.Addresses[i];
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(line.Shown);
            if (line.ListedWorld)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(world the ad is listed on)");
            }

            ImGui.SameLine();
            if (SmallIconButton("##copy", FontAwesomeIcon.Clone))
            {
                ImGui.SetClipboardText(line.Copied);
                Notify($"Copied {line.Copied}", NotificationType.Success, "Copied the address");
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Copy {line.Copied}");

            // A housing address needs one plot or apartment building to flag; a place always has its zone's map
            if (line.Spot is not null || line.Place is not null)
            {
                ImGui.SameLine();
                if (SmallIconButton("##map", FontAwesomeIcon.MapMarkerAlt) && !ShowOnMap(line))
                    Notify("The game couldn't open the map there.", NotificationType.Warning, "Couldn't open the map");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(line.Place is { Point: null } ? "Show the zone's map" : "Show on the map");
            }
        }

        if (freeText is not null)
            ImGui.TextWrapped(ShownText.Fold(freeText));
    }

    private bool ShowOnMap(AddressLine line) =>
        line.Spot is { } spot ? housingMap.Show(spot) : line.Place is { } place && housingMap.Show(place.Zone, place.Point);

    /// <summary>
    /// An ad's text or a description, cut to <see cref="AboutLines"/> lines with "Read more" when longer.
    /// Once a description cut short is shown in full, a link to the page follows, when there is one.
    /// </summary>
    private void DrawAbout(DetailView view, float width)
    {
        var row = view.Row;
        if (row.About is not { Length: > 0 } about)
            return;

        Section("About");
        var cut = aboutExpanded ? about : CutToLines(about, width);
        ImGui.TextWrapped(cut);
        var whole = ReferenceEquals(cut, about);
        if (whole && row.AboutCut && view.Page is var page and >= 0)
            DrawLinkText("Read the rest on the website", row.Details.Links[page].Uri);
        if (!whole || aboutExpanded)
        {
            if (ImGui.SmallButton(aboutExpanded ? "Show less" : "Read more"))
                aboutExpanded = !aboutExpanded;
        }
    }

    /// <summary>
    /// The performers the entry's ads name, separated by dots and wrapping as needed, each linking to
    /// their Twitch channel when an ad gives it. Nothing when no ad names any.
    /// </summary>
    private static void DrawLineup(DetailView view)
    {
        var lineup = view.Lineup;
        if (lineup.Length == 0)
            return;

        Section("Lineup");
        for (var i = 0; i < lineup.Length; i++)
        {
            var entry = lineup[i];
            if (i > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("·");
                FlowNext(ImGui.CalcTextSize(entry.Text).X);
            }

            if (entry.Twitch is { } twitch)
                DrawLinkText(entry.Text, twitch);
            else
                ImGui.TextUnformatted(entry.Text);
        }
    }

    /// <summary>
    /// Each age or rating the entry's members give, once each, then SFW or NSFW as the safety filter
    /// counts the entry ("18+ · SFW", "Mature · NSFW").
    /// </summary>
    private static string RatingOf(Row row)
    {
        var ages = row.Members.Select(m => ListingTags.AgeOf(m.Listing)).OfType<string>().Distinct();
        return string.Join(" · ", ages.Append(row.Tagging.Nsfw ? "NSFW" : "SFW"));
    }

    /// <summary>The age or rating with SFW or NSFW, then the entry's interests as chips, when it has any.</summary>
    private static void DrawRatingAndTags(DetailView view)
    {
        Section("Age / rating");
        ImGui.TextUnformatted(view.Rating);
        if (view.Tags.Length == 0)
            return;

        Section("Tags");
        for (var i = 0; i < view.Tags.Length; i++)
            TagChip(view.Tags[i], i == 0);
    }

    /// <summary>A rounded, outlined label like an unlit filter chip, but not clickable; wraps to a new line when it doesn't fit.</summary>
    private static void TagChip(string label, bool first)
    {
        var padding = ImGui.GetStyle().FramePadding;
        var size = ImGui.CalcTextSize(label) + (padding * 2);
        if (!first)
            FlowNext(size.X);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(min, min + size, ImGui.GetColorU32(ImGuiCol.Border), size.Y / 2, ImDrawFlags.None, Math.Max(1, ImGuiHelpers.GlobalScale));
        drawList.AddText(min + padding, ImGui.GetColorU32(ImGuiCol.Text), label);
    }

    /// <summary>
    /// The Discord invite, then where the listing came from, with the credits its source needs.
    /// "Listed by" (the ad's poster) goes first once d12eb2c4 keeps it.
    /// </summary>
    private void DrawFooter(DetailView view)
    {
        var row = view.Row;
        ImGui.Spacing();
        ImGui.Separator();
        if (view.Discord is var discord and >= 0)
        {
            DrawLinkText("Discord", row.Details.Links[discord].Uri);
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
        }

        // Every source on one line, each linking to its site, with the calendar's licence after. The in-game
        // Party Finder has none, so it opens the ad there instead, while the game can
        var sources = view.Sources;
        ImGui.TextDisabled("Sources:");
        for (var i = 0; i < sources.Length; i++)
        {
            ImGui.SameLine(0, 0);
            ImGui.TextDisabled(i == 0 ? " " : ", ");
            ImGui.SameLine(0, 0);
            if (sources[i] == ListingSource.InGamePartyFinder && view.PartyFinderAd is { } ad)
            {
                if (LinkText(SourceName(sources[i])))
                    ImGui.SetTooltip("Open this ad in the game's Party Finder");
                if (ImGui.IsItemClicked() && !partyFinder.Open(ad))
                    Notify("The game couldn't open the Party Finder just now. Try again in a moment.", NotificationType.Warning, "Couldn't open the ad");
            }
            else if (SiteUrl(sources[i]) is { } site)
                DrawLinkText(SourceName(sources[i]), site);
            else
                ImGui.TextDisabled(SourceName(sources[i]));
        }

        if (Array.IndexOf(sources, ListingSource.RpEventCalendar) >= 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
            DrawLinkText(RpEventCalendar.LicenceName, RpEventCalendar.LicenceUrl);
        }
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

    /// <summary>
    /// Text that looks like a link (link colour, underlined), shows its address and a hand cursor on
    /// hover, and opens it when clicked.
    /// </summary>
    internal static void DrawLinkText(string text, Uri uri)
    {
        if (LinkText(text))
            ImGui.SetTooltip(LinkAddress(uri));
        if (ImGui.IsItemClicked())
            Util.OpenLink(LinkAddress(uri));
    }

    /// <summary>Text that looks like a link (link colour, underlined), with a hand cursor on hover; returns whether it is hovered.</summary>
    private static bool LinkText(string text)
    {
        ImGui.TextColored(ImGuiColors.TankBlue, text);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), max, ImGui.GetColorU32(ImGuiColors.TankBlue), Math.Max(1, ImGuiHelpers.GlobalScale));
        if (!ImGui.IsItemHovered())
            return false;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return true;
    }

    /// <summary>A link's address to show or open; the first read of each allocates, so counts as a fill.</summary>
    private static string LinkAddress(Uri uri)
    {
        var address = Core.Text.WebLinks.Address(uri, out var filled);
        if (filled)
            AllocationWatch.Filled();
        return address;
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
        AllocationWatch.Filled();
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

        AllocationWatch.Filled();
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
    /// Forgets the selection when its listing is no longer listed, whatever the filters.
    /// </summary>
    private void Rebuild(DateTimeOffset now)
    {
        imageSlots.Clear();
        unfiltered.Clear();
        unfilteredKeys.Clear();
        var travel = region();
        loggedOut = travel is null;
        if (travel is null)
        {
            rowsByKey.Clear();
            listingCount = 0;
            groups = [];
            return;
        }

        var adRegion = travel.DataCentres.Select(Schedules.RegionOf).FirstOrDefault(r => r is not null) ?? PartyFinderRegion.Europe;
        var rows = new List<(Row Row, AddressKey? Address)>();
        foreach (var listing in cache.Listings)
        {
            // A venue's world is checked once it is shown, as that corrects misspellings. Hidden
            // listings and places are merged too, so hiding a venue also hides the ads listed as it
            if (listing.Source != ListingSource.FfxivVenues && !travel.Includes(listing))
                continue;

            if (RowFor(listing, travel, adRegion, now) is { } row)
                rows.Add((row, row.Details.Addresses.Count > 0 ? AddressKey.Of(row.Details.Addresses[0]) : null));
        }

        var candidates = new List<MergeCandidate<Row>>(rows.Count);
        foreach (var (row, address) in WithAddressZones(rows, adRegion, now))
        {
            var listed = Schedules.Listed(row.Schedule, now);
            if (listed.Count > 0)
                candidates.Add(new MergeCandidate<Row>(row, address, row.Schedule, listed, row.Listing.Source.IsAd() ? row.Listing.Description : null));
        }

        var merged = Duplicates.Merge(candidates, now).Select(Entry).ToList();
        var places = PlaceMarks.Match(cache, [.. merged.Select(m => m.Marks)]);
        if (cache.MarksUnsaved)
            saveCache();
        for (var i = 0; i < merged.Count; i++)
        {
            var row = merged[i];
            row.Place = places[i];
            unfiltered.Add(row);
            foreach (var member in row.Members)
                unfilteredKeys.Add(member.Key);
        }

        if (unfiltered.Count == 0)
        {
            if (emptyLine < 0)
                emptyLine = Random.Shared.Next(EmptyLines.Length);
        }
        else
        {
            emptyLine = -1;
        }

        // A listing the filters leave out stays selected, so it shows again once they let it through
        if (SelectedKey is { } selected && !unfilteredKeys.Contains(selected))
            ClearSelection();

        ApplyFilters(now);
        Forget();
    }

    /// <summary>
    /// Lists the entries the search and filters let through, grouped by day, with their headings and
    /// count, and forgets the status lines and detail worked out for the last list; cheap enough to run
    /// on every keystroke.
    /// </summary>
    private void ApplyFilters(DateTimeOffset now)
    {
        var filters = configuration.Filters;
        if (!ReferenceEquals(searchParsed, filters.Search))
        {
            query = SearchQuery.Parse(filters.Search);
            searchParsed = filters.Search;
        }

        rowsByKey.Clear();
        listingCount = 0;
        var entries = new List<BrowserEntry<Row>>();
        foreach (var row in unfiltered)
        {
            var entry = new FilterEntry(
                row.Tagging, EntryMarks.Has(row.Marks, row.Place, EntryMark.Favourite), EntryMarks.Has(row.Marks, row.Place, EntryMark.Visited),
                EntryMarks.Has(row.Marks, row.Place, EntryMark.Hidden), row.SearchFields);
            if (!ListingFilters.Shows(filters, query, entry))
                continue;
            listingCount++;
            foreach (var member in row.Members)
                rowsByKey[member.Key] = row;
            var key = $"{(int)row.Listing.Source}:{row.Listing.Id}";
            foreach (var when in row.Listed)
                entries.Add(new BrowserEntry<Row>(row, row.Title, key, when, row.Schedule.AlwaysOpen));
        }

        groups = DayGroups.Group(entries, now, TimeZoneInfo.Local);
        AllocationWatch.Filled();
        var locale = CultureInfo.CurrentCulture;
        headings.Clear();
        foreach (var group in groups)
            headings.Add(DayGroups.Heading(group, configuration.DateFormat, locale));
        statuses.Clear();
        detail = null;
        countText = listingCount == 1 ? "1 listing" : $"{listingCount.ToString(locale)} listings";
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
                || !row.Listing.Source.IsAd())
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
            yield return (row with { Schedule = Schedules.ForAd(times, listing.LastSeen, adRegion, stillListed, now, unstated, listing.FirstSeen) }, address);
        }
    }

    /// <summary>
    /// The entry for listings shown as one: the title of the member <see cref="Duplicates.TitleRank"/>
    /// puts first, the addresses and world of the one <see cref="Duplicates.AddressRank"/> does among
    /// those naming a plot or apartment, every member's links once each, and the About and lineup
    /// (performers in that order) of the member <see cref="Duplicates.AboutRank"/> puts first, ties going
    /// to the one that stops being listed soonest.
    /// </summary>
    private Row Entry(MergedListing<Row> merged)
    {
        if (merged.Members.Count == 1)
        {
            var only = merged.Members[0];
            return Filterable(only with { Members = merged.Members, Listed = merged.Listed, Marks = MarksOf(only, merged.Members) });
        }

        var byTitle = merged.Members.OrderBy(m => Duplicates.TitleRank(m.Listing.Source)).ToList();
        var byAddress = merged.Members
            .OrderBy(m => m.Details.Addresses.Count == 0 || AddressKey.Of(m.Details.Addresses[0]) is not { WardOnly: false })
            .ThenBy(m => Duplicates.AddressRank(m.Listing.Source))
            .First();
        var links = Duplicates.Links(byTitle.Select(m => m.Details.Links));

        // A shout, then an ad or event, is about the opening listed, so its text wins over a venue's standing description
        var byAbout = byTitle.OrderBy(m => Duplicates.AboutRank(m.Listing.Source))
            .ThenBy(m => Schedules.ListedUntil(m.Listing.Source, m.Listing.LastSeen, configuration.RefreshCooldownMinutes))
            .ToList();
        var about = byAbout.Find(m => m.About is { Length: > 0 });
        var first = byTitle[0];
        var row = new Row(
            first.Listing, first.Title, byAddress.Details with { Links = links, Performers = Performers.Merge(byAbout.Select(m => m.Details.Performers)) },
            byAddress.World, merged.Schedule, about?.About,
            byTitle.Select(m => m.Event).FirstOrDefault(e => e is not null),
            byTitle.Select(m => m.CalendarEvent).FirstOrDefault(e => e is not null),
            about?.AboutCut ?? false)
        {
            Members = byTitle,
            Listed = merged.Listed,
        };
        return Filterable(row with { Marks = MarksOf(row, byTitle) });
    }

    /// <summary>The entry with what the filters read: its members' tagging and the texts search looks in.</summary>
    private Row Filterable(Row row)
    {
        var tagging = ListingTags.Combine(row.Members.Select(m => TaggingOf(m.Listing)));
        var fields = new List<string>(row.Members.Count + 2);
        foreach (var member in row.Members)
        {
            var title = SearchQuery.Fold(ShownText.Title(member.Title));
            if (!fields.Contains(title))
                fields.Add(title);
        }

        if (row.About is { Length: > 0 } about)
            fields.Add(SearchQuery.Fold(about));
        fields.AddRange(ListingFilters.LabelsOf(tagging.Interests));
        return row with { Tagging = tagging, SearchFields = fields };
    }

    /// <summary>A listing's interests and NSFW, worked out once per change to the fields they come from.</summary>
    private Tagging TaggingOf(CachedListing listing)
    {
        var key = (listing.Source, listing.Id);
        var fields = TaggedFields.Of(listing);
        if (!taggings.TryGetValue(key, out var entry) || !entry.Fields.Same(fields))
        {
            entry = (fields, ListingTags.Of(listing));
            taggings[key] = entry;
        }

        return entry.Tagging;
    }

    /// <summary>
    /// The entry as its marks see it: its listings, every plot or apartment its members give (the one
    /// it shows first), its title and first address as shown, and its links.
    /// </summary>
    private static MarkEntry MarksOf(Row row, IReadOnlyList<Row> members)
    {
        var places = new List<AddressKey>();
        foreach (var address in row.Details.Addresses.Take(1).Concat(members.SelectMany(m => m.Details.Addresses.Take(1))))
        {
            if (AddressKey.Of(address) is { WardOnly: false } key && !places.Contains(key))
                places.Add(key);
        }

        var shown = row.Details.Addresses.Count > 0 ? AddressText.Format(row.Details.Addresses[0]) : "";
        return new MarkEntry([.. members.Select(m => m.Listing)], places, ShownText.Title(row.Title), shown, [.. row.Details.Links.Select(l => l.Uri)]);
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
                        Schedules.ForVenue(listing.Venue, now), Described(listing), AboutCut: listing.About?.Cut ?? false);
                }

            case ListingSource.Partake:
                {
                    // Shown without an address too: the location is free text the parser may not read
                    var item = Partake.Show(listing, gazetteer);
                    var location = Parsed(listing, item.Location, item.World);
                    return new Row(listing, item.Title, location with { Links = WebLinks(item.Links) }, item.World,
                        Schedules.ForEvent(listing.StartsAt, listing.EndsAt), Described(listing), Event: item,
                        AboutCut: listing.About?.Cut ?? false);
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
                        Schedules.ForOpenings(listing.Openings, listing.OneOff), Described(listing), CalendarEvent: calendarEvent,
                        AboutCut: listing.About?.Cut ?? false);
                }

            case ListingSource.InGamePartyFinder or ListingSource.Xivpf or ListingSource.Shout:
                {
                    if (Xivpf.ShownInGame(cache, listing) || Xivpf.ShownFromXivpf(cache, listing))
                        return null;
                    var adDetails = Parsed(listing, listing.Description, listing.ListedOn);
                    if (adDetails.Addresses.Count == 0)
                        return null;
                    var stillListed = Schedules.StillListed(listing.Source, listing.LastSeen, now, configuration.RefreshCooldownMinutes);
                    var schedule = Schedules.ForAd(AdTimes(listing), listing.LastSeen, adRegion, stillListed, now, firstSeen: listing.FirstSeen);
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
        ForgetMissing(taggings);
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
            entry = (text, listedOn, listing.Source == ListingSource.Partake
                ? new ListingDetails(Partake.Addresses(text, listedOn ?? "", parser), [])
                : ListingDetails.Parse(parser, text, listedOn));
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

    /// <summary>A venue's or event's description for About, as the font can draw it; null when it has none.</summary>
    private static string? Described(CachedListing listing) => listing.About is { } about ? ShownText.Fold(about.Text) : null;

    private static IReadOnlyList<WebLink> WebLinks(IEnumerable<Uri> links) => [.. links.Select(uri => new WebLink(uri.OriginalString, uri))];

    /// <param name="minimized">
    /// The one line shown once the toast shrinks; without it Dalamud cuts <paramref name="text"/> to fit,
    /// so keep it short with the gist first.
    /// </param>
    private void Notify(string text, NotificationType type, string minimized) =>
        notifications.AddNotification(new Notification { Title = "Jaunt", Content = text, MinimizedText = minimized, Type = type });

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
        ListingSource.Shout => "Shout chat",
        _ => "Party Finder",
    };
}
