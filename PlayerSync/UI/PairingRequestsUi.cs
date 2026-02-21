using Microsoft.Extensions.Logging;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.API.Data;
using System.Numerics;

namespace MareSynchronos.UI;

public class PairingRequestsUi : WindowMediatorSubscriberBase
{
    private readonly PairRequestManager _pairRequestManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiTheme _theme;
    private bool _userClickedSomething = false;

    public PairingRequestsUi(ILogger<PairingRequestsUi> logger, MareMediator mediator,
        PerformanceCollectorService performanceCollectorService, PlayerPerformanceConfigService playerPerformanceConfig,
        UiTheme theme, PairRequestManager pairRequestManager, ServerConfigurationManager serverConfigurationManager)
        : base(logger, mediator, "PlayerSync Pair Requests", performanceCollectorService)
    {
        _pairRequestManager = pairRequestManager;
        _serverConfigurationManager = serverConfigurationManager;
        _theme = theme;

        Flags = ImGuiWindowFlags.NoResize;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => IsOpen = false);

        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 600,
                Y = 300
            },
            MaximumSize = new()
            {
                X = 600,
                Y = 300
            }
        };
    }

    protected override void DrawInternal()
    {
        // basically close the window if the user accepted/rejected the final requets
        // but don't close if they just toggle open the empty window manually
        if (_pairRequestManager.ReceivedPendingCount == 0 && _userClickedSomething)
            IsOpen = false;

        using var windowStyle = _theme.PushWindowStyle();
        DrawPendingRequests();
    }

    private void DrawPendingRequests()
    {
        var pendingIncomingRequests = _pairRequestManager.GetReceivedPendingRequests();

        var style = ImGui.GetStyle();
        float globalScale = ImGuiHelpers.GlobalScale;

        // UID is max 10 chars; worst-case "W" width
        float uidColumnWidth =
            ImGui.CalcTextSize(new string('W', 10)).X +
            style.CellPadding.X * 2f +
            12f * globalScale;

        // Actions width: compute from rendered button labels
        float acceptButtonWidth = ImGui.CalcTextSize("Accept").X + style.FramePadding.X * 2f;
        float dismissButtonWidth = ImGui.CalcTextSize("Dismiss").X + style.FramePadding.X * 2f;
        float ignoreButtonWidth = ImGui.CalcTextSize("Ignore Player").X + style.FramePadding.X * 2f;

        float actionsColumnWidth =
            acceptButtonWidth +
            style.ItemSpacing.X +
            dismissButtonWidth +
            style.ItemSpacing.X +
            ignoreButtonWidth +
            style.CellPadding.X * 2f +
            10f * globalScale;

        // Match other ModernUi tables
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(8f * globalScale, 4f * globalScale));

        float tableHeight = MathF.Max(0f, ImGui.GetContentRegionAvail().Y);

        using var table = ImRaii.Table("##PendingPairRequests", 3,
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit |
            ImGuiTableFlags.NoHostExtendX,
            new Vector2(0, tableHeight));

        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);

        ImGui.TableSetupColumn("Requestor UID", ImGuiTableColumnFlags.WidthFixed, uidColumnWidth);
        ImGui.TableSetupColumn("Requestor Name", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, actionsColumnWidth);

        ImGui.TableHeadersRow();

        foreach (var request in pendingIncomingRequests)
        {
            float rowStartHeightStart = ImGui.GetCursorPosY();

            var requestorUid = request.Requestor.UID;
            var requestorName = _serverConfigurationManager.GetPendingRequestNameForIdent(request.RequestorIdent) ?? "Unknown";

            ImGui.TableNextRow();

            // UID
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(requestorUid);

            // Name
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(requestorName);

            // Actions
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();

            if (ImGui.Button($"Accept##{requestorUid}"))
            {
                _pairRequestManager.SendPairRequest(new UserData(requestorUid));
                _userClickedSomething = true;
            }

            ImGui.SameLine();

            if (ImGui.Button($"Dismiss##{requestorUid}"))
            {
                _pairRequestManager.SendPairRejection(new UserData(requestorUid));
                _userClickedSomething = true;
            }
            UiSharedService.AttachToolTip("Silently drop this request without notifying the player who requested to pair.");

            ImGui.SameLine();

            if (ImGui.Button($"Ignore Player##{requestorUid}"))
            {
                _pairRequestManager.SendPairRejection(new UserData(requestorUid));
                _serverConfigurationManager.AddPairingRequestBlacklistUid(requestorUid);
                _userClickedSomething = true;
            }
            UiSharedService.AttachToolTip("Ignoring a player will block their requests to you." + UiSharedService.TooltipSeparator +
                "Unblock requests in Settings -> Sync Settings -> Pair Requests.");

            // Row hover highlight (same behavior as PlayerAnalysisViewerUI)
            if (TableHelper.SRowhovered(rowStartHeightStart, ImGui.GetCursorPosY()))
            {
                var rowIndex = ImGui.TableGetRowIndex();
                var color = ImGui.GetColorU32(ImGuiCol.HeaderHovered);
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, color, rowIndex);
            }
        }
    }

    public override void OnClose()
    {
        _userClickedSomething = false;

        base.OnClose();
    }
}
