using Dalamud.Plugin.Services;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.UI.Components.Popup;
using MareSynchronos.UI.Handlers;
using MareSynchronos.UI.ModernUi;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class UiFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly IBroadcastManager _broadcastManager;
    private readonly UiTheme _theme;
    private readonly FileImageTransferHandler _fileImageTransferHandler;
    private readonly PairInviteManager _pairRequestManager;
    private readonly MareConfigService _mareConfigService;
    private readonly IpcManager _ipcManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;

    public UiFactory(ILoggerFactory loggerFactory, MareMediator mareMediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, ServerConfigurationManager serverConfigManager,
        MareProfileManager mareProfileManager, IBroadcastManager broadcastManager, PerformanceCollectorService performanceCollectorService, 
        UiTheme theme, FileImageTransferHandler fileImageTransferHandler, PairInviteManager pairRequestManager, MareConfigService mareConfigService,
        IpcManager ipcManager, FileUploadManager fileUploadManager, IdDisplayHandler idDisplayHandler, IClientState clientState, IPlayerState playerState)
    {
        _loggerFactory = loggerFactory;
        _mareMediator = mareMediator;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _serverConfigManager = serverConfigManager;
        _mareProfileManager = mareProfileManager;
        _broadcastManager = broadcastManager;
        _performanceCollectorService = performanceCollectorService;
        _theme = theme;
        _fileImageTransferHandler = fileImageTransferHandler;
        _pairRequestManager = pairRequestManager;
        _mareConfigService = mareConfigService;
        _ipcManager = ipcManager;
        _fileUploadManager = fileUploadManager;
        _idDisplayHandler = idDisplayHandler;
        _clientState = clientState;
        _playerState = playerState;
    }

    public SyncshellAdminUI CreateSyncshellAdminUi(GroupFullInfoDto dto)
    {
        return new SyncshellAdminUI(_loggerFactory.CreateLogger<SyncshellAdminUI>(), _mareMediator,
            _apiController, _uiSharedService, _broadcastManager, _pairManager, dto, _performanceCollectorService, _theme, _idDisplayHandler);
    }

    public SyncshellProfileUi CreateSyncshellProfileUi(GroupFullInfoDto dto)
    {
        return new SyncshellProfileUi(_loggerFactory.CreateLogger<SyncshellProfileUi>(), _mareMediator,
            _uiSharedService, dto, _performanceCollectorService);
    }

    public StandaloneProfileUi CreateStandaloneProfileUi(Pair pair)
    {
        return new StandaloneProfileUi(_loggerFactory.CreateLogger<StandaloneProfileUi>(), _mareMediator, _uiSharedService, _serverConfigManager, 
            _mareProfileManager, _pairManager, pair, _mareConfigService, _performanceCollectorService, _theme, _fileImageTransferHandler, _pairRequestManager);
    }

    public PermissionWindowUI CreatePermissionPopupUi(Pair pair)
    {
        return new PermissionWindowUI(_loggerFactory.CreateLogger<PermissionWindowUI>(), pair,
            _mareMediator, _uiSharedService, _apiController, _performanceCollectorService);
    }

    public StageDetailsUi CreateStageDetailsUi(StageFullInfoDto? startingStageInfo, string? owningGroupId)
    {
        return new StageDetailsUi(_loggerFactory.CreateLogger<StageDetailsUi>(), _mareMediator, _performanceCollectorService, startingStageInfo, owningGroupId, _apiController, _pairManager, _ipcManager, _fileUploadManager, _uiSharedService, _idDisplayHandler, _clientState, _playerState);
    }
}
