using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;

namespace MareSynchronos.UI.ModernUi;

/// <summary>
/// Sidebar nav + custom tabs.
/// Modern web-style, kinda of like Bootstrap
/// </summary>
public static class UiNav
{
    /// <summary>
    /// NavItem contains everything to display and control actions for sidebars
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <param name="Id"></param>
    /// <param name="Label"></param>
    /// <param name="NavAction"></param>
    /// <param name="Icon"></param>
    /// <param name="Enabled"></param>
    public sealed record NavItem<TId>(TId Id, string Label, Action NavAction, FontAwesomeIcon? Icon = null, bool Enabled = true) where TId : struct, Enum;

    /// <summary>
    /// Returns a navitem which can be used for logic/display and passed in next draw
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <param name="t"></param>
    /// <param name="title"></param>
    /// <param name="groups"></param>
    /// <param name="selectedNavItem"></param>
    /// <param name="widthPx"></param>
    /// <param name="iconFont"></param>
    /// <returns></returns>
    public static NavItem<TId> DrawSidebar<TId>(UiTheme theme, string title, IReadOnlyList<(string GroupLabel, IReadOnlyList<NavItem<TId>> Items)> groups,
        NavItem<TId>? selectedNavItem, float widthPx = 210f, IFontHandle? iconFont = null) where TId : struct, Enum
    {
        var width = UiScale.ScaledFloat(widthPx);
        var padding = UiScale.ScaledFloat(10f);

        if (selectedNavItem == null)
        {
            selectedNavItem = groups[0].Items[0];
        }

        using var child = ImRaii.Child("##sidebar", new Vector2(width, 0), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        if (!child)
            return selectedNavItem;

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding)))
        {
            // layout constants
            var rowHeight = UiScale.ScaledFloat(34f);
            var iconPaddingLeft = UiScale.ScaledFloat(12f);
            var iconBoxWidth = UiScale.ScaledFloat(18f);
            var iconLabelGap = UiScale.ScaledFloat(10f);

            foreach (var (groupLabel, items) in groups)
            {
                if (!string.IsNullOrWhiteSpace(groupLabel))
                    Ui.DrawSectionHeader(theme, groupLabel, addHr: false);

                foreach (var item in items)
                {
                    var isSelected = Convert.ToInt32(item.Id) == Convert.ToInt32(selectedNavItem.Id);

                    using var disabled = ImRaii.Disabled(!item.Enabled);
                    using var id = ImRaii.PushId(Convert.ToInt32(item.Id));

                    var buttonColor = isSelected ? new Vector4(theme.Primary.X, theme.Primary.Y, theme.Primary.Z, 0.20f) : new Vector4(1, 1, 1, 0.00f);
                    var buttonHoverColor = isSelected ? new Vector4(theme.Primary.X, theme.Primary.Y, theme.Primary.Z, 0.28f) : theme.HoverOverlay;
                    var buttonActiveColor = isSelected ? new Vector4(theme.Primary.X, theme.Primary.Y, theme.Primary.Z, 0.34f) : theme.ActiveOverlay;

                    using var buttonColorScoped = ImRaii.PushColor(ImGuiCol.Button, buttonColor);
                    using var buttonHoverColorScoped = ImRaii.PushColor(ImGuiCol.ButtonHovered, buttonHoverColor);
                    using var buttonActiveColorScoped = ImRaii.PushColor(ImGuiCol.ButtonActive, buttonActiveColor);

                    using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, UiScale.ScaledFloat(theme.RadiusSm));
                    using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(UiScale.ScaledFloat(10), UiScale.ScaledFloat(8)));

                    if (ImGui.Button("##nav", new Vector2(-1, rowHeight)) && item.Enabled)
                        selectedNavItem = item;

                    var itemMin = ImGui.GetItemRectMin();
                    var itemMax = ImGui.GetItemRectMax();
                    var itemCenterY = (itemMin.Y + itemMax.Y) * 0.5f;
                    var drawList = ImGui.GetWindowDrawList();

                    if (isSelected)
                    {
                        var selectionBarWidth = UiScale.ScaledFloat(3f);
                        var selectionFillColor = theme.Primary;
                        selectionFillColor.W = 0.90f;
                        drawList.AddRectFilled(new Vector2(itemMin.X, itemMin.Y), new Vector2(itemMin.X + selectionBarWidth, itemMax.Y), ImGui.GetColorU32(selectionFillColor));
                    }

                    var textColor = ImGui.GetColorU32(theme.Text);
                    var cursorX = itemMin.X + iconPaddingLeft;

                    if (item.Icon.HasValue)
                    {
                        var iconText = item.Icon.Value.ToIconString();
                        Vector2 iconTextSize;

                        if (iconFont != null)
                        {
                            using (iconFont.Push())
                                iconTextSize = ImGui.CalcTextSize(iconText);

                            var iconPosition = new Vector2(cursorX, itemCenterY - (iconTextSize.Y * 0.5f));
                            using (iconFont.Push())
                                drawList.AddText(iconPosition, textColor, iconText);
                        }
                        else
                        {
                            iconTextSize = ImGui.CalcTextSize(iconText);
                            var iconPosition = new Vector2(cursorX, itemCenterY - (iconTextSize.Y * 0.5f));
                            drawList.AddText(iconPosition, textColor, iconText);
                        }
                        cursorX += iconBoxWidth + iconLabelGap;
                    }
                    var labelTextSize = ImGui.CalcTextSize(item.Label);
                    var labelPosition = new Vector2(cursorX, itemCenterY - (labelTextSize.Y * 0.5f));
                    drawList.AddText(labelPosition, textColor, item.Label);
                }
                Ui.AddVerticalSpace(8);
            }
        }

        return selectedNavItem;
    }

    /// <summary>
    /// Tabs contain everything to control a horizontal tab navbar
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <param name="Id"></param>
    /// <param name="Label"></param>
    /// <param name="TabAction"></param>
    /// <param name="Icon"></param>
    /// <param name="Enabled"></param>
    public sealed record Tab<TId>(TId Id, string Label, Action TabAction, FontAwesomeIcon? Icon = null, bool Enabled = true) where TId : struct, Enum;

    /// <summary>
    /// Returns a tab item which can be used for logic/display or passed in next draw
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <param name="t"></param>
    /// <param name="tabs"></param>
    /// <param name="selectedTab"></param>
    /// <param name="iconFont"></param>
    /// <returns></returns>
    public static Tab<TId> DrawTabsUnderline<TId>(UiTheme theme, IReadOnlyList<Tab<TId>> tabs, Tab<TId>? selectedTab, IFontHandle? iconFont = null) where TId : struct, Enum
    {
        if (selectedTab == null)
        {
            selectedTab = tabs[0];
        }

        var drawList = ImGui.GetWindowDrawList();

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(UiScale.ScaledFloat(10), UiScale.ScaledFloat(1)));
        using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(UiScale.ScaledFloat(6), UiScale.ScaledFloat(6)));

        for (var tabIndex = 0; tabIndex < tabs.Count; tabIndex++)
        {
            var tab = tabs[tabIndex];
            var isSelected = tab == selectedTab;

            using var disabled = ImRaii.Disabled(!tab.Enabled);
            using var id = ImRaii.PushId(Convert.ToInt32(tab.Id));

            using var buttonColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(1, 1, 1, 0));
            using var buttonHoverColor = ImRaii.PushColor(ImGuiCol.ButtonHovered, theme.HoverOverlay);
            using var buttonActiveColor = ImRaii.PushColor(ImGuiCol.ButtonActive, theme.ActiveOverlay);

            if (tab.Icon.HasValue && iconFont != null)
            {
                var iconText = tab.Icon.Value.ToIconString();

                Vector2 iconTextSize;
                using (iconFont.Push())
                    iconTextSize = ImGui.CalcTextSize(iconText);

                var labelTextSize = ImGui.CalcTextSize(tab.Label);
                var iconLabelGap = UiScale.ScaledFloat(8f);
                var totalWidth = iconTextSize.X + iconLabelGap + labelTextSize.X + (ImGui.GetStyle().FramePadding.X * 2);

                var wasClicked = ImGui.Button("##tab", new Vector2(totalWidth, 0));
                if (wasClicked && tab.Enabled)
                    selectedTab = tab;

                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();
                var itemCenterY = (itemMin.Y + itemMax.Y) * 0.5f;

                // can change this but I didn't like the text color changing, didn't remove so it's not lost
                var textColor = ImGui.GetColorU32(isSelected ? theme.Text : theme.Text);

                var iconPos = new Vector2(itemMin.X + ImGui.GetStyle().FramePadding.X, itemCenterY - (iconTextSize.Y * 0.5f));
                using (iconFont.Push())
                    drawList.AddText(iconPos, textColor, iconText);

                var textPos = new Vector2(iconPos.X + iconTextSize.X + iconLabelGap, itemCenterY - (labelTextSize.Y * 0.5f));
                drawList.AddText(textPos, textColor, tab.Label);

                if (isSelected)
                {
                    var underlineY = itemMax.Y + UiScale.ScaledFloat(2);
                    drawList.AddLine(new Vector2(itemMin.X, underlineY), new Vector2(itemMax.X, underlineY), ImGui.GetColorU32(theme.Primary), UiScale.ScaledFloat(2));
                }
            }
            else
            {
                var label = tab.Label;
                if (tab.Icon.HasValue)
                    label = $"{tab.Icon.Value.ToIconString()}  {label}";

                // can change this but I didn't like the text color changing, didn't remove so it's not lost
                using var textColor = ImRaii.PushColor(ImGuiCol.Text, isSelected ? theme.Text : theme.Text);

                var wasClicked = ImGui.Button(label);
                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();

                if (isSelected)
                {
                    var underlineY = itemMax.Y + UiScale.ScaledFloat(2);
                    drawList.AddLine(new Vector2(itemMin.X, underlineY), new Vector2(itemMax.X, underlineY), ImGui.GetColorU32(theme.Primary), UiScale.ScaledFloat(2));
                }

                if (wasClicked && tab.Enabled)
                    selectedTab = tab;
            }

            if (tabIndex != tabs.Count - 1)
                ImGui.SameLine();
        }
        ImGui.NewLine();

        return selectedTab;
    }
}
