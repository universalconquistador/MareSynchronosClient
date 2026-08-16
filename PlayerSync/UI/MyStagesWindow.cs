using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
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
        FindById
    }

    private readonly UiTheme _uiTheme;
    private readonly UiSharedService _uiSharedService;
    private readonly ApiController _apiController;

    private readonly IReadOnlyList<UiNav.Tab<MyStagesTabs>> _tabs;
    private readonly StageListComponent _myStageList;

    private UiNav.Tab<MyStagesTabs>? _selectedTab = null;

    public MyStagesWindow(ILogger<MyStagesWindow> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService, UiTheme uiTheme, UiSharedService uiSharedService, ApiController apiController, PairManager pairManager, IdDisplayHandler idDisplayHandler)
        : base(logger, mediator, "My Stages", performanceCollectorService)
    {
        _uiTheme = uiTheme;
        _uiSharedService = uiSharedService;
        _apiController = apiController;

        _tabs =
        [
            new(MyStagesTabs.Stages, "Stages", DrawStagesTab, FontAwesomeIcon.MapMarkedAlt),
            new(MyStagesTabs.Subscriptions, "Subscriptions", DrawSubscriptionsTab, FontAwesomeIcon.Tasks),
            new(MyStagesTabs.FindById, "Find", DrawFindByIdTab, FontAwesomeIcon.Search),
        ];
        _selectedTab = _tabs[0];
        _myStageList = new(logger, mediator, apiController, pairManager, idDisplayHandler, page => apiController.StageListForUser(apiController.UID, page));

        SizeConstraints = new()
        {
            MinimumSize = new(400.0f, 300.0f),
        };
        Size = new(500.0f, 400.0f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _myStageList.LoadPage(_myStageList.PageIndex);
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
    }

    private void DrawFindByIdTab()
    {
        _uiSharedService.BigText("Find");
        ImGuiHelpers.ScaledDummy(2);
    }
}
