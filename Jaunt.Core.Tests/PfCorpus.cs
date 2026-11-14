using System.Text.Json;

namespace Jaunt.Core.Tests;

/// <summary>Reads the labelled Party Finder corpus (Corpus/pf-listings.jsonl, schema in Corpus/README.md).</summary>
internal static class PfCorpus
{
    /// <summary>
    /// Each ad's id and JSON object, one per non-empty line. The object's document is disposed when
    /// the next ad is read, so take what you need from it before moving on.
    /// </summary>
    public static IEnumerable<(string Id, JsonElement Root)> Ads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "pf-listings.jsonl");
        foreach (var line in File.ReadLines(path).Where(l => l.Length > 0))
        {
            using var doc = JsonDocument.Parse(line);
            yield return (doc.RootElement.GetProperty("id").GetString()!, doc.RootElement);
        }
    }
}
