using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace MareSynchronos.UI;

public class MyStagesWindow : WindowMediatorSubscriberBase
{
    private enum MyStagesTabs
    {
        Stages,
        Subscriptions,
        FindById,
    }

    private enum FindType
    {
        Stage,
        User,
        Group,
    }

    private readonly UiTheme _uiTheme;
    private readonly UiSharedService _uiSharedService;
    private readonly ApiController _apiController;
    private readonly PairManager _pairManager;
    private readonly IdDisplayHandler _idDisplayHandler;

    private readonly IReadOnlyList<UiNav.Tab<MyStagesTabs>> _tabs;
    private UiNav.Tab<MyStagesTabs>? _selectedTab = null;
    
    // Stages
    private readonly StageListComponent _myStageList;

    // Subscriptions
    private readonly StageListComponent _subscribedStageList;

    // Find
    private FindType _selectedFindType = FindType.Stage;
    private string _findText = "";
    private StageListComponent? _findStageList = null;

    public MyStagesWindow(ILogger<MyStagesWindow> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService, UiTheme uiTheme, UiSharedService uiSharedService, ApiController apiController, PairManager pairManager, IdDisplayHandler idDisplayHandler)
        : base(logger, mediator, "Stages", performanceCollectorService)
    {
        _uiTheme = uiTheme;
        _uiSharedService = uiSharedService;
        _apiController = apiController;
        _pairManager = pairManager;
        _idDisplayHandler = idDisplayHandler;

        _tabs =
        [
            new(MyStagesTabs.Stages, "My Stages", DrawStagesTab, FontAwesomeIcon.MapMarkedAlt),
            new(MyStagesTabs.Subscriptions, "Subscriptions", DrawSubscriptionsTab, FontAwesomeIcon.Tasks),
            new(MyStagesTabs.FindById, "Find Stages", DrawFindByIdTab, FontAwesomeIcon.Search),
        ];
        _selectedTab = _tabs[0];
        _myStageList = new(logger, mediator, apiController, pairManager, idDisplayHandler, _uiSharedService, page => apiController.StageListForUser(apiController.UID, page));
        _subscribedStageList = new(logger, mediator, apiController, pairManager, idDisplayHandler, _uiSharedService, page => apiController.StageListSubscribed(page));

        SizeConstraints = new()
        {
            MinimumSize = new(400.0f, 300.0f),
        };
        Size = new(500.0f, 400.0f);
        SizeCondition = ImGuiCond.FirstUseEver;

        Mediator.Subscribe<ShowStageWithIdMessage>(this, message =>
        {
            _findText = message.StageId;
            _selectedFindType = FindType.Stage;
            _selectedTab = _tabs.First(tab => tab.Id == MyStagesTabs.FindById);
            FindByStageId(message.StageId);
            IsOpen = true;
        });
        Mediator.Subscribe<ShowStagesForUserMessage>(this, message =>
        {
            _findText = message.UidOrAlias;
            _selectedFindType = FindType.User;
            _selectedTab = _tabs.First(tab => tab.Id == MyStagesTabs.FindById);
            FindByUserId(message.UidOrAlias);
            IsOpen = true;
        });
        Mediator.Subscribe<ShowStagesForGroupMessage>(this, message =>
        {
            _findText = message.GidOrAlias;
            _selectedFindType = FindType.Group;
            _selectedTab = _tabs.First(tab => tab.Id == MyStagesTabs.FindById);
            FindByGroupId(message.GidOrAlias);
            IsOpen = true;
        });
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _myStageList.LoadPage(_myStageList.PageIndex);
        _subscribedStageList.LoadPage(_subscribedStageList.PageIndex);
        _findStageList?.LoadPage(_findStageList.PageIndex);
    }

    protected override void DrawInternal()
    {
        var newTab = UiNav.DrawTabsUnderline(_uiTheme, _tabs, _selectedTab, _uiSharedService.IconFont);
        if (newTab != _selectedTab)
        {
            _selectedTab = newTab;

            switch (newTab.Id)
            {
                case MyStagesTabs.Stages:
                    // Reload the stages list when switching to its tab
                    if (_apiController.IsConnected)
                    {
                        _myStageList.LoadPage(_myStageList.PageIndex);
                    }
                    break;
                case MyStagesTabs.Subscriptions:
                    if (_apiController.IsConnected)
                    {
                        _subscribedStageList.LoadPage(_subscribedStageList.PageIndex);
                    }
                    break;
                case MyStagesTabs.FindById:
                    if (_apiController.IsConnected && _findStageList != null)
                    {
                        _findStageList.LoadPage(_findStageList.PageIndex);
                    }
                    break;
            }
        }

        _selectedTab.TabAction.Invoke();
    }

    private void DrawStagesTab()
    {
        _uiSharedService.BigText("My Stages");
        ImGuiHelpers.ScaledDummy(2);

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "New Stage"))
        {
            Mediator.Publish(new OpenStageDetailsWindow(null, null));
        }

        ImGuiHelpers.ScaledDummy(1);

        using (var stageList = ImRaii.Child("##MyStageList", ImGui.GetContentRegionAvail()))
        {
            if (stageList.Success)
            {
                _myStageList.Draw();
            }
        }
    }

    private void DrawSubscriptionsTab()
    {
        _uiSharedService.BigText("My Subscriptions");
        ImGuiHelpers.ScaledDummy(2);

        using (var stageList = ImRaii.Child("StageList"u8, ImGui.GetContentRegionAvail()))
        {
            _subscribedStageList.Draw();
        }
    }

    private static string FindTypeToString(FindType findType) => findType switch
    {
        FindType.User => "Player",
        FindType.Stage => "Stage",
        FindType.Group => "Syncshell",
        _ => "???"
    };

    private void DrawFindByIdTab()
    {
        _uiSharedService.BigText($"Find {(_selectedFindType == FindType.Stage ? "" : "by ")}{FindTypeToString(_selectedFindType)}");
        ImGuiHelpers.ScaledDummy(2);

        ImGui.SetNextItemWidth(100.0f * ImGuiHelpers.GlobalScale);
        using (var searchTypeCombo = ImRaii.Combo("###SearchTypeCombo"u8, FindTypeToString(_selectedFindType)))
        {
            if (searchTypeCombo.Success)
            {
                if (ImGui.Selectable(FindType.Stage.ToString(), _selectedFindType == FindType.Stage) && _selectedFindType != FindType.Stage)
                {
                    _selectedFindType = FindType.Stage;
                    _findText = "";
                    _findStageList = null;
                }
                if (ImGui.Selectable(FindType.User.ToString(), _selectedFindType == FindType.User) && _selectedFindType != FindType.User)
                {
                    _selectedFindType = FindType.User;
                    _findText = "";
                    _findStageList = null;
                }
                if (ImGui.Selectable(FindTypeToString(FindType.Group), _selectedFindType == FindType.Group) && _selectedFindType != FindType.Group)
                {
                    _selectedFindType = FindType.Group;
                    _findText = "";
                    _findStageList = null;
                }
            }
        }

        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X);
        bool doSearch = false;
        if (ImGui.InputTextWithHint("###SearchText"u8, _selectedFindType switch { FindType.Stage => "Stage ID", FindType.User => "Player ID or Vanity", FindType.Group => "Syncshell ID or Vanity", _ => "ID" }, ref _findText, flags: ImGuiInputTextFlags.EnterReturnsTrue))
        {
            doSearch = true;
        }
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Search, new (ImGui.GetFrameHeight() / ImGuiHelpers.GlobalScale)))
        {
            doSearch = true;
        }

        if (doSearch && _findText != "")
        {
            switch (_selectedFindType)
            {
                case FindType.Stage:
                    FindByStageId(_findText);
                    break;
                case FindType.User:
                    FindByUserId(_findText);
                    break;
                case FindType.Group:
                    FindByGroupId(_findText);
                    break;
            }
        }

        if (_findStageList != null)
        {
            using (var stageList = ImRaii.Child("StageList"u8, ImGui.GetContentRegionAvail()))
            {
                _findStageList.Draw();
            }
        }
    }

    private void FindByStageId(string stageId)
    {
        _findStageList = new(_logger, Mediator, _apiController, _pairManager, _idDisplayHandler, _uiSharedService, async page =>
        {
            var stageInfo = await _apiController.StageGetInfo(stageId).ConfigureAwait(false);
            if (stageInfo != null)
            {
                return (new List<StageFullInfoDto>() { stageInfo }, false);
            }
            else
            {
                return (new List<StageFullInfoDto>(), false);
            }
        });
    }

    private void FindByUserId(string uidOrAlias)
    {
        _findStageList = new(_logger, Mediator, _apiController, _pairManager, _idDisplayHandler, _uiSharedService, page => _apiController.StageListForUser(uidOrAlias, page));
    }

    private void FindByGroupId(string gidOrAlias)
    {
        _findStageList = new(_logger, Mediator, _apiController, _pairManager, _idDisplayHandler, _uiSharedService, page => _apiController.StageListForGroup(gidOrAlias, page));
    }
}
