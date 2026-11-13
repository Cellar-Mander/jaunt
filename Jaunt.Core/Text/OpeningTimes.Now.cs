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
            var prev = At(tokens, i - 1);
            var next = At(tokens, i + 1);
            if (IsWord(tokens[i], "now")
                && !(IsWord(prev, "starts") || IsWord(prev, "start"))
                && !IsWordIn(next, NotNowAfterNow))
            {
                return true;
            }

            if (!IsWord(tokens[i], "open"))
                continue;

            var whenFollows = next is { Kind: Kind.Word } && next.Value.Text.ToLowerInvariant() is "from" or "tonight" or "tonite" or "today" or "tomorrow";
            if (whenFollows)
                continue;

            if (IsWord(prev, "is"))
                return true;
            if (IsWord(prev, "always") || spanLast.Contains(i - 1))
                continue;

            var j = i + 1;
            if (IsSymbol(next, '@'))
                j++;
            if (spanFirst.Contains(j))
                continue;
            if (IsWordIn(next, NotNowAfterOpen))
                continue;
            if (IsSymbol(next, ':'))
                continue;
            if (next is { Kind: Kind.Number, Text: "24" })
                continue;
            return true;
        }

        return false;
    }
}
