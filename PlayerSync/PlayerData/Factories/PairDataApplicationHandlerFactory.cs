using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayerSync.FileCache;
using PlayerSync.Utils;

namespace MareSynchronos.PlayerData.Factories;

public class PairDataApplicationHandlerFactory
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileDownloadManagerFactory _fileDownloadManagerFactory;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IpcManager _ipcManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly PlayerPerformanceService _playerPerformanceService;
    private readonly PluginWarningNotificationService _pluginWarningNotificationManager;
    private readonly ICompressedAlternateManager _compressedAlternateManager;
    private readonly PlayerPerformanceConfigService _performanceConfig;
    private readonly MareConfigService _mareConfigService;
    private readonly IDataManager _dataManager;
    private readonly ZoneSyncConfigService _zoneSyncConfigService;
    //private readonly JsonlStreamLogger _jsonlStreamLogger;

    public PairDataApplicationHandlerFactory(ILoggerFactory loggerFactory, GameObjectHandlerFactory gameObjectHandlerFactory, IpcManager ipcManager,
        FileDownloadManagerFactory fileDownloadManagerFactory, DalamudUtilService dalamudUtilService,
        PluginWarningNotificationService pluginWarningNotificationManager, IHostApplicationLifetime hostApplicationLifetime,
        FileCacheManager fileCacheManager, MareMediator mareMediator, PlayerPerformanceService playerPerformanceService,
        ICompressedAlternateManager compressedAlternateManager, PlayerPerformanceConfigService performanceConfig, 
        MareConfigService mareConfigService, IDataManager dataManager, ZoneSyncConfigService zoneSyncConfigService)
    {
        _loggerFactory = loggerFactory;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _fileDownloadManagerFactory = fileDownloadManagerFactory;
        _dalamudUtilService = dalamudUtilService;
        _pluginWarningNotificationManager = pluginWarningNotificationManager;
        _hostApplicationLifetime = hostApplicationLifetime;
        _fileCacheManager = fileCacheManager;
        _mareMediator = mareMediator;
        _playerPerformanceService = playerPerformanceService;
        _compressedAlternateManager = compressedAlternateManager;
        _performanceConfig = performanceConfig;
        _mareConfigService = mareConfigService;
        _dataManager = dataManager;
        _zoneSyncConfigService = zoneSyncConfigService;
        //_jsonlStreamLogger = jsonlStreamLogger;
    }

    public PairDataApplicationHandler Create(Pair pair)
    {
        return new PairDataApplicationHandler(_loggerFactory.CreateLogger<PairDataApplicationHandler>(), pair, _gameObjectHandlerFactory, _ipcManager, 
            _fileDownloadManagerFactory.Create(), _pluginWarningNotificationManager, _dalamudUtilService, _hostApplicationLifetime,
            _fileCacheManager, _mareMediator, _playerPerformanceService, _compressedAlternateManager, 
            _mareConfigService, _performanceConfig, _zoneSyncConfigService, _dataManager);
    }
}