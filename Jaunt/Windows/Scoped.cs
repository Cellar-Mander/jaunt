using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Jaunt.Windows;

/// <summary>
/// ImGui pushes undone at the end of a <c>using</c>, for per-frame code: Dalamud's
/// <c>ImRaii.PushId</c>, <c>PushStyle</c>, <c>PushColor</c>, <c>Disabled</c> and <c>PushIndent</c>
/// allocate an object on every call, these allocate nothing. Each undoes one push.
/// </summary>
internal static class Scoped
{
    public static IdScope Id(ImU8String id)
    {
        ImGui.PushID(id);
        return default;
    }

    public static IdScope Id(int id)
    {
        ImGui.PushID(id);
        return default;
    }

    public static StyleScope Style(ImGuiStyleVar var, float value)
    {
        ImGui.PushStyleVar(var, value);
        return default;
    }

    public static ColorScope Color(ImGuiCol col, Vector4 colour)
    {
        ImGui.PushStyleColor(col, colour);
        return default;
    }

    public static DisabledScope Disabled(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        return default;
    }

    /// <summary>Indents by the style's indent spacing.</summary>
    public static IndentScope Indent()
    {
        ImGui.Indent();
        return default;
    }

    public ref struct IdScope
    {
        public readonly void Dispose() => ImGui.PopID();
    }

    public ref struct StyleScope
    {
        public readonly void Dispose() => ImGui.PopStyleVar();
    }

    public ref struct ColorScope
    {
        public readonly void Dispose() => ImGui.PopStyleColor();
    }

    public ref struct DisabledScope
    {
        public readonly void Dispose() => ImGui.EndDisabled();
    }

    public ref struct IndentScope
    {
        public readonly void Dispose() => ImGui.Unindent();
    }
}
