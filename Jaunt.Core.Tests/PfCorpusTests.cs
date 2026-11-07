using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jaunt.Core.Tests;

/// <summary>
/// Keeps the labelled Party Finder corpus (Corpus/pf-listings.jsonl) self-consistent, so a hand
/// edit to an ad's text or labels, such as obscuring personal data, can't silently break a label
/// the parser tests rely on. The schema is described in Corpus/README.md.
/// </summary>
public partial class PfCorpusTests
{
    private static readonly string[] Kinds = ["venue", "event", "housing", "shop", "recruiting", "other"];
    private static readonly string[] Districts = ["Mist", "Lavender Beds", "Goblet", "Shirogane", "Empyreum"];

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex HourMinute();

    [GeneratedRegex(@"^(today|tomorrow|mon|tue|wed|thu|fri|sat|sun|\d{4}-\d{2}-\d{2})$")]
    private static partial Regex Day();

    private static List<JsonElement> Entries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "pf-listings.jsonl");
        return [.. File.ReadLines(path).Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement)];
    }

    [Fact]
    public void EveryAdIsDistinct()
    {
        // revert-check: not revert-sensitive (checks the corpus data, which ships with the tests, not product code)
        var entries = Entries();

        Assert.True(entries.Count >= 300, $"only {entries.Count} entries");
        Assert.Equal(entries.Count, entries.Select(e => e.GetProperty("id").GetString()).Distinct().Count());
        Assert.Equal(entries.Count, entries.Select(e => e.GetProperty("text").GetString()).Distinct().Count());
    }

    [Fact]
    public void LabelsAreConsistentWithTheirAd()
    {
        // revert-check: not revert-sensitive (checks the corpus data, which ships with the tests, not product code)
        var problems = Entries().SelectMany(Check).ToList();

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    private static IEnumerable<string> Check(JsonElement entry)
    {
        var id = entry.GetProperty("id").GetString();
        var text = entry.GetProperty("text").GetString()!;
        var labels = entry.GetProperty("labels");

        if (entry.GetProperty("listedOn").GetArrayLength() == 0)
            yield return $"{id}: listedOn is empty";
        if (!Kinds.Contains(labels.GetProperty("kind").GetString()))
            yield return $"{id}: unknown kind";

        foreach (var url in labels.GetProperty("urls").EnumerateArray())
        {
            if (!text.Contains(url.GetString()!, StringComparison.Ordinal))
                yield return $"{id}: URL {url} is not in the text";
        }

        foreach (var a in labels.GetProperty("addresses").EnumerateArray())
        {
            var fromText = a.GetProperty("world").ValueKind == JsonValueKind.String;
            if (a.GetProperty("worldFrom").GetString() != (fromText ? "text" : "listing"))
                yield return $"{id}: worldFrom does not match whether a world is given";
            if (a.GetProperty("district") is { ValueKind: JsonValueKind.String } d && !Districts.Contains(d.GetString()))
                yield return $"{id}: unknown district {d}";
            if (a.GetProperty("ward") is { ValueKind: JsonValueKind.Number } w && w.GetInt32() is < 1 or > 30)
                yield return $"{id}: ward {w} out of range";
            if (a.GetProperty("plot") is { ValueKind: JsonValueKind.Number } p && p.GetInt32() is < 1 or > 60)
                yield return $"{id}: plot {p} out of range";
        }

        foreach (var t in labels.GetProperty("times").EnumerateArray())
        {
            if (t.GetProperty("day") is { ValueKind: JsonValueKind.String } day && !Day().IsMatch(day.GetString()!))
                yield return $"{id}: day {day} is not a weekday, today, tomorrow or a date";
            foreach (var field in (string[])["start", "end"])
            {
                if (t.GetProperty(field) is { ValueKind: JsonValueKind.String } hm && !HourMinute().IsMatch(hm.GetString()!))
                    yield return $"{id}: {field} {hm} is not HH:MM";
            }
        }
    }
}
