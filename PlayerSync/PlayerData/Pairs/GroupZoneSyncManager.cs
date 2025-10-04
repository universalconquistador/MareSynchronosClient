using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using MareSynchronos.MareConfiguration;
using MareSynchronos.WebAPI;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.MareConfiguration.Models;

namespace PlayerSync.PlayerData.Pairs;

public class GroupZoneSyncManager : DisposableMediatorSubscriberBase
{
    private readonly ILogger<GroupZoneSyncManager> _logger;
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly ZoneSyncConfigService _zoneSyncConfigService;
    private readonly PairManager _pairManager;

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

        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (__) => _ = SendGroupZoneSyncInfo());
        Mediator.Subscribe<ConnectedMessage>(this, (__) => _ = SendGroupZoneSyncInfo());
        Mediator.Subscribe<WorldChangeMessage>(this, (__) => _ = SendGroupZoneSyncInfo());
        Mediator.Subscribe<GroupZoneSetEnableState>(this, (msg) => _ = GroupZoneJoinEnabled(msg.isEnabled));
        Mediator.Subscribe<GroupZoneSyncUpdateMessage>(this, (__) => _ = SendGroupZoneSyncInfo());

        _logger.LogDebug("ZoneSync manger initialized.");
    }

    /// <summary>
    /// Send the users current world and location information to join a zone sync
    /// </summary>
    /// <returns></returns>
    private async Task SendGroupZoneSyncInfo()
    {
        var enableGroupZoneSyncJoining = _zoneSyncConfigService.Current.EnableGroupZoneSyncJoining;
        if (!enableGroupZoneSyncJoining) return;

        var dutyBound = _dalamudUtilService.IsBoundByDuty;
        var ownLocation = await _dalamudUtilService.GetMapDataAsync().ConfigureAwait(false);
        bool? inst = TerritoryTools.TerritoryStaticMap.IsInstance(ownLocation.TerritoryId);
        if (inst != false || dutyBound)
        {
            Logger.LogDebug("Cancelled ZoneSync, not in a permitted area.");
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

        await _apiController.GroupZoneJoin(new(ownLocation)).ConfigureAwait(false);
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
            await SendGroupZoneSyncInfo().ConfigureAwait(false);
        }
        else
        {
            // If they turned it off, look up if we have joined a zone sync so we can attempt to leave it
            await GroupZoneLeaveAll().ConfigureAwait(false);
        }
    }

    private async Task GroupZoneLeaveAll()
    {
        var zoneSync = _pairManager.Groups.Where(g => g.Value.PublicData.IsZoneSync);
        foreach (var sync in zoneSync)
        {
            Logger.LogDebug("Leaving ZoneSync for zone: {zone}", sync.Value.GID);
            _ = _apiController.GroupLeave(sync.Value);
        }
    }
}