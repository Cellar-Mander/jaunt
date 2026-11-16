using System.Globalization;
using System.Net;
using Jaunt.Core.Text;

namespace Jaunt.Core.Images;

/// <summary>Finds the icons a website's home page names for itself.</summary>
public static class Favicons
{
    /// <summary>At most this many icon addresses are tried for one site.</summary>
    internal const int MaxTried = 3;

    /// <summary>
    /// The icon addresses to try for a site, best first: those its home page names in <c>&lt;link&gt;</c>
    /// tags (<c>apple-touch-icon</c>, made for home screens and large, then <c>icon</c> by its largest
    /// size), then <c>/favicon.ico</c>. SVG icons are left out, as they can't be decoded. Each address
    /// once, http or https only, at most <see cref="MaxTried"/>.
    /// </summary>
    /// <param name="html">The home page as sent, or null when it couldn't be read.</param>
    /// <param name="page">The page's address, which relative icon addresses are read against.</param>
    public static IReadOnlyList<Uri> Candidates(string? html, Uri page)
    {
        var found = new List<(Uri Uri, int Rank, int Size, int Order)>();
        var order = 0;
        // Stryker disable once String : with no page, any text without a link tag finds no icons either
        foreach (var attributes in LinkTags(html ?? ""))
        {
            // One condition per statement, so each out variable is assigned wherever it is read, as mutation testing needs
            if (!attributes.TryGetValue("rel", out var rel))
                continue;
            if (!attributes.TryGetValue("href", out var href))
                continue;
            var rels = rel.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rank = rels.Contains("apple-touch-icon", StringComparer.OrdinalIgnoreCase) || rels.Contains("apple-touch-icon-precomposed", StringComparer.OrdinalIgnoreCase)
                ? 0
                : rels.Contains("icon", StringComparer.OrdinalIgnoreCase) ? 1 : -1;
            // Stryker disable once String : with no type, any text without "svg" lets the icon through too
            if (rank < 0 || attributes.GetValueOrDefault("type", "").Contains("svg", StringComparison.OrdinalIgnoreCase))
                continue;
            // Stryker disable once Statement : an address that doesn't parse is null, which the next check skips too
            if (!Uri.TryCreate(page, WebUtility.HtmlDecode(href.Trim()), out var uri))
                continue;
            if (!WebLinks.IsWeb(uri) || uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                continue;
            var size = Largest(attributes.GetValueOrDefault("sizes"));
            found.Add((uri, rank, size, order++));
        }

        var candidates = new List<Uri>();
        foreach (var (uri, _, _, _) in found.OrderBy(f => f.Rank).ThenByDescending(f => f.Size).ThenBy(f => f.Order))
        {
            if (!candidates.Contains(uri))
                candidates.Add(uri);
        }

        var fallback = new Uri(page, "/favicon.ico");
        if (!candidates.Contains(fallback))
            candidates.Add(fallback);
        return candidates[..Math.Min(candidates.Count, MaxTried)];
    }

    /// <summary>The largest side in a <c>sizes</c> value ("16x16 32x32" gives 32); 0 when none is given, as for "any".</summary>
    internal static int Largest(string? sizes)
    {
        var largest = 0;
        // Stryker disable once String : with no sizes, any text without an "x" and digits before it gives 0 too
        foreach (var size in (sizes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var x = size.IndexOfAny(['x', 'X']);
            // Stryker disable once Equality : at 0 the width is empty, which doesn't parse, so it gives nothing either way
            if (x > 0 && int.TryParse(size[..x], NumberStyles.None, CultureInfo.InvariantCulture, out var width))
                largest = Math.Max(largest, width);
        }

        return largest;
    }

    /// <summary>
    /// The attributes of each <c>&lt;link&gt;</c> tag, names in lower case, in one pass over the page
    /// with no regex. Values may be quoted with either quote or bare.
    /// </summary>
    internal static IEnumerable<Dictionary<string, string>> LinkTags(string html)
    {
        var i = 0;
        while ((i = html.IndexOf("<link", i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            i += 5;
            if (i < html.Length && !char.IsWhiteSpace(html[i]))
                continue;

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            while (i < html.Length && html[i] != '>')
            {
                if (char.IsWhiteSpace(html[i]) || html[i] == '/')
                {
                    i++;
                    // Stryker disable once Statement : reading a name straight after finds none at a space or "/", and the next pass skips it the same way
                    continue;
                }

                var nameStart = i;
                while (i < html.Length && html[i] is not ('=' or '>' or '/') && !char.IsWhiteSpace(html[i]))
                    i++;
                var name = html[nameStart..i].ToLowerInvariant();
                while (i < html.Length && char.IsWhiteSpace(html[i]))
                    i++;
                var value = "";
                if (i < html.Length && html[i] == '=')
                {
                    i++;
                    while (i < html.Length && char.IsWhiteSpace(html[i]))
                        i++;
                    if (i < html.Length && html[i] is '"' or '\'')
                    {
                        var quote = html[i];
                        var end = html.IndexOf(quote, i + 1);
                        // Stryker disable once Equality : the search starts past the opening quote, so end is never 0
                        if (end < 0)
                            end = html.Length;
                        value = html[(i + 1)..end];
                        i = Math.Min(end + 1, html.Length);
                    }
                    else
                    {
                        var valueStart = i;
                        // A bare value ends at a space or the tag's end, "/>" included
                        while (i < html.Length && html[i] != '>' && !char.IsWhiteSpace(html[i])
                            && !(html[i] == '/' && i + 1 < html.Length && html[i + 1] == '>'))
                            i++;
                        value = html[valueStart..i];
                    }
                }

                // Stryker disable once Equality : an attribute with no name is never looked up, so keeping it changes nothing
                if (name.Length > 0)
                    attributes.TryAdd(name, value);
            }

            yield return attributes;
        }
    }
}
