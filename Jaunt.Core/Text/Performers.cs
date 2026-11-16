namespace Jaunt.Core.Text;

/// <summary>A performer a Party Finder ad names: a DJ, bard or act.</summary>
/// <param name="Name">
/// The name as written, glyphs decoded and a leading "DJ" marker dropped ("DJ Damsca" is "Damsca",
/// but "DJKhaosVoid" and "djayYAMS" stay whole); null when the ad only links the performer's stream.
/// </param>
/// <param name="Twitch">The performer's Twitch channel, from a link in the ad, or null.</param>
public sealed record Performer(string? Name, WebLink? Twitch)
{
    /// <summary>What to show for the performer: the name, else the Twitch handle.</summary>
    public string Shown => Name ?? Performers.HandleOf(Twitch!.Uri)!;
}

/// <summary>
/// Finds the performers a Party Finder ad names: names after a marker ("DJ", "DJs:", "Music by",
/// "presenting", "Bard Night |"), and Twitch channels. Names without a marker are left out, as they
/// may be hosts or a birthday. The rules are in <c>docs/design.md</c> under "Performers in ad text".
/// </summary>
/// <remarks>
/// One pass over the normalised text, with no regex. Each name list is read once and the scan
/// carries on after it, so the cost stays linear in the text's length.
/// </remarks>
public static class Performers
{
    /// <summary>Words that end a name or are never one: amenities, times and link words.</summary>
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "live", "now", "and", "an", "more", "from", "tonight", "today", "open", "gamba", "tarot", "raffle", "steam",
        "giveaway", "giveaways", "auctions", "bar", "courts", "bingo", "drt", "dancers", "rp", "vibe", "at", "the", "on",
        "with", "w", "bj", "blackjack", "slots", "games", "prizes", "dance", "chat", "night", "afterparty", "in", "of",
        "for", "x", "dj", "djs", "dj's",
    };

    /// <summary>Lower-case words that may join the words of one name ("Crying in Colour").</summary>
    private static readonly HashSet<string> Joining = new(StringComparer.Ordinal) { "in", "of", "the", "de" };

    /// <summary>Endings that make a number-led word an amount, time or date ("10mil", "11pm", "2nd"), not a name.</summary>
    private static readonly HashSet<string> Units = new(StringComparer.OrdinalIgnoreCase) { "mil", "pm", "am", "st", "nd", "rd", "th" };

    private const int MaxNameWords = 3;

    private enum Separator { And, SpacedDash, Comma, Slash, Times, Bullet, Dash }

    /// <summary>Finds the performers in one ad: the names in the order written, then Twitch channels no name claims.</summary>
    /// <param name="adText">The ad's text as the game sends it, glyphs included.</param>
    /// <param name="links">The ad's links, as <see cref="WebLinks.Find"/> reads them.</param>
    public static IReadOnlyList<Performer> Find(string adText, IReadOnlyList<WebLink> links)
    {
        var text = PartyFinderText.Normalise(adText, markDecoration: true).Text;
        var names = new List<string>();
        var i = 0;
        while (i < text.Length)
            i = Math.Max(i + 1, AtWordStart(text, i) ? ReadAt(text, i, names) : 0);

        var performers = new List<Performer>();
        foreach (var name in names)
        {
            if (!performers.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                performers.Add(new Performer(name, null));
        }

        foreach (var link in links)
        {
            if (HandleOf(link.Uri) is not { } handle
                || performers.Exists(p => p.Twitch is not null && string.Equals(HandleOf(p.Twitch.Uri), handle, StringComparison.OrdinalIgnoreCase)))
                continue;
            var tied = performers.FindIndex(p => p.Twitch is null && p.Name is not null && Ties(p.Name, handle, text, link));
            if (tied >= 0)
                performers[tied] = performers[tied] with { Twitch = link };
            else
                performers.Add(new Performer(null, link));
        }

        return performers;
    }

    /// <summary>
    /// The performers of several ads shown as one entry, each once, in the order first named: a name
    /// once whatever its case, with the first stream any ad ties to it, and a stream with no name
    /// only when no named performer has it.
    /// </summary>
    public static IReadOnlyList<Performer> Merge(IEnumerable<IReadOnlyList<Performer>> lists)
    {
        var merged = new List<Performer>();
        foreach (var performer in lists.SelectMany(l => l))
        {
            var same = performer.Name is { } name
                ? merged.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                : -1;
            if (same >= 0)
            {
                if (merged[same].Twitch is null && performer.Twitch is not null)
                    merged[same] = merged[same] with { Twitch = performer.Twitch };
            }
            else
            {
                merged.Add(performer);
            }
        }

        // A stream shown on its own goes once a performer carries it, and is listed once
        var streams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in merged)
        {
            if (p.Name is not null && p.Twitch is not null)
                streams.Add(HandleOf(p.Twitch.Uri)!);
        }

        return merged.FindAll(p => p.Name is not null || streams.Add(HandleOf(p.Twitch!.Uri)!));
    }

    /// <summary>The channel a twitch.tv link points to, or null for any other link.</summary>
    public static string? HandleOf(Uri uri)
    {
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (!host.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase))
            return null;
        var path = uri.AbsolutePath.AsSpan(1);
        var length = 0;
        while (length < path.Length && path[length] != '/')
        {
            if (!char.IsAsciiLetterOrDigit(path[length]) && path[length] != '_')
                return null;
            length++;
        }

        return length > 0 ? path[..length].ToString() : null;
    }

    /// <summary>
    /// Whether a stream is the named performer's: the handle contains the name ("XIANA",
    /// "xianaforever"), or the ad writes the name just before it ("DJ Saber @ twitch.tv/0xsabrieln").
    /// </summary>
    private static bool Ties(string name, string handle, string text, WebLink link)
    {
        if (handle.Contains(name.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            return true;
        var at = text.IndexOf(link.Text, StringComparison.Ordinal);
        var p = SkipSpacesBack(text, at);
        if (p <= 0 || text[p - 1] != '@')
            return false;
        var wordEnd = SkipSpacesBack(text, p - 1);
        var wordStart = wordEnd;
        while (wordStart > 0 && IsWordChar(text[wordStart - 1]))
            wordStart--;
        var last = name.LastIndexOf(' ') + 1;
        return text.AsSpan(wordStart, wordEnd - wordStart).Equals(name.AsSpan(last), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads what starts at a word start: a name list after a marker, or a byline. Returns where to
    /// carry on, or 0 to go on with the next character.
    /// </summary>
    private static int ReadAt(string text, int i, List<string> names)
    {
        var rest = text.AsSpan(i);
        // "DJKhaosVoid", "DJay Nightshade": the DJ is part of the name.
        if ((rest.StartsWith("DJ", StringComparison.Ordinal) && rest.Length > 3 && char.IsAsciiLetterUpper(rest[2]) && char.IsAsciiLetterLower(rest[3]))
            || IsDjayName(rest))
            return ReadList(text, i, plural: false, names);
        var byline = Byline(rest);
        if (byline > 0)
        {
            names.Add(text.Substring(i, byline));
            return 0; // a byline is all letters, so no marker starts inside it
        }

        if (Marker(rest) is not { } marker)
            return 0;
        var (length, plural) = marker;
        var p = i + length;
        p = SkipSpaces(text, p);
        var colon = p < text.Length && text[p] == ':';
        if (colon || (p + 1 < text.Length && text[p] == '-' && text[p + 1] != ' '))
            p = SkipSpaces(text, p + 1);

        // "LIVE DJs", "7 DJs": DJs as an amenity, unless a colon lists them ("LIVE DJs: Erebus • Velo")
        if (!colon && AfterLiveOrNumber(text, i))
            return p;
        var after = text.AsSpan(p);
        if (after.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || after.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || after.StartsWith("twitch", StringComparison.OrdinalIgnoreCase))
            return p; // "DJ: twitch.tv/x": the link names them
        return ReadList(text, p, plural || colon, names);
    }

    /// <summary>"DJay " or "Djay " before a capitalised word, which is all one name.</summary>
    /// <remarks>The text is trimmed, so a space is never last and <c>s[5]</c> is always there.</remarks>
    private static bool IsDjayName(ReadOnlySpan<char> s) =>
        s.StartsWith("djay ", StringComparison.OrdinalIgnoreCase) && char.IsAsciiLetterUpper(s[5]);

    /// <summary>The length of "djayYAMS" in "djayYAMS @ VENUE", or 0: a dj-led handle used as a byline.</summary>
    private static int Byline(ReadOnlySpan<char> s)
    {
        if (!s.StartsWith("dj", StringComparison.Ordinal))
            return 0;
        var p = 2;
        while (p < s.Length && char.IsAsciiLetterLower(s[p]))
            p++;
        if (p >= s.Length || !char.IsAsciiLetterUpper(s[p]))
            return 0;
        var end = p;
        while (end < s.Length && char.IsAsciiLetter(s[end]))
            end++;
        if (end - p < 2)
            return 0;
        var q = end;
        while (q < s.Length && s[q] == ' ')
            q++;
        return q < s.Length && s[q] == '@' ? end : 0;
    }

    /// <summary>A marker a name list follows, its length and whether it names several; null if none starts here.</summary>
    private static (int Length, bool Plural)? Marker(ReadOnlySpan<char> s)
    {
        if (s.StartsWith("dj's", StringComparison.OrdinalIgnoreCase) && !LetterAt(s, 4))
            return (4, true);
        if (s.StartsWith("djs", StringComparison.OrdinalIgnoreCase) && !LetterAt(s, 3))
            return (3, true);
        if (s.StartsWith("dj", StringComparison.OrdinalIgnoreCase) && !LetterAt(s, 2))
            return (2, false);
        if (s.StartsWith("music by", StringComparison.OrdinalIgnoreCase) && !LetterAt(s, 8))
            return (8, true);
        if (s.StartsWith("presenting", StringComparison.OrdinalIgnoreCase) && !LetterAt(s, 10))
            return (10, true);
        if (s.StartsWith("bard night", StringComparison.OrdinalIgnoreCase))
        {
            var p = 10;
            while (p < s.Length && s[p] == ' ')
                p++;
            if (p < s.Length && s[p] == '|')
                return (p + 1, true);
        }

        return null;
    }

    /// <summary>
    /// Reads a list of names from <paramref name="p"/>, adds them, and returns where it ends. A list
    /// keeps the separator it starts with ("A, B, C", "A/B/C", "A-B-C", "A x B"), and a final "&amp;"
    /// or "and" ends it ("A, B and C"). A comma list after a singular "DJ" must go on with names, so
    /// "DJ Damsca, Gamba, Tarot" names one. A list stops at a repeated "DJ" ("DJ Seikat, DJ
    /// Luckroll"), where the scan reads the next list.
    /// </summary>
    private static int ReadList(string text, int p, bool plural, List<string> names)
    {
        var items = new List<string>();
        var words = new List<string>();
        Separator? kind = null;
        while (true)
        {
            var after = TakeName(text, p, words);
            if (after == 0)
                break;
            p = after;
            var sep = MatchSeparator(text, p);
            var ends = sep is null || (kind is { } k && sep.Value.Kind != k && sep.Value.Kind != Separator.And)
                || (kind is null && sep.Value.Kind == Separator.Comma && !plural && !CommaListGoesOn(text, p + sep.Value.Length));
            if (ends)
            {
                // A name of several words that ends a list needs something after it that ends it: "Syren Mad Mist W6" is Syren.
                if (words.Count > 1 && !EndsName(text, p))
                {
                    if (kind == Separator.SpacedDash)
                    {
                        // "DJ REIKO BEING GOOBY - REMNANT LOUNGE [SIREN]": the dashes weren't a list
                        items.RemoveRange(1, items.Count - 1);
                        items[0] = FirstWord(items[0]);
                        break;
                    }

                    words.RemoveRange(1, words.Count - 1);
                }

                items.Add(string.Join(' ', words));
                break;
            }

            var (sepKind, sepLength) = sep!.Value;
            kind ??= sepKind;
            items.Add(string.Join(' ', words));
            p += sepLength;
            if (sepKind == Separator.And && kind != Separator.And)
            {
                // "A, B and C": C is the last
                var last = TakeName(text, p, words);
                if (last > 0)
                {
                    if (!EndsName(text, last))
                        words.RemoveRange(1, words.Count - 1);
                    items.Add(string.Join(' ', words));
                    p = last;
                }

                // Stryker disable once Statement : going on, the next name would start where this one ended, at a character no word starts with, so the loop stops there anyway
                break;
            }
        }

        names.AddRange(items);
        return p;
    }

    /// <summary>
    /// Reads one name of up to <see cref="MaxNameWords"/> words into <paramref name="words"/>: any
    /// word first, then capitalised ones, with "in", "of", "the" or "de" allowed between. Returns
    /// where it ends, or 0 when no name starts here.
    /// </summary>
    private static int TakeName(string text, int p, List<string> words)
    {
        words.Clear();
        var end = WordEnd(text, p);
        if (end == 0 || IsNotName(text, p, end))
            return 0;
        words.Add(text[p..end]);
        while (words.Count < MaxNameWords && end < text.Length && text[end] == ' ')
        {
            var start = end + 1;
            string? joining = null;
            var smallEnd = WordEnd(text, start);
            if (smallEnd > 0 && Joining.Contains(text[start..smallEnd]) && smallEnd < text.Length && text[smallEnd] == ' ')
            {
                joining = text[start..smallEnd];
                start = smallEnd + 1;
            }

            // The text is trimmed, so a space is never last and text[start] is always there
            if (!char.IsAsciiLetterUpper(text[start]))
                break;
            var next = WordEnd(text, start);
            if (NotNames.Contains(text[start..next]))
                break;
            if (joining is not null)
                words.Add(joining);
            words.Add(text[start..next]);
            end = next;
        }

        return end;
    }

    /// <summary>Whether the word is never a name: an amenity, a link word, or an amount, time or date.</summary>
    private static bool IsNotName(string text, int start, int end)
    {
        var word = text[start..end];
        if (NotNames.Contains(word))
            return true;
        if (!char.IsAsciiDigit(word[0]))
            return false;
        // WordEnd puts letters after a word's digits, so the loop stops inside it
        var letters = 0;
        while (char.IsAsciiDigit(word[letters]))
            letters++;
        return Units.Contains(word[letters..]);
    }

    /// <summary>
    /// The end of a name-like word at <paramref name="p"/>, or 0: an ASCII letter then letters,
    /// digits, "_" or "'", or digits then at least two letters ("8bit").
    /// </summary>
    private static int WordEnd(string text, int p)
    {
        if (p >= text.Length)
            return 0;
        var q = p;
        if (char.IsAsciiDigit(text[q]))
        {
            while (q < text.Length && char.IsAsciiDigit(text[q]))
                q++;
            var letters = q;
            while (letters < text.Length && char.IsAsciiLetter(text[letters]))
                letters++;
            if (letters - q < 2)
                return 0;
            q = letters;
        }
        else if (!char.IsAsciiLetter(text[q]))
        {
            return 0;
        }

        while (q < text.Length && (IsWordChar(text[q]) || text[q] == '\''))
            q++;
        return q;
    }

    /// <summary>The separator after a name, if another name follows it.</summary>
    private static (Separator Kind, int Length)? MatchSeparator(string text, int p)
    {
        var spaced = SkipSpaces(text, p);
        (Separator, int)? found = null;
        var and = AndAt(text, p);
        if (and > 0)
            found = (Separator.And, and - p);
        // After a space, spaced is inside the text, as it is trimmed. A name never ends just before an
        // x or a dash, and a dash with a word after it has no space after it, so those need no checks.
        else if (spaced > p && text[spaced] == '-' && spaced + 1 < text.Length && text[spaced + 1] == ' ')
            found = (Separator.SpacedDash, SkipSpaces(text, spaced + 1) - p);
        else if (spaced < text.Length && text[spaced] is ',' or '/' or '•')
            found = (text[spaced] switch { ',' => Separator.Comma, '/' => Separator.Slash, _ => Separator.Bullet }, SkipSpaces(text, spaced + 1) - p);
        else if (spaced + 1 < text.Length && text[spaced] == 'x' && text[spaced + 1] == ' ')
            found = (Separator.Times, SkipSpaces(text, spaced + 1) - p);
        else if (p < text.Length && text[p] == '-')
            found = (Separator.Dash, 1);

        return found is { } f && WordEnd(text, p + f.Item2) > 0 ? f : null;
    }

    /// <summary>Where an "&amp;", "and" or ", and" at <paramref name="p"/> ends with the spaces after it, or 0.</summary>
    private static int AndAt(string text, int p)
    {
        var q = SkipSpaces(text, p);
        if (q < text.Length && text[q] == ',')
            q = SkipSpaces(text, q + 1);
        if (q < text.Length && text[q] == '&')
            return SkipSpaces(text, q + 1);
        // A name ends at a character that isn't part of a word, so "and" here is a word of its own
        if (text.AsSpan(q).StartsWith("and", StringComparison.OrdinalIgnoreCase) && !WordCharAt(text, q + 3))
            return SkipSpaces(text, q + 3);
        return 0;
    }

    /// <summary>Whether the comma list after a singular marker goes on with names: "Seikat,Luckroll, Stella".</summary>
    private static bool CommaListGoesOn(string text, int p)
    {
        var words = new List<string>();
        var end = TakeName(text, p, words);
        // Stryker disable once Boolean : going on, the list would read the same word, fail the same way and end with the same names, as a comma ends the name before it
        if (end == 0)
            return false;
        var q = SkipSpaces(text, end);
        return q < text.Length && (text[q] is ',' or '&' || (text.AsSpan(q).StartsWith("and", StringComparison.OrdinalIgnoreCase) && !WordCharAt(text, q + 3)));
    }

    /// <summary>
    /// Whether what follows a name ends it: the end of the text or line, a number, punctuation or
    /// decoration other than "-", "&amp;", "/" or an opening bracket, or "from", "at", "on" or "with".
    /// </summary>
    private static bool EndsName(string text, int p)
    {
        var q = SkipSpaces(text, p);
        if (q >= text.Length)
            return true;
        var c = text[q];
        if (char.IsDigit(c))
            return true;
        // A dash, & or / may join it to more ("Cynaxia Dyn-Raff"), and an opening bracket starts more
        // about the venue ("REMNANT LOUNGE [SIREN]"), so they don't end a name
        if (!(char.IsLetter(c) || c is '-' or '&' or '/')
            && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.OpenPunctuation)
            return true;
        foreach (var word in (string[])["from", "at", "on", "with"])
        {
            if (text.AsSpan(q).StartsWith(word, StringComparison.OrdinalIgnoreCase) && !WordCharAt(text, q + word.Length))
                return true;
        }

        return false;
    }

    /// <summary>Whether "live" or a number comes just before <paramref name="i"/>, with spaces between.</summary>
    private static bool AfterLiveOrNumber(string text, int i)
    {
        var p = SkipSpacesBack(text, i);
        if (p > 0 && char.IsAsciiDigit(text[p - 1]))
            return true;
        return p >= 4 && text.AsSpan(p - 4, 4).Equals("live", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstWord(string name) => name.Split(' ')[0];

    private static bool AtWordStart(string text, int i) => i == 0 || !char.IsAsciiLetterOrDigit(text[i - 1]);

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool WordCharAt(string text, int p) => p < text.Length && IsWordChar(text[p]);

    private static bool LetterAt(ReadOnlySpan<char> s, int p) => p < s.Length && char.IsLetter(s[p]);

    private static int SkipSpaces(string text, int p)
    {
        while (p < text.Length && text[p] == ' ')
            p++;
        return p;
    }

    private static int SkipSpacesBack(string text, int p)
    {
        while (p > 0 && text[p - 1] == ' ')
            p--;
        return p;
    }
}
