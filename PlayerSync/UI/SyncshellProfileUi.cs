using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MareSynchronos.UI;

public class SyncshellProfileUi : WindowMediatorSubscriberBase
{
    private readonly UiSharedService _uiSharedService;
    private Memory<byte> _rulesBuffer = new byte[2000];
    private Memory<byte> _descriptionBuffer = new byte[2000];

    public SyncshellProfileUi(ILogger<SyncshellProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        GroupFullInfoDto groupFullInfo, PerformanceCollectorService performanceCollector)
        : base(logger, mediator, "Syncshell Profile for " + groupFullInfo.GroupAliasOrGID, performanceCollector)
    {
        _uiSharedService = uiBuilder;
        GroupFullInfo = groupFullInfo;

        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;

        var spacing = ImGui.GetStyle().ItemSpacing;

        Size = new(512 + spacing.X * 3 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 512);

        IsOpen = true;

        Encoding.UTF8.GetBytes(GroupFullInfo.PublicData.GroupProfile?.Description ?? "", _descriptionBuffer.Span);
        Encoding.UTF8.GetBytes(GroupFullInfo.PublicData.GroupProfile?.Rules ?? "", _rulesBuffer.Span);
    }

    public GroupFullInfoDto GroupFullInfo { get; private set; }

    protected override void DrawInternal()
    {
        DrawProfileInfo();
    }

    private void DrawProfileInfo()
    {
        if (GroupFullInfo.PublicData.GroupProfile == null)
        {
            _uiSharedService.HeaderText("This Syncshell has no profile associated.");
            return;
        }
        _uiSharedService.HeaderText("Syncshell Description");
        ImGuiHelpers.ScaledDummy(2f);
        ImGui.TextWrapped(GroupFullInfo.PublicData.GroupProfile.Description);
        ImGuiHelpers.ScaledDummy(4f);
        _uiSharedService.HeaderText("Syncshell Rules");
        ImGuiHelpers.ScaledDummy(2f);
        ImGui.TextWrapped(GroupFullInfo.PublicData.GroupProfile.Rules);
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}