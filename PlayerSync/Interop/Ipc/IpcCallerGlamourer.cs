using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerGlamourer : DisposableMediatorSubscriberBase, IIpcCaller
{
    private readonly ILogger<IpcCallerGlamourer> _logger;
    private readonly IDalamudPluginInterface _pi;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;

    private readonly ApiVersion _glamourerApiVersions;
    private readonly ApplyState? _glamourerApplyAll;
    private readonly GetStateBase64? _glamourerGetAllCustomization;
    private readonly RevertState _glamourerRevert;
    private readonly RevertStateName _glamourerRevertByName;
    private readonly UnlockState _glamourerUnlock;
    private readonly UnlockStateName _glamourerUnlockByName;
    private readonly EventSubscriber<nint>? _glamourerStateChanged;

    private bool _shownGlamourerUnavailable = false;
    private readonly uint LockCode = 0x73796E63;

    public IpcCallerGlamourer(ILogger<IpcCallerGlamourer> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mareMediator) : base(logger, mareMediator)
    {
        _glamourerApiVersions = new ApiVersion(pi);
        _glamourerGetAllCustomization = new GetStateBase64(pi);
        _glamourerApplyAll = new ApplyState(pi);
        _glamourerRevert = new RevertState(pi);
        _glamourerRevertByName = new RevertStateName(pi);
        _glamourerUnlock = new UnlockState(pi);
        _glamourerUnlockByName = new UnlockStateName(pi);

        _logger = logger;
        _pi = pi;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;
        CheckAPI();

        _glamourerStateChanged = StateChanged.Subscriber(pi, GlamourerChanged);
        _glamourerStateChanged.Enable();

        Mediator.Subscribe<DalamudLoginMessage>(this, s => _shownGlamourerUnavailable = false);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _glamourerStateChanged?.Dispose();
    }

    public bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        bool apiAvailable = false;
        try
        {
            bool versionValid = (_pi.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, "Glamourer", StringComparison.OrdinalIgnoreCase))
                ?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 3, 0, 10);
            try
            {
                var version = _glamourerApiVersions.Invoke();
                if (version is { Major: 1, Minor: >= 1 } && versionValid)
                {
                    apiAvailable = true;
                }
            }
            catch
            {
                // ignore
            }
            _shownGlamourerUnavailable = _shownGlamourerUnavailable && !apiAvailable;

            APIAvailable = apiAvailable;
        }
        catch
        {
            APIAvailable = apiAvailable;
        }
        finally
        {
            if (!apiAvailable && !_shownGlamourerUnavailable)
            {
                _shownGlamourerUnavailable = true;
                _mareMediator.Publish(new NotificationMessage("Glamourer inactive", "Your Glamourer installation is not active or out of date. Update Glamourer to continue to use PlayerSync. If you just updated Glamourer, ignore this message.",
                    NotificationType.Error));
            }
        }
    }

    public void ApplyAllOnFramework(ushort index, string customization, Guid applicationId)
    {
        if (!APIAvailable || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning)
        {
            return;
        }

        try
        {
            _glamourerApplyAll!.Invoke(customization, index, LockCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{appid}] Failed to apply Glamourer data", applicationId);
        }
    }
    public string GetCharacterCustomizationOnFramework(ushort index)
    {
        if (!APIAvailable)
        {
            return string.Empty;
        }

        try
        {
            return _glamourerGetAllCustomization!.Invoke(index).Item2 ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Glamourer IPC failed!");

            return string.Empty;
        }
    }

    public unsafe void RevertOnFramework(ILogger logger, Guid applicationId, nint address)
    {
        if ((!APIAvailable) || _dalamudUtil.IsZoning)
        {
            return;
        }

        try
        {
            var chara = (Character*)address;
            var idx = chara->ObjectIndex;
            _glamourerUnlock.Invoke(idx, LockCode);
            _glamourerRevert.Invoke(idx, LockCode);
        }
        catch
        {
            logger.LogWarning("[{appid}] Error during GlamourerRevert for", applicationId);
        }
    }

    public void RevertByNameOnFramework(ILogger logger, string name, Guid applicationId)
    {
        if ((!APIAvailable) || _dalamudUtil.IsZoning)
        {
            return;
        }

        try
        {
            logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
            _glamourerRevertByName.Invoke(name, LockCode);
            logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
            _glamourerUnlockByName.Invoke(name, LockCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Glamourer RevertByName");
        }
    }

    private void GlamourerChanged(nint address)
    {
        _mareMediator.Publish(new GlamourerChangedMessage(_dalamudUtil.GetOwnedObjectKindOnFramework(address)));
    }
}
