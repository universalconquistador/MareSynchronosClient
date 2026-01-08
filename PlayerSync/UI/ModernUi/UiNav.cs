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
    private static Vector4 WithAlpha(Vector4 v, float a) => new(v.X, v.Y, v.Z, a);

    /// <summary>
    /// NavItem contains everything to display and control actions for sidebars
    /// </summary>
    /// <param name="Id"></param>
    /// <param name="Label"></param>
    /// <param name="NavAction"></param>
    /// <param name="Icon"></param>
    /// <param name="Enabled"></param>
    public sealed record NavItem(string Id, string Label, Action NavAction, FontAwesomeIcon? Icon = null, bool Enabled = true);

    /// <summary>
    /// Returns a navitem which can be used for logic/display and passed in next draw
    /// </summary>
    /// <param name="t"></param>
    /// <param name="title"></param>
    /// <param name="groups"></param>
    /// <param name="selectedNavItem"></param>
    /// <param name="widthPx"></param>
    /// <param name="iconFont"></param>
    /// <returns></returns>
    public static NavItem DrawSidebar(UiTheme t, string title, IReadOnlyList<(string GroupLabel, IReadOnlyList<NavItem> Items)> groups, NavItem? selectedNavItem, float widthPx = 210f, IFontHandle? iconFont = null)
    {
        var width = UiScale.S(widthPx);
        var pad = UiScale.S(10f);

        if (selectedNavItem == null)
        {
            selectedNavItem = groups[0].Items[0];
        }

        using var child = ImRaii.Child("##sidebar", new Vector2(width, 0), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        if (!child)
            return selectedNavItem;

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad)))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, t.Text))
                ImGui.TextUnformatted(title);

            Ui.VSpace(6);
            Ui.Hr(t);

            // layout constants
            var rowH = UiScale.S(34f);
            var iconPadL = UiScale.S(12f);
            var iconBoxW = UiScale.S(18f);
            var gap = UiScale.S(10f);

            foreach (var (groupLabel, items) in groups)
            {
                if (!string.IsNullOrWhiteSpace(groupLabel))
                    Ui.SectionHeader(t, groupLabel, addHr: false);

                foreach (var item in items)
                {
                    var isSelected = item.Id == selectedNavItem.Id;

                    using var disabled = ImRaii.Disabled(!item.Enabled);
                    using var id = ImRaii.PushId(item.Id);

                    var bg = isSelected ? new Vector4(t.Primary.X, t.Primary.Y, t.Primary.Z, 0.20f) : new Vector4(1, 1, 1, 0.00f);
                    var hov = isSelected ? new Vector4(t.Primary.X, t.Primary.Y, t.Primary.Z, 0.28f) : t.HoverOverlay;
                    var act = isSelected ? new Vector4(t.Primary.X, t.Primary.Y, t.Primary.Z, 0.34f) : t.ActiveOverlay;

                    using var c1 = ImRaii.PushColor(ImGuiCol.Button, bg);
                    using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, hov);
                    using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive, act);

                    using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, UiScale.S(t.RadiusSm));
                    using var fp = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(UiScale.S(10), UiScale.S(8)));

                    if (ImGui.Button("##nav", new Vector2(-1, rowH)) && item.Enabled)
                        selectedNavItem = item;

                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    var centerY = (min.Y + max.Y) * 0.5f;
                    var dl = ImGui.GetWindowDrawList();

                    if (isSelected)
                    {
                        var barW = UiScale.S(3f);
                        dl.AddRectFilled(new Vector2(min.X, min.Y), new Vector2(min.X + barW, max.Y), ImGui.GetColorU32(WithAlpha(t.Primary, 0.90f)));
                    }

                    var col = ImGui.GetColorU32(t.Text);
                    var x = min.X + iconPadL;

                    if (item.Icon.HasValue)
                    {
                        var iconStr = item.Icon.Value.ToIconString();
                        Vector2 iconSize;

                        if (iconFont != null)
                        {
                            using (iconFont.Push())
                                iconSize = ImGui.CalcTextSize(iconStr);

                            var iconPos = new Vector2(x, centerY - (iconSize.Y * 0.5f));
                            using (iconFont.Push())
                                dl.AddText(iconPos, col, iconStr);
                        }
                        else
                        {
                            iconSize = ImGui.CalcTextSize(iconStr);
                            var iconPos = new Vector2(x, centerY - (iconSize.Y * 0.5f));
                            dl.AddText(iconPos, col, iconStr);
                        }
                        x += iconBoxW + gap;
                    }
                    var labelSize = ImGui.CalcTextSize(item.Label);
                    var labelPos = new Vector2(x, centerY - (labelSize.Y * 0.5f));
                    dl.AddText(labelPos, col, item.Label);
                }
                Ui.VSpace(8);
            }
        }

        return selectedNavItem;
    }

    /// <summary>
    /// Tabs contain everything to control a horizontal tab navbar
    /// </summary>
    /// <param name="Id"></param>
    /// <param name="Label"></param>
    /// <param name="TabAction"></param>
    /// <param name="Icon"></param>
    /// <param name="Enabled"></param>
    public sealed record Tab(string Id, string Label, Action TabAction, FontAwesomeIcon? Icon = null, bool Enabled = true);

    /// <summary>
    /// Returns a tab item which can be used for logic/display or passed in next draw
    /// </summary>
    /// <param name="t"></param>
    /// <param name="tabs"></param>
    /// <param name="selectedTab"></param>
    /// <param name="iconFont"></param>
    /// <returns></returns>
    public static Tab DrawTabsUnderline(UiTheme t, IReadOnlyList<Tab> tabs, Tab? selectedTab, IFontHandle? iconFont = null)
    {
        if (selectedTab == null)
        {
            selectedTab = tabs[0];
        }

        var dl = ImGui.GetWindowDrawList();

        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(UiScale.S(10), UiScale.S(1)));
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(UiScale.S(6), UiScale.S(6)));

        for (var i = 0; i < tabs.Count; i++)
        { 
            var tab = tabs[i];
            var isSel = tab == selectedTab;

            using var disabled = ImRaii.Disabled(!tab.Enabled);
            using var id = ImRaii.PushId(tab.Id);

            using var c = ImRaii.PushColor(ImGuiCol.Button, new Vector4(1, 1, 1, 0));
            using var ch = ImRaii.PushColor(ImGuiCol.ButtonHovered, t.HoverOverlay);
            using var ca = ImRaii.PushColor(ImGuiCol.ButtonActive, t.ActiveOverlay);

            if (tab.Icon.HasValue && iconFont != null)
            {
                var iconStr = tab.Icon.Value.ToIconString();

                Vector2 iconSize;
                using (iconFont.Push())
                    iconSize = ImGui.CalcTextSize(iconStr);

                var labelSize = ImGui.CalcTextSize(tab.Label);
                var gap = UiScale.S(8f);
                var totalW = iconSize.X + gap + labelSize.X + (ImGui.GetStyle().FramePadding.X * 2);

                var clicked = ImGui.Button("##tab", new Vector2(totalW, 0));
                if (clicked && tab.Enabled)
                    selectedTab = tab;

                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var centerY = (min.Y + max.Y) * 0.5f;

                var col = ImGui.GetColorU32(isSel ? t.Text : t.Text);

                var iconPos = new Vector2(min.X + ImGui.GetStyle().FramePadding.X, centerY - (iconSize.Y * 0.5f));
                using (iconFont.Push())
                    dl.AddText(iconPos, col, iconStr);

                var textPos = new Vector2(iconPos.X + iconSize.X + gap, centerY - (labelSize.Y * 0.5f));
                dl.AddText(textPos, col, tab.Label);

                if (isSel)
                {
                    var y = max.Y + UiScale.S(2);
                    dl.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), ImGui.GetColorU32(t.Primary), UiScale.S(2));
                }
            }
            else
            {
                var label = tab.Label;
                if (tab.Icon.HasValue)
                    label = $"{tab.Icon.Value.ToIconString()}  {label}";

                using var txt = ImRaii.PushColor(ImGuiCol.Text, isSel ? t.Text : t.Text);

                var clicked = ImGui.Button(label);
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();

                if (isSel)
                {
                    var y = max.Y + UiScale.S(2);
                    dl.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), ImGui.GetColorU32(t.Primary), UiScale.S(2));
                }

                if (clicked && tab.Enabled)
                    selectedTab = tab;
            }

            if (i != tabs.Count - 1)
                ImGui.SameLine();
        }
        ImGui.NewLine();

        return selectedTab;
    }
}
