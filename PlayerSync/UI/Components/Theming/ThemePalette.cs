using System.Numerics;

namespace MareSynchronos.UI.Components.Theming;

public class ThemePalette
{
    // Core panel colors
    public Vector4 PanelBg { get; set; }
    public Vector4 PanelBorder { get; set; }
    public Vector4 HeaderBg { get; set; }
    public Vector4 Accent { get; set; }
    public Vector4 Accent2 { get; set; }

    // Button colors
    public Vector4 Btn { get; set; }
    public Vector4 BtnHovered { get; set; }
    public Vector4 BtnActive { get; set; }
    public Vector4 BtnText { get; set; }
    public Vector4 BtnTextHovered { get; set; }
    public Vector4 BtnTextActive { get; set; }

    // Text colors
    public Vector4 TextPrimary { get; set; }
    public Vector4 TextSecondary { get; set; }
    public Vector4 TextMuted { get; set; }
    public Vector4 TextMuted2 { get; set; }
    public Vector4 TextDisabled { get; set; }

    // Link colors
    public Vector4 Link { get; set; }
    public Vector4 LinkHover { get; set; }

    // Tooltip colors
    public Vector4 TooltipBg { get; set; }
    public Vector4 TooltipText { get; set; }

    // Status colors
    public Vector4 StatusOk { get; set; }
    public Vector4 StatusWarn { get; set; }
    public Vector4 StatusError { get; set; }
    public Vector4 StatusPaused { get; set; }
    public Vector4 StatusInfo { get; set; }

    // Connection & service status colors
    public Vector4 StatusConnected { get; set; }
    public Vector4 StatusConnecting { get; set; }
    public Vector4 StatusDisconnected { get; set; }
    public Vector4 StatusBroadcasting { get; set; }

    // UI Text Colors
    public Vector4 UidAliasText { get; set; }
    public Vector4 UsersOnlineText { get; set; }
    public Vector4 UsersOnlineNumber { get; set; }

    // Surface layers for glassmorphism
    public float BackgroundOpacity { get; set; }
    public Vector4 Surface0 { get; set; }
    public Vector4 Surface1 { get; set; }
    public Vector4 Surface2 { get; set; }
    public Vector4 Surface3 { get; set; }

    // Computed properties
    public Vector4 Border => new(1f, 1f, 1f, 0.08f);
    public Vector4 Separator => new(1f, 1f, 1f, 0.07f);

    // Design tokens
    public float RadiusSmall { get; set; }
    public float RadiusMedium { get; set; }
    public float RadiusLarge { get; set; }
    public float SpacingXS { get; set; }
    public float SpacingS { get; set; }
    public float SpacingM { get; set; }
    public float SpacingL { get; set; }

    // Window rounding (matching sync_client values)
    public float WindowRounding { get; set; }
    public float ChildRounding { get; set; }
    public float FrameRounding { get; set; }
    public float PopupRounding { get; set; }
    public float ScrollbarRounding { get; set; }
    public float GrabRounding { get; set; }
    public float TabRounding { get; set; }

    public ThemePalette()
    {
        // Core panel colors
        PanelBg = new(0.11f, 0.11f, 0.11f, 0.80f);
        PanelBorder = new(0.43f, 0.43f, 0.50f, 1.00f);
        HeaderBg = new(0.16f, 0.16f, 0.21f, 1.00f);
        Accent = new(0.26f, 0.59f, 0.98f, 1.00f);
        Accent2 = new(0.608f, 0.694f, 1f, 1f);

        // Button colors
        Btn = new(0.16f, 0.16f, 0.21f, 1.00f);
        BtnHovered = new(0.26f, 0.59f, 0.98f, 1.00f);
        BtnActive = new(0.06f, 0.53f, 0.98f, 1.00f);
        BtnText = new(1.00f, 1.00f, 1.00f, 1.00f);
        BtnTextHovered = new(0.00f, 0.00f, 0.00f, 1.00f);
        BtnTextActive = new(1.00f, 1.00f, 1.00f, 1.00f);

        // Text colors
        TextPrimary = new(1.00f, 1.00f, 1.00f, 1.00f);
        TextSecondary = new(0.86f, 0.86f, 0.86f, 1.00f);
        TextMuted = new(0.604f, 0.639f, 0.678f, 1f);
        TextMuted2 = new(0.420f, 0.459f, 0.502f, 1f);
        TextDisabled = new(0.50f, 0.50f, 0.50f, 1.00f);

        // Link colors
        Link = new(0.26f, 0.59f, 0.98f, 1.00f);
        LinkHover = new(0.46f, 0.69f, 1.00f, 1.00f);

        // Tooltip colors
        TooltipBg = new(0.05f, 0.06f, 0.10f, 0.95f);
        TooltipText = new(0.90f, 0.95f, 1.00f, 1.00f);

        // Status colors
        StatusOk = new(0.212f, 0.773f, 0.416f, 1f);
        StatusWarn = new(1f, 0.690f, 0.125f, 1f);
        StatusError = new(1f, 0.322f, 0.322f, 1f);
        StatusPaused = new(1f, 0.420f, 0.208f, 1f);
        StatusInfo = new(0.239f, 0.694f, 1f, 1f);

        // Connection & service status colors (matching ImGuiColors)
        StatusConnected = new(0.212f, 0.773f, 0.416f, 1f);     // ParsedGreen/HealerGreen
        StatusConnecting = new(1f, 0.800f, 0.282f, 1f);        // DalamudYellow
        StatusDisconnected = new(1f, 0.322f, 0.322f, 1f);      // DalamudRed
        StatusBroadcasting = new(0.094f, 0.835f, 0.369f, 1f);  // HealerGreen (slightly different for broadcasting)

        // UI Text Colors
        UidAliasText = new(0.212f, 0.773f, 0.416f, 1f);        // ParsedGreen/HealerGreen
        UsersOnlineText = new(0.86f, 0.86f, 0.86f, 1.00f);     // TextSecondary
        UsersOnlineNumber = new(0.212f, 0.773f, 0.416f, 1f);   // ParsedGreen/HealerGreen (same as StatusConnected)

        // Glassmorphism
        BackgroundOpacity = 0.40f;
        UpdateSurfaceOpacity();

        // Design tokens
        RadiusSmall = 8f;
        RadiusMedium = 14f;
        RadiusLarge = 20f;
        SpacingXS = 6f;
        SpacingS = 10f;
        SpacingM = 14f;
        SpacingL = 18f;

        // Window rounding (sync_client values)
        WindowRounding = 12f;
        ChildRounding = 10f;
        FrameRounding = 6f;
        PopupRounding = 8f;
        ScrollbarRounding = 6f;
        GrabRounding = 6f;
        TabRounding = 6f;
    }

    public void UpdateSurfaceOpacity()
    {
        Surface0 = new(0.035f, 0.039f, 0.055f, BackgroundOpacity);  // rgba(9, 10, 14, opacity)
        Surface1 = new(0.071f, 0.078f, 0.110f, BackgroundOpacity);  // rgba(18, 20, 28, opacity)
        Surface2 = new(0.110f, 0.118f, 0.165f, BackgroundOpacity);  // rgba(28, 30, 42, opacity)
        Surface3 = new(0.141f, 0.149f, 0.220f, BackgroundOpacity);  // rgba(36, 38, 56, opacity)
    }

    // Copy constructor for theme variations
    public ThemePalette(ThemePalette source)
    {
        // Copy all properties from source
        PanelBg = source.PanelBg;
        PanelBorder = source.PanelBorder;
        HeaderBg = source.HeaderBg;
        Accent = source.Accent;
        Accent2 = source.Accent2;

        Btn = source.Btn;
        BtnHovered = source.BtnHovered;
        BtnActive = source.BtnActive;
        BtnText = source.BtnText;
        BtnTextHovered = source.BtnTextHovered;
        BtnTextActive = source.BtnTextActive;

        TextPrimary = source.TextPrimary;
        TextSecondary = source.TextSecondary;
        TextMuted = source.TextMuted;
        TextMuted2 = source.TextMuted2;
        TextDisabled = source.TextDisabled;

        Link = source.Link;
        LinkHover = source.LinkHover;

        TooltipBg = source.TooltipBg;
        TooltipText = source.TooltipText;

        StatusOk = source.StatusOk;
        StatusWarn = source.StatusWarn;
        StatusError = source.StatusError;
        StatusPaused = source.StatusPaused;
        StatusInfo = source.StatusInfo;

        StatusConnected = source.StatusConnected;
        StatusConnecting = source.StatusConnecting;
        StatusDisconnected = source.StatusDisconnected;
        StatusBroadcasting = source.StatusBroadcasting;

        UidAliasText = source.UidAliasText;
        UsersOnlineText = source.UsersOnlineText;
        UsersOnlineNumber = source.UsersOnlineNumber;

        BackgroundOpacity = source.BackgroundOpacity;
        Surface0 = source.Surface0;
        Surface1 = source.Surface1;
        Surface2 = source.Surface2;
        Surface3 = source.Surface3;

        RadiusSmall = source.RadiusSmall;
        RadiusMedium = source.RadiusMedium;
        RadiusLarge = source.RadiusLarge;
        SpacingXS = source.SpacingXS;
        SpacingS = source.SpacingS;
        SpacingM = source.SpacingM;
        SpacingL = source.SpacingL;

        WindowRounding = source.WindowRounding;
        ChildRounding = source.ChildRounding;
        FrameRounding = source.FrameRounding;
        PopupRounding = source.PopupRounding;
        ScrollbarRounding = source.ScrollbarRounding;
        GrabRounding = source.GrabRounding;
        TabRounding = source.TabRounding;
    }

    public static Vector4 GetDarkerColor(Vector4 color, bool isHovered) => isHovered
        ? new Vector4(color.X * 0.7f, color.Y * 0.7f, color.Z * 0.7f, color.W)
        : color;
}