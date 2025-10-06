using MareSynchronos.UI.Components.Theming;

namespace MareSynchronos.MareConfiguration.Configurations;

public class UIThemeConfig : IMareConfiguration
{
    public int Version { get; set; } = 1;
    public string SelectedTheme { get; set; } = "Default";
    public bool UseCustomTheme { get; set; } = false;
    public ThemePalette? CustomThemeData { get; set; } = null;
}