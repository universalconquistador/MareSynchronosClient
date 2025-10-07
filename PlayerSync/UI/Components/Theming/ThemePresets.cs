using System.Collections.Generic;
using System.Numerics;

namespace MareSynchronos.UI.Components.Theming;

public static class ThemePresets
{
#pragma warning disable MA0002 // IEqualityComparer<string> or IComparer<string> is missing
    public static readonly Dictionary<string, ThemePalette> Presets = new()
    {
        // =========================
        // Core / Neutral
        // =========================
        ["Blue"] = new ThemePalette
        {
            // Deep blue UI with blue accents
            PanelBg = new(0.06f, 0.08f, 0.12f, 0.85f),
            PanelBorder = new(0.22f, 0.46f, 0.86f, 1.00f),
            HeaderBg = new(0.09f, 0.12f, 0.18f, 1.00f),
            Accent = new(0.27f, 0.60f, 1.00f, 1.00f),

            Btn = new(0.12f, 0.16f, 0.22f, 1.00f),
            BtnHovered = new(0.22f, 0.46f, 0.86f, 1.00f),
            BtnActive = new(0.18f, 0.38f, 0.75f, 1.00f),

            TextPrimary = new(0.85f, 0.90f, 1.00f, 1.00f),
            TextSecondary = new(0.70f, 0.76f, 0.88f, 1.00f),
            TextDisabled = new(0.50f, 0.56f, 0.66f, 1.00f),

            Link = new(0.35f, 0.70f, 1.00f, 1.00f),
            LinkHover = new(0.55f, 0.82f, 1.00f, 1.00f),

            BtnText = new(0.90f, 0.94f, 1.00f, 1.00f),
            BtnTextHovered = new(1.00f, 1.00f, 1.00f, 1.00f),
            BtnTextActive = new(0.95f, 0.98f, 1.00f, 1.00f),

            StatusConnected = new(0.22f, 0.80f, 0.50f, 1f),
            StatusConnecting = new(1.00f, 0.85f, 0.35f, 1f),
            StatusDisconnected = new(1.00f, 0.35f, 0.35f, 1f),
            StatusBroadcasting = new(0.20f, 0.88f, 0.55f, 1f),

            UidAliasText = new(0.35f, 0.70f, 1.00f, 1.00f),
            UsersOnlineText = new(0.70f, 0.76f, 0.88f, 1.00f),
            UsersOnlineNumber = new(0.22f, 0.46f, 0.86f, 1.00f),
        },

        ["Mint"] = new ThemePalette
        {
            PanelBg = new(0.06f, 0.09f, 0.10f, 0.80f),
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

            BtnText = new(0.86f, 0.96f, 0.92f, 1.00f),
            BtnTextHovered = new(0.10f, 0.16f, 0.16f, 1.00f),
            BtnTextActive = new(0.95f, 1.00f, 0.98f, 1.00f),

            StatusConnected = new(0.20f, 0.90f, 0.78f, 1f),
            StatusConnecting = new(0.90f, 0.85f, 0.30f, 1f),
            StatusDisconnected = new(0.95f, 0.40f, 0.40f, 1f),
            StatusBroadcasting = new(0.10f, 0.95f, 0.82f, 1f),

            UidAliasText = new(0.20f, 0.90f, 0.78f, 1.00f),
            UsersOnlineText = new(0.72f, 0.86f, 0.80f, 1.00f),
            UsersOnlineNumber = new(0.10f, 0.80f, 0.65f, 1.00f),
        },

        ["Purple"] = new ThemePalette
        {
            PanelBg = new(0.08f, 0.07f, 0.12f, 0.80f),
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

            BtnText = new(0.90f, 0.88f, 0.98f, 1.00f),
            BtnTextHovered = new(0.18f, 0.15f, 0.28f, 1.00f),
            BtnTextActive = new(0.98f, 0.96f, 1.00f, 1.00f),

            StatusConnected = new(0.50f, 0.85f, 0.60f, 1f),
            StatusConnecting = new(0.95f, 0.80f, 0.40f, 1f),
            StatusDisconnected = new(0.95f, 0.45f, 0.55f, 1f),
            StatusBroadcasting = new(0.60f, 0.90f, 0.70f, 1f),

            UidAliasText = new(0.80f, 0.62f, 1.00f, 1.00f),
            UsersOnlineText = new(0.76f, 0.72f, 0.88f, 1.00f),
            UsersOnlineNumber = new(0.60f, 0.40f, 0.95f, 1.00f),
        },

        ["Dark"] = new ThemePalette
        {
            PanelBg = new(0.06f, 0.06f, 0.06f, 0.80f),
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

            BtnText = new(0.88f, 0.88f, 0.90f, 1.00f),
            BtnTextHovered = new(0.14f, 0.14f, 0.14f, 1.00f),
            BtnTextActive = new(0.96f, 0.96f, 0.98f, 1.00f),

            StatusConnected = new(0.40f, 0.80f, 0.50f, 1f),
            StatusConnecting = new(0.95f, 0.85f, 0.40f, 1f),
            StatusDisconnected = new(0.95f, 0.50f, 0.50f, 1f),
            StatusBroadcasting = new(0.30f, 0.85f, 0.45f, 1f),

            UidAliasText = new(0.45f, 0.75f, 1.00f, 1.00f),
            UsersOnlineText = new(0.74f, 0.74f, 0.78f, 1.00f),
            UsersOnlineNumber = new(0.35f, 0.35f, 0.38f, 1.00f),
        },

        // =========================
        // Bright / Light Backgrounds
        // =========================
        ["Sakura Daylight"] = new ThemePalette
        {
            // Light cherry blossom pinks
            PanelBg = new(0.98f, 0.95f, 0.97f, 0.85f),
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

            BtnText = new(0.12f, 0.10f, 0.14f, 1.00f),
            BtnTextHovered = new(0.98f, 0.95f, 0.97f, 1.00f),
            BtnTextActive = new(1.00f, 0.98f, 0.99f, 1.00f),

            StatusConnected = new(0.15f, 0.70f, 0.30f, 1f),
            StatusConnecting = new(0.90f, 0.75f, 0.30f, 1f),
            StatusDisconnected = new(0.88f, 0.30f, 0.45f, 1f),
            StatusBroadcasting = new(0.12f, 0.75f, 0.32f, 1f),

            UidAliasText = new(0.85f, 0.32f, 0.62f, 1.00f),
            UsersOnlineText = new(0.35f, 0.28f, 0.38f, 1.00f),
            UsersOnlineNumber = new(0.85f, 0.52f, 0.72f, 1.00f),
        },

        ["Cotton Candy"] = new ThemePalette
        {
            // Pastel blue base, pink accent
            PanelBg = new(0.97f, 0.98f, 1.00f, 0.85f),
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

            BtnText = new(0.10f, 0.12f, 0.16f, 1.00f),
            BtnTextHovered = new(0.97f, 0.98f, 1.00f, 1.00f),
            BtnTextActive = new(1.00f, 1.00f, 1.00f, 1.00f),

            StatusConnected = new(0.18f, 0.70f, 0.30f, 1f),
            StatusConnecting = new(0.80f, 0.65f, 0.20f, 1f),
            StatusDisconnected = new(0.85f, 0.25f, 0.35f, 1f),
            StatusBroadcasting = new(0.14f, 0.75f, 0.28f, 1f),

            UidAliasText = new(0.28f, 0.58f, 0.98f, 1.00f),
            UsersOnlineText = new(0.32f, 0.36f, 0.46f, 1.00f),
            UsersOnlineNumber = new(0.98f, 0.60f, 0.78f, 1.00f),
        },

        ["Ocean Deep"] = new ThemePalette
        {
            // Dark navy with aqua/cerulean highlights
            PanelBg = new(0.04f, 0.07f, 0.11f, 0.85f),
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

            BtnText = new(0.86f, 0.92f, 0.98f, 1.00f),
            BtnTextHovered = new(0.04f, 0.07f, 0.11f, 1.00f),
            BtnTextActive = new(0.90f, 0.98f, 1.00f, 1.00f),

            StatusConnected = new(0.22f, 0.80f, 0.50f, 1f),
            StatusConnecting = new(0.95f, 0.85f, 0.32f, 1f),
            StatusDisconnected = new(0.95f, 0.40f, 0.40f, 1f),
            StatusBroadcasting = new(0.18f, 0.85f, 0.45f, 1f),

            UidAliasText = new(0.22f, 0.70f, 1.00f, 1.00f),
            UsersOnlineText = new(0.72f, 0.82f, 0.90f, 1.00f),
            UsersOnlineNumber = new(0.16f, 0.58f, 0.86f, 1.00f),
        },

        ["Sunset"] = new ThemePalette
        {
            // Warm oranges/ambers with dark umber background
            PanelBg = new(0.12f, 0.08f, 0.06f, 0.85f),
            PanelBorder = new(0.95f, 0.55f, 0.30f, 1.00f),
            HeaderBg = new(0.25f, 0.15f, 0.10f, 1.00f),
            Accent = new(1.00f, 0.65f, 0.35f, 1.00f),

            Btn = new(0.30f, 0.20f, 0.15f, 1.00f),
            BtnHovered = new(0.95f, 0.55f, 0.30f, 1.00f),
            BtnActive = new(0.85f, 0.45f, 0.22f, 1.00f),

            TextPrimary = new(0.98f, 0.92f, 0.86f, 1.00f),
            TextSecondary = new(0.90f, 0.80f, 0.70f, 1.00f),
            TextDisabled = new(0.70f, 0.60f, 0.50f, 1.00f),

            Link = new(1.00f, 0.70f, 0.40f, 1.00f),
            LinkHover = new(1.00f, 0.80f, 0.60f, 1.00f),

            BtnText = new(0.98f, 0.92f, 0.86f, 1.00f),
            BtnTextHovered = new(0.30f, 0.20f, 0.15f, 1.00f),
            BtnTextActive = new(1.00f, 0.98f, 0.95f, 1.00f),

            StatusConnected = new(0.40f, 0.85f, 0.30f, 1f),
            StatusConnecting = new(1.00f, 0.78f, 0.30f, 1f),
            StatusDisconnected = new(1.00f, 0.40f, 0.20f, 1f),
            StatusBroadcasting = new(0.35f, 0.90f, 0.25f, 1f),

            UidAliasText = new(1.00f, 0.70f, 0.40f, 1.00f),
            UsersOnlineText = new(0.90f, 0.80f, 0.70f, 1.00f),
            UsersOnlineNumber = new(0.95f, 0.55f, 0.30f, 1.00f),
        },

        // =========================
        // Pride & Glory Collection
        // =========================
        ["Pride Rainbow"] = new ThemePalette
        {
            // Warm light base; rainbow for interactive elements
            PanelBg = new(0.98f, 0.95f, 0.90f, 0.85f),
            PanelBorder = new(1.00f, 0.00f, 0.00f, 1.00f), // Red
            HeaderBg = new(0.95f, 0.92f, 0.85f, 1.00f),
            Accent = new(0.00f, 0.80f, 0.00f, 1.00f), // Green

            Btn = new(0.96f, 0.93f, 0.88f, 1.00f),
            BtnHovered = new(1.00f, 0.65f, 0.00f, 1.00f), // Orange
            BtnActive = new(0.00f, 0.50f, 1.00f, 1.00f), // Blue

            TextPrimary = new(0.15f, 0.08f, 0.05f, 1.00f),
            TextSecondary = new(0.35f, 0.25f, 0.20f, 1.00f),
            TextDisabled = new(0.60f, 0.50f, 0.45f, 1.00f),

            Link = new(0.60f, 0.00f, 1.00f, 1.00f),       // Purple
            LinkHover = new(1.00f, 1.00f, 0.00f, 1.00f),  // Yellow

            BtnText = new(0.15f, 0.08f, 0.05f, 1.00f),
            BtnTextHovered = new(1.00f, 1.00f, 1.00f, 1.00f),
            BtnTextActive = new(1.00f, 1.00f, 1.00f, 1.00f),

            StatusConnected = new(0.00f, 0.80f, 0.00f, 1f),
            StatusConnecting = new(1.00f, 1.00f, 0.00f, 1f),
            StatusDisconnected = new(1.00f, 0.00f, 0.00f, 1f),
            StatusBroadcasting = new(0.00f, 0.90f, 0.00f, 1f),

            UidAliasText = new(0.60f, 0.00f, 1.00f, 1.00f),
            UsersOnlineText = new(0.35f, 0.25f, 0.20f, 1.00f),
            UsersOnlineNumber = new(1.00f, 0.65f, 0.00f, 1.00f), // Orange
        },

        ["Trans Pride"] = new ThemePalette
        {
            // Light blue, pink, white
            PanelBg = new(0.96f, 0.98f, 1.00f, 0.85f),
            PanelBorder = new(0.36f, 0.81f, 0.98f, 1.00f), // Light blue
            HeaderBg = new(0.93f, 0.95f, 0.98f, 1.00f),
            Accent = new(0.96f, 0.63f, 0.76f, 1.00f), // Pink

            Btn = new(0.94f, 0.96f, 0.99f, 1.00f),
            BtnHovered = new(0.96f, 0.63f, 0.76f, 1.00f),
            BtnActive = new(0.36f, 0.81f, 0.98f, 1.00f),

            TextPrimary = new(0.10f, 0.15f, 0.20f, 1.00f),
            TextSecondary = new(0.30f, 0.35f, 0.45f, 1.00f),
            TextDisabled = new(0.55f, 0.60f, 0.70f, 1.00f),

            Link = new(0.36f, 0.81f, 0.98f, 1.00f),
            LinkHover = new(0.96f, 0.63f, 0.76f, 1.00f),

            BtnText = new(0.10f, 0.15f, 0.20f, 1.00f),
            BtnTextHovered = new(0.96f, 0.98f, 1.00f, 1.00f),
            BtnTextActive = new(1.00f, 1.00f, 1.00f, 1.00f),

            StatusConnected = new(0.22f, 0.72f, 0.45f, 1f),
            StatusConnecting = new(0.92f, 0.82f, 0.35f, 1f),
            StatusDisconnected = new(0.90f, 0.32f, 0.42f, 1f),
            StatusBroadcasting = new(0.18f, 0.78f, 0.40f, 1f),

            UidAliasText = new(0.36f, 0.81f, 0.98f, 1.00f),
            UsersOnlineText = new(0.30f, 0.35f, 0.45f, 1.00f),
            UsersOnlineNumber = new(0.96f, 0.63f, 0.76f, 1.00f),
        },

        ["Cyberpunk"] = new ThemePalette
        {
            // Neon magenta & cyan on deep violet
            PanelBg = new(0.06f, 0.05f, 0.09f, 0.85f),
            PanelBorder = new(0.98f, 0.20f, 0.66f, 1.00f),   // Neon magenta
            HeaderBg = new(0.10f, 0.08f, 0.16f, 1.00f),
            Accent = new(0.18f, 0.90f, 0.95f, 1.00f),        // Neon cyan

            Btn = new(0.12f, 0.10f, 0.18f, 1.00f),
            BtnHovered = new(0.98f, 0.20f, 0.66f, 1.00f),
            BtnActive = new(0.16f, 0.74f, 0.78f, 1.00f),

            TextPrimary = new(0.92f, 0.90f, 0.98f, 1.00f),
            TextSecondary = new(0.76f, 0.74f, 0.88f, 1.00f),
            TextDisabled = new(0.58f, 0.56f, 0.74f, 1.00f),

            Link = new(0.98f, 0.24f, 0.70f, 1.00f),
            LinkHover = new(1.00f, 0.36f, 0.78f, 1.00f),

            BtnText = new(0.92f, 0.90f, 0.98f, 1.00f),
            BtnTextHovered = new(0.06f, 0.05f, 0.09f, 1.00f),
            BtnTextActive = new(0.95f, 0.92f, 1.00f, 1.00f),

            StatusConnected = new(0.18f, 0.90f, 0.50f, 1f),
            StatusConnecting = new(0.95f, 0.80f, 0.30f, 1f),
            StatusDisconnected = new(0.98f, 0.20f, 0.45f, 1f),
            StatusBroadcasting = new(0.16f, 0.95f, 0.55f, 1f),

            UidAliasText = new(0.18f, 0.90f, 0.95f, 1.00f),
            UsersOnlineText = new(0.76f, 0.74f, 0.88f, 1.00f),
            UsersOnlineNumber = new(0.98f, 0.24f, 0.70f, 1.00f),
        },
    };
#pragma warning restore MA0002 // IEqualityComparer<string> or IComparer<string> is missing
}