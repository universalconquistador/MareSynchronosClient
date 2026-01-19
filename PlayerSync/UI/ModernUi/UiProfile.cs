using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.API.Dto.User;
using MareSynchronosq.UI.ModernUi;
using System.Numerics;
using System.Reflection;
using System.Runtime.Serialization;

namespace MareSynchronos.UI.ModernUi;

/// <summary>
/// This was modified to use profiles from the API, thus not generic in nature
/// </summary>
public static class UiProfile
{
    private const float Spacing = 15;

    private static void CalcUvCropToAspect(IDalamudTextureWrap tex, float destAspect, out Vector2 uv0, out Vector2 uv1)
    {
        float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;

        if (tex.Width > 0 && tex.Height > 0 && destAspect > 0)
        {
            var srcAspect = tex.Width / (float)tex.Height;

            // crop left/right
            if (srcAspect > destAspect)
            {
                var keep = destAspect / srcAspect;
                var excess = 1f - keep;
                u0 = excess * 0.5f;
                u1 = 1f - excess * 0.5f;
            }
            // crop top/bottom
            else if (srcAspect < destAspect)
            {
                var keep = srcAspect / destAspect;
                var excess = 1f - keep;
                v0 = excess * 0.5f;
                v1 = 1f - excess * 0.5f;
            }
        }

        uv0 = new Vector2(u0, v0);
        uv1 = new Vector2(u1, v1);
    }

    public static void DrawProfileWindow(Vector4 headerColor, Vector4 bodyColor, float headerHeightPx, float radiusPx, float insetPx = 0.0f)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var winMin = pos;
        var winMax = pos + size;

        var headerH = MathF.Max(0f, headerHeightPx * s);
        var inset = insetPx * s;

        // inner rectangle
        var minX = winMin.X + inset;
        var maxX = winMax.X - inset;
        var minY = winMin.Y + inset;
        var maxY = winMax.Y - inset;

        if (maxX <= minX || maxY <= minY)
            return;

        headerH = MathF.Min(headerH, maxY - winMin.Y);
        var r = MathF.Max(0f, radiusPx * s - inset);

        // header region
        var topMin = new Vector2(minX, minY);
        var topMax = new Vector2(maxX, winMin.Y + headerH);

        if (topMax.Y > topMin.Y)
        {
            dl.AddRectFilled(topMin, topMax, ImGui.GetColorU32(headerColor), r, ImDrawFlags.RoundCornersTopLeft | ImDrawFlags.RoundCornersTopRight);
        }

        // body region
        var bodyMin = new Vector2(minX, winMin.Y + headerH);
        var bodyMax = new Vector2(maxX, maxY);

        if (bodyMax.Y > bodyMin.Y)
        {
            dl.AddRectFilled(bodyMin, bodyMax, ImGui.GetColorU32(bodyColor), r, ImDrawFlags.RoundCornersBottomLeft | ImDrawFlags.RoundCornersBottomRight);
        }
    }

    public static void DrawBackGroundWindow(Vector4 color, float roundingOverridePx = -1f, float insetPx = 0.5f)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var inset = insetPx * s;
        var min = pos + new Vector2(inset, inset);
        var max = pos + size - new Vector2(inset, inset);
        var r = roundingOverridePx >= 0 ? roundingOverridePx * s : ImGui.GetStyle().WindowRounding;
        r = MathF.Max(0f, r - inset);
        dl.AddRectFilled(min, max, ImGui.GetColorU32(color), r, ImDrawFlags.RoundCornersAll);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="headerColor"></param>
    /// <param name="bodyColor"></param>
    /// <param name="headerHeightPx"></param>
    /// <param name="radiusPx"></param>
    /// <param name="borderColor"></param>
    /// <param name="borderThicknessPx"></param>
    /// <param name="insetPx"></param>
    public static void DrawGradientWindow(
    Vector4 headerColor,
    Vector4 bodyColor,
    float headerHeightPx,
    float radiusPx,
    Vector4 borderColor,
    float borderThicknessPx = 1.0f,
    float insetPx = 0.0f)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();

        var winMin = ImGui.GetWindowPos();
        var winMax = winMin + ImGui.GetWindowSize();

        var headerH = MathF.Max(0f, headerHeightPx * s);
        var inset = insetPx * s;

        // Outer rect (used for border)
        var minX = winMin.X + inset;
        var maxX = winMax.X - inset;
        var minY = winMin.Y + inset;
        var maxY = winMax.Y - inset;

        if (maxX <= minX || maxY <= minY)
            return;

        headerH = MathF.Min(headerH, maxY - winMin.Y);

        // Border radius (outer)
        var r = MathF.Max(0f, radiusPx * s - inset);

        // --- NEW: pull fills inward so they can't AA-bleed past the border ---
        var thickness = MathF.Max(0f, borderThicknessPx * s);
        var half = thickness * 0.5f;

        // Extra inset that stays WITHIN the border stroke so we don't create a gap.
        // (For 1px borders this becomes ~1px; for thicker borders ~half+0.5px.)
        var aaPad = MathF.Min(0.5f * s, half);
        var fillInset = (thickness > 0f && borderColor.W > 0f) ? (half + aaPad) : 0f;

        // Inner rect (used for fills)
        var fMinX = minX + fillInset;
        var fMaxX = maxX - fillInset;
        var fMinY = minY + fillInset;
        var fMaxY = maxY - fillInset;

        if (fMaxX <= fMinX || fMaxY <= fMinY)
            return;

        var rFill = MathF.Max(0f, r - fillInset);

        // --- Header (fill, inner rect) ---
        var topMin = new Vector2(fMinX, fMinY);
        var topMax = new Vector2(fMaxX, winMin.Y + headerH);
        topMax.Y = MathF.Min(topMax.Y, fMaxY);

        if (topMax.Y > topMin.Y)
        {
            dl.AddRectFilled(
                topMin,
                topMax,
                ImGui.GetColorU32(headerColor),
                rFill,
                ImDrawFlags.RoundCornersTopLeft | ImDrawFlags.RoundCornersTopRight);
        }

        // --- Body (fill, inner rect) ---
        var bodyMin = new Vector2(fMinX, winMin.Y + headerH);
        var bodyMax = new Vector2(fMaxX, fMaxY);

        if (bodyMax.Y > bodyMin.Y)
        {
            float bodyH = bodyMax.Y - bodyMin.Y;

            if (rFill <= 0f || bodyH <= rFill + 0.5f)
            {
                dl.AddRectFilled(
                    bodyMin,
                    bodyMax,
                    ImGui.GetColorU32(bodyColor),
                    rFill,
                    ImDrawFlags.RoundCornersBottomLeft | ImDrawFlags.RoundCornersBottomRight);
            }
            else
            {
                float gradBottomY = bodyMax.Y - rFill;
                float gradH = MathF.Max(1f, gradBottomY - bodyMin.Y);
                int steps = Math.Clamp((int)((gradBottomY - bodyMin.Y) / UiScale.S(8f)), 12, 220);
                float stepH = (gradBottomY - bodyMin.Y) / steps;

                static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

                for (int i = 0; i < steps; i++)
                {
                    float y0 = bodyMin.Y + stepH * i;
                    float y1 = (i == steps - 1) ? gradBottomY : (y0 + stepH);

                    float tMid = ((y0 + y1) * 0.5f - bodyMin.Y) / gradH;
                    var c = Lerp(headerColor, bodyColor, Math.Clamp(tMid, 0f, 1f));

                    dl.AddRectFilled(
                        new Vector2(bodyMin.X, y0),
                        new Vector2(bodyMax.X, y1),
                        ImGui.GetColorU32(c));
                }

                // Rounded bottom cap
                var seam = 1f;
                dl.AddRectFilled(
                    new Vector2(bodyMin.X, gradBottomY - seam),
                    bodyMax,
                    ImGui.GetColorU32(bodyColor),
                    rFill,
                    ImDrawFlags.RoundCornersBottomLeft | ImDrawFlags.RoundCornersBottomRight);
            }
        }

        // --- Border (outer rect) ---
        if (thickness > 0f && borderColor.W > 0f)
        {
            // Keep border fully inside the outer rect
            var bMin = new Vector2(minX + half, minY + half);
            var bMax = new Vector2(maxX - half, maxY - half);

            if (bMax.X > bMin.X && bMax.Y > bMin.Y)
            {
                var br = MathF.Max(0f, r - half);
                dl.AddRect(bMin, bMax, ImGui.GetColorU32(borderColor), br, ImDrawFlags.RoundCornersAll, thickness);
            }
        }
    }




    public static void DrawAvatar(UiTheme t, IDalamudTextureWrap? avatar, IDalamudTextureWrap? badge, Vector4 borderColor, Vector4 backgroundColor,
        out Vector2 nameMin, out Vector2 nameMax, float bannerHeightPx = 250f, float portraitAspectW = 9f, float portraitAspectH = 16f)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = UiScale.S(t.PanelPad);
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var bannerH = UiScale.S(bannerHeightPx);
        var innerMin = origin + new Vector2(pad, pad);
        var innerMax = origin + new Vector2(width - pad, bannerH - pad);
        var innerW = MathF.Max(1f, innerMax.X - innerMin.X);
        var innerH = MathF.Max(1f, innerMax.Y - innerMin.Y);
        var aspect = portraitAspectW / portraitAspectH;
        var gap = UiScale.S(16f);
        var portraitH = innerH;
        var portraitW = portraitH * aspect;
        var maxPortraitW = innerW * 0.52f;

        if (portraitW > maxPortraitW)
        {
            portraitW = maxPortraitW;
            portraitH = portraitW / aspect;
        }

        var minPortraitW = UiScale.S(130f);
        if (portraitW < minPortraitW)
        {
            portraitW = Math.Min(minPortraitW, innerW);
            portraitH = Math.Min(innerH, portraitW / aspect);
        }

        var imgMin = innerMin;
        var imgMax = imgMin + new Vector2(portraitW, portraitH);

        DrawPortraitAvatar(dl, t, avatar, imgMin, imgMax, borderColor, backgroundColor);

        if (badge != null)
            DrawCircularBadge(dl, t, badge, imgMin, imgMax);

        // Name rect to the right of the portrait
        nameMin = new Vector2(imgMax.X + gap, imgMin.Y);
        nameMax = new Vector2(innerMax.X, imgMin.Y + portraitH);
    }

    public static void DrawNameInfo(UiTheme t, string displayName, string handle, ProfileV1 profile, bool online, Vector2 nameMin, Vector2 nameMax)
    {
        if (nameMax.X <= nameMin.X + UiScale.S(8f) || nameMax.Y <= nameMin.Y + UiScale.S(8f))
            return;

        var restoreCursor = ImGui.GetCursorPos();
        var nameW = Math.Max(1f, nameMax.X - nameMin.X);

        ImGui.SetCursorScreenPos(nameMin);
        ImGui.PushClipRect(nameMin, nameMax, true);

        var localStartX = ImGui.GetCursorPosX();

        ImGui.PushTextWrapPos(localStartX + nameW);
        UiText.ThemedText(t, displayName, UiTextStyle.Heading, UiTheme.ToVec4(profile.Theme.TextPrimary));

        var pronouns = profile.Pronouns.Trim();
        var gap = UiScale.S(6f);
        var line2Y = ImGui.GetCursorScreenPos().Y;

        ImGui.SetCursorScreenPos(new Vector2(nameMin.X, line2Y));
        using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
        {
            ImGui.TextUnformatted(handle);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                ImGui.SetClipboardText(handle);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(UiScale.S(10f), UiScale.S(6f))))
                using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, UiScale.S(8f)))
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Copy UID");
                    ImGui.EndTooltip();
                }
            }

            if (!string.IsNullOrWhiteSpace(pronouns))
            {
                ImGui.SameLine(0, gap);
                ImGui.TextUnformatted("•");
                ImGui.SameLine(0, gap);
                ImGui.TextUnformatted(pronouns);
            }
        }

        var line3Y = ImGui.GetCursorScreenPos().Y;

        ImGui.SetCursorScreenPos(new Vector2(nameMin.X, line3Y));

        var statusLabel = profile.Status.GetAttribute<EnumMemberAttribute>()?.Value ?? profile.Status.ToString();
        if (!string.IsNullOrWhiteSpace(statusLabel))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
            {
                ImGui.TextUnformatted("Relations");
            }
                
            var yAfterLabel = ImGui.GetCursorScreenPos().Y;

            ImGui.SetCursorScreenPos(new Vector2(nameMin.X, yAfterLabel));

            DrawPill(t, "##profile_status", statusLabel!, bg: UiTheme.ToVec4(profile.Theme.Accent), 
                border: UiTheme.ToVec4(profile.Theme.Secondary), fg: UiTheme.ToVec4(profile.Theme.TextPrimary));
        }

        ImGui.PopTextWrapPos();
        ImGui.PopClipRect();
        ImGui.SetCursorPos(restoreCursor);
    }

    public static void DrawInterests(UiTheme t, ProfileV1 profile)
    {
        var list = profile.Interests.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

        if (list is not { Count: > 0 })
            return;

        using (PushContentInset(t, out var width))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
            {
                ImGui.TextUnformatted("Interests");
            }

            DrawPillWrap(t, "interest", list, bg: UiTheme.ToVec4(profile.Theme.Accent), border: UiTheme.ToVec4(profile.Theme.Secondary), 
                fg: UiTheme.ToVec4(profile.Theme.TextPrimary), width);
        }

    }

    public static void DrawAboutMe(UiTheme t, ProfileV1 profile)
    {
        ImGui.Dummy(new Vector2(1f, UiScale.S(Spacing)));

        var about = profile.AboutMe?.Trim();
        if (string.IsNullOrWhiteSpace(about))
            return;

        using (PushContentInset(t, out var width))
        {
            UiText.TextWrappedMaxLines(about, width: width, maxLines: 4, color: UiTheme.ToVec4(profile.Theme.TextPrimary), ellipsisColor: t.TextMuted);
        }
    }

    public static bool DrawNotes(UiTheme t, ProfileV1 profile, ref string notesDraft, ref bool editing, string id, 
        string heading = "Note (only visible to you)", string placeholder = "Click to add a note", int maxLen = 200, int lines = 10)
    {
        ImGui.Dummy(new Vector2(1f, UiScale.S(Spacing)));

        var dl = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();

        using (PushContentInset(t, out var width))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
            {
                ImGui.TextUnformatted(heading);
            }

            var lineH = ImGui.GetTextLineHeightWithSpacing();
            var height = (lineH * lines) + (style.FramePadding.Y * 2f);
            var boxSize = new Vector2(width, height);
            var boxMin = ImGui.GetCursorScreenPos();
            var boxMax = boxMin + boxSize;
            var rounding = style.FrameRounding;
            var bg = UiTheme.ToVec4(profile.Theme.Accent);
            var border = UiTheme.ToVec4(profile.Theme.Secondary);
            var hovered = ImGui.IsMouseHoveringRect(boxMin, boxMax, true);
            var storage = ImGui.GetStateStorage();
            var focusKey = ImGui.GetID($"{id}##notes_focus_next");
            bool focusNext = storage.GetInt(focusKey, 0) != 0;
            bool changed = false;

            if (editing)
                dl.AddRectFilled(boxMin, boxMax, ImGui.GetColorU32(bg), rounding);

            if (hovered || editing)
                dl.AddRect(boxMin, boxMax, ImGui.GetColorU32(border), rounding);

            if (!editing)
            {
                ImGui.SetCursorScreenPos(boxMin);
                if (ImGui.InvisibleButton($"{id}##hit", boxSize))
                {
                    editing = true;
                    storage.SetInt(focusKey, 1);
                }

                var pad = style.FramePadding;
                var textMin = boxMin + pad;
                var textMax = boxMax - pad;

                dl.PushClipRect(textMin, textMax, true);
                try
                {
                    ImGui.SetCursorScreenPos(textMin);

                    var wrapLocalX = ImGui.GetCursorPosX() + MathF.Max(1f, textMax.X - textMin.X);

                    ImGui.PushTextWrapPos(wrapLocalX);

                    if (string.IsNullOrWhiteSpace(notesDraft))
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, UiText.MutedText(UiTheme.ToVec4(profile.Theme.TextPrimary))))
                            ImGui.TextWrapped(placeholder);
                    }
                    else
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
                            ImGui.TextWrapped(notesDraft);
                    }

                    ImGui.PopTextWrapPos();
                }
                finally
                {
                    dl.PopClipRect();
                }

                ImGui.SetCursorScreenPos(new Vector2(boxMin.X, boxMax.Y));
            }
            else
            {
                ImGui.SetCursorScreenPos(boxMin);

                using var _fb1 = ImRaii.PushColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0));
                using var _fb2 = ImRaii.PushColor(ImGuiCol.FrameBgHovered, new Vector4(0, 0, 0, 0));
                using var _fb3 = ImRaii.PushColor(ImGuiCol.FrameBgActive, new Vector4(0, 0, 0, 0));
                using var _bd = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f);

                if (focusNext)
                {
                    ImGui.SetKeyboardFocusHere();
                    storage.SetInt(focusKey, 0);
                }

                using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
                    changed = ImGui.InputTextMultiline(id, ref notesDraft, maxLen, boxSize, ImGuiInputTextFlags.None);

                if (ImGui.IsItemDeactivated())
                    editing = false;

                ImGui.SetCursorScreenPos(new Vector2(boxMin.X, boxMax.Y));
            }

            ImGui.Dummy(new Vector2(1f, UiScale.S(8f)));
            return changed;
        }
    }

    private static void DrawPortraitAvatar(ImDrawListPtr dl, UiTheme t, IDalamudTextureWrap? avatar, 
        Vector2 slotMin, Vector2 slotMax, Vector4 borderColor, Vector4 backgroundColor)
    {
        var size = slotMax - slotMin;
        if (size.X <= 1f || size.Y <= 1f)
            return;

        var rounding = UiScale.S(t.RadiusLg);
        var borderThickness = UiScale.S(3f);

        dl.AddRectFilled(slotMin, slotMax, ImGui.GetColorU32(backgroundColor), rounding);

        if (avatar != null)
        {
            var destAspect = size.X / size.Y;
            CalcUvCropToAspect(avatar, destAspect, out var uv0, out var uv1);

            dl.AddImageRounded(avatar.Handle, slotMin, slotMax, uv0, uv1, 0xFFFFFFFF, rounding, ImDrawFlags.RoundCornersAll
            );
        }

        dl.AddRect(slotMin, slotMax, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.RoundCornersAll, borderThickness);
    }

    private static void DrawCircularBadge(ImDrawListPtr dl, UiTheme t, IDalamudTextureWrap badge, Vector2 avatarMin, Vector2 avatarMax)
    {
        var b = UiScale.S(28f);
        var bMin = new Vector2(avatarMax.X - b, avatarMax.Y - b);
        var bMax = bMin + new Vector2(b, b);
        var radius = b * 0.5f;
        var center = (bMin + bMax) * 0.5f;

        dl.AddCircleFilled(center, radius, ImGui.GetColorU32(t.PanelBg));

        dl.AddImageRounded(badge.Handle, bMin, bMax, new Vector2(0, 0), new Vector2(1, 1), 0xFFFFFFFF, radius, ImDrawFlags.RoundCornersAll
        );

        var borderThickness = UiScale.S(1.5f);

        dl.AddCircle(center, radius - (borderThickness * 0.5f), ImGui.GetColorU32(t.Border), 0, borderThickness);
    }

    private static Vector2 CalcPillSize(string text)
    {
        var padX = UiScale.S(10f);
        var padY = UiScale.S(5f);
        var ts = ImGui.CalcTextSize(text);
        return new Vector2(ts.X + padX * 2f, ts.Y + padY * 2f);
    }

    private static void DrawPill(UiTheme t, string id, string text, Vector4 bg, Vector4 border, Vector4 fg)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = UiScale.S(10f);
        var padY = UiScale.S(5f);
        var rounding = UiScale.S(999f); // "pill"
        var ts = ImGui.CalcTextSize(text);
        var size = new Vector2(ts.X + padX * 2f, ts.Y + padY * 2f);

        ImGui.InvisibleButton(id, size);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        dl.AddRectFilled(min, max, ImGui.GetColorU32(bg), rounding);
        dl.AddText(min + new Vector2(padX, padY), ImGui.GetColorU32(fg), text);
    }

    private static void DrawPillWrap(UiTheme t, string idPrefix, IReadOnlyList<string> items, Vector4 bg, Vector4 border, Vector4 fg, float width)
    {
        var gap = UiScale.S(8f);
        var style = ImGui.GetStyle();

        using var _spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing,
            new Vector2(style.ItemSpacing.X, gap));

        var start = ImGui.GetCursorScreenPos();
        var winRight = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
        var pad = UiScale.S(t.PanelPad);
        var maxX = MathF.Min(start.X + MathF.Max(1f, width), winRight - pad) - 1f;
        bool first = true;

        for (var i = 0; i < items.Count; i++)
        {
            var text = items[i]?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var pillSize = CalcPillSize(text);

            if (!first)
            {
                var prevMaxX = ImGui.GetItemRectMax().X;
                var nextStartX = prevMaxX + gap;

                if (nextStartX + pillSize.X <= maxX)
                    ImGui.SameLine(0, gap);
            }

            DrawPill(t, $"##{idPrefix}_{i}", text, bg: bg, border: border,fg: fg);

            first = false;
        }
    }

    private readonly struct Scope : IDisposable
    {
        private readonly Action _end;
        public Scope(Action end) => _end = end;
        public void Dispose() => _end();
    }

    private static IDisposable PushContentInset(UiTheme t, out float innerWidth)
    {
        var pad = UiScale.S(t.PanelPad);

        ImGui.Indent(pad);

        innerWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - pad);

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerWidth);

        return new Scope(() =>
        {
            ImGui.PopTextWrapPos();
            ImGui.Unindent(pad);
        });
    }
}
