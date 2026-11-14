namespace Jaunt.Core.Text;

/// <summary>A website link found in ad text.</summary>
/// <param name="Text">The link as the ad writes it.</param>
/// <param name="Uri">Where it points, always http or https: https when the ad gives no scheme.</param>
public sealed record WebLink(string Text, Uri Uri);

/// <summary>
/// Finds website links in Party Finder ad text, including ones written without <c>https://</c>
/// ("discord.gg/abc", "venue.carrd.co") and with a broken scheme ("https:www.twitch.tv/x"). The
/// rules are in <c>docs/design.md</c> under "Links in ad text".
/// </summary>
/// <remarks>One pass over the normalised text, with no regex, so the cost stays linear in its length.</remarks>
public static class WebLinks
{
    private const string UrlPunctuation = "-._~:/?#@%&=+";

    /// <summary>
    /// Endings a link without a scheme or "www." must have, so that "Lav.Beds" or "p.m." is not a
    /// link. A link with either counts whatever it ends in.
    /// </summary>
    private static readonly HashSet<string> BareEndings = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "net", "org", "co", "io", "me", "ee", "gg", "tv", "gd", "ly", "gl", "cc", "club", "site", "link",
        "xyz", "app", "dev", "page", "info", "bio", "live", "art", "us", "uk", "ca", "au", "nz", "eu", "de", "fr", "jp",
    };

    /// <summary>Finds every link in one ad, in the order written.</summary>
    /// <param name="adText">The ad's text as the game sends it, glyphs included.</param>
    public static IReadOnlyList<WebLink> Find(string adText)
    {
        var text = PartyFinderText.Normalise(adText).Text;
        var links = new List<WebLink>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && IsUrlChar(text[i]))
                continue;
            if (Read(text[start..i]) is { } link) // an empty run reads as no link
                links.Add(link);
            start = i + 1;
        }

        return links;
    }

    /// <summary>Whether a URI is an absolute http or https address, the only kind Jaunt opens.</summary>
    public static bool IsWeb(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static bool IsUrlChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || UrlPunctuation.Contains(c);

    /// <summary>Reads one run of URL characters as a link, or returns null when it isn't one.</summary>
    private static WebLink? Read(string run)
    {
        // Punctuation around a link belongs to the sentence: "(see venue.com)." or "Info: x.com".
        var first = 0;
        while (first < run.Length && !char.IsAsciiLetterOrDigit(run[first]))
            first++;
        var token = run[first..].TrimEnd('.', ':', '?');

        var (scheme, rest) = SplitScheme(token);
        var hostEnd = rest.IndexOfAny(['/', '?', '#']);
        // Stryker disable once Equality : a host starts with / only after a scheme ("https:/x"), and Uri rejects "https:///x" either way
        var host = hostEnd < 0 ? rest : rest[..hostEnd];
        if (host.Contains('@') || host.Contains(':'))
            return null; // an email address, a handle or a time, not a website

        var dot = host.LastIndexOf('.');
        // Stryker disable once Equality : a host starts with . only after a scheme ("https://.com"), and Uri rejects that either way
        if (dot <= 0)
            return null;
        if (dot == host.Length - 1)
            return null;
        if (scheme is null && !host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && !BareEndings.Contains(host[(dot + 1)..]))
            return null;

        return Uri.TryCreate($"{scheme ?? "https"}://{rest}", UriKind.Absolute, out var uri) ? new WebLink(token, uri) : null;
    }

    /// <summary>The http or https scheme a link starts with, if any, and the text after it.</summary>
    private static (string? Scheme, string AfterScheme) SplitScheme(string token)
    {
        foreach (var scheme in (string[])["https", "http"])
        {
            if (!token.StartsWith(scheme + ":", StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = token[(scheme.Length + 1)..];
            // "https:www.twitch.tv/x" leaves the slashes out.
            return (scheme, rest.StartsWith("//", StringComparison.Ordinal) ? rest[2..] : rest);
        }

        return (null, token);
    }
}
