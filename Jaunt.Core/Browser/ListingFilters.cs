using System.Text;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Browser;

/// <summary>The listing browser's search and filter chips, saved in the settings so they stay set when it is reopened.</summary>
/// <remarks>Interests are saved by number; <see cref="ConfigurationBase.Normalize"/> drops bits no interest uses.</remarks>
public sealed class BrowserFilters
{
    /// <summary>Hides listings <see cref="ListingTags"/> counts as NSFW. On by default.</summary>
    public bool SfwOnly { get; set; } = true;

    /// <summary>Lists only favourites.</summary>
    public bool Favourites { get; set; }

    public bool HideVisited { get; set; }

    /// <summary>Lists hidden venues too.</summary>
    public bool ShowHidden { get; set; }

    /// <summary>Interests a listing must have, all of them.</summary>
    public Interests Tags { get; set; }

    /// <summary>The search box's text (<see cref="SearchQuery"/>).</summary>
    public string Search { get; set; } = "";
}

/// <summary>One entry as the filters see it.</summary>
/// <param name="Tagging">The entry's interests and NSFW, its members' combined (<see cref="ListingTags.Combine"/>).</param>
/// <param name="SearchFields">The texts search looks in: its titles, ad text or description, and interest labels, each passed through <see cref="SearchQuery.Fold"/>.</param>
public readonly record struct FilterEntry(Tagging Tagging, bool Favourite, bool Visited, bool Hidden, IReadOnlyList<string> SearchFields);

/// <summary>Decides which entries the browser lists. See "Filters and search" under "Listing browser" in <c>docs/design.md</c>.</summary>
public static class ListingFilters
{
    /// <summary>Each interest with its label, in the order the Tags menu lists them.</summary>
    public static readonly IReadOnlyList<(Interests Interest, string Label)> Labels =
    [
        (Interests.Bars, "Bars"),
        (Interests.Cafes, "Cafes"),
        (Interests.Clubs, "Clubs"),
        (Interests.LiveShows, "Live shows"),
        (Interests.Games, "Games"),
        (Interests.Gambling, "Gambling"),
        (Interests.Roleplay, "Roleplay"),
        (Interests.Spa, "Spa"),
        (Interests.Art, "Art"),
        (Interests.Photography, "Photography"),
        (Interests.Tarot, "Tarot"),
        (Interests.Chill, "Chill"),
    ];

    /// <summary>Every interest's bit, for dropping unknown ones from saved settings.</summary>
    public static readonly Interests AllInterests = Labels.Aggregate(Interests.None, (all, label) => all | label.Interest);

    /// <summary>Whether the filters list the entry.</summary>
    /// <param name="query">The filters' <see cref="BrowserFilters.Search"/>, parsed.</param>
    public static bool Shows(BrowserFilters filters, SearchQuery query, FilterEntry entry)
    {
        if (entry.Hidden && !filters.ShowHidden)
            return false;
        if (filters.SfwOnly && entry.Tagging.Nsfw)
            return false;
        if (filters.Favourites && !entry.Favourite)
            return false;
        if (filters.HideVisited && entry.Visited)
            return false;
        if ((entry.Tagging.Interests & filters.Tags) != filters.Tags)
            return false;
        return query.Matches(entry.SearchFields);
    }

    /// <summary>The labels of the interests set, in <see cref="Labels"/> order.</summary>
    public static IEnumerable<string> LabelsOf(Interests interests) =>
        Labels.Where(l => (interests & l.Interest) != 0).Select(l => l.Label);
}

/// <summary>
/// The search box's text, parsed: space-separated terms must all match, <c>-term</c> excludes,
/// <c>"two words"</c> is a phrase and <c>-"two words"</c> excludes it. An unclosed quote runs to the
/// end; a lone <c>-</c> or empty quotes are ignored. Terms match anywhere in a field, ignoring case
/// and Latin accents (<see cref="Fold"/>).
/// </summary>
public sealed class SearchQuery
{
    public static readonly SearchQuery Empty = new([], []);

    private SearchQuery(IReadOnlyList<string> include, IReadOnlyList<string> exclude)
    {
        Include = include;
        Exclude = exclude;
    }

    /// <summary>Terms and phrases every match contains.</summary>
    public IReadOnlyList<string> Include { get; }

    /// <summary>Terms and phrases no match contains.</summary>
    public IReadOnlyList<string> Exclude { get; }

    public static SearchQuery Parse(string text)
    {
        var include = new List<string>();
        var exclude = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            var negated = text[i] == '-';
            if (negated)
                i++;

            string term;
            if (i < text.Length && text[i] == '"')
            {
                var close = text.IndexOf('"', i + 1);
                var end = close == -1 ? text.Length : close;
                term = text[(i + 1)..end].Trim();
                i = end + 1;
            }
            else
            {
                var end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                    end++;
                term = text[i..end];
                i = end;
            }

            if (term.Length > 0)
                (negated ? exclude : include).Add(Fold(term));
        }

        return new SearchQuery(include, exclude);
    }

    /// <summary>
    /// The text with Latin accents taken off, so "Café Ōkami" reads as "Cafe Okami" and a keyboard without
    /// them can still find it: combining diacritics (U+0300–U+036F) are dropped after decomposing, and
    /// ø, ł, đ, ħ, æ, œ and ß, which don't decompose, are spelt out. Other scripts are left alone (kana keep
    /// their dakuten), and the result is recomposed. Case is kept; matching ignores it.
    /// </summary>
    public static string Fold(string text)
    {
        if (Ascii.IsValid(text))
            return text;

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            switch (c)
            {
                case >= '̀' and <= 'ͯ':
                    break;
                case 'ø': folded.Append('o'); break;
                case 'Ø': folded.Append('O'); break;
                case 'ł': folded.Append('l'); break;
                case 'Ł': folded.Append('L'); break;
                case 'đ': folded.Append('d'); break;
                case 'Đ': folded.Append('D'); break;
                case 'ħ': folded.Append('h'); break;
                case 'Ħ': folded.Append('H'); break;
                case 'æ': folded.Append("ae"); break;
                case 'Æ': folded.Append("AE"); break;
                case 'œ': folded.Append("oe"); break;
                case 'Œ': folded.Append("OE"); break;
                case 'ß': folded.Append("ss"); break;
                default: folded.Append(c); break;
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Whether every included term is in some field and no excluded one is in any. The fields must already be <see cref="Fold"/>ed.</summary>
    public bool Matches(IReadOnlyList<string> fields)
    {
        foreach (var term in Include)
        {
            if (!InAny(fields, term))
                return false;
        }

        foreach (var term in Exclude)
        {
            if (InAny(fields, term))
                return false;
        }

        return true;
    }

    private static bool InAny(IReadOnlyList<string> fields, string term)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
