using System.Text.Encodings.Web;
using System.Text.Json;

namespace Jaunt.Core.Listings;

/// <summary>A file to write: its path under the plugin's config folder, and its text, made on demand.</summary>
/// <param name="Path">Relative, with <c>/</c> between folders.</param>
/// <param name="BackupPath">Where the file being replaced is kept, or null to keep no copy.</param>
/// <param name="Json">Serialises a snapshot taken when the file was asked for, so it can run on any thread.</param>
public sealed record ListingFile(string Path, string? BackupPath, Func<string> Json);

// The cache's files. Each source's file keeps its listings as the source sent them, in the source's
// own type, so what is shown is worked out again on load; the marks file keeps titles and one-off
// flags too, so the hidden list works without the source files. Sources are saved by name, so the
// files stay readable and a renumbered source can't take another's data.
public sealed partial class ListingCache
{
    /// <summary>The version this build writes in every file; a file with a later one is unreadable.</summary>
    public const int FileVersion = 1;

    public const string MarksFile = "marks.json";

    /// <summary>The marks file before its last save, read when the marks file is missing or unreadable.</summary>
    public const string MarksBackupFile = "marks.json.bak";

    public const string FetchTimesFile = "fetch-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The file keeping one source's listings, e.g. <c>cache/FfxivVenues.json</c>.</summary>
    public static string SourceFile(ListingSource source) => $"cache/{source}.json";

    /// <summary>
    /// The files that changed since the last call, each with a snapshot of what to write, and forgets
    /// the changes. Taking the snapshots is one pass over each changed source's listings; serialising
    /// them is left to <see cref="ListingFile.Json"/>, off the game thread.
    /// </summary>
    public IReadOnlyList<ListingFile> TakeChanges()
    {
        var files = new List<ListingFile>();
        foreach (var source in changedSources)
            files.Add(SourceSnapshot(source));
        if (fetchTimesChanged)
        {
            var times = lastFetched.ToDictionary(pair => FetchTimeKey(pair.Key.Source, pair.Key.DataCentre), pair => pair.Value);
            files.Add(new ListingFile(FetchTimesFile, null, () => Serialise(new SavedFetchTimes { LastFetched = times })));
        }

        if (marksChanged)
        {
            List<SavedMark?> marks = [.. Listings.Where(l => l.IsMarked).Select(l => new SavedMark
            {
                Source = l.Source.ToString(),
                Id = l.Id,
                Title = l.Title,
                OneOff = l.OneOff,
                Favourite = l.Favourite,
                Visited = l.Visited,
                Hidden = l.Hidden,
            })];
            files.Add(new ListingFile(MarksFile, MarksBackupFile, () => Serialise(new SavedMarks { Marks = marks })));
        }

        changedSources.Clear();
        fetchTimesChanged = false;
        marksChanged = false;
        return files;
    }

    /// <summary>Counts everything as changed, so the next <see cref="TakeChanges"/> writes every file, as for a full copy.</summary>
    public void ChangedAll()
    {
        changedSources.UnionWith(Enum.GetValues<ListingSource>());
        fetchTimesChanged = true;
        marksChanged = true;
    }

    private ListingFile SourceSnapshot(ListingSource source) => source switch
    {
        ListingSource.FfxivVenues => Snapshot(source, l => new FfxivVenues.Venue(l.Id, l.Title, l.Venue!)),
        ListingSource.Partake => Snapshot(source, l => new Partake.Event(
            l.Id, l.Title, l.StartsAt ?? default, l.EndsAt ?? default, l.ListedOn ?? "", l.DataCentre, l.Location, l.LinkTexts)),
        ListingSource.RpEventCalendar => Snapshot(source, l => new RpEventCalendar.Event(
            l.Id, l.Title, l.ListedOn, l.Location, l.Rating, l.LinkTexts, l.Openings, l.OneOff, l.LastValidated)),
        _ => Snapshot(source, l => new Xivpf.Ad(l.Id, l.Description, l.ListedOn)),
    };

    // Listings known only from the marks have nothing the source sent, so the marks file alone keeps them
    private ListingFile Snapshot<T>(ListingSource source, Func<CachedListing, T> sent)
    {
        List<SavedListing<T>?> saved = listings.TryGetValue(source, out var bySource)
            ? [.. bySource.Values.Where(l => l.Recorded).Select(l => new SavedListing<T> { LastSeen = l.LastSeen, Sent = sent(l) })]
            : [];
        return new ListingFile(SourceFile(source), null, () => Serialise(new SavedSource<T> { Listings = saved }));
    }

    private static string Serialise<T>(T file) => JsonSerializer.Serialize(file, JsonOptions);

    /// <summary>
    /// Reads the saved files into a new cache: each source's file, the fetch times, then the marks,
    /// from the backup if the marks file is missing or unreadable. A file that is unreadable or from
    /// a later version is reported and left out, so its part starts empty. Loading counts as no change.
    /// </summary>
    /// <param name="read">A file's text by its path (<see cref="MarksFile"/>, ...), or null if there is none.</param>
    /// <param name="unreadable">Told of each file left out, with why.</param>
    public static ListingCache Load(Func<string, string?> read, Action<string, JsonException> unreadable)
    {
        var cache = new ListingCache();
        foreach (var source in Enum.GetValues<ListingSource>())
            TryRead(read, SourceFile(source), unreadable, json => cache.LoadSource(source, json));
        TryRead(read, FetchTimesFile, unreadable, cache.LoadFetchTimes);
        if (!TryRead(read, MarksFile, unreadable, cache.LoadMarks))
            TryRead(read, MarksBackupFile, unreadable, cache.LoadMarks);

        cache.changedSources.Clear();
        cache.fetchTimesChanged = false;
        cache.marksChanged = false;
        return cache;
    }

    private static bool TryRead(Func<string, string?> read, string path, Action<string, JsonException> unreadable, Action<string> load)
    {
        if (read(path) is not { } json)
            return false;
        try
        {
            load(json);
            return true;
        }
        catch (JsonException e)
        {
            unreadable(path, e);
            return false;
        }
    }

    // The whole file is read before any listing is added, so an unreadable one adds none
    private void LoadSource(ListingSource source, string json)
    {
        switch (source)
        {
            case ListingSource.InGamePartyFinder:
                foreach (var (ad, seen) in Read<Xivpf.Ad>(json))
                    SeenAd(source, ad.Id, ad.Description ?? "", ad.ListedOn, seen);
                break;
            case ListingSource.Xivpf:
                foreach (var (ad, seen) in Read<Xivpf.Ad>(json))
                    Xivpf.Record(this, [ad with { Description = ad.Description ?? "" }], seen);
                break;
            case ListingSource.FfxivVenues:
                foreach (var (venue, seen) in Read<FfxivVenues.Venue>(json))
                    FfxivVenues.Record(this, [venue], seen);
                break;
            case ListingSource.Partake:
                foreach (var (item, seen) in Read<Partake.Event>(json))
                    Partake.Record(this, [item with { Title = item.Title ?? "", World = item.World ?? "", LinkTexts = Present(item.LinkTexts) }], seen);
                break;
            case ListingSource.RpEventCalendar:
                foreach (var (item, seen) in Read<RpEventCalendar.Event>(json))
                {
                    var openings = (item.Openings ?? []).Where(o => o is not null && o.End > o.Start);
                    RpEventCalendar.Record(this, [item with { LinkTexts = Present(item.LinkTexts), Openings = [.. openings] }], seen);
                }

                break;
        }
    }

    // JSON can hold nulls where the types say there are none, so those are dropped or made empty
    private static IReadOnlyList<string> Present(IReadOnlyList<string?>? texts) => [.. (texts ?? []).OfType<string>()];

    /// <summary>The listings in a source's file that have an ID, with when each was last seen.</summary>
    /// <exception cref="JsonException">The text is not a source's file, or is from a later version.</exception>
    private static List<(T Sent, DateTimeOffset LastSeen)> Read<T>(string json)
        where T : class
    {
        var file = Deserialise<SavedSource<T>>(json);
        var read = new List<(T, DateTimeOffset)>();
        foreach (var saved in file?.Listings ?? [])
        {
            if (saved?.Sent is { } sent && !string.IsNullOrEmpty(IdOf(sent)))
                read.Add((sent, saved.LastSeen));
        }

        return read;
    }

    private static string? IdOf(object sent) => sent switch
    {
        Xivpf.Ad ad => ad.Id,
        FfxivVenues.Venue venue => venue.Id,
        Partake.Event item => item.Id,
        RpEventCalendar.Event item => item.Id,
        _ => null,
    };

    /// <summary>
    /// Fetch times for sources this version doesn't know, that are never fetched, or saved for every
    /// region when the source is now fetched by data centre (or the other way round), are dropped.
    /// </summary>
    private void LoadFetchTimes(string json)
    {
        foreach (var (key, time) in Deserialise<SavedFetchTimes>(json)?.LastFetched ?? [])
        {
            var slash = key.IndexOf('/', StringComparison.Ordinal);
            var dataCentre = slash < 0 ? "" : key[(slash + 1)..];
            if (KnownSource(slash < 0 ? key : key[..slash]) is { } source && RefreshPolicy.Minimum(source) is not null
                && RefreshPolicy.ByDataCentre(source) == (dataCentre.Length > 0))
                lastFetched[(source, dataCentre)] = time;
        }
    }

    /// <summary>A fetch time's key in the file: the source's name, then <c>/</c> and the data centre for a source fetched by data centre.</summary>
    private static string FetchTimeKey(ListingSource source, string dataCentre) =>
        dataCentre.Length == 0 ? source.ToString() : $"{source}/{dataCentre}";

    /// <summary>
    /// Marks the listings read from the source files, titling those without a title as the marks file
    /// says. A mark whose listing they don't hold adds it, so the hidden list still shows it; it was
    /// last seen at no known time, so a marked one-off expires.
    /// </summary>
    private void LoadMarks(string json)
    {
        foreach (var mark in Deserialise<SavedMarks>(json)?.Marks ?? [])
        {
            if (mark is null || string.IsNullOrEmpty(mark.Id) || KnownSource(mark.Source) is not { } source)
                continue;

            var bySource = Of(source);
            if (!bySource.TryGetValue(mark.Id, out var listing))
            {
                listing = new CachedListing { Source = source, Id = mark.Id, Title = mark.Title ?? "", OneOff = mark.OneOff, Owner = this };
                bySource.Add(mark.Id, listing);
            }

            // An ad saved without its text has its title only here
            if (listing.Title.Length == 0)
                listing.Title = mark.Title ?? "";
            listing.Favourite = mark.Favourite;
            listing.Visited = mark.Visited;
            listing.Hidden = mark.Hidden;
        }
    }

    // Only a source's name: a number would follow a renumbering
    private static ListingSource? KnownSource(string? name) =>
        Enum.TryParse<ListingSource>(name, out var source) && Enum.IsDefined(source) && source.ToString() == name ? source : null;

    /// <exception cref="JsonException">The text is not the expected file, or is from a later version.</exception>
    private static T? Deserialise<T>(string json)
        where T : SavedFile
    {
        var file = JsonSerializer.Deserialize<T>(json, JsonOptions);
        if (file?.Version > FileVersion)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new JsonException($"The file is from a later version of Jaunt ({file.Version})");
        return file;
    }

    private abstract class SavedFile
    {
        public int Version { get; set; } = FileVersion;
    }

    private sealed class SavedSource<T> : SavedFile
    {
        public List<SavedListing<T>?>? Listings { get; set; }
    }

    private sealed class SavedListing<T>
    {
        public DateTimeOffset LastSeen { get; set; }
        public T? Sent { get; set; }
    }

    private sealed class SavedFetchTimes : SavedFile
    {
        public Dictionary<string, DateTimeOffset>? LastFetched { get; set; }
    }

    private sealed class SavedMarks : SavedFile
    {
        public List<SavedMark?>? Marks { get; set; }
    }

    private sealed class SavedMark
    {
        public string? Source { get; set; }
        public string? Id { get; set; }
        public string? Title { get; set; }
        public bool OneOff { get; set; }
        public bool Favourite { get; set; }
        public bool Visited { get; set; }
        public bool Hidden { get; set; }
    }
}
