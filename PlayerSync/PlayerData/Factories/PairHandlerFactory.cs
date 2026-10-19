using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Factories;

public class PairHandlerFactory
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly MareConfigService _configService;

    public PairHandlerFactory(ILoggerFactory loggerFactory, GameObjectHandlerFactory gameObjectHandlerFactory, 
        DalamudUtilService dalamudUtilService, MareMediator mareMediator, MareConfigService configService,
        ServerConfigurationManager serverConfigManager, MareConfigService mareConfigService)
    {
        _loggerFactory = loggerFactory;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _dalamudUtilService = dalamudUtilService;
        _mareMediator = mareMediator;
        _serverConfigManager = serverConfigManager;
        _configService = configService;
    }

    public PairHandler Create(Pair pair)
    {
        return new PairHandler(_loggerFactory.CreateLogger<PairHandler>(), pair, _gameObjectHandlerFactory, _dalamudUtilService, _mareMediator, _serverConfigManager, _configService);
    }
}