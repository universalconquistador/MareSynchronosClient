using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Components.Theming;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public class HoveringToolbar
{
    private readonly UiSharedService _uiSharedService;
    private readonly ThemeManager _themeManager;
    private readonly MareMediator _mediator;

    private bool _showToolbar = true;
    private bool _showPopup = false;
    private Vector2 _lastWindowPos;
    private Vector2 _lastWindowSize;

    public bool AllowClickthrough { get; set; } = false;
    public bool AllowPinning { get; set; } = false;

    public HoveringToolbar(UiSharedService uiSharedService, ThemeManager themeManager, MareMediator mediator)
    {
        _uiSharedService = uiSharedService;
        _themeManager = themeManager;
        _mediator = mediator;
    }

    public void Draw(Vector2 parentWindowPos, Vector2 parentWindowSize)
    {
        if (!_showToolbar) return;

        _lastWindowPos = parentWindowPos;
        _lastWindowSize = parentWindowSize;

        DrawFloatingToolbar();

        if (AllowClickthrough)
        {
            DrawFloatingResetButton();
        }
    }

    private void DrawFloatingToolbar()
    {
        float spacing = 6f * ImGuiHelpers.GlobalScale;
        float btnSide = 22f * ImGuiHelpers.GlobalScale;
        float rightPad = ImGui.GetStyle().WindowPadding.X + 10f * ImGuiHelpers.GlobalScale;

        var style = ImGui.GetStyle();
        float topOffset = style.FramePadding.Y + style.ItemSpacing.Y + ImGuiHelpers.GlobalScale;

        var crMin = ImGui.GetWindowContentRegionMin();
        var crMax = ImGui.GetWindowContentRegionMax();

        float stripWidth = (btnSide * 3) + (spacing * 2);
        float overlayX = _lastWindowPos.X + crMax.X - stripWidth - spacing - 32f - 5f - 15f;
        float overlayY = _lastWindowPos.Y + crMin.Y + topOffset - 10f;

        ImGui.SetNextWindowPos(new Vector2(overlayX, overlayY), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);

        var floatingFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking;

        if (AllowClickthrough)
            floatingFlags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoFocusOnAppearing;

        if (ImGui.Begin("##PlayerSyncFloatingToolbar", floatingFlags))
        {
            var currentTheme = _themeManager?.Current;
            if (currentTheme != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, currentTheme.Btn);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, currentTheme.BtnHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, currentTheme.BtnActive);
                ImGui.PushStyleColor(ImGuiCol.Text, currentTheme.BtnText);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
            }

            // Hamburger menu button
            using (ImRaii.PushColor(ImGuiCol.Text, currentTheme?.BtnText ?? new Vector4(1f, 1f, 1f, 1f)))
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Bars))
                {
                    _showPopup = !_showPopup;
                    if (_showPopup)
                        ImGui.OpenPopup("##PlayerSyncHamburgerMenu");
                }
            }
            AttachTooltip("Player Sync Menu");
            ImGui.SameLine(0, spacing);

            // Collapse button - need to get the collapse state from parent
            using (ImRaii.PushColor(ImGuiCol.Text, currentTheme?.BtnText ?? new Vector4(1f, 1f, 1f, 1f)))
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.ChevronUp))
                {
                    _mediator.Publish(new ToggleCollapseMessage());
                }
            }
            AttachTooltip("Collapse Window");
            ImGui.SameLine(0, spacing);

            // Close button
            using (ImRaii.PushColor(ImGuiCol.Text, currentTheme?.BtnText ?? new Vector4(1f, 1f, 1f, 1f)))
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Times))
                {
                    _mediator.Publish(new CloseWindowMessage());
                }
            }
            AttachTooltip("Close Window");

            // Hamburger menu popup
            DrawHamburgerMenuPopup(currentTheme);

            if (currentTheme != null)
            {
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(4);
            }
        }
        ImGui.End();
    }

    private void DrawHamburgerMenuPopup(ThemePalette? currentTheme)
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 8f))
        using (ImRaii.PushColor(ImGuiCol.PopupBg, currentTheme?.Btn ?? new Vector4(0.15f, 0.18f, 0.25f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.Border, currentTheme?.PanelBorder ?? new Vector4(0.43f, 0.43f, 0.50f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.Text, currentTheme?.BtnText ?? new Vector4(1f, 1f, 1f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.HeaderHovered, currentTheme?.BtnHovered ?? new Vector4(0.26f, 0.59f, 0.98f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.HeaderActive, currentTheme?.BtnActive ?? new Vector4(0.06f, 0.53f, 0.98f, 1f)))
        {
            if (ImGui.BeginPopup("##PlayerSyncHamburgerMenu"))
            {
                // Window Controls
                bool isPinned = AllowPinning;
                if (ImGui.MenuItem($"{FontAwesomeIcon.Thumbtack.ToIconString()}  Pin Window", "", isPinned))
                {
                    AllowPinning = !AllowPinning;
                }

                bool isClickThrough = AllowClickthrough;
                if (ImGui.MenuItem($"{FontAwesomeIcon.MousePointer.ToIconString()}  Click Through", "", isClickThrough))
                {
                    AllowClickthrough = !AllowClickthrough;
                    if (AllowClickthrough)
                    {
                        // Auto-enable pin window when click-through is enabled
                        AllowPinning = true;
                    }
                }

                ImGui.Separator();

                // Event Viewer
                if (ImGui.MenuItem($"{FontAwesomeIcon.Book.ToIconString()}  Event Viewer"))
                {
                    _mediator.Publish(new UiToggleMessage(typeof(EventViewerUI)));
                }

                // Additional menu items
                if (ImGui.MenuItem($"{FontAwesomeIcon.Users.ToIconString()}  Pair Management"))
                {
                    // Add pair management functionality
                }

                if (ImGui.MenuItem($"{FontAwesomeIcon.BroadcastTower.ToIconString()}  Broadcast Options"))
                {
                    // Add broadcast functionality
                }

                ImGui.EndPopup();
            }
        }
    }

    private void DrawFloatingResetButton()
    {
        float spacing = 6f * ImGuiHelpers.GlobalScale;
        float btnSide = 22f * ImGuiHelpers.GlobalScale;

        var buttonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Undo);

        var crMin = ImGui.GetWindowContentRegionMin();
        var crMax = ImGui.GetWindowContentRegionMax();

        float stripWidth = (btnSide * 3) + (spacing * 2);
        float resetX = _lastWindowPos.X + crMax.X - stripWidth - spacing - 32f - buttonSize.X - spacing - 5f - 15f;
        float resetY = _lastWindowPos.Y + crMin.Y + ImGui.GetStyle().FramePadding.Y + ImGui.GetStyle().ItemSpacing.Y + ImGuiHelpers.GlobalScale - 10f;

        ImGui.SetNextWindowPos(new Vector2(resetX, resetY));
        ImGui.SetNextWindowBgAlpha(0f);

        var floatingFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking;
        // Don't add NoInputs here - we want this button to remain clickable

        if (ImGui.Begin("##PlayerSyncResetButton", floatingFlags))
        {
            var currentTheme = _themeManager?.Current;
            if (currentTheme != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, currentTheme.Btn);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, currentTheme.BtnHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, currentTheme.BtnActive);
                ImGui.PushStyleColor(ImGuiCol.Text, currentTheme.BtnText);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
            }

            if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
            {
                // Reset click-through and pin window
                AllowClickthrough = false;
                AllowPinning = false;
            }
            AttachTooltip("Reset window settings");

            if (currentTheme != null)
            {
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(4);
            }
        }
        ImGui.End();
    }

    private void AttachTooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            var currentTheme = _themeManager?.Current;
            if (currentTheme != null)
            {
                using (ImRaii.PushColor(ImGuiCol.PopupBg, currentTheme.TooltipBg))
                using (ImRaii.PushColor(ImGuiCol.Text, currentTheme.TooltipText))
                {
                    ImGui.BeginTooltip();
                    ImGui.Text(text);
                    ImGui.EndTooltip();
                }
            }
            else
            {
                ImGui.BeginTooltip();
                ImGui.Text(text);
                ImGui.EndTooltip();
            }
        }
    }

    public void Toggle()
    {
        _showToolbar = !_showToolbar;
    }

    public void Show()
    {
        _showToolbar = true;
    }

    public void Hide()
    {
        _showToolbar = false;
    }
}

// Message for theme editor toggle
public record ToggleThemeEditorMessage : MessageBase;