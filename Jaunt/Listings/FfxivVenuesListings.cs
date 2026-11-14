using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Listings;

/// <summary>
/// Fetches the venue list from FFXIV Venues when the listing window opens, unless the source is
/// within its cooldown (<see cref="RefreshPolicy"/>), and records it in the listing cache.
/// </summary>
/// <remarks>
/// The download and parsing run off the game thread; the cache is only touched on it. Every request
/// is recorded with <see cref="ListingCache.Fetched"/> before it is sent, so a failed one still counts
/// against the cooldown.
/// </remarks>
internal sealed class FfxivVenuesListings : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly ListingCache cache;
    private readonly Gazetteer gazetteer;
    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Action save;
    private readonly HttpClient http;
    private readonly TimeProvider time;
    private readonly CancellationTokenSource stopping = new();
    private Task fetching = Task.CompletedTask;

    public FfxivVenuesListings(ListingCache cache, Gazetteer gazetteer, Configuration configuration, IFramework framework, IPluginLog log, Action save)
        : this(cache, gazetteer, configuration, framework, log, save, new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }, TimeProvider.System)
    {
    }

    /// <summary>For the self-tests: a stand-in for the network and the clock, so a test never reaches FFXIV Venues.</summary>
    internal FfxivVenuesListings(ListingCache cache, Gazetteer gazetteer, Configuration configuration, IFramework framework, IPluginLog log, Action save, HttpMessageHandler handler, TimeProvider time)
    {
        this.cache = cache;
        this.gazetteer = gazetteer;
        this.configuration = configuration;
        this.framework = framework;
        this.log = log;
        this.save = save;
        this.time = time;
        http = new HttpClient(handler) { Timeout = Timeout };
        var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Jaunt/{version} (+https://github.com/Cellar-Mander/jaunt)");
    }

    /// <summary>The latest fetch, or a completed task if none has started.</summary>
    internal Task Fetching => fetching;

    /// <summary>Starts a fetch if the cooldown has passed and none is running. Call on the game thread.</summary>
    public void FetchIfDue()
    {
        var now = time.GetUtcNow();
        if (!fetching.IsCompleted || !cache.ShouldFetch(ListingSource.FfxivVenues, configuration.RefreshCooldownMinutes, now))
            return;

        cache.Fetched(ListingSource.FfxivVenues, now);
        save();
        fetching = Task.Run(() => FetchAsync(stopping.Token));
    }

    private async Task FetchAsync(CancellationToken token)
    {
        try
        {
            var json = await http.GetStringAsync(FfxivVenues.Url, token).ConfigureAwait(false);
            var venues = FfxivVenues.Parse(json, gazetteer);
            await framework.RunOnFrameworkThread(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                FfxivVenues.Record(cache, venues, time.GetUtcNow());
                save();
            }).ConfigureAwait(false);
            log.Information($"Fetched {venues.Count} venues from FFXIV Venues");
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Unloading
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.Warning(e, "Couldn't fetch venues from FFXIV Venues; showing cached ones until the cooldown passes");
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't record venues from FFXIV Venues");
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
