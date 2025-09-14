using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;

namespace MareSynchronos.UI.Themes;

public class ThemeManager : DisposableMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly string _themesPath;
    private Theme _currentTheme;
    private readonly Dictionary<string, Theme> _availableThemes = new();

    public ThemeManager(ILogger<ThemeManager> logger, MareConfigService configService, MareMediator mediator)
        : base(logger, mediator)
    {
        _configService = configService;
        _themesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "pluginConfigs", "MareSynchronos", "themes");
        
        EnsureThemeDirectory();
        LoadDefaultThemes();
        
        // Load preset themes from the old sync_client collection
        PresetThemes.LoadPresetsIntoThemeManager(this);
        
        LoadCustomThemes();
        
        var savedThemeName = _configService.Current.SelectedTheme ?? "Default Dark";
        _currentTheme = _availableThemes.TryGetValue(savedThemeName, out var theme) ? theme : _availableThemes["Default Dark"];
    }

    public Theme CurrentTheme => _currentTheme;
    public IReadOnlyDictionary<string, Theme> AvailableThemes => _availableThemes;

    public event Action<Theme>? ThemeChanged;

    public void SetTheme(string themeName)
    {
        if (_availableThemes.TryGetValue(themeName, out var theme))
        {
            _currentTheme = theme;
            _configService.Current.SelectedTheme = themeName;
            _configService.Save();
            ThemeChanged?.Invoke(_currentTheme);
            Logger.LogInformation("Theme changed to {ThemeName}", themeName);
        }
    }

    public void AddTheme(Theme theme)
    {
        _availableThemes[theme.Name] = theme;
        Logger.LogInformation("Theme {ThemeName} added to theme manager", theme.Name);
    }

    public void SaveTheme(Theme theme)
    {
        try
        {
            var themeJson = JsonSerializer.Serialize(theme, new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(_themesPath, $"{theme.Name}.json");
            File.WriteAllText(filePath, themeJson);
            
            _availableThemes[theme.Name] = theme;
            Logger.LogInformation("Theme {ThemeName} saved successfully", theme.Name);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save theme {ThemeName}", theme.Name);
        }
    }

    private void EnsureThemeDirectory()
    {
        if (!Directory.Exists(_themesPath))
        {
            Directory.CreateDirectory(_themesPath);
        }
    }

    private void LoadDefaultThemes()
    {
        var darkTheme = new Theme
        {
            Name = "Default Dark",
            Background = new Vector4(0.12f, 0.12f, 0.13f, 1.0f),
            BackgroundSecondary = new Vector4(0.18f, 0.18f, 0.20f, 1.0f),
            Surface = new Vector4(0.24f, 0.24f, 0.26f, 1.0f),
            Primary = new Vector4(0.40f, 0.60f, 0.95f, 1.0f),
            Secondary = new Vector4(0.70f, 0.70f, 0.75f, 1.0f),
            Accent = new Vector4(0.95f, 0.60f, 0.40f, 1.0f),
            Text = new Vector4(0.95f, 0.95f, 0.95f, 1.0f),
            TextSecondary = new Vector4(0.80f, 0.80f, 0.85f, 1.0f),
            TextDisabled = new Vector4(0.50f, 0.50f, 0.55f, 1.0f),
            Success = new Vector4(0.30f, 0.85f, 0.40f, 1.0f),
            Warning = new Vector4(1.0f, 0.75f, 0.30f, 1.0f),
            Error = new Vector4(0.95f, 0.35f, 0.35f, 1.0f),
            Border = new Vector4(0.30f, 0.30f, 0.32f, 1.0f),
            Hover = new Vector4(0.35f, 0.35f, 0.38f, 1.0f),
            Active = new Vector4(0.45f, 0.45f, 0.48f, 1.0f),
            TitleBarBackground = new Vector4(0.12f, 0.12f, 0.13f, 1.0f),
            TransparentTitleBar = true,
            WindowRounding = 8.0f,
            FrameRounding = 4.0f,
            ItemSpacing = new Vector2(8.0f, 4.0f),
            WindowPadding = new Vector2(12.0f, 12.0f),
        };

        var lightTheme = new Theme
        {
            Name = "Default Light",
            Background = new Vector4(0.95f, 0.95f, 0.95f, 1.0f),
            BackgroundSecondary = new Vector4(0.88f, 0.88f, 0.88f, 1.0f),
            Surface = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
            Primary = new Vector4(0.25f, 0.45f, 0.85f, 1.0f),
            Secondary = new Vector4(0.35f, 0.35f, 0.40f, 1.0f),
            Accent = new Vector4(0.85f, 0.45f, 0.25f, 1.0f),
            Text = new Vector4(0.15f, 0.15f, 0.15f, 1.0f),
            TextSecondary = new Vector4(0.30f, 0.30f, 0.30f, 1.0f),
            TextDisabled = new Vector4(0.60f, 0.60f, 0.60f, 1.0f),
            Success = new Vector4(0.20f, 0.70f, 0.30f, 1.0f),
            Warning = new Vector4(0.90f, 0.60f, 0.15f, 1.0f),
            Error = new Vector4(0.85f, 0.25f, 0.25f, 1.0f),
            Border = new Vector4(0.70f, 0.70f, 0.70f, 1.0f),
            Hover = new Vector4(0.92f, 0.92f, 0.92f, 1.0f),
            Active = new Vector4(0.85f, 0.85f, 0.85f, 1.0f),
            TitleBarBackground = new Vector4(0.95f, 0.95f, 0.95f, 1.0f),
            TransparentTitleBar = true,
            WindowRounding = 8.0f,
            FrameRounding = 4.0f,
            ItemSpacing = new Vector2(8.0f, 4.0f),
            WindowPadding = new Vector2(12.0f, 12.0f),
        };

        _availableThemes[darkTheme.Name] = darkTheme;
        _availableThemes[lightTheme.Name] = lightTheme;
    }

    private void LoadCustomThemes()
    {
        if (!Directory.Exists(_themesPath)) return;

        foreach (var filePath in Directory.GetFiles(_themesPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var theme = JsonSerializer.Deserialize<Theme>(json);
                if (theme != null && !_availableThemes.ContainsKey(theme.Name))
                {
                    _availableThemes[theme.Name] = theme;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to load theme from {FilePath}", filePath);
            }
        }
    }
}