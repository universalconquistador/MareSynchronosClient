using MareSynchronos.UI.Components.Theming;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MareSynchronos.UI.Components.Theming;

public static class ThemeImport
{
    public static ThemePalette ImportDs1ToThemePalette(string ds1)
    {
        if (string.IsNullOrWhiteSpace(ds1))
            throw new ArgumentException("Empty DS1 payload.", nameof(ds1));

        var span = ds1.AsSpan().Trim();
        if (span.StartsWith("DS1".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            int i = 3;
            while (i < span.Length && (char.IsWhiteSpace(span[i]) || span[i] == ':' || span[i] == ';' || span[i] == ',')) i++;
            span = span[i..];
        }

        // Base64 decode
        byte[] compressed;
        try
        {
            compressed = Convert.FromBase64String(span.ToString());
        }
        catch (FormatException fe)
        {
            throw new InvalidOperationException("DS1 payload is not valid Base64.", fe);
        }

        // GZip decompress -> JSON text
        string json;
        try
        {
            using var ms = new MemoryStream(compressed);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var sr = new StreamReader(gz, Encoding.UTF8);
            json = sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to GZip-decompress DS1 payload.", ex);
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement colors = root.TryGetProperty("Colors", out var cObj) ? cObj : root;
        JsonElement style = root.TryGetProperty("Style", out var sObj) ? sObj : root;

        var theme = new ThemePalette
        {
            PanelBg = new Vector4(0.06f, 0.06f, 0.07f, 0.94f),
            PanelBorder = new Vector4(1f, 1f, 1f, 0.08f),
            HeaderBg = new Vector4(0.14f, 0.14f, 0.14f, 1f),
            Accent = new Vector4(0.26f, 0.59f, 0.98f, 1f),
            Accent2 = new Vector4(0.98f, 0.26f, 0.40f, 1f),

            Btn = new Vector4(0.20f, 0.20f, 0.20f, 1f),
            BtnHovered = new Vector4(0.28f, 0.28f, 0.28f, 1f),
            BtnActive = new Vector4(0.35f, 0.35f, 0.35f, 1f),

            BtnText = new Vector4(1, 1, 1, 1),
            BtnTextHovered = new Vector4(1, 1, 1, 1),
            BtnTextActive = new Vector4(1, 1, 1, 1),

            TextPrimary = new Vector4(1, 1, 1, 1),
            TextSecondary = new Vector4(0.86f, 0.86f, 0.86f, 1f),
            TextMuted = new Vector4(0.72f, 0.72f, 0.72f, 1f),
            TextMuted2 = new Vector4(0.6f, 0.6f, 0.6f, 1f),
            TextDisabled = new Vector4(0.5f, 0.5f, 0.5f, 1f),

            Link = new Vector4(0.26f, 0.59f, 0.98f, 1f),
            LinkHover = new Vector4(0.26f, 0.59f, 0.98f, 1f),

            TooltipBg = new Vector4(0.10f, 0.10f, 0.10f, 0.94f),
            TooltipText = new Vector4(1, 1, 1, 1),

            StatusOk = new Vector4(0.35f, 0.80f, 0.35f, 1f),
            StatusWarn = new Vector4(0.95f, 0.80f, 0.2f, 1f),
            StatusError = new Vector4(0.90f, 0.25f, 0.25f, 1f),
            StatusPaused = new Vector4(0.70f, 0.70f, 0.70f, 1f),
            StatusInfo = new Vector4(0.30f, 0.60f, 1.00f, 1f),

            StatusConnected = new Vector4(0.2f, 0.8f, 0.2f, 1f),
            StatusConnecting = new Vector4(0.95f, 0.80f, 0.2f, 1f),
            StatusDisconnected = new Vector4(0.9f, 0.25f, 0.25f, 1f),
            StatusBroadcasting = new Vector4(1.0f, 0.5f, 0.0f, 1f),

            UidAliasText = new Vector4(1, 1, 1, 1),
            UsersOnlineText = new Vector4(0.9f, 0.9f, 0.9f, 1f),
            UsersOnlineNumber = new Vector4(1, 1, 1, 1),

            BackgroundOpacity = 0.94f,
            Surface0 = new Vector4(0, 0, 0, 0),
            Surface1 = new Vector4(0, 0, 0, 0),
            Surface2 = new Vector4(0, 0, 0, 0),
            Surface3 = new Vector4(0, 0, 0, 0),

            RadiusSmall = 3,
            RadiusMedium = 6,
            RadiusLarge = 12,
            SpacingXS = 2,
            SpacingS = 4,
            SpacingM = 8,
            SpacingL = 12,

            WindowRounding = GetFloat(style, "WindowRounding", 8),
            ChildRounding = GetFloat(style, "ChildRounding", 4),
            FrameRounding = GetFloat(style, "FrameRounding", 4),
            PopupRounding = GetFloat(style, "PopupRounding", 4),
            ScrollbarRounding = GetFloat(style, "ScrollbarRounding", 4),
            GrabRounding = GetFloat(style, "GrabRounding", 4),
            TabRounding = GetFloat(style, "TabRounding", 4),
        };

        // Map ImGui colors -> ThemePalette
        theme.PanelBg = GetCol(colors, "WindowBg", theme.PanelBg);
        theme.PanelBorder = GetCol(colors, "Border", theme.PanelBorder);
        theme.HeaderBg = GetCol(colors, "TitleBg", theme.HeaderBg);
        theme.TextPrimary = GetCol(colors, "Text", theme.TextPrimary);
        theme.TextDisabled = GetCol(colors, "TextDisabled", theme.TextDisabled);

        // Buttons & headers
        theme.Btn = GetCol(colors, "Button", theme.Btn);
        theme.BtnHovered = GetCol(colors, "ButtonHovered", theme.BtnHovered);
        theme.BtnActive = GetCol(colors, "ButtonActive", theme.BtnActive);
        theme.Accent = GetCol(colors, "CheckMark", theme.Accent);

        // Sliders / grabs
        var sliderGrab = GetCol(colors, "SliderGrab", theme.Accent);
        var sliderGrabAct = GetCol(colors, "SliderGrabActive", theme.BtnActive);
        if (!IsZero(sliderGrab)) theme.Accent = sliderGrab;
        if (!IsZero(sliderGrabAct)) theme.BtnActive = sliderGrabAct;

        // Tabs/headers often match buttons
        theme.Btn = GetCol(colors, "Tab", theme.Btn);
        theme.BtnHovered = GetCol(colors, "TabHovered", theme.BtnHovered);
        theme.BtnActive = GetCol(colors, "TabActive", theme.BtnActive);

        // Misc useful mappings
        var hdr = GetCol(colors, "Header", theme.HeaderBg);
        if (!IsZero(hdr)) theme.HeaderBg = hdr;

        // Table colors -> borders/surfaces
        var tableHeaderBg = GetCol(colors, "TableHeaderBg", theme.HeaderBg);
        var tableBorder = GetCol(colors, "TableBorderStrong", theme.PanelBorder);
        if (!IsZero(tableHeaderBg)) theme.HeaderBg = tableHeaderBg;
        if (!IsZero(tableBorder)) theme.PanelBorder = tableBorder;

        // Optional surfaces (for glass/overlay look)
        theme.Surface0 = GetCol(colors, "ChildBg", theme.PanelBg);
        theme.Surface1 = GetCol(colors, "FrameBg", theme.Btn);
        theme.Surface2 = GetCol(colors, "PopupBg", theme.PanelBg);
        theme.Surface3 = GetCol(colors, "ModalWindowDimBg", new Vector4(theme.PanelBg.X, theme.PanelBg.Y, theme.PanelBg.Z, 0.5f));

        return theme;
    }

    private static Vector4 GetCol(JsonElement colors, string key, Vector4 fallback)
    {
        if (colors.ValueKind == JsonValueKind.Object &&
            colors.TryGetProperty(key, out var arr) &&
            arr.ValueKind == JsonValueKind.Array &&
            arr.GetArrayLength() >= 3)
        {
            float r = GetFloat(arr, 0, fallback.X);
            float g = GetFloat(arr, 1, fallback.Y);
            float b = GetFloat(arr, 2, fallback.Z);
            float a = arr.GetArrayLength() >= 4 ? GetFloat(arr, 3, fallback.W) : fallback.W;
            return new Vector4(r, g, b, a);
        }
        return fallback;
    }

    private static float GetFloat(JsonElement obj, string key, float fallback)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var el) && el.TryGetSingle(out var f) ? f : fallback;

    private static float GetFloat(JsonElement arr, int index, float fallback)
    {
        if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > index)
        {
            var el = arr[index];
            if (el.TryGetSingle(out var f)) return f;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return (float)d;
        }
        return fallback;
    }

    private static bool IsZero(in Vector4 v) => v.X == 0 && v.Y == 0 && v.Z == 0 && v.W == 0;
}
