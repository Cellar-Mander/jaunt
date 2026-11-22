using System;
using System.IO;
using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Jaunt.Core.Browser;
using Jaunt.Core.Images;
using Jaunt.Core.Listings;
using Jaunt.Listings;
using Jaunt.Text;
using Jaunt.Travel;
using Jaunt.Windows;

namespace Jaunt;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IPartyFinderGui PartyFinderGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static ISelfTestRegistry SelfTestRegistry { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    private const string CommandName = "/jaunt";

    // Reading where the player is is cheap, so arriving somewhere is noticed quickly; AutoVisits.Due paces the entry checks
    private static readonly TimeSpan AddressCheckInterval = TimeSpan.FromSeconds(5);

    public Configuration Configuration { get; init; }

    /// <summary>Listings kept between sessions; use from the game thread and save with <see cref="SaveListingCache"/>.</summary>
    internal ListingCache ListingCache { get; }
    private readonly ListingCacheStore listingCacheStore;
    private readonly PartyFinderListings partyFinderListings;
    private readonly ShoutListings shoutListings;
    private readonly FetchedListings xivpfListings;
    private readonly FetchedListings ffxivVenuesListings;
    private readonly FetchedListings partakeListings;
    private readonly FetchedListings rpEventCalendarListings;
    private readonly IFontHandle titleFontHandle;
    private readonly HeldFont titleFont;
    private readonly HeldFont iconFont;
    private readonly ImageStore imageStore;
    private readonly PlayerAddress playerAddress;
    private readonly AutoVisits autoVisits = new();
    private DateTimeOffset nextAddressCheck;

    public readonly WindowSystem WindowSystem = new("Jaunt");
    private ConfigWindow ConfigWindow { get; init; }
    private ListingsWindow ListingsWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Normalize();

        listingCacheStore = new ListingCacheStore(PluginInterface.ConfigDirectory.FullName, Log);
        ListingCache = listingCacheStore.Load();
        if (ListingCache.RemoveExpired(DateTimeOffset.UtcNow) > 0)
            SaveListingCache();

        // Commit hash included, to check which checkout's build the game has loaded
        Log.Information($"Jaunt {typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} loaded");

        var gazetteer = GazetteerLoader.Load(DataManager);
        var region = new CurrentRegion(PlayerState, gazetteer);
        partyFinderListings = new PartyFinderListings(PartyFinderGui, Framework, Log, ListingCache, region.Get, SaveListingCache);
        shoutListings = new ShoutListings(ChatGui, Framework, PlayerState, Log, ListingCache, gazetteer, region.Get, SaveListingCache);

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        xivpfListings = FetchedListings.Xivpf(region.Get, ListingCache, Configuration, Framework, Log, SaveListingCache);
        ffxivVenuesListings = FetchedListings.FfxivVenues(region.Get, ListingCache, Configuration, Framework, Log, SaveListingCache);
        partakeListings = FetchedListings.Partake(region.Get, ListingCache, Configuration, Framework, Log, SaveListingCache);
        rpEventCalendarListings = FetchedListings.RpEventCalendar(region.Get, ListingCache, Configuration, Framework, Log, SaveListingCache);
        // Titles are a quarter larger than the default font, at whatever size the user set it
        titleFontHandle = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(-1.25f)));
        titleFont = new HeldFont(titleFontHandle);
        iconFont = new HeldFont(PluginInterface.UiBuilder.IconFontHandle);
        imageStore = new ImageStore(
            Path.Combine(PluginInterface.ConfigDirectory.FullName, "images"), PublicNetwork.Handler(), FetchedListings.UserAgent,
            (message, e) => Log.Warning(e, message));
        ListingsWindow = new ListingsWindow(
            ListingCache, gazetteer, GamePlacesLoader.Load(DataManager), new Lifestream(PluginInterface, Log), new GamePartyFinder(PlayerState), HousingMap.Load(DataManager, GameGui), NotificationManager, Configuration, FetchSources, SaveListingCache, region.Get, () => ConfigWindow.IsOpen = true,
            titleFont, iconFont, imageStore, TextureProvider, PlaceholderArt.Backdrops(DataManager),
            new CalendarOpener(Path.Combine(PluginInterface.ConfigDirectory.FullName, "calendar"), NotificationManager, Log));
        WindowSystem.AddWindow(ListingsWindow);
        playerAddress = new PlayerAddress(PlayerState, ClientState, Log);
        // On drawing rather than Framework.Update, as a check may rebuild the list, which needs an ImGui frame
        PluginInterface.UiBuilder.Draw += CheckVisits;
        SelfTestRegistry.RegisterTestSteps(new SelfTests(ConfigWindow, ListingsWindow, Configuration, gazetteer).Steps());

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the listing browser. /jaunt config → open the settings"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    public void Dispose()
    {
        // Unhook from Dalamud first, so no command or draw call reaches a disposed component
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= CheckVisits;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        partyFinderListings.Dispose();
        shoutListings.Dispose();
        xivpfListings.Dispose();
        ffxivVenuesListings.Dispose();
        partakeListings.Dispose();
        rpEventCalendarListings.Dispose();

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        titleFont.Dispose();
        iconFont.Dispose();
        titleFontHandle.Dispose();
        imageStore.Dispose();
        listingCacheStore.Dispose();
    }

    /// <summary>
    /// Marks visited what is open where the player is, when Settings → Listings says to: on arriving at a
    /// plot or apartment, then every <see cref="AutoVisits.CheckInterval"/> while there.
    /// </summary>
    private void CheckVisits()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < nextAddressCheck)
            return;
        nextAddressCheck = now + AddressCheckInterval;
        if (Configuration.AutoMarkVisited && autoVisits.Due(playerAddress.Get(), now))
            ListingsWindow.CheckVisits(autoVisits, now);
    }

    internal void SaveListingCache() => listingCacheStore.Save(ListingCache);

    private void FetchSources()
    {
        xivpfListings.FetchIfDue();
        ffxivVenuesListings.FetchIfDue();
        partakeListings.FetchIfDue();
        rpEventCalendarListings.FetchIfDue();
    }

    // "config" always opens the settings rather than toggling, so it never closes a window someone
    // asked to see.
    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            ConfigWindow.IsOpen = true;
        else
            ToggleMainUi();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();

    public void ToggleMainUi() => ListingsWindow.Toggle();
}
