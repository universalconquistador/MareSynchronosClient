using System.Collections.Generic;
using System.Numerics;

namespace MareSynchronos.UI.Themes;

public static class PresetThemes
{
    public static readonly Dictionary<string, Theme> Presets = new()
    {
        // =========================
        // Core / Neutral
        // =========================
        ["Blue"] = new Theme
        {
            Name = "Blue",
            Primary = new(0.35f, 0.70f, 1.00f, 1.00f),
            Secondary = new(0.55f, 0.82f, 1.00f, 1.00f),
            Background = new(0.06f, 0.07f, 0.10f, 1.00f),
            BackgroundSecondary = new(0.08f, 0.09f, 0.12f, 1.00f),
            Surface = new(0.10f, 0.12f, 0.16f, 1.00f),
            Text = new(0.85f, 0.90f, 1.00f, 1.00f),
            TextDisabled = new(0.50f, 0.56f, 0.66f, 1.00f),
            Border = new(0.35f, 0.70f, 1.00f, 0.3f),
            Success = new(0.35f, 0.70f, 1.00f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.35f, 0.70f, 1.00f, 1.00f),
            Accent = new(0.35f, 0.70f, 1.00f, 1.00f),
            Hover = new(0.35f, 0.70f, 1.00f, 0.2f),
            Active = new(0.35f, 0.70f, 1.00f, 0.4f),
            NavBackground = new(0.06f, 0.07f, 0.10f, 1.00f),
            NavItemHover = new(0.35f, 0.70f, 1.00f, 0.3f),
            NavItemActive = new(0.35f, 0.70f, 1.00f, 0.5f),
            NavSeparator = new(0.35f, 0.70f, 1.00f, 0.2f)
        },

        ["Mint"] = new Theme
        {
            Name = "Mint",
            Primary = new(0.10f, 0.90f, 0.75f, 1.00f),
            Secondary = new(0.20f, 0.90f, 0.78f, 1.00f),
            Background = new(0.06f, 0.09f, 0.10f, 1.00f),
            BackgroundSecondary = new(0.08f, 0.11f, 0.12f, 1.00f),
            Surface = new(0.10f, 0.16f, 0.16f, 1.00f),
            Text = new(0.86f, 0.96f, 0.92f, 1.00f),
            TextDisabled = new(0.52f, 0.68f, 0.64f, 1.00f),
            Border = new(0.10f, 0.80f, 0.65f, 1.00f),
            Success = new(0.10f, 0.90f, 0.75f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.20f, 0.90f, 0.78f, 1.00f),
            Accent = new(0.10f, 0.90f, 0.75f, 1.00f),
            Hover = new(0.10f, 0.80f, 0.65f, 0.3f),
            Active = new(0.08f, 0.60f, 0.50f, 1.00f),
            NavBackground = new(0.06f, 0.20f, 0.18f, 1.00f),
            NavItemHover = new(0.10f, 0.80f, 0.65f, 0.3f),
            NavItemActive = new(0.10f, 0.90f, 0.75f, 0.5f),
            NavSeparator = new(0.10f, 0.80f, 0.65f, 0.2f)
        },

        ["Purple"] = new Theme
        {
            Name = "Purple",
            Primary = new(0.75f, 0.55f, 1.00f, 1.00f),
            Secondary = new(0.80f, 0.62f, 1.00f, 1.00f),
            Background = new(0.08f, 0.07f, 0.12f, 1.00f),
            BackgroundSecondary = new(0.10f, 0.09f, 0.14f, 1.00f),
            Surface = new(0.18f, 0.15f, 0.28f, 1.00f),
            Text = new(0.90f, 0.88f, 0.98f, 1.00f),
            TextDisabled = new(0.56f, 0.52f, 0.70f, 1.00f),
            Border = new(0.60f, 0.40f, 0.95f, 1.00f),
            Success = new(0.35f, 0.80f, 0.35f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.75f, 0.55f, 1.00f, 1.00f),
            Accent = new(0.75f, 0.55f, 1.00f, 1.00f),
            Hover = new(0.60f, 0.40f, 0.95f, 0.3f),
            Active = new(0.48f, 0.30f, 0.85f, 1.00f),
            NavBackground = new(0.18f, 0.12f, 0.28f, 1.00f),
            NavItemHover = new(0.60f, 0.40f, 0.95f, 0.3f),
            NavItemActive = new(0.75f, 0.55f, 1.00f, 0.5f),
            NavSeparator = new(0.60f, 0.40f, 0.95f, 0.2f)
        },

        ["Dark"] = new Theme
        {
            Name = "Dark",
            Primary = new(0.80f, 0.80f, 0.80f, 1.00f),
            Secondary = new(0.70f, 0.70f, 0.70f, 1.00f),
            Background = new(0.06f, 0.06f, 0.06f, 1.00f),
            BackgroundSecondary = new(0.08f, 0.08f, 0.08f, 1.00f),
            Surface = new(0.14f, 0.14f, 0.14f, 1.00f),
            Text = new(0.88f, 0.88f, 0.90f, 1.00f),
            TextDisabled = new(0.56f, 0.56f, 0.60f, 1.00f),
            Border = new(0.35f, 0.35f, 0.38f, 1.00f),
            Success = new(0.35f, 0.80f, 0.35f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.45f, 0.75f, 1.00f, 1.00f),
            Accent = new(0.80f, 0.80f, 0.80f, 1.00f),
            Hover = new(0.35f, 0.35f, 0.38f, 0.5f),
            Active = new(0.28f, 0.28f, 0.30f, 1.00f),
            NavBackground = new(0.10f, 0.10f, 0.10f, 1.00f),
            NavItemHover = new(0.35f, 0.35f, 0.38f, 0.3f),
            NavItemActive = new(0.80f, 0.80f, 0.80f, 0.3f),
            NavSeparator = new(0.35f, 0.35f, 0.38f, 0.5f)
        },

        // =========================
        // Light Themes
        // =========================
        ["Sakura Daylight"] = new Theme
        {
            Name = "Sakura Daylight",
            Primary = new(0.93f, 0.40f, 0.66f, 1.00f),
            Secondary = new(0.85f, 0.52f, 0.72f, 1.00f),
            Background = new(0.98f, 0.95f, 0.97f, 1.00f),
            BackgroundSecondary = new(0.95f, 0.92f, 0.94f, 1.00f),
            Surface = new(0.94f, 0.88f, 0.92f, 1.00f),
            Text = new(0.12f, 0.10f, 0.14f, 1.00f),
            TextDisabled = new(0.60f, 0.52f, 0.60f, 1.00f),
            Border = new(0.85f, 0.52f, 0.72f, 1.00f),
            Success = new(0.35f, 0.80f, 0.35f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.93f, 0.40f, 0.66f, 1.00f),
            Accent = new(0.93f, 0.40f, 0.66f, 1.00f),
            Hover = new(0.93f, 0.40f, 0.66f, 0.2f),
            Active = new(0.82f, 0.32f, 0.56f, 1.00f),
            NavBackground = new(0.95f, 0.86f, 0.92f, 1.00f),
            NavItemHover = new(0.93f, 0.40f, 0.66f, 0.3f),
            NavItemActive = new(0.93f, 0.40f, 0.66f, 0.5f),
            NavSeparator = new(0.85f, 0.52f, 0.72f, 0.5f)
        },

        // =========================
        // Dark Cyberpunk & Neon
        // =========================
        ["Midnight Neon"] = new Theme
        {
            Name = "Midnight Neon",
            Primary = new(0.10f, 0.95f, 0.90f, 1.00f),
            Secondary = new(0.08f, 0.85f, 0.90f, 1.00f),
            Background = new(0.05f, 0.06f, 0.09f, 1.00f),
            BackgroundSecondary = new(0.07f, 0.08f, 0.11f, 1.00f),
            Surface = new(0.10f, 0.12f, 0.18f, 1.00f),
            Text = new(0.86f, 0.94f, 0.98f, 1.00f),
            TextDisabled = new(0.52f, 0.68f, 0.72f, 1.00f),
            Border = new(0.08f, 0.85f, 0.90f, 1.00f),
            Success = new(0.10f, 0.95f, 0.90f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.14f, 0.92f, 0.92f, 1.00f),
            Accent = new(0.10f, 0.95f, 0.90f, 1.00f),
            Hover = new(0.08f, 0.85f, 0.90f, 0.3f),
            Active = new(0.06f, 0.70f, 0.75f, 1.00f),
            NavBackground = new(0.08f, 0.10f, 0.16f, 1.00f),
            NavItemHover = new(0.08f, 0.85f, 0.90f, 0.3f),
            NavItemActive = new(0.10f, 0.95f, 0.90f, 0.3f),
            NavSeparator = new(0.08f, 0.85f, 0.90f, 0.5f)
        },

        ["Cyberpunk"] = new Theme
        {
            Name = "Cyberpunk",
            Primary = new(0.98f, 0.20f, 0.66f, 1.00f),
            Secondary = new(0.18f, 0.90f, 0.95f, 1.00f),
            Background = new(0.06f, 0.05f, 0.09f, 1.00f),
            BackgroundSecondary = new(0.08f, 0.07f, 0.11f, 1.00f),
            Surface = new(0.12f, 0.10f, 0.18f, 1.00f),
            Text = new(0.92f, 0.90f, 0.98f, 1.00f),
            TextDisabled = new(0.58f, 0.56f, 0.74f, 1.00f),
            Border = new(0.98f, 0.20f, 0.66f, 1.00f),
            Success = new(0.18f, 0.90f, 0.95f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(0.98f, 0.20f, 0.66f, 1.00f),
            Info = new(0.18f, 0.90f, 0.95f, 1.00f),
            Accent = new(0.98f, 0.20f, 0.66f, 1.00f),
            Hover = new(0.98f, 0.20f, 0.66f, 0.3f),
            Active = new(0.16f, 0.74f, 0.78f, 1.00f),
            NavBackground = new(0.10f, 0.08f, 0.16f, 1.00f),
            NavItemHover = new(0.98f, 0.20f, 0.66f, 0.3f),
            NavItemActive = new(0.98f, 0.20f, 0.66f, 0.5f),
            NavSeparator = new(0.98f, 0.20f, 0.66f, 0.2f)
        },

        // =========================
        // Pride Collection
        // =========================
        ["Pride Rainbow"] = new Theme
        {
            Name = "Pride Rainbow",
            Primary = new(1.00f, 0.00f, 0.00f, 1.00f), // Red
            Secondary = new(1.00f, 0.65f, 0.00f, 1.00f), // Orange
            Background = new(0.08f, 0.05f, 0.08f, 1.00f),
            BackgroundSecondary = new(0.10f, 0.07f, 0.10f, 1.00f),
            Surface = new(0.15f, 0.10f, 0.15f, 1.00f),
            Text = new(0.95f, 0.92f, 0.95f, 1.00f),
            TextDisabled = new(0.60f, 0.55f, 0.60f, 1.00f),
            Border = new(1.00f, 0.65f, 0.00f, 1.00f),
            Success = new(0.00f, 0.80f, 0.00f, 1.00f), // Green
            Warning = new(1.00f, 1.00f, 0.00f, 1.00f), // Yellow
            Error = new(1.00f, 0.00f, 0.00f, 1.00f), // Red
            Info = new(0.00f, 0.50f, 1.00f, 1.00f), // Blue
            Accent = new(0.60f, 0.00f, 1.00f, 1.00f), // Purple
            Hover = new(1.00f, 0.65f, 0.00f, 0.3f),
            Active = new(1.00f, 1.00f, 0.00f, 0.3f),
            NavBackground = new(0.12f, 0.08f, 0.12f, 1.00f),
            NavItemHover = new(1.00f, 0.65f, 0.00f, 0.3f),
            NavItemActive = new(0.60f, 0.00f, 1.00f, 0.3f),
            NavSeparator = new(1.00f, 0.65f, 0.00f, 0.5f)
        },

        ["Trans Pride"] = new Theme
        {
            Name = "Trans Pride",
            Primary = new(0.96f, 0.63f, 0.76f, 1.00f), // Pink
            Secondary = new(0.36f, 0.81f, 0.98f, 1.00f), // Light blue
            Background = new(0.06f, 0.08f, 0.10f, 1.00f),
            BackgroundSecondary = new(0.08f, 0.10f, 0.12f, 1.00f),
            Surface = new(0.12f, 0.14f, 0.16f, 1.00f),
            Text = new(0.96f, 0.98f, 1.00f, 1.00f), // White
            TextDisabled = new(0.55f, 0.60f, 0.70f, 1.00f),
            Border = new(0.36f, 0.81f, 0.98f, 1.00f),
            Success = new(0.35f, 0.80f, 0.35f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.36f, 0.81f, 0.98f, 1.00f),
            Accent = new(0.96f, 0.63f, 0.76f, 1.00f),
            Hover = new(0.96f, 0.63f, 0.76f, 0.3f),
            Active = new(0.36f, 0.81f, 0.98f, 0.5f),
            NavBackground = new(0.08f, 0.10f, 0.12f, 1.00f),
            NavItemHover = new(0.96f, 0.63f, 0.76f, 0.3f),
            NavItemActive = new(0.36f, 0.81f, 0.98f, 0.3f),
            NavSeparator = new(0.96f, 0.63f, 0.76f, 0.2f)
        },

        // =========================
        // Fun Character Themes
        // =========================
        ["PixxieStixx Fairy"] = new Theme
        {
            Name = "PixxieStixx Fairy",
            Primary = new(1.00f, 0.41f, 0.70f, 1.00f),
            Secondary = new(1.00f, 0.20f, 0.58f, 1.00f),
            Background = new(0.08f, 0.05f, 0.08f, 1.00f),
            BackgroundSecondary = new(0.10f, 0.07f, 0.10f, 1.00f),
            Surface = new(0.15f, 0.10f, 0.15f, 1.00f),
            Text = new(1.00f, 0.95f, 0.98f, 1.00f),
            TextDisabled = new(0.65f, 0.50f, 0.60f, 1.00f),
            Border = new(1.00f, 0.41f, 0.70f, 1.00f),
            Success = new(0.35f, 0.80f, 0.35f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.95f, 0.25f, 0.65f, 1.00f),
            Accent = new(1.00f, 0.20f, 0.58f, 1.00f),
            Hover = new(1.00f, 0.20f, 0.58f, 0.3f),
            Active = new(0.90f, 0.30f, 0.60f, 1.00f),
            NavBackground = new(0.12f, 0.08f, 0.12f, 1.00f),
            NavItemHover = new(1.00f, 0.20f, 0.58f, 0.3f),
            NavItemActive = new(1.00f, 0.41f, 0.70f, 0.5f),
            NavSeparator = new(1.00f, 0.41f, 0.70f, 0.2f)
        },

        ["Ocean Deep"] = new Theme
        {
            Name = "Ocean Deep",
            Primary = new(0.18f, 0.70f, 0.98f, 1.00f),
            Secondary = new(0.16f, 0.58f, 0.86f, 1.00f),
            Background = new(0.04f, 0.07f, 0.11f, 1.00f),
            BackgroundSecondary = new(0.06f, 0.09f, 0.13f, 1.00f),
            Surface = new(0.10f, 0.14f, 0.20f, 1.00f),
            Text = new(0.86f, 0.92f, 0.98f, 1.00f),
            TextDisabled = new(0.54f, 0.66f, 0.78f, 1.00f),
            Border = new(0.16f, 0.58f, 0.86f, 1.00f),
            Success = new(0.35f, 0.80f, 0.35f, 1.00f),
            Warning = new(1.00f, 0.65f, 0.00f, 1.00f),
            Error = new(1.00f, 0.32f, 0.32f, 1.00f),
            Info = new(0.22f, 0.70f, 1.00f, 1.00f),
            Accent = new(0.18f, 0.70f, 0.98f, 1.00f),
            Hover = new(0.16f, 0.58f, 0.86f, 0.3f),
            Active = new(0.14f, 0.48f, 0.72f, 1.00f),
            NavBackground = new(0.07f, 0.11f, 0.16f, 1.00f),
            NavItemHover = new(0.16f, 0.58f, 0.86f, 0.3f),
            NavItemActive = new(0.18f, 0.70f, 0.98f, 0.3f),
            NavSeparator = new(0.16f, 0.58f, 0.86f, 0.2f)
        }
    };

    public static void LoadPresetsIntoThemeManager(ThemeManager themeManager)
    {
        foreach (var preset in Presets)
        {
            themeManager.AddTheme(preset.Value);
        }
    }
}