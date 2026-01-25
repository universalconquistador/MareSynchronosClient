using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace MareSynchronos.UI.ModernUi;

/// <summary>
/// Bootstrap inspired palette + tokens for consistent styling.
/// kind of like CSS variables
/// </summary>
public sealed class UiTheme(IFontHandle? body = null, IFontHandle? heading = null, IFontHandle? small = null)
{
    public IFontHandle? FontBody { get; set; } = body;
    public IFontHandle? FontHeading { get; set; } = heading;
    public IFontHandle? FontSmall { get; set; } = small;

    public Vector4 WindowBg { get; init; } = new(0.08f, 0.08f, 0.09f, 0.95f);
    public Vector4 PanelBg { get; init; } = new(0.12f, 0.12f, 0.13f, 0.80f);
    public Vector4 CardBg { get; init; } = new(0.14f, 0.14f, 0.16f, 0.85f);

    public Vector4 Border { get; init; } = new(0.25f, 0.25f, 0.27f, 0.90f);
    public Vector4 Separator { get; init; } = new(0.30f, 0.30f, 0.33f, 0.60f);

    public Vector4 Text { get; init; } = ImGuiColors.DalamudWhite;
    public Vector4 TextMuted { get; init; } = new(0.70f, 0.70f, 0.72f, 0.80f);

    public Vector4 Primary { get; init; } = ImGuiColors.TankBlue;
    public Vector4 Success { get; init; } = ImGuiColors.HealerGreen;
    public Vector4 Warning { get; init; } = ImGuiColors.DalamudYellow;
    public Vector4 Danger { get; init; } = ImGuiColors.DalamudRed;
    public Vector4 Info { get; init; } = new(0.35f, 0.75f, 0.95f, 1.0f);

    public Vector4 HoverOverlay { get; init; } = new(1f, 1f, 1f, 0.06f);
    public Vector4 ActiveOverlay { get; init; } = new(1f, 1f, 1f, 0.10f);

    // Profile theme
    public Vector4 ProfileTextColor { get; set; } = new(0.9569f, 0.9804f, 1.0000f, 1.0f);
    public Vector4 ProfilePrimaryColor { get; set; } = new(0.2588f, 0.6000f, 0.7373f, 1.0f);
    public Vector4 ProfileSecondaryColor { get; set; } = new(0.0745f, 0.2353f, 0.3373f, 1.0f);
    public Vector4 ProfileAccentColor { get; set; } = new(0.6510f, 0.8549f, 0.9216f, 1.0f);

    // sizing
    public float RadiusSm { get; init; } = 6f;
    public float RadiusMd { get; init; } = 10f;
    public float RadiusLg { get; init; } = 14f;

    public float CardPadding { get; init; } = 10f;
    public float PanelPadding { get; init; } = 10f;
    public float Gutter { get; init; } = 8f;

    public float PanelPad { get; init; } = 20f;
    public float PanelGap { get; init; } = 12f;

    public static UiTheme Default { get; } = new();

    /// <summary>
    /// Call only once per window draw, cleans up if used properly
    /// </summary>
    /// <returns></returns>
    public IDisposable PushWindowStyle()
    {
        // window and frame rounding must go in the PreDraw
        var d3 = ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, UiScale.S(RadiusSm));
        var d4 = ImRaii.PushStyle(ImGuiStyleVar.ScrollbarRounding, UiScale.S(RadiusSm));
        var d5 = ImRaii.PushStyle(ImGuiStyleVar.GrabRounding, UiScale.S(RadiusSm));

        // spacing
        var d6 = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(UiScale.S(8), UiScale.S(4.5f)));
        var d7 = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(UiScale.S(10), UiScale.S(6)));

        // colors
        var c1 = ImRaii.PushColor(ImGuiCol.WindowBg, WindowBg);
        var c2 = ImRaii.PushColor(ImGuiCol.Border, Border);
        var c3 = ImRaii.PushColor(ImGuiCol.Text, Text);

        return new CompositeDisposable(d3, d4, d5, d6, d7, c1, c2, c3);
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _items;
        public CompositeDisposable(params IDisposable[] items) => _items = items;
        public void Dispose()
        {
            for (var i = _items.Length - 1; i >= 0; i--)
                _items[i].Dispose();
        }
    }

    public static Vector4 ToVec4(float[] v) => new Vector4(v[0], v[1], v[2], v[3]);

    public static float[] FromVec4(Vector4 c) => [c.X, c.Y, c.Z, c.W];
}
