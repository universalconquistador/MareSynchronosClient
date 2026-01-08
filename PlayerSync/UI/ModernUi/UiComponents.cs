using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace MareSynchronos.UI.ModernUi;

/// <summary>
/// Bootstrap inspired UI elements
/// A lot of this could just be ImGui extension
/// </summary>
public static class Ui
{
    public enum Intent { Default, Primary, Success, Warning, Danger, Info }
    public enum Justify { Left, Center, Right }

    public static Vector4 IntentColor(UiTheme t, Intent intent)
        => intent switch
    {
        Intent.Primary => t.Primary,
        Intent.Success => t.Success,
        Intent.Warning => t.Warning,
        Intent.Danger => t.Danger,
        Intent.Info => t.Info,
        _ => t.TextMuted,
    };

    // used for very small spacing
    public static void VSpace(float px) => ImGuiHelpers.ScaledDummy(px);

    // themed seperator
    public static void Hr(UiTheme t)
    {
        VSpace(5);
        using (ImRaii.PushColor(ImGuiCol.Separator, t.Separator))
            ImGui.Separator();
        VSpace(5);
    }

    // section headers
    public static void SectionHeader(UiTheme t, string text, bool addHr = true)
    {
        using var col = ImRaii.PushColor(ImGuiCol.Text, t.TextMuted);
        ImGui.TextUnformatted(text.ToUpperInvariant());
        if (addHr) Hr(t);
    }

    // used for centering
    public static void CenterNext(float itemWidth)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail <= 0) return;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, (avail - itemWidth) * 0.5f));
    }

    /// <summary>Render text aligned inside the current region (or table cell).</summary>
    public static void TextJustified(string text, Justify justify)
    {
        var w = ImGui.GetContentRegionAvail().X;
        var ts = ImGui.CalcTextSize(text).X;
        var offset = justify switch
        {
            Justify.Center => (w - ts) * 0.5f,
            Justify.Right => (w - ts),
            _ => 0f,
        };
        if (offset > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ImGui.TextUnformatted(text);
    }

    public static void Card(UiTheme t, string id, Action? header, Action body, Action? footer = null, Vector2? size = null, bool border = true)
    {
        var pad = UiScale.S(t.CardPadding);
        var rounding = UiScale.S(t.RadiusMd);

        var childSize = size ?? new Vector2(0, 0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, rounding);
        using var bg = ImRaii.PushColor(ImGuiCol.ChildBg, t.CardBg);
        using var b = border ? ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, UiScale.S(1f)) : null;
        using var bc = border ? ImRaii.PushColor(ImGuiCol.Border, t.Border) : null;

        using var child = ImRaii.Child(id, childSize, true, ImGuiWindowFlags.None);
        if (!child) return;

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad)))
        {
            header?.Invoke();
            if (header != null) Hr(t);

            body();

            if (footer != null)
            {
                Hr(t);
                footer();
            }
        }
    }

    public static void Alert(UiTheme t, Intent intent, string message, Action? rightSide = null)
    {
        var col = IntentColor(t, intent);
        var bg = new Vector4(col.X, col.Y, col.Z, 0.12f);

        Card(t, id: $"##alert_{intent}_{ImGui.GetID(message)}", header: null,
            body: () =>
            {
                using var bgc = ImRaii.PushColor(ImGuiCol.ChildBg, bg);
                using var text = ImRaii.PushColor(ImGuiCol.Text, t.Text);
                ImGui.TextWrapped(message);

                if (rightSide != null)
                {
                    ImGui.SameLine();
                    var w = ImGui.GetContentRegionAvail().X;
                    Ui.CenterNext(MathF.Min(120, w));
                    rightSide();
                }
            },
            border: true);
    }

    public static void Badge(UiTheme t, Intent intent, string text)
    {
        var col = IntentColor(t, intent);
        var bg = new Vector4(col.X, col.Y, col.Z, 0.20f);

        using var c1 = ImRaii.PushColor(ImGuiCol.Button, bg);
        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(col.X, col.Y, col.Z, 0.26f));
        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(col.X, col.Y, col.Z, 0.32f));
        using var c4 = ImRaii.PushColor(ImGuiCol.Text, t.Text);

        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, UiScale.S(t.RadiusSm));
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(UiScale.S(8), UiScale.S(3)));

        ImGui.Button(text);
    }

    public static void HelpMarker(string text)
    {
        ImGui.SameLine();
        // update to include font-awesome by default
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public static void Progress(float fraction01, string? label = null, float heightPx = 8f)
    {
        var size = new Vector2(-1, UiScale.S(heightPx));
        ImGui.ProgressBar(Math.Clamp(fraction01, 0f, 1f), size, label ?? string.Empty);
    }

    public static void Hint(UiTheme t, string text)
    {
        using var col = ImRaii.PushColor(ImGuiCol.Text, t.TextMuted);
        ImGui.TextWrapped(text);
    }
}
