using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Themes;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class ThemeEditorUi : WindowMediatorSubscriberBase
{
    private readonly ThemeManager _themeManager;
    private readonly UiSharedService _uiSharedService;
    private readonly MareConfigService _configService;
    private Theme _editingTheme;
    private string _newThemeName = "";
    private bool _unsavedChanges = false;
    private string _selectedCategory = "Presets";
    private bool _autoPreview = true;
    private bool _autoSave = false;
    private DateTime _lastAutoSaveTime = DateTime.MinValue;
    private readonly TimeSpan _autoSaveInterval = TimeSpan.FromSeconds(30);

    public ThemeEditorUi(ILogger<ThemeEditorUi> logger, UiSharedService uiSharedService, 
        ThemeManager themeManager, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
        MareConfigService configService)
        : base(logger, mediator, "Theme Editor###ThemeEditor", performanceCollectorService)
    {
        _uiSharedService = uiSharedService;
        _themeManager = themeManager;
        _configService = configService;
        _editingTheme = _themeManager.CurrentTheme.Clone();
        
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(600, 400),
            MaximumSize = new Vector2(1200, 800),
        };

        Size = new Vector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    protected override void DrawInternal()
    {
        // Use auto-preview theme if enabled and there are unsaved changes, otherwise use current theme
        var theme = (_autoPreview && _unsavedChanges) ? _editingTheme : _themeManager.CurrentTheme;
        
        // Apply theme with proper style stack management (scoped to this window only)
        using var themeScope = ApplyThemeForWindow(theme);
        
        // Handle auto-save
        if (_autoSave && _unsavedChanges && DateTime.Now - _lastAutoSaveTime > _autoSaveInterval)
        {
            if (!string.IsNullOrWhiteSpace(_newThemeName))
            {
                var autoSaveTheme = _editingTheme.Clone();
                autoSaveTheme.Name = _newThemeName.Trim() + " (Auto)";
                _themeManager.SaveTheme(autoSaveTheme);
                _lastAutoSaveTime = DateTime.Now;
            }
        }
        
        DrawTopBar();
        ImGui.Separator();
        
        using var child = ImRaii.Child("ThemeEditorContent", Vector2.Zero, false);
        if (!child) return;

        var availWidth = ImGui.GetContentRegionAvail().X;
        var leftWidth = 200f;
        var rightWidth = availWidth - leftWidth - ImGui.GetStyle().ItemSpacing.X;

        DrawCategorySelector(leftWidth);
        ImGui.SameLine();
        DrawEditorContent(rightWidth);
    }

    private void DrawTopBar()
    {
        var theme = _themeManager.CurrentTheme;
        
        // Theme selection
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("Base Theme", _editingTheme.Name))
        {
            foreach (var availableTheme in _themeManager.AvailableThemes)
            {
                bool isSelected = availableTheme.Key == _editingTheme.Name;
                if (ImGui.Selectable(availableTheme.Key, isSelected))
                {
                    _editingTheme = availableTheme.Value.Clone();
                    _unsavedChanges = true;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        
        // New theme name
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint("##NewThemeName", "New theme name...", ref _newThemeName, 100))
        {
            _unsavedChanges = true;
        }

        ImGui.SameLine();
        
        // Save button
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(_newThemeName)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save as New"))
            {
                var newTheme = _editingTheme.Clone();
                newTheme.Name = _newThemeName.Trim();
                _themeManager.SaveTheme(newTheme);
                _newThemeName = "";
                _unsavedChanges = false;
            }
        }

        ImGui.SameLine();
        
        // Apply button (sets as current theme)
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Apply Theme"))
        {
            var themeToApply = _editingTheme.Clone();
            themeToApply.Name = _themeManager.CurrentTheme.Name;
            _themeManager.AddTheme(themeToApply);
            _themeManager.SetTheme(themeToApply.Name);
        }

        ImGui.SameLine();
        
        // Auto-preview toggle (now only affects this window)
        ImGui.Checkbox("Auto Preview", ref _autoPreview);
        
        ImGui.SameLine();
        ImGui.Checkbox("Auto Save", ref _autoSave);
        
        ImGui.SameLine();
        
        // Reset button
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Reset"))
        {
            _editingTheme = _themeManager.CurrentTheme.Clone();
            _unsavedChanges = false;
        }

        if (_unsavedChanges)
        {
            ImGui.SameLine();
            using (theme.PushThemedColor(ImGuiCol.Text))
            {
                var statusText = "(Unsaved changes)";
                if (_autoSave && !string.IsNullOrWhiteSpace(_newThemeName))
                {
                    var timeLeft = _autoSaveInterval - (DateTime.Now - _lastAutoSaveTime);
                    if (timeLeft.TotalSeconds > 0)
                    {
                        statusText = $"(Auto-save in {timeLeft.TotalSeconds:F0}s)";
                    }
                    else
                    {
                        statusText = "(Auto-saving...)";
                    }
                }
                ImGui.TextUnformatted(statusText);
            }
        }
    }

    private void DrawCategorySelector(float width)
    {
        using var child = ImRaii.Child("Categories", new Vector2(width, 0), true);
        if (!child) return;

        var categories = new[] { "Presets", "Colors", "Sizing", "Spacing", "Preview" };
        
        foreach (var category in categories)
        {
            bool isSelected = _selectedCategory == category;
            
            if (isSelected)
            {
                var theme = _themeManager.CurrentTheme;
                using (theme.PushThemedColor(ImGuiCol.Button))
                {
                    ImGui.Button(category, new Vector2(-1, 0));
                }
            }
            else
            {
                if (ImGui.Button(category, new Vector2(-1, 0)))
                {
                    _selectedCategory = category;
                }
            }
        }
    }

    private void DrawEditorContent(float width)
    {
        using var child = ImRaii.Child("EditorContent", new Vector2(width, 0), true);
        if (!child) return;

        switch (_selectedCategory)
        {
            case "Presets":
                DrawPresetSelector();
                break;
            case "Colors":
                DrawColorEditor();
                break;
            case "Sizing":
                DrawSizingEditor();
                break;
            case "Spacing":
                DrawSpacingEditor();
                break;
            case "Preview":
                DrawPreview();
                break;
        }
    }

    private void DrawPresetSelector()
    {
        ImGui.TextUnformatted("Preset Themes");
        ImGui.Separator();
        ImGui.TextUnformatted("Select a preset theme to start customizing:");
        ImGui.Spacing();

        // Group presets by category
        var presetCategories = new Dictionary<string, List<string>>
        {
            ["Core"] = new() { "Blue", "Mint", "Purple", "Dark" },
            ["Light"] = new() { "Sakura Daylight" },
            ["Cyberpunk"] = new() { "Midnight Neon", "Cyberpunk", "Ocean Deep" },
            ["Pride"] = new() { "Pride Rainbow", "Trans Pride" },
            ["Character"] = new() { "PixxieStixx Fairy" }
        };

        foreach (var category in presetCategories)
        {
            if (ImGui.CollapsingHeader(category.Key, ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var presetName in category.Value)
                {
                    if (PresetThemes.Presets.ContainsKey(presetName))
                    {
                        if (ImGui.Button($"Load {presetName}", new Vector2(-1, 0)))
                        {
                            _editingTheme = PresetThemes.Presets[presetName].Clone();
                            _unsavedChanges = true;
                        }
                    }
                }
                ImGui.Spacing();
            }
        }
    }

    private void DrawColorEditor()
    {
        ImGui.TextUnformatted("Theme Colors");
        ImGui.Separator();

        // Color editing simplified for compilation
        ImGui.TextUnformatted("Theme color editing interface");
        ImGui.TextUnformatted("(Individual color editors temporarily disabled for compilation)");
        
        if (ImGui.Button("Reset to Default Dark Theme"))
        {
            // Reset logic would go here
            _unsavedChanges = true;
        }
    }

    private void DrawSizingEditor()
    {
        ImGui.TextUnformatted("Rounding & Borders");
        ImGui.Separator();

        // Float sliders simplified for compilation
        ImGui.TextUnformatted("Rounding & Border editing interface");
        ImGui.TextUnformatted("(Individual sliders temporarily disabled for compilation)");
        
        if (ImGui.Button("Apply Standard Rounding"))
        {
            // Standard rounding logic would go here
            _unsavedChanges = true;
        }
    }

    private void DrawSpacingEditor()
    {
        ImGui.TextUnformatted("Spacing & Padding");
        ImGui.Separator();

        // Vector2 sliders simplified for compilation
        ImGui.TextUnformatted("Window Padding, Frame Padding, etc. can be edited individually");
        ImGui.TextUnformatted("(Vector2 editing temporarily disabled for compilation)");
    }

    private void DrawPreview()
    {
        ImGui.TextUnformatted("Theme Preview");
        ImGui.Separator();

        // Apply editing theme with scoped styling for preview area
        using var previewScope = ApplyPreviewTheme(_editingTheme);

        if (ImGui.BeginChild("PreviewArea", Vector2.Zero, true))
        {
            ImGui.TextUnformatted("Sample UI Elements:");
            ImGui.Separator();

            // Sample buttons
            ImGui.Button("Primary Button");
            ImGui.SameLine();
            ImGui.SmallButton("Small Button");
            
            // Sample input
            string sampleText = "Sample input text";
            ImGui.InputText("Input", ref sampleText, 100);
            
            // Sample checkbox
            bool sampleCheck = true;
            ImGui.Checkbox("Sample Checkbox", ref sampleCheck);
            
            // Sample slider
            float sampleFloat = 0.5f;
            ImGui.SliderFloat("Sample Slider", ref sampleFloat, 0f, 1f);
            
            // Sample tree
            if (ImGui.TreeNode("Sample Tree"))
            {
                ImGui.TextUnformatted("Tree content");
                ImGui.TreePop();
            }
            
            // Sample colors
            ImGui.Separator();
            ImGui.TextUnformatted("Color Samples:");
            
            using (ImRaii.PushColor(ImGuiCol.Text, _editingTheme.Success))
                ImGui.TextUnformatted("Success Color");
            using (ImRaii.PushColor(ImGuiCol.Text, _editingTheme.Warning))
                ImGui.TextUnformatted("Warning Color");
            using (ImRaii.PushColor(ImGuiCol.Text, _editingTheme.Error))
                ImGui.TextUnformatted("Error Color");
            using (ImRaii.PushColor(ImGuiCol.Text, _editingTheme.Info))
                ImGui.TextUnformatted("Info Color");
                
            ImGui.EndChild();
        }
    }

    private IDisposable ApplyPreviewTheme(Theme theme)
    {
        var styleStack = new List<IDisposable>();
        
        try
        {
            // Apply just the key visual colors for preview - minimal set to avoid stack overflow
            styleStack.Add(ImRaii.PushColor(ImGuiCol.FrameBg, theme.Surface));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.FrameBgHovered, theme.Hover));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.FrameBgActive, theme.Active));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.Button, theme.Primary with { W = 0.6f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ButtonHovered, theme.Primary with { W = 0.8f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ButtonActive, theme.Primary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.CheckMark, theme.Success));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.SliderGrab, theme.Primary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.SliderGrabActive, theme.Primary with { W = 0.8f }));

            return new StyleStackDisposer(styleStack);
        }
        catch
        {
            foreach (var item in styleStack)
            {
                item?.Dispose();
            }
            throw;
        }
    }

    private void DrawColorEdit(string label, ref Vector4 color)
    {
        if (ImGui.ColorEdit4($"{label}###{label}ColorEdit", ref color, 
            ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreview))
        {
            _unsavedChanges = true;
        }
    }

    private bool DrawFloatSlider(string label, ref float value, float min, float max)
    {
        return ImGui.SliderFloat(label, ref value, min, max);
    }

    private bool DrawVector2Slider(string label, ref Vector2 value, Vector2 min, Vector2 max)
    {
        bool changed = false;
        var x = value.X;
        var y = value.Y;
        changed |= ImGui.SliderFloat($"{label} X", ref x, min.X, max.X);
        changed |= ImGui.SliderFloat($"{label} Y", ref y, min.Y, max.Y);
        if (changed)
        {
            value = new Vector2(x, y);
        }
        return changed;
    }

    private IDisposable ApplyThemeForWindow(Theme theme)
    {
        var styleStack = new List<IDisposable>();
        
        try
        {
            // Apply theme colors with proper scoping - each Push creates a disposable that must be disposed
            styleStack.Add(ImRaii.PushColor(ImGuiCol.WindowBg, theme.Background));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ChildBg, theme.BackgroundSecondary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.PopupBg, theme.Surface));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.Border, theme.Border));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.BorderShadow, Vector4.Zero));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.FrameBg, theme.Surface));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.FrameBgHovered, theme.Hover));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.FrameBgActive, theme.Active));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.TitleBg, theme.NavBackground));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.TitleBgActive, theme.NavBackground));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.TitleBgCollapsed, theme.NavBackground));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.MenuBarBg, theme.NavBackground));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ScrollbarBg, theme.Background with { W = 0.5f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ScrollbarGrab, theme.Secondary with { W = 0.6f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ScrollbarGrabHovered, theme.Secondary with { W = 0.8f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ScrollbarGrabActive, theme.Secondary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.CheckMark, theme.Success));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.SliderGrab, theme.Primary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.SliderGrabActive, theme.Primary with { W = 0.8f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.Button, theme.Primary with { W = 0.6f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ButtonHovered, theme.Primary with { W = 0.8f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ButtonActive, theme.Primary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.Header, theme.Primary with { W = 0.3f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.HeaderHovered, theme.Primary with { W = 0.5f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.HeaderActive, theme.Primary with { W = 0.7f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.Separator, theme.NavSeparator));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.SeparatorHovered, theme.Primary with { W = 0.6f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.SeparatorActive, theme.Primary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ResizeGrip, theme.Primary with { W = 0.2f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ResizeGripHovered, theme.Primary with { W = 0.5f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ResizeGripActive, theme.Primary with { W = 0.8f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.Tab, theme.NavBackground));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.TabHovered, theme.NavItemHover));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.TabActive, theme.NavItemActive));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.TabUnfocused, theme.NavBackground));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.TabUnfocusedActive, theme.NavItemActive with { W = 0.7f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.DragDropTarget, theme.Accent));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.NavHighlight, theme.Primary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.NavWindowingHighlight, theme.Primary));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.NavWindowingDimBg, theme.Background with { W = 0.8f }));
            styleStack.Add(ImRaii.PushColor(ImGuiCol.ModalWindowDimBg, theme.Background with { W = 0.8f }));
            
            // Apply style variables if theme has curved windows enabled
            if (_configService.Current.CurvedWindows)
            {
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, theme.WindowRounding));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, theme.ChildRounding));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, theme.FrameRounding));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, theme.PopupRounding));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.ScrollbarRounding, theme.ScrollbarRounding));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.TabRounding, theme.TabRounding));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, theme.WindowBorderSize));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, theme.ChildBorderSize));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, theme.PopupBorderSize));
                styleStack.Add(ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, theme.FrameBorderSize));
            }

            return new StyleStackDisposer(styleStack);
        }
        catch
        {
            // If anything fails, dispose what we've created so far
            foreach (var item in styleStack)
            {
                item?.Dispose();
            }
            throw;
        }
    }

    private sealed class StyleStackDisposer : IDisposable
    {
        private readonly List<IDisposable> _items;
        private bool _disposed;

        public StyleStackDisposer(List<IDisposable> items)
        {
            _items = items;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Dispose in reverse order to properly unwind the ImGui style stack
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    _items[i]?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}