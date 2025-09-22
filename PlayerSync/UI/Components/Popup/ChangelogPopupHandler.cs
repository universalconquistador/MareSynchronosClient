using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using MareSynchronos.UI.Components.Theming;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

public class ChangelogPopupHandler : IPopupHandler
{
    private readonly UiSharedService _uiSharedService;
    private readonly ThemeManager _themeManager;
    private string _changelogText = string.Empty;
    private string _versionText = string.Empty;

    public ChangelogPopupHandler(UiSharedService uiSharedService, ThemeManager themeManager)
    {
        _uiSharedService = uiSharedService;
        _themeManager = themeManager;
    }

    public Vector2 PopupSize => new(600, 400);

    public bool ShowClose => false;

    public void DrawContent()
    {
        using var theme = _themeManager.PushTheme();

        // Header with version info
        ImGui.PushStyleColor(ImGuiCol.Text, _themeManager.Current.Accent);
        ImGui.Text($"PlayerSync {_versionText}");
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, _themeManager.Current.TextSecondary);
        ImGui.Text("What's New");
        ImGui.PopStyleColor();

        ImGui.Separator();
        ImGui.Spacing();

        // Changelog content area
        var contentRegion = ImGui.GetContentRegionAvail();
        var childSize = new Vector2(contentRegion.X, contentRegion.Y - 50); // Reserve space for button

        if (ImGui.BeginChild("ChangelogContent", childSize, true))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, _themeManager.Current.TextPrimary);

            UiSharedService.TextWrapped(_changelogText);

            ImGui.PopStyleColor();
        }
        ImGui.EndChild();

        ImGui.Spacing();

        // Buttons
        var buttonWidth = 120f;
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var centerX = (totalWidth - buttonWidth) * 0.5f;

        ImGui.SetCursorPosX(centerX);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Got it!"))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    public void Open(string version, string changelogText)
    {
        _versionText = version;
        _changelogText = changelogText;
    }
}