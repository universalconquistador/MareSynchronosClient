using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using MareSynchronos.MareConfiguration;
using MareSynchronos.WebAPI;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.MareConfiguration.Models;
using Microsoft.AspNetCore.SignalR;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.Utils;
using MareSynchronos.API.Dto;

namespace PlayerSync.PlayerData.Pairs;

public class GroupZoneSyncManager : DisposableMediatorSubscriberBase
{
    private readonly ILogger<GroupZoneSyncManager> _logger;
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly ZoneSyncConfigService _zoneSyncConfigService;
    private readonly PairManager _pairManager;
    private DefaultPermissionsDto _ownPermissions = null!;
    private readonly object _zoneSyncLock = new();
    private CancellationTokenSource? _zoneSyncCts;
    private Task? _zoneSyncPendingTask;
    private bool _waitingToJoinZoneGroup;

    public GroupZoneSyncManager(ILogger<GroupZoneSyncManager> logger, MareMediator mediator,
            ApiController apiController,
            DalamudUtilService dalamudUtilService,
            ZoneSyncConfigService zoneSyncConfigService,
            PairManager pairManager)
            : base(logger, mediator)
    {
        _logger = logger;
        _apiController = apiController;
        _dalamudUtilService = dalamudUtilService;
        _zoneSyncConfigService = zoneSyncConfigService;
        _pairManager = pairManager;
        _ownPermissions = _apiController.DefaultPermissions.DeepClone()!;

        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (__) => ScheduleGroupZoneSync());
        Mediator.Subscribe<WorldChangeMessage>(this, (__) => ScheduleGroupZoneSync());
        Mediator.Subscribe<GroupZoneSetEnableState>(this, (msg) => _ = GroupZoneJoinEnabled(msg.isEnabled));
        Mediator.Subscribe<GroupZoneSyncUpdateMessage>(this, (__) => ScheduleGroupZoneSync());

        _logger.LogDebug("ZoneSync manger initialized.");
    }

    private DefaultPermissionsDto OwnDefaultPermissions => _apiController.DefaultPermissions.DeepClone()!;

    /// <summary>
    /// Debounce based scheduler for ZoneSync so we can add some delay before triggering.
    /// Also resets in case the user is zoning quickly (teleporting between areas)
    /// </summary>
    public void ScheduleGroupZoneSync()
    {
        var enableGroupZoneSyncJoining = _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining;
        if (!enableGroupZoneSyncJoining) return;

        var delay = TimeSpan.FromSeconds(_zoneSyncConfigService.Current.ZoneJoinDelayTime);

        var newCts = new CancellationTokenSource();
        CancellationTokenSource? oldCts;

        lock (_zoneSyncLock)
        {
            _waitingToJoinZoneGroup = true;
            oldCts = _zoneSyncCts;
            _zoneSyncCts = newCts;

            _zoneSyncPendingTask = DebouncedSendAsync(delay, newCts.Token);
        }

        oldCts?.Cancel();
        oldCts?.Dispose();
    }

    private async Task DebouncedSendAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            _logger.LogDebug("Sending ZoneSync join message in {sec}s.", delay);
            await Task.Delay(delay, token).ConfigureAwait(false);
            _logger.LogDebug("Sending ZoneSync join message now.");
            await SendGroupZoneSyncInfo().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SendGroupZoneSyncInfo reset before timer expired.");
        }
        finally
        {
            lock (_zoneSyncLock)
            {
                if (_zoneSyncCts == null || token == _zoneSyncCts.Token)
                {
                    _zoneSyncCts?.Dispose();
                    _zoneSyncCts = null;
                    _waitingToJoinZoneGroup = false;
                }
            }
        }
    }

    /// <summary>
    /// Send the users current world and location information to join a zone sync
    /// </summary>
    /// <returns></returns>
    private async Task SendGroupZoneSyncInfo()
    {
        if (!_apiController.IsConnected)
        {
            _logger.LogWarning("Can't call SendGroupZoneSyncInfo when not connected.");
            return;
        }
        var dutyBound = _dalamudUtilService.IsBoundByDuty;
        var ownLocation = await _dalamudUtilService.GetMapDataAsync().ConfigureAwait(false);
        bool? inst = TerritoryTools.TerritoryStaticMap.IsInstance(ownLocation.TerritoryId);
        if (inst != false || dutyBound)
        {
            Logger.LogDebug("Cancelled ZoneSync, not in a permitted area.");
            await GroupZoneLeaveAll().ConfigureAwait(false);
            return;
        }

        var filteredZones = _zoneSyncConfigService.Current.ZoneSyncFilter;
        var isTown = TerritoryTools.TerritoryStaticMap.IsTown(ownLocation.TerritoryId);
        var isResidential = ownLocation.WardId != 0;
        switch(filteredZones)
        {
            case ZoneSyncFilter.All:
                break;

            case ZoneSyncFilter.TownOnly:
                if (!isTown)
                {
                    await GroupZoneLeaveAll().ConfigureAwait(false);
                    return;
                }
                break;

            case ZoneSyncFilter.ResidentialOnly:
                if (!isResidential)
                {
                    await GroupZoneLeaveAll().ConfigureAwait(false);
                    return;
                }
                break;

            case ZoneSyncFilter.ResidentialTown:
                if (!(isTown || isResidential))
                {
                    await GroupZoneLeaveAll().ConfigureAwait(false);
                    return;
                }
                break;
        }
        
        _logger.LogDebug("Sending ZoneSync join for {world} {territory} {ward} {house} {room}",
        ownLocation.ServerId, ownLocation.TerritoryId, ownLocation.WardId, ownLocation.HouseId, ownLocation.RoomId);

        GroupUserPreferredPermissions joinPermissions = GroupUserPreferredPermissions.NoneSet;
        joinPermissions.SetDisableSounds(OwnDefaultPermissions.DisableGroupSounds);
        joinPermissions.SetDisableAnimations(OwnDefaultPermissions.DisableGroupAnimations);
        joinPermissions.SetDisableVFX(OwnDefaultPermissions.DisableGroupVFX);

        try
        {
            await _apiController.GroupZoneJoin(new(ownLocation, joinPermissions)).ConfigureAwait(false);
        }
        catch (HubException)
        {
            var message = "This sync service does not support ZoneSync and the feature will be disabled.";
            Logger.LogError(message);
            Mediator.Publish(new NotificationMessage("ZoneSync Error", message, NotificationType.Error, TimeSpan.FromSeconds(7.5)));
            _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining = false;
            _zoneSyncConfigService.Save();
        }
        catch (AggregateException)
        {
            // TODO Find out who is calling early
            _logger.LogDebug("ZoneSync was called before the server state was connected.");
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "ZoneSync join failed.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ZoneSync join failed.");
        }
    }

    /// <summary>
    /// Set the state of the zone based syncshell when the user toggles it on/off from the settings menu
    /// </summary>
    /// <param name="isZoneGroupEnabled"></param>
    /// <returns></returns>
    private async Task GroupZoneJoinEnabled(bool isZoneGroupEnabled)
    {
        if (isZoneGroupEnabled)
        {
            // If the user turns it on, initiate a zone join before actually zoning
            ScheduleGroupZoneSync();
        }
        else
        {
            // If they turned it off, look up if we have joined a zone sync so we can attempt to leave it
            await GroupZoneLeaveAll().ConfigureAwait(false);

            // Ensure we've disabled ZoneSync
            _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining = false;
            _zoneSyncConfigService.Save();
        }
    }

    private async Task GroupZoneLeaveAll()
    {
        if (!_apiController.IsConnected)
        {
            _logger.LogWarning("Can't call GroupZoneLeaveAll when not connected.");
            return;
        }
        var zoneSync = _pairManager.Groups.Where(g => g.Value.PublicData.IsZoneSync);
        foreach (var sync in zoneSync)
        {
            Logger.LogDebug("Leaving ZoneSync for zone: {zone}", sync.Value.GID);
            _ = _apiController.GroupLeave(sync.Value);
        }
    }
}