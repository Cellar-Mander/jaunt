using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>
/// Whether an ad recruits: for a free company, linkshell or venue staff. The listing browser drops
/// such an ad unless it also states an opening. The rules are in <c>docs/design.md</c> under
/// "Recruitment ads".
/// </summary>
public static class Recruitment
{
    // Whole words in lower case; a word ending in "s" also matches without it
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "recruiting", "recruitment", "recruit", "hiring", "apply", "application", "staff", "member",
    };

    // Groups a player joins; with "join" anywhere in the text the ad recruits ("Join Silly Gang FC")
    private static readonly HashSet<string> Groups = new(StringComparer.Ordinal) { "fc", "cwls", "linkshell", "ls" };

    // Matched as substrings. A bare 募集 is too wide: Party Finder ads use it for the party's own slots
    // ("募集ロール", "募集期限").
    private static readonly string[] Japanese = ["メンバー募集", "スタッフ募集", "キャスト募集", "募集中"];

    /// <summary>
    /// Whether the listing browser drops an ad: it recruits and states no opening (no time, day, "open
    /// now" or 24/7), as read by <see cref="Browser.Schedules.ParseAd"/>.
    /// </summary>
    public static bool OnlyRecruits(ListingDetails details, IReadOnlyList<OpeningTime> times) =>
        details.Recruits && times.Count == 0;

    /// <summary>Whether an ad's text, as the game sends it, recruits.</summary>
    public static bool Recruits(string adText) => adText.Length > 0 && Recruits(PartyFinderText.Normalise(adText));

    /// <summary>Whether an ad recruits, from its text already normalised (<see cref="PartyFinderText.Normalise(string)"/>).</summary>
    public static bool Recruits(NormalisedText normalised)
    {
        var text = normalised.Text.ToLowerInvariant();
        foreach (var keyword in Japanese)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
                return true;
        }

        bool group = text.Contains("free company", StringComparison.Ordinal), join = false;
        foreach (var word in ListingTags.Split(text))
        {
            // For the word "s" the rest is empty, which is no keyword
            if (Words.Contains(word) || (word[^1] == 's' && Words.Contains(word[..^1])))
                return true;
            group |= Groups.Contains(word);
            join |= word == "join";
        }

        return group && join;
    }
}
