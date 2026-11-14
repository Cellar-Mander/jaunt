using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Jaunt.Core.Listings;

namespace Jaunt.Listings;

/// <summary>Loads the listing cache from the plugin's config folder and saves it there off the game thread.</summary>
internal sealed class ListingCacheStore : IDisposable
{
    private readonly string path;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private Task pending = Task.CompletedTask;

    public ListingCacheStore(string configDirectory, IPluginLog log)
    {
        path = Path.Combine(configDirectory, "listings.json");
        this.log = log;
    }

    /// <summary>
    /// Reads the saved cache, or an empty one if there is none. An unreadable file is moved aside to
    /// <c>listings.json.bad</c>, so the next save doesn't overwrite marks someone could still recover.
    /// </summary>
    public ListingCache Load()
    {
        if (!File.Exists(path))
            return new ListingCache();

        try
        {
            return ListingCache.FromJson(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            log.Warning(e, $"Couldn't read {path}; moved it to listings.json.bad and started an empty cache");
            TryMoveAside();
        }
        catch (IOException e)
        {
            log.Warning(e, $"Couldn't read {path}; started an empty cache");
        }

        return new ListingCache();
    }

    /// <summary>Takes a snapshot now and writes it on a background task, after any earlier save.</summary>
    public void Save(ListingCache cache)
    {
        var json = cache.ToJson();
        lock (gate)
            pending = pending.ContinueWith(_ => Write(json), TaskScheduler.Default);
    }

    private void Write(string json)
    {
        // Write then rename, so a crash mid-write never leaves a half-written cache
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error(e, $"Couldn't save {path}");
        }
    }

    private void TryMoveAside()
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error(e, $"Couldn't move {path} aside");
        }
    }

    /// <summary>Waits briefly for the last save, so unloading the plugin doesn't lose it.</summary>
    public void Dispose()
    {
        Task last;
        lock (gate)
            last = pending;
#pragma warning disable VSTHRD002 // Blocks unload at most 5 s so the last save (a Clear or Un-hide) isn't lost
        if (!last.Wait(TimeSpan.FromSeconds(5)))
#pragma warning restore VSTHRD002
            log.Warning("The listing cache was still saving when Jaunt unloaded");
    }
}
