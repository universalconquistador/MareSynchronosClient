using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using MareSynchronos.MareConfiguration;
using static Dalamud.Interface.Windowing.Window;

namespace MareSynchronos.UI.Components.Theming;

public class ThemeManager
{
    private readonly UIThemeConfigService _uIThemeConfigService;
    private readonly Dictionary<string, ThemePalette> _predefinedThemes;
    private ThemePalette _currentTheme;
    private string _currentThemeName = "Default";
    private bool _isCustomTheme;
    private readonly float _baseWindowWidth = 375f;
    private readonly  float _baseWindowHeightMin = 400f;
    private readonly float _baseWindowHeightMax = 2000f;
    private readonly float _baseCollapsedWindowHeight = 60f;
    private readonly float _spacing = 6f;
    private readonly float _padding = 5f;

    public static ThemeManager? Instance { get; private set; }

    public ThemeManager(UIThemeConfigService uIThemeConfigService)
    {
        _uIThemeConfigService = uIThemeConfigService;
        _predefinedThemes = CreatePredefinedThemes();
        LoadSavedTheme();
        Instance = this;
    }

    public ThemePalette Current => _currentTheme;
    public string CurrentThemeName => _currentThemeName;
    public bool IsCustomTheme => _isCustomTheme;
    public IReadOnlyDictionary<string, ThemePalette> PredefinedThemes => _predefinedThemes;
    public float WindowWidth => _baseWindowWidth;
    public float WindowHeightMin => _baseWindowHeightMin;
    public float WindowheightMax => _baseWindowHeightMax;
    public float CollapsedWindowHeight => _baseCollapsedWindowHeight;
    public float ScaledWindowWidth => _baseWindowWidth * ImGuiHelpers.GlobalScale;
    public float ScaledCollapsedWindowHeight => _baseCollapsedWindowHeight * ImGuiHelpers.GlobalScale;
    public WindowSizeConstraints CompactUISizeConstraints => new WindowSizeConstraints()
    {
        MinimumSize = new Vector2(_baseWindowWidth, _baseWindowHeightMin),
        MaximumSize = new Vector2(_baseWindowWidth, _baseWindowHeightMax),
    };
    public WindowSizeConstraints CompactUICollapsedSizeConstraints => new WindowSizeConstraints()
    {
        MinimumSize = new Vector2(_baseWindowWidth, _baseCollapsedWindowHeight),
        MaximumSize = new Vector2(_baseWindowWidth, _baseCollapsedWindowHeight),
    };
    public float Spacing => _spacing;
    public float Padding => _padding;
    public float ScaledSpacing => _spacing * ImGuiHelpers.GlobalScale;

    public void SetTheme(string themeName)
    {
        if (_predefinedThemes.TryGetValue(themeName, out var theme))
        {
            _currentTheme = theme;
            _currentThemeName = themeName;
            _isCustomTheme = false;
            SaveThemeSettings();
        }
    }

    public void SetCustomTheme(ThemePalette customTheme)
    {
        _currentTheme = customTheme;
        _currentThemeName = "Custom";
        _isCustomTheme = true;
        SaveThemeSettings();
    }

    public IDisposable PushTheme()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, _currentTheme.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _currentTheme.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, _currentTheme.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, _currentTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, _currentTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, _currentTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.MenuBarBg, _currentTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.Header, _currentTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, _currentTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.Button, _currentTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _currentTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.Text, _currentTheme.TextPrimary);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, _currentTheme.TextDisabled);
        ImGui.PushStyleColor(ImGuiCol.Tab, _currentTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, _currentTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.TabActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocused, _currentTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, _currentTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, _currentTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, _currentTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, _currentTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, _currentTheme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, _currentTheme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.Separator, _currentTheme.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, _currentTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.SeparatorActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, _currentTheme.Accent);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, _currentTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, _currentTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, _currentTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, _currentTheme.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, _currentTheme.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(_currentTheme.HeaderBg.X, _currentTheme.HeaderBg.Y, _currentTheme.HeaderBg.Z, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, _currentTheme.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(_currentTheme.PanelBg.X, _currentTheme.PanelBg.Y, _currentTheme.PanelBg.Z, 0.50f));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, _currentTheme.BtnActive);

        // Apply rounding styles
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, _currentTheme.WindowRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, _currentTheme.ChildRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, _currentTheme.FrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, _currentTheme.PopupRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, _currentTheme.ScrollbarRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, _currentTheme.GrabRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, _currentTheme.TabRounding);

        return new ThemeScope(44, 7);
    }

    private class ThemeScope : IDisposable
    {
        private readonly int _colorCount;
        private readonly int _styleVarCount;

        public ThemeScope(int colorCount, int styleVarCount = 0)
        {
            _colorCount = colorCount;
            _styleVarCount = styleVarCount;
        }

        public void Dispose()
        {
            if (_styleVarCount > 0)
                ImGui.PopStyleVar(_styleVarCount);
            ImGui.PopStyleColor(_colorCount);
        }
    }

    private void LoadSavedTheme()
    {
        var config = _uIThemeConfigService.Current;
        if (config.UseCustomTheme && config.CustomThemeData != null)
        {
            _currentThemeName = "Custom";
            _isCustomTheme = true;
            _currentTheme = config.CustomThemeData;
        }
        else if (_predefinedThemes.TryGetValue(config.SelectedTheme, out var theme))
        {
            _currentTheme = theme;
            _currentThemeName = config.SelectedTheme;
            _isCustomTheme = false;
        }
        else
        {
            _currentTheme = _predefinedThemes["Default"];
            _currentThemeName = "Default";
            _isCustomTheme = false;
        }
    }

    private void SaveThemeSettings()
    {
        var config = _uIThemeConfigService.Current;
        config.SelectedTheme = _currentThemeName;
        config.UseCustomTheme = _isCustomTheme;

        // Save custom theme data if using a custom theme
        if (_isCustomTheme)
        {
            config.CustomThemeData = _currentTheme;
        }

        _uIThemeConfigService.Save();
    }

    private static Dictionary<string, ThemePalette> CreatePredefinedThemes()
    {
        var themes = new Dictionary<string, ThemePalette>
        {
            ["Default"] = new ThemePalette(),
        };

        // Add all themes from ThemePresets
        foreach (var preset in ThemePresets.Presets)
        {
            themes[preset.Key] = preset.Value;
        }

        return themes;
    }
}