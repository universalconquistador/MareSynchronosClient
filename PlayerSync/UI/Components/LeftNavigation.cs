using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Themes;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.SignalR.Utils;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public class LeftNavigation : IMediatorSubscriber
{
    private readonly ApiController _apiController;
    private readonly MareMediator _mareMediator;
    private readonly PairManager _pairManager;
    private readonly IBroadcastManager _broadcastManager;
    private readonly UiSharedService _uiSharedService;
    private readonly MareConfigService _mareConfigService;
    private readonly ThemeManager _themeManager;
    private string _filter = string.Empty;
    private int _globalControlCountdown = 0;
    private string _pairToAdd = string.Empty;
    private SelectedTab _selectedTab = SelectedTab.None;
    
    MareMediator IMediatorSubscriber.Mediator => _mareMediator;

    public LeftNavigation(MareMediator mareMediator, ApiController apiController, PairManager pairManager, 
        IBroadcastManager broadcastManager, UiSharedService uiSharedService, MareConfigService mareConfigService, 
        ThemeManager themeManager)
    {
        _mareMediator = mareMediator;
        _apiController = apiController;
        _pairManager = pairManager;
        _broadcastManager = broadcastManager;
        _uiSharedService = uiSharedService;
        _mareConfigService = mareConfigService;
        _themeManager = themeManager;
    }

    private enum SelectedTab
    {
        None,
        Individual,
        Syncshell,
        Filter,
        Broadcast,
        UserConfig,
        Themes
    }

    public string Filter
    {
        get => _filter;
        private set
        {
            if (!string.Equals(_filter, value, StringComparison.OrdinalIgnoreCase))
            {
                _mareMediator.Publish(new RefreshUiMessage());
            }
            _filter = value;
        }
    }

    private SelectedTab TabSelection
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == SelectedTab.Filter && value != SelectedTab.Filter)
            {
                Filter = string.Empty;
            }
            _selectedTab = value;
        }
    }

    public void Draw(float navWidth)
    {
        var theme = _themeManager.CurrentTheme;
        
        using var navChild = ImRaii.Child("LeftNavigation", new Vector2(navWidth, -1), true);
        if (!navChild) return;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, theme.NavBackground))
        {
            DrawNavigationHeader();
            ImGui.Separator();
            DrawNavigationItems();
            ImGui.Separator();
            DrawTabContent();
        }
    }

    private void DrawNavigationHeader()
    {
        var theme = _themeManager.CurrentTheme;
        
        using (_uiSharedService.UidFont.Push())
        {
            var headerText = "Mare Sync";
            var textSize = ImGui.CalcTextSize(headerText);
            var availWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX((availWidth - textSize.X) * 0.5f);
            
            using (ImRaii.PushColor(ImGuiCol.Text, theme.Text))
            {
                ImGui.TextUnformatted(headerText);
            }
        }
        
        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawNavigationItems()
    {
        var theme = _themeManager.CurrentTheme;
        var itemHeight = ImGui.GetFrameHeight() * 1.2f;
        var availWidth = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ScrollbarSize;

        var navItems = new[]
        {
            (SelectedTab.Individual, FontAwesomeIcon.User, "Individual"),
            (SelectedTab.Syncshell, FontAwesomeIcon.Users, "Syncshell"),
            (SelectedTab.Filter, FontAwesomeIcon.Filter, "Filter"),
            (SelectedTab.Broadcast, FontAwesomeIcon.Wifi, "Broadcast"),
            (SelectedTab.UserConfig, FontAwesomeIcon.UserCog, "User Config"),
            (SelectedTab.Themes, FontAwesomeIcon.Palette, "Themes")
        };

        foreach (var (tab, icon, label) in navItems)
        {
            DrawNavigationItem(tab, icon, label, availWidth, itemHeight, theme);
        }
    }

    private void DrawNavigationItem(SelectedTab tab, FontAwesomeIcon icon, string label, float width, float height, Theme theme)
    {
        var isSelected = TabSelection == tab;
        var isHovered = false;

        var cursorPos = ImGui.GetCursorScreenPos();
        var itemRect = new Vector2(cursorPos.X, cursorPos.Y);
        var itemRectMax = new Vector2(cursorPos.X + width, cursorPos.Y + height);

        ImGui.SetCursorScreenPos(cursorPos);
        ImGui.InvisibleButton($"##nav_{tab}", new Vector2(width, height));
        
        if (ImGui.IsItemClicked())
        {
            TabSelection = TabSelection == tab ? SelectedTab.None : tab;
        }
        
        isHovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var backgroundColor = isSelected 
            ? theme.NavItemActive 
            : isHovered 
                ? theme.NavItemHover 
                : Vector4.Zero;

        if (backgroundColor != Vector4.Zero)
        {
            drawList.AddRectFilled(itemRect, itemRectMax, 
                ImGui.ColorConvertFloat4ToU32(backgroundColor), theme.FrameRounding);
        }

        if (isSelected)
        {
            var indicatorWidth = 3f;
            drawList.AddRectFilled(
                itemRect,
                new Vector2(itemRect.X + indicatorWidth, itemRectMax.Y),
                ImGui.ColorConvertFloat4ToU32(theme.Primary));
        }

        var iconColor = isSelected ? theme.Primary : 
                       isHovered ? theme.Text : theme.TextSecondary;
        
        var textColor = isSelected ? theme.Text : theme.TextSecondary;

        using (_uiSharedService.IconFont.Push())
        {
            var iconSize = ImGui.CalcTextSize(icon.ToIconString());
            var iconPos = new Vector2(
                itemRect.X + ImGui.GetStyle().FramePadding.X * 2,
                itemRect.Y + (height - iconSize.Y) * 0.5f
            );
            
            drawList.AddText(iconPos, ImGui.ColorConvertFloat4ToU32(iconColor), icon.ToIconString());
        }

        var textSize = ImGui.CalcTextSize(label);
        var textPos = new Vector2(
            itemRect.X + ImGui.GetStyle().FramePadding.X * 2 + 25f * ImGuiHelpers.GlobalScale,
            itemRect.Y + (height - textSize.Y) * 0.5f
        );
        
        drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), label);

        if (tab == SelectedTab.Broadcast && _broadcastManager.IsBroadcasting())
        {
            var statusColor = theme.Success;
            var statusSize = 6f;
            var statusPos = new Vector2(
                itemRectMax.X - statusSize - ImGui.GetStyle().FramePadding.X,
                itemRect.Y + (height - statusSize) * 0.5f
            );
            
            drawList.AddCircleFilled(statusPos, statusSize * 0.5f, 
                ImGui.ColorConvertFloat4ToU32(statusColor));
        }

        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + height));
    }

    private void DrawTabContent()
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacingX = ImGui.GetStyle().ItemSpacing.X;

        switch (TabSelection)
        {
            case SelectedTab.Individual:
                DrawAddPair(availableWidth, spacingX);
                break;
            case SelectedTab.Syncshell:
                DrawSyncshellMenu(availableWidth, spacingX);
                break;
            case SelectedTab.Filter:
                DrawFilter(availableWidth, spacingX);
                break;
            case SelectedTab.Broadcast:
                DrawBroadcast(availableWidth, spacingX);
                break;
            case SelectedTab.UserConfig:
                DrawUserConfig(availableWidth, spacingX);
                break;
            case SelectedTab.Themes:
                DrawThemes(availableWidth, spacingX);
                break;
        }
    }

    private void DrawAddPair(float availableXWidth, float spacingX)
    {
        var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.UserPlus, "Add");
        ImGui.SetNextItemWidth(availableXWidth - buttonSize - spacingX);
        ImGui.InputTextWithHint("##otheruid", "Other players UID/Alias", ref _pairToAdd, 20);
        
        var alreadyExisting = _pairManager.DirectPairs.Exists(p => 
            string.Equals(p.UserData.UID, _pairToAdd, StringComparison.Ordinal) || 
            string.Equals(p.UserData.Alias, _pairToAdd, StringComparison.Ordinal));
            
        using (ImRaii.Disabled(alreadyExisting || string.IsNullOrEmpty(_pairToAdd)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserPlus, "Add"))
            {
                _ = _apiController.UserAddPair(new(new(_pairToAdd)), false);
                _pairToAdd = string.Empty;
            }
        }
        UiSharedService.AttachToolTip("Pair with " + (string.IsNullOrEmpty(_pairToAdd) ? "other user" : _pairToAdd));
    }

    private void DrawFilter(float availableWidth, float spacingX)
    {
        var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Ban, "Clear");
        ImGui.SetNextItemWidth(availableWidth - buttonSize - spacingX);
        string filter = Filter;
        if (ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref filter, 255))
        {
            Filter = filter;
        }
        ImGui.SameLine();
        using var disabled = ImRaii.Disabled(string.IsNullOrEmpty(Filter));
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Clear"))
        {
            Filter = string.Empty;
        }
    }

    private void DrawBroadcast(float availableXWidth, float spacingX)
    {
        bool showBroadcastingSyncshells = _mareConfigService.Current.ListenForBroadcasts;
        if (ImGui.Checkbox("Show broadcasting Syncshells", ref showBroadcastingSyncshells))
        {
            if (showBroadcastingSyncshells)
            {
                _broadcastManager.StartListening();
            }
            else
            {
                _broadcastManager.StopListening();
            }
            _mareConfigService.Current.ListenForBroadcasts = showBroadcastingSyncshells;
            _mareConfigService.Save();
        }
    }

    private void DrawSyncshellMenu(float availableWidth, float spacingX)
    {
        var buttonX = (availableWidth - spacingX) / 2f;

        using (ImRaii.Disabled(_pairManager.GroupPairs.Select(k => k.Key).Distinct()
            .Count(g => string.Equals(g.OwnerUID, _apiController.UID, StringComparison.Ordinal)) >= _apiController.ServerInfo.MaxGroupsCreatedByUser))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Create new Syncshell", buttonX))
            {
                _mareMediator.Publish(new UiToggleMessage(typeof(CreateSyncshellUI)));
            }
            ImGui.SameLine();
        }

        using (ImRaii.Disabled(_pairManager.GroupPairs.Select(k => k.Key).Distinct().Count() >= _apiController.ServerInfo.MaxGroupsJoinedByUser))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Users, "Join existing Syncshell", buttonX))
            {
                _mareMediator.Publish(new UiToggleMessage(typeof(JoinSyncshellUI)));
            }
        }
    }

    private void DrawUserConfig(float availableWidth, float spacingX)
    {
        var buttonX = (availableWidth - spacingX) / 2f;
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserCircle, "Edit Player Sync Profile", buttonX))
        {
            _mareMediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
        }
        UiSharedService.AttachToolTip("Edit your Player Sync Profile");
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.PersonCircleQuestion, "Chara Data Analysis", buttonX))
        {
            _mareMediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }
        UiSharedService.AttachToolTip("View and analyze your generated character data");
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Running, "Character Data Hub", availableWidth))
        {
            _mareMediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
    }

    private void DrawThemes(float availableWidth, float spacingX)
    {
        ImGui.TextUnformatted("Current Theme:");
        
        var currentTheme = _themeManager.CurrentTheme.Name;
        if (ImGui.BeginCombo("##ThemeSelector", currentTheme))
        {
            foreach (var theme in _themeManager.AvailableThemes)
            {
                bool isSelected = theme.Key == currentTheme;
                if (ImGui.Selectable(theme.Key, isSelected))
                {
                    _themeManager.SetTheme(theme.Key);
                }
            }
            ImGui.EndCombo();
        }

        ImGuiHelpers.ScaledDummy(4f);
        
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Edit, "Theme Editor", availableWidth))
        {
            _mareMediator.Publish(new UiToggleMessage(typeof(ThemeEditorUi)));
        }
        UiSharedService.AttachToolTip("Open the theme editor to customize themes");
    }
}