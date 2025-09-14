using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace MareSynchronos.UI.Themes;

public static class CurvedWindowHelper
{
    public static ImRaii.Style ApplyCurvedWindowStyle(Theme theme)
    {
        var style = ImGui.GetStyle();
        
        // Store original values
        var originalWindowRounding = style.WindowRounding;
        var originalChildRounding = style.ChildRounding;
        var originalFrameRounding = style.FrameRounding;
        var originalPopupRounding = style.PopupRounding;
        var originalScrollbarRounding = style.ScrollbarRounding;
        var originalTabRounding = style.TabRounding;
        var originalWindowBorderSize = style.WindowBorderSize;
        var originalWindowPadding = style.WindowPadding;

        // Apply curved styling
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, theme.WindowRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, theme.ChildRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, theme.FrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, theme.PopupRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, theme.ScrollbarRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, theme.TabRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, theme.WindowBorderSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, theme.WindowPadding);

        return ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, theme.WindowRounding); // Only return one for simplicity
    }

    public static void DrawCurvedBackground(Vector2 pos, Vector2 size, Vector4 color, float rounding = 8.0f, int cornerFlags = -1)
    {
        var drawList = ImGui.GetWindowDrawList();
        var colorU32 = ImGui.ColorConvertFloat4ToU32(color);
        
        if (cornerFlags == -1)
        {
            drawList.AddRectFilled(pos, pos + size, colorU32, rounding);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + size, colorU32, rounding, (ImDrawFlags)cornerFlags);
        }
    }

    public static void DrawCurvedBorder(Vector2 pos, Vector2 size, Vector4 color, float thickness = 1.0f, float rounding = 8.0f, int cornerFlags = -1)
    {
        var drawList = ImGui.GetWindowDrawList();
        var colorU32 = ImGui.ColorConvertFloat4ToU32(color);
        
        if (cornerFlags == -1)
        {
            drawList.AddRect(pos, pos + size, colorU32, rounding, ImDrawFlags.None, thickness);
        }
        else
        {
            drawList.AddRect(pos, pos + size, colorU32, rounding, (ImDrawFlags)cornerFlags, thickness);
        }
    }

    public static void DrawGlowEffect(Vector2 pos, Vector2 size, Vector4 color, float intensity = 0.5f, float radius = 10.0f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var centerX = pos.X + size.X * 0.5f;
        var centerY = pos.Y + size.Y * 0.5f;
        
        var glowColor = color with { W = color.W * intensity };
        var glowColorU32 = ImGui.ColorConvertFloat4ToU32(glowColor);
        
        for (int i = 0; i < 3; i++)
        {
            var currentRadius = radius * (1.0f + i * 0.3f);
            var currentIntensity = intensity * (1.0f - i * 0.3f);
            var currentColor = glowColor with { W = glowColor.W * currentIntensity };
            var currentColorU32 = ImGui.ColorConvertFloat4ToU32(currentColor);
            
            drawList.AddRectFilled(
                new Vector2(centerX - currentRadius, centerY - currentRadius),
                new Vector2(centerX + currentRadius, centerY + currentRadius),
                currentColorU32,
                currentRadius * 0.5f
            );
        }
    }

    public static bool CurvedButton(string label, Vector2 size, Theme theme, float rounding = -1f)
    {
        if (rounding < 0) rounding = theme.FrameRounding;

        using (ImRaii.PushColor(ImGuiCol.Button, theme.Primary with { W = 0.6f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, theme.Hover))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, theme.Active))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, rounding))
        {
            return ImGui.Button(label, size);
        }
    }

    public static void CurvedProgressBar(float progress, Vector2 size, Theme theme, string? overlay = null, float rounding = -1f)
    {
        if (rounding < 0) rounding = theme.FrameRounding;

        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        
        progress = Math.Max(0f, Math.Min(1f, progress));
        
        // Draw background
        drawList.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(theme.Surface), rounding);
        
        // Draw progress
        if (progress > 0)
        {
            var progressSize = new Vector2(size.X * progress, size.Y);
            drawList.AddRectFilled(pos, pos + progressSize, ImGui.ColorConvertFloat4ToU32(theme.Primary), rounding);
        }
        
        // Draw border
        drawList.AddRect(pos, pos + size, ImGui.ColorConvertFloat4ToU32(theme.Border), rounding, ImDrawFlags.None, 1.0f);
        
        // Draw overlay text
        if (!string.IsNullOrEmpty(overlay))
        {
            var textSize = ImGui.CalcTextSize(overlay);
            var textPos = new Vector2(
                pos.X + (size.X - textSize.X) * 0.5f,
                pos.Y + (size.Y - textSize.Y) * 0.5f
            );
            
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(theme.Text), overlay);
        }
        
        // Advance cursor
        ImGui.SetCursorScreenPos(pos + new Vector2(0, size.Y));
    }

    public static void CurvedSeparator(Theme theme, float thickness = 1.0f, float padding = 4.0f)
    {
        var pos = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();
        
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + padding);
        
        var lineStart = new Vector2(pos.X, pos.Y + padding);
        var lineEnd = new Vector2(pos.X + availWidth, pos.Y + padding);
        
        drawList.AddLine(lineStart, lineEnd, ImGui.ColorConvertFloat4ToU32(theme.NavSeparator), thickness);
        
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + padding + thickness);
    }
}