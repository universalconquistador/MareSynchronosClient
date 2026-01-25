using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace MareSynchronos.UI.ModernUi;

public static class UiText
{
    public static void ThemedText(UiTheme theme, string text, UiTextStyle style = UiTextStyle.Body, Vector4? color = null)
    {
        var fontHandle = style switch
        {
            UiTextStyle.Heading => theme.FontHeading,
            UiTextStyle.Small => theme.FontSmall,
            _ => theme.FontBody
        };

        IDisposable? fontScope = null;
        if (fontHandle != null)
            fontScope = fontHandle.Push();

        try
        {
            if (color != null)
            {
                using var textColorScope = ImRaii.PushColor(ImGuiCol.Text, color.Value);
                ImGui.TextUnformatted(text);
            }
            else
            {
                ImGui.TextUnformatted(text);
            }
        }
        finally
        {
            fontScope?.Dispose();
        }
    }

    /// <summary>
    /// Draws text at the current cursor position with a soft shadow behind it.
    /// </summary>
    public static void DrawTextShadowed(string text, Vector4 foregroundColor, Vector4 shadowColor, Vector2? offsetPx = null, float radiusPx = 1.5f, int passes = 6)
    {
        if (string.IsNullOrEmpty(text))
            return;

        passes = Math.Clamp(passes, 1, 12);

        var drawList = ImGui.GetWindowDrawList();
        var screenPos = ImGui.GetCursorScreenPos();

        var globalScale = ImGuiHelpers.GlobalScale;
        var shadowOffset = (offsetPx ?? new Vector2(1f, 1f)) * globalScale;
        var shadowRadius = radiusPx * globalScale;

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();

        var perPassShadowColor = shadowColor;
        perPassShadowColor.W = shadowColor.W / passes;

        for (int passIndex = 0; passIndex < passes; passIndex++)
        {
            float angle = (passIndex / (float)passes) * (MathF.PI * 2f);
            var jitter = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (shadowRadius * 0.5f);
            drawList.AddText(font, fontSize, screenPos + shadowOffset + jitter, ImGui.GetColorU32(perPassShadowColor), text);
        }

        using (ImRaii.PushColor(ImGuiCol.Text, foregroundColor))
            ImGui.TextUnformatted(text);
    }

    public static void DrawTextWrappedMaxLines(string text, float width, int maxLines, Vector4 color, Vector4? ellipsisColor = null)
    {
        if (string.IsNullOrEmpty(text) || width <= 1f || maxLines <= 0)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var maxHeight = lineHeight * maxLines;
        var startLocalPos = ImGui.GetCursorPos();
        var startScreenPos = ImGui.GetCursorScreenPos();
        var textSize = ImGui.CalcTextSize(text, false, width);
        var usedHeight = MathF.Min(textSize.Y, maxHeight);

        var clipMax = startScreenPos + new Vector2(width, maxHeight);
        drawList.PushClipRect(startScreenPos, clipMax, true);
        try
        {
            using (ImRaii.PushColor(ImGuiCol.Text, color))
            {
                var localCursorX = ImGui.GetCursorPosX();
                ImGui.PushTextWrapPos(localCursorX + width);
                ImGui.TextUnformatted(text);
                ImGui.PopTextWrapPos();
            }
        }
        finally
        {
            drawList.PopClipRect();
        }

        ImGui.SetCursorPos(new Vector2(startLocalPos.X, startLocalPos.Y + usedHeight));
    }

    public static Vector4 GetMutedTextColor(Vector4 text, float strength = 0.55f, float alphaMult = 0.85f)
    {
        strength = Math.Clamp(strength, 0f, 1f);

        float luma = (text.X * 0.2126f) + (text.Y * 0.7152f) + (text.Z * 0.0722f);
        var gray = new Vector3(luma, luma, luma);
        var rgb = new Vector3(text.X, text.Y, text.Z);
        var towardGray = Vector3.Lerp(rgb, gray, strength * 0.70f);
        var darkened = towardGray * (1f - strength * 0.25f);

        return new Vector4(
            Math.Clamp(darkened.X, 0f, 1f),
            Math.Clamp(darkened.Y, 0f, 1f),
            Math.Clamp(darkened.Z, 0f, 1f),
            Math.Clamp(text.W * alphaMult, 0f, 1f)
        );
    }
}

public enum UiTextStyle
{
    Body,
    Heading,
    Small,
}
