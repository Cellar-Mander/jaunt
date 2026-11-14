using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.SelfTest;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;
using Jaunt.Listings;
using Jaunt.Windows;

namespace Jaunt;

// Dalamud's self-test window (/xldev, Dalamud menu, Open Self-Test; group Jaunt): the checks of
// docs/smoke-test.md that need no look at the screen and no click, one step per check. Each step works
// on its own scratch copies (a scratch folder, cache, settings object and a stand-in for FFXIV Venues),
// so it never touches the user's listings.json and runs the same alone or after the others. The
// settings step saves through Dalamud and puts the user's settings back on disk before it ends.
internal sealed class SelfTests(ConfigWindow configWindow, ListingsWindow listingsWindow, Configuration configuration, Gazetteer gazetteer)
{
    // Frames to wait for a background save or fetch: about 5 s at 60 fps
    private const int Timeout = 300;

    private const string SampleResource = "Jaunt.smoke-test-listings.json";

    private static readonly DateTimeOffset SampleFetched = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    // Two venues in FFXIV Venues' shape, one on a misspelt world the gazetteer corrects
    private const string VenuesJson = """
        [{"id":"st-1","name":"Self-test Lounge","location":{"world":"Cuchulainn","district":"Mist","ward":14,"plot":33,"apartment":0,"room":0,"subdivision":false},"website":"https://example.com/","discord":null,"schedule":[],"approved":true},
         {"id":"st-2","name":"Self-test Bar","location":{"world":"Bismark","district":"Goblet","ward":3,"plot":7,"apartment":0,"room":0,"subdivision":false},"website":null,"discord":null,"schedule":[],"approved":true}]
        """;

    // Three listings in xivpf's shape: a venue ad, one the game has also sent and a raid
    private const string XivpfJson = """
        [{"listing":{"id":9000001,"category":"None","description":{"en":"Self-test Club Mist W14 P33","ja":"Self-test Club Mist W14 P33"},"created_world":{"name":"Cuchulainn"}}},
         {"listing":{"id":9000002,"category":"None","description":{"en":"Self-test Den Goblet W3 P7"},"created_world":{"name":"Bismarck"}}},
         {"listing":{"id":9000003,"category":"HighEndDuty","description":{"en":"Self-test raid"},"created_world":{"name":"Cuchulainn"}}}]
        """;

    // Three events in Partake's shape at the self-test clock's 20:00: one tonight, one that ended and one with no world
    private const string PartakeJson = """
        {"data":{"events":[
          {"id":9100001,"title":"Self-test Gala","startsAt":"2026-11-14T21:00:00Z","endsAt":"2026-11-15T01:00:00Z","location":"LB W19 P58","server":{"name":"Lich"},"dataCenter":{"name":"Light"},"team":{"websiteUrl":"https://example.com/","discordUrl":null}},
          {"id":9100002,"title":"Self-test Matinee","startsAt":"2026-11-14T12:00:00Z","endsAt":"2026-11-14T15:00:00Z","location":"Mist W1 P1","server":{"name":"Lich"},"dataCenter":{"name":"Light"},"team":null},
          {"id":9100003,"title":"Self-test Nowhere","startsAt":"2026-11-14T21:00:00Z","endsAt":"2026-11-14T23:00:00Z","location":"TBA","server":null,"dataCenter":null,"team":null}]}}
        """;

    private sealed class SelfTestFailure(string message) : Exception(message);

    // One check: its body yields to wait a frame and throws SelfTestFailure to fail
    private sealed class Step(string name, Func<IEnumerable<object?>> body) : ISelfTestStep
    {
#pragma warning disable IDISP006 // Dalamud never disposes a step; CleanUp disposes the enumerator instead
        private IEnumerator<object?>? running;
#pragma warning restore IDISP006

        public string Name => name;

        // Called by Dalamud once per frame while the step is current
        public SelfTestStepResult RunStep()
        {
            running ??= body().GetEnumerator();
            try
            {
                if (running.MoveNext())
                    return SelfTestStepResult.Waiting;
                Plugin.Log.Information($"Self-test PASS: {name}");
                CleanUp();
                return SelfTestStepResult.Pass;
            }
            catch (Exception e)
            {
                if (e is SelfTestFailure)
                    Plugin.Log.Warning($"Self-test FAIL: {name} ({e.Message})");
                else
                    Plugin.Log.Error(e, $"Self-test FAIL: {name} (exception)");
                CleanUp();
                return SelfTestStepResult.Fail;
            }
        }

        // Runs the body's finally blocks, which delete its scratch files; Dalamud may call it twice
        public void CleanUp()
        {
            running?.Dispose();
            running = null;
        }
    }

    public IEnumerable<ISelfTestStep> Steps() =>
    [
        new Step("/jaunt toggles the listings window", JauntToggles),
        new Step("/jaunt config opens Settings and keeps it open", JauntConfigOpens),
        new Step("Settings survive Dalamud's save and load", SettingsRoundTrip),
        new Step("Sample cache: expiry, Clear, Un-hide and a background save", SampleCacheRoundTrip),
        new Step("An unreadable cache is moved aside", UnreadableCache),
        new Step("FFXIV Venues: first fetch is recorded", FirstFetch),
        new Step("FFXIV Venues: no fetch inside the 8 h cooldown", Cooldown),
        new Step("FFXIV Venues: a failed fetch counts and keeps venues", FailedFetch),
        new Step("xivpf: first fetch records None ads beside the game's", XivpfFirstFetch),
        new Step("Partake: first fetch records upcoming events, then waits 8 h", PartakeFirstFetch),
    ];

    private static void Check(bool condition, string detail)
    {
        if (!condition)
            throw new SelfTestFailure(detail);
    }

    private static IEnumerable<object?> WaitFor(Task task, string what)
    {
        for (var frames = 0; !task.IsCompleted; frames++)
        {
            Check(frames < Timeout, $"{what} took more than {Timeout} frames");
            yield return null;
        }
    }

    // Both toggles run within one frame, so the window never draws and opening it fetches nothing
    private IEnumerable<object?> JauntToggles()
    {
        var wasOpen = listingsWindow.IsOpen;
        var configWasOpen = configWindow.IsOpen;
        try
        {
            Plugin.CommandManager.ProcessCommand("/jaunt");
            Check(listingsWindow.IsOpen != wasOpen, "the first /jaunt didn't toggle the listings window");
            Plugin.CommandManager.ProcessCommand("/jaunt");
            Check(listingsWindow.IsOpen == wasOpen, "the second /jaunt didn't toggle it back");
            Check(configWindow.IsOpen == configWasOpen, "/jaunt changed the Settings window");
        }
        finally
        {
            listingsWindow.IsOpen = wasOpen;
            configWindow.IsOpen = configWasOpen;
        }

        yield break;
    }

    private IEnumerable<object?> JauntConfigOpens()
    {
        var wasOpen = configWindow.IsOpen;
        var listingsWasOpen = listingsWindow.IsOpen;
        try
        {
            configWindow.IsOpen = false;
            Plugin.CommandManager.ProcessCommand("/jaunt config");
            Check(configWindow.IsOpen, "/jaunt config didn't open Settings");
            Plugin.CommandManager.ProcessCommand("/jaunt config");
            Check(configWindow.IsOpen, "a second /jaunt config closed Settings");
            Check(listingsWindow.IsOpen == listingsWasOpen, "/jaunt config toggled the listings window");
        }
        finally
        {
            configWindow.IsOpen = wasOpen;
            listingsWindow.IsOpen = listingsWasOpen;
        }

        yield break;
    }

    // Saves a different settings object, so the live one is never changed, then saves the user's again
    private IEnumerable<object?> SettingsRoundTrip()
    {
        var probe = new Configuration
        {
            RefreshCooldownMinutes = configuration.RefreshCooldownMinutes == 125 ? 130 : 125,
            TimeFormat = configuration.TimeFormat == Core.Display.TimeFormat.System ? Core.Display.TimeFormat.TwentyFourHour : Core.Display.TimeFormat.System,
            ExpandedSections = [Core.SettingsSection.Display],
        };
        try
        {
            Plugin.PluginInterface.SavePluginConfig(probe);
            var loaded = Plugin.PluginInterface.GetPluginConfig() as Configuration;
            Check(loaded is not null, "GetPluginConfig returned no Jaunt settings");
            Check(loaded!.RefreshCooldownMinutes == probe.RefreshCooldownMinutes, $"cooldown came back as {loaded.RefreshCooldownMinutes}, not {probe.RefreshCooldownMinutes}");
            Check(loaded.TimeFormat == probe.TimeFormat, $"time format came back as {loaded.TimeFormat}, not {probe.TimeFormat}");
            Check(loaded.ExpandedSections is { Count: 1 } sections && sections.Contains(Core.SettingsSection.Display), "the expanded sections didn't come back as saved");
        }
        finally
        {
            configuration.Save();
        }

        yield break;
    }

    private static string ScratchFolder()
    {
        var folder = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "self-test");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string ReadSample()
    {
        using var stream = typeof(SelfTests).Assembly.GetManifestResourceStream(SampleResource)
            ?? throw new SelfTestFailure($"the build has no {SampleResource}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // The same cases as "With the sample cache" in docs/smoke-test.md, on a scratch copy
    private static IEnumerable<object?> SampleCacheRoundTrip()
    {
        var folder = ScratchFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "listings.json"), ReadSample());
            using var store = new ListingCacheStore(folder, Plugin.Log);
            var cache = store.Load();
            Check(cache.Count == 8, $"loaded {cache.Count} listings, not 8");
            var expired = cache.RemoveExpired(DateTimeOffset.UtcNow);
            Check(expired == 2, $"{expired} expired on load, not 2");
            var hidden = string.Join(", ", cache.HiddenListings().Select(l => l.Title));
            Check(hidden == "Another hidden, Hidden lounge", $"hidden venues are \"{hidden}\"");
            var cleared = cache.ClearUnmarked();
            Check(cleared == 2, $"Clear removed {cleared}, not 2");
            var unhidden = cache.Find(ListingSource.FfxivVenues, "v-8");
            Check(unhidden is not null, "\"Another hidden\" is missing after Clear");
            unhidden!.Hidden = false;

            store.Save(cache);
            foreach (var wait in WaitFor(store.Pending, "the background save"))
                yield return wait;

            var saved = new ListingCacheStore(folder, Plugin.Log).Load();
            var titles = string.Join(", ", saved.Listings.Select(l => l.Title).Order(StringComparer.Ordinal));
            Check(titles == "Another hidden, Favourite café, Hidden lounge, Visited night", $"read back \"{titles}\"");
            hidden = string.Join(", ", saved.HiddenListings().Select(l => l.Title));
            Check(hidden == "Hidden lounge", $"after Un-hide and a reload, hidden venues are \"{hidden}\"");
            Check(saved.LastFetched(ListingSource.FfxivVenues) == SampleFetched, "the FFXIV Venues fetch time wasn't kept");
            cleared = saved.ClearUnmarked();
            Check(cleared == 1, $"Clear after Un-hide removed {cleared}, not 1");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static IEnumerable<object?> UnreadableCache()
    {
        var folder = ScratchFolder();
        try
        {
            const string broken = "{ not a cache";
            var path = Path.Combine(folder, "listings.json");
            File.WriteAllText(path, broken);
            Plugin.Log.Information("Self-test: the warning about listings.json below is expected");
            using var store = new ListingCacheStore(folder, Plugin.Log);
            var cache = store.Load();
            Check(cache.Count == 0, $"started with {cache.Count} listings, not an empty cache");
            Check(!File.Exists(path), "listings.json is still there");
            Check(File.Exists(path + ".bad") && File.ReadAllText(path + ".bad") == broken, "listings.json.bad doesn't hold the unreadable file");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        yield break;
    }

    private IEnumerable<object?> FirstFetch()
    {
        using var rig = new VenueRig(gazetteer);
        var start = rig.Time.Now;
        rig.Listings.FetchIfDue();
        Check(rig.Cache.LastFetched(ListingSource.FfxivVenues) == start, "the request wasn't recorded before it was sent");
        foreach (var wait in WaitFor(rig.Listings.Fetching, "the fetch"))
            yield return wait;

        Check(rig.Handler.Requests == 1, $"{rig.Handler.Requests} requests, not 1");
        Check(rig.Cache.Count == 2, $"{rig.Cache.Count} venues recorded, not 2");
        var bar = rig.Cache.Find(ListingSource.FfxivVenues, "st-2");
        Check(bar?.Address?.World == "Bismarck", $"the venue on Bismark has world \"{bar?.Address?.World}\"");
        Check(rig.Saves >= 2, $"saved {rig.Saves} times, not after the request and again after the venues");
    }

    private IEnumerable<object?> Cooldown()
    {
        using var rig = new VenueRig(gazetteer);
        var start = rig.Time.Now;
        rig.Listings.FetchIfDue();
        foreach (var wait in WaitFor(rig.Listings.Fetching, "the first fetch"))
            yield return wait;

        rig.Time.Now = start + RefreshPolicy.Minimum(ListingSource.FfxivVenues)!.Value - TimeSpan.FromMinutes(1);
        rig.Listings.FetchIfDue();
        Check(rig.Handler.Requests == 1, "fetched again inside the cooldown");
        Check(rig.Cache.LastFetched(ListingSource.FfxivVenues) == start, "a skipped fetch changed the fetch time");

        rig.Time.Now += TimeSpan.FromMinutes(1);
        rig.Listings.FetchIfDue();
        foreach (var wait in WaitFor(rig.Listings.Fetching, "the second fetch"))
            yield return wait;
        Check(rig.Handler.Requests == 2, "didn't fetch once the cooldown had passed");
        Check(rig.Cache.LastFetched(ListingSource.FfxivVenues) == rig.Time.Now, "the second request wasn't recorded");
    }

    private IEnumerable<object?> FailedFetch()
    {
        using var rig = new VenueRig(gazetteer);
        rig.Listings.FetchIfDue();
        foreach (var wait in WaitFor(rig.Listings.Fetching, "the first fetch"))
            yield return wait;

        rig.Handler.Fail = true;
        rig.Time.Now += RefreshPolicy.Minimum(ListingSource.FfxivVenues)!.Value;
        var failedAt = rig.Time.Now;
        Plugin.Log.Information("Self-test: the warning about fetching venues below is expected");
        rig.Listings.FetchIfDue();
        foreach (var wait in WaitFor(rig.Listings.Fetching, "the failing fetch"))
            yield return wait;

        Check(rig.Handler.Requests == 2, $"{rig.Handler.Requests} requests, not 2");
        Check(rig.Cache.LastFetched(ListingSource.FfxivVenues) == failedAt, "the failed request wasn't recorded");
        Check(rig.Cache.Count == 2, $"{rig.Cache.Count} venues left after the failure, not 2");

        rig.Listings.FetchIfDue();
        Check(rig.Handler.Requests == 2, "retried straight after the failure");
    }

    private static IEnumerable<object?> XivpfFirstFetch()
    {
        var time = new ManualTime();
        var cache = new ListingCache();
        var inGame = cache.Seen(ListingSource.InGamePartyFinder, "9000002", "Self-test Den", time.Now.AddDays(-1));
        var start = time.Now;
        var saves = 0;
        using var listings = FetchedListings.Xivpf(cache, new Configuration(), Plugin.Framework, Plugin.Log, () => saves++, new FakeSource(XivpfJson), time);
        listings.FetchIfDue();
        Check(cache.LastFetched(ListingSource.Xivpf) == start, "the request wasn't recorded before it was sent");
        foreach (var wait in WaitFor(listings.Fetching, "the fetch"))
            yield return wait;

        var club = cache.Find(ListingSource.Xivpf, "9000001");
        Check(club?.Description == "Self-test Club Mist W14 P33" && club.ListedOn == "Cuchulainn", "the venue ad wasn't recorded with its text and world");
        Check(cache.Find(ListingSource.Xivpf, "9000003") is null, "the raid listing was recorded");
        var den = cache.Find(ListingSource.Xivpf, "9000002");
        Check(den is not null && Xivpf.ShownInGame(cache, den), "the ad the game also sent isn't left to the in-game copy");
        Check(inGame.LastSeen == start, "the in-game copy's last seen time didn't move to the fetch");
        Check(saves >= 2, $"saved {saves} times, not after the request and again after the ads");

        time.Now += TimeSpan.FromMinutes(RefreshPolicy.DefaultCooldownMinutes - 1);
        listings.FetchIfDue();
        Check(cache.LastFetched(ListingSource.Xivpf) == start, "fetched again inside the 10 min cooldown");
    }

    private IEnumerable<object?> PartakeFirstFetch()
    {
        var time = new ManualTime();
        var cache = new ListingCache();
        var start = time.Now;
        var saves = 0;
#pragma warning disable IDISP001 // The fetcher's HttpClient disposes it
        var source = new FakeSource(PartakeJson);
#pragma warning restore IDISP001
        using var listings = FetchedListings.Partake(cache, new Configuration(), Plugin.Framework, Plugin.Log, () => saves++, source, time);
        listings.FetchIfDue();
        Check(cache.LastFetched(ListingSource.Partake) == start, "the request wasn't recorded before it was sent");
        foreach (var wait in WaitFor(listings.Fetching, "the fetch"))
            yield return wait;

        Check(source.Requests == 1 && source.LastMethod == HttpMethod.Post, $"sent {source.Requests} requests ({source.LastMethod}), not one POST for a short page");
        var gala = cache.Find(ListingSource.Partake, "9100001");
        Check(gala is { OneOff: true, ListedOn: "Lich", DataCentre: "Light", Location: "LB W19 P58" } && gala.StartsAt == start.AddHours(1),
            "the upcoming event wasn't recorded with its world, location and times");
        Check(cache.Find(ListingSource.Partake, "9100002") is null, "the event that ended was recorded");
        Check(cache.Find(ListingSource.Partake, "9100003") is null, "the event with no world was recorded");
        Check(saves >= 2, $"saved {saves} times, not after the request and again after the events");

        time.Now += RefreshPolicy.Minimum(ListingSource.Partake)!.Value - TimeSpan.FromMinutes(1);
        listings.FetchIfDue();
        Check(source.Requests == 1, "fetched again inside the 8 h cooldown");
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Stands in for a fetched source: answers every request with the body, or fails like a network error
    private sealed class FakeSource(string body) : HttpMessageHandler
    {
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        public volatile bool Fail;

        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            Interlocked.Increment(ref requests);
            if (Fail)
                throw new HttpRequestException("self-test: no network");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    // A fetcher on a scratch cache with default settings, the fake source and a clock the step moves
    private sealed class VenueRig : IDisposable
    {
        public ManualTime Time { get; } = new();
        public FakeSource Handler { get; } = new(VenuesJson);
        public ListingCache Cache { get; } = new();
        public FetchedListings Listings { get; }
        public int Saves { get; private set; }

        public VenueRig(Gazetteer gazetteer) =>
            Listings = FetchedListings.FfxivVenues(Cache, gazetteer, new Configuration(), Plugin.Framework, Plugin.Log, () => Saves++, Handler, Time);

        // Also disposes Handler, through the fetcher's HttpClient
        public void Dispose() => Listings.Dispose();
    }
}
