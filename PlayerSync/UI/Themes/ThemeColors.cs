using Dalamud.Interface.Colors;
using System.Numerics;

namespace MareSynchronos.UI.Themes;

/// <summary>
/// Smart theme color helper that provides backward compatibility with existing code
/// while allowing for modern theming capabilities
/// </summary>
public static class ThemeColors
{
    private static ThemeManager? _themeManager;
    
    public static void Initialize(ThemeManager themeManager)
    {
        _themeManager = themeManager;
    }

    // Smart color getters that fall back to Dalamud colors if theme manager not available
    public static Vector4 Text => _themeManager?.CurrentTheme.Text ?? ImGuiColors.DalamudWhite;
    public static Vector4 TextDisabled => _themeManager?.CurrentTheme.TextDisabled ?? ImGuiColors.DalamudGrey;
    public static Vector4 Background => _themeManager?.CurrentTheme.Background ?? Vector4.Zero;
    public static Vector4 Primary => _themeManager?.CurrentTheme.Primary ?? ImGuiColors.ParsedBlue;
    public static Vector4 Secondary => _themeManager?.CurrentTheme.Secondary ?? ImGuiColors.DalamudGrey;
    public static Vector4 Success => _themeManager?.CurrentTheme.Success ?? ImGuiColors.HealerGreen;
    public static Vector4 Warning => _themeManager?.CurrentTheme.Warning ?? ImGuiColors.DalamudYellow;
    public static Vector4 Error => _themeManager?.CurrentTheme.Error ?? ImGuiColors.DalamudRed;
    public static Vector4 Border => _themeManager?.CurrentTheme.Border ?? ImGuiColors.DalamudGrey;
    public static Vector4 Hover => _themeManager?.CurrentTheme.Hover ?? ImGuiColors.DalamudGrey2;
    public static Vector4 Active => _themeManager?.CurrentTheme.Active ?? ImGuiColors.DalamudGrey3;

    // Status colors with fallbacks
    public static Vector4 Connected => Success;
    public static Vector4 Connecting => Warning;
    public static Vector4 Disconnected => Error;
    public static Vector4 Offline => Error;
    public static Vector4 Online => Success;

    // UI element colors
    public static Vector4 Button => Primary;
    public static Vector4 ButtonHovered => Primary with { W = 0.8f };
    public static Vector4 ButtonActive => Primary with { W = 1.0f };

    // Navigation colors
    public static Vector4 NavBackground => _themeManager?.CurrentTheme.NavBackground ?? Background;
    public static Vector4 NavItemHover => _themeManager?.CurrentTheme.NavItemHover ?? Hover;
    public static Vector4 NavItemActive => _themeManager?.CurrentTheme.NavItemActive ?? Active;
    public static Vector4 NavSeparator => _themeManager?.CurrentTheme.NavSeparator ?? Border;

    // Backward compatibility methods
    public static Vector4 GetStatusColor(bool isOnline)
    {
        return isOnline ? Online : Offline;
    }

    public static Vector4 GetConnectionColor(bool isConnected)
    {
        return isConnected ? Connected : Disconnected;
    }

    public static Vector4 GetBoolColor(bool value)
    {
        return value ? Success : Error;
    }

    // Color interpolation helpers
    public static Vector4 Lerp(Vector4 from, Vector4 to, float t)
    {
        t = Math.Max(0f, Math.Min(1f, t));
        return new Vector4(
            from.X + (to.X - from.X) * t,
            from.Y + (to.Y - from.Y) * t,
            from.Z + (to.Z - from.Z) * t,
            from.W + (to.W - from.W) * t
        );
    }

    public static Vector4 WithAlpha(Vector4 color, float alpha)
    {
        return color with { W = Math.Max(0f, Math.Min(1f, alpha)) };
    }

    // Theme-aware color variants
    public static Vector4 GetVariant(Vector4 baseColor, float factor)
    {
        if (factor > 0f)
        {
            // Lighten
            return Lerp(baseColor, Vector4.One, factor);
        }
        else
        {
            // Darken
            return Lerp(baseColor, Vector4.Zero, -factor);
        }
    }

    public static Vector4 Lighten(Vector4 color, float amount = 0.1f)
    {
        return GetVariant(color, amount);
    }

    public static Vector4 Darken(Vector4 color, float amount = 0.1f)
    {
        return GetVariant(color, -amount);
    }

    // Current theme access
    public static Theme? CurrentTheme => _themeManager?.CurrentTheme;
    public static bool IsThemeActive => _themeManager != null;
}