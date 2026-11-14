using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

/// <summary>Saving and loading a cache in memory, as <c>ListingCacheStore</c> does on disk.</summary>
internal static class SavedFiles
{
    /// <summary>The files a save would write now, by path.</summary>
    public static Dictionary<string, string> Written(ListingCache cache) =>
        cache.TakeChanges().ToDictionary(file => file.Path, file => file.Json());

    /// <summary>Every file of the cache, by path.</summary>
    public static Dictionary<string, string> All(ListingCache cache)
    {
        cache.ChangedAll();
        return Written(cache);
    }

    /// <summary>Loads files by path; an unreadable one fails the test.</summary>
    public static ListingCache Load(IReadOnlyDictionary<string, string> files) =>
        ListingCache.Load(path => files.GetValueOrDefault(path), (path, e) => throw new InvalidOperationException($"{path} is unreadable", e));

    public static ListingCache RoundTrip(ListingCache cache) => Load(All(cache));
}
