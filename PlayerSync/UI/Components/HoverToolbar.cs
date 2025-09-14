using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Themes;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public class HoverToolbar
{
    private readonly ThemeManager _themeManager;
    private readonly UiSharedService _uiSharedService;
    private bool _isVisible = false;
    private Vector2 _position = Vector2.Zero;
    private Vector2 _size = Vector2.Zero;
    private float _fadeAlpha = 0f;
    private DateTime _lastHoverTime = DateTime.MinValue;
    private readonly TimeSpan _fadeDelay = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _fadeDuration = TimeSpan.FromMilliseconds(200);

    public HoverToolbar(ThemeManager themeManager, UiSharedService uiSharedService)
    {
        _themeManager = themeManager;
        _uiSharedService = uiSharedService;
    }

    public void Update(Vector2 windowPos, Vector2 windowSize, bool isWindowHovered)
    {
        var currentTime = DateTime.Now;
        
        if (isWindowHovered)
        {
            _lastHoverTime = currentTime;
            _isVisible = true;
        }

        var timeSinceHover = currentTime - _lastHoverTime;
        if (timeSinceHover > _fadeDelay)
        {
            var fadeProgress = (float)Math.Min(1.0, (timeSinceHover - _fadeDelay).TotalMilliseconds / _fadeDuration.TotalMilliseconds);
            _fadeAlpha = 1f - fadeProgress;
            
            if (_fadeAlpha <= 0f)
            {
                _isVisible = false;
            }
        }
        else
        {
            _fadeAlpha = 1f;
        }

        var toolbarSize = CalculateToolbarSize();
        _position = new Vector2(
            windowPos.X + windowSize.X - toolbarSize.X - 10f,
            windowPos.Y + 5f
        );
        _size = toolbarSize;
    }

    public void Draw()
    {
        if (!_isVisible || _fadeAlpha <= 0f) return;

        var theme = _themeManager.CurrentTheme;
        
        ImGui.SetNextWindowPos(_position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(_size, ImGuiCond.Always);
        
        var flags = ImGuiWindowFlags.NoDecoration | 
                   ImGuiWindowFlags.NoMove | 
                   ImGuiWindowFlags.NoResize | 
                   ImGuiWindowFlags.NoSavedSettings |
                   ImGuiWindowFlags.NoFocusOnAppearing |
                   ImGuiWindowFlags.NoBringToFrontOnFocus |
                   ImGuiWindowFlags.AlwaysAutoResize;

        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, _fadeAlpha))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, theme.WindowRounding))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(8f, 6f)))
        using (ImRaii.PushColor(ImGuiCol.WindowBg, theme.Surface))
        using (ImRaii.PushColor(ImGuiCol.Border, theme.Border))
        {
            if (ImGui.Begin("###HoverToolbar", flags))
            {
                DrawToolbarContent();
            }
            ImGui.End();
        }
    }

    private void DrawToolbarContent()
    {
        var theme = _themeManager.CurrentTheme;
        var buttonSize = new Vector2(24f, 24f);
        var spacing = 4f;

        using (ImRaii.PushColor(ImGuiCol.Button, theme.Primary with { W = 0.6f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, theme.Primary with { W = 0.8f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, theme.Primary))
        {
            if (DrawToolbarButton(FontAwesomeIcon.WindowMinimize, buttonSize, "Minimize"))
            {
                // Handle minimize
            }

            ImGui.SameLine(0, spacing);

            if (DrawToolbarButton(FontAwesomeIcon.Cog, buttonSize, "Settings"))
            {
                // Handle settings
            }

            ImGui.SameLine(0, spacing);

            if (DrawToolbarButton(FontAwesomeIcon.Palette, buttonSize, "Themes"))
            {
                // Handle theme selection
            }

            ImGui.SameLine(0, spacing);

            var pinIcon = _isVisible ? FontAwesomeIcon.Thumbtack : FontAwesomeIcon.Times;
            if (DrawToolbarButton(pinIcon, buttonSize, "Pin Toolbar"))
            {
                // Toggle toolbar pinning
            }
        }
    }

    private bool DrawToolbarButton(FontAwesomeIcon icon, Vector2 size, string tooltip)
    {
        using (_uiSharedService.IconFont.Push())
        {
            var result = ImGui.Button(icon.ToIconString(), size);
            
            if (ImGui.IsItemHovered())
            {
                _lastHoverTime = DateTime.Now;
                UiSharedService.AttachToolTip(tooltip);
            }
            
            return result;
        }
    }

    private Vector2 CalculateToolbarSize()
    {
        var buttonSize = new Vector2(24f, 24f);
        var spacing = 4f;
        var buttonCount = 4f;
        var padding = new Vector2(8f, 6f) * 2f;

        return new Vector2(
            (buttonSize.X * buttonCount) + (spacing * (buttonCount - 1)) + padding.X,
            buttonSize.Y + padding.Y
        );
    }

    public void SetPinned(bool pinned)
    {
        if (pinned)
        {
            _fadeAlpha = 1f;
            _isVisible = true;
            _lastHoverTime = DateTime.MaxValue;
        }
        else
        {
            _lastHoverTime = DateTime.Now;
        }
    }
}