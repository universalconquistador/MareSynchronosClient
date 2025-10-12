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
    private bool _isCustomTheme;
    private ThemePalette? _savedCustomTheme;
    private readonly float _classicWindowWidth = 375f;
    private readonly float _baseWindowWidth = 400f;
    private readonly float _baseWindowHeightMin = 400f;
    private readonly float _baseWindowHeightMax = 2000f;
    private readonly float _baseCollapsedWindowHeight = 24f;
    private readonly float _spacing = 6f;
    private readonly float _padding = 5f;

    public ThemeManager(UIThemeConfigService uIThemeConfigService)
    {
        _uIThemeConfigService = uIThemeConfigService;
        _predefinedThemes = CreatePredefinedThemes();
        LoadSavedTheme();
    }

    private string _currentThemeName = "Classic";
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
    public bool UsingDalamudTheme => _uIThemeConfigService.Current.UseDalamudTheme;
    public UIThemeConfigService UIThemeConfig => _uIThemeConfigService;
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
    public WindowSizeConstraints ClassicUISizeConstraints => new WindowSizeConstraints()
    {
        MinimumSize = new Vector2(_classicWindowWidth, _baseWindowHeightMin),
        MaximumSize = new Vector2(_classicWindowWidth, _baseWindowHeightMax),
    };
    public float Spacing => _spacing;
    public float Padding => _padding;
    public float ScaledSpacing => _spacing * ImGuiHelpers.GlobalScale;

    public ThemePalette ActiveThemeToThemePalette()
    {
        var style = ImGui.GetStyle();
        var colors = style.Colors;

        var theme = new ThemePalette
        {
            // Core panel colors
            PanelBg = colors[(int)ImGuiCol.WindowBg],
            PanelBorder = colors[(int)ImGuiCol.Border],
            HeaderBg = colors[(int)ImGuiCol.Header], // could also use TitleBg/TitleBgActive

            // Accent(s)
            Accent = colors[(int)ImGuiCol.CheckMark],
            Accent2 = colors[(int)ImGuiCol.SliderGrab], // reasonable second accent

            // Buttons
            Btn = colors[(int)ImGuiCol.Button],
            BtnHovered = colors[(int)ImGuiCol.ButtonHovered],
            BtnActive = colors[(int)ImGuiCol.ButtonActive],
            // ImGui has no explicit "button text" colors; reuse text colors
            BtnText = colors[(int)ImGuiCol.Text],
            BtnTextHovered = colors[(int)ImGuiCol.Text],
            BtnTextActive = colors[(int)ImGuiCol.Text],

            // Text
            TextPrimary = colors[(int)ImGuiCol.Text],
            TextDisabled = colors[(int)ImGuiCol.TextDisabled],
            // Best-effort mappings for secondary/muted
            TextSecondary = colors[(int)ImGuiCol.Text],
            TextMuted = colors[(int)ImGuiCol.TextDisabled],
            TextMuted2 = colors[(int)ImGuiCol.TextDisabled],

            // Links (ImGui has no link color; pick something sensible)
            Link = colors[(int)ImGuiCol.HeaderHovered],
            LinkHover = colors[(int)ImGuiCol.HeaderActive],

            // Tooltip / popups
            TooltipBg = colors[(int)ImGuiCol.PopupBg],
            TooltipText = colors[(int)ImGuiCol.Text],

            // “Status” colors (pick reasonable mappings)
            StatusOk = colors[(int)ImGuiCol.CheckMark],
            StatusWarn = colors[(int)ImGuiCol.SeparatorHovered],
            StatusError = colors[(int)ImGuiCol.TextSelectedBg],
            StatusPaused = colors[(int)ImGuiCol.TabUnfocused],
            StatusInfo = colors[(int)ImGuiCol.SliderGrab],

            // Connection/service status (reuse)
            StatusConnected = colors[(int)ImGuiCol.CheckMark],
            StatusConnecting = colors[(int)ImGuiCol.SliderGrabActive],
            StatusDisconnected = colors[(int)ImGuiCol.TabUnfocused],
            StatusBroadcasting = colors[(int)ImGuiCol.TextSelectedBg],

            // UI text accents
            UidAliasText = colors[(int)ImGuiCol.Text],
            UsersOnlineText = colors[(int)ImGuiCol.TextDisabled],
            UsersOnlineNumber = colors[(int)ImGuiCol.Text],

            // Surfaces / layers
            BackgroundOpacity = colors[(int)ImGuiCol.WindowBg].W,
            Surface0 = colors[(int)ImGuiCol.ChildBg],
            Surface1 = colors[(int)ImGuiCol.TableRowBgAlt],
            Surface2 = colors[(int)ImGuiCol.TableHeaderBg],
            Surface3 = colors[(int)ImGuiCol.MenuBarBg],

            // Design tokens (leave as-is if you already manage them elsewhere)
            RadiusSmall = style.FrameRounding * 0.5f,
            RadiusMedium = style.FrameRounding,
            RadiusLarge = style.WindowRounding,
            SpacingXS = 2f,
            SpacingS = 4f,
            SpacingM = 6f,
            SpacingL = 8f,

            // Rounding from style vars
            WindowRounding = style.WindowRounding,
            ChildRounding = style.ChildRounding,
            FrameRounding = style.FrameRounding,
            PopupRounding = style.PopupRounding,
            ScrollbarRounding = style.ScrollbarRounding,
            GrabRounding = style.GrabRounding,
            TabRounding = style.TabRounding,
        };

        return theme;
    }

    public void SetTheme(string themeName)
    {
        if (_predefinedThemes.TryGetValue(themeName, out var theme))
        {
            if (_isCustomTheme)
            {
                _savedCustomTheme = _currentTheme;
            }
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
        _savedCustomTheme = customTheme;
        SaveThemeSettings();
    }

    public void SetThemeFromThemePalette(ThemePalette themePalette)
    {
        _currentTheme = themePalette;
        _currentThemeName = "Dalamud"; // this needs to be part of the theme palette
        _isCustomTheme = false;
        SaveThemeSettings();
    }

    public bool RestoreSavedCustomTheme()
    {
        if (_savedCustomTheme != null)
        {
            _currentTheme = _savedCustomTheme;
            _currentThemeName = "Custom";
            _isCustomTheme = true;
            SaveThemeSettings();
            return true;
        }
        return false;
    }

    public IDisposable PushTheme()
    {
        //ImGui.PushStyleColor(ImGuiCol.WindowBg, _currentTheme.PanelBg);  
        //ImGui.PushStyleColor(ImGuiCol.ChildBg, _currentTheme.PanelBg);
        //ImGui.PushStyleColor(ImGuiCol.Border, _currentTheme.PanelBorder); 
        //ImGui.PushStyleColor(ImGuiCol.TitleBg, _currentTheme.HeaderBg); 
        //ImGui.PushStyleColor(ImGuiCol.TitleBgActive, _currentTheme.HeaderBg);
        //ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, _currentTheme.HeaderBg);
        //ImGui.PushStyleColor(ImGuiCol.MenuBarBg, _currentTheme.HeaderBg);
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
        //ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, _currentTheme.WindowRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, _currentTheme.ChildRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, _currentTheme.FrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, _currentTheme.PopupRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, _currentTheme.ScrollbarRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, _currentTheme.GrabRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, _currentTheme.TabRounding);

        return new ThemeScope(37, 6);
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
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
            _savedCustomTheme = config.CustomThemeData; // Also save for future reference
        }
        else if (_predefinedThemes.TryGetValue(config.SelectedTheme, out var theme))
        {
            _currentTheme = theme;
            _currentThemeName = config.SelectedTheme;
            _isCustomTheme = false;
            // Keep any existing saved custom theme from config
            _savedCustomTheme = config.CustomThemeData;
        }
        else
        {
            _currentTheme = _predefinedThemes["Classic"];
            _currentThemeName = "Classic";
            _isCustomTheme = false;
            // Keep any existing saved custom theme from config
            _savedCustomTheme = config.CustomThemeData;
        }
    }

    private void SaveThemeSettings()
    {
        var config = _uIThemeConfigService.Current;
        config.SelectedTheme = _currentThemeName;
        config.UseCustomTheme = _isCustomTheme;

        // Always preserve custom theme data if we have any
        if (_savedCustomTheme != null)
        {
            config.CustomThemeData = _savedCustomTheme;
        }

        _uIThemeConfigService.Save();
    }

    private static Dictionary<string, ThemePalette> CreatePredefinedThemes()
    {
        
        var themes = new Dictionary<string, ThemePalette>
        {
            ["PlayerSync"] = new ThemePalette(),
        };
        themes.Add("Dalamud", ThemeImport.ImportDs1ToThemePalette(ThemePresets.DalamudDefaultTheme));

        // Add all themes from ThemePresets
        foreach (var preset in ThemePresets.Presets)
        {
            themes[preset.Key] = preset.Value;
        }

        return themes;
    }
}