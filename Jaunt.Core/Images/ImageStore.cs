using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Jaunt.Core.Images;

/// <summary>
/// One entry's icon or splash image: the sources to try, and the PNG file once one gave a picture.
/// Made by <see cref="ImageStore.Slot"/>; <see cref="Path"/> is set off the game thread.
/// </summary>
public sealed class ImageSlot
{
    private volatile string? path;

    internal ImageSlot(string key, ImageKind kind, IReadOnlyList<ImageSource> sources)
    {
        Key = key;
        Kind = kind;
        Sources = sources;
    }

    /// <summary>The kind and sources, which name the slot in the index.</summary>
    public string Key { get; }

    public ImageKind Kind { get; }

    internal IReadOnlyList<ImageSource> Sources { get; }

    /// <summary>The kept PNG's full path, or null while there is none (not fetched yet, or no source gave one).</summary>
    public string? Path
    {
        get => path;
        internal set => path = value;
    }

    /// <summary>Where the slot is; changed on the game thread by <see cref="ImageStore.Want"/> and by the workers.</summary>
    internal volatile SlotState State;

    /// <summary>When a slot whose fetch failed for a passing reason may try again; read with <see cref="State"/>.</summary>
    internal long RetryAtTicks;
}

internal enum SlotState
{
    /// <summary>Not looked at since it was made.</summary>
    New,

    /// <summary>Waiting for or being fetched.</summary>
    Queued,

    /// <summary>Its image (or that it has none) is known and fresh; nothing more to do this session.</summary>
    Settled,

    /// <summary>The last fetch failed for a passing reason; it tries again after <see cref="ImageSlot.RetryAtTicks"/>.</summary>
    Retry,
}

/// <summary>
/// Fetches, converts and keeps listings' icons and splash images on disk, off the game thread, only
/// for slots the window asks for (<see cref="Want"/>) as they come into view. The rules are in
/// <c>docs/design.md</c> under "Icons and splash images".
/// </summary>
/// <remarks>
/// The folder holds one PNG per image address and kind, named by a hash of them, and
/// <c>index.json</c>, which records for each slot the file its sources gave (or none) and when that
/// was worked out, so reopening the window or restarting the game shows kept images without fetching.
/// </remarks>
public sealed class ImageStore : IDisposable
{
    /// <summary>A kept image is looked up again this long after it was found, in case it changed.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromDays(7);

    /// <summary>A slot whose sources gave no image tries them again this long after.</summary>
    public static readonly TimeSpan MissingFor = TimeSpan.FromDays(1);

    /// <summary>A fetch that failed for a passing reason (a timeout, a busy server) tries again this long after, while the game runs.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    /// <summary>An index entry not worked out again for this long is dropped on load, with its file.</summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(30);

    /// <summary>Downloads larger than this are skipped; Partake has sent 60 MB GIFs.</summary>
    internal const int MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>Only this much of a home page is read for its icon links, which are in its head.</summary>
    internal const int MaxPageBytes = 512 * 1024;

    /// <summary>Fetches running at once.</summary>
    private const int Workers = 3;

    private const string IndexName = "index.json";

    private readonly string folder;
    private readonly HttpClient http;
    private readonly TimeProvider time;
    private readonly Action<string, Exception?> log;
    private readonly CancellationTokenSource stopping = new();
    private readonly TimeSpan saveDelay;
    private readonly Func<byte[], ImageKind, byte[]?> process;

    // Set once SkiaSharp's native library fails to load, which won't change while the game runs
    private volatile bool decoderMissing;
    private readonly Channel<ImageSlot> queue = Channel.CreateUnbounded<ImageSlot>();
    private readonly Task[] workers;

    // Game thread only
    private readonly Dictionary<string, ImageSlot> slots = new(StringComparer.Ordinal);

    // Guarded by gate
    private readonly object gate = new();
    private readonly Dictionary<string, IndexEntry> index;
    private bool saveScheduled;
    private int pending;
    private TaskCompletionSource idle = NewIdle(completed: true);

    // Each invite looked up at most once per session, shared by its icon and splash
    private readonly ConcurrentDictionary<string, Task<Lookup<DiscordInvites.Images?>>> invites = new(StringComparer.Ordinal);

    /// <param name="folder">Where images and the index are kept; made if missing.</param>
    /// <param name="handler">Sends the requests; <see cref="PublicNetwork.Handler"/> in the plugin. Disposed with the store.</param>
    /// <param name="log">Logs a message, with the exception if one caused it.</param>
    /// <param name="saveDelay">How long after a change the index is saved, so a burst of fetches saves once; 5 s unless a test sets it.</param>
    /// <param name="process">Converts a download; <see cref="ImageProcessing.Process"/> unless a test stands in for it.</param>
    public ImageStore(
        string folder, HttpMessageHandler handler, string userAgent, Action<string, Exception?> log, TimeProvider? time = null, TimeSpan? saveDelay = null,
        Func<byte[], ImageKind, byte[]?>? process = null)
    {
        this.folder = folder;
        this.log = log;
        this.process = process ?? ImageProcessing.Process;
        this.time = time ?? TimeProvider.System;
        this.saveDelay = saveDelay ?? TimeSpan.FromSeconds(5);
        // Stryker disable once Initializer : a test can't wait out the 20 s timeout; a timeout's handling is tested with a stand-in
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        Directory.CreateDirectory(folder);
        index = LoadIndex();
        workers = [.. Enumerable.Range(0, Workers).Select(_ => Task.Run(WorkAsync))];
    }

    /// <summary>
    /// The slot for an entry's image of this kind from these sources, the same one for the same kind and
    /// sources. A kept image shows at once from its file. Call on the game thread.
    /// </summary>
    public ImageSlot Slot(ImageKind kind, IReadOnlyList<ImageSource> sources)
    {
        var key = KeyOf(kind, sources);
        if (slots.TryGetValue(key, out var slot))
            return slot;

        slot = new ImageSlot(key, kind, sources);
        lock (gate)
        {
            if (index.TryGetValue(key, out var entry) && entry.File is { } file && File.Exists(System.IO.Path.Combine(folder, file)))
                slot.Path = System.IO.Path.Combine(folder, file);
        }

        slots[key] = slot;
        return slot;
    }

    /// <summary>
    /// Asks for the slot's image, as it is in view: fetches it unless it is kept and fresh, or failed
    /// recently. Cheap enough to call every frame; allocates only when it starts a fetch. Call on the game thread.
    /// </summary>
    /// <returns>Whether it started a fetch, so draw code can count that frame as filling a cache.</returns>
    public bool Want(ImageSlot slot)
    {
        switch (slot.State)
        {
            case SlotState.Queued or SlotState.Settled:
                return false;
            case SlotState.Retry when time.GetUtcNow().UtcTicks < Interlocked.Read(ref slot.RetryAtTicks):
                return false;
        }

        if (slot.State == SlotState.New && IsFresh(slot))
        {
            slot.State = SlotState.Settled;
            return false;
        }

        slot.State = SlotState.Queued;
        lock (gate)
        {
            if (pending++ == 0)
                idle = NewIdle(completed: false);
        }

        queue.Writer.TryWrite(slot);
        return true;
    }

    /// <summary>Completes once no fetch is waiting or running, for tests.</summary>
    internal Task Idle
    {
        get
        {
            lock (gate)
                return idle.Task;
        }
    }

    // Stryker disable Statement : stopping the workers and freeing the client can't be observed after the store is gone; SaveIndex is tested by reopening
    public void Dispose()
    {
        queue.Writer.TryComplete();
        stopping.Cancel();
#pragma warning disable VSTHRD002 // Blocks unload at most 2 s so no worker writes after the store is gone
        Task.WaitAll(workers, TimeSpan.FromSeconds(2));
#pragma warning restore VSTHRD002
        SaveIndex();
        http.Dispose();
        stopping.Dispose();
    }

    // Stryker restore Statement
    // Stryker disable once String : every source's key starts with its kind ("url:"), so keys stay distinct without the separators
    internal static string KeyOf(ImageKind kind, IReadOnlyList<ImageSource> sources) =>
        kind + "|" + string.Join("|", sources.Select(s => s.Key));

    /// <summary>The file a kind of image from an address is kept in: a hash of both, so the same picture is kept once.</summary>
    // Stryker disable once String : the kinds' names end in letters and an address starts with its scheme, so the hash stays distinct without the separator
    internal static string FileNameOf(ImageKind kind, Uri uri) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "|" + uri.AbsoluteUri)))[..32] + ".png";

    private bool IsFresh(ImageSlot slot)
    {
        lock (gate)
        {
            if (!index.TryGetValue(slot.Key, out var entry))
                return false;
            var age = time.GetUtcNow() - entry.Checked;
            return entry.File is null ? age < MissingFor : slot.Path is not null && age < FreshFor;
        }
    }

    private async Task WorkAsync()
    {
        var token = stopping.Token;
        try
        {
            while (await queue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (queue.Reader.TryRead(out var slot))
                {
                    try
                    {
                        await ResolveAsync(slot, token).ConfigureAwait(false);
                    }
                    // Stryker disable once Block,Statement : once unloading, the loop's next wait ends it anyway
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception e) when (e is DllNotFoundException or TypeInitializationException { InnerException: DllNotFoundException })
                    {
                        // Every image would fail the same way, so say it once and stop downloading
                        if (!decoderMissing)
                        {
                            decoderMissing = true;
                            // Stryker disable once String : the message is for the developer reading the log, not behaviour
                            log("Images are off: SkiaSharp's native library (libSkiaSharp.dll) couldn't be loaded", e);
                        }

                        Retry(slot);
                    }
                    catch (Exception e)
                    {
                        // Stryker disable once String,Statement : the message is for the developer reading the log, not behaviour
                        log($"Couldn't fetch the {slot.Kind} image for {slot.Key}", e);
                        Retry(slot);
                    }
                    finally
                    {
                        Finished();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Unloading
        }
    }

    private void Finished()
    {
        lock (gate)
        {
            if (--pending == 0)
                idle.TrySetResult();
        }
    }

    /// <summary>Tries the slot's sources in turn; the first picture wins. A passing failure stops and retries later, keeping any image shown.</summary>
    private async Task ResolveAsync(ImageSlot slot, CancellationToken token)
    {
        if (decoderMissing)
        {
            // Kept images still show; new ones can't be converted
            Retry(slot);
            return;
        }

        foreach (var source in slot.Sources)
        {
            var found = source switch
            {
                PictureSource picture => await ImageAsync(picture.Uri, slot.Kind, token).ConfigureAwait(false),
                DiscordSource discord => await DiscordAsync(discord.Code, slot.Kind, token).ConfigureAwait(false),
                FaviconSource favicon => await FaviconAsync(favicon.Site, token).ConfigureAwait(false),
                _ => Lookup<string>.Absent,
            };

            if (found.Outcome == Outcome.Passing)
            {
                Retry(slot);
                return;
            }

            if (found.Value is { } file)
            {
                Settle(slot, file);
                return;
            }
        }

        Settle(slot, null);
    }

    private void Retry(ImageSlot slot)
    {
        Interlocked.Exchange(ref slot.RetryAtTicks, (time.GetUtcNow() + RetryAfter).UtcTicks);
        slot.State = SlotState.Retry;
    }

    private void Settle(ImageSlot slot, string? file)
    {
        lock (gate)
            index[slot.Key] = new IndexEntry { File = file, Checked = time.GetUtcNow() };
        slot.Path = file is null ? null : System.IO.Path.Combine(folder, file);
        slot.State = SlotState.Settled;
        ScheduleSave();
    }

    /// <summary>The kept file for a kind of image at an address, downloading and converting it unless it is already kept.</summary>
    private async Task<Lookup<string>> ImageAsync(Uri uri, ImageKind kind, CancellationToken token)
    {
        var name = FileNameOf(kind, uri);
        var full = System.IO.Path.Combine(folder, name);
        if (File.Exists(full))
            return Lookup<string>.Found(name);

        var download = await GetAsync(uri, MaxImageBytes, token).ConfigureAwait(false);
        if (download.Value is not { } data)
            return new Lookup<string>(null, download.Outcome);
        if (process(data.Bytes, kind) is not { } png)
            return Lookup<string>.Absent;

        // Written beside it and renamed into place, so the game never loads half a file
        // Stryker disable once String : without a suffix the file is written in place, which only a reader at that moment would see
        var temporary = full + ".tmp";
        await File.WriteAllBytesAsync(temporary, png, token).ConfigureAwait(false);
        // Stryker disable once Boolean : only two workers writing one image at the same moment find it already there, which a test can't time
        File.Move(temporary, full, overwrite: true);
        return Lookup<string>.Found(name);
    }

    /// <summary>An invite's server icon, or for a splash its banner and then its invite splash.</summary>
    private async Task<Lookup<string>> DiscordAsync(string code, ImageKind kind, CancellationToken token)
    {
        var lookup = await invites.GetOrAdd(code, c => InviteAsync(c, token)).ConfigureAwait(false);
        if (lookup.Outcome == Outcome.Passing)
        {
            // A passing failure is asked again next time, not remembered
            invites.TryRemove(code, out _);
            return Lookup<string>.Passing;
        }

        if (lookup.Value is not { } images)
            return Lookup<string>.Absent;
        Uri?[] tried = kind == ImageKind.Icon ? [images.Icon] : [images.Banner, images.Splash];
        foreach (var uri in tried.OfType<Uri>())
        {
            var found = await ImageAsync(uri, kind, token).ConfigureAwait(false);
            if (found.Outcome == Outcome.Passing || found.Value is not null)
                return found;
        }

        return Lookup<string>.Absent;
    }

    private async Task<Lookup<DiscordInvites.Images?>> InviteAsync(string code, CancellationToken token)
    {
        var response = await GetAsync(DiscordInvites.Url(code), MaxPageBytes, token).ConfigureAwait(false);
        if (response.Value is not { } data)
            return new Lookup<DiscordInvites.Images?>(null, response.Outcome);
        try
        {
            return Lookup<DiscordInvites.Images?>.Found(DiscordInvites.Parse(Encoding.UTF8.GetString(data.Bytes)));
        }
        // Stryker disable once Block : an empty lookup has no images, so it counts as absent too
        catch (JsonException)
        {
            return Lookup<DiscordInvites.Images?>.Absent;
        }
    }

    /// <summary>A website's icon: those its home page names, then <c>/favicon.ico</c>.</summary>
    private async Task<Lookup<string>> FaviconAsync(Uri site, CancellationToken token)
    {
        var page = await GetAsync(site, MaxPageBytes, token).ConfigureAwait(false);
        if (page.Outcome == Outcome.Passing)
            return Lookup<string>.Passing;

        // A page that isn't there may still have /favicon.ico; relative links are read against where redirects led
        var html = page.Value is { } data ? Encoding.UTF8.GetString(data.Bytes) : null;
        foreach (var uri in Favicons.Candidates(html, page.Value?.FinalUri ?? site))
        {
            var found = await ImageAsync(uri, ImageKind.Icon, token).ConfigureAwait(false);
            if (found.Outcome == Outcome.Passing || found.Value is not null)
                return found;
        }

        return Lookup<string>.Absent;
    }

    /// <summary>
    /// Downloads at most <paramref name="maxBytes"/>. A missing page, a refused or unknown host, an
    /// address off the public internet and a download too large are absent; a timeout, a rate limit
    /// (429) and a server error (5xx) are passing.
    /// </summary>
    private async Task<Lookup<Download>> GetAsync(Uri uri, int maxBytes, CancellationToken token)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
                return Lookup<Download>.Passing;
            // Stryker disable once Logical,Equality : the stated length only saves reading a body the loop below stops at the same size
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
                return Lookup<Download>.Absent;

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
            {
                // Stryker disable once Equality,Arithmetic : whether the cap falls exactly or one read (80 KB) later doesn't matter
                if (buffer.Length + read > maxBytes)
                    return Lookup<Download>.Absent;
                await buffer.WriteAsync(chunk.AsMemory(0, read), token).ConfigureAwait(false);
            }

            return Lookup<Download>.Found(new Download(buffer.ToArray(), response.RequestMessage?.RequestUri ?? uri));
        }
        // Stryker disable once Block : an empty lookup has no image, so it counts as absent too
        catch (HttpRequestException e) when (e.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError)
        {
            // An unknown or refused host won't come back soon; PublicNetwork's refusal is a connection error too
            return Lookup<Download>.Absent;
        }
        catch (HttpRequestException)
        {
            return Lookup<Download>.Passing;
        }
        catch (TaskCanceledException)
        {
            // HttpClient's timeout, or unloading, after which nothing reads the result
            return Lookup<Download>.Passing;
        }
    }

    // Stryker disable Statement,Boolean,Block : saving more often than needed writes the same index; that saves are scheduled is tested
    private void ScheduleSave()
    {
        lock (gate)
        {
            if (saveScheduled)
                return;
            saveScheduled = true;
        }

        _ = SaveLaterAsync();
    }

    private async Task SaveLaterAsync()
    {
        try
        {
            await Task.Delay(saveDelay, time, stopping.Token).ConfigureAwait(false);
        }
        // Stryker disable once Block,Statement : Dispose's save follows at once; a test can't time the two
        catch (OperationCanceledException)
        {
            // Dispose saves instead, and two saves at once would write the same file
            return;
        }

        SaveIndex();
    }

    // Stryker restore Statement,Boolean,Block

    private void SaveIndex()
    {
        string json;
        lock (gate)
        {
            saveScheduled = false;
            json = JsonSerializer.Serialize(new SavedIndex { Version = 1, Slots = index.ToDictionary(e => e.Key, e => (IndexEntry?)e.Value, StringComparer.Ordinal) });
        }

        try
        {
            // Written beside it and renamed into place, so a crash mid-write keeps the last index
            var path = System.IO.Path.Combine(folder, IndexName);
            // Stryker disable once String : without a suffix the index is written in place, which only a crash at that moment would show
            File.WriteAllText(path + ".tmp", json);
            // Stryker disable once Boolean : the first save finds no index to replace; later ones are tested by reopening
            File.Move(path + ".tmp", path, overwrite: true);
        }
        // Stryker disable once Block : logging only
        catch (IOException e)
        {
            // Stryker disable once String,Statement : the message is for the developer reading the log, not behaviour
            log("Couldn't save the image index; images may be fetched again", e);
        }
    }

    /// <summary>
    /// Reads the index, dropping entries not worked out for <see cref="ForgetAfter"/>, and deletes kept
    /// files no entry names. An unreadable index starts empty, so images are fetched again.
    /// </summary>
    private Dictionary<string, IndexEntry> LoadIndex()
    {
        var loaded = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        var path = System.IO.Path.Combine(folder, IndexName);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<SavedIndex>(File.ReadAllText(path)) is { Version: 1, Slots: { } saved })
            {
                var now = time.GetUtcNow();
                foreach (var (key, entry) in saved)
                {
                    if (entry is not null && now - entry.Checked < ForgetAfter && (entry.File is null || IsKeptName(entry.File)))
                        loaded[key] = entry;
                }
            }
        }
        // Stryker disable once Block : logging only
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Stryker disable once String,Statement : the message is for the developer reading the log, not behaviour
            log("Couldn't read the image index; images will be fetched again", e);
        }

        // Images no entry names, and any left half-written; the index itself and other files stay
        var named = loaded.Values.Select(e => e.File).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = System.IO.Path.GetFileName(file);
            if ((name.EndsWith(".png", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.Ordinal)) && !named.Contains(name))
                TryDelete(file);
        }

        return loaded;
    }

    /// <summary>Whether a name is one <see cref="FileNameOf"/> gives, so a hand-edited index can't point outside the folder.</summary>
    internal static bool IsKeptName(string name) =>
        name.Length == 36 && name.EndsWith(".png", StringComparison.Ordinal) && name[..32].All(char.IsAsciiHexDigitLower);

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        // Stryker disable once Block : logging only
        catch (IOException e)
        {
            // Stryker disable once String,Statement : the message is for the developer reading the log, not behaviour
            log($"Couldn't delete the unused image {file}", e);
        }
    }

    private static TaskCompletionSource NewIdle(bool completed)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed)
            source.SetResult();
        return source;
    }

    private enum Outcome
    {
        Found,

        /// <summary>This source has no such image; the next is tried.</summary>
        Absent,

        /// <summary>Failed for a reason that may pass (a timeout, a rate limit); tried again later.</summary>
        Passing,
    }

    private readonly record struct Lookup<T>(T? Value, Outcome Outcome)
    {
        public static Lookup<T> Absent => new(default, Outcome.Absent);

        public static Lookup<T> Passing => new(default, Outcome.Passing);

        public static Lookup<T> Found(T value) => new(value, Outcome.Found);
    }

    private sealed record Download(byte[] Bytes, Uri FinalUri);

    private sealed class SavedIndex
    {
        public int Version { get; set; }
        public Dictionary<string, IndexEntry?>? Slots { get; set; }
    }

    private sealed class IndexEntry
    {
        /// <summary>The kept file's name in the folder, or null when no source gave an image.</summary>
        public string? File { get; set; }

        /// <summary>When the slot's sources were last tried.</summary>
        public DateTimeOffset Checked { get; set; }
    }
}
