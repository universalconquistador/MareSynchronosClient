using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.UI;

namespace MareSynchronos.UI.Components.Theming;

public class ThemeEditor
{
    private readonly ThemeManager _themeManager;
    private readonly UiSharedService _uiSharedService;
    private ThemePalette _editingTheme;
    private string _editingThemeName = string.Empty;
    private bool _hasChanges;
    private bool _closeRequested;
    private string _colorPickerPopupId = string.Empty;

    public ThemeEditor(ThemeManager themeManager, UiSharedService uiSharedService)
    {
        _themeManager = themeManager;
        _uiSharedService = uiSharedService;
        _editingTheme = new ThemePalette();
        CopyCurrentTheme();
    }

    public bool CloseRequested => _closeRequested;

    public void ResetCloseRequest()
    {
        _closeRequested = false;
    }

    public IDisposable PushEditingTheme()
    {
        //ImGui.PushStyleColor(ImGuiCol.WindowBg, _editingTheme.PanelBg);
        //ImGui.PushStyleColor(ImGuiCol.ChildBg, _editingTheme.PanelBg);
        //ImGui.PushStyleColor(ImGuiCol.Border, _editingTheme.PanelBorder);
        //ImGui.PushStyleColor(ImGuiCol.TitleBg, _editingTheme.HeaderBg);
        //ImGui.PushStyleColor(ImGuiCol.TitleBgActive, _editingTheme.HeaderBg);
        //ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, _editingTheme.HeaderBg);
        //ImGui.PushStyleColor(ImGuiCol.MenuBarBg, _editingTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.Header, _editingTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, _editingTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.Button, _editingTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _editingTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.Text, _editingTheme.TextPrimary);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, _editingTheme.TextDisabled);
        ImGui.PushStyleColor(ImGuiCol.Tab, _editingTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, _editingTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.TabActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocused, _editingTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, _editingTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, _editingTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, _editingTheme.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, _editingTheme.Btn);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, _editingTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, _editingTheme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, _editingTheme.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.Separator, _editingTheme.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, _editingTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.SeparatorActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, _editingTheme.Accent);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, _editingTheme.BtnHovered);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, _editingTheme.BtnActive);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, _editingTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, _editingTheme.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, _editingTheme.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(_editingTheme.HeaderBg.X, _editingTheme.HeaderBg.Y, _editingTheme.HeaderBg.Z, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, _editingTheme.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(_editingTheme.PanelBg.X, _editingTheme.PanelBg.Y, _editingTheme.PanelBg.Z, 0.50f));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, _editingTheme.BtnActive);

        // Apply rounding styles
        //ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, _editingTheme.WindowRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, _editingTheme.ChildRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, _editingTheme.FrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, _editingTheme.PopupRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, _editingTheme.ScrollbarRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, _editingTheme.GrabRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, _editingTheme.TabRounding);

        return new EditingThemeScope(37, 6);
    }

    private class EditingThemeScope : IDisposable
    {
        private readonly int _colorCount;
        private readonly int _styleVarCount;

        public EditingThemeScope(int colorCount, int styleVarCount = 0)
        {
            _colorCount = colorCount;
            _styleVarCount = styleVarCount;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_styleVarCount > 0)
                ImGui.PopStyleVar(_styleVarCount);
            ImGui.PopStyleColor(_colorCount);
        }
    }

    public void Draw()
    {
        // Main Theme Editor body, disabled when using Dalamud as the theme source
        using (ImRaii.Disabled(_themeManager.UsingDalamudTheme))
        {
            // Theme selection dropdown
            ImGui.Text("Theme Selection");
            DrawThemeSelector();

            ImGui.Dummy(new Vector2(5));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(5));

            // Color picker region
            ImGui.Text("Theme Colors");
            DrawTabbedColorEditor();
        }

        ImGui.Dummy(new Vector2(5));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(5));

        // Reset and Save/Exit buttons
        DrawApplyButton();
        
    }

    private void DrawThemeSelector()
    {
        var currentTheme = _themeManager.CurrentThemeName;

        if (ImGui.BeginCombo("Theme", currentTheme))
        {
            foreach (var theme in _themeManager.PredefinedThemes)
            {
                bool isSelected = currentTheme == theme.Key;
                if (ImGui.Selectable(theme.Key, isSelected))
                {
                    _themeManager.SetTheme(theme.Key);
                    CopyCurrentTheme();
                    _hasChanges = false;
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            bool isCustomSelected = _themeManager.IsCustomTheme;
            if (ImGui.Selectable("Custom", isCustomSelected))
            {
                if (!_themeManager.IsCustomTheme)
                {
                    if (!_themeManager.RestoreSavedCustomTheme())
                    {
                        CopyCurrentTheme();
                        _editingThemeName = "Custom";
                        _hasChanges = true;
                    }
                    else
                    {
                        CopyCurrentTheme();
                        _hasChanges = false;
                    }
                }
            }
            if (isCustomSelected)
                ImGui.SetItemDefaultFocus();

            ImGui.EndCombo();
        }
    }

    private void DrawTabbedColorEditor()
    {
        if (ImGui.BeginTabBar("ThemeColorTabs"))
        {
            if (ImGui.BeginTabItem("Panel & UI"))
            {
                ImGui.Text("Panel Colors");
                DrawColorSquare("Panel Background", "PanelBg", _editingTheme.PanelBg);
                DrawColorSquare("Panel Border", "PanelBorder", _editingTheme.PanelBorder);
                DrawColorSquare("Header Background", "HeaderBg", _editingTheme.HeaderBg);
                DrawColorSquare("Accent", "Accent", _editingTheme.Accent);

                ImGui.Spacing();
                ImGui.Text("Button Colors");
                DrawColorSquare("Button", "Btn", _editingTheme.Btn);
                DrawColorSquare("Button Hovered", "BtnHovered", _editingTheme.BtnHovered);
                DrawColorSquare("Button Active", "BtnActive", _editingTheme.BtnActive);

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Text Colors"))
            {
                ImGui.Text("General Text Colors");
                DrawColorSquare("Primary Text", "TextPrimary", _editingTheme.TextPrimary);
                DrawColorSquare("Secondary Text", "TextSecondary", _editingTheme.TextSecondary);
                DrawColorSquare("Disabled Text", "TextDisabled", _editingTheme.TextDisabled);

                ImGui.Spacing();
                ImGui.Text("Button Text Colors");
                DrawColorSquare("Button Text", "BtnText", _editingTheme.BtnText);
                DrawColorSquare("Button Text Hovered", "BtnTextHovered", _editingTheme.BtnTextHovered);
                DrawColorSquare("Button Text Active", "BtnTextActive", _editingTheme.BtnTextActive);

                ImGui.Spacing();
                ImGui.Text("Link Colors");
                DrawColorSquare("Link", "Link", _editingTheme.Link);
                DrawColorSquare("Link Hover", "LinkHover", _editingTheme.LinkHover);

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("UI Text"))
            {
                ImGui.Text("Specific UI Text Colors");
                DrawColorSquare("UID/Alias Text", "UidAliasText", _editingTheme.UidAliasText);
                DrawColorSquare("Users Online Text", "UsersOnlineText", _editingTheme.UsersOnlineText);
                DrawColorSquare("Users Online Number", "UsersOnlineNumber", _editingTheme.UsersOnlineNumber);

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Status"))
            {
                ImGui.Text("Connection Status");
                DrawColorSquare("Connected", "StatusConnected", _editingTheme.StatusConnected);
                DrawColorSquare("Connecting", "StatusConnecting", _editingTheme.StatusConnecting);
                DrawColorSquare("Disconnected", "StatusDisconnected", _editingTheme.StatusDisconnected);
                DrawColorSquare("Broadcasting", "StatusBroadcasting", _editingTheme.StatusBroadcasting);

                ImGui.Spacing();
                ImGui.Text("General Status");
                DrawColorSquare("OK", "StatusOk", _editingTheme.StatusOk);
                DrawColorSquare("Warning", "StatusWarn", _editingTheme.StatusWarn);
                DrawColorSquare("Error", "StatusError", _editingTheme.StatusError);
                DrawColorSquare("Paused", "StatusPaused", _editingTheme.StatusPaused);
                DrawColorSquare("Info", "StatusInfo", _editingTheme.StatusInfo);

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Other"))
            {
                ImGui.Text("Tooltip Colors");
                DrawColorSquare("Tooltip Background", "TooltipBg", _editingTheme.TooltipBg);
                DrawColorSquare("Tooltip Text", "TooltipText", _editingTheme.TooltipText);

                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        // Handle popups outside of the tab bar entirely
        HandleColorPickerPopups();
    }

    private void DrawColorSquare(string label, string propertyName, Vector4 color)
    {
        var buttonId = $"##ColorSquare{propertyName}";
        // Add scaling to the color picker buttons
        var colorSquareX = 30 * ImGuiHelpers.GlobalScale;
        var colorSquareY = 20 * ImGuiHelpers.GlobalScale;
        if (ImGui.ColorButton(buttonId, color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.AlphaPreview, new Vector2(colorSquareX, colorSquareY)))
        {
            _colorPickerPopupId = propertyName;
            ImGui.OpenPopup($"ColorPicker{propertyName}");
        }
        ImGui.SameLine();
        ImGui.Text(label);

        // Handle popup immediately after the button
        var popupId = $"ColorPicker{propertyName}";
        if (ImGui.BeginPopup(popupId))
        {
            ImGui.Text($"Edit {label}");
            ImGui.Separator();

            var tempColor = color;
            if (ImGui.ColorPicker4($"##{propertyName}Picker", ref tempColor))
            {
                // Find and call the appropriate setter
                SetColorByPropertyName(propertyName, tempColor);
            }

            ImGui.Spacing();
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save and Close"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void SetColorByPropertyName(string propertyName, Vector4 color)
    {
        switch (propertyName)
        {
            case "PanelBg": _editingTheme.PanelBg = color; break;
            case "PanelBorder": _editingTheme.PanelBorder = color; break;
            case "HeaderBg": _editingTheme.HeaderBg = color; break;
            case "Accent": _editingTheme.Accent = color; break;
            case "Btn": _editingTheme.Btn = color; break;
            case "BtnHovered": _editingTheme.BtnHovered = color; break;
            case "BtnActive": _editingTheme.BtnActive = color; break;
            case "TextPrimary": _editingTheme.TextPrimary = color; break;
            case "TextSecondary": _editingTheme.TextSecondary = color; break;
            case "TextDisabled": _editingTheme.TextDisabled = color; break;
            case "BtnText": _editingTheme.BtnText = color; break;
            case "BtnTextHovered": _editingTheme.BtnTextHovered = color; break;
            case "BtnTextActive": _editingTheme.BtnTextActive = color; break;
            case "Link": _editingTheme.Link = color; break;
            case "LinkHover": _editingTheme.LinkHover = color; break;
            case "TooltipBg": _editingTheme.TooltipBg = color; break;
            case "TooltipText": _editingTheme.TooltipText = color; break;
            case "UidAliasText": _editingTheme.UidAliasText = color; break;
            case "UsersOnlineText": _editingTheme.UsersOnlineText = color; break;
            case "UsersOnlineNumber": _editingTheme.UsersOnlineNumber = color; break;
            case "StatusConnected": _editingTheme.StatusConnected = color; break;
            case "StatusConnecting": _editingTheme.StatusConnecting = color; break;
            case "StatusDisconnected": _editingTheme.StatusDisconnected = color; break;
            case "StatusBroadcasting": _editingTheme.StatusBroadcasting = color; break;
            case "StatusOk": _editingTheme.StatusOk = color; break;
            case "StatusWarn": _editingTheme.StatusWarn = color; break;
            case "StatusError": _editingTheme.StatusError = color; break;
            case "StatusPaused": _editingTheme.StatusPaused = color; break;
            case "StatusInfo": _editingTheme.StatusInfo = color; break;
        }
        _hasChanges = true;
        _themeManager.SetCustomTheme(_editingTheme);
    }

    private void HandleColorPickerPopups()
    {
        HandleColorPickerPopup("PanelBg", "Panel Background", _editingTheme.PanelBg, (color) => _editingTheme.PanelBg = color);
        HandleColorPickerPopup("PanelBorder", "Panel Border", _editingTheme.PanelBorder, (color) => _editingTheme.PanelBorder = color);
        HandleColorPickerPopup("HeaderBg", "Header Background", _editingTheme.HeaderBg, (color) => _editingTheme.HeaderBg = color);
        HandleColorPickerPopup("Accent", "Accent", _editingTheme.Accent, (color) => _editingTheme.Accent = color);
        HandleColorPickerPopup("Btn", "Button", _editingTheme.Btn, (color) => _editingTheme.Btn = color);
        HandleColorPickerPopup("BtnHovered", "Button Hovered", _editingTheme.BtnHovered, (color) => _editingTheme.BtnHovered = color);
        HandleColorPickerPopup("BtnActive", "Button Active", _editingTheme.BtnActive, (color) => _editingTheme.BtnActive = color);
        HandleColorPickerPopup("TextPrimary", "Primary Text", _editingTheme.TextPrimary, (color) => _editingTheme.TextPrimary = color);
        HandleColorPickerPopup("TextSecondary", "Secondary Text", _editingTheme.TextSecondary, (color) => _editingTheme.TextSecondary = color);
        HandleColorPickerPopup("TextDisabled", "Disabled Text", _editingTheme.TextDisabled, (color) => _editingTheme.TextDisabled = color);
        HandleColorPickerPopup("BtnText", "Button Text", _editingTheme.BtnText, (color) => _editingTheme.BtnText = color);
        HandleColorPickerPopup("BtnTextHovered", "Button Text Hovered", _editingTheme.BtnTextHovered, (color) => _editingTheme.BtnTextHovered = color);
        HandleColorPickerPopup("BtnTextActive", "Button Text Active", _editingTheme.BtnTextActive, (color) => _editingTheme.BtnTextActive = color);
        HandleColorPickerPopup("Link", "Link", _editingTheme.Link, (color) => _editingTheme.Link = color);
        HandleColorPickerPopup("LinkHover", "Link Hover", _editingTheme.LinkHover, (color) => _editingTheme.LinkHover = color);
        HandleColorPickerPopup("TooltipBg", "Tooltip Background", _editingTheme.TooltipBg, (color) => _editingTheme.TooltipBg = color);
        HandleColorPickerPopup("TooltipText", "Tooltip Text", _editingTheme.TooltipText, (color) => _editingTheme.TooltipText = color);
        HandleColorPickerPopup("UidAliasText", "UID/Alias Text", _editingTheme.UidAliasText, (color) => _editingTheme.UidAliasText = color);
        HandleColorPickerPopup("UsersOnlineText", "Users Online Text", _editingTheme.UsersOnlineText, (color) => _editingTheme.UsersOnlineText = color);
        HandleColorPickerPopup("UsersOnlineNumber", "Users Online Number", _editingTheme.UsersOnlineNumber, (color) => _editingTheme.UsersOnlineNumber = color);
        HandleColorPickerPopup("StatusConnected", "Connected", _editingTheme.StatusConnected, (color) => _editingTheme.StatusConnected = color);
        HandleColorPickerPopup("StatusConnecting", "Connecting", _editingTheme.StatusConnecting, (color) => _editingTheme.StatusConnecting = color);
        HandleColorPickerPopup("StatusDisconnected", "Disconnected", _editingTheme.StatusDisconnected, (color) => _editingTheme.StatusDisconnected = color);
        HandleColorPickerPopup("StatusBroadcasting", "Broadcasting", _editingTheme.StatusBroadcasting, (color) => _editingTheme.StatusBroadcasting = color);
        HandleColorPickerPopup("StatusOk", "OK", _editingTheme.StatusOk, (color) => _editingTheme.StatusOk = color);
        HandleColorPickerPopup("StatusWarn", "Warning", _editingTheme.StatusWarn, (color) => _editingTheme.StatusWarn = color);
        HandleColorPickerPopup("StatusError", "Error", _editingTheme.StatusError, (color) => _editingTheme.StatusError = color);
        HandleColorPickerPopup("StatusPaused", "Paused", _editingTheme.StatusPaused, (color) => _editingTheme.StatusPaused = color);
        HandleColorPickerPopup("StatusInfo", "Info", _editingTheme.StatusInfo, (color) => _editingTheme.StatusInfo = color);
    }

    private void HandleColorPickerPopup(string propertyName, string displayName, Vector4 currentColor, Action<Vector4> setColor)
    {
        var popupId = $"ColorPicker{propertyName}";
        if (ImGui.BeginPopup(popupId))
        {
            ImGui.Text($"Edit {displayName}");
            ImGui.Separator();

            var tempColor = currentColor;
            bool colorChanged = false;

            if (ImGui.ColorPicker4($"##{propertyName}Picker", ref tempColor, ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf))
            {
                colorChanged = true;
            }

            ImGui.Spacing();
            ImGui.Text("Opacity:");
            ImGui.SameLine();
            var alpha = tempColor.W;
            if (ImGui.SliderFloat($"##Alpha{propertyName}", ref alpha, 0.0f, 1.0f, "%.2f"))
            {
                tempColor.W = alpha;
                colorChanged = true;
            }

            if (colorChanged)
            {
                setColor(tempColor);
                _hasChanges = true;
                _themeManager.SetCustomTheme(_editingTheme);
            }

            ImGui.Spacing();
            // Color picker popup save button
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save and Close"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawApplyButton()
    {
        //ImGui.Separator();

        // Show that changes are automatically applied
        ImGui.TextColored(_editingTheme.Accent, "Changes applied automatically");

        if (_hasChanges)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Reset"))
            {
                CopyCurrentTheme();
                _hasChanges = false;
                _themeManager.SetCustomTheme(_editingTheme);
            }
            _uiSharedService.AttachToolTip("Reset to current theme");
        }
        else
        {
            ImGui.BeginDisabled();
            _uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Reset");
            ImGui.EndDisabled();
            _uiSharedService.AttachToolTip("No changes to reset");
        }

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save and Close"))
        {
            CloseThemeEditor();
        }
        _uiSharedService.AttachToolTip("Close theme editor");
    }

    private void CloseThemeEditor()
    {
        if (_hasChanges)
        {
            CopyCurrentTheme();
            _hasChanges = false;
        }
        _closeRequested = true;
    }

    private void CopyCurrentTheme()
    {
        var current = _themeManager.Current;
        _editingTheme = current.Clone();
        _editingThemeName = _themeManager.CurrentThemeName;
        _hasChanges = false;
    }
}