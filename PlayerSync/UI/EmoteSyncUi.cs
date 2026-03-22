using Dalamud.Bindings.ImGui;
using MareSynchronos.API.Data;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.EmoteSync;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class EmoteSyncUi : WindowMediatorSubscriberBase
{
    private readonly UiSharedService _uiSharedService;
    private readonly UiTheme _theme;
    private readonly EmoteSyncManagerService _emoteSync;
    private readonly PairManager _pairManager;
    private readonly DalamudUtilService _dalamudUtilService;

    public EmoteSyncUi(ILogger<EmoteSyncUi> logger, MareMediator mediator, UiSharedService uiSharedService,
        PerformanceCollectorService performanceCollectorService, UiTheme theme, EmoteSyncManagerService emoteSyncManagerService, 
        PairManager pairManager, DalamudUtilService dalamudUtilService)
        : base(logger, mediator, "PlayerSync Diagnostics", performanceCollectorService)
    {
        
        _uiSharedService = uiSharedService;
        _theme = theme;
        _emoteSync = emoteSyncManagerService;
        _pairManager = pairManager;
        _dalamudUtilService = dalamudUtilService;

        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 600,
                Y = 400
            },
            MaximumSize = new()
            {
                X = 600,
                Y = 400
            }
        };

        Flags |= ImGuiWindowFlags.NoResize;
    }

    public override void OnOpen()
    {
        base.OnOpen();

        if (_emoteSync.CurrentGroupId != null)
            return;

        if (_emoteSync.IsTimeSyncEnabled)
            return;

        _ = SendEmoteSyncJoin();
    }

    private Task SendEmoteSyncJoin()
    {
        var visibleAllianceAndPartyMembers = _dalamudUtilService.GetVisibleAllianceAndPartyMembers() ?? [];
        var visibleAllianceAndPartyMemberSet = new HashSet<string>(visibleAllianceAndPartyMembers, StringComparer.OrdinalIgnoreCase);

        List<UserData> allVisible = _pairManager.GetVisibleUsers();
        List<UserData> pairsToSync = [];

        foreach (var user in allVisible)
        {
            var pairToCheck = _pairManager.GetPairByUID(user.UID);
            if (pairToCheck == null) continue;

            if (visibleAllianceAndPartyMemberSet.Contains(pairToCheck.Ident))
            {
                pairsToSync.Add(user);
            }
        }

        return Task.CompletedTask;
    }

    protected override void DrawInternal()
    {
        using var _ = _theme.PushWindowStyle();

        
    }

    public override void OnClose()
    {

        base.OnClose();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
