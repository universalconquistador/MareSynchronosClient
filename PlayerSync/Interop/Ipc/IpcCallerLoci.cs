using Dalamud.Plugin;
using LociApi.Enums;
using LociApi.Helpers;
using LociApi.Ipc;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerLoci : IIpcCaller
{
    private readonly ILogger<IpcCallerLoci> _logger;
    private readonly MareMediator _mareMediator;
    private readonly DalamudUtilService _dalamudUtil;

    private readonly ApiVersion _lociApiVersions;
    private readonly IsEnabled _lociIsEnabled;
    private readonly EventSubscriber _lociReady;
    private readonly EventSubscriber _lociDisposed;
    private readonly EventSubscriber<bool> _lociEnabledChanged;

    private readonly RegisterByPtr _lociRegister;
    private readonly RegisterByName _lociRegisterName;
    private readonly UnregisterByPtr _lociUnregister;
    private readonly UnregisterByName _lociUnregisterName;

    private readonly GetManager _lociGetManager;
    private readonly GetManagerByPtr _lociGetManagerByPtr;
    private readonly SetManagerByPtr _lociSetManagerByPtr;
    private readonly ClearManagerByPtr _lociClearManagerByPtr;
    private readonly ClearManagerByName _lociClearManagerByName;
    private readonly ConvertLegacyData _lociConvertLegacyData;
    private readonly EventSubscriber<nint, ManagerChangeType> _lociManagerModified;

    private readonly string IdentifierTag = "PlayerSync";

    public IpcCallerLoci(ILogger<IpcCallerLoci> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mediator)
    {
        _logger = logger;
        _mareMediator = mediator;
        _dalamudUtil = dalamudUtil;

        // State
        _lociApiVersions = new ApiVersion(pi);
        _lociIsEnabled = new IsEnabled(pi);
        _lociReady = Ready.Subscriber(pi, OnLociReady);
        _lociDisposed = Disposed.Subscriber(pi, OnLociDisposed);
        _lociEnabledChanged = EnabledStateChanged.Subscriber(pi, state => FeaturesEnabled = state);
        _lociEnabledChanged.Enable();

        // Registry
        _lociRegister = new RegisterByPtr(pi);
        _lociRegisterName = new RegisterByName(pi);
        _lociUnregister = new UnregisterByPtr(pi);
        _lociUnregisterName = new UnregisterByName(pi);

        // Manager
        _lociGetManager = new GetManager(pi);
        _lociGetManagerByPtr = new GetManagerByPtr(pi);
        _lociSetManagerByPtr = new SetManagerByPtr(pi);
        _lociClearManagerByPtr = new ClearManagerByPtr(pi);
        _lociClearManagerByName = new ClearManagerByName(pi);
        _lociManagerModified = ManagerChanged.Subscriber(pi, OnManagerModified);
        _lociManagerModified.Enable();

        // Support
        _lociConvertLegacyData = new ConvertLegacyData(pi);

        CheckAPI();
    }

    public void Dispose()
    {
        _lociReady.Dispose();
        _lociDisposed.Dispose();
        _lociEnabledChanged?.Dispose();
        _lociManagerModified?.Dispose();
    }

    public bool APIAvailable { get; private set; } = false;
    public bool FeaturesEnabled { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            var version = _lociApiVersions.Invoke();
            APIAvailable = (version.Major == 2 && version.Minor >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    // None of these methods really need to be try-catched but doing so to keep with consistency.
    /// <inheritdoc cref="LociApi.Ipc.RegisterByPtr"/>
    public async Task<bool> RegisterActor(nint address)
    {
        if (!APIAvailable) return false;
        var res = await _dalamudUtil.RunOnFrameworkThread(() => _lociRegister.Invoke(address, IdentifierTag)).ConfigureAwait(false);
        if (res is not (LociApiEc.Success or LociApiEc.NoChange))
            _logger.LogWarning("Loci failed to register actor {ActorAddress} with Loci. Error: {ErrorCode}", address.ToString("X"), res);
        return res is (LociApiEc.Success or LociApiEc.NoChange);
    }

    /// <inheritdoc cref="LociApi.Ipc.RegisterByName"/>
    public async Task<bool> RegisterPlayer(string playerNameWorld)
    {
        if (!APIAvailable) return false;

        try
        {
            var res = await _dalamudUtil.RunOnFrameworkThread(() => _lociRegisterName.Invoke(playerNameWorld, IdentifierTag)).ConfigureAwait(false);
            if (res is not (LociApiEc.Success or LociApiEc.NoChange))
                _logger.LogWarning("Loci failed to register player {PlayerNameWorld}. Error: {ErrorCode}", playerNameWorld, res);
            return res is (LociApiEc.Success or LociApiEc.NoChange);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error Registering player {PlayerNameWorld}: ", playerNameWorld);
            return false;
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.RegisterByName"/>
    public async Task<bool> RegisterBuddy(string playerName, string buddyName)
    {
        if (!APIAvailable) return false;

        try
        {
            var res = await _dalamudUtil.RunOnFrameworkThread(() => _lociRegisterName.Invoke(playerName, buddyName, IdentifierTag)).ConfigureAwait(false);
            if (res is not (LociApiEc.Success or LociApiEc.NoChange))
                _logger.LogWarning("Loci failed to register buddy {BuddyName} of player {PlayerName}. Error: {ErrorCode}", buddyName, playerName, res);
            return res is (LociApiEc.Success or LociApiEc.NoChange);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error Registering buddy {BuddyName} of player {PlayerName}: ", buddyName, playerName);
            return false;
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.UnregisterByPtr"/>
    public async Task UnregisterActor(nint address)
    {
        if (!APIAvailable) return;

        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _lociUnregister.Invoke(address, IdentifierTag)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error Unregistering actor {ActorAddress}: ", address.ToString("X"));
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.UnregisterByName"/>
    public async Task UnregisterPlayer(string playerNameWorld)
    {
        if (!APIAvailable) return;

        try
        {
            var res = await _dalamudUtil.RunOnFrameworkThread(() => _lociUnregisterName.Invoke(playerNameWorld, IdentifierTag)).ConfigureAwait(false);
            if (res is not (LociApiEc.Success or LociApiEc.NoChange))
                _logger.LogWarning("Failed to unregister player {PlayerNameWorld}. Error: {ErrorCode}", playerNameWorld, res);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error Unregistering player {PlayerNameWorld}: ", playerNameWorld);
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.UnregisterByName"/>
    public async Task UnregisterBuddy(string playerName, string buddyName)
    {
        if (!APIAvailable) return;

        try
        {
            var res = await _dalamudUtil.RunOnFrameworkThread(() => _lociUnregisterName.Invoke(playerName, buddyName, IdentifierTag, true)).ConfigureAwait(false);
            if (res is not (LociApiEc.Success or LociApiEc.NoChange))
                _logger.LogWarning("Failed to unregister buddy {BuddyName} of player {PlayerName}. Error: {ErrorCode}", buddyName, playerName, res);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error Unregistering buddy {BuddyName} of player {PlayerName}: ", buddyName, playerName);
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.GetManager"/>
    public async Task<string> GetOwnManager()
    {
        if (!APIAvailable) return string.Empty;

        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(() => _lociGetManager.Invoke().Item2 ?? string.Empty).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.GetManagerByPtr"/>
    public async Task<string> GetActorManager(nint actorAddr)
    {
        if (!APIAvailable) return string.Empty;

        try
        {
            var (ec, res) = await _dalamudUtil.RunOnFrameworkThread(() => _lociGetManagerByPtr.Invoke(actorAddr)).ConfigureAwait(false);
            if (ec != LociApiEc.Success)
                _logger.LogWarning("Failed to get status manager for actor {ActorAddress}. Error: {ErrorCode}", actorAddr.ToString("X"), ec);
            return res ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.SetManagerByPtr"/>
    public async Task SetActorManager(nint actorAddr, string dataStr)
    {
        if (!APIAvailable) return;

        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _lociSetManagerByPtr.Invoke(actorAddr, dataStr)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting status manager for actor {ActorAddress}: ", actorAddr.ToString("X"));
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.ClearManagerByPtr"/>
    public async Task ClearActorManager(nint actorAddr)
    {
        if (!APIAvailable) return;

        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _lociClearManagerByPtr.Invoke(actorAddr)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error clearing status manager for actor {ActorAddress}: ", actorAddr.ToString("X"));
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.ClearManagerByName"/>
    public async Task ClearPlayerManager(string playerNameWorld)
    {
        if (!APIAvailable) return;

        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _lociClearManagerByName.Invoke(playerNameWorld)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error clearing status manager for player {PlayerNameWorld}: ", playerNameWorld);
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.ClearManagerByName"/>
    public async Task ClearBuddyManager(string playerName, string buddyName)
    {
        if (!APIAvailable) return;

        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _lociClearManagerByName.Invoke(playerName, buddyName)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error clearing status manager for buddy {BuddyName} of player {PlayerName}: ", buddyName, playerName);
        }
    }

    /// <inheritdoc cref="LociApi.Ipc.ConvertLegacyData"/>
    public string ConvertToLociData(string legacyStatusManagerBase64)
    {
        if (!APIAvailable) return string.Empty;

        try
        {
            return _lociConvertLegacyData.Invoke(legacyStatusManagerBase64);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error converting legacy data to Loci format: ");
            return string.Empty;
        }
    }

    private void OnLociReady()
    {
        APIAvailable = true;
        FeaturesEnabled = _lociIsEnabled.Invoke();
        _mareMediator.Publish(new LociReadyMessage());
    }

    private void OnLociDisposed()
    {
        APIAvailable = false;
        FeaturesEnabled = false;
        _mareMediator.Publish(new LociDisposedMessage());
    }

    private void OnManagerModified(nint address, ManagerChangeType changeType)
    {
        _mareMediator.Publish(new LociUpdateMessage(address));
    }
}
