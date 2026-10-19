using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
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

    public bool RegisterActorOnFramework(nint address)
    {
        if (!APIAvailable || address == nint.Zero)
        {
            return false;
        }

        try
        {
            var result = _lociRegister.Invoke(address, IdentifierTag);

            if (result is not (LociApiEc.Success or LociApiEc.NoChange))
            {
                _logger.LogWarning("Loci failed to register actor {ActorAddress}. Error: {ErrorCode}", address.ToString("X"), result);
            }

            return result is LociApiEc.Success or LociApiEc.NoChange;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error registering actor {ActorAddress}", address.ToString("X"));

            return false;
        }
    }

    public void SetActorManagerOnFramework(nint address, string data)
    {
        if (!APIAvailable || address == nint.Zero)
        {
            return;
        }

        try
        {
            _lociSetManagerByPtr.Invoke(address, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting status manager for actor {ActorAddress}", address.ToString("X"));
        }
    }

    public void UnregisterActorFromFramework(nint address)
    {
        _dalamudUtil.EnsureIsOnFramework();

        if (!APIAvailable || address == nint.Zero)
        {
            return;
        }

        _lociUnregister.Invoke(address, IdentifierTag);
    }

    public unsafe void UnregisterBuddyFromFramework(string playerName, nint address)
    {
        if (!APIAvailable)
        {
            return;
        }

        var chara = (Character*)address;
        var buddyName = chara->GameObject.NameString;

        try
        {
            var res = _lociUnregisterName.Invoke(playerName, buddyName, IdentifierTag, true);

            if (res is not (LociApiEc.Success or LociApiEc.NoChange))
                _logger.LogWarning("Failed to unregister buddy {BuddyName} of player {PlayerName}. Error: {ErrorCode}", buddyName, playerName, res);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error Unregistering buddy {BuddyName} of player {PlayerName}: ", buddyName, playerName);
        }
    }



    /// <inheritdoc cref="LociApi.Ipc.GetManagerByPtr"/>
    public string GetActorManagerOnFramework(nint actorAddr)
    {
        _dalamudUtil.EnsureIsOnFramework();

        if (!APIAvailable || actorAddr == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            var (ec, res) = _lociGetManagerByPtr.Invoke(actorAddr);

            if (ec != LociApiEc.Success)
            {
                _logger.LogWarning("Failed to get status manager for actor {ActorAddress}. Error: {ErrorCode}", actorAddr.ToString("X"), ec);
            }

            return res ?? string.Empty;
        }
        catch
        {
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
        _mareMediator.Publish(new LociUpdateMessage(_dalamudUtil.GetOwnedObjectKindOnFramework(address)));
    }
}
