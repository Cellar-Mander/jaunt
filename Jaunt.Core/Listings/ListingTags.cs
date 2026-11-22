using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>What a listing offers, for filtering. A listing usually has several.</summary>
/// <remarks>
/// Saved filters store these by number, so never change a member's value or give a removed
/// member's bit to a new one; renaming is safe.
/// </remarks>
[Flags]
public enum Interests
{
    None = 0,

    /// <summary>Clubs and DJs.</summary>
    Clubs = 1 << 0,

    /// <summary>Bars and taverns.</summary>
    Bars = 1 << 1,

    /// <summary>Cafes and food.</summary>
    Cafes = 1 << 2,

    /// <summary>Live music, open stage, comedy and theatre.</summary>
    LiveShows = 1 << 3,

    /// <summary>Games, contests and giveaways, other than gambling.</summary>
    Games = 1 << 4,

    /// <summary>Betting gil on chance: casino games, deathroll, raffles.</summary>
    Gambling = 1 << 11,

    Roleplay = 1 << 5,

    /// <summary>Spas and baths.</summary>
    Spa = 1 << 6,

    Art = 1 << 7,
    Photography = 1 << 8,
    Tarot = 1 << 9,

    /// <summary>Chill, AFK and open house, including venues open around the clock.</summary>
    Chill = 1 << 10,
}

/// <summary>A listing's interests and whether it counts as not safe for work.</summary>
public readonly record struct Tagging(Interests Interests, bool Nsfw);

/// <summary>
/// Tags listings with <see cref="Interests"/> from developer-maintained keyword lists and the
/// sources' own tags, and decides whether each counts as NSFW. The rules are in <c>docs/design.md</c>
/// under "Tags and SFW".
/// </summary>
/// <remarks>
/// Text is read in one pass into words, each looked up in a dictionary, plus one substring search
/// per Japanese keyword, so the cost stays linear in the text's length. No regex.
/// </remarks>
public static class ListingTags
{
    // Words and phrases (words separated by single spaces) in lower case, matched as whole words; a
    // word ending in "s" also matches without it. Keywords with Japanese characters are matched as
    // substrings, since Japanese has no spaces between words.
    internal static readonly (string Keyword, Interests Interests)[] Keywords =
    [
        .. Each(Interests.Clubs,
            "nightclub", "dj", "dance", "dancing", "dancer", "rave", "edm", "techno", "house music",
            "twitch tv", "disco", "party", "emo night", "emonight", "クラブ", "ダンス"),
        .. Each(Interests.Bars,
            "bar", "tavern", "pub", "saloon", "lounge", "inn", "drink", "cocktail", "brewery", "speakeasy", "booze",
            "バー", "酒場", "居酒屋", "酒処"),
        .. Each(Interests.Cafes,
            "cafe", "café", "coffee", "tea", "teahouse", "bakery", "restaurant", "food", "dining", "diner",
            "bistro", "dinner", "ramen", "maidcafe", "喫茶", "珈琲", "カフェ", "レストラン", "飲食店", "食べ物"),
        .. Each(Interests.LiveShows,
            "bard", "open stage", "open mic", "live music", "concert", "singer", "singing", "band", "karaoke",
            "performance", "performer", "musician", "rock show", "comedy", "comedian", "stand up", "standup",
            "improv", "theater", "theatre", "ライブ", "演奏"),
        .. Each(Interests.Games,
            "game", "triple triad", "truth or dare", "trivia", "mahjong", "bingo", "tournament", "fightclub",
            "fight club", "fight ring", "fight pit", "fight night", "fight bar", "fight den", "fighting club",
            "fighting ring", "fighting arena", "fighting venue", "sparring ring", "brawl", "brawling", "wrestling",
            "duel", "bloodsport", "deathmatch", "giveaway", "prize", "ビンゴ"),
        .. Each(Interests.Gambling,
            "gamba", "gambling", "gamble", "casino", "blackjack", "bj", "deathroll", "drt", "roulette", "poker",
            "holdem", "raffle", "50/50", "カジノ", "ギャンブル"),
        .. Each(Interests.Roleplay,
            "rp", "roleplay", "role play", "rper", "ic", "in character", "ロールプレイ"),
        .. Each(Interests.Spa,
            "spa", "bath", "bathhouse", "onsen", "sauna", "hot spring", "massage", "温泉", "銭湯", "風呂"),
        .. Each(Interests.Art,
            "art", "artist", "gallery", "exhibition", "painting", "drawing", "美術", "ギャラリー"),
        .. Each(Interests.Photography,
            "photo", "photography", "photographer", "photoshoot", "gpose", "screenshot", "撮影", "写真"),
        .. Each(Interests.Tarot,
            "tarot", "fortune", "fortune telling", "card reading", "占い", "タロット"),
        .. Each(Interests.Chill,
            "afk", "24/7", "chill", "chillout", "hangout", "hang out", "open house", "relax", "relaxing", "cozy",
            "cosy", "quiet", "library", "雑談", "読書", "見学", "まったり", "放置"),
    ];

    // "Club" counts as Clubs only when no "club" in the text follows one of these words, which name
    // other kinds of club: "The Wildwood Club ... social club" is a social club. A lone "s" between
    // them is an apostrophe's ("gentleman's club").
    private static readonly HashSet<string> OtherClubKinds = new(StringComparer.Ordinal)
    {
        "social", "host", "hostess", "gentleman", "gentlemen", "gentlemens", "strip", "book", "fight", "fighting",
        "supper", "members",
    };

    // The tags FFXIV Venues owners and Partake hosts choose from, matched ignoring case
    internal static readonly Dictionary<string, Interests> SourceTagInterests = new(StringComparer.OrdinalIgnoreCase)
    {
        // FFXIV Venues
        ["Nightclub"] = Interests.Clubs,
        ["Twitch DJ"] = Interests.Clubs,
        ["Sync DJ"] = Interests.Clubs,
        ["Dancers"] = Interests.Clubs,
        ["Bar"] = Interests.Bars,
        ["Drink"] = Interests.Bars,
        ["Tavern"] = Interests.Bars,
        ["Lounge"] = Interests.Bars,
        ["Inn"] = Interests.Bars,
        ["Cafe"] = Interests.Cafes,
        ["Food"] = Interests.Cafes,
        ["Restaurant"] = Interests.Cafes,
        ["Maid cafe"] = Interests.Cafes,
        ["Bards"] = Interests.LiveShows,
        ["Open stage"] = Interests.LiveShows,
        ["Performances"] = Interests.LiveShows,
        ["Shows and Performances"] = Interests.LiveShows,
        ["Gambling"] = Interests.Gambling,
        ["Casino"] = Interests.Gambling,
        ["Deathroll"] = Interests.Gambling,
        ["Blackjack"] = Interests.Gambling,
        ["Roulette"] = Interests.Gambling,
        ["Texas holdem"] = Interests.Gambling,
        ["Bingo"] = Interests.Games,
        ["Triple triad"] = Interests.Games,
        ["Truth or dare"] = Interests.Games,
        ["Giveaways"] = Interests.Games,
        ["Other Games"] = Interests.Games,
        ["Fightclub"] = Interests.Games,
        ["RP Heavy"] = Interests.Roleplay,
        ["IC RP Only"] = Interests.Roleplay,
        ["Bath house"] = Interests.Spa,
        ["Artists"] = Interests.Art,
        ["Photography"] = Interests.Photography,
        ["Tarot"] = Interests.Tarot,
        ["24/7 open house"] = Interests.Chill,
        ["Library"] = Interests.Chill,

        // Partake
        ["Nightlife & Entertainment"] = Interests.Clubs,
        ["Mature Nightlife"] = Interests.Clubs,
        ["Live DJs"] = Interests.Clubs,
        ["Bars & Lounges"] = Interests.Bars,
        ["Cafes & Dining"] = Interests.Cafes,
        ["Cafes, Taverns & Inns"] = Interests.Cafes | Interests.Bars,
        ["Live Performances"] = Interests.LiveShows,
        ["Performance & Shows"] = Interests.LiveShows,
        ["Gambling Available"] = Interests.Gambling,
        ["Casino & Gambling"] = Interests.Gambling,
        ["Includes giveaways"] = Interests.Games,
        ["Tournaments & Competitions"] = Interests.Games,
        ["Casual RP"] = Interests.Roleplay,
        ["Heavy RP"] = Interests.Roleplay,
        ["Fully IC"] = Interests.Roleplay,
        ["Mixed IC/OOC"] = Interests.Roleplay,
        ["Roleplay & Story"] = Interests.Roleplay,
        ["Spa & Relaxation"] = Interests.Spa,
        ["Creative & Showcase"] = Interests.Art,
        ["Photography Services"] = Interests.Photography,
        ["Community & Hangouts"] = Interests.Chill,
    };

    // Single words, and phrases by their first word with the rest of their words
    private static readonly Dictionary<string, Interests> Words = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<(string[] Tail, Interests Interests)>> Phrases = new(StringComparer.Ordinal);
    private static readonly List<(string Keyword, Interests Interests)> Substrings = [];

    // Words saying the listing is age-restricted, with the age. Japanese ones are matched as substrings.
    // Words starting "18+" or "21+" ("18+only") count too.
    private static readonly Dictionary<string, int> AgeWords = new(StringComparer.Ordinal) { ["+18"] = 18, ["+21"] = 21, ["r18"] = 18, ["r18+"] = 18 };
    private static readonly string[] AgePhrases = ["r 18", "adults only", "adult only"];
    private static readonly string[] AgeSubstrings = ["18禁", "成人向け"];

    static ListingTags()
    {
        foreach (var (keyword, interests) in Keywords)
        {
            // Stryker disable once Linq : every keyword is either all Japanese or has no Japanese
            if (keyword.Any(IsJapanese))
            {
                Substrings.Add((keyword, interests));
                // Stryker disable once Statement : a Japanese keyword has no spaces, so as a word it matches only a whole run of text, which the substring search already finds
                continue;
            }

            var words = keyword.Split(' ');
            if (words.Length == 1)
            {
                Words[keyword] = Words.GetValueOrDefault(keyword) | interests;
                // Stryker disable once Statement : a one-word keyword added as a phrase has no tail, so it matches exactly where the word does
                continue;
            }

            if (!Phrases.TryGetValue(words[0], out var list))
                Phrases[words[0]] = list = [];
            list.Add((words[1..], interests));
        }
    }

    /// <summary>The interests and SFW or NSFW of one cached listing, from the fields its source sent.</summary>
    public static Tagging Of(CachedListing listing)
    {
        switch (listing.Source)
        {
            case ListingSource.FfxivVenues:
                {
                    var fields = listing.Venue;
                    var interests = FromSourceTags(fields?.Tags) | Read(listing.Title).Interests;
                    return new Tagging(interests, fields?.Sfw == false);
                }

            case ListingSource.Partake:
                {
                    var text = Read(listing.Title);
                    var adult = string.Equals(listing.Rating?.Trim(), "ADULT", StringComparison.OrdinalIgnoreCase);
                    return new Tagging(FromSourceTags(listing.SourceTags) | text.Interests, text.Nsfw ?? adult);
                }

            case ListingSource.RpEventCalendar:
                {
                    var (name, taggedRating) = RpEventCalendar.WithoutRatingTag(listing.Title);
                    var rating = string.IsNullOrWhiteSpace(listing.Rating) ? taggedRating : listing.Rating.Trim();
                    var text = Read(name);
                    var adultsOnly = string.Equals(rating, "Adults Only", StringComparison.OrdinalIgnoreCase);
                    return new Tagging(Interests.Roleplay | text.Interests, text.Nsfw ?? adultsOnly);
                }

            default:
                {
                    var text = Read(listing.Description);
                    return new Tagging(text.Interests, text.Nsfw ?? text.AgeRestricted);
                }
        }
    }

    /// <summary>
    /// The age or content rating a listing gives, for the detail pane: the rating its source sent
    /// (Partake's "Mature", the RP calendar's "Teen"), else an age restriction in its text ("18+");
    /// null when it gives none. FFXIV Venues has no rating field.
    /// </summary>
    public static string? AgeOf(CachedListing listing)
    {
        switch (listing.Source)
        {
            case ListingSource.FfxivVenues:
                return null;

            case ListingSource.Partake:
                {
                    // Sent in capitals ("MATURE")
                    var rating = listing.Rating?.Trim();
                    return string.IsNullOrEmpty(rating) ? AgeText(Read(listing.Title).MinAge)
                        : string.Concat(rating[..1].ToUpperInvariant(), rating[1..].ToLowerInvariant());
                }

            case ListingSource.RpEventCalendar:
                {
                    var (name, taggedRating) = RpEventCalendar.WithoutRatingTag(listing.Title);
                    var rating = string.IsNullOrWhiteSpace(listing.Rating) ? taggedRating : listing.Rating.Trim();
                    return rating ?? AgeText(Read(name).MinAge);
                }

            default:
                return AgeText(Read(listing.Description).MinAge);
        }
    }

    private static string? AgeText(int minAge) => minAge > 0 ? $"{minAge}+" : null;

    /// <summary>
    /// The tagging of listings shown as one: every member's interests, and NSFW when any member is,
    /// as NSFW always wins over SFW.
    /// </summary>
    public static Tagging Combine(IEnumerable<Tagging> members)
    {
        var interests = Interests.None;
        var nsfw = false;
        foreach (var tagging in members)
        {
            interests |= tagging.Interests;
            nsfw |= tagging.Nsfw;
        }

        return new Tagging(interests, nsfw);
    }

    /// <summary>What a text says.</summary>
    /// <param name="Nsfw">True when it says NSFW, false when it says SFW and not NSFW, null when it says neither.</param>
    /// <param name="MinAge">The age restriction it gives ("18+", "R18", "18禁" are 18; "21+" is 21), the highest if several; 0 if none.</param>
    internal readonly record struct TextSignals(Interests Interests, bool? Nsfw, int MinAge)
    {
        /// <summary>Whether it gives an age restriction.</summary>
        public bool AgeRestricted => MinAge > 0;
    }

    /// <summary>Reads a listing's text, Party Finder glyphs included, for keywords and SFW, NSFW and age words.</summary>
    internal static TextSignals Read(string raw)
    {
        if (raw.Length == 0)
            return default;

        var text = PartyFinderText.Normalise(raw).Text.ToLowerInvariant();
        var words = Split(text);
        var interests = Interests.None;
        bool sfw = false, nsfw = false, club = false, otherClub = false;
        var age = 0;
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word == "nsfw")
                nsfw = true;
            else if (word == "sfw")
                sfw = true;
            if (AgeWords.TryGetValue(word, out var wordAge))
                age = Math.Max(age, wordAge);
            else if (word.StartsWith("21+", StringComparison.Ordinal))
                age = 21;
            else if (word.StartsWith("18+", StringComparison.Ordinal))
                age = Math.Max(age, 18);

            if (word is "club" or "clubs")
            {
                var before = i > 0 && words[i - 1] == "s" ? i - 2 : i - 1;
                if (before >= 0 && OtherClubKinds.Contains(words[before]))
                    otherClub = true;
                else
                    club = true;
            }

            interests |= Lookup(word);
            if (Phrases.TryGetValue(word, out var phrases))
            {
                foreach (var (tail, phraseInterests) in phrases)
                {
                    if (Follows(words, i + 1, tail))
                        interests |= phraseInterests;
                }
            }
        }

        if (club && !otherClub)
            interests |= Interests.Clubs;

        // Each of these means 18
        foreach (var phrase in AgePhrases)
        {
            if (HasPhrase(words, phrase.Split(' ')))
                age = Math.Max(age, 18);
        }

        foreach (var keyword in AgeSubstrings)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
                age = Math.Max(age, 18);
        }
        foreach (var (keyword, keywordInterests) in Substrings)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
                interests |= keywordInterests;
        }

        return new TextSignals(interests, nsfw ? true : sfw ? false : null, age);
    }

    /// <summary>The interests of one word, as a whole or without a final "s".</summary>
    private static Interests Lookup(string word)
    {
        if (Words.TryGetValue(word, out var interests))
            return interests;
        // Stryker disable once Equality : for the word "s" the rest is empty, and no keyword is empty
        var hasRest = word.Length > 1;
        if (hasRest && word[^1] == 's' && Words.TryGetValue(word[..^1], out var singular))
            return singular;
        return Interests.None;
    }

    private static Interests FromSourceTags(IEnumerable<string?>? tags)
    {
        var interests = Interests.None;
        foreach (var tag in tags ?? [])
        {
            if (tag is not null && SourceTagInterests.TryGetValue(tag.Trim(), out var tagInterests))
                interests |= tagInterests;
        }

        return interests;
    }

    /// <summary>
    /// The text's words: runs of letters and digits, with "/" between digits ("24/7", "50/50") and "+"
    /// next to one ("18+", "+18") kept, so "bar/club" and links ("twitch.tv/name") split into words.
    /// </summary>
    internal static List<string> Split(string text)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && InWord(text, i))
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0)
            {
                words.Add(text[start..i]);
                start = -1;
            }
        }

        return words;
    }

    private static bool InWord(string text, int i)
    {
        var c = text[i];
        if (char.IsLetterOrDigit(c))
            return true;
        var before = i > 0 && char.IsAsciiDigit(text[i - 1]);
        var after = i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]);
        return c switch
        {
            '/' => before && after,
            '+' => before || after,
            _ => false,
        };
    }

    private static bool Follows(List<string> words, int start, string[] rest)
    {
        if (start + rest.Length > words.Count)
            return false;
        for (var j = 0; j < rest.Length; j++)
        {
            if (!Matches(words[start + j], rest[j]))
                return false;
        }

        return true;
    }

    // A phrase's words may end in an extra "s" too ("hot springs", "card readings")
    private static bool Matches(string word, string keyword) =>
        word == keyword || (word.Length == keyword.Length + 1 && word[^1] == 's' && word.StartsWith(keyword, StringComparison.Ordinal));

    private static bool HasPhrase(List<string> words, string[] phrase)
    {
        for (var i = 0; i < words.Count; i++)
        {
            if (words[i] == phrase[0] && Follows(words, i + 1, phrase[1..]))
                return true;
        }

        return false;
    }

    // Stryker disable once Equality : U+3040 is unassigned, so no keyword contains it
    private static bool IsJapanese(char c) => c >= '\u3040';

    private static IEnumerable<(string, Interests)> Each(Interests interests, params string[] keywords) =>
        keywords.Select(k => (k, interests));
}
