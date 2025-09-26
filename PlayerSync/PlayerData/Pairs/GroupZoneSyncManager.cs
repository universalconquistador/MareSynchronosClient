using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using MareSynchronos.MareConfiguration;
using MareSynchronos.WebAPI;
using MareSynchronos.PlayerData.Pairs;
using Lumina.Extensions;


namespace PlayerSync.PlayerData.Pairs;

public class GroupZoneSyncManager : DisposableMediatorSubscriberBase
{
    private readonly ILogger<GroupZoneSyncManager> _logger;
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly MareConfigService _mareConfigService;
    private readonly PairManager _pairManager;

    public GroupZoneSyncManager(ILogger<GroupZoneSyncManager> logger, MareMediator mediator,
            ApiController apiController,
            DalamudUtilService dalamudUtilService,
            MareConfigService mareConfigService,
            PairManager pairManager)
            : base(logger, mediator)
    {
        _logger = logger;
        _apiController = apiController;
        _dalamudUtilService = dalamudUtilService;
        _mareConfigService = mareConfigService;
        _pairManager = pairManager;

        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => SendGroupZoneSyncInfo());
        Mediator.Subscribe<GroupZoneSetEnableState>(this, (msg) => GroupZoneJoinLeave(msg.isEnabled));     
    }

    /// <summary>
    /// Send the users current world and location information to join a zone sync
    /// </summary>
    /// <returns></returns>
    private async Task SendGroupZoneSyncInfo()
    {
        var enableGroupZoneSyncJoining = _mareConfigService.Current.EnableGroupZoneSyncJoining;
        if (!enableGroupZoneSyncJoining) return;

        // Get the required world + location info first, this is how we build our sync id
        var ownLocation = await _dalamudUtilService.GetMapDataAsync().ConfigureAwait(false);
        
        // We don't support instance zones, so we should try and avoid joining them
        // This will be replaced with proper Lumina code later to build dynamically
        bool? inst = TerritoryTools.TerritoryStaticMap.IsInstance(ownLocation.TerritoryId);
        if (inst != false) return;

        var currentWorld = await _dalamudUtilService.GetWorldIdAsync().ConfigureAwait(false);
        
        _logger.LogDebug("Sending zone sync join for {world} {territory} {ward} {house} {room}",
            currentWorld, ownLocation.TerritoryId, ownLocation.WardId, ownLocation.HouseId, ownLocation.RoomId);

        await _apiController.GroupZoneJoin(new(currentWorld, ownLocation)).ConfigureAwait(false);
    }

    /// <summary>
    /// Set the state of the zone based syncshell when the user toggles it on/off from the settings menu
    /// </summary>
    /// <param name="isZoneGroupEnabled"></param>
    /// <returns></returns>
    private async Task GroupZoneJoinLeave(bool isZoneGroupEnabled)
    {
        if (isZoneGroupEnabled)
        {
            // If the user turns it on, initiate a zone join before actually zoning
            await SendGroupZoneSyncInfo().ConfigureAwait(false);
        }
        else
        {
            // If they turned it off, look up if we have joined a zone sync so we can attempt to leave it
            var zoneSync = _pairManager.Groups.Where(g => g.Value.PublicData.IsZoneSync);
            foreach (var sync in zoneSync)
            {
                _ = _apiController.GroupLeave(sync.Value);
            }
        }
    }


}