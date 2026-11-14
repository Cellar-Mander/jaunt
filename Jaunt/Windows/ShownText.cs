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

    private static readonly Dictionary<string, string> Remembered = new(StringComparer.Ordinal);
    private static unsafe ImFont* RememberedFont;

    /// <summary>A listing's title to draw, or "(untitled)".</summary>
    public static string Title(string title) => title.Length > 0 ? Fold(title) : "(untitled)";

    public static unsafe string Fold(string text)
    {
        var font = ImGui.GetFont();
        if (font.Handle != RememberedFont)
        {
            // A rebuilt or different font has different glyphs
            Remembered.Clear();
            RememberedFont = font.Handle;
        }

        if (Remembered.TryGetValue(text, out var shown))
            return shown;

        // ImGui's characters are 16-bit here, so it never has a glyph beyond U+FFFF
        shown = FontFallback.Fold(text, c => c <= char.MaxValue && font.FindGlyphNoFallback((ushort)c) != null);
        if (Remembered.Count >= MaxRemembered)
            Remembered.Clear();
        Remembered[text] = shown;
        return shown;
    }
}
