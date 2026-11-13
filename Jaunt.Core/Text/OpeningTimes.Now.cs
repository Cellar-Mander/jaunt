namespace Jaunt.Core.Text;

public static partial class OpeningTimes
{
    /// <summary>Words after "open" that say what is open, not that the venue is.</summary>
    private static readonly HashSet<string> NotNowAfterOpen = new(StringComparer.OrdinalIgnoreCase)
    {
        "stage", "bar", "mic", "recruitment", "recruiting", "positions", "applications", "auditions", "at", "to",
    };

    /// <summary>Words after "now" that are about something other than opening.</summary>
    private static readonly HashSet<string> NotNowAfterNow = new(StringComparer.OrdinalIgnoreCase)
    {
        "hiring", "recruiting", "accepting",
    };

    private static readonly string[] JapaneseNow = ["営業中", "開店中", "オープン中", "オープンです", "開店して", "ただいま"];

    /// <summary>
    /// "open now", "now open", "is open", "NOW", 営業中. A bare "open" counts unless a time, "from",
    /// "tonight" and the like say when it opens, or a word after it says what is open ("Open Stage",
    /// "Open Recruitment", "open to all"). "starts now" and "now hiring" don't count.
    /// </summary>
    private static bool FindNow(string text, List<Token> tokens, List<Span> spans)
    {
        foreach (var phrase in JapaneseNow)
        {
            if (text.Contains(phrase, StringComparison.Ordinal))
                return true;
        }

        var spanFirst = spans.Select(s => s.First).ToHashSet();
        var spanLast = spans.Select(s => s.Last).ToHashSet();

        for (var i = 0; i < tokens.Count; i++)
        {
            if (IsWord(tokens[i], "now")
                && !(At(tokens, i - 1) is { } p && (IsWord(p, "starts") || IsWord(p, "start")))
                && !(At(tokens, i + 1) is { Kind: Kind.Word } n && NotNowAfterNow.Contains(n.Text)))
            {
                return true;
            }

            if (!IsWord(tokens[i], "open"))
                continue;

            var next = At(tokens, i + 1);
            var whenFollows = next is { Kind: Kind.Word } nw && nw.Text.ToLowerInvariant() is "from" or "tonight" or "tonite" or "today" or "tomorrow";
            if (whenFollows)
                continue;

            if (At(tokens, i - 1) is { } before && IsWord(before, "is"))
                return true;
            if (At(tokens, i - 1) is { } prev && (IsWord(prev, "always") || spanLast.Contains(i - 1)))
                continue;

            var j = i + 1;
            if (At(tokens, j) is { } at && IsSymbol(at, '@'))
                j++;
            if (spanFirst.Contains(j))
                continue;
            if (next is { Kind: Kind.Word } w && NotNowAfterOpen.Contains(w.Text))
                continue;
            if (next is { } colon && IsSymbol(colon, ':'))
                continue;
            if (next is { Kind: Kind.Number, Text: "24" })
                continue;
            return true;
        }

        return false;
    }
}
