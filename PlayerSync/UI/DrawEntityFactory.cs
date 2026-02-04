using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace MareSynchronos.UI;

public class DrawEntityFactory
{
    private readonly ILogger<DrawEntityFactory> _logger;
    private readonly ApiController _apiController;
    private readonly MareMediator _mediator;
    private readonly SelectPairForTagUi _selectPairForTagUi;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;
    private readonly CharaDataManager _charaDataManager;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly TagHandler _tagHandler;
    private readonly IdDisplayHandler _uidDisplayHandler;
    private readonly IBroadcastManager _broadcastManager;
    private readonly PairManager _pairManager;

    public DrawEntityFactory(ILogger<DrawEntityFactory> logger, ApiController apiController, IdDisplayHandler uidDisplayHandler,
        SelectTagForPairUi selectTagForPairUi, MareMediator mediator,
        TagHandler tagHandler, SelectPairForTagUi selectPairForTagUi,
        ServerConfigurationManager serverConfigurationManager, UiSharedService uiSharedService,
        PlayerPerformanceConfigService playerPerformanceConfigService, CharaDataManager charaDataManager, PairManager pairManager,
        IBroadcastManager broadcastManager)
    {
        _logger = logger;
        _apiController = apiController;
        _uidDisplayHandler = uidDisplayHandler;
        _selectTagForPairUi = selectTagForPairUi;
        _mediator = mediator;
        _tagHandler = tagHandler;
        _selectPairForTagUi = selectPairForTagUi;
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
        _playerPerformanceConfigService = playerPerformanceConfigService;
        _charaDataManager = charaDataManager;
        _broadcastManager = broadcastManager;
        _pairManager = pairManager;
    }

    public DrawFolderGroup CreateDrawGroupFolder(GroupFullInfoDto groupFullInfoDto,
        Dictionary<Pair, List<GroupFullInfoDto>> filteredPairs,
        IImmutableList<Pair> allPairs)
    {
        return new DrawFolderGroup(groupFullInfoDto.Group.GID, groupFullInfoDto, _apiController,
            filteredPairs.Select(p => CreateDrawPair(groupFullInfoDto.Group.GID + p.Key.UserData.UID, p.Key, p.Value, groupFullInfoDto)).ToImmutableList(),
            allPairs, _tagHandler, _uidDisplayHandler, _mediator, _pairManager, _serverConfigurationManager, _uiSharedService, _broadcastManager);
    }

    public DrawFolderTag CreateDrawTagFolder(string tag,
        Dictionary<Pair, List<GroupFullInfoDto>> filteredPairs,
        IImmutableList<Pair> allPairs)
    {
        return new(tag, filteredPairs.Select(u => CreateDrawPair(tag, u.Key, u.Value, null)).ToImmutableList(),
            allPairs, _tagHandler, _apiController, _selectPairForTagUi, _serverConfigurationManager, _uiSharedService);
    }

    public DrawUserPair CreateDrawPair(string id, Pair user, List<GroupFullInfoDto> groups, GroupFullInfoDto? currentGroup)
    {
        return new DrawUserPair(id + user.UserData.UID, user, groups, currentGroup, _apiController, _uidDisplayHandler,
            _mediator, _selectTagForPairUi, _serverConfigurationManager, _uiSharedService, _playerPerformanceConfigService,
            _charaDataManager);
    }

    public DrawBroadcastGroup CreateDrawBroadcastGroup(GroupBroadcastDto broadcast, IReadOnlyList<GroupFullInfoDto> groups)
    {
        return new DrawBroadcastGroup(broadcast.Group.GID, broadcast, groups, _apiController, _mediator, _serverConfigurationManager, _uiSharedService, _broadcastManager);
    }

    public DrawFolderBroadcasts CreateDrawFolderBroadcasts(IReadOnlyList<GroupBroadcastDto> broadcasts, List<GroupFullInfoDto> groups)
    {
        return new DrawFolderBroadcasts(broadcasts.OrderByDescending(broadcast => broadcast.CurrentMemberCount).Select(broadcast => CreateDrawBroadcastGroup(broadcast, groups)).ToImmutableList(), _tagHandler, _uiSharedService);
    }
}