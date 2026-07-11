using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace MareSynchronos.PlayerData.Pairs;

public sealed class PairManager : DisposableMediatorSubscriberBase
{
    private readonly TimeSpan PausedPairCheckInterval = TimeSpan.FromMinutes(1);

    private readonly MareConfigService _configurationService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly PairFactory _pairFactory;
    private readonly DalamudUtilService _dalamudUtilService;

    private readonly ConcurrentDictionary<UserData, Pair> _allClientPairs = new(UserDataComparer.Instance);
    private readonly ConcurrentDictionary<GroupData, GroupFullInfoDto> _allGroups = new(GroupDataComparer.Instance);
    private readonly ConcurrentDictionary<string, Pair> _identToUserPairs = new();
    private readonly object _applyDataLock = new();
    private readonly List<Pair> _applyDataQueue = [];
    private readonly Dictionary<Pair, OnlineUserCharaDataDto> _applyDataQueueDtos = [];
    private readonly HashSet<Pair> _runningApplyDataTasks = [];

    private Lazy<List<Pair>> _directPairsInternal;
    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> _groupPairsInternal;
    private Lazy<Dictionary<Pair, List<GroupFullInfoDto>>> _pairsWithGroupsInternal;
    private bool _isZoning = false;
    private bool _isConnected = false;
    private CancellationTokenSource? _periodicCts;
    private Task? _periodicTask;
    private int _maxConcurrentApplyData;

    public PairManager(ILogger<PairManager> logger, PairFactory pairFactory, DalamudUtilService dalamudUtilService,
                MareConfigService configurationService, ServerConfigurationManager serverConfigurationManager, MareMediator mediator)
        : base(logger, mediator)
    {
        _pairFactory = pairFactory;
        _configurationService = configurationService;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        _maxConcurrentApplyData = _configurationService.Current.MaxConcurrentApplications;

        Mediator.Subscribe<DisconnectedMessage>(this, (_) =>
        {
            _isConnected = false;
            ClearPairs();
        });

        Mediator.Subscribe<ConnectedMessage>(this, (_) => _isConnected = true);
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => ReapplyPairData());
        Mediator.Subscribe<ChangeFilterMessage>(this, (_) => ReapplyPairData());
        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) => _isZoning = true);
        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => _isZoning = false);
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => InitializePairs());

        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
        _pairsWithGroupsInternal = PairsWithGroupsLazy();

        _periodicCts?.Dispose();
        _periodicCts = new CancellationTokenSource();
        _periodicTask = PausedPairExpiredTimerCheckTask(_periodicCts.Token);

        Logger.LogTrace("{class} created.", nameof(PairManager));
    }

    public List<Pair> DirectPairs => _directPairsInternal.Value;
    public Dictionary<GroupFullInfoDto, List<Pair>> GroupPairs => _groupPairsInternal.Value;
    public Dictionary<GroupData, GroupFullInfoDto> Groups => _allGroups.ToDictionary(k => k.Key, k => k.Value);
    public Pair? LastAddedUser { get; internal set; }
    public Dictionary<Pair, List<GroupFullInfoDto>> PairsWithGroups => _pairsWithGroupsInternal.Value;
    public bool InitialLoading { get; set; } = true;
    public int MaxConcurrentApplyData
    {
        get
        {
            lock (_applyDataLock)
            {
                _maxConcurrentApplyData = _configurationService.Current.MaxConcurrentApplications;

                return _maxConcurrentApplyData;
            }
        }
        set
        {
            lock (_applyDataLock)
            {
                var max = value;
                if (max < 0)
                {
                    max = 0;
                }
                _maxConcurrentApplyData = max;
                _configurationService.Current.MaxConcurrentApplications = max;
                _configurationService.Save();
            }
        }
    }

    public void AddGroup(GroupFullInfoDto dto)
    {
        _allGroups[dto.Group] = dto;
        RecreateLazy();
    }

    public void AddGroupPair(GroupPairFullInfoDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
            _allClientPairs[dto.User] = _pairFactory.Create(new UserFullPairDto(dto.User, API.Data.Enum.IndividualPairStatus.None,
                [dto.Group.GID], dto.SelfToOtherPermissions, dto.OtherToSelfPermissions));
        else _allClientPairs[dto.User].UserPair.Groups.Add(dto.GID);
        RecreateLazy();
    }

    public Pair? GetPairByUID(string uid)
    {
        var existingPair = _allClientPairs.FirstOrDefault(f => f.Key.UID == uid);
        if (!Equals(existingPair, default(KeyValuePair<UserData, Pair>)))
        {
            return existingPair.Value;
        }

        return null;
    }

    public Pair? GetPairByCID(string cid)
    {
        var existingPair = _allClientPairs.FirstOrDefault(f => f.Value.Ident == cid);
        if (!Equals(existingPair, default(KeyValuePair<UserData, Pair>)))
        {
            return existingPair.Value;
        }

        return null;
    }

    public void AddUserPair(UserFullPairDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
        {
            _allClientPairs[dto.User] = _pairFactory.Create(dto);
        }
        else
        {
            _allClientPairs[dto.User].UserPair.IndividualPairStatus = dto.IndividualPairStatus;
            _allClientPairs[dto.User].ApplyLastReceivedData();
        }

        RecreateLazy();
    }

    public void AddUserPair(UserPairDto dto, bool addToLastAddedUser = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
        {
            _allClientPairs[dto.User] = _pairFactory.Create(dto);
        }
        else
        {
            addToLastAddedUser = false;
        }

        _allClientPairs[dto.User].UserPair.IndividualPairStatus = dto.IndividualPairStatus;
        _allClientPairs[dto.User].UserPair.OwnPermissions = dto.OwnPermissions;
        _allClientPairs[dto.User].UserPair.OtherPermissions = dto.OtherPermissions;
        if (addToLastAddedUser)
            LastAddedUser = _allClientPairs[dto.User];
        _allClientPairs[dto.User].ApplyLastReceivedData();
        RecreateLazy();
    }

    public void ClearPairs()
    {
        Logger.LogDebug("Clearing all Pairs");

        lock (_applyDataLock)
        {
            _applyDataQueue.Clear();
            _applyDataQueueDtos.Clear();
            _runningApplyDataTasks.Clear();
        }

        Logger.LogDebug("Data queue cleared");

        DisposePairs(); // this must come first as it references _allClientPairs
        _allClientPairs.Clear();
        _allGroups.Clear();
        _identToUserPairs.Clear();
        RecreateLazy();
    }

    public List<Pair> GetOnlineUserPairs() => _allClientPairs.Where(p => !string.IsNullOrEmpty(p.Value.GetPlayerNameHash())).Select(p => p.Value).ToList();

    public int GetVisibleUserCount() => _allClientPairs.Count(p => p.Value.IsVisible);

    public List<UserData> GetVisibleUsers() => [.. _allClientPairs.Where(p => p.Value.IsVisible).Select(p => p.Key)];

    public List<Pair> GetVisiblePairs() => [.. _allClientPairs.Where(p => p.Value.IsVisible).Select(p => p.Value)];

    public void MarkPairOffline(UserData user)
    {
        if (_allClientPairs.TryGetValue(user, out var pair))
        {
            Mediator.Publish(new ClearProfileDataMessage(pair.UserData));
            _identToUserPairs.TryRemove(pair.Ident, out _);
            pair.MarkOffline();
        }      

        RecreateLazy();
    }

    public void MarkPairOnline(OnlineUserIdentDto dto, bool sendNotif = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) throw new InvalidOperationException("No user found for " + dto);

        var pair = _allClientPairs[dto.User];
        if (pair.HasCachedPlayer)
        {
            RecreateLazy();
            return;
        }

        if (sendNotif && _configurationService.Current.ShowOnlineNotifications
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs && pair.IsDirectlyPaired && !pair.IsOneSidedPair
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs)
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs && !string.IsNullOrEmpty(pair.GetNote())
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs))
        {
            string? note = pair.GetNote();
            var msg = !string.IsNullOrEmpty(note)
                ? $"{note} ({pair.UserData.AliasOrUID}) is now online"
                : $"{pair.UserData.AliasOrUID} is now online";
            Mediator.Publish(new NotificationMessage("User online", msg, NotificationType.Info, TimeSpan.FromSeconds(5)));
        }

        pair.SetOnlinePlayerDto(dto);

        pair.IsOnline = true;

        _identToUserPairs.AddOrUpdate(dto.Ident, pair, (_, _) => pair);

        if (!InitialLoading)
        {
            RecreateLazy();
        }
    }

    /// <summary>
    /// Entry point for the pair data application pipeline, called via SignralR.
    /// Application processing is forked based on the existance of an AddonPlugin in the dto.
    /// Applications are only queued if they arrive here, direct application methods still work and bypass the queue (Reapply last data)
    /// </summary>
    public void ReceiveCharaData(OnlineUserCharaDataDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No user found for " + dto.User);
        }

        // process the dto via the addon plugin path if AddonPlugin exists
        if (dto.AddonPlugin is not null)
        {
            Logger.LogTrace("CharaData received with AddonPlugin: {plugin}", dto.AddonPlugin);
            Mediator.Publish(new EventMessage(new Event(pair.UserData, nameof(PairManager), EventSeverity.Informational, "Received AddonPlugin Data")));
            pair.ApplyAddonPluginUpdate(dto);
        }
        else
        {
            Mediator.Publish(new EventMessage(new Event(pair.UserData, nameof(PairManager), EventSeverity.Informational, "Received Full Character Data")));
            AddApplyDataToQueue(pair, dto);
        }
    }

    public void RemoveGroup(GroupData data)
    {
        _allGroups.TryRemove(data, out _);

        foreach (var item in _allClientPairs.ToList())
        {
            item.Value.UserPair.Groups.Remove(data.GID);

            if (!item.Value.HasAnyConnection())
            {
                item.Value.MarkOffline();
                _allClientPairs.TryRemove(item.Key, out _);
            }
        }

        RecreateLazy();
    }

    public void RemoveGroupPair(GroupPairDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair.Groups.Remove(dto.Group.GID);

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
        }

        RecreateLazy();
    }

    public void RemoveUserPair(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair.IndividualPairStatus = API.Data.Enum.IndividualPairStatus.None;

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
        }

        RecreateLazy();
    }

    public void SetGroupInfo(GroupInfoDto dto)
    {
        _allGroups[dto.Group].Group = dto.Group;
        _allGroups[dto.Group].Owner = dto.Owner;
        _allGroups[dto.Group].GroupPermissions = dto.GroupPermissions;
        _allGroups[dto.Group].PublicData = dto.PublicData;

        Mediator.Publish(new GroupInfoChanged(dto));

        RecreateLazy();
    }

    public void UpdatePairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            _serverConfigurationManager.RemovePauseReasonForUid(dto.User.UID);
            _serverConfigurationManager.RemovePendingPauseForUid(dto.User.UID);
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null)
        {
            _serverConfigurationManager.RemovePauseReasonForUid(dto.User.UID);
            _serverConfigurationManager.RemovePendingPauseForUid(dto.User.UID);
            throw new InvalidOperationException("No direct pair for " + dto);
        }

        if (pair.UserPair.OtherPermissions.IsPaused() != dto.Permissions.IsPaused())
        {
            Mediator.Publish(new ClearProfileDataMessage(dto.User));
        }

        pair.UserPair.OtherPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OtherPermissions.IsPaused(),
            pair.UserPair.OtherPermissions.IsDisableAnimations(),
            pair.UserPair.OtherPermissions.IsDisableSounds(),
            pair.UserPair.OtherPermissions.IsDisableVFX());

        if (!pair.IsPaused)
        {
            _serverConfigurationManager.RemovePauseReasonForUid(pair.UserData.UID);
            _serverConfigurationManager.RemovePendingPauseForUid(pair.UserData.UID);
            pair.ApplyLastReceivedData();
        }

        RecreateLazy();
    }

    public void UpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            _serverConfigurationManager.RemovePauseReasonForUid(dto.User.UID);
            _serverConfigurationManager.RemovePendingPauseForUid(dto.User.UID);
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair.OwnPermissions.IsPaused() != dto.Permissions.IsPaused())
        {
            Mediator.Publish(new ClearProfileDataMessage(dto.User));
        }

        pair.UserPair.OwnPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OwnPermissions.IsPaused(),
            pair.UserPair.OwnPermissions.IsDisableAnimations(),
            pair.UserPair.OwnPermissions.IsDisableSounds(),
            pair.UserPair.OwnPermissions.IsDisableVFX());

        if (!pair.IsPaused)
        {
            _serverConfigurationManager.RemovePauseReasonForUid(pair.UserData.UID);
            _serverConfigurationManager.RemovePendingPauseForUid(pair.UserData.UID);
            pair.ApplyLastReceivedData();
        }

        RecreateLazy();
    }

    internal void ReceiveUploadStatus(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var existingPair) && existingPair.IsVisible)
        {
            existingPair.SetIsUploading();
        }
    }

    internal void SetGroupPairStatusInfo(GroupPairUserInfoDto dto)
    {
        _allGroups[dto.Group].GroupPairUserInfos[dto.UID] = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void SetGroupPermissions(GroupPermissionDto dto)
    {
        _allGroups[dto.Group].GroupPermissions = dto.Permissions;
        RecreateLazy();
    }

    internal void SetGroupStatusInfo(GroupPairUserInfoDto dto)
    {
        _allGroups[dto.Group].GroupUserInfo = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void UpdateGroupPairPermissions(GroupPairUserPermissionDto dto)
    {
        _allGroups[dto.Group].GroupUserPermissions = dto.GroupPairPermissions;
        RecreateLazy();
    }

    internal void UpdateIndividualPairStatus(UserIndividualPairStatusDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair.IndividualPairStatus = dto.IndividualPairStatus;
            RecreateLazy();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        CancellationTokenSource? cts;
        cts = _periodicCts;
        _periodicCts = null;
        cts?.Cancel();
        cts?.Dispose();

        _isConnected = false;
        DisposePairs();
    }

    private Lazy<List<Pair>> DirectPairsLazy() => new(() => _allClientPairs.Select(k => k.Value)
        .Where(k => k.IndividualPairStatus != API.Data.Enum.IndividualPairStatus.None).ToList());

    private void DisposePairs()
    {
        Logger.LogDebug("Disposing all Pairs");
        Parallel.ForEach(_allClientPairs, item =>
        {
            item.Value.MarkOffline(wait: false);
        });

        RecreateLazy();
    }

    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> GroupPairsLazy()
    {
        return new Lazy<Dictionary<GroupFullInfoDto, List<Pair>>>(() =>
        {
            Dictionary<GroupFullInfoDto, List<Pair>> outDict = [];
            foreach (var group in _allGroups)
            {
                outDict[group.Value] = _allClientPairs.Select(p => p.Value).Where(p => p.UserPair.Groups.Exists(g => GroupDataComparer.Instance.Equals(group.Key, new(g)))).ToList();
            }
            return outDict;
        });
    }

    private Lazy<Dictionary<Pair, List<GroupFullInfoDto>>> PairsWithGroupsLazy()
    {
        return new Lazy<Dictionary<Pair, List<GroupFullInfoDto>>>(() =>
        {
            Dictionary<Pair, List<GroupFullInfoDto>> outDict = [];

            foreach (var pair in _allClientPairs.Select(k => k.Value))
            {
                outDict[pair] = _allGroups.Where(k => pair.UserPair.Groups.Contains(k.Key.GID, StringComparer.Ordinal)).Select(k => k.Value).ToList();
            }

            return outDict;
        });
    }

    private void ReapplyPairData()
    {
        foreach (var pair in _allClientPairs.Select(k => k.Value))
        {
            pair.ApplyLastReceivedData(forced: true);
        }
    }

    public void RecreateLazy()
    {
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
        _pairsWithGroupsInternal = PairsWithGroupsLazy();
        Mediator.Publish(new RefreshUiMessage());
    }

    // enqueues a pair and dto, or updates the dto if the data application hasn't run yet for the pair
    private void AddApplyDataToQueue(Pair pair, OnlineUserCharaDataDto dto)
    {
        lock (_applyDataLock)
        {
            _applyDataQueueDtos[pair] = dto; // save the pair-to-dto mapping and only allow one instance/latest dto

            // as a default action, we put direct pairs in the front of the queue
            if (_applyDataQueue.Contains(pair))
            {
                if (pair.IsDirectlyPaired)
                {
                    _applyDataQueue.Remove(pair);
                    _applyDataQueue.Insert(0, pair);
                }
            }
            else
            {
                if (pair.IsDirectlyPaired)
                {
                    _applyDataQueue.Insert(0, pair);
                }
                else
                {
                    _applyDataQueue.Add(pair);
                }
            }
        }

        ProcessApplyDataQueue();
    }

    // This runs through the queue and kicks off the data application task per queued pair entry
    private void ProcessApplyDataQueue()
    {
        List<(Pair, OnlineUserCharaDataDto)> dataToApply = [];

        lock (_applyDataLock)
        {
            while (_applyDataQueue.Count > 0 && (_maxConcurrentApplyData == 0 || _runningApplyDataTasks.Count < _maxConcurrentApplyData))
            {
                // find the next pair in the queu that is not already running
                var index = _applyDataQueue.FindIndex(pair => !_runningApplyDataTasks.Contains(pair));

                if (index < 0)
                {
                    break;
                }

                var pair = _applyDataQueue[index];
                _applyDataQueue.RemoveAt(index);

                if (!_applyDataQueueDtos.Remove(pair, out var dto))
                {
                    continue;
                }

                _runningApplyDataTasks.Add(pair);
                dataToApply.Add((pair, dto));
            }
        }

        foreach (var pairToDto in dataToApply)
        {
            // each task will remove its pair in the finally clause
            _ = ApplyPairDataAsync(pairToDto.Item1, pairToDto.Item2);
        }
    }

    // This handles running and awaiting the data application pipeline
    private async Task ApplyPairDataAsync(Pair pair, OnlineUserCharaDataDto dto)
    {
        Stopwatch? stopwatch = null;
        bool hadErrors = false;
        Guid applicationBase = Guid.NewGuid();

        try
        {
            if (!_allClientPairs.TryGetValue(pair.UserData, out var currentPair) || currentPair.UserData.UID != pair.UserData.UID)
            {
                return;
            }

            stopwatch = Stopwatch.StartNew();

            Logger.LogDebug("[BASE-{appBase}] Starting queued data application for {player}:{uid}", applicationBase, pair.PlayerName, pair.UserData.UID);
            // The entire pipeline chains a Task so we know when it's finished (or fails)
            await pair.ApplyDataAsync(applicationBase, dto).ConfigureAwait(false); // original pipeline entry point
        }
        catch (OperationCanceledException)
        {
            hadErrors = true;
            Logger.LogDebug("[BASE-{appBase}] Queued data application was cancelled for {player}:{uid}", applicationBase, pair.PlayerName, pair.UserData.UID);
        }
        catch (Exception ex)
        {
            hadErrors = true;
            Logger.LogError(ex, "[BASE-{appBase}] Failed to apply queued pair data for {player}:{uid}", applicationBase, pair.PlayerName, pair.UserData.UID);
        }
        finally
        {
            stopwatch?.Stop();
            if (stopwatch != null)
            {
                if (hadErrors)
                {
                    Logger.LogWarning("[BASE-{appBase}] Queued data application failed for {player}:{uid} after {elapsedMs}ms", applicationBase, pair.PlayerName, pair.UserData.UID, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    Logger.LogDebug("[BASE-{appBase}] Queued data application finished for {player}:{uid} in {elapsedMs}ms", applicationBase, pair.PlayerName, pair.UserData.UID, stopwatch.ElapsedMilliseconds);
                }
            }

            lock (_applyDataLock)
            {
                _runningApplyDataTasks.Remove(pair); // ensure we always remove the entry, pass or fail
            }

            ProcessApplyDataQueue(); // continue processing since we aren't running an infinite while loop
        }
    }

    private void InitializePairs()
    {
        if (_isZoning || !_isConnected)
        {
            return;
        }

        var visiblePlayerIdents = _dalamudUtilService.GetVisiblePlayerIdents();

        foreach (var playerIdent in visiblePlayerIdents)
        {
            if (!_identToUserPairs.TryGetValue(playerIdent, out var pair) || pair == null || pair.HasCachedPlayer)
            {
                continue;
            }

            var playerCharacter = _dalamudUtilService.FindPlayerByNameHash(pair.Ident); // This kicks everything off once we can discern the Pair's hashed ident
            if (playerCharacter == default((string, nint)))
            {
                continue;
            }

            Logger.LogDebug("One-Time Initializing {uid}:{name}", pair.UserData.UID, playerCharacter.Name);

            pair.Initialize(playerCharacter.Name); // this should not take long and can be called on Framework thread

            Logger.LogDebug("One-Time Initialized {uid}:{name}", pair.UserData.UID, pair.PlayerName);
            Mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PairHandler), EventSeverity.Informational,
                $"Initializing User For Character {pair.PlayerName}")));
        }
    }

    private async Task PausedPairExpiredTimerCheckTask(CancellationToken ct)
    {
        try
        {
            Logger.LogDebug("Starting checks for paused players to unpause.");
            var initDelay = TimeSpan.FromMinutes(1);
            await Task.Delay(initDelay, ct).ConfigureAwait(false); // init delay for plugin load

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_isConnected)
                    {
                        Logger.LogTrace("Checking for paused pairs to unpause...");

                        var onlinePairs = GetOnlineUserPairs();

                        var pendingPausedPairs = _serverConfigurationManager.GetPendingPausedPairUIDs();
                        List<string> pairsToUnPause = new List<string>();

                        foreach (var pausedPair in pendingPausedPairs)
                        {
                            var uid = pausedPair.Key;
                            var pair = GetPairByUID(uid);
                            if (onlinePairs.Any(p => string.Equals(p.UserData.UID, uid, StringComparison.OrdinalIgnoreCase)) || pair == null || !pair.IsPaused)
                            {
                                Logger.LogDebug("Removing stale paused pair entry for UID: {uid}", uid);
                                _serverConfigurationManager.RemovePauseReasonForUid(uid);
                                _serverConfigurationManager.RemovePendingPauseForUid(uid);
                                continue;
                            }

                            if (DateTimeOffset.UtcNow >= pausedPair.Value)
                            {
                                pairsToUnPause.Add(uid);
                            }
                        }

                        foreach (var uidToUnpause in pairsToUnPause)
                        {
                            Logger.LogDebug("Automatically removing paused status for UID: {uid}", uidToUnpause);
                            Mediator.Publish(new UnPauseMessage(new(uidToUnpause), true));
                            await Task.Delay(250).ConfigureAwait(false);
                        }
                    }

                    await Task.Delay(PausedPairCheckInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error while checking for paused pair.");

                    await Task.Delay(PausedPairCheckInterval, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "Error while checking for paused pairs. This background task will now stop.");
        }
        finally
        {
            _periodicTask = null;
        }
    }
}