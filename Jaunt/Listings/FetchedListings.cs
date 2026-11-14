using System;
using System.Collections.Generic;
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
/// Fetches one web source's listings when the listing window opens, unless the source is within its
/// cooldown (<see cref="RefreshPolicy"/>), and records them in the listing cache.
/// </summary>
/// <remarks>
/// The download and parsing run off the game thread; the cache is only touched on it. Every request
/// is recorded with <see cref="ListingCache.Fetched"/> before it is sent, so a failed one still counts
/// against the cooldown.
/// </remarks>
internal sealed class FetchedListings : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly ListingSource source;
    private readonly string sourceName;
    private readonly string itemName;
    private readonly Download download;
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
    /// Requests and parses a source's listings at a time; throws <see cref="JsonException"/> when a
    /// response is not the expected shape.
    /// </summary>
    private delegate Task<Recording> Download(HttpClient http, DateTimeOffset now, CancellationToken token);

    /// <param name="sourceName">The source as the log names it.</param>
    /// <param name="itemName">Its listings as the log names them ("venues").</param>
    /// <param name="handler">A stand-in for the network in the self-tests, so a test never reaches the source.</param>
    /// <param name="time">A stand-in for the clock in the self-tests.</param>
    private FetchedListings(
        ListingSource source, string sourceName, string itemName, Download download,
        ListingCache cache, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler handler, TimeProvider time)
    {
        this.source = source;
        this.sourceName = sourceName;
        this.itemName = itemName;
        this.download = download;
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

    /// <summary>The venue list from FFXIV Venues.</summary>
    public static FetchedListings FfxivVenues(
        ListingCache cache, Gazetteer gazetteer, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler? handler = null, TimeProvider? time = null) =>
        new(
            ListingSource.FfxivVenues, "FFXIV Venues", "venues",
            async (http, _, token) =>
            {
                var json = await http.GetStringAsync(Core.Listings.FfxivVenues.Url, token).ConfigureAwait(false);
                var venues = Core.Listings.FfxivVenues.Parse(json, gazetteer);
                return new Recording(venues.Count, (c, now) => Core.Listings.FfxivVenues.Record(c, venues, now));
            },
            cache, configuration, framework, log, save, handler ?? NewHandler(), time ?? TimeProvider.System);

    /// <summary>The Party Finder ads in the None category from xivpf.</summary>
    public static FetchedListings Xivpf(
        ListingCache cache, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler? handler = null, TimeProvider? time = null) =>
        new(
            ListingSource.Xivpf, "xivpf", "Party Finder ads",
            async (http, _, token) =>
            {
                var json = await http.GetStringAsync(Core.Listings.Xivpf.Url, token).ConfigureAwait(false);
                var ads = Core.Listings.Xivpf.Parse(json);
                return new Recording(ads.Count, (c, now) => Core.Listings.Xivpf.Record(c, ads, now));
            },
            cache, configuration, framework, log, save, handler ?? NewHandler(), time ?? TimeProvider.System);

    /// <summary>
    /// Upcoming and running FFXIV events from Partake: pages of <see cref="Partake.PageSize"/> until one
    /// comes back short, at most <see cref="Partake.MaxPages"/>, all in one fetch.
    /// </summary>
    public static FetchedListings Partake(
        ListingCache cache, Configuration configuration, IFramework framework, IPluginLog log, Action save,
        HttpMessageHandler? handler = null, TimeProvider? time = null) =>
        new(
            ListingSource.Partake, "Partake", "events",
            async (http, now, token) =>
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

                return new Recording(events.Count, (c, at) => Core.Listings.Partake.Record(c, events, at));
            },
            cache, configuration, framework, log, save, handler ?? NewHandler(), time ?? TimeProvider.System);

    private static HttpClientHandler NewHandler() => new() { AutomaticDecompression = DecompressionMethods.All };

    /// <summary>The latest fetch, or a completed task if none has started.</summary>
    internal Task Fetching => fetching;

    /// <summary>Starts a fetch if the cooldown has passed and none is running. Call on the game thread.</summary>
    public void FetchIfDue()
    {
        var now = time.GetUtcNow();
        if (!fetching.IsCompleted || !cache.ShouldFetch(source, configuration.RefreshCooldownMinutes, now))
            return;

        cache.Fetched(source, now);
        save();
        fetching = Task.Run(() => FetchAsync(stopping.Token));
    }

    private async Task FetchAsync(CancellationToken token)
    {
        try
        {
            var recording = await download(http, time.GetUtcNow(), token).ConfigureAwait(false);
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
