using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.Emote;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MareSynchronos.Services.EmoteSync;

public sealed class EmoteSyncManagerService : MediatorSubscriberBase, IHostedService, IDisposable
{
    private static readonly TimeSpan TimeSyncInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FinalSpinThreshold = TimeSpan.FromMilliseconds(3);

    private readonly ILogger<EmoteSyncManagerService> _logger;
    private readonly IDataManager _gameData;
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly PairManager _pairManager;

    private readonly object _stateLock = new();

    EmoteTimeSyncService _timeSync = new();
    private CancellationTokenSource? _timeSyncCts;
    private Task? _timeSyncTask;
    private bool _timeSyncEnabled;

    private string? _currentGroupId;
    private Dictionary<string, bool> _groupMembers = [];
    private readonly ConcurrentDictionary<Guid, byte> _executedEventIds = new();
    private bool _isRunning = false;

    public EmoteSyncManagerService(ILogger<EmoteSyncManagerService> logger, MareMediator mediator, IDataManager gameData,
        ApiController apiController, DalamudUtilService dalamudUtilService, PairManager pairManager) : base(logger, mediator)
    {
        _logger = logger;
        _apiController = apiController;
        _dalamudUtilService = dalamudUtilService;
        _pairManager = pairManager;
        _gameData = gameData;
    }

    public record EmoteAction(int ActionId, string ActionName, ushort SortOrder);

    public bool IsTimeSyncEnabled
    {
        get
        {
            lock (_stateLock)
            {
                return _timeSyncEnabled;
            }
        }
    }

    public string? CurrentGroupId
    {
        get
        {
            lock (_stateLock)
            {
                return _currentGroupId;
            }
        }
    }

    public int EmoteId { get; set; }

    public IReadOnlyDictionary<string, bool> GroupMembers
    {
        get
        {
            lock (_stateLock)
            {
                return new Dictionary<string, bool>(_groupMembers, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("EmoteSync manager started.");
        Mediator.Subscribe<EmoteSyncUpdateMessage>(this, (msg) => ApplyGroupUpdate(msg.Dto));
        Mediator.Subscribe<EmoteSyncStartMessage>(this, (msg) => _ = HandleScheduledEmoteActionAsync(msg.Dto));
        Mediator.Subscribe<ConnectedMessage>(this, (__) => _ = OnServerConnect());
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("EmoteSync manager stopping.");
        await SetTimeSyncEnabledAsync(false).ConfigureAwait(false);
        _logger.LogDebug("EmoteSync manager stopped.");
    }

    public void Dispose()
    {
        _timeSyncCts?.Cancel();
        _timeSyncCts?.Dispose();
    }

    public async Task<string?> GetCurrentLobbyHostAsync()
    {
        ushort worldId = (ushort)await _dalamudUtilService.GetWorldIdAsync().ConfigureAwait(false);
        string? dataCenterName = _dalamudUtilService.GetDataCenterNameForWorld(worldId);
        _logger.LogTrace("Starting ping to {dc}.", dataCenterName);
        return _timeSync.GetLobbyHostForDataCenter(dataCenterName);
    }

    public async Task SetGameServerHostAsync(string? host)
    {
        await _timeSync.SetGameServerHostAsync(host).ConfigureAwait(false);
    }

    /// <summary>
    /// toggle for the UI to enable/disable background time sync
    /// </summary>
    public async Task SetTimeSyncEnabledAsync(bool isEnabled)
    {
        CancellationTokenSource? ctsToCancel = null;
        Task? taskToAwait = null;

        lock (_stateLock)
        {
            if (_timeSyncEnabled == isEnabled)
                return;

            _timeSyncEnabled = isEnabled;

            if (isEnabled)
            {
                _timeSyncCts = new CancellationTokenSource();
                _timeSyncTask = RunTimeSyncLoopAsync(_timeSyncCts.Token);
            }
            else
            {
                ctsToCancel = _timeSyncCts;
                taskToAwait = _timeSyncTask;
                _timeSyncCts = null;
                _timeSyncTask = null;
            }
        }

        if (isEnabled)
        {
            return;
        }

        if (ctsToCancel != null)
        {
            try
            {
                await ctsToCancel.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                ctsToCancel.Dispose();
            }
        }

        if (taskToAwait != null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // swallow
            }
        }

        _timeSync.Reset();
    }

    public async Task RequestTimeSyncNowAsync()
    {
        if (!_apiController.IsConnected)
        {
            _logger.LogDebug("Skipping time sync request, not connected.");
            await SetTimeSyncEnabledAsync(false).ConfigureAwait(false);
            await _timeSync.StopGameServerPingAsync().ConfigureAwait(false);
            return;
        }

        long clientSendUtcTicks = DateTime.UtcNow.Ticks;

        ServerTimeRequestDto request = new()
        {
            RequestId = Guid.NewGuid(),
            ClientSendUtcTicks = clientSendUtcTicks
        };

        try
        {
            ServerTimeResponseDto response = await _apiController.GetServerTime(request).ConfigureAwait(false);
            _logger.LogTrace("Response ticks: {t1}, {t2}", response.ClientSendUtcTicks.ToString(), response.ServerSendUtcTicks.ToString());

            long clientReceiveUtcTicks = DateTime.UtcNow.Ticks;
            long clientReceiveStopwatchTicks = Stopwatch.GetTimestamp();

            _timeSync.AcceptSample(response, clientReceiveUtcTicks, clientReceiveStopwatchTicks);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to synchronize emote server time.");
        }
    }

    public void ClearGroupState()
    {
        lock (_stateLock)
        {
            _currentGroupId = null;
            _groupMembers = [];
        }
    }

    /// <summary>
    /// Send an EmoteSync group join to the server. The first player in an alliance/party to issue the command becomes the group leader
    /// </summary>
    /// <returns></returns>
    public async Task SendEmoteSyncJoin()
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
        _logger.LogDebug("Emote join users: {users}", string.Join(", ", pairsToSync));

        EmoteActionDto dto = new()
        {
            EmoteSyncAction = EmoteSyncAction.Join,
            VisiblePartyMembers = pairsToSync
        };

        _logger.LogDebug("Sending emote sync join.");
        await _apiController.UserEmoteSyncAction(dto).ConfigureAwait(false);
    }

    /// <summary>
    /// Send a EmoteSync group leave message to the server, this leaves the group
    /// </summary>
    /// <returns></returns>
    public async Task SendEmoteSyncLeave()
    {
        if (!_apiController.IsConnected) return;

        EmoteActionDto dto = new()
        {
            EmoteSyncAction = EmoteSyncAction.Leave
        };

        await _apiController.UserEmoteSyncAction(dto).ConfigureAwait(false);
    }

    /// <summary>
    /// Send a ready message to the server for your player status
    /// </summary>
    /// <param name="isReady"></param>
    /// <returns></returns>
    public async Task SendEmoteSyncReadyStatus(bool isReady)
    {
        EmoteActionDto dto = new()
        {
            EmoteSyncAction = isReady ? EmoteSyncAction.Ready : EmoteSyncAction.NotReady
        };

        _logger.LogDebug("Sending EmoteSync status ready: {status}", isReady.ToString());
        await _apiController.UserEmoteSyncAction(dto).ConfigureAwait(false);
    }

    /// <summary>
    /// Send a start message for the EmoteSync group to the server, can only be executed by the group leader
    /// </summary>
    /// <returns></returns>
    public async Task SendEmoteSyncStart()
    {
        EmoteActionDto dto = new()
        {
            EmoteSyncAction = EmoteSyncAction.Start
        };

        await _apiController.UserEmoteSyncAction(dto).ConfigureAwait(false);
    }

    /// <summary>
    /// Remove a player from a EmoteSync group, can only be executed the group leader
    /// </summary>
    /// <param name="uid"></param>
    /// <returns></returns>
    public async Task KickUserFromGroup(string uid)
    {
        EmoteActionDto dto = new()
        {
            EmoteSyncAction = EmoteSyncAction.Kick,
            VisiblePartyMembers = [new UserData(uid)]
        };

        await _apiController.UserEmoteSyncAction(dto).ConfigureAwait(false);
    }

    public List<EmoteAction> GetUnlockedEmotes()
    {
        return _gameData.GetExcelSheet<Emote>().Where(IsUnlocked).Select(GetEmoteAction).ToList();
    }

    /// <summary>
    /// This is called from a server group update dto
    /// </summary>
    /// <param name="dto"></param>
    private void ApplyGroupUpdate(EmoteResponseDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        lock (_stateLock)
        {
            if (dto.EmoteGroupMembers.Count == 0)
            {
                _currentGroupId = null;
                _groupMembers = [];
            }
            else
            {
                _currentGroupId = dto.EmoteLeadUser.UID;
                _groupMembers = dto.EmoteGroupMembers;
            }

            _logger.LogDebug("Emote join users: {users}", string.Join(", ", _groupMembers.Keys.ToList()));
        }
    }

    /// <summary>
    /// This is called from the server to start a client emote sync
    /// </summary>
    /// <param name="dto"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task HandleScheduledEmoteActionAsync(ScheduledEmoteActionDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!_executedEventIds.TryAdd(dto.EventId, 0))
        {
            _logger.LogDebug("Ignoring duplicate scheduled emote event {eventId}.", dto.EventId);
            return;
        }

        _isRunning = true;

        try
        {
            if (!_timeSync.TryGetDelayUntilServerUtcTicks(dto.ExecuteAtServerUtcTicks, out TimeSpan delay, compensateForGameServerLatency: true))
            {
                _logger.LogWarning("Received scheduled emote action without time sync, executing immediately.");
                await ExecuteScheduledAsync().ConfigureAwait(false);
                return;
            }

            long compensatedExecuteAtTicks = dto.ExecuteAtServerUtcTicks - _timeSync.EstimatedCompensatedGameServerOneWay.Ticks;

            if (delay > FinalSpinThreshold)
            {
                await Task.Delay(delay - FinalSpinThreshold, cancellationToken).ConfigureAwait(false);
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_timeSync.TryGetEstimatedServerUtcTicksNow(out long estimatedServerUtcTicksNow))
                {
                    _logger.LogWarning("Lost server time sync during scheduled emote action, executing immediately.");
                    break;
                }

                if (estimatedServerUtcTicksNow >= compensatedExecuteAtTicks)
                {
                    break;
                }

                Thread.SpinWait(50);
            }

            await ExecuteScheduledAsync().ConfigureAwait(false);
        }
        finally
        {
            _executedEventIds.TryRemove(dto.EventId, out _);
            _isRunning = false;
        }
    }

    public async Task Reset()
    {
        await Task.Delay(100).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();

        while (_isRunning)
        {
            if (stopwatch.ElapsedMilliseconds >= 2000)
            {
                _isRunning = false;
                break;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        await SendEmoteSyncLeave().ConfigureAwait(false);
        await SetTimeSyncEnabledAsync(false).ConfigureAwait(false);
        await _timeSync.StopGameServerPingAsync().ConfigureAwait(false);
        _timeSync.Reset();
        ClearGroupState();
    }

    private async Task OnServerConnect()
    {
        await SetTimeSyncEnabledAsync(false).ConfigureAwait(false);
        await _timeSync.StopGameServerPingAsync().ConfigureAwait(false);
        _timeSync.Reset();
        ClearGroupState();
    }

    private async Task RunTimeSyncLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Emote time sync loop started.");

        try
        {
            await RequestTimeSyncNowAsync().ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSyncInterval, cancellationToken).ConfigureAwait(false);
                await RequestTimeSyncNowAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Emote time sync loop canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unhandled exception in emote time sync loop.");
        }
        finally
        {
            _logger.LogDebug("Emote time sync loop stopped.");
        }
    }

    private async Task ExecuteScheduledAsync()
    {
        ExecuteEmote((uint)EmoteId);
    }

    private unsafe void ExecuteEmote(uint id)
    {

        var emote = GetEmoteById(id);

        if (emote == null)
        {
            _logger.LogWarning("Tried to EmoteSync with unknown emote id: {id}", id.ToString());
            return;
        }

        _ = _dalamudUtilService.RunOnFrameworkThread(() => ExecuteEmoteById((ushort)id));
    }

    private static EmoteAction GetEmoteAction(Emote emote)
    {
        return new EmoteAction((int)emote.RowId, emote.Name.ToString(), emote.Order);
    }

    private Emote? GetEmoteById(uint id) => _gameData.Excel.GetSheet<Emote>().GetRowOrDefault(id);

    private static unsafe void ExecuteEmoteById(ushort id)
    {
        var emoteManager = FFXIVClientStructs.FFXIV.Client.Game.Control.EmoteManager.Instance();
        if (emoteManager->CanExecuteEmote(id))
        {
            emoteManager->ExecuteEmote(id);
        }
    }

    // Code borrowed from https://github.com/KazWolfe/XIVDeck by KazWolfe
    private static unsafe bool IsUnlocked(Emote emote)
    {
        if (emote.EmoteCategory.RowId == 0 || emote.Order == 0) return false;

        switch (emote.RowId)
        {
            case 55 when PlayerState.Instance()->GrandCompany != 1: // Maelstrom
            case 56 when PlayerState.Instance()->GrandCompany != 2: // Twin Adders
            case 57 when PlayerState.Instance()->GrandCompany != 3: // Immortal Flames
                return false;
        }

        return emote.UnlockLink == 0 || UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(emote.UnlockLink);
    }
}