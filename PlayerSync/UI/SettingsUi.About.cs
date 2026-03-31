using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.UI.ModernUi;
using System.Numerics;


namespace MareSynchronos.UI;

public partial class SettingsUi
{
    private UiNav.Tab<AboutTabs>? _selectedTabAbout;

    private IReadOnlyList<UiNav.Tab<AboutTabs>>? _aboutTabs;
    private IReadOnlyList<UiNav.Tab<AboutTabs>> AboutTabsList => _aboutTabs ??=
    [
        new(AboutTabs.About, "About", DrawServiceAbout),
    ];

    private enum AboutTabs
    {
        About
    }

    private void DrawAboutSettings()
    {
        _lastTab = "Service";

        _selectedTabAbout = UiNav.DrawTabsUnderline(_theme, AboutTabsList, _selectedTabAbout, _uiShared.IconFont);

        using var child = ImRaii.Child("##panel", new Vector2(0, 0), false);

        _selectedTabAbout.TabAction.Invoke();
    }

    private void DrawServiceAbout()
    {
        _lastTab = "About";

        _uiShared.BigText("About");
        ImGuiHelpers.ScaledDummy(2);

        UiSharedService.TextWrapped("PlayerSync is developed, supported, and funded by the community. We make no money from this, it actually costs us money to run. " +
            "If you have found this service useful, please consider helping to ensure it can survive.");

        ImGuiHelpers.ScaledDummy(4);

        if (ImGui.Button("Ko-Fi"))
        {
            Util.OpenLink("https://ko-fi.com/playersync");
        }

        ImGui.SameLine();

        if (ImGui.Button("Patreon"))
        {
            Util.OpenLink("https://www.patreon.com/PlayerSync");
        }
    }
}
