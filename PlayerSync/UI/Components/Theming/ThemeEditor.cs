using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
        ImGui.PushStyleColor(ImGuiCol.WindowBg, _editingTheme.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _editingTheme.PanelBg);
        ImGui.PushStyleColor(ImGuiCol.Border, _editingTheme.PanelBorder);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, _editingTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, _editingTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, _editingTheme.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.MenuBarBg, _editingTheme.HeaderBg);
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
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, _editingTheme.WindowRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, _editingTheme.ChildRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, _editingTheme.FrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, _editingTheme.PopupRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, _editingTheme.ScrollbarRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, _editingTheme.GrabRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, _editingTheme.TabRounding);

        return new EditingThemeScope(44, 7);
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
            if (_styleVarCount > 0)
                ImGui.PopStyleVar(_styleVarCount);
            ImGui.PopStyleColor(_colorCount);
        }
    }

    public void Draw()
    {
        ImGui.Text("Theme Selection");
        ImGui.Separator();

        DrawThemeSelector();
        ImGui.Spacing();

        ImGui.Text("Theme Colors");
        ImGui.Separator();
        DrawInlineColorEditor();

        ImGui.Spacing();
        DrawApplyButton();

        HandleColorPickerPopups();
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
                    CopyCurrentTheme();
                    _editingThemeName = "Custom";
                    _hasChanges = true;
                }
            }
            if (isCustomSelected)
                ImGui.SetItemDefaultFocus();

            ImGui.EndCombo();
        }
    }

    private void DrawInlineColorEditor()
    {
        ImGui.Columns(2, "ThemeColors", true);

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

        ImGui.NextColumn();

        ImGui.Text("Text Colors");
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

        ImGui.Spacing();
        ImGui.Text("Tooltip Colors");
        DrawColorSquare("Tooltip Background", "TooltipBg", _editingTheme.TooltipBg);
        DrawColorSquare("Tooltip Text", "TooltipText", _editingTheme.TooltipText);

        ImGui.Columns(1);
    }

    private void DrawColorSquare(string label, string propertyName, Vector4 color)
    {
        var buttonId = $"##ColorSquare{propertyName}";
        if (ImGui.ColorButton(buttonId, color, ImGuiColorEditFlags.NoTooltip, new Vector2(30, 20)))
        {
            _colorPickerPopupId = propertyName;
            ImGui.OpenPopup($"ColorPicker{propertyName}");
        }
        ImGui.SameLine();
        ImGui.Text(label);
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
    }

    private void HandleColorPickerPopup(string propertyName, string displayName, Vector4 currentColor, Action<Vector4> setColor)
    {
        var popupId = $"ColorPicker{propertyName}";
        if (ImGui.BeginPopup(popupId))
        {
            ImGui.Text($"Edit {displayName}");
            ImGui.Separator();

            var tempColor = currentColor;
            if (ImGui.ColorPicker4($"##{propertyName}Picker", ref tempColor))
            {
                setColor(tempColor);
                _hasChanges = true;
                _themeManager.SetCustomTheme(_editingTheme);
            }

            ImGui.Spacing();
            if (ImGui.Button("Close"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawApplyButton()
    {
        ImGui.Separator();

        // Show that changes are automatically applied
        ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), "Changes applied automatically");

        if (_hasChanges)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Reset"))
            {
                CopyCurrentTheme();
                _hasChanges = false;
                _themeManager.SetCustomTheme(_editingTheme);
            }
            UiSharedService.AttachToolTip("Reset to current theme");
        }
        else
        {
            ImGui.BeginDisabled();
            _uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Reset");
            ImGui.EndDisabled();
            UiSharedService.AttachToolTip("No changes to reset");
        }

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Times, "Close"))
        {
            CloseThemeEditor();
        }
        UiSharedService.AttachToolTip("Close theme editor");
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
        _editingTheme = new ThemePalette(current);
        _editingThemeName = _themeManager.CurrentThemeName;
        _hasChanges = false;
    }
}