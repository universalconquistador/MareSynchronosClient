using System.Collections.Generic;
using System.Numerics;

namespace MareSynchronos.UI.Components.Theming;

public static class ThemePresets
{
#pragma warning disable MA0002 // IEqualityComparer<string> or IComparer<string> is missing
    public static readonly Dictionary<string, ThemePalette> Presets = new()
    {
        // =========================
        // OG Mare
        // =========================
        ["Classic"] = new ThemePalette
        {
            PanelBg = new(0.060f, 0.060f, 0.060f, 0.930f),
            PanelBorder = new(0.430f, 0.430f, 0.500f, 0.500f),
            HeaderBg = new(0.389f, 0.109f, 0.109f, 0.828f),
            Accent = new(1.000f, 1.000f, 1.000f, 1.000f),

            Btn = new(0.710f, 0.710f, 0.710f, 0.400f),
            BtnHovered = new(0.540f, 0.540f, 0.540f, 0.400f),
            BtnActive = new(0.484f, 0.101f, 0.101f, 0.945f),

            TextPrimary = new(1.000f, 1.000f, 1.000f, 1.000f),
            TextSecondary = new(0.70f, 0.76f, 0.88f, 1.00f),
            TextDisabled = new(0.500f, 0.500f, 0.500f, 1.00f),

            Link = new(0.35f, 0.70f, 1.00f, 1.00f),
            LinkHover = new(0.55f, 0.82f, 1.00f, 1.00f),

            BtnText = new(1.000f, 1.000f, 1.000f, 1.000f),
            BtnTextHovered = new(0.700f, 0.700f, 0.700f, 1.000f),
            BtnTextActive = new(0.95f, 0.98f, 1.00f, 1.00f),

            StatusConnected = new(0.117f, 1.000f, 0.000f, 1.000f),
            StatusConnecting = new(0.117f, 1.000f, 0.000f, 1.000f),
            StatusDisconnected = new(1.0000f, 1.000f, 0.4000f, 1.000f),
            StatusBroadcasting = new(0.20f, 0.88f, 0.55f, 1f),

            UidAliasText = new(0.117f, 1.000f, 0.000f, 1.000f),
            UsersOnlineText = new(1.000f, 1.000f, 1.000f, 1.000f),
            UsersOnlineNumber = new(0.117f, 1.000f, 0.000f, 1.000f),

            StatusError = new(1.000f, 0.000f, 0.000f, 1.000f),
            StatusWarn = new(1.0000f, 1.000f, 0.4000f, 1.000f),

        },
        // =========================
        // Core / Neutral
        // =========================
        ["Blue"] = new ThemePalette
        {
            // Deep blue UI with blue accents
            PanelBg = new(0.06f, 0.08f, 0.12f, 0.930f),
            PanelBorder = new(0.22f, 0.46f, 0.86f, 1.00f),
            HeaderBg = new(0.09f, 0.12f, 0.18f, 1.00f),
            Accent = new(0.27f, 0.60f, 1.00f, 1.00f),

            Btn = new(0.12f, 0.16f, 0.22f, 1.00f),
            BtnHovered = new(0.22f, 0.46f, 0.86f, 0.40f),
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
            PanelBg = new(0.06f, 0.09f, 0.10f, 0.930f),
            PanelBorder = new(0.10f, 0.80f, 0.65f, 1.00f),
            HeaderBg = new(0.06f, 0.20f, 0.18f, 1.00f),
            Accent = new(0.10f, 0.90f, 0.75f, 1.00f),
            Btn = new(0.10f, 0.16f, 0.16f, 1.00f),
            BtnHovered = new(0.10f, 0.80f, 0.65f, 0.40f),
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
            PanelBg = new(0.08f, 0.07f, 0.12f, 0.930f),
            PanelBorder = new(0.60f, 0.40f, 0.95f, 1.00f),
            HeaderBg = new(0.18f, 0.12f, 0.28f, 1.00f),
            Accent = new(0.75f, 0.55f, 1.00f, 1.00f),
            Btn = new(0.18f, 0.15f, 0.28f, 1.00f),
            BtnHovered = new(0.60f, 0.40f, 0.95f, 0.40f),
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
            PanelBg = new(0.06f, 0.06f, 0.06f, 0.930f),
            PanelBorder = new(0.35f, 0.35f, 0.38f, 1.00f),
            HeaderBg = new(0.10f, 0.10f, 0.10f, 1.00f),
            Accent = new(0.80f, 0.80f, 0.80f, 1.00f),
            Btn = new(0.14f, 0.14f, 0.14f, 1.00f),
            BtnHovered = new(0.35f, 0.35f, 0.38f, 0.40f),
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
            BtnHovered = new(0.93f, 0.40f, 0.66f, 0.40f),
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
            BtnHovered = new(0.52f, 0.70f, 0.98f, 0.40f),
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
            UsersOnlineNumber = new(0.98f, 0.60f, 0.78f, 1.00f)
        },

        ["Ocean Deep"] = new ThemePalette
        {
            // Dark navy with aqua/cerulean highlights
            PanelBg = new(0.04f, 0.07f, 0.11f, 0.930f),
            PanelBorder = new(0.16f, 0.58f, 0.86f, 1.00f),
            HeaderBg = new(0.07f, 0.11f, 0.16f, 1.00f),
            Accent = new(0.18f, 0.70f, 0.98f, 1.00f),

            Btn = new(0.10f, 0.14f, 0.20f, 1.00f),
            BtnHovered = new(0.16f, 0.58f, 0.86f, 0.40f),
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
            PanelBg = new(0.12f, 0.08f, 0.06f, 0.930f),
            PanelBorder = new(0.95f, 0.55f, 0.30f, 1.00f),
            HeaderBg = new(0.25f, 0.15f, 0.10f, 1.00f),
            Accent = new(1.00f, 0.65f, 0.35f, 1.00f),

            Btn = new(0.30f, 0.20f, 0.15f, 1.00f),
            BtnHovered = new(0.95f, 0.55f, 0.30f, 0.40f),
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
            BtnHovered = new(1.00f, 0.65f, 0.00f, 0.40f), // Orange
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
            BtnHovered = new(0.96f, 0.63f, 0.76f, 0.40f),
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
            PanelBg = new(0.06f, 0.05f, 0.09f, 0.930f),
            PanelBorder = new(0.98f, 0.20f, 0.66f, 1.00f),   // Neon magenta
            HeaderBg = new(0.10f, 0.08f, 0.16f, 1.00f),
            Accent = new(0.18f, 0.90f, 0.95f, 1.00f),        // Neon cyan

            Btn = new(0.12f, 0.10f, 0.18f, 1.00f),
            BtnHovered = new(0.98f, 0.20f, 0.66f, 0.40f),
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

    public static readonly string DalamudDefaultTheme = "DS1H4sIAAAAAAAACq1XUXPbKBD+Kzc8ezIgECC9Ncld89B2Mk1ucvEbsYmsi2K5suI07fi/3yBYQMjOeXrnFxa838ey7C6rn0ihkpzhGXpA5U/0FyqlmdwP436GFqhkZmGJSmxG7bSGyb0Z8/0MPaKSzFDllleOsXZz9eCEvx14YLxHJR22eHILjdN6Tgxhg9Y6wdrVNlnNhtXNxEiz+g2V2WBXh0rCjbBFZWHGHpWEGuFlsGmGdo7x1Y3fHdebJ86j0/84uJ1SbpmOTrtoG7N+q7/37v/Bpns3zt14N4z72aB4WW/VQ6OX090HwDB6wF29Xrav51UwioNV3Kkb4c4IBd3P0MWqbpaxPqiDtlM25Nft5mUT60pQlqAtgZvtZ+i87Za68+qMOnUjRJZbP1rlm5Vatq8nWfNHp551ZE1WOGUjzEGw/CzoX7U73cXeZOBOBlYxB4tQHxZ9vQvhzwHEAcQBxIW5t7pvYttwlvGMMQoesPMs3EmYDyQyx1xIwQJVYgCVBWE4g1AguCAC50AXppYtkzhnNJBdtE2jNtvIC7/I91mvX85VF52UgGeMYMEsis+bRdc2zcMI8t4le/2PnSkkYC1xECPMQZhuYkDpfTPAMsCyo9jE6TlAjWBDJYZerPTi6bPqnjxAQvIZYQ5C2Kupl3p8sqPRmCLSgBQQkAICUkS485e+b6GI4jMBBzHCHAQf81Y7dRzlTOAckgwLySihAopQNHcFIMeFxIEuMZhJRngmwWyCsRB5AcaHaUp2pVVcU7w9RrD+gqSnxKtPcv5gBcVn0iNS94J3wbnxneiN6lTfnlrovP5/c7CwWchixrRI/OqNfdXb+of+2NXhLRVAYwQbMuDoLB9B0mMJeCWMYJEyrpUB+X+ZfxslVAZ3YQT7KlC4bW51J9EhqSDYXyLFVDAsIB/D1F6DNdIypTEuCk5FAecnuWQ0Y74u+umU6M/1Y7t4iQs05p4Gk6IoDKsrrxJjjOFQhchYwpFYRWju8NYvfKCDyGZZXG8u28VTva6uO72rdXiWM55mnaGzxwio3583/Vv8QMOOcBHRRtdN23+q13obUg5qlBFs0pFDgPHtQTsV5R7NE9hVve3bqlPQauKzXEhSQKARXshCGNR8PHVpN0QdS7mmUU+KAvv3NMsoJtJz+mnKeWtaPVuC4kfV2+Zbm7hJNBjXPvVdu67eeyfzw8BPdbXq30uaCe7ruM185w0P6h+af2t7TaC7vvdGN3rR67g5fSfuqClCl52qLrt2c6u6Sh/bKhhnku2L2l3V1aoZn//YPvb8X9TO9tn1ukrBxw8mEuRl/RwdDdIR0hrOlZkuq12qxuJOA9F8bz6a1LNGJXpsu6re6fVvj43qe929oRla2u8QNcmasYvscUMxhdcanum4xVicpHXaV4w+6ePoMfjBvzGDZH3hXhmrW01OKjDcbcy5mkQ0P7BzPdHy1ZgOv0gXPnvNjrmUprwe9fTT0ZCNi3ITGFNXFzx2YyhwsCk7QLcOZYZYvjRTrB58bYdDM2pdOHZO6Bm4+d/WEewtHDnn2+RacgylKuYMvZX0PTXznDyPrzo8IdI/cnA9A7vT3M/QznSSeP8P9O3gOgcRAAA=";
#pragma warning restore MA0002 // IEqualityComparer<string> or IComparer<string> is missing
}