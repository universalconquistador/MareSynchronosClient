using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace MareSynchronos.UI.ModernUi;

/// <summary>
/// Adapted from ModernUi
/// </summary>
public static class UiFx
{
    public static void DrawWindowShimmerBorderFull(Vector4 gold, float thicknessPx = 1.35f, float outerInsetPx = 0f, 
        float innerInsetPx = 12f, bool drawInner = true, float roundingOverridePx = -1f)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetForegroundDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var rounding = roundingOverridePx >= 0 ? roundingOverridePx * s : ImGui.GetStyle().WindowRounding;
        var outerInset = outerInsetPx * s;
        var outerMin = pos + new Vector2(outerInset, outerInset);
        var outerMax = pos + size - new Vector2(outerInset, outerInset);

        dl.PushClipRect(pos, pos + size, true);
        try
        {
            DrawShimmerRect(dl, outerMin, outerMax, rounding - outerInset, gold, thicknessPx * s, 
                glowStrength: 0.22f, shimmerStrength: 1.10f, sparkleCount: 80, intensity: 1.15f);

            if (drawInner)
            {
                var innerInset = innerInsetPx * s;
                var innerMin = pos + new Vector2(innerInset, innerInset);
                var innerMax = pos + size - new Vector2(innerInset, innerInset);

                DrawShimmerRect(dl, innerMin, innerMax, MathF.Max(0f, rounding - innerInset), gold, MathF.Max(1f, thicknessPx * s * 0.75f), 
                    glowStrength: 0.10f, shimmerStrength: 0.45f, sparkleCount: 24, intensity: 0.60f);
            }
        }
        finally
        {
            dl.PopClipRect();
        }
    }

    private static void DrawShimmerRect(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding, Vector4 gold,
        float thickness, float glowStrength, float shimmerStrength, int sparkleCount, float intensity = 1.0f)
    {
        if (max.X <= min.X + 2f || max.Y <= min.Y + 2f)
            return;

        intensity = Math.Clamp(intensity, 0.25f, 3.0f);

        var time = (float)ImGui.GetTime();
        var s = ImGuiHelpers.GlobalScale;
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var r = MathF.Max(0f, MathF.Min(rounding, 0.5f * MathF.Min(w, h)));
        var edgeInset = MathF.Max(0.5f * thickness - 0.5f * s, 0f);
        var edgeMin = min + new Vector2(edgeInset, edgeInset);
        var edgeMax = max - new Vector2(edgeInset, edgeInset);
        var edgeW = edgeMax.X - edgeMin.X;
        var edgeH = edgeMax.Y - edgeMin.Y;
        var edgeR = MathF.Max(0f, MathF.Min(r - edgeInset, 0.5f * MathF.Min(edgeW, edgeH)));
        var edgeCol = gold; edgeCol.W = 0.95f;

        dl.AddRect(edgeMin, edgeMax, ImGui.GetColorU32(edgeCol), edgeR, ImDrawFlags.RoundCornersAll, thickness);

        var maxGlowThick = thickness * (2.4f + 0.7f * intensity);
        var strokePad = 0.5f * MathF.Max(thickness, maxGlowThick) + 1.0f * s;

        min += new Vector2(strokePad, strokePad);
        max -= new Vector2(strokePad, strokePad);

        if (max.X <= min.X + 2f || max.Y <= min.Y + 2f)
            return;

        w = max.X - min.X;
        h = max.Y - min.Y;
        r = MathF.Max(0f, MathF.Min(r - strokePad, 0.5f * MathF.Min(w, h)));

        float pulse = 0.65f + 0.35f * MathF.Sin(time * 1.7f);
        float glowA = glowStrength * (0.75f + 0.55f * pulse) * intensity;
        var baseCol = gold; baseCol.W = 0.78f;
        var glowCol = gold; glowCol.W = glowA;
        var glowThick1 = thickness * (2.0f + 0.6f * intensity);
        var glowThick2 = thickness * (1.2f + 0.3f * intensity);

        dl.AddRect(min, max, ImGui.GetColorU32(glowCol), r, ImDrawFlags.RoundCornersAll, glowThick1);
        dl.AddRect(min, max, ImGui.GetColorU32(new Vector4(gold.X, gold.Y, gold.Z, glowA * 0.55f)), r, ImDrawFlags.RoundCornersAll, glowThick2);
        dl.AddRect(min, max, ImGui.GetColorU32(baseCol), r, ImDrawFlags.RoundCornersAll, thickness);

        float per = RoundedPerimeterLength(min, max, r);
        int segs = (int)Math.Clamp(per / (7.5f * s), 160, 420);

        segs = (int)(segs * (0.90f + 0.35f * intensity));

        DrawBand(dl, min, max, r, thickness, baseCol, time, speed: 0.11f, sigma: 0.040f, phaseOffset: 0.00f, segs, shimmerStrength * (1.00f * intensity));
        DrawBand(dl, min, max, r, thickness, baseCol, time, speed: 0.08f, sigma: 0.060f, phaseOffset: 0.37f, segs, shimmerStrength * (0.70f * intensity));
        DrawBand(dl, min, max, r, thickness, baseCol, time, speed: 0.06f, sigma: 0.090f, phaseOffset: 0.71f, segs, shimmerStrength * (0.45f * intensity));
        DrawBand(dl, min, max, r, thickness, baseCol, time, speed: -0.05f, sigma: 0.120f, phaseOffset: 0.15f, segs, shimmerStrength * (0.35f * intensity));

        var head = PerimeterPointRounded(min, max, r, (time * 0.11f) % 1f);

        dl.AddCircleFilled(head, (2.4f + 0.5f * intensity) * s, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f)));

        int glitter = (int)(sparkleCount * (1.15f + 0.75f * intensity));
        for (int i = 0; i < glitter; i++)
        {
            float u = Hash01(i);
            float tw = 0.35f + 0.65f * MathF.Max(0f, MathF.Sin(time * (1.3f + 2.7f * Hash01(i * 13)) + i));
            float travel = (time * 0.02f * (0.5f + Hash01(i * 7))) % 1f;
            float up = (u + travel) % 1f;

            var p = PerimeterPointRounded(min, max, r, up);
            float rr = (0.7f + tw * (1.2f + 0.5f * intensity)) * s;
            float a = (0.05f + 0.16f * tw) * (0.55f + 0.45f * intensity);
            dl.AddCircleFilled(p, rr, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, a)));
        }
    }

    private static void DrawBand(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding, float thickness, 
        Vector4 baseCol, float time, float speed, float sigma, float phaseOffset, int segs, float strength)
    {
        var phase = (time * speed + phaseOffset) % 1f;
        Vector4 white = new(1f, 1f, 1f, 1f);

        for (int i = 0; i < segs; i++)
        {
            float u0 = i / (float)segs;
            float u1 = (i + 1) / (float)segs;
            float um = (u0 + u1) * 0.5f;

            float d = MathF.Abs(um - phase);
            d = MathF.Min(d, 1f - d);

            float band = MathF.Exp(-(d * d) / (2f * sigma * sigma));

            var p0 = PerimeterPointRounded(min, max, rounding, u0);
            var p1 = PerimeterPointRounded(min, max, rounding, u1);

            var c = Lerp(baseCol, white, band * 0.85f);
            c.W = (0.10f + band * 0.90f) * strength;

            dl.AddLine(p0, p1, ImGui.GetColorU32(c), thickness);
        }
    }

    private static float RoundedPerimeterLength(Vector2 min, Vector2 max, float r)
    {
        var w = MathF.Max(0f, max.X - min.X);
        var h = MathF.Max(0f, max.Y - min.Y);
        if (r <= 0f) return 2f * (w + h);

        r = MathF.Min(r, 0.5f * MathF.Min(w, h));
        var w2 = MathF.Max(0f, w - 2f * r);
        var h2 = MathF.Max(0f, h - 2f * r);
        return 2f * (w2 + h2) + 2f * MathF.PI * r;
    }

    private static Vector2 PerimeterPointRounded(Vector2 min, Vector2 max, float r, float u)
    {
        u = u - MathF.Floor(u);

        var w = max.X - min.X;
        var h = max.Y - min.Y;
        if (w <= 0f || h <= 0f) return min;

        if (r <= 0f)
            return PerimeterPoint(min, max, u);

        r = MathF.Min(r, 0.5f * MathF.Min(w, h));

        var w2 = MathF.Max(0f, w - 2f * r);
        var h2 = MathF.Max(0f, h - 2f * r);
        var arc = 0.5f * MathF.PI * r;
        var per = 2f * (w2 + h2) + 4f * arc;

        float d = u * per;

        if (d < w2) return new Vector2(min.X + r + d, min.Y);
        d -= w2;

        if (d < arc)
        {
            float t = d / arc;
            float ang = -0.5f * MathF.PI + t * 0.5f * MathF.PI;
            var c = new Vector2(max.X - r, min.Y + r);
            return c + new Vector2(r * MathF.Cos(ang), r * MathF.Sin(ang));
        }
        d -= arc;

        if (d < h2) return new Vector2(max.X, min.Y + r + d);
        d -= h2;

        if (d < arc)
        {
            float t = d / arc;
            float ang = 0f + t * 0.5f * MathF.PI;
            var c = new Vector2(max.X - r, max.Y - r);
            return c + new Vector2(r * MathF.Cos(ang), r * MathF.Sin(ang));
        }
        d -= arc;

        if (d < w2) return new Vector2(max.X - r - d, max.Y);
        d -= w2;

        if (d < arc)
        {
            float t = d / arc;
            float ang = 0.5f * MathF.PI + t * 0.5f * MathF.PI;
            var c = new Vector2(min.X + r, max.Y - r);
            return c + new Vector2(r * MathF.Cos(ang), r * MathF.Sin(ang));
        }
        d -= arc;

        if (d < h2) return new Vector2(min.X, max.Y - r - d);
        d -= h2;

        {
            float t = d / arc;
            float ang = MathF.PI + t * 0.5f * MathF.PI;
            var c = new Vector2(min.X + r, min.Y + r);
            return c + new Vector2(r * MathF.Cos(ang), r * MathF.Sin(ang));
        }
    }

    private static float Hash01(int n)
    {
        unchecked
        {
            uint x = (uint)n;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return (x & 0x00FFFFFF) / 16777215f;
        }
    }

    private static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private static Vector2 PerimeterPoint(Vector2 min, Vector2 max, float u)
    {
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var per = 2f * (w + h);
        float d = (u % 1f) * per;

        if (d < w) return new Vector2(min.X + d, min.Y);
        d -= w;
        if (d < h) return new Vector2(max.X, min.Y + d);
        d -= h;
        if (d < w) return new Vector2(max.X - d, max.Y);
        d -= w;

        return new Vector2(min.X, max.Y - d);
    }

    public static void DrawContentVerticalGradientBgRoundedToWindow(Vector4 top, Vector4 bottom, int steps = 220, 
        float roundingOverridePx = -1f, float windowInsetPx = 2.0f)
    {
        steps = Math.Clamp(steps, 16, 256);

        var dl = ImGui.GetWindowDrawList();
        var s = ImGuiHelpers.GlobalScale;
        var rawMin = ImGui.GetWindowPos();
        var rawMax = rawMin + ImGui.GetWindowSize();
        var inset = windowInsetPx * s;
        var winMin = rawMin + new Vector2(inset, inset);
        var winMax = rawMax - new Vector2(inset, inset);
        var contentMin = ImGui.GetCursorScreenPos();
        var contentMax = contentMin + ImGui.GetContentRegionAvail();
        var r0 = roundingOverridePx >= 0 ? UiScale.S(roundingOverridePx) : ImGui.GetStyle().WindowRounding;
        var r = MathF.Max(0f, r0 - inset);

        dl.PushClipRect(rawMin, rawMax, true);
        try
        {
            var h = MathF.Max(1f, contentMax.Y - contentMin.Y);
            var stepH = h / steps;
            var topArcY = winMin.Y + r;
            var botArcY = winMax.Y - r;

            for (var i = 0; i < steps; i++)
            {
                var y0 = contentMin.Y + i * stepH;
                var y1 = (i == steps - 1) ? contentMax.Y : (y0 + stepH);
                var yMid = (y0 + y1) * 0.5f;

                float insetX = 0f;
                if (r > 0f)
                {
                    if (yMid < winMin.Y + r)
                    {
                        var dy = topArcY - yMid;
                        var dx = MathF.Sqrt(MathF.Max(0f, r * r - dy * dy));
                        insetX = r - dx;
                    }
                    else if (yMid > winMax.Y - r)
                    {
                        var dy = yMid - botArcY;
                        var dx = MathF.Sqrt(MathF.Max(0f, r * r - dy * dy));
                        insetX = r - dx;
                    }
                }

                var winX0 = winMin.X + insetX;
                var winX1 = winMax.X - insetX;
                var x0 = MathF.Max(contentMin.X, winX0);
                var x1 = MathF.Min(contentMax.X, winX1);

                if (x1 <= x0) continue;

                var tt = (i + 0.5f) / steps;
                var c = Lerp(top, bottom, tt);

                dl.AddRectFilled(new Vector2(x0, y0), new Vector2(x1, y1), ImGui.GetColorU32(c));
            }
        }
        finally
        {
            dl.PopClipRect();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="color"></param>
    /// <param name="roundingOverridePx"></param>
    public static void DrawWindowRoundedFill(Vector4 color, float roundingOverridePx = -1f)
    {
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var min = winPos;
        var max = winPos + winSize;
        var rounding = roundingOverridePx >= 0 ? UiScale.S(roundingOverridePx) : ImGui.GetStyle().WindowRounding;

        dl.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="color"></param>
    /// <param name="roundingOverridePx"></param>
    public static void DrawContentRoundedFill(Vector4 color, float roundingOverridePx = -1f)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var min = origin;
        var max = origin + size;
        var rounding = roundingOverridePx >= 0 ? UiScale.S(roundingOverridePx) : ImGui.GetStyle().WindowRounding;

        dl.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
    }

    public static void DrawContentRoundedFillMatched(Vector4 color, float roundingOverridePx = -1f)
    {
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var crMin = winPos + ImGui.GetWindowContentRegionMin();
        var crMax = winPos + ImGui.GetWindowContentRegionMax();
        var rWin = roundingOverridePx >= 0 ? UiScale.S(roundingOverridePx) : ImGui.GetStyle().WindowRounding;
        var pad = ImGui.GetStyle().WindowPadding;
        var inset = MathF.Min(pad.X, pad.Y);
        var rContent = MathF.Max(0f, rWin - inset);

        dl.AddRectFilled(crMin, crMax, ImGui.GetColorU32(color), rContent);
    }
}
