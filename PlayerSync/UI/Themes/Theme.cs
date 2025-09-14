using System.Numerics;

namespace MareSynchronos.UI.Themes;

[Serializable]
public class Theme
{
    public string Name { get; set; } = "Custom Theme";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";

    // Primary Colors
    public Vector4 Background { get; set; } = new(0.12f, 0.12f, 0.13f, 1.0f);
    public Vector4 BackgroundSecondary { get; set; } = new(0.18f, 0.18f, 0.20f, 1.0f);
    public Vector4 Surface { get; set; } = new(0.24f, 0.24f, 0.26f, 1.0f);
    public Vector4 Primary { get; set; } = new(0.40f, 0.60f, 0.95f, 1.0f);
    public Vector4 Secondary { get; set; } = new(0.70f, 0.70f, 0.75f, 1.0f);
    public Vector4 Accent { get; set; } = new(0.95f, 0.60f, 0.40f, 1.0f);

    // Text Colors
    public Vector4 Text { get; set; } = new(0.95f, 0.95f, 0.95f, 1.0f);
    public Vector4 TextSecondary { get; set; } = new(0.80f, 0.80f, 0.85f, 1.0f);
    public Vector4 TextDisabled { get; set; } = new(0.50f, 0.50f, 0.55f, 1.0f);

    // Status Colors
    public Vector4 Success { get; set; } = new(0.30f, 0.85f, 0.40f, 1.0f);
    public Vector4 Warning { get; set; } = new(1.0f, 0.75f, 0.30f, 1.0f);
    public Vector4 Error { get; set; } = new(0.95f, 0.35f, 0.35f, 1.0f);
    public Vector4 Info { get; set; } = new(0.40f, 0.80f, 1.0f, 1.0f);

    // Interactive States
    public Vector4 Border { get; set; } = new(0.30f, 0.30f, 0.32f, 1.0f);
    public Vector4 Hover { get; set; } = new(0.35f, 0.35f, 0.38f, 1.0f);
    public Vector4 Active { get; set; } = new(0.45f, 0.45f, 0.48f, 1.0f);
    public Vector4 Focus { get; set; } = new(0.40f, 0.60f, 0.95f, 0.8f);

    // Navigation specific colors
    public Vector4 NavBackground { get; set; } = new(0.10f, 0.10f, 0.11f, 1.0f);
    public Vector4 NavItemHover { get; set; } = new(0.30f, 0.30f, 0.32f, 1.0f);
    public Vector4 NavItemActive { get; set; } = new(0.40f, 0.60f, 0.95f, 0.3f);
    public Vector4 NavSeparator { get; set; } = new(0.25f, 0.25f, 0.27f, 1.0f);
    
    // Title bar specific colors
    public Vector4 TitleBarBackground { get; set; } = new(0.12f, 0.12f, 0.13f, 1.0f);
    public bool TransparentTitleBar { get; set; } = false;

    // Styling Properties
    public float WindowRounding { get; set; } = 8.0f;
    public float ChildRounding { get; set; } = 4.0f;
    public float FrameRounding { get; set; } = 4.0f;
    public float PopupRounding { get; set; } = 6.0f;
    public float ScrollbarRounding { get; set; } = 9.0f;
    public float TabRounding { get; set; } = 4.0f;

    public Vector2 WindowPadding { get; set; } = new(12.0f, 12.0f);
    public Vector2 FramePadding { get; set; } = new(8.0f, 4.0f);
    public Vector2 CellPadding { get; set; } = new(4.0f, 2.0f);
    public Vector2 ItemSpacing { get; set; } = new(8.0f, 4.0f);
    public Vector2 ItemInnerSpacing { get; set; } = new(4.0f, 4.0f);
    public Vector2 TouchExtraPadding { get; set; } = new(0.0f, 0.0f);

    public float IndentSpacing { get; set; } = 21.0f;
    public float ScrollbarSize { get; set; } = 14.0f;
    public float WindowBorderSize { get; set; } = 1.0f;
    public float ChildBorderSize { get; set; } = 1.0f;
    public float PopupBorderSize { get; set; } = 1.0f;
    public float FrameBorderSize { get; set; } = 0.0f;
    public float TabBorderSize { get; set; } = 0.0f;

    public Theme Clone()
    {
        return new Theme
        {
            Name = Name + " (Copy)",
            Description = Description,
            Author = Author,
            Version = Version,
            Background = Background,
            BackgroundSecondary = BackgroundSecondary,
            Surface = Surface,
            Primary = Primary,
            Secondary = Secondary,
            Accent = Accent,
            Text = Text,
            TextSecondary = TextSecondary,
            TextDisabled = TextDisabled,
            Success = Success,
            Warning = Warning,
            Error = Error,
            Info = Info,
            Border = Border,
            Hover = Hover,
            Active = Active,
            Focus = Focus,
            NavBackground = NavBackground,
            NavItemHover = NavItemHover,
            NavItemActive = NavItemActive,
            NavSeparator = NavSeparator,
            TitleBarBackground = TitleBarBackground,
            TransparentTitleBar = TransparentTitleBar,
            WindowRounding = WindowRounding,
            ChildRounding = ChildRounding,
            FrameRounding = FrameRounding,
            PopupRounding = PopupRounding,
            ScrollbarRounding = ScrollbarRounding,
            TabRounding = TabRounding,
            WindowPadding = WindowPadding,
            FramePadding = FramePadding,
            CellPadding = CellPadding,
            ItemSpacing = ItemSpacing,
            ItemInnerSpacing = ItemInnerSpacing,
            TouchExtraPadding = TouchExtraPadding,
            IndentSpacing = IndentSpacing,
            ScrollbarSize = ScrollbarSize,
            WindowBorderSize = WindowBorderSize,
            ChildBorderSize = ChildBorderSize,
            PopupBorderSize = PopupBorderSize,
            FrameBorderSize = FrameBorderSize,
            TabBorderSize = TabBorderSize,
        };
    }
}