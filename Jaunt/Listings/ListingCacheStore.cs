using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Jaunt.Core.Listings;

namespace Jaunt.Listings;

/// <summary>
/// Loads the listing cache from the plugin's config folder and saves the files that changed there,
/// serialising and writing them off the game thread.
/// </summary>
internal sealed class ListingCacheStore : IDisposable
{
    private readonly string folder;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private Task pending = Task.CompletedTask;

    public ListingCacheStore(string configDirectory, IPluginLog log)
    {
        folder = configDirectory;
        this.log = log;
    }

    /// <summary>
    /// Reads the saved files, or an empty cache if there are none. An unreadable file is moved aside
    /// with <c>.bad</c> added to its name, so the next save doesn't overwrite marks someone could
    /// still recover, and its part starts empty.
    /// </summary>
    public ListingCache Load() => ListingCache.Load(Read, MoveAside);

    /// <summary>Snapshots the files that changed now and writes them on a background task, after any earlier save.</summary>
    public void Save(ListingCache cache)
    {
        var files = cache.TakeChanges();
        if (files.Count == 0)
            return;
        Enqueue(() =>
        {
            foreach (var file in files)
                Write(file);
        });
    }

    private void Enqueue(Action write)
    {
        lock (gate)
            pending = pending.ContinueWith(_ => write(), TaskScheduler.Default);
    }

    /// <summary>The last save asked for, or a completed task if none is waiting.</summary>
    internal Task Pending
    {
        get
        {
            lock (gate)
                return pending;
        }
    }

    private string FullPath(string path) => Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));

    private string? Read(string path)
    {
        var full = FullPath(path);
        try
        {
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning(e, $"Couldn't read {full}; left it out");
            return null;
        }
    }

    private void Write(ListingFile file)
    {
        // Write then rename, so a crash mid-write never leaves a half-written file
        var path = FullPath(file.Path);
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, file.Json());
            if (file.BackupPath is { } backup && File.Exists(path))
                File.Replace(temp, path, FullPath(backup));
            else
                File.Move(temp, path, overwrite: true);
        }
        catch (Exception e)
        {
            // Not only IO: an unexpected serialisation error must not stop the saves queued after it
            log.Error(e, $"Couldn't save {path}");
        }
    }

    private void MoveAside(string path, JsonException e)
    {
        log.Warning(e, $"Couldn't read {FullPath(path)}; moved it to {path}.bad and left it out");
        TryMove(path, path + ".bad");
    }

    private void TryMove(string from, string to)
    {
        try
        {
            File.Move(FullPath(from), FullPath(to), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error(e, $"Couldn't move {FullPath(from)} to {to}");
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
