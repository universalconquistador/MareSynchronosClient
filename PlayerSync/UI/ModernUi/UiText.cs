using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.UI.ModernUi;
using System.Numerics;

namespace MareSynchronosq.UI.ModernUi;

public static class UiText
{
    /// <summary>
    /// Draw text using a specific font handle and color (uint).
    /// </summary>
    public static void FontText(string text, IFontHandle font, uint color)
    {
        using var _font = font.Push();
        using var _col = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
    }

    /// <summary>
    /// Draw text using a specific font handle and color (Vector4).
    /// </summary>
    public static void FontText(string text, IFontHandle font, Vector4 color)
        => FontText(text, font, ImGui.GetColorU32(color));

    /// <summary>
    /// Draw wrapped text using a specific font handle and color, wrapping to the given width.
    /// </summary>
    public static void FontTextWrapped(string text, IFontHandle font, Vector4 color, float wrapWidth)
    {
        using var _font = font.Push();
        using var _col = ImRaii.PushColor(ImGuiCol.Text, color);

        var x = ImGui.GetCursorScreenPos().X;
        ImGui.PushTextWrapPos(x + wrapWidth);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>
    /// Draw wrapped text using a specific font handle and color, wrapping to current available width.
    /// </summary>
    public static void FontTextWrapped(string text, IFontHandle font, Vector4 color)
        => FontTextWrapped(text, font, color, ImGui.GetContentRegionAvail().X);

    public static IDisposable PushFont(IFontHandle font) => font.Push();

    public static IDisposable PushTextColor(Vector4 color) => ImRaii.PushColor(ImGuiCol.Text, color);

    public static IDisposable PushTextColor(uint color) => ImRaii.PushColor(ImGuiCol.Text, color);

    private static IFontHandle? PickFont(UiTheme t, UiTextStyle style) => style switch
    {
        UiTextStyle.Heading => t.FontHeading,
        UiTextStyle.Small => t.FontSmall,
        _ => t.FontBody,
    };

    public static void ThemedText(UiTheme t, string text, UiTextStyle style = UiTextStyle.Body, Vector4? color = null)
    {
        var font = PickFont(t, style);

        IDisposable? fontPush = null;
        if (font != null)
            fontPush = font.Push();

        try
        {
            if (color is { } c)
            {
                using var _ = ImRaii.PushColor(ImGuiCol.Text, c);
                ImGui.TextUnformatted(text);
            }
            else
            {
                ImGui.TextUnformatted(text);
            }
        }
        finally
        {
            fontPush?.Dispose();
        }
    }

    /// <summary>
    /// Draws text at the current cursor position with a soft shadow behind it.
    /// </summary>
    public static void TextShadowed(string text, Vector4 fg, Vector4 shadow, Vector2? offsetPx = null, float radiusPx = 1.5f, int passes = 6)
    {
        if (string.IsNullOrEmpty(text))
            return;

        passes = Math.Clamp(passes, 1, 12);

        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        var s = ImGuiHelpers.GlobalScale;
        var off = (offsetPx ?? new Vector2(1f, 1f)) * s;
        var r = radiusPx * s;

        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize();

        var perPass = shadow;
        perPass.W = shadow.W / passes;

        for (int i = 0; i < passes; i++)
        {
            float a = (i / (float)passes) * (MathF.PI * 2f);
            var jitter = new Vector2(MathF.Cos(a), MathF.Sin(a)) * (r * 0.5f);
            dl.AddText(font, size, pos + off + jitter, ImGui.GetColorU32(perPass), text);
        }

        using (ImRaii.PushColor(ImGuiCol.Text, fg))
            ImGui.TextUnformatted(text);
    }

    public static void TextWrappedMaxLines(string text, float width, int maxLines, Vector4 color, Vector4? ellipsisColor = null)
    {
        if (string.IsNullOrEmpty(text) || width <= 1f || maxLines <= 0)
            return;

        var dl = ImGui.GetWindowDrawList();
        var lineH = ImGui.GetTextLineHeightWithSpacing();
        var maxH = lineH * maxLines;
        var startLocal = ImGui.GetCursorPos();
        var startScreen = ImGui.GetCursorScreenPos();
        var textSize = ImGui.CalcTextSize(text, false, width);
        var usedH = MathF.Min(textSize.Y, maxH);

        var clipMax = startScreen + new Vector2(width, maxH);
        dl.PushClipRect(startScreen, clipMax, true);
        try
        {
            using (ImRaii.PushColor(ImGuiCol.Text, color))
            {
                var localX = ImGui.GetCursorPosX();
                ImGui.PushTextWrapPos(localX + width);
                ImGui.TextUnformatted(text);
                ImGui.PopTextWrapPos();
            }
        }
        finally
        {
            dl.PopClipRect();
        }

        ImGui.SetCursorPos(new Vector2(startLocal.X, startLocal.Y + usedH));
    }

    public static Vector4 MutedText(Vector4 text, float strength = 0.55f, float alphaMult = 0.85f)
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