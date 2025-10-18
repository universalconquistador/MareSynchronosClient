using Dalamud.Bindings.ImGui;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class SyncshellProfileUi : WindowMediatorSubscriberBase
{
    private readonly UiSharedService _uiSharedService;

    public SyncshellProfileUi(ILogger<SyncshellProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        GroupFullInfoDto groupFullInfo, PerformanceCollectorService performanceCollector)
        : base(logger, mediator, "Syncshell Profile for " + groupFullInfo.GroupAliasOrGID + "##PlayerSyncshellProfileUI", performanceCollector)
    {
        _uiSharedService = uiBuilder;
        GroupFullInfo = groupFullInfo;

        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;

        var spacing = ImGui.GetStyle().ItemSpacing;

        Size = new(512 + spacing.X * 3 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 512);

        IsOpen = true;
    }

    public GroupFullInfoDto GroupFullInfo { get; private set; }

    protected override void DrawInternal()
    {

    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}