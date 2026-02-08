using Microsoft.Extensions.Logging;
using Dalamud.Bindings.ImGui;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.ServerConfiguration;
namespace MareSynchronos.UI;

public class PairingRequestsUi : WindowMediatorSubscriberBase
{
    private readonly PairRequestManager _pairRequestManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private readonly UiTheme _theme;

    public PairingRequestsUi(ILogger<PairingRequestsUi> logger, MareMediator mediator,
        PerformanceCollectorService performanceCollectorService, UiSharedService uiSharedService,
        PlayerPerformanceConfigService playerPerformanceConfig, UiTheme theme, PairRequestManager pairRequestManager, ServerConfigurationManager serverConfigurationManager)
        : base(logger, mediator, "PlayerSync Pair Requests", performanceCollectorService)
    {
        _pairRequestManager = pairRequestManager;
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
        _theme = theme;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => IsOpen = false);

        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 1000,
                Y = 800
            },
            MaximumSize = new()
            {
                X = 1000,
                Y = 800
            }
        };
    }

    protected override void DrawInternal()
    {
        using var _ = _theme.PushWindowStyle();

        DrawPendingRequests();
    }

    private void DrawPendingRequests()
    {
        var pendingIncomingRequests = _pairRequestManager.GetReceivedPendingRequests();
        var pendingOutgoingRequests = _pairRequestManager.GetSentPendingRequests();

        if (ImGui.BeginTable("##PendingPairRequests", 4,
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Requestor UID");
            ImGui.TableSetupColumn("Requestor Name");
            ImGui.TableSetupColumn("Recipient Name");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            foreach (var request in pendingIncomingRequests)
            {
                var requestorUid = request.Requestor.UID;
                var requestorIdent = request.RequestTargetIdent;
                var requestorName = _serverConfigurationManager.GetPendingRequestNameForIdent(request.RequestorIdent) ?? "Unknown";
                var recipientName = _serverConfigurationManager.GetPendingRequestNameForIdent(request.RequestTargetIdent) ?? "Unknown";

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(requestorUid);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(requestorName);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(recipientName);

                ImGui.TableNextColumn();

                if (ImGui.Button($"Accept##{requestorIdent}"))
                {
                    _pairRequestManager.SendPairRequest(requestorIdent);
                }

                ImGui.SameLine();

                if (ImGui.Button($"Deny##{requestorUid}"))
                {
                    _pairRequestManager.SendPairRejection(new(requestorUid));
                }

                ImGui.SameLine();

                if (ImGui.Button($"Ignore Player##{requestorUid}"))
                {
                    _pairRequestManager.SendPairRejection(new(requestorUid));
                    _serverConfigurationManager.AddPairingRequestBlacklistUid(requestorUid);
                }
            }

            foreach (var request in pendingOutgoingRequests)
            {
                var requestorUid = request.Requestor.UID;
                var requestorName = _serverConfigurationManager.GetPendingRequestNameForIdent(request.RequestorIdent) ?? "Unknown";
                var recipientName = _serverConfigurationManager.GetPendingRequestNameForIdent(request.RequestTargetIdent) ?? "Unknown";

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(requestorUid);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(requestorName);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(recipientName);

                ImGui.TableNextColumn();

                if (ImGui.Button($"Cancel##{requestorUid}"))
                {
                    _pairRequestManager.SendPairRejection(new(requestorUid));
                }
            }

            ImGui.EndTable();
        }
    }
}
