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
    public static void DrawBackgroundWindow(Vector4 color, float roundingOverridePx = -1f, float insetPx = 1.0f)
    {
        // ensure solid color
        color.W = 1f;
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

    public static void DrawGradient(Vector4 topColor, Vector4 bottomColor)
    {
        var globalScale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        var bottomOffset = 45f * globalScale;
        var startY = windowPos.Y + (windowSize.Y * 0.60f);
        var endY = windowPos.Y + windowSize.Y - bottomOffset;
        var bottomY = windowPos.Y + windowSize.Y;
        var leftX = windowPos.X;
        var rightX = windowPos.X + windowSize.X;
        var inset = 3f * globalScale;

        var fillMin = new Vector2(leftX + inset, endY + inset);
        var fillMax = new Vector2(rightX - inset, bottomY - 2.5f * globalScale);

        var min = new Vector2(windowPos.X + inset, startY - inset);
        var max = new Vector2(windowPos.X + windowSize.X - inset, endY + inset);

        topColor.W = 0.01f; // top fade out
        bottomColor.W = 1.0f; // bottom solid
        var topColorUint = ImGui.GetColorU32(topColor);
        var bottomColorUint = ImGui.GetColorU32(bottomColor);
        var roundPx = (24f * globalScale) - inset;

        // bottom solid fill
        drawList.AddRectFilled(fillMin, fillMax, bottomColorUint, roundPx, ImDrawFlags.RoundCornersBottom);

        // gradient fill
        drawList.AddRectFilledMultiColor(min, max, topColorUint, topColorUint, bottomColorUint, bottomColorUint);
    }

    public static void DrawWindowBorder(Vector4 borderColor, float radiusPx, float borderThicknessPx = 1.0f, float insetPx = 0.0f)
    {
        // ensure solid color
        borderColor.W = 1f;
        var globalScale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();

        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();

        var inset = insetPx * globalScale;
        var minX = windowMin.X + inset;
        var maxX = windowMax.X - inset;
        var minY = windowMin.Y + inset;
        var maxY = windowMax.Y - inset;

        if (maxX <= minX || maxY <= minY)
            return;

        var borderThickness = MathF.Max(0f, borderThicknessPx * globalScale);
        var borderHalfThickness = borderThickness * 0.5f;

        var borderMin = new Vector2(minX + borderHalfThickness, minY + borderHalfThickness);
        var borderMax = new Vector2(maxX - borderHalfThickness, maxY - borderHalfThickness);

        if (borderMax.X <= borderMin.X || borderMax.Y <= borderMin.Y)
            return;

        var rounding = MathF.Max(0f, radiusPx * globalScale - inset);
        var borderRounding = MathF.Max(0f, rounding - borderHalfThickness);

        drawList.AddRect(borderMin, borderMax, ImGui.GetColorU32(borderColor), borderRounding, ImDrawFlags.RoundCornersAll, borderThickness);
    }

    public static void DrawBackgroundImage(UiTheme theme, IDalamudTextureWrap? avatar, float portraitAspectW = 9f, float portraitAspectH = 16f, float insetPx = 3f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var contentMin = windowPos + ImGui.GetWindowContentRegionMin();
        var contentMax = windowPos + ImGui.GetWindowContentRegionMax();
        var contentSize = contentMax - contentMin;
        var aspectRatio = portraitAspectW / portraitAspectH;
        var targetWidth = MathF.Max(1f, contentSize.X);
        var targetHeight = MathF.Max(1f, targetWidth / aspectRatio);

        if (targetHeight > contentSize.Y)
        {
            targetHeight = MathF.Max(1f, contentSize.Y);
            targetWidth = MathF.Max(1f, targetHeight * aspectRatio);
        }

        var imageMin = contentMin + (contentSize - new Vector2(targetWidth, targetHeight)) * 0.5f;
        var imageMax = imageMin + new Vector2(targetWidth, targetHeight);

        // don't run picture to window edge else we get pixel issues
        var inset = UiScale.ScaledFloat(insetPx);
        imageMin += new Vector2(inset, inset);
        imageMax -= new Vector2(inset, inset);

        var rounding = UiScale.ScaledFloat(24f) - 3f * ImGuiHelpers.GlobalScale - inset;

        if (avatar != null)
            drawList.AddImageRounded(avatar.Handle, imageMin, imageMax, new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFF, rounding);
    }

    public static void DrawNameInfo(UiTheme theme, string displayName, string handle, ProfileV1 profile, bool online = true)
    {
        var pad = UiScale.ScaledFloat(theme.PanelPad);

        ImGui.SetCursorPos(new(pad, pad));
        UiText.ThemedText(theme, displayName, UiTextStyle.Heading, profile.Theme.TextPrimaryV4);

        var pronouns = profile.Pronouns.Trim();
        var bulletGap = UiScale.ScaledFloat(6f);

        using (ImRaii.PushColor(ImGuiCol.Text, profile.Theme.TextPrimaryV4))
        {
            ImGui.SetCursorPosX(pad);
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

        if (!string.IsNullOrWhiteSpace(StatusLabel(profile.Status)) && profile.Status != ProfileStatus.NotShared)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, profile.Theme.TextPrimaryV4))
            {
                ImGui.SetCursorPosX(pad);
                ImGui.TextUnformatted("Relationship Status");
            }

            ImGui.SetCursorPosX(pad);
            DrawPill("##profile_status", StatusLabel(profile.Status), bg: profile.Theme.AccentV4, border: profile.Theme.SecondaryV4, fg: profile.Theme.TextPrimaryV4);
        }
    }

    public static float CalculateGapHeight(UiTheme theme, ProfileV1 profile)
    {
        var windowPos = ImGui.GetWindowPos();
        var contentMin = windowPos + ImGui.GetWindowContentRegionMin();
        var contentMax = windowPos + ImGui.GetWindowContentRegionMax();
        var contentSize = contentMax - contentMin;
        var interestsHeight = MeasureInterestsHeight(theme, profile);
        var aboutMeHeight = MeasureAboutHeight(theme, profile);

        var calculated = contentSize.Y - interestsHeight - aboutMeHeight - 150 * ImGuiHelpers.GlobalScale;

        return calculated;
    }

    public static string StatusLabel(ProfileStatus status) => status switch
    {
        ProfileStatus.NotShared => "Not Shared",
        ProfileStatus.NotInterested => "Not Interested",
        ProfileStatus.Taken => "Taken",
        ProfileStatus.Open => "Open",
        ProfileStatus.Looking => "Looking",
        ProfileStatus.ItsComplicated => "It's Complicated",
        ProfileStatus.AskMe => "Ask Me",
        _ => status.ToString(),
    };

    public static void DrawInterests(UiTheme theme, ProfileV1 profile)
    {
        var interestList = profile.Interests.Where(interest => !string.IsNullOrWhiteSpace(interest)).Select(interest => interest.Trim()).ToList();

        if (interestList.Count == 0)
            return;

        using (PushContentInset(theme, out var innerWidth))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, profile.Theme.TextPrimaryV4))
            {
                ImGui.TextUnformatted("Interests");
            }

            DrawPillWrap(theme, "interest", interestList, bg: profile.Theme.AccentV4, border: profile.Theme.SecondaryV4, fg: profile.Theme.TextPrimaryV4, innerWidth);
        }

    }

    public static void DrawAboutMe(UiTheme theme, ProfileV1 profile)
    {
        ImGui.Dummy(new Vector2(1f, UiScale.ScaledFloat(Spacing/4)));

        var about = profile.AboutMe?.Trim();
        if (string.IsNullOrWhiteSpace(about))
            return;

        using (PushContentInset(theme, out var innerWidth))
        {
            UiText.DrawTextWrappedMaxLines(about, width: innerWidth, maxLines: 5, color: profile.Theme.TextPrimaryV4, ellipsisColor: theme.TextMuted);
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
            using (ImRaii.PushColor(ImGuiCol.Text, profile.Theme.TextPrimaryV4))
            {
                ImGui.TextUnformatted(heading);
            }

            var lineHeight = ImGui.GetTextLineHeightWithSpacing();
            var height = (lineHeight * lines) + (style.FramePadding.Y * 2f);
            var boxSize = new Vector2(innerWidth, height);
            var boxMin = ImGui.GetCursorScreenPos();
            var boxMax = boxMin + boxSize;
            var rounding = style.FrameRounding;
            var background = profile.Theme.AccentV4;
            var border = profile.Theme.SecondaryV4;
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
                        using (ImRaii.PushColor(ImGuiCol.Text, UiText.GetMutedTextColor(profile.Theme.TextPrimaryV4)))
                            ImGui.TextWrapped(placeholder);
                    }
                    else
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, profile.Theme.TextPrimaryV4))
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

                using (ImRaii.PushColor(ImGuiCol.Text, profile.Theme.TextPrimaryV4))
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

    private static float MeasurePillWrapHeight(UiTheme theme, IReadOnlyList<string> items)
    {
        var gap = UiScale.ScaledFloat(8f);
        var padX = UiScale.ScaledFloat(10f);
        var padY = UiScale.ScaledFloat(5f);

        var windowPos = ImGui.GetWindowPos();
        var contentMin = windowPos + ImGui.GetWindowContentRegionMin();
        var contentMax = windowPos + ImGui.GetWindowContentRegionMax();

        var pad = UiScale.ScaledFloat(theme.PanelPad);
        var leftX = contentMin.X + pad;
        var rightX = contentMax.X - pad;

        var maxX = rightX - 1f;

        int rowCount = 0;
        float currentRowRightX = leftX;

        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var text = items[itemIndex]?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var textSize = ImGui.CalcTextSize(text);
            var pillWidth = textSize.X + padX * 2f;

            if (rowCount == 0)
            {
                rowCount = 1;
                currentRowRightX = leftX + pillWidth;
                continue;
            }

            var nextStartX = currentRowRightX + gap;

            if (nextStartX + pillWidth <= maxX)
            {
                currentRowRightX = nextStartX + pillWidth;
            }
            else
            {
                rowCount++;
                currentRowRightX = leftX + pillWidth;
            }
        }

        if (rowCount == 0)
            return 0f;

        var pillHeight = ImGui.GetTextLineHeight() + padY * 2f;
        return (rowCount * pillHeight) + ((rowCount - 1) * gap);
    }



    private static float MeasureInterestsHeight(UiTheme theme, ProfileV1 profile)
    {
        float measuredHeight;

        var interestList = profile.Interests
            .Where(interest => !string.IsNullOrWhiteSpace(interest))
            .Select(interest => interest.Trim())
            .ToList();

        if (interestList.Count == 0)
        {
            measuredHeight = 0f;
        }
        else
        {
            var headerHeight = ImGui.GetTextLineHeightWithSpacing();
            var pillsHeight = MeasurePillWrapHeight(theme, interestList);
            measuredHeight = headerHeight + pillsHeight;
        }

        return measuredHeight;
    }

    private static float MeasureAboutHeight(UiTheme theme, ProfileV1 profile, int maxLines = 5)
    {
        float measuredValue;

        var about = profile.AboutMe?.Trim();

        if (string.IsNullOrWhiteSpace(about))
        {
            measuredValue = 0f;
        }
        else
        {
            var windowPos = ImGui.GetWindowPos();
            var contentMin = windowPos + ImGui.GetWindowContentRegionMin();
            var contentMax = windowPos + ImGui.GetWindowContentRegionMax();

            var pad = UiScale.ScaledFloat(theme.PanelPad);
            var wrapWidth = MathF.Max(1f, (contentMax.X - contentMin.X) - (pad * 2f));
            var textSize = ImGui.CalcTextSize(about, false, wrapWidth);

            var lineHeight = ImGui.GetTextLineHeight();
            var lineSpacing = ImGui.GetStyle().ItemSpacing.Y;

            var estimatedLines = Math.Max(1, (int)MathF.Ceiling(textSize.Y / MathF.Max(1f, lineHeight)));
            var lines = Math.Clamp(estimatedLines, 1, maxLines);

            measuredValue = (lines * lineHeight) + ((lines - 1) * lineSpacing);
        }

        return measuredValue;
    }
}
