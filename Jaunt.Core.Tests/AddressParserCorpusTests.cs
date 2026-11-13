using System.Text.Json;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>Every ad in the labelled corpus parses to exactly its labelled addresses.</summary>
public class AddressParserCorpusTests
{
    // Ads the parser gets wrong, each with the reason. Keep this list short.
    private static readonly Dictionary<string, string> KnownMisses = [];

    private static readonly Dictionary<string, (string Text, string ListedOn, string[] Expected)> Ads = Load();

    private static Dictionary<string, (string, string, string[])> Load()
    {
        var ads = new Dictionary<string, (string, string, string[])>();
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "pf-listings.jsonl");
        foreach (var line in File.ReadLines(path).Where(l => l.Length > 0))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var listedOn = root.GetProperty("listedOn")[0].GetString()!;
            var expected = root.GetProperty("labels").GetProperty("addresses").EnumerateArray().Select(a => FromLabel(a, listedOn)).ToArray();
            ads[root.GetProperty("id").GetString()!] = (root.GetProperty("text").GetString()!, listedOn, expected);
        }

        return ads;
    }

    public static TheoryData<string> AdIds() => [.. Ads.Keys];

    [Theory]
    [MemberData(nameof(AdIds))]
    public void ParsesTheLabelledAddresses(string id)
    {
        var (text, listedOn, expected) = Ads[id];
        var actual = TestGazetteer.Parser.Parse(text, listedOn).Select(Describe).ToArray();

        if (KnownMisses.ContainsKey(id))
            Assert.False(expected.SequenceEqual(actual), $"{id} now parses correctly: remove it from KnownMisses");
        else
            Assert.Equal(expected, actual);
    }

    private static string FromLabel(JsonElement a, string listedOn)
    {
        var fromText = a.GetProperty("worldFrom").GetString() == "text";
        var district = a.GetProperty("district").GetString() is { } d ? Enum.Parse<District>(d.Replace(" ", "", StringComparison.Ordinal)) : (District?)null;
        return Describe(new HousingAddress(
            fromText ? a.GetProperty("world").GetString() : listedOn,
            fromText ? WorldSource.Text : WorldSource.Listing,
            district,
            a.GetProperty("ward").GetInt32(),
            Int(a.GetProperty("plot")),
            a.GetProperty("subdivision") is { ValueKind: JsonValueKind.True or JsonValueKind.False } s ? s.GetBoolean() : null,
            Int(a.GetProperty("apartment")),
            a.GetProperty("room").GetString()));
    }

    private static int? Int(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null;

    private static string Describe(HousingAddress a) => a.ToString();
}
