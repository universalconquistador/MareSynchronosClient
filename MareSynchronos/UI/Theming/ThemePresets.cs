using System.Collections.Generic;
using MareSynchronos.UI;

namespace MareSynchronos.UI.Theming;

public static class ThemePresets
{
    public static readonly Dictionary<string, ThemePalette> Presets = new()
    {
        // =========================
        // Core / Neutral
        // =========================
        ["Blue"] = new ThemePalette
        {
            // (Blue uses ThemePalette defaults for base colors)
            // Text & Links for dark UI
            TextPrimary = new(0.85f, 0.90f, 1.00f, 1.00f),
            TextSecondary = new(0.70f, 0.76f, 0.88f, 1.00f),
            TextDisabled = new(0.50f, 0.56f, 0.66f, 1.00f),
            Link = new(0.35f, 0.70f, 1.00f, 1.00f),
            LinkHover = new(0.55f, 0.82f, 1.00f, 1.00f),
        },

        ["Mint"] = new ThemePalette
        {
            PanelBg = new(0.06f, 0.09f, 0.10f, 0.98f),
            PanelBorder = new(0.10f, 0.80f, 0.65f, 1.00f),
            HeaderBg = new(0.06f, 0.20f, 0.18f, 1.00f),
            Accent = new(0.10f, 0.90f, 0.75f, 1.00f),
            Btn = new(0.10f, 0.16f, 0.16f, 1.00f),
            BtnHovered = new(0.10f, 0.80f, 0.65f, 1.00f),
            BtnActive = new(0.08f, 0.60f, 0.50f, 1.00f),

            TextPrimary = new(0.86f, 0.96f, 0.92f, 1.00f),
            TextSecondary = new(0.72f, 0.86f, 0.80f, 1.00f),
            TextDisabled = new(0.52f, 0.68f, 0.64f, 1.00f),
            Link = new(0.20f, 0.90f, 0.78f, 1.00f),
            LinkHover = new(0.36f, 0.96f, 0.86f, 1.00f),
        },

        ["Purple"] = new ThemePalette
        {
            PanelBg = new(0.08f, 0.07f, 0.12f, 0.98f),
            PanelBorder = new(0.60f, 0.40f, 0.95f, 1.00f),
            HeaderBg = new(0.18f, 0.12f, 0.28f, 1.00f),
            Accent = new(0.75f, 0.55f, 1.00f, 1.00f),
            Btn = new(0.18f, 0.15f, 0.28f, 1.00f),
            BtnHovered = new(0.60f, 0.40f, 0.95f, 1.00f),
            BtnActive = new(0.48f, 0.30f, 0.85f, 1.00f),

            TextPrimary = new(0.90f, 0.88f, 0.98f, 1.00f),
            TextSecondary = new(0.76f, 0.72f, 0.88f, 1.00f),
            TextDisabled = new(0.56f, 0.52f, 0.70f, 1.00f),
            Link = new(0.80f, 0.62f, 1.00f, 1.00f),
            LinkHover = new(0.88f, 0.72f, 1.00f, 1.00f),
        },

        ["Dark"] = new ThemePalette
        {
            PanelBg = new(0.06f, 0.06f, 0.06f, 0.98f),
            PanelBorder = new(0.35f, 0.35f, 0.38f, 1.00f),
            HeaderBg = new(0.10f, 0.10f, 0.10f, 1.00f),
            Accent = new(0.80f, 0.80f, 0.80f, 1.00f),
            Btn = new(0.14f, 0.14f, 0.14f, 1.00f),
            BtnHovered = new(0.35f, 0.35f, 0.38f, 1.00f),
            BtnActive = new(0.28f, 0.28f, 0.30f, 1.00f),

            TextPrimary = new(0.88f, 0.88f, 0.90f, 1.00f),
            TextSecondary = new(0.74f, 0.74f, 0.78f, 1.00f),
            TextDisabled = new(0.56f, 0.56f, 0.60f, 1.00f),
            Link = new(0.45f, 0.75f, 1.00f, 1.00f),
            LinkHover = new(0.62f, 0.85f, 1.00f, 1.00f),
        },

        // =========================
        // Bright / Light Backgrounds
        // =========================
        ["Sakura Daylight"] = new ThemePalette
        {
            PanelBg = new(0.98f, 0.95f, 0.97f, 0.98f),
            PanelBorder = new(0.85f, 0.52f, 0.72f, 1.00f),
            HeaderBg = new(0.95f, 0.86f, 0.92f, 1.00f),
            Accent = new(0.93f, 0.40f, 0.66f, 1.00f),
            Btn = new(0.94f, 0.88f, 0.92f, 1.00f),
            BtnHovered = new(0.93f, 0.40f, 0.66f, 1.00f),
            BtnActive = new(0.82f, 0.32f, 0.56f, 1.00f),

            TextPrimary = new(0.12f, 0.10f, 0.14f, 1.00f),
            TextSecondary = new(0.35f, 0.28f, 0.38f, 1.00f),
            TextDisabled = new(0.60f, 0.52f, 0.60f, 1.00f),
            Link = new(0.85f, 0.32f, 0.62f, 1.00f),
            LinkHover = new(0.93f, 0.46f, 0.74f, 1.00f),
        },

        ["Cotton Candy"] = new ThemePalette
        {
            PanelBg = new(0.97f, 0.98f, 1.00f, 0.98f),
            PanelBorder = new(0.52f, 0.70f, 0.98f, 1.00f),
            HeaderBg = new(0.90f, 0.94f, 1.00f, 1.00f),
            Accent = new(0.98f, 0.60f, 0.78f, 1.00f),
            Btn = new(0.92f, 0.95f, 1.00f, 1.00f),
            BtnHovered = new(0.52f, 0.70f, 0.98f, 1.00f),
            BtnActive = new(0.43f, 0.58f, 0.90f, 1.00f),

            TextPrimary = new(0.10f, 0.12f, 0.16f, 1.00f),
            TextSecondary = new(0.32f, 0.36f, 0.46f, 1.00f),
            TextDisabled = new(0.58f, 0.62f, 0.72f, 1.00f),
            Link = new(0.28f, 0.58f, 0.98f, 1.00f),
            LinkHover = new(0.40f, 0.70f, 1.00f, 1.00f),
        },

        ["Peach Sunrise"] = new ThemePalette
        {
            PanelBg = new(1.00f, 0.97f, 0.93f, 0.98f),
            PanelBorder = new(0.98f, 0.64f, 0.42f, 1.00f),
            HeaderBg = new(1.00f, 0.92f, 0.84f, 1.00f),
            Accent = new(0.98f, 0.45f, 0.35f, 1.00f),
            Btn = new(1.00f, 0.94f, 0.88f, 1.00f),
            BtnHovered = new(0.98f, 0.64f, 0.42f, 1.00f),
            BtnActive = new(0.92f, 0.50f, 0.32f, 1.00f),

            TextPrimary = new(0.14f, 0.10f, 0.06f, 1.00f),
            TextSecondary = new(0.38f, 0.28f, 0.22f, 1.00f),
            TextDisabled = new(0.62f, 0.52f, 0.46f, 1.00f),
            Link = new(0.90f, 0.42f, 0.32f, 1.00f),
            LinkHover = new(0.98f, 0.52f, 0.42f, 1.00f),
        },

        ["Seafoam Day"] = new ThemePalette
        {
            PanelBg = new(0.95f, 0.99f, 0.97f, 0.98f),
            PanelBorder = new(0.24f, 0.75f, 0.60f, 1.00f),
            HeaderBg = new(0.88f, 0.96f, 0.93f, 1.00f),
            Accent = new(0.20f, 0.80f, 0.68f, 1.00f),
            Btn = new(0.90f, 0.96f, 0.93f, 1.00f),
            BtnHovered = new(0.24f, 0.75f, 0.60f, 1.00f),
            BtnActive = new(0.16f, 0.62f, 0.52f, 1.00f),

            TextPrimary = new(0.08f, 0.12f, 0.10f, 1.00f),
            TextSecondary = new(0.28f, 0.40f, 0.36f, 1.00f),
            TextDisabled = new(0.54f, 0.64f, 0.60f, 1.00f),
            Link = new(0.16f, 0.68f, 0.58f, 1.00f),
            LinkHover = new(0.24f, 0.80f, 0.68f, 1.00f),
        },

        ["Sky Light"] = new ThemePalette
        {
            PanelBg = new(0.97f, 0.98f, 1.00f, 0.98f),
            PanelBorder = new(0.30f, 0.60f, 0.98f, 1.00f),
            HeaderBg = new(0.90f, 0.94f, 1.00f, 1.00f),
            Accent = new(0.22f, 0.52f, 0.95f, 1.00f),
            Btn = new(0.92f, 0.95f, 1.00f, 1.00f),
            BtnHovered = new(0.30f, 0.60f, 0.98f, 1.00f),
            BtnActive = new(0.20f, 0.48f, 0.88f, 1.00f),

            TextPrimary = new(0.10f, 0.12f, 0.18f, 1.00f),
            TextSecondary = new(0.32f, 0.38f, 0.50f, 1.00f),
            TextDisabled = new(0.58f, 0.64f, 0.74f, 1.00f),
            Link = new(0.24f, 0.52f, 0.95f, 1.00f),
            LinkHover = new(0.36f, 0.64f, 1.00f, 1.00f),
        },

        ["Lilac Cream"] = new ThemePalette
        {
            PanelBg = new(0.98f, 0.97f, 1.00f, 0.98f),
            PanelBorder = new(0.62f, 0.48f, 0.95f, 1.00f),
            HeaderBg = new(0.94f, 0.92f, 0.99f, 1.00f),
            Accent = new(0.70f, 0.52f, 1.00f, 1.00f),
            Btn = new(0.95f, 0.94f, 1.00f, 1.00f),
            BtnHovered = new(0.62f, 0.48f, 0.95f, 1.00f),
            BtnActive = new(0.56f, 0.40f, 0.88f, 1.00f),

            TextPrimary = new(0.12f, 0.10f, 0.18f, 1.00f),
            TextSecondary = new(0.34f, 0.30f, 0.46f, 1.00f),
            TextDisabled = new(0.58f, 0.54f, 0.70f, 1.00f),
            Link = new(0.56f, 0.42f, 0.98f, 1.00f),
            LinkHover = new(0.68f, 0.54f, 1.00f, 1.00f),
        },

        ["Rose Quartz"] = new ThemePalette
        {
            PanelBg = new(1.00f, 0.98f, 0.99f, 0.98f),
            PanelBorder = new(0.92f, 0.56f, 0.66f, 1.00f),
            HeaderBg = new(0.97f, 0.90f, 0.93f, 1.00f),
            Accent = new(0.96f, 0.42f, 0.58f, 1.00f),
            Btn = new(0.98f, 0.94f, 0.96f, 1.00f),
            BtnHovered = new(0.92f, 0.56f, 0.66f, 1.00f),
            BtnActive = new(0.84f, 0.44f, 0.54f, 1.00f),

            TextPrimary = new(0.16f, 0.10f, 0.12f, 1.00f),
            TextSecondary = new(0.40f, 0.28f, 0.34f, 1.00f),
            TextDisabled = new(0.64f, 0.54f, 0.58f, 1.00f),
            Link = new(0.90f, 0.38f, 0.56f, 1.00f),
            LinkHover = new(0.98f, 0.48f, 0.66f, 1.00f),
        },

        ["High Contrast Light"] = new ThemePalette
        {
            PanelBg = new(1.00f, 1.00f, 1.00f, 0.98f),
            PanelBorder = new(0.00f, 0.00f, 0.00f, 1.00f),
            HeaderBg = new(0.94f, 0.94f, 0.94f, 1.00f),
            Accent = new(0.00f, 0.45f, 0.90f, 1.00f),
            Btn = new(0.97f, 0.97f, 0.97f, 1.00f),
            BtnHovered = new(0.00f, 0.45f, 0.90f, 1.00f),
            BtnActive = new(0.00f, 0.35f, 0.70f, 1.00f),

            TextPrimary = new(0.08f, 0.08f, 0.10f, 1.00f),
            TextSecondary = new(0.30f, 0.30f, 0.34f, 1.00f),
            TextDisabled = new(0.58f, 0.58f, 0.62f, 1.00f),
            Link = new(0.00f, 0.45f, 0.90f, 1.00f),
            LinkHover = new(0.12f, 0.56f, 1.00f, 1.00f),
        },

        // =========================
        // Cozy / Dark Backgrounds
        // =========================
        ["Midnight Neon"] = new ThemePalette
        {
            PanelBg = new(0.05f, 0.06f, 0.09f, 0.98f),
            PanelBorder = new(0.08f, 0.85f, 0.90f, 1.00f),
            HeaderBg = new(0.08f, 0.10f, 0.16f, 1.00f),
            Accent = new(0.10f, 0.95f, 0.90f, 1.00f),
            Btn = new(0.10f, 0.12f, 0.18f, 1.00f),
            BtnHovered = new(0.08f, 0.85f, 0.90f, 1.00f),
            BtnActive = new(0.06f, 0.70f, 0.75f, 1.00f),

            TextPrimary = new(0.86f, 0.94f, 0.98f, 1.00f),
            TextSecondary = new(0.70f, 0.84f, 0.90f, 1.00f),
            TextDisabled = new(0.52f, 0.68f, 0.72f, 1.00f),
            Link = new(0.14f, 0.92f, 0.92f, 1.00f),
            LinkHover = new(0.28f, 0.98f, 0.98f, 1.00f),
        },

        ["Ocean Deep"] = new ThemePalette
        {
            PanelBg = new(0.04f, 0.07f, 0.11f, 0.98f),
            PanelBorder = new(0.16f, 0.58f, 0.86f, 1.00f),
            HeaderBg = new(0.07f, 0.11f, 0.16f, 1.00f),
            Accent = new(0.18f, 0.70f, 0.98f, 1.00f),
            Btn = new(0.10f, 0.14f, 0.20f, 1.00f),
            BtnHovered = new(0.16f, 0.58f, 0.86f, 1.00f),
            BtnActive = new(0.14f, 0.48f, 0.72f, 1.00f),

            TextPrimary = new(0.86f, 0.92f, 0.98f, 1.00f),
            TextSecondary = new(0.72f, 0.82f, 0.90f, 1.00f),
            TextDisabled = new(0.54f, 0.66f, 0.78f, 1.00f),
            Link = new(0.22f, 0.70f, 1.00f, 1.00f),
            LinkHover = new(0.38f, 0.80f, 1.00f, 1.00f),
        },

        ["Forest Night"] = new ThemePalette
        {
            PanelBg = new(0.05f, 0.07f, 0.06f, 0.98f),
            PanelBorder = new(0.28f, 0.72f, 0.44f, 1.00f),
            HeaderBg = new(0.08f, 0.12f, 0.10f, 1.00f),
            Accent = new(0.20f, 0.84f, 0.52f, 1.00f),
            Btn = new(0.12f, 0.16f, 0.14f, 1.00f),
            BtnHovered = new(0.28f, 0.72f, 0.44f, 1.00f),
            BtnActive = new(0.22f, 0.60f, 0.38f, 1.00f),

            TextPrimary = new(0.88f, 0.94f, 0.90f, 1.00f),
            TextSecondary = new(0.74f, 0.86f, 0.80f, 1.00f),
            TextDisabled = new(0.54f, 0.70f, 0.64f, 1.00f),
            Link = new(0.26f, 0.88f, 0.60f, 1.00f),
            LinkHover = new(0.36f, 0.96f, 0.72f, 1.00f),
        },

        ["Rose Gold Dark"] = new ThemePalette
        {
            PanelBg = new(0.09f, 0.07f, 0.08f, 0.98f),
            PanelBorder = new(0.92f, 0.60f, 0.52f, 1.00f),
            HeaderBg = new(0.12f, 0.10f, 0.12f, 1.00f),
            Accent = new(1.00f, 0.72f, 0.62f, 1.00f),
            Btn = new(0.16f, 0.13f, 0.14f, 1.00f),
            BtnHovered = new(0.92f, 0.60f, 0.52f, 1.00f),
            BtnActive = new(0.80f, 0.50f, 0.44f, 1.00f),

            TextPrimary = new(0.96f, 0.90f, 0.92f, 1.00f),
            TextSecondary = new(0.82f, 0.76f, 0.78f, 1.00f),
            TextDisabled = new(0.60f, 0.56f, 0.58f, 1.00f),
            Link = new(1.00f, 0.72f, 0.62f, 1.00f),
            LinkHover = new(1.00f, 0.82f, 0.74f, 1.00f),
        },

        ["Grape Twilight"] = new ThemePalette
        {
            PanelBg = new(0.07f, 0.06f, 0.10f, 0.98f),
            PanelBorder = new(0.70f, 0.50f, 0.98f, 1.00f),
            HeaderBg = new(0.11f, 0.10f, 0.18f, 1.00f),
            Accent = new(0.82f, 0.64f, 1.00f, 1.00f),
            Btn = new(0.14f, 0.12f, 0.20f, 1.00f),
            BtnHovered = new(0.70f, 0.50f, 0.98f, 1.00f),
            BtnActive = new(0.58f, 0.40f, 0.90f, 1.00f),

            TextPrimary = new(0.92f, 0.90f, 0.98f, 1.00f),
            TextSecondary = new(0.78f, 0.76f, 0.90f, 1.00f),
            TextDisabled = new(0.58f, 0.56f, 0.74f, 1.00f),
            Link = new(0.84f, 0.66f, 1.00f, 1.00f),
            LinkHover = new(0.92f, 0.76f, 1.00f, 1.00f),
        },

        ["Cyberpunk"] = new ThemePalette
        {
            PanelBg = new(0.06f, 0.05f, 0.09f, 0.98f),
            PanelBorder = new(0.98f, 0.20f, 0.66f, 1.00f),
            HeaderBg = new(0.10f, 0.08f, 0.16f, 1.00f),
            Accent = new(0.18f, 0.90f, 0.95f, 1.00f),
            Btn = new(0.12f, 0.10f, 0.18f, 1.00f),
            BtnHovered = new(0.98f, 0.20f, 0.66f, 1.00f),
            BtnActive = new(0.16f, 0.74f, 0.78f, 1.00f),

            TextPrimary = new(0.92f, 0.90f, 0.98f, 1.00f),
            TextSecondary = new(0.76f, 0.74f, 0.88f, 1.00f),
            TextDisabled = new(0.58f, 0.56f, 0.74f, 1.00f),
            Link = new(0.98f, 0.24f, 0.70f, 1.00f),
            LinkHover = new(1.00f, 0.36f, 0.78f, 1.00f),
        },

        ["Chocolate Mint"] = new ThemePalette
        {
            PanelBg = new(0.08f, 0.06f, 0.05f, 0.98f),
            PanelBorder = new(0.10f, 0.80f, 0.65f, 1.00f),
            HeaderBg = new(0.12f, 0.09f, 0.08f, 1.00f),
            Accent = new(0.12f, 0.90f, 0.72f, 1.00f),
            Btn = new(0.15f, 0.12f, 0.11f, 1.00f),
            BtnHovered = new(0.10f, 0.80f, 0.65f, 1.00f),
            BtnActive = new(0.09f, 0.64f, 0.52f, 1.00f),

            TextPrimary = new(0.95f, 0.92f, 0.90f, 1.00f),
            TextSecondary = new(0.82f, 0.78f, 0.76f, 1.00f),
            TextDisabled = new(0.62f, 0.58f, 0.56f, 1.00f),
            Link = new(0.16f, 0.88f, 0.72f, 1.00f),
            LinkHover = new(0.28f, 0.96f, 0.82f, 1.00f),
        },

        ["Slate"] = new ThemePalette
        {
            PanelBg = new(0.09f, 0.10f, 0.12f, 0.98f),
            PanelBorder = new(0.50f, 0.56f, 0.64f, 1.00f),
            HeaderBg = new(0.12f, 0.14f, 0.17f, 1.00f),
            Accent = new(0.70f, 0.78f, 0.88f, 1.00f),
            Btn = new(0.16f, 0.18f, 0.21f, 1.00f),
            BtnHovered = new(0.50f, 0.56f, 0.64f, 1.00f),
            BtnActive = new(0.40f, 0.46f, 0.54f, 1.00f),

            TextPrimary = new(0.90f, 0.92f, 0.96f, 1.00f),
            TextSecondary = new(0.76f, 0.80f, 0.86f, 1.00f),
            TextDisabled = new(0.58f, 0.62f, 0.70f, 1.00f),
            Link = new(0.64f, 0.78f, 0.96f, 1.00f),
            LinkHover = new(0.74f, 0.86f, 1.00f, 1.00f),
        },

        // =========================
        // Vibrant / Mixed
        // =========================
        ["Sunset Pop"] = new ThemePalette
        {
            PanelBg = new(0.12f, 0.08f, 0.10f, 0.98f),
            PanelBorder = new(1.00f, 0.55f, 0.30f, 1.00f),
            HeaderBg = new(0.18f, 0.12f, 0.14f, 1.00f),
            Accent = new(1.00f, 0.40f, 0.55f, 1.00f),
            Btn = new(0.20f, 0.14f, 0.16f, 1.00f),
            BtnHovered = new(1.00f, 0.55f, 0.30f, 1.00f),
            BtnActive = new(0.88f, 0.42f, 0.38f, 1.00f),

            TextPrimary = new(0.96f, 0.90f, 0.92f, 1.00f),
            TextSecondary = new(0.84f, 0.74f, 0.78f, 1.00f),
            TextDisabled = new(0.64f, 0.56f, 0.60f, 1.00f),
            Link = new(1.00f, 0.48f, 0.40f, 1.00f),
            LinkHover = new(1.00f, 0.60f, 0.52f, 1.00f),
        },

        ["Neon Bubblegum"] = new ThemePalette
        {
            PanelBg = new(0.10f, 0.07f, 0.10f, 0.98f),
            PanelBorder = new(0.98f, 0.30f, 0.70f, 1.00f),
            HeaderBg = new(0.16f, 0.10f, 0.16f, 1.00f),
            Accent = new(0.30f, 0.85f, 1.00f, 1.00f),
            Btn = new(0.18f, 0.12f, 0.18f, 1.00f),
            BtnHovered = new(0.98f, 0.30f, 0.70f, 1.00f),
            BtnActive = new(0.24f, 0.70f, 0.86f, 1.00f),

            TextPrimary = new(0.94f, 0.92f, 0.98f, 1.00f),
            TextSecondary = new(0.82f, 0.78f, 0.90f, 1.00f),
            TextDisabled = new(0.62f, 0.58f, 0.74f, 1.00f),
            Link = new(1.00f, 0.40f, 0.78f, 1.00f),
            LinkHover = new(1.00f, 0.52f, 0.86f, 1.00f),
        },

        ["Tropical"] = new ThemePalette
        {
            PanelBg = new(0.95f, 0.99f, 0.97f, 0.98f),
            PanelBorder = new(0.99f, 0.69f, 0.16f, 1.00f),
            HeaderBg = new(0.90f, 0.97f, 0.93f, 1.00f),
            Accent = new(0.12f, 0.76f, 0.64f, 1.00f),
            Btn = new(0.92f, 0.97f, 0.95f, 1.00f),
            BtnHovered = new(0.99f, 0.69f, 0.16f, 1.00f),
            BtnActive = new(0.86f, 0.57f, 0.12f, 1.00f),

            TextPrimary = new(0.10f, 0.14f, 0.12f, 1.00f),
            TextSecondary = new(0.30f, 0.40f, 0.35f, 1.00f),
            TextDisabled = new(0.56f, 0.64f, 0.60f, 1.00f),
            Link = new(0.12f, 0.76f, 0.64f, 1.00f),
            LinkHover = new(0.20f, 0.86f, 0.74f, 1.00f),
        },

        ["Berry Sorbet"] = new ThemePalette
        {
            PanelBg = new(0.11f, 0.08f, 0.11f, 0.98f),
            PanelBorder = new(0.92f, 0.32f, 0.52f, 1.00f),
            HeaderBg = new(0.17f, 0.12f, 0.18f, 1.00f),
            Accent = new(0.66f, 0.32f, 0.92f, 1.00f),
            Btn = new(0.18f, 0.14f, 0.19f, 1.00f),
            BtnHovered = new(0.92f, 0.32f, 0.52f, 1.00f),
            BtnActive = new(0.54f, 0.26f, 0.78f, 1.00f),

            TextPrimary = new(0.94f, 0.92f, 0.98f, 1.00f),
            TextSecondary = new(0.82f, 0.78f, 0.90f, 1.00f),
            TextDisabled = new(0.62f, 0.58f, 0.74f, 1.00f),
            Link = new(0.92f, 0.36f, 0.58f, 1.00f),
            LinkHover = new(0.98f, 0.48f, 0.70f, 1.00f),
        },

        ["Aqua Coral"] = new ThemePalette
        {
            PanelBg = new(0.07f, 0.09f, 0.10f, 0.98f),
            PanelBorder = new(0.16f, 0.82f, 0.88f, 1.00f),
            HeaderBg = new(0.10f, 0.13f, 0.15f, 1.00f),
            Accent = new(1.00f, 0.43f, 0.42f, 1.00f),
            Btn = new(0.14f, 0.17f, 0.19f, 1.00f),
            BtnHovered = new(0.16f, 0.82f, 0.88f, 1.00f),
            BtnActive = new(0.84f, 0.36f, 0.34f, 1.00f),

            TextPrimary = new(0.92f, 0.96f, 0.98f, 1.00f),
            TextSecondary = new(0.78f, 0.86f, 0.90f, 1.00f),
            TextDisabled = new(0.60f, 0.68f, 0.72f, 1.00f),
            Link = new(0.18f, 0.82f, 0.90f, 1.00f),
            LinkHover = new(0.30f, 0.92f, 0.98f, 1.00f),
        },

        // =========================
        // Neutral Light & Classic
        // =========================
        ["Mono Light"] = new ThemePalette
        {
            PanelBg = new(0.98f, 0.98f, 0.98f, 0.98f),
            PanelBorder = new(0.50f, 0.50f, 0.52f, 1.00f),
            HeaderBg = new(0.93f, 0.93f, 0.94f, 1.00f),
            Accent = new(0.15f, 0.60f, 0.95f, 1.00f),
            Btn = new(0.95f, 0.95f, 0.96f, 1.00f),
            BtnHovered = new(0.50f, 0.50f, 0.52f, 1.00f),
            BtnActive = new(0.38f, 0.38f, 0.42f, 1.00f),

            TextPrimary = new(0.10f, 0.10f, 0.12f, 1.00f),
            TextSecondary = new(0.32f, 0.32f, 0.36f, 1.00f),
            TextDisabled = new(0.58f, 0.58f, 0.62f, 1.00f),
            Link = new(0.15f, 0.60f, 0.95f, 1.00f),
            LinkHover = new(0.26f, 0.70f, 1.00f, 1.00f),
        },

        ["Solarized Light-ish"] = new ThemePalette
        {
            PanelBg = new(0.99f, 0.96f, 0.89f, 0.98f),
            PanelBorder = new(0.42f, 0.53f, 0.55f, 1.00f),
            HeaderBg = new(0.94f, 0.88f, 0.76f, 1.00f),
            Accent = new(0.15f, 0.55f, 0.82f, 1.00f),
            Btn = new(0.97f, 0.93f, 0.84f, 1.00f),
            BtnHovered = new(0.42f, 0.53f, 0.55f, 1.00f),
            BtnActive = new(0.32f, 0.44f, 0.46f, 1.00f),

            TextPrimary = new(0.12f, 0.12f, 0.08f, 1.00f),
            TextSecondary = new(0.36f, 0.38f, 0.30f, 1.00f),
            TextDisabled = new(0.60f, 0.62f, 0.52f, 1.00f),
            Link = new(0.15f, 0.55f, 0.82f, 1.00f),
            LinkHover = new(0.26f, 0.66f, 0.92f, 1.00f),
        },

        ["Solarized Dark-ish"] = new ThemePalette
        {
            PanelBg = new(0.00f, 0.17f, 0.21f, 0.98f),
            PanelBorder = new(0.50f, 0.58f, 0.59f, 1.00f),
            HeaderBg = new(0.02f, 0.21f, 0.25f, 1.00f),
            Accent = new(0.65f, 0.77f, 0.55f, 1.00f),
            Btn = new(0.06f, 0.24f, 0.28f, 1.00f),
            BtnHovered = new(0.50f, 0.58f, 0.59f, 1.00f),
            BtnActive = new(0.42f, 0.50f, 0.51f, 1.00f),

            TextPrimary = new(0.86f, 0.92f, 0.88f, 1.00f),
            TextSecondary = new(0.72f, 0.82f, 0.78f, 1.00f),
            TextDisabled = new(0.54f, 0.66f, 0.62f, 1.00f),
            Link = new(0.65f, 0.77f, 0.55f, 1.00f),
            LinkHover = new(0.76f, 0.86f, 0.66f, 1.00f),
        },
    };
}