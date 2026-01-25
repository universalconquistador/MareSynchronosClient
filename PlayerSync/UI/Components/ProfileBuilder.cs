using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.User;
using MareSynchronos.UI.ModernUi;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public static class ProfileBuilder
{
    public static void DrawProfileWindow(Vector4 headerColor, Vector4 bodyColor, float headerHeightPx, float radiusPx, float insetPx = 0.0f)
    {
        var globalScale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var windowMin = windowPos;
        var windowMax = windowPos + windowSize;

        var headerHeight = MathF.Max(0f, headerHeightPx * globalScale);
        var inset = insetPx * globalScale;

        // inner rectangle
        var minX = windowMin.X + inset;
        var maxX = windowMax.X - inset;
        var minY = windowMin.Y + inset;
        var maxY = windowMax.Y - inset;

        if (maxX <= minX || maxY <= minY)
            return;

        headerHeight = MathF.Min(headerHeight, maxY - windowMin.Y);
        var rounding = MathF.Max(0f, radiusPx * globalScale - inset);

        // header region
        var headerMin = new Vector2(minX, minY);
        var headerMax = new Vector2(maxX, windowMin.Y + headerHeight);

        if (headerMax.Y > headerMin.Y)
        {
            drawList.AddRectFilled(headerMin, headerMax, ImGui.GetColorU32(headerColor), rounding, ImDrawFlags.RoundCornersTopLeft | ImDrawFlags.RoundCornersTopRight);
        }

        // body region
        var bodyMin = new Vector2(minX, windowMin.Y + headerHeight);
        var bodyMax = new Vector2(maxX, maxY);

        if (bodyMax.Y > bodyMin.Y)
        {
            drawList.AddRectFilled(bodyMin, bodyMax, ImGui.GetColorU32(bodyColor), rounding, ImDrawFlags.RoundCornersBottomLeft | ImDrawFlags.RoundCornersBottomRight);
        }
    }

    public static void DrawBackGroundWindow(Vector4 color, float roundingOverridePx = -1f, float insetPx = 0.5f)
    {
        var globalScale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var inset = insetPx * globalScale;
        var fillMin = windowPos + new Vector2(inset, inset);
        var fillMax = windowPos + windowSize - new Vector2(inset, inset);
        var rounding = roundingOverridePx >= 0 ? roundingOverridePx * globalScale : ImGui.GetStyle().WindowRounding;
        rounding = MathF.Max(0f, rounding - inset);
        drawList.AddRectFilled(fillMin, fillMax, ImGui.GetColorU32(color), rounding, ImDrawFlags.RoundCornersAll);
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
    public static void DrawGradientWindow(Vector4 headerColor, Vector4 bodyColor, float headerHeightPx,
        float radiusPx, Vector4 borderColor, float borderThicknessPx = 1.0f, float insetPx = 0.0f)
    {
        var globalScale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();
        var headerHeight = MathF.Max(0f, headerHeightPx * globalScale);
        var inset = insetPx * globalScale;
        var minX = windowMin.X + inset;
        var maxX = windowMax.X - inset;
        var minY = windowMin.Y + inset;
        var maxY = windowMax.Y - inset;

        if (maxX <= minX || maxY <= minY)
            return;

        headerHeight = MathF.Min(headerHeight, maxY - windowMin.Y);

        var rounding = MathF.Max(0f, radiusPx * globalScale - inset);
        var borderThickness = MathF.Max(0f, borderThicknessPx * globalScale);
        var borderHalfThickness = borderThickness * 0.5f;
        var antiAliasPadding = MathF.Min(0.5f * globalScale, borderHalfThickness);
        var fillInset = (borderThickness > 0f && borderColor.W > 0f) ? (borderHalfThickness + antiAliasPadding) : 0f;
        var fillMinX = minX + fillInset;
        var fillMaxX = maxX - fillInset;
        var fillMinY = minY + fillInset;
        var fillMaxY = maxY - fillInset;

        if (fillMaxX <= fillMinX || fillMaxY <= fillMinY)
            return;

        var fillRounding = MathF.Max(0f, rounding - fillInset);
        var headerMin = new Vector2(fillMinX, fillMinY);
        var headerMax = new Vector2(fillMaxX, windowMin.Y + headerHeight);
        headerMax.Y = MathF.Min(headerMax.Y, fillMaxY);

        if (headerMax.Y > headerMin.Y)
        {
            drawList.AddRectFilled(headerMin, headerMax, ImGui.GetColorU32(headerColor), fillRounding,
                ImDrawFlags.RoundCornersTopLeft | ImDrawFlags.RoundCornersTopRight);
        }

        var bodyMin = new Vector2(fillMinX, windowMin.Y + headerHeight);
        var bodyMax = new Vector2(fillMaxX, fillMaxY);

        if (bodyMax.Y > bodyMin.Y)
        {
            float bodyHeight = bodyMax.Y - bodyMin.Y;

            if (fillRounding <= 0f || bodyHeight <= fillRounding + 0.5f)
            {
                drawList.AddRectFilled(bodyMin, bodyMax, ImGui.GetColorU32(bodyColor), fillRounding,
                    ImDrawFlags.RoundCornersBottomLeft | ImDrawFlags.RoundCornersBottomRight);
            }
            else
            {
                float gradientBottomY = bodyMax.Y - fillRounding;
                float gradientHeight = MathF.Max(1f, gradientBottomY - bodyMin.Y);
                int stepCount = Math.Clamp((int)((gradientBottomY - bodyMin.Y) / UiScale.ScaledFloat(8f)), 12, 220);
                float stepHeight = (gradientBottomY - bodyMin.Y) / stepCount;

                static Vector4 Lerp(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

                for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
                {
                    float y0 = bodyMin.Y + stepHeight * stepIndex;
                    float y1 = (stepIndex == stepCount - 1) ? gradientBottomY : (y0 + stepHeight);

                    float tMid = ((y0 + y1) * 0.5f - bodyMin.Y) / gradientHeight;
                    var lerpedColor = Lerp(headerColor, bodyColor, Math.Clamp(tMid, 0f, 1f));

                    drawList.AddRectFilled(new Vector2(bodyMin.X, y0), new Vector2(bodyMax.X, y1), ImGui.GetColorU32(lerpedColor));
                }

                var seamOverlap = 1f;
                drawList.AddRectFilled(new Vector2(bodyMin.X, gradientBottomY - seamOverlap), bodyMax, ImGui.GetColorU32(bodyColor),
                    fillRounding, ImDrawFlags.RoundCornersBottomLeft | ImDrawFlags.RoundCornersBottomRight);
            }
        }

        if (borderThickness > 0f && borderColor.W > 0f)
        {
            var borderMin = new Vector2(minX + borderHalfThickness, minY + borderHalfThickness);
            var borderMax = new Vector2(maxX - borderHalfThickness, maxY - borderHalfThickness);

            if (borderMax.X > borderMin.X && borderMax.Y > borderMin.Y)
            {
                var borderRounding = MathF.Max(0f, rounding - borderHalfThickness);
                drawList.AddRect(borderMin, borderMax, ImGui.GetColorU32(borderColor), borderRounding, ImDrawFlags.RoundCornersAll, borderThickness);
            }
        }
    }

    public static void DrawAvatar(UiTheme theme, IDalamudTextureWrap? avatar, IDalamudTextureWrap? badge, Vector4 borderColor, Vector4 backgroundColor,
        out Vector2 nameMin, out Vector2 nameMax, float bannerHeightPx = 250f, float portraitAspectW = 9f, float portraitAspectH = 16f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var padding = UiScale.ScaledFloat(theme.PanelPad);
        var origin = ImGui.GetCursorScreenPos();
        var contentWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var bannerHeight = UiScale.ScaledFloat(bannerHeightPx);
        var innerMin = origin + new Vector2(padding, padding);
        var innerMax = origin + new Vector2(contentWidth - padding, bannerHeight - padding);
        var innerWidth = MathF.Max(1f, innerMax.X - innerMin.X);
        var innerHeight = MathF.Max(1f, innerMax.Y - innerMin.Y);
        var aspectRatio = portraitAspectW / portraitAspectH;
        var gap = UiScale.ScaledFloat(16f);
        var portraitHeight = innerHeight;
        var portraitWidth = portraitHeight * aspectRatio;
        var maxPortraitWidth = innerWidth * 0.52f;

        if (portraitWidth > maxPortraitWidth)
        {
            portraitWidth = maxPortraitWidth;
            portraitHeight = portraitWidth / aspectRatio;
        }

        var minPortraitWidth = UiScale.ScaledFloat(130f);
        if (portraitWidth < minPortraitWidth)
        {
            portraitWidth = Math.Min(minPortraitWidth, innerWidth);
            portraitHeight = Math.Min(innerHeight, portraitWidth / aspectRatio);
        }

        var imageMin = innerMin;
        var imageMax = imageMin + new Vector2(portraitWidth, portraitHeight);

        DrawPortraitAvatar(drawList, theme, avatar, imageMin, imageMax, borderColor, backgroundColor);

        if (badge != null)
            DrawCircularBadge(drawList, theme, badge, imageMin, imageMax);

        // Name rect to the right of the portrait
        nameMin = new Vector2(imageMax.X + gap, imageMin.Y);
        nameMax = new Vector2(innerMax.X, imageMin.Y + portraitHeight);
    }

    public static void DrawNameInfo(UiTheme theme, string displayName, string handle, ProfileV1 profile, bool online, Vector2 nameMin, Vector2 nameMax)
    {
        if (nameMax.X <= nameMin.X + UiScale.ScaledFloat(8f) || nameMax.Y <= nameMin.Y + UiScale.ScaledFloat(8f))
            return;

        var restoreCursor = ImGui.GetCursorPos();
        var nameWidth = Math.Max(1f, nameMax.X - nameMin.X);

        ImGui.SetCursorScreenPos(nameMin);
        ImGui.PushClipRect(nameMin, nameMax, true);

        var wrapStartLocalX = ImGui.GetCursorPosX();

        ImGui.PushTextWrapPos(wrapStartLocalX + nameWidth);
        UiText.ThemedText(theme, displayName, UiTextStyle.Heading, UiTheme.ToVec4(profile.Theme.TextPrimary));

        var pronouns = profile.Pronouns.Trim();
        var bulletGap = UiScale.ScaledFloat(6f);
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

                using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(UiScale.ScaledFloat(10f), UiScale.ScaledFloat(6f))))
                using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, UiScale.ScaledFloat(8f)))
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Copy UID");
                    ImGui.EndTooltip();
                }
            }

            if (!string.IsNullOrWhiteSpace(pronouns))
            {
                ImGui.SameLine(0, bulletGap);
                ImGui.TextUnformatted("•");
                ImGui.SameLine(0, bulletGap);
                ImGui.TextUnformatted(pronouns);
            }
        }

        var line3Y = ImGui.GetCursorScreenPos().Y;

        ImGui.SetCursorScreenPos(new Vector2(nameMin.X, line3Y));

        var statusLabel = profile.Status switch
        {
            ProfileStatus.NotShared => "Not Shared",
            ProfileStatus.NotInterested => "Not Interested",
            ProfileStatus.Taken => "Taken",
            ProfileStatus.Open => "Open",
            ProfileStatus.Looking => "Looking",
            ProfileStatus.ItsComplicated => "It's Complicated",
            ProfileStatus.AskMe => "Ask Me",
            _ => "Not Shared",
        };
        if (!string.IsNullOrWhiteSpace(statusLabel))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
            {
                ImGui.TextUnformatted("Relations");
            }

            var yAfterLabel = ImGui.GetCursorScreenPos().Y;

            ImGui.SetCursorScreenPos(new Vector2(nameMin.X, yAfterLabel));

            DrawPill("##profile_status", statusLabel!, bg: UiTheme.ToVec4(profile.Theme.Accent),
                border: UiTheme.ToVec4(profile.Theme.Secondary), fg: UiTheme.ToVec4(profile.Theme.TextPrimary));
        }

        ImGui.PopTextWrapPos();
        ImGui.PopClipRect();
        ImGui.SetCursorPos(restoreCursor);
    }

    public static void DrawInterests(UiTheme theme, ProfileV1 profile)
    {
        var interestList = profile.Interests.Where(interest => !string.IsNullOrWhiteSpace(interest)).Select(interest => interest.Trim()).ToList();

        if (interestList is not { Count: > 0 })
            return;

        using (PushContentInset(theme, out var innerWidth))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
            {
                ImGui.TextUnformatted("Interests");
            }

            DrawPillWrap(theme, "interest", interestList, bg: UiTheme.ToVec4(profile.Theme.Accent), border: UiTheme.ToVec4(profile.Theme.Secondary),
                fg: UiTheme.ToVec4(profile.Theme.TextPrimary), innerWidth);
        }

    }

    public static void DrawAboutMe(UiTheme theme, ProfileV1 profile)
    {
        ImGui.Dummy(new Vector2(1f, UiScale.ScaledFloat(Spacing)));

        var about = profile.AboutMe?.Trim();
        if (string.IsNullOrWhiteSpace(about))
            return;

        using (PushContentInset(theme, out var innerWidth))
        {
            UiText.DrawTextWrappedMaxLines(about, width: innerWidth, maxLines: 4, color: UiTheme.ToVec4(profile.Theme.TextPrimary), ellipsisColor: theme.TextMuted);
        }
    }

    public static bool DrawNotes(UiTheme theme, ProfileV1 profile, ref string notesDraft, ref bool editing, string id,
        string heading = "Note (only visible to you)", string placeholder = "Click to add a note", int maxLen = 200, int lines = 10)
    {
        ImGui.Dummy(new Vector2(1f, UiScale.ScaledFloat(Spacing)));

        var drawList = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();

        using (PushContentInset(theme, out var innerWidth))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiTheme.ToVec4(profile.Theme.TextPrimary)))
            {
                ImGui.TextUnformatted(heading);
            }

            var lineHeight = ImGui.GetTextLineHeightWithSpacing();
            var height = (lineHeight * lines) + (style.FramePadding.Y * 2f);
            var boxSize = new Vector2(innerWidth, height);
            var boxMin = ImGui.GetCursorScreenPos();
            var boxMax = boxMin + boxSize;
            var rounding = style.FrameRounding;
            var background = UiTheme.ToVec4(profile.Theme.Accent);
            var border = UiTheme.ToVec4(profile.Theme.Secondary);
            var hovered = ImGui.IsMouseHoveringRect(boxMin, boxMax, true);
            var storage = ImGui.GetStateStorage();
            var focusKey = ImGui.GetID($"{id}##notes_focus_next");
            bool focusNext = storage.GetInt(focusKey, 0) != 0;
            bool changed = false;

            if (editing)
                drawList.AddRectFilled(boxMin, boxMax, ImGui.GetColorU32(background), rounding);

            if (hovered || editing)
                drawList.AddRect(boxMin, boxMax, ImGui.GetColorU32(border), rounding);

            if (!editing)
            {
                ImGui.SetCursorScreenPos(boxMin);
                if (ImGui.InvisibleButton($"{id}##hit", boxSize))
                {
                    editing = true;
                    storage.SetInt(focusKey, 1);
                }

                var framePadding = style.FramePadding;
                var textMin = boxMin + framePadding;
                var textMax = boxMax - framePadding;

                drawList.PushClipRect(textMin, textMax, true);
                try
                {
                    ImGui.SetCursorScreenPos(textMin);

                    var wrapLocalX = ImGui.GetCursorPosX() + MathF.Max(1f, textMax.X - textMin.X);

                    ImGui.PushTextWrapPos(wrapLocalX);

                    if (string.IsNullOrWhiteSpace(notesDraft))
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, UiText.GetMutedTextColor(UiTheme.ToVec4(profile.Theme.TextPrimary))))
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
                    drawList.PopClipRect();
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

            ImGui.Dummy(new Vector2(1f, UiScale.ScaledFloat(8f)));
            return changed;
        }
    }

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

    private static void DrawPortraitAvatar(ImDrawListPtr drawList, UiTheme theme, IDalamudTextureWrap? avatar,
        Vector2 slotMin, Vector2 slotMax, Vector4 borderColor, Vector4 backgroundColor)
    {
        var size = slotMax - slotMin;
        if (size.X <= 1f || size.Y <= 1f)
            return;

        var rounding = UiScale.ScaledFloat(theme.RadiusLg);
        var borderThickness = UiScale.ScaledFloat(3f);

        drawList.AddRectFilled(slotMin, slotMax, ImGui.GetColorU32(backgroundColor), rounding);

        if (avatar != null)
        {
            var destAspect = size.X / size.Y;
            CalcUvCropToAspect(avatar, destAspect, out var uv0, out var uv1);

            drawList.AddImageRounded(avatar.Handle, slotMin, slotMax, uv0, uv1, 0xFFFFFFFF, rounding, ImDrawFlags.RoundCornersAll
            );
        }

        drawList.AddRect(slotMin, slotMax, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.RoundCornersAll, borderThickness);
    }

    private static void DrawCircularBadge(ImDrawListPtr drawList, UiTheme theme, IDalamudTextureWrap badge, Vector2 avatarMin, Vector2 avatarMax)
    {
        var badgeSize = UiScale.ScaledFloat(28f);
        var badgeMin = new Vector2(avatarMax.X - badgeSize, avatarMax.Y - badgeSize);
        var badgeMax = badgeMin + new Vector2(badgeSize, badgeSize);
        var radius = badgeSize * 0.5f;
        var center = (badgeMin + badgeMax) * 0.5f;

        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(theme.PanelBg));

        drawList.AddImageRounded(badge.Handle, badgeMin, badgeMax, new Vector2(0, 0), new Vector2(1, 1), 0xFFFFFFFF, radius, ImDrawFlags.RoundCornersAll
        );

        var borderThickness = UiScale.ScaledFloat(1.5f);

        drawList.AddCircle(center, radius - (borderThickness * 0.5f), ImGui.GetColorU32(theme.Border), 0, borderThickness);
    }

    private static Vector2 CalcPillSize(string text)
    {
        var padX = UiScale.ScaledFloat(10f);
        var padY = UiScale.ScaledFloat(5f);
        var ts = ImGui.CalcTextSize(text);
        return new Vector2(ts.X + padX * 2f, ts.Y + padY * 2f);
    }

    private static void DrawPill(string id, string text, Vector4 bg, Vector4 border, Vector4 fg)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = UiScale.ScaledFloat(10f);
        var padY = UiScale.ScaledFloat(5f);
        var rounding = UiScale.ScaledFloat(999f); // "pill"
        var ts = ImGui.CalcTextSize(text);
        var size = new Vector2(ts.X + padX * 2f, ts.Y + padY * 2f);

        ImGui.InvisibleButton(id, size);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        dl.AddRectFilled(min, max, ImGui.GetColorU32(bg), rounding);
        dl.AddText(min + new Vector2(padX, padY), ImGui.GetColorU32(fg), text);
    }

    private static void DrawPillWrap(UiTheme theme, string idPrefix, IReadOnlyList<string> items, Vector4 bg, Vector4 border, Vector4 fg, float width)
    {
        var gap = UiScale.ScaledFloat(8f);
        var style = ImGui.GetStyle();

        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing,
            new Vector2(style.ItemSpacing.X, gap));

        var start = ImGui.GetCursorScreenPos();
        var winRight = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
        var pad = UiScale.ScaledFloat(theme.PanelPad);
        var maxX = MathF.Min(start.X + MathF.Max(1f, width), winRight - pad) - 1f;
        bool isFirst = true;

        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var text = items[itemIndex]?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var pillSize = CalcPillSize(text);

            if (!isFirst)
            {
                var prevMaxX = ImGui.GetItemRectMax().X;
                var nextStartX = prevMaxX + gap;

                if (nextStartX + pillSize.X <= maxX)
                    ImGui.SameLine(0, gap);
            }

            DrawPill($"##{idPrefix}_{itemIndex}", text, bg: bg, border: border, fg: fg);

            isFirst = false;
        }
    }

    private readonly struct Scope : IDisposable
    {
        private readonly Action _end;
        public Scope(Action end) => _end = end;
        public void Dispose() => _end();
    }

    private static IDisposable PushContentInset(UiTheme theme, out float innerWidth)
    {
        var pad = UiScale.ScaledFloat(theme.PanelPad);

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
