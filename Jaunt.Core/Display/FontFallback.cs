using System.Text;

namespace Jaunt.Core.Display;

/// <summary>
/// Rewrites third-party text so the plugin's font can draw it: decorative letters become the plain
/// letters they stand for, a few symbols become ones the game font has, and anything else the font
/// lacks is left out rather than drawn as ImGui's fallback box. For display only; the cache and Copy
/// keep the text as written. The rules are in <c>docs/design.md</c> under "Text the font can't draw".
/// </summary>
/// <remarks>One pass over the text's runes, so the cost stays linear in its length.</remarks>
public static class FontFallback
{
    /// <summary>
    /// Small capitals, always read as the lower-case letter: the default font lacks them, and two of
    /// them (ʜ, ʀ) collide with FontAwesome icons merged into it, so a glyph check can't be trusted.
    /// </summary>
    private static readonly Dictionary<int, string> SmallCapitals = new()
    {
        [0x1D00] = "a", [0x0299] = "b", [0x1D04] = "c", [0x1D05] = "d", [0x1D07] = "e", [0xA730] = "f",
        [0x0262] = "g", [0x029C] = "h", [0x026A] = "i", [0x1D0A] = "j", [0x1D0B] = "k", [0x029F] = "l",
        [0x1D0D] = "m", [0x0274] = "n", [0x1D0F] = "o", [0x1D18] = "p", [0xA7AF] = "q", [0x0280] = "r",
        [0xA731] = "s", [0x1D1B] = "t", [0x1D1C] = "u", [0x1D20] = "v", [0x1D21] = "w", [0x028F] = "y",
        [0x1D22] = "z",
    };

    /// <summary>
    /// Characters from other scripts used as Latin letters in "fancy text", and decorative symbols, with
    /// a stand-in the game font has. Used only when the font lacks the character itself.
    /// </summary>
    private static readonly Dictionary<int, string> LookAlikes = new()
    {
        // Canadian syllabics
        [0x15E9] = "A", [0x15F7] = "B", [0x1455] = "C", [0x15EA] = "D", [0x15F4] = "E", [0x15B4] = "F",
        [0x157C] = "H", [0x148D] = "J", [0x14AA] = "L", [0x15F0] = "M", [0x144E] = "N", [0x146D] = "P",
        [0x146B] = "Q", [0x1587] = "R", [0x1515] = "S", [0x144C] = "U", [0x142F] = "V", [0x15EF] = "W",
        // Reversed and turned capitals
        [0x2143] = "L", [0x018E] = "E",
        // Stars, hearts and ornaments
        [0x2726] = "◆", [0x2727] = "◇", [0x2729] = "☆", [0x272A] = "★", [0x272B] = "★", [0x272C] = "★",
        [0x272D] = "★", [0x272E] = "★", [0x272F] = "★", [0x2730] = "☆",
        [0x2764] = "♥", [0xA9C1] = "«", [0xA9C2] = "»",
    };

    /// <summary>Rewrites <paramref name="text"/> for a font that has the glyphs <paramref name="hasGlyph"/> says.</summary>
    /// <param name="hasGlyph">Whether the font draws this code point itself, without its fallback.</param>
    public static string Fold(string text, Func<int, bool> hasGlyph)
    {
        StringBuilder? folded = null;
        var dropped = false;
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var replacement = Replace(rune, hasGlyph);
            if (replacement is not null && folded is null)
                folded = new StringBuilder(text, 0, index, text.Length);
            index += rune.Utf16SequenceLength;

            if (replacement is null)
                folded?.Append(rune.ToString());
            else if (replacement.Length == 0)
                dropped = true;
            else
                folded!.Append(replacement);
        }

        if (folded is null)
            return text;
        return dropped ? CollapseSpaces(folded) : folded.ToString();
    }

    /// <summary>Null to keep the rune, "" to leave it out, or the text to draw instead.</summary>
    private static string? Replace(Rune rune, Func<int, bool> hasGlyph)
    {
        var value = rune.Value;
        if (SmallCapitals.TryGetValue(value, out var letter))
            return letter;
        if (Rune.IsWhiteSpace(rune) || hasGlyph(value))
            return null;
        if (LookAlikes.TryGetValue(value, out var lookAlike) && All(lookAlike, hasGlyph))
            return lookAlike;

        // Compatibility forms, such as the bold and italic "maths" letters 𝗛𝗢𝗪𝗟 and full-width ones,
        // decompose to the plain characters they style
        var plain = rune.ToString().Normalize(NormalizationForm.FormKC);
        return plain != rune.ToString() && All(plain, hasGlyph) ? plain : "";
    }

    private static bool All(string text, Func<int, bool> hasGlyph)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (!hasGlyph(rune.Value))
                return false;
        }

        return true;
    }

    /// <summary>Closes up the spaces a left-out character leaves ("🌴 DESERT" → "DESERT").</summary>
    private static string CollapseSpaces(StringBuilder text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' && (result.Length == 0 || result[^1] == ' '))
                continue;
            result.Append(text[i]);
        }

        return result.ToString().TrimEnd(' ');
    }
}
