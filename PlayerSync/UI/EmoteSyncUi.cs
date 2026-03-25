using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.EmoteSync;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;
using static MareSynchronos.Services.EmoteSync.EmoteSyncManagerService;

namespace MareSynchronos.UI;

public class EmoteSyncUi : WindowMediatorSubscriberBase
{
    private readonly UiSharedService _uiSharedService;
    private readonly UiTheme _theme;
    private readonly EmoteSyncManagerService _emoteSync;
    private readonly PairManager _pairManager;
    private readonly ApiController _apiController;
    private bool _isReady = false;
    private List<EmoteAction> _availableEmotes = [];
    private int _selectedEmoteId;
    private string _emoteSearchText = string.Empty;
    private bool _isActive = false;

    public EmoteSyncUi(ILogger<EmoteSyncUi> logger, MareMediator mediator, UiSharedService uiSharedService,
        PerformanceCollectorService performanceCollectorService, UiTheme theme, EmoteSyncManagerService emoteSyncManagerService, 
        PairManager pairManager, ApiController apiController)
        : base(logger, mediator, "PlayerSync EmoteSync", performanceCollectorService)
    {
        
        _uiSharedService = uiSharedService;
        _theme = theme;
        _emoteSync = emoteSyncManagerService;
        _pairManager = pairManager;
        _apiController = apiController;

        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 300,
                Y = 200
            }
        };

        Mediator.Subscribe<EmoteSyncStartMessage>(this, (_) => { IsOpen = false; });
        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) => { IsOpen = false; });
        Mediator.Subscribe<GposeStartMessage>(this, (_) => { IsOpen = false; });
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => { IsOpen = false; });
        Mediator.Subscribe<CombatOrPerformanceStartMessage>(this, (_) => { IsOpen = false; });
        Mediator.Subscribe<WorldChangeMessage>(this, (_) => { IsOpen = false; });
    }

    private string UserUID => _apiController.UID;

    public override void OnOpen()
    {
        base.OnOpen();

        if (!_apiController.IsConnected) return;

        if (_emoteSync.CurrentGroupId != null)
            return;

        if (_emoteSync.IsTimeSyncEnabled)
            return;

        _ = _emoteSync.SendEmoteSyncJoin();

        _ = InitializeEmoteSyncAsync();

        _availableEmotes = _emoteSync.GetUnlockedEmotes().OrderBy(emote => emote.SortOrder)
            .ThenBy(emote => emote.ActionName, StringComparer.OrdinalIgnoreCase).ToList();

        _emoteSearchText = string.Empty;

        if (_selectedEmoteId == 0 && _availableEmotes.Count > 0)
        {
            _selectedEmoteId = _availableEmotes[0].ActionId;
            _emoteSync.EmoteId = _availableEmotes[0].ActionId;
        }
    }

    public override bool DrawConditions()
    {
        if (!_apiController.IsConnected) return false;

        return true;
    }

    protected override void DrawInternal()
    {
        if (!_apiController.IsConnected)
            IsOpen = false;

        using var theme = _theme.PushWindowStyle();

        ImGuiHelpers.ScaledDummy(5f);

        var groupMembers = _emoteSync.GroupMembers;
        if (groupMembers.Count > 0)
        {
            _isActive = true;
        }
        else if (groupMembers.Count == 0 && _isActive)
        {
            IsOpen = false;
        }
        var groupId = _emoteSync.CurrentGroupId ?? "UNKNOWN";
        bool isLeader = string.Equals(groupId, UserUID, StringComparison.OrdinalIgnoreCase);
        string leaderName = isLeader ? _uiSharedService.PlayerName : _pairManager.GetPairByUID(groupId)?.PlayerName ?? "Unknown";

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8f, 8f));

        int columnCount = isLeader ? 3 : 2;

        if (ImGui.BeginTable("##ReadyStatusTable", columnCount, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV))
        {
            foreach (var entry in groupMembers)
            {
                string userName = string.Equals(UserUID, entry.Key, StringComparison.OrdinalIgnoreCase)
                    ? _uiSharedService.PlayerName
                    : _pairManager.GetPairByUID(entry.Key)?.PlayerName ?? "Unknown";

                bool isReady = entry.Value;
                bool isRowLeader = string.Equals(userName, leaderName, StringComparison.OrdinalIgnoreCase);
                bool canKickThisRow = isLeader && !string.Equals(entry.Key, UserUID, StringComparison.OrdinalIgnoreCase) && !isRowLeader;

                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(userName);

                ImGui.TableSetColumnIndex(1);
                if (isRowLeader)
                {
                    ImGui.TextColored(ImGuiColors.DalamudYellow, "Group Leader");
                }
                else if (isReady)
                {
                    ImGui.TextColored(ImGuiColors.HealerGreen, "Ready!");
                }
                else
                {
                    ImGui.TextColored(ImGuiColors.DalamudRed, "Not Ready");
                }

                if (isLeader)
                {
                    ImGui.TableSetColumnIndex(2);
                    if (canKickThisRow)
                    {
                        if (ImGui.SmallButton($"Kick##{entry.Key}"))
                        {
                            _ = _emoteSync.KickUserFromGroup(entry.Key);
                        }
                    }
                }
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar();

        ImGuiHelpers.ScaledDummy(2f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(2f);

        DrawEmoteCombo();

        if (isLeader)
        {
            bool isGroupReady = groupMembers.Where(g => !string.Equals(g.Key, groupId, StringComparison.OrdinalIgnoreCase)).All(g => g.Value);
            using (ImRaii.Disabled(!isGroupReady))
            {
                if (ImGui.Button("Start!"))
                {
                    _ = _emoteSync.SendEmoteSyncStart();
                }
            }
        }
        else
        {
            if (_isReady)
            {
                if (ImGui.Button("Not Ready"))
                {
                    _isReady = false;
                    _ = _emoteSync.SendEmoteSyncReadyStatus(false);
                }
            }
            else
            {
                if (ImGui.Button("Ready!"))
                {
                    _isReady = true;
                    _ = _emoteSync.SendEmoteSyncReadyStatus(true);
                }

            }
        }
    }

    private void DrawEmoteCombo()
    {
        EmoteAction? selectedEmote = _availableEmotes.FirstOrDefault(emote => emote.ActionId == _selectedEmoteId);
        string previewValue = selectedEmote?.ActionName ?? "Select an emote";

        float emoteItemHeight = ImGui.GetTextLineHeightWithSpacing();
        float emoteChildHeight = emoteItemHeight * 10f;
        float emotePopupHeight = emoteChildHeight
            + ImGui.GetFrameHeightWithSpacing()
            + ImGui.GetStyle().ItemSpacing.Y
            + ImGui.GetStyle().WindowPadding.Y * 2f
            + 6f;

        ImGui.SetNextWindowSizeConstraints(
            new Vector2(0f, emotePopupHeight),
            new Vector2(float.MaxValue, emotePopupHeight));

        ImGui.SetNextItemWidth(ImGui.GetWindowContentRegionMax().X / 2);
        if (!ImGui.BeginCombo("Emote", previewValue))
            return;

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##EmoteSearch", "Search emotes...", ref _emoteSearchText, 128);

        ImGui.Separator();

        if (ImGui.BeginChild("##EmoteResults", new Vector2(0, emoteChildHeight), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            string search = _emoteSearchText.Trim();

            foreach (EmoteAction emote in _availableEmotes)
            {
                if (!string.IsNullOrWhiteSpace(search) &&
                    !emote.ActionName.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isSelected = emote.ActionId == _selectedEmoteId;

                if (ImGui.Selectable($"{emote.ActionName}##{emote.ActionId}", isSelected))
                {
                    _selectedEmoteId = emote.ActionId;
                    _emoteSync.EmoteId = emote.ActionId;
                    ImGui.CloseCurrentPopup();
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
        }

        ImGui.EndChild();
        ImGui.EndCombo();
    }

    private async Task InitializeEmoteSyncAsync()
    {
        await _emoteSync.SetTimeSyncEnabledAsync(true).ConfigureAwait(false);

        string? hostName = await _emoteSync.GetCurrentLobbyHostAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            await _emoteSync.TimeSync.SetGameServerHostAsync(hostName).ConfigureAwait(false);
        }
    }

    public override void OnClose()
    {
        _logger.LogDebug("EmoteSync Window Closing...");
        _ = _emoteSync.Reset();
        _isReady = false;
        _isActive = false;
        base.OnClose();
    }
}
