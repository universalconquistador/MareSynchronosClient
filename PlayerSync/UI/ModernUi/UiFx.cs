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
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetForegroundDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var rounding = roundingOverridePx >= 0 ? roundingOverridePx * scale : ImGui.GetStyle().WindowRounding;
        var outerInset = outerInsetPx * scale;
        var outerMin = windowPos + new Vector2(outerInset, outerInset);
        var outerMax = windowPos + windowSize - new Vector2(outerInset, outerInset);

        drawList.PushClipRect(windowPos, windowPos + windowSize, true);
        try
        {
            DrawShimmerRect(drawList, outerMin, outerMax, rounding - outerInset, gold, thicknessPx * scale,
                glowStrength: 0.22f, shimmerStrength: 1.10f, sparkleCount: 80, intensity: 1.15f);

            if (drawInner)
            {
                var innerInset = innerInsetPx * scale;
                var innerMin = windowPos + new Vector2(innerInset, innerInset);
                var innerMax = windowPos + windowSize - new Vector2(innerInset, innerInset);

                DrawShimmerRect(drawList, innerMin, innerMax, MathF.Max(0f, rounding - innerInset), gold, MathF.Max(1f, thicknessPx * scale * 0.75f),
                    glowStrength: 0.10f, shimmerStrength: 0.45f, sparkleCount: 24, intensity: 0.60f);
            }
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    private static void DrawShimmerRect(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding, Vector4 gold,
        float thickness, float glowStrength, float shimmerStrength, int sparkleCount, float intensity = 1.0f)
    {
        if (max.X <= min.X + 2f || max.Y <= min.Y + 2f)
            return;

        intensity = Math.Clamp(intensity, 0.25f, 3.0f);

        var time = (float)ImGui.GetTime();
        var scale = ImGuiHelpers.GlobalScale;
        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var cornerRadius = MathF.Max(0f, MathF.Min(rounding, 0.5f * MathF.Min(width, height)));
        var edgeInset = MathF.Max(0.5f * thickness - 0.5f * scale, 0f);
        var edgeMin = min + new Vector2(edgeInset, edgeInset);
        var edgeMax = max - new Vector2(edgeInset, edgeInset);
        var edgeWidth = edgeMax.X - edgeMin.X;
        var edgeHeight = edgeMax.Y - edgeMin.Y;
        var edgeRadius = MathF.Max(0f, MathF.Min(cornerRadius - edgeInset, 0.5f * MathF.Min(edgeWidth, edgeHeight)));
        var edgeCol = gold; edgeCol.W = 0.95f;

        dl.AddRect(edgeMin, edgeMax, ImGui.GetColorU32(edgeCol), edgeRadius, ImDrawFlags.RoundCornersAll, thickness);

        var maxGlowThick = thickness * (2.4f + 0.7f * intensity);
        var strokePad = 0.5f * MathF.Max(thickness, maxGlowThick) + 1.0f * scale;

        min += new Vector2(strokePad, strokePad);
        max -= new Vector2(strokePad, strokePad);

        if (max.X <= min.X + 2f || max.Y <= min.Y + 2f)
            return;

        width = max.X - min.X;
        height = max.Y - min.Y;
        cornerRadius = MathF.Max(0f, MathF.Min(cornerRadius - strokePad, 0.5f * MathF.Min(width, height)));

        float pulse = 0.65f + 0.35f * MathF.Sin(time * 1.7f);
        float glowA = glowStrength * (0.75f + 0.55f * pulse) * intensity;
        var baseCol = gold; baseCol.W = 0.78f;
        var glowCol = gold; glowCol.W = glowA;
        var glowThick1 = thickness * (2.0f + 0.6f * intensity);
        var glowThick2 = thickness * (1.2f + 0.3f * intensity);

        dl.AddRect(min, max, ImGui.GetColorU32(glowCol), cornerRadius, ImDrawFlags.RoundCornersAll, glowThick1);
        dl.AddRect(min, max, ImGui.GetColorU32(new Vector4(gold.X, gold.Y, gold.Z, glowA * 0.55f)), cornerRadius, ImDrawFlags.RoundCornersAll, glowThick2);
        dl.AddRect(min, max, ImGui.GetColorU32(baseCol), cornerRadius, ImDrawFlags.RoundCornersAll, thickness);

        float perimeter = GetRoundedPerimeterLength(min, max, cornerRadius);
        int segmentCount = (int)Math.Clamp(perimeter / (7.5f * scale), 160, 420);

        segmentCount = (int)(segmentCount * (0.90f + 0.35f * intensity));

        DrawBand(dl, min, max, cornerRadius, thickness, baseCol, time, speed: 0.11f, sigma: 0.040f, phaseOffset: 0.00f, segmentCount, shimmerStrength * (1.00f * intensity));
        DrawBand(dl, min, max, cornerRadius, thickness, baseCol, time, speed: 0.08f, sigma: 0.060f, phaseOffset: 0.37f, segmentCount, shimmerStrength * (0.70f * intensity));
        DrawBand(dl, min, max, cornerRadius, thickness, baseCol, time, speed: 0.06f, sigma: 0.090f, phaseOffset: 0.71f, segmentCount, shimmerStrength * (0.45f * intensity));
        DrawBand(dl, min, max, cornerRadius, thickness, baseCol, time, speed: -0.05f, sigma: 0.120f, phaseOffset: 0.15f, segmentCount, shimmerStrength * (0.35f * intensity));

        var head = GetPerimeterPointRounded(min, max, cornerRadius, (time * 0.11f) % 1f);

        dl.AddCircleFilled(head, (2.4f + 0.5f * intensity) * scale, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f)));

        int glitter = (int)(sparkleCount * (1.15f + 0.75f * intensity));
        for (int sparkleIndex = 0; sparkleIndex < glitter; sparkleIndex++)
        {
            float unit = GetHash01(sparkleIndex);
            float tw = 0.35f + 0.65f * MathF.Max(0f, MathF.Sin(time * (1.3f + 2.7f * GetHash01(sparkleIndex * 13)) + sparkleIndex));
            float travel = (time * 0.02f * (0.5f + GetHash01(sparkleIndex * 7))) % 1f;
            float up = (unit + travel) % 1f;

            var point = GetPerimeterPointRounded(min, max, cornerRadius, up);
            float rr = (0.7f + tw * (1.2f + 0.5f * intensity)) * scale;
            float alpha = (0.05f + 0.16f * tw) * (0.55f + 0.45f * intensity);
            dl.AddCircleFilled(point, rr, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));
        }
    }

    private static void DrawBand(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding, float thickness,
        Vector4 baseCol, float time, float speed, float sigma, float phaseOffset, int segs, float strength)
    {
        var phase = (time * speed + phaseOffset) % 1f;
        Vector4 white = new(1f, 1f, 1f, 1f);

        for (int segmentIndex = 0; segmentIndex < segs; segmentIndex++)
        {
            float u0 = segmentIndex / (float)segs;
            float u1 = (segmentIndex + 1) / (float)segs;
            float um = (u0 + u1) * 0.5f;

            float distance = MathF.Abs(um - phase);
            distance = MathF.Min(distance, 1f - distance);

            float band = MathF.Exp(-(distance * distance) / (2f * sigma * sigma));

            var p0 = GetPerimeterPointRounded(min, max, rounding, u0);
            var p1 = GetPerimeterPointRounded(min, max, rounding, u1);

            var color = Lerp(baseCol, white, band * 0.85f);
            color.W = (0.10f + band * 0.90f) * strength;

            dl.AddLine(p0, p1, ImGui.GetColorU32(color), thickness);
        }
    }

    private static float GetRoundedPerimeterLength(Vector2 min, Vector2 max, float rounding)
    {
        var width = MathF.Max(0f, max.X - min.X);
        var height = MathF.Max(0f, max.Y - min.Y);
        if (rounding <= 0f) return 2f * (width + height);

        rounding = MathF.Min(rounding, 0.5f * MathF.Min(width, height));
        var w2 = MathF.Max(0f, width - 2f * rounding);
        var h2 = MathF.Max(0f, height - 2f * rounding);
        return 2f * (w2 + h2) + 2f * MathF.PI * rounding;
    }

    private static Vector2 GetPerimeterPointRounded(Vector2 min, Vector2 max, float rounding, float unit)
    {
        unit = unit - MathF.Floor(unit);

        var width = max.X - min.X;
        var height = max.Y - min.Y;
        if (width <= 0f || height <= 0f) return min;

        if (rounding <= 0f)
            return GetPerimeterPoint(min, max, unit);

        rounding = MathF.Min(rounding, 0.5f * MathF.Min(width, height));

        var w2 = MathF.Max(0f, width - 2f * rounding);
        var h2 = MathF.Max(0f, height - 2f * rounding);
        var arc = 0.5f * MathF.PI * rounding;
        var per = 2f * (w2 + h2) + 4f * arc;

        float distance = unit * per;

        if (distance < w2) return new Vector2(min.X + rounding + distance, min.Y);
        distance -= w2;

        if (distance < arc)
        {
            float arcUnit = distance / arc;
            float ang = -0.5f * MathF.PI + arcUnit * 0.5f * MathF.PI;
            var center = new Vector2(max.X - rounding, min.Y + rounding);
            return center + new Vector2(rounding * MathF.Cos(ang), rounding * MathF.Sin(ang));
        }
        distance -= arc;

        if (distance < h2) return new Vector2(max.X, min.Y + rounding + distance);
        distance -= h2;

        if (distance < arc)
        {
            float arcUnit = distance / arc;
            float ang = 0f + arcUnit * 0.5f * MathF.PI;
            var center = new Vector2(max.X - rounding, max.Y - rounding);
            return center + new Vector2(rounding * MathF.Cos(ang), rounding * MathF.Sin(ang));
        }
        distance -= arc;

        if (distance < w2) return new Vector2(max.X - rounding - distance, max.Y);
        distance -= w2;

        if (distance < arc)
        {
            float arcUnit = distance / arc;
            float ang = 0.5f * MathF.PI + arcUnit * 0.5f * MathF.PI;
            var center = new Vector2(min.X + rounding, max.Y - rounding);
            return center + new Vector2(rounding * MathF.Cos(ang), rounding * MathF.Sin(ang));
        }
        distance -= arc;

        if (distance < h2) return new Vector2(min.X, max.Y - rounding - distance);
        distance -= h2;

        {
            float arcUnit = distance / arc;
            float ang = MathF.PI + arcUnit * 0.5f * MathF.PI;
            var center = new Vector2(min.X + rounding, min.Y + rounding);
            return center + new Vector2(rounding * MathF.Cos(ang), rounding * MathF.Sin(ang));
        }
    }

    private static float GetHash01(int seed)
    {
        unchecked
        {
            uint hash = (uint)seed;
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFF) / 16777215f;
        }
    }

    private static Vector4 Lerp(Vector4 fromColor, Vector4 toColor, float blend) => fromColor + (toColor - fromColor) * Math.Clamp(blend, 0f, 1f);

    private static Vector2 GetPerimeterPoint(Vector2 min, Vector2 max, float unit)
    {
        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var per = 2f * (width + height);
        float distance = (unit % 1f) * per;

        if (distance < width) return new Vector2(min.X + distance, min.Y);
        distance -= width;
        if (distance < height) return new Vector2(max.X, min.Y + distance);
        distance -= height;
        if (distance < width) return new Vector2(max.X - distance, max.Y);
        distance -= width;

        return new Vector2(min.X, max.Y - distance);
    }

    public static void DrawContentVerticalGradientBgRoundedToWindow(Vector4 top, Vector4 bottom, int steps = 220,
        float roundingOverridePx = -1f, float windowInsetPx = 2.0f)
    {
        steps = Math.Clamp(steps, 16, 256);

        var drawList = ImGui.GetWindowDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var rawMin = ImGui.GetWindowPos();
        var rawMax = rawMin + ImGui.GetWindowSize();
        var inset = windowInsetPx * scale;
        var winMin = rawMin + new Vector2(inset, inset);
        var winMax = rawMax - new Vector2(inset, inset);
        var contentMin = ImGui.GetCursorScreenPos();
        var contentMax = contentMin + ImGui.GetContentRegionAvail();
        var roundingBase = roundingOverridePx >= 0 ? UiScale.ScaledFloat(roundingOverridePx) : ImGui.GetStyle().WindowRounding;
        var rounding = MathF.Max(0f, roundingBase - inset);

        drawList.PushClipRect(rawMin, rawMax, true);
        try
        {
            var height = MathF.Max(1f, contentMax.Y - contentMin.Y);
            var stepH = height / steps;
            var topArcY = winMin.Y + rounding;
            var botArcY = winMax.Y - rounding;

            for (var stepIndex = 0; stepIndex < steps; stepIndex++)
            {
                var y0 = contentMin.Y + stepIndex * stepH;
                var y1 = (stepIndex == steps - 1) ? contentMax.Y : (y0 + stepH);
                var yMid = (y0 + y1) * 0.5f;

                float insetX = 0f;
                if (rounding > 0f)
                {
                    if (yMid < winMin.Y + rounding)
                    {
                        var dy = topArcY - yMid;
                        var dx = MathF.Sqrt(MathF.Max(0f, rounding * rounding - dy * dy));
                        insetX = rounding - dx;
                    }
                    else if (yMid > winMax.Y - rounding)
                    {
                        var dy = yMid - botArcY;
                        var dx = MathF.Sqrt(MathF.Max(0f, rounding * rounding - dy * dy));
                        insetX = rounding - dx;
                    }
                }

                var winX0 = winMin.X + insetX;
                var winX1 = winMax.X - insetX;
                var x0 = MathF.Max(contentMin.X, winX0);
                var x1 = MathF.Min(contentMax.X, winX1);

                if (x1 <= x0) continue;

                var tt = (stepIndex + 0.5f) / steps;
                var c = Lerp(top, bottom, tt);

                drawList.AddRectFilled(new Vector2(x0, y0), new Vector2(x1, y1), ImGui.GetColorU32(c));
            }
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="color"></param>
    /// <param name="roundingOverridePx"></param>
    public static void DrawWindowRoundedFill(Vector4 color, float roundingOverridePx = -1f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var min = winPos;
        var max = winPos + winSize;
        var rounding = roundingOverridePx >= 0 ? UiScale.ScaledFloat(roundingOverridePx) : ImGui.GetStyle().WindowRounding;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="color"></param>
    /// <param name="roundingOverridePx"></param>
    public static void DrawContentRoundedFill(Vector4 color, float roundingOverridePx = -1f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var min = origin;
        var max = origin + size;
        var rounding = roundingOverridePx >= 0 ? UiScale.ScaledFloat(roundingOverridePx) : ImGui.GetStyle().WindowRounding;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(color), rounding);
    }

    public static void DrawContentRoundedFillMatched(Vector4 color, float roundingOverridePx = -1f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var crMin = winPos + ImGui.GetWindowContentRegionMin();
        var crMax = winPos + ImGui.GetWindowContentRegionMax();
        var rWin = roundingOverridePx >= 0 ? UiScale.ScaledFloat(roundingOverridePx) : ImGui.GetStyle().WindowRounding;
        var pad = ImGui.GetStyle().WindowPadding;
        var inset = MathF.Min(pad.X, pad.Y);
        var rContent = MathF.Max(0f, rWin - inset);

        drawList.AddRectFilled(crMin, crMax, ImGui.GetColorU32(color), rContent);
    }
}
