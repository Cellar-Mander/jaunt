using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

namespace Jaunt.Windows;

/// <summary>
/// A font handle kept locked between frames, so pushing it allocates nothing: <see cref="IFontHandle.Push"/>
/// locks the font anew, allocating, on every call. The lock is renewed when the font is rebuilt. Use
/// from the draw thread only, except that the handle may report a rebuild from any thread.
/// </summary>
internal sealed class HeldFont : IDisposable
{
    private readonly IFontHandle handle;
    private ILockedImFont? locked;
    private volatile bool rebuilt;

    /// <param name="handle">The font; owned by the caller, who disposes this first.</param>
    public HeldFont(IFontHandle handle)
    {
        this.handle = handle;
        handle.ImFontChanged += OnChanged;
    }

    /// <summary>
    /// Locks the font if it isn't, or locks it anew once rebuilt. Call at the start of a window's draw,
    /// before any <see cref="Push"/>, so no font drawn with this frame is released.
    /// </summary>
    public void BeginFrame()
    {
        if (locked is not null && !rebuilt)
            return;
        AllocationWatch.Filled();
        rebuilt = false;
        locked?.Dispose();
        locked = handle.TryLock(out _);
    }

    /// <summary>The font locked this frame, or null while it isn't built.</summary>
    public ImFontPtr? Font => locked?.ImFont;

    /// <summary>Pushes the font, or nothing while it isn't built, until the scope ends.</summary>
    public Scope Push()
    {
        if (locked is not { } font)
            return default;
        ImGui.PushFont(font.ImFont);
        return new Scope(true);
    }

    /// <summary>Lets go of the font, as when its window closes, so a rebuild can free the old one; the next <see cref="BeginFrame"/> locks it again.</summary>
    public void Release()
    {
        locked?.Dispose();
        locked = null;
    }

    public void Dispose()
    {
        handle.ImFontChanged -= OnChanged;
        Release();
    }

    private void OnChanged(IFontHandle fontHandle, ILockedImFont lockedFont) => rebuilt = true;

    public readonly ref struct Scope(bool pushed)
    {
        public void Dispose()
        {
            if (pushed)
                ImGui.PopFont();
        }
    }
}
