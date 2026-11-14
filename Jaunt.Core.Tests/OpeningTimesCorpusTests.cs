using System.Globalization;
using System.Text.Json;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>Checks <see cref="OpeningTimes.Parse"/> against the hand-labelled times of every corpus ad.</summary>
public class OpeningTimesCorpusTests
{
    // Ads the parser reads differently from their labels, each with the reason. Keep this list
    // short: a new entry means a rule is probably wrong.
    private static readonly Dictionary<string, string> KnownMisses = new()
    {
        ["pf-0217"] = "\"Commissons open!\": a shop taking orders reads as open now",
        ["pf-0348"] = "\"Open for Visits\": a house to look round reads as open now, while \"Open for a limited time\" (pf-0345) is",
    };

    private sealed record Ad(string Text, DateOnly Seen, string[] Expected);

    private static readonly Dictionary<string, Ad> Ads = Load();

    private static Dictionary<string, Ad> Load()
    {
        var ads = new Dictionary<string, Ad>();
        foreach (var (id, root) in PfCorpus.Ads())
        {
            var seen = DateOnly.FromDateTime(root.GetProperty("firstSeen").GetDateTimeOffset().UtcDateTime);
            var expected = root.GetProperty("labels").GetProperty("times").EnumerateArray().Select(Describe).Order().ToArray();
            ads[id] = new Ad(root.GetProperty("text").GetString()!, seen, expected);
        }

        return ads;
    }

    private static string Describe(JsonElement t) => Describe(
        t.GetProperty("now").GetBoolean(),
        t.GetProperty("always").GetBoolean(),
        t.GetProperty("day").GetString(),
        t.GetProperty("start").GetString(),
        t.GetProperty("end").GetString(),
        t.GetProperty("zone").GetString());

    private static string Describe(OpeningTime t) => Describe(
        t.Now,
        t.Always,
        t.Day switch
        {
            OpeningDay.Today => "today",
            OpeningDay.Tomorrow => "tomorrow",
            OpeningDay.Weekday => t.Weekday!.Value.ToString()[..3].ToLowerInvariant(),
            OpeningDay.Date => t.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => null,
        },
        t.Start?.ToString("HH:mm", CultureInfo.InvariantCulture),
        t.End?.ToString("HH:mm", CultureInfo.InvariantCulture),
        t.Zone);

    private static string Describe(bool now, bool always, string? day, string? start, string? end, string? zone) =>
        $"now={now} always={always} day={day} {start}-{end} zone={zone}";

    public static TheoryData<string> AllAds() => [.. Ads.Keys];

    [Theory]
    [MemberData(nameof(AllAds))]
    public void ParsedTimesMatchTheLabels(string id)
    {
        var ad = Ads[id];
        var normalised = PartyFinderText.Normalise(ad.Text).Text;
        var actual = OpeningTimes.Parse(normalised, ad.Seen).Select(Describe).Order().ToArray();
        var matches = actual.SequenceEqual(ad.Expected);

        if (KnownMisses.ContainsKey(id))
        {
            Assert.False(matches, $"{id} now parses correctly: remove it from KnownMisses");
        }
        else
        {
            Assert.True(matches, $"{id}: \"{normalised}\"\n  expected: {string.Join(" ; ", ad.Expected)}\n  actual:   {string.Join(" ; ", actual)}");
        }
    }
}
