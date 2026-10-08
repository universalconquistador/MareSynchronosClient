using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.Interop.Ipc;

public class IpcProvider : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<IpcProvider> _logger;
    private readonly IDalamudPluginInterface _pi;
    private readonly CharaDataManager _charaDataManager;
    private readonly DalamudUtilService _dalamudUtil;
    private ICallGateProvider<string, IGameObject, bool>? _loadFileProvider;
    private ICallGateProvider<string, IGameObject, bool>? _loadFilePSProvider;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProvider;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncPSProvider;
    private ICallGateProvider<List<nint>>? _handledGameAddresses;
    private ICallGateProvider<List<nint>>? _handledGameAddressesPS;
    private readonly ConcurrentDictionary<GameObjectHandler, byte> _activeGameObjectHandlers = [];

    public MareMediator Mediator { get; init; }

    public IpcProvider(ILogger<IpcProvider> logger, IDalamudPluginInterface pi,
        CharaDataManager charaDataManager, MareMediator mareMediator, DalamudUtilService dalamudUtil)
    {
        _logger = logger;
        _pi = pi;
        _charaDataManager = charaDataManager;
        _dalamudUtil = dalamudUtil;
        Mediator = mareMediator;

        Mediator.Subscribe<GameObjectHandlerCreatedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.TryAdd(msg.GameObjectHandler, 0);
        });
        Mediator.Subscribe<GameObjectHandlerDestroyedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.TryRemove(msg.GameObjectHandler, out _);
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting IpcProviderService");
        _loadFileProvider = _pi.GetIpcProvider<string, IGameObject, bool>("MareSynchronos.LoadMcdf");
        _loadFileProvider.RegisterFunc(LoadMcdf);
        _loadFilePSProvider = _pi.GetIpcProvider<string, IGameObject, bool>("PlayerSync.LoadMcdf");
        _loadFilePSProvider.RegisterFunc(LoadMcdf);
        _loadFileAsyncProvider = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("MareSynchronos.LoadMcdfAsync");
        _loadFileAsyncProvider.RegisterFunc(LoadMcdfAsync);
        _loadFileAsyncPSProvider = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("PlayerSync.LoadMcdfAsync");
        _loadFileAsyncPSProvider.RegisterFunc(LoadMcdfAsync);
        _handledGameAddresses = _pi.GetIpcProvider<List<nint>>("MareSynchronos.GetHandledAddresses");
        _handledGameAddresses.RegisterFunc(GetHandledAddresses);
        _handledGameAddressesPS = _pi.GetIpcProvider<List<nint>>("PlayerSync.GetHandledAddresses");
        _handledGameAddressesPS.RegisterFunc(GetHandledAddresses);
        _logger.LogInformation("Started IpcProviderService");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        _loadFileProvider?.UnregisterFunc();
        _loadFilePSProvider?.UnregisterFunc();
        _loadFileAsyncProvider?.UnregisterFunc();
        _loadFileAsyncPSProvider?.UnregisterFunc();
        _handledGameAddresses?.UnregisterFunc();
        _handledGameAddressesPS?.UnregisterFunc();
        Mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    private Task<bool> LoadMcdfAsync(string path, IGameObject chara)
    {
        if (!_dalamudUtil.IsOnFrameworkThread || chara is not ICharacter character || !character.IsValid())
        {
            return Task.FromResult(false);
        }
        var actor = _dalamudUtil.GetGposeCharacterFromObjectTableByIndex(character.ObjectIndex);

        if (actor == null)
        {
            return Task.FromResult(false);
        }
        string name = actor.Name.ToString();

        return Task.Run(() => ApplyFileAsync(path, name));
    }

    private bool LoadMcdf(string path, IGameObject chara)
    {
        if (!_dalamudUtil.IsOnFrameworkThread || chara is not ICharacter character || !character.IsValid())
        {
            return false;
        }
        var actor = _dalamudUtil.GetGposeCharacterFromObjectTableByIndex(character.ObjectIndex);
        if (actor == null)
        {
            return false;
        }
        string name = actor.Name.ToString();
        _ = Task.Run(() => ApplyFileAsync(path, name));

        return true;
    }

    private async Task<bool> ApplyFileAsync(string path, string name)
    {
        var header = await _charaDataManager.LoadMcdf(path).ConfigureAwait(false);
        _charaDataManager.McdfApplyToTarget(name, header);

        return true;
    }

    private List<nint> GetHandledAddresses()
    {
        if (!_dalamudUtil.IsOnFrameworkThread)
        {
            return [];
        }

        return _activeGameObjectHandlers.Keys.Select(g => g.GetAddressFromFramework()).Where(address => address != nint.Zero).Distinct().ToList();
    }
}
