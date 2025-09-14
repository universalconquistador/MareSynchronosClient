using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace MareSynchronos.UI.Themes;

public static class ThemeApplier
{
    private static Dictionary<ImGuiCol, Vector4> _originalColors = new();
    private static Dictionary<ImGuiStyleVar, object> _originalStyles = new();
    private static bool _isApplied = false;

    public static void ApplyTheme(Theme theme)
    {
        if (!_isApplied)
        {
            BackupOriginalStyle();
        }

        var style = ImGui.GetStyle();

        // Apply colors
        ApplyColors(theme);
        
        // Apply style variables
        style.WindowRounding = theme.WindowRounding;
        style.ChildRounding = theme.ChildRounding;
        style.FrameRounding = theme.FrameRounding;
        style.PopupRounding = theme.PopupRounding;
        style.ScrollbarRounding = theme.ScrollbarRounding;
        style.TabRounding = theme.TabRounding;
        
        style.WindowPadding = theme.WindowPadding;
        style.FramePadding = theme.FramePadding;
        style.CellPadding = theme.CellPadding;
        style.ItemSpacing = theme.ItemSpacing;
        style.ItemInnerSpacing = theme.ItemInnerSpacing;
        style.TouchExtraPadding = theme.TouchExtraPadding;
        
        style.IndentSpacing = theme.IndentSpacing;
        style.ScrollbarSize = theme.ScrollbarSize;
        style.WindowBorderSize = theme.WindowBorderSize;
        style.ChildBorderSize = theme.ChildBorderSize;
        style.PopupBorderSize = theme.PopupBorderSize;
        style.FrameBorderSize = theme.FrameBorderSize;
        style.TabBorderSize = theme.TabBorderSize;

        _isApplied = true;
    }

    private static void ApplyColors(Theme theme)
    {
        var colors = ImGui.GetStyle().Colors;

        // Window colors
        colors[(int)ImGuiCol.WindowBg] = theme.Background;
        colors[(int)ImGuiCol.ChildBg] = theme.BackgroundSecondary;
        colors[(int)ImGuiCol.PopupBg] = theme.Surface;
        colors[(int)ImGuiCol.MenuBarBg] = theme.NavBackground;
        
        // Frame colors
        colors[(int)ImGuiCol.FrameBg] = theme.Surface;
        colors[(int)ImGuiCol.FrameBgHovered] = theme.Hover;
        colors[(int)ImGuiCol.FrameBgActive] = theme.Active;
        
        // Title bar
        colors[(int)ImGuiCol.TitleBg] = theme.NavBackground;
        colors[(int)ImGuiCol.TitleBgActive] = theme.NavBackground;
        colors[(int)ImGuiCol.TitleBgCollapsed] = theme.NavBackground;
        
        // Text colors
        colors[(int)ImGuiCol.Text] = theme.Text;
        colors[(int)ImGuiCol.TextDisabled] = theme.TextDisabled;
        colors[(int)ImGuiCol.TextSelectedBg] = theme.Primary with { W = 0.3f };
        
        // Border colors
        colors[(int)ImGuiCol.Border] = theme.Border;
        colors[(int)ImGuiCol.BorderShadow] = Vector4.Zero;
        
        // Button colors
        colors[(int)ImGuiCol.Button] = theme.Primary with { W = 0.6f };
        colors[(int)ImGuiCol.ButtonHovered] = theme.Primary with { W = 0.8f };
        colors[(int)ImGuiCol.ButtonActive] = theme.Primary;
        
        // Header colors (for trees, selectables, etc.)
        colors[(int)ImGuiCol.Header] = theme.Primary with { W = 0.3f };
        colors[(int)ImGuiCol.HeaderHovered] = theme.Primary with { W = 0.5f };
        colors[(int)ImGuiCol.HeaderActive] = theme.Primary with { W = 0.7f };
        
        // Separator
        colors[(int)ImGuiCol.Separator] = theme.NavSeparator;
        colors[(int)ImGuiCol.SeparatorHovered] = theme.Primary with { W = 0.6f };
        colors[(int)ImGuiCol.SeparatorActive] = theme.Primary;
        
        // Resize grip
        colors[(int)ImGuiCol.ResizeGrip] = theme.Primary with { W = 0.2f };
        colors[(int)ImGuiCol.ResizeGripHovered] = theme.Primary with { W = 0.5f };
        colors[(int)ImGuiCol.ResizeGripActive] = theme.Primary with { W = 0.8f };
        
        // Tab colors
        colors[(int)ImGuiCol.Tab] = theme.NavBackground;
        colors[(int)ImGuiCol.TabHovered] = theme.NavItemHover;
        colors[(int)ImGuiCol.TabActive] = theme.NavItemActive;
        colors[(int)ImGuiCol.TabUnfocused] = theme.NavBackground;
        colors[(int)ImGuiCol.TabUnfocusedActive] = theme.NavItemActive with { W = 0.7f };
        
        // Scrollbar colors
        colors[(int)ImGuiCol.ScrollbarBg] = theme.Background with { W = 0.5f };
        colors[(int)ImGuiCol.ScrollbarGrab] = theme.Secondary with { W = 0.6f };
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = theme.Secondary with { W = 0.8f };
        colors[(int)ImGuiCol.ScrollbarGrabActive] = theme.Secondary;
        
        // Check mark
        colors[(int)ImGuiCol.CheckMark] = theme.Success;
        
        // Slider colors
        colors[(int)ImGuiCol.SliderGrab] = theme.Primary;
        colors[(int)ImGuiCol.SliderGrabActive] = theme.Primary with { W = 0.8f };
        
        // Progress bar
        colors[(int)ImGuiCol.PlotHistogram] = theme.Primary;
        colors[(int)ImGuiCol.PlotHistogramHovered] = theme.Primary with { W = 0.8f };
        
        // Table colors
        colors[(int)ImGuiCol.TableHeaderBg] = theme.NavBackground;
        colors[(int)ImGuiCol.TableBorderStrong] = theme.Border;
        colors[(int)ImGuiCol.TableBorderLight] = theme.Border with { W = 0.5f };
        colors[(int)ImGuiCol.TableRowBg] = Vector4.Zero;
        colors[(int)ImGuiCol.TableRowBgAlt] = theme.Surface with { W = 0.3f };
        
        // Drag and drop
        colors[(int)ImGuiCol.DragDropTarget] = theme.Accent;
    }

    private static void BackupOriginalStyle()
    {
        var colors = ImGui.GetStyle().Colors;
        var style = ImGui.GetStyle();

        _originalColors.Clear();
        for (int i = 0; i < 53; i++) // ImGuiCol enum count in Dalamud
        {
            _originalColors[(ImGuiCol)i] = colors[i];
        }

        _originalStyles.Clear();
        _originalStyles[ImGuiStyleVar.WindowRounding] = style.WindowRounding;
        _originalStyles[ImGuiStyleVar.ChildRounding] = style.ChildRounding;
        _originalStyles[ImGuiStyleVar.FrameRounding] = style.FrameRounding;
        _originalStyles[ImGuiStyleVar.PopupRounding] = style.PopupRounding;
        _originalStyles[ImGuiStyleVar.ScrollbarRounding] = style.ScrollbarRounding;
        _originalStyles[ImGuiStyleVar.TabRounding] = style.TabRounding;
        _originalStyles[ImGuiStyleVar.WindowPadding] = style.WindowPadding;
        _originalStyles[ImGuiStyleVar.FramePadding] = style.FramePadding;
        _originalStyles[ImGuiStyleVar.CellPadding] = style.CellPadding;
        _originalStyles[ImGuiStyleVar.ItemSpacing] = style.ItemSpacing;
        _originalStyles[ImGuiStyleVar.ItemInnerSpacing] = style.ItemInnerSpacing;
        _originalStyles[ImGuiStyleVar.IndentSpacing] = style.IndentSpacing;
        _originalStyles[ImGuiStyleVar.ScrollbarSize] = style.ScrollbarSize;
        _originalStyles[ImGuiStyleVar.WindowBorderSize] = style.WindowBorderSize;
        _originalStyles[ImGuiStyleVar.ChildBorderSize] = style.ChildBorderSize;
        _originalStyles[ImGuiStyleVar.PopupBorderSize] = style.PopupBorderSize;
        _originalStyles[ImGuiStyleVar.FrameBorderSize] = style.FrameBorderSize;
        // TabBorderSize not available in this ImGui version
    }

    public static void RestoreOriginalStyle()
    {
        if (!_isApplied) return;

        var colors = ImGui.GetStyle().Colors;
        var style = ImGui.GetStyle();

        foreach (var kvp in _originalColors)
        {
            colors[(int)kvp.Key] = kvp.Value;
        }

        foreach (var kvp in _originalStyles)
        {
            switch (kvp.Key)
            {
                case ImGuiStyleVar.WindowRounding:
                    style.WindowRounding = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.ChildRounding:
                    style.ChildRounding = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.FrameRounding:
                    style.FrameRounding = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.PopupRounding:
                    style.PopupRounding = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.ScrollbarRounding:
                    style.ScrollbarRounding = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.TabRounding:
                    style.TabRounding = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.WindowPadding:
                    style.WindowPadding = (Vector2)kvp.Value;
                    break;
                case ImGuiStyleVar.FramePadding:
                    style.FramePadding = (Vector2)kvp.Value;
                    break;
                case ImGuiStyleVar.CellPadding:
                    style.CellPadding = (Vector2)kvp.Value;
                    break;
                case ImGuiStyleVar.ItemSpacing:
                    style.ItemSpacing = (Vector2)kvp.Value;
                    break;
                case ImGuiStyleVar.ItemInnerSpacing:
                    style.ItemInnerSpacing = (Vector2)kvp.Value;
                    break;
                case ImGuiStyleVar.IndentSpacing:
                    style.IndentSpacing = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.ScrollbarSize:
                    style.ScrollbarSize = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.WindowBorderSize:
                    style.WindowBorderSize = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.ChildBorderSize:
                    style.ChildBorderSize = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.PopupBorderSize:
                    style.PopupBorderSize = (float)kvp.Value;
                    break;
                case ImGuiStyleVar.FrameBorderSize:
                    style.FrameBorderSize = (float)kvp.Value;
                    break;
                // TabBorderSize not available in this ImGui version
            }
        }

        _isApplied = false;
    }

    public static ImRaii.Color PushThemeColor(ImGuiCol col, Vector4 color)
    {
        return ImRaii.PushColor(col, color);
    }

    public static ImRaii.Color PushThemedColor(this Theme theme, ImGuiCol col)
    {
        return col switch
        {
            ImGuiCol.Text => ImRaii.PushColor(col, theme.Text),
            ImGuiCol.TextDisabled => ImRaii.PushColor(col, theme.TextDisabled),
            ImGuiCol.WindowBg => ImRaii.PushColor(col, theme.Background),
            ImGuiCol.ChildBg => ImRaii.PushColor(col, theme.BackgroundSecondary),
            ImGuiCol.PopupBg => ImRaii.PushColor(col, theme.Surface),
            ImGuiCol.Border => ImRaii.PushColor(col, theme.Border),
            ImGuiCol.FrameBg => ImRaii.PushColor(col, theme.Surface),
            ImGuiCol.FrameBgHovered => ImRaii.PushColor(col, theme.Hover),
            ImGuiCol.FrameBgActive => ImRaii.PushColor(col, theme.Active),
            ImGuiCol.Button => ImRaii.PushColor(col, theme.Primary with { W = 0.6f }),
            ImGuiCol.ButtonHovered => ImRaii.PushColor(col, theme.Primary with { W = 0.8f }),
            ImGuiCol.ButtonActive => ImRaii.PushColor(col, theme.Primary),
            _ => ImRaii.PushColor(col, Vector4.One)
        };
    }
}