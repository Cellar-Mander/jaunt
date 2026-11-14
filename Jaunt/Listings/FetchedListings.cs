using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Listings;

/// <summary>
/// Fetches one web source's listings for the character's region when the listing window opens, unless
/// the source is within its cooldown (<see cref="RefreshPolicy"/>), and records the ones on worlds the
/// character can travel to in the listing cache. Nothing is fetched while no character is logged in.
/// </summary>
/// <remarks>
/// The download and parsing run off the game thread; the cache is only touched on it. Every request
/// is recorded with <see cref="ListingCache.Fetched"/> before it is sent, so a failed one still counts
/// against the cooldown. A source fetched by data centre (<see cref="RefreshPolicy.ByDataCentre"/>)
/// asks for each of the region's data centres whose cooldown has passed, one after another, in one fetch.
/// </remarks>
internal sealed class FetchedListings : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly ListingSource source;
    private readonly string sourceName;
    private readonly string itemName;
    private readonly Download download;
    private readonly Func<TravelRegion?> region;
    private readonly ListingCache cache;
    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Action save;
    private readonly HttpClient http;
    private readonly TimeProvider time;
    private readonly CancellationTokenSource stopping = new();
    private Task fetching = Task.CompletedTask;

    /// <summary>A parsed response: how many listings it holds, and how to record them in the cache at a time.</summary>
    internal sealed record Recording(int Count, Action<ListingCache, DateTimeOffset> Record);

    /// <summary>
    /// Requests and parses a source's listings at a time for a region; throws <see cref="JsonException"/>
    /// when a response is not the expected shape.
    /// </summary>
    /// <param name="dataCentres">For a source fetched by data centre, the ones to ask for; empty otherwise.</param>
    private delegate Task<Recording> Download(
        HttpClient http, DateTimeOffset now, TravelRegion region, IReadOnlyList<string> dataCentres, CancellationToken token);

    /// <param name="sourceName">The source as the log names it.</param>
    /// <param name="itemName">Its listings as the log names them ("venues").</param>
    /// <param name="region">The worlds the current character can travel to, or null while none is logged in. Read on the game thread.</param>
    /// <param name="handler">A stand-in for the network in the self-tests, so a test never reaches the source.</param>
    /// <param name="time">A stand-in for the clock in the self-tests.</param>
    private FetchedListings(
        ListingSource source, string sourceName, string itemName, Download download, Func<TravelRegion?> region,
        ListingCache cache, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler handler, TimeProvider time)
    {
        this.source = source;
        this.sourceName = sourceName;
        this.itemName = itemName;
        this.download = download;
        this.region = region;
        this.cache = cache;
        this.configuration = configuration;
        this.framework = framework;
        this.log = log;
        this.save = save;
        this.time = time;
        http = new HttpClient(handler) { Timeout = Timeout };
        var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Jaunt/{version} (+https://github.com/Cellar-Mander/jaunt)");
    }

    /// <summary>The venue list from FFXIV Venues, one request per data centre due.</summary>
    public static FetchedListings FfxivVenues(
        Func<TravelRegion?> region, ListingCache cache, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler? handler = null, TimeProvider? time = null) =>
        new(
            ListingSource.FfxivVenues, "FFXIV Venues", "venues",
            async (http, _, _, dataCentres, token) =>
            {
                var venues = new List<Core.Listings.FfxivVenues.Venue>();
                foreach (var dataCentre in dataCentres)
                {
                    var json = await http.GetStringAsync(Core.Listings.FfxivVenues.UrlFor(dataCentre), token).ConfigureAwait(false);
                    venues.AddRange(Core.Listings.FfxivVenues.Parse(json));
                }

                return new Recording(venues.Count, (c, now) => Core.Listings.FfxivVenues.Record(c, venues, now));
            },
            region, cache, configuration, framework, log, save, handler ?? NewHandler(), time ?? TimeProvider.System);

    /// <summary>The Party Finder ads in the None category from xivpf.</summary>
    public static FetchedListings Xivpf(
        Func<TravelRegion?> region, ListingCache cache, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler? handler = null, TimeProvider? time = null) =>
        new(
            ListingSource.Xivpf, "xivpf", "Party Finder ads",
            async (http, _, region, _, token) =>
            {
                var json = await http.GetStringAsync(Core.Listings.Xivpf.Url, token).ConfigureAwait(false);
                var ads = Core.Listings.Xivpf.Parse(json);
                return new Recording(ads.Count, (c, now) => Core.Listings.Xivpf.Record(c, ads, now, region));
            },
            region, cache, configuration, framework, log, save, handler ?? NewHandler(), time ?? TimeProvider.System);

    /// <summary>
    /// Upcoming and running FFXIV events from Partake: pages of <see cref="Partake.PageSize"/> until one
    /// comes back short, at most <see cref="Partake.MaxPages"/>, all in one fetch.
    /// </summary>
    public static FetchedListings Partake(
        Func<TravelRegion?> region, ListingCache cache, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler? handler = null, TimeProvider? time = null) =>
        new(
            ListingSource.Partake, "Partake", "events",
            async (http, now, region, _, token) =>
            {
                var start = now - Core.Listings.Partake.Lookback;
                var events = new List<Core.Listings.Partake.Event>();
                for (var pages = 1; ; pages++)
                {
                    var body = Core.Listings.Partake.RequestBody(start, offset: (pages - 1) * Core.Listings.Partake.PageSize);
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    using var response = await http.PostAsync(Core.Listings.Partake.Url, content, token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var page = Core.Listings.Partake.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                    events.AddRange(page.Events);
                    if (!Core.Listings.Partake.HasMore(page, pages))
                        break;
                }

                return new Recording(events.Count, (c, at) => Core.Listings.Partake.Record(c, events, at, region));
            },
            region, cache, configuration, framework, log, save, handler ?? NewHandler(), time ?? TimeProvider.System);

    /// <summary>The next few weeks' events from the FFXIV RP Event Calendar.</summary>
    public static FetchedListings RpEventCalendar(
        Func<TravelRegion?> region, ListingCache cache, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler? handler = null, TimeProvider? time = null) =>
        new(
            ListingSource.RpEventCalendar, "the FFXIV RP Event Calendar", "events",
            async (http, _, region, _, token) =>
            {
                var json = await http.GetStringAsync(Core.Listings.RpEventCalendar.Url, token).ConfigureAwait(false);
                var events = Core.Listings.RpEventCalendar.Parse(json);
                return new Recording(events.Count, (c, now) => Core.Listings.RpEventCalendar.Record(c, events, now, region));
            },
            region, cache, configuration, framework, log, save, handler ?? NewHandler(), time ?? TimeProvider.System);

    private static HttpClientHandler NewHandler() => new() { AutomaticDecompression = DecompressionMethods.All };

    /// <summary>The latest fetch, or a completed task if none has started.</summary>
    internal Task Fetching => fetching;

    /// <summary>
    /// Starts a fetch if a character is logged in, the cooldown has passed (for some data centre in
    /// the region, for a source fetched by data centre) and none is running. Call on the game thread.
    /// </summary>
    public void FetchIfDue()
    {
        var now = time.GetUtcNow();
        if (!fetching.IsCompleted || region() is not { } travel)
            return;

        var cooldown = configuration.RefreshCooldownMinutes;
        IReadOnlyList<string> dataCentres = [];
        if (RefreshPolicy.ByDataCentre(source))
        {
            dataCentres = [.. travel.DataCentres.Where(dc => cache.ShouldFetch(source, cooldown, now, dc))];
            if (dataCentres.Count == 0)
                return;
            foreach (var dataCentre in dataCentres)
                cache.Fetched(source, now, dataCentre);
        }
        else
        {
            if (!cache.ShouldFetch(source, cooldown, now))
                return;
            cache.Fetched(source, now);
        }

        save();
        fetching = Task.Run(() => FetchAsync(travel, dataCentres, stopping.Token));
    }

    private async Task FetchAsync(TravelRegion travel, IReadOnlyList<string> dataCentres, CancellationToken token)
    {
        try
        {
            var recording = await download(http, time.GetUtcNow(), travel, dataCentres, token).ConfigureAwait(false);
            await framework.RunOnFrameworkThread(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                recording.Record(cache, time.GetUtcNow());
                save();
            }).ConfigureAwait(false);
            log.Information($"Fetched {recording.Count} {itemName} from {sourceName}");
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Unloading
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.Warning(e, $"Couldn't fetch {itemName} from {sourceName}; showing cached ones until the cooldown passes");
        }
        catch (Exception e)
        {
            log.Error(e, $"Couldn't record {itemName} from {sourceName}");
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
#pragma warning disable VSTHRD002 // Blocks unload at most 2 s so a fetch doesn't touch the cache after it is gone
        fetching.Wait(TimeSpan.FromSeconds(2));
#pragma warning restore VSTHRD002
        http.Dispose();
        stopping.Dispose();
    }
}
