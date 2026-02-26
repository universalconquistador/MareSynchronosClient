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

    public static Vector4 GetIntentColor(UiTheme theme, Intent intent)
        => intent switch
        {
            Intent.Primary => theme.Primary,
            Intent.Success => theme.Success,
            Intent.Warning => theme.Warning,
            Intent.Danger => theme.Danger,
            Intent.Info => theme.Info,
            _ => theme.TextMuted,
        };

    // used for very small spacing
    public static void AddVerticalSpace(float px) => ImGuiHelpers.ScaledDummy(px);

    // themed seperator
    public static void DrawHorizontalRule(UiTheme theme)
    {
        AddVerticalSpace(5);
        using (ImRaii.PushColor(ImGuiCol.Separator, theme.Separator))
            ImGui.Separator();
        AddVerticalSpace(5);
    }

    // section headers
    public static void DrawSectionHeader(UiTheme theme, string text, bool addHr = true)
    {
        using var col = ImRaii.PushColor(ImGuiCol.Text, theme.TextMuted);
        ImGui.TextUnformatted(text.ToUpperInvariant());
        if (addHr) DrawHorizontalRule(theme);
    }

    // used for centering
    public static void CenterNext(float itemWidth)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (availableWidth <= 0) return;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, (availableWidth - itemWidth) * 0.5f));
    }

    // Draw text aligned inside the current region
    public static void DrawTextJustified(string text, Justify justify)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var textWidth = ImGui.CalcTextSize(text).X;
        var offset = justify switch
        {
            Justify.Center => (availableWidth - textWidth) * 0.5f,
            Justify.Right => (availableWidth - textWidth),
            _ => 0f,
        };
        if (offset > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ImGui.TextUnformatted(text);
    }

    public static void DrawCard(UiTheme theme, string id, Action? header, Action body, Action? footer = null, Vector2? size = null, bool border = true)
    {
        var padding = UiScale.ScaledFloat(theme.CardPadding);
        var rounding = UiScale.ScaledFloat(theme.RadiusMd);

        var childSize = size ?? new Vector2(0, 0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, rounding);
        using var background = ImRaii.PushColor(ImGuiCol.ChildBg, theme.CardBg);
        using var borderSize = border ? ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, UiScale.ScaledFloat(1f)) : null;
        using var borderColor = border ? ImRaii.PushColor(ImGuiCol.Border, theme.Border) : null;

        using var child = ImRaii.Child(id, childSize, true, ImGuiWindowFlags.None);
        if (!child) return;

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding)))
        {
            header?.Invoke();
            if (header != null) DrawHorizontalRule(theme);

            body();

            if (footer != null)
            {
                DrawHorizontalRule(theme);
                footer();
            }
        }
    }

    public static void DrawAlert(UiTheme theme, Intent intent, string message, Action? rightSide = null)
    {
        var intentColor = GetIntentColor(theme, intent);
        var backgroundColor = new Vector4(intentColor.X, intentColor.Y, intentColor.Z, 0.12f);

        DrawCard(theme, id: $"##alert_{intent}_{ImGui.GetID(message)}", header: null,
            body: () =>
            {
                using var background = ImRaii.PushColor(ImGuiCol.ChildBg, backgroundColor);
                using var textColor = ImRaii.PushColor(ImGuiCol.Text, theme.Text);
                ImGui.TextWrapped(message);

                if (rightSide != null)
                {
                    ImGui.SameLine();
                    var availableWidth = ImGui.GetContentRegionAvail().X;
                    Ui.CenterNext(MathF.Min(120, availableWidth));
                    rightSide();
                }
            },
            border: true);
    }

    public static void DrawBadge(UiTheme theme, Intent intent, string text)
    {
        var intentColor = GetIntentColor(theme, intent);
        var backgroundColor = new Vector4(intentColor.X, intentColor.Y, intentColor.Z, 0.20f);

        using var buttonColor = ImRaii.PushColor(ImGuiCol.Button, backgroundColor);
        using var hoveredColor = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(intentColor.X, intentColor.Y, intentColor.Z, 0.26f));
        using var activeColor = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(intentColor.X, intentColor.Y, intentColor.Z, 0.32f));
        using var textColor = ImRaii.PushColor(ImGuiCol.Text, theme.Text);

        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, UiScale.ScaledFloat(theme.RadiusSm));
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(UiScale.ScaledFloat(8), UiScale.ScaledFloat(3)));

        ImGui.Button(text);
    }

    public static void DrawHelpMarker(string text)
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

    public static void DrawProgress(float fraction, string? label = null, float heightPx = 8f)
    {
        var size = new Vector2(-1, UiScale.ScaledFloat(heightPx));
        ImGui.ProgressBar(Math.Clamp(fraction, 0f, 1f), size, label ?? string.Empty);
    }

    public static void DrawHint(UiTheme theme, string text)
    {
        using var col = ImRaii.PushColor(ImGuiCol.Text, theme.TextMuted);
        ImGui.TextWrapped(text);
    }
}
