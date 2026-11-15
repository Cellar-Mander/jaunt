using System.Text;
using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>
/// A venue's or event's own description, cleaned when the source is read (<see cref="Clean"/>), for
/// About when no ad gives one. The rules are in <c>docs/design.md</c> under "Descriptions".
/// </summary>
/// <param name="Text">
/// Plain text: Markdown and HTML removed, line breaks kept, at most <see cref="MaxLength"/> characters
/// plus "…". Empty when the description is only images and links.
/// </param>
/// <param name="Cut">The description was longer than <see cref="MaxLength"/>, so the rest is on the listing's website.</param>
/// <param name="Invites">The Discord invites the description links to, each once, for the listing's links: Markdown links' first, then those in the text.</param>
public sealed record ListingAbout(string Text, bool Cut, IReadOnlyList<string> Invites)
{
    /// <summary>
    /// The longest text kept, in characters, before it is cut with "…". Longer than 99% of FFXIV
    /// Venues' descriptions, which are the longest of the sources once cleaned.
    /// </summary>
    public const int MaxLength = 1500;

    /// <summary>A cut ends at the last space after this much of <see cref="MaxLength"/>, else mid-word.</summary>
    private const int MinCut = MaxLength * 2 / 3;

    /// <summary>Discord usernames are at most this long.</summary>
    private const int MaxHandle = 32;

    /// <summary>
    /// Cleans a description as a source sends it. When nothing but images, links and symbols is left,
    /// the text is empty, so About stays empty, and the result is null unless it links to a Discord invite.
    /// </summary>
    /// <param name="written">The description as sent, Markdown and HTML included; null or blank gives null.</param>
    /// <param name="scrubHandles">Removes Discord handles ("@name", "name#1234"), for a source whose descriptions name contacts.</param>
    public static ListingAbout? Clean(string? written, bool scrubHandles = false)
    {
        if (string.IsNullOrWhiteSpace(written))
            return null;

        var targets = new List<string>();
        var text = new StringBuilder(written.Length);
        // Blank lines before the first text add nothing, as a break goes only between lines of text
        // Stryker disable once Boolean : the first line of text is never preceded by a break, so its starting value is never read
        var blank = false;
        foreach (var raw in written.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = Inline(Block(raw.Replace('\u00A0', ' ')), targets);
            if (scrubHandles)
                line = WithoutHandles(line);
            line = Spaced(line);
            if (line.Length == 0)
            {
                blank = true;
                continue;
            }

            if (text.Length > 0)
                text.Append(blank ? "\n\n" : "\n");
            text.Append(line);
            blank = false;
        }

        var cleaned = text.ToString();
        var invites = new List<string>();
        foreach (var link in targets.SelectMany(WebLinks.Find).Concat(WebLinks.Find(cleaned)))
        {
            var uri = link.Uri.AbsoluteUri;
            if (WebLinks.IsDiscordInvite(link.Uri) && !invites.Contains(uri))
                invites.Add(uri);
        }

        if (!HasWords(cleaned))
            return invites.Count == 0 ? null : new ListingAbout("", false, invites);
        return cleaned.Length <= MaxLength
            ? new ListingAbout(cleaned, false, invites)
            : new ListingAbout(Shorten(cleaned), true, invites);
    }

    /// <summary>The line without a leading heading, quote or rule marker; a list item's marker becomes "•".</summary>
    private static string Block(string line)
    {
        var text = line.TrimStart();
        if (text.Length >= 3 && text.TrimEnd().All(c => c is '-' or '*' or '_' or ' ') && text.Count(c => c != ' ') >= 3)
            return "";

        var hashes = 0;
        while (hashes < text.Length && text[hashes] == '#')
            hashes++;
        if (hashes is >= 1 and <= 6 && (hashes == text.Length || text[hashes] == ' '))
            return text[hashes..];
        if (text.StartsWith('>'))
            return text[1..];
        if (text.Length >= 2 && text[0] is '-' or '*' or '+' && text[1] == ' ')
            return "• " + text[2..];
        return text;
    }

    /// <summary>
    /// The line without images, HTML tags, emphasis markers or escapes, and with each Markdown link as
    /// its text; link targets are added to <paramref name="targets"/>.
    /// </summary>
    private static string Inline(string line, List<string> targets)
    {
        var text = new StringBuilder(line.Length);
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            var next = i + 1 < line.Length ? line[i + 1] : '\0';
            if (c == '!' && next == '[' && LinkEnd(line, i + 1) is var (_, imageEnd))
            {
                i = imageEnd;
            }
            else if (c == '[' && LinkEnd(line, i) is var (close, end))
            {
                targets.Add(line[(close + 2)..end]);
                text.Append(Inline(line[(i + 1)..close], targets));
                i = end;
            }
            else if (c == '<' && (char.IsAsciiLetter(next) || next == '/') && line.IndexOf('>', i) is var tagEnd and > 0)
            {
                // An autolink (<https://…>) keeps its address; any other tag becomes a space, as "<br>" parts words
                var inside = line[(i + 1)..tagEnd];
                text.Append(inside.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? inside : " ");
                i = tagEnd;
            }
            else if (c == '\\' && next is > ' ' and < '\u007F' && !char.IsAsciiLetterOrDigit(next))
            {
                text.Append(next);
                i++;
            }
            else if (c == '~' && next == '~')
            {
                // "**" and "__" need no case of their own: each of their marks is a lone one beside another mark
                i++;
            }
            else if (!Emphasis(line, i))
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// For a "[" at <paramref name="open"/>, the "](" closing its text and the ")" ending its target,
    /// on the same line; null when it isn't a Markdown link.
    /// </summary>
    private static (int Close, int End)? LinkEnd(string line, int open)
    {
        // Images may sit inside a link's text ("[![](a.png)](b)"), so brackets are counted
        var depth = 0;
        for (var i = open; i < line.Length; i++)
        {
            if (line[i] == '[')
            {
                depth++;
            }
            else if (line[i] == ']' && --depth == 0)
            {
                if (i + 1 >= line.Length || line[i + 1] != '(')
                    return null;
                var end = line.IndexOf(')', i + 2);
                // Stryker disable once Equality : the search starts past "](", so end is never 0
                return end < 0 ? null : (i, end);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a lone "*" or "_" marks emphasis: a "*" not standing between spaces ("5 * 3"), or an
    /// "_" not inside a word ("snake_case").
    /// </summary>
    private static bool Emphasis(string line, int i)
    {
        var before = i > 0 ? line[i - 1] : ' ';
        var after = i + 1 < line.Length ? line[i + 1] : ' ';
        return line[i] switch
        {
            '*' => !(char.IsWhiteSpace(before) && char.IsWhiteSpace(after)),
            '_' => !(char.IsLetterOrDigit(before) && char.IsLetterOrDigit(after)),
            _ => false,
        };
    }

    /// <summary>
    /// The line without Discord handles: "@" then a letter and up to 31 more letters, digits,
    /// "_" or ".", not after a letter, digit or "." (so e-mail addresses stay); and a name ending in
    /// "#" and four digits.
    /// </summary>
    internal static string WithoutHandles(string line)
    {
        var text = new StringBuilder(line.Length);
        for (var i = 0; i < line.Length; i++)
        {
            var before = i > 0 ? line[i - 1] : ' ';
            if (line[i] == '@' && !IsHandleChar(before) && i + 1 < line.Length && char.IsAsciiLetter(line[i + 1]))
            {
                var end = i + 1;
                while (end < line.Length && end - i <= MaxHandle && IsHandleChar(line[end]))
                    end++;
                i = end - 1;
                continue;
            }

            if (IsHandleChar(line[i]) && !IsHandleChar(before) && TagEnd(line, i) is { } tagEnd)
            {
                i = tagEnd - 1;
                continue;
            }

            text.Append(line[i]);
        }

        return text.ToString();
    }

    /// <summary>For a word starting at <paramref name="start"/>, the end of its "#1234" tag; null when it has none.</summary>
    private static int? TagEnd(string line, int start)
    {
        var hash = start;
        while (hash < line.Length && IsHandleChar(line[hash]))
            hash++;
        var end = hash + 5;
        if (hash >= line.Length || line[hash] != '#' || end > line.Length
            || line.AsSpan(hash + 1, 4).ContainsAnyExceptInRange('0', '9') || (end < line.Length && char.IsAsciiDigit(line[end])))
            return null;
        return end;
    }

    private static bool IsHandleChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.';

    /// <summary>The line trimmed, with each run of spaces or tabs as one space.</summary>
    private static string Spaced(string line)
    {
        var text = new StringBuilder(line.Length);
        foreach (var c in line)
        {
            if (c is ' ' or '\t')
            {
                if (text.Length > 0 && text[^1] != ' ')
                    text.Append(' ');
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Whether the text has a letter or digit outside its links.</summary>
    private static bool HasWords(string text)
    {
        var words = text;
        foreach (var link in WebLinks.Find(text))
            words = words.Replace(link.Text, "", StringComparison.Ordinal);
        return words.Any(char.IsLetterOrDigit);
    }

    /// <summary>The text cut to <see cref="MaxLength"/> characters, at a space where one is near the end, with "…".</summary>
    private static string Shorten(string text)
    {
        var cut = text[..MaxLength];
        var space = cut.LastIndexOfAny([' ', '\n']);
        if (space >= MinCut)
            cut = cut[..space];
        else if (char.IsHighSurrogate(cut[^1]))
            cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }
}
