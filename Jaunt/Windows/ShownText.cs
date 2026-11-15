using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Jaunt.Core.Display;

namespace Jaunt.Windows;

/// <summary>
/// Third-party text as the current font can draw it (<see cref="FontFallback"/>), worked out once per
/// text and font rather than every frame. Use from the draw thread only.
/// </summary>
internal static class ShownText
{
    /// <summary>Enough for every title in the cache; past it the memo starts again rather than grow.</summary>
    private const int MaxRemembered = 8192;

    /// <summary>The fonts remembered for at once; past it, as after font rebuilds, all are forgotten.</summary>
    private const int MaxFonts = 4;

    // One memo per font, as titles are drawn in a larger font than the rest
    private static readonly Dictionary<nint, Dictionary<string, string>> Remembered = [];

    /// <summary>A listing's title to draw, or "(untitled)".</summary>
    public static string Title(string title) => title.Length > 0 ? Fold(title) : "(untitled)";

    public static unsafe string Fold(string text)
    {
        var font = ImGui.GetFont();
        if (!Remembered.TryGetValue((nint)font.Handle, out var memo))
        {
            // A rebuilt font has a new handle and different glyphs
            if (Remembered.Count >= MaxFonts)
                Remembered.Clear();
            memo = new Dictionary<string, string>(StringComparer.Ordinal);
            Remembered[(nint)font.Handle] = memo;
        }

        if (memo.TryGetValue(text, out var shown))
            return shown;

        // ImGui's characters are 16-bit here, so it never has a glyph beyond U+FFFF
        shown = FontFallback.Fold(text, c => c <= char.MaxValue && font.FindGlyphNoFallback((ushort)c) != null);
        if (memo.Count >= MaxRemembered)
            memo.Clear();
        memo[text] = shown;
        return shown;
    }
}
