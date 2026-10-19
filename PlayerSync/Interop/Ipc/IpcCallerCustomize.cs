using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerCustomize : IIpcCaller
{
    private readonly ICallGateSubscriber<(int, int)> _customizePlusApiVersion;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _customizePlusGetActiveProfile;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _customizePlusGetProfileById;
    private readonly ICallGateSubscriber<ushort, Guid, object> _customizePlusOnScaleUpdate;
    private readonly ICallGateSubscriber<ushort, int> _customizePlusRevertCharacter;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _customizePlusSetBodyScaleToCharacter;
    private readonly ICallGateSubscriber<Guid, int> _customizePlusDeleteByUniqueId;
    private readonly ILogger<IpcCallerCustomize> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;

    public IpcCallerCustomize(ILogger<IpcCallerCustomize> logger, IDalamudPluginInterface dalamudPluginInterface,
        DalamudUtilService dalamudUtil, MareMediator mareMediator)
    {
        _customizePlusApiVersion = dalamudPluginInterface.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        _customizePlusGetActiveProfile = dalamudPluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        _customizePlusGetProfileById = dalamudPluginInterface.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        _customizePlusRevertCharacter = dalamudPluginInterface.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
        _customizePlusSetBodyScaleToCharacter = dalamudPluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        _customizePlusOnScaleUpdate = dalamudPluginInterface.GetIpcSubscriber<ushort, Guid, object>("CustomizePlus.Profile.OnUpdate");
        _customizePlusDeleteByUniqueId = dalamudPluginInterface.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");

        _customizePlusOnScaleUpdate.Subscribe(OnCustomizePlusScaleChange);
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;

        CheckAPI();
    }

    public bool APIAvailable { get; private set; } = false;

    public Guid? SetBodyScaleOnFramework(ushort index, string decodedScale)
    {
        if (!APIAvailable)
        {
            return null;
        }

        if (string.IsNullOrEmpty(decodedScale))
        {
            _customizePlusRevertCharacter.InvokeFunc(index);

            return null;
        }

        return _customizePlusSetBodyScaleToCharacter.InvokeFunc(index, decodedScale).Item2;
    }
    public void RevertByIdFromFramework(Guid? profileId)
    {
        if (!APIAvailable || profileId == null)
        {
            return;
        }

        _customizePlusDeleteByUniqueId.InvokeFunc(profileId.Value);
    }

    // This must be called from within a Framework thread
    public string GetScaleOnFramework(ushort index)
    {
        if (!APIAvailable)
        {
            return string.Empty;
        }

        try
        {
            var res = _customizePlusGetActiveProfile.InvokeFunc(index);
            if (res.Item1 != 0 || res.Item2 == null)
            {
                return string.Empty;
            }

            var scale = _customizePlusGetProfileById.InvokeFunc(res.Item2.Value).Item2;
            if (string.IsNullOrEmpty(scale))
            {
                return string.Empty;
            }

            return scale;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cutomize+ IPC failed!");

            return string.Empty;
        }
    }

    public void CheckAPI()
    {
        try
        {
            var version = _customizePlusApiVersion.InvokeFunc();
            APIAvailable = (version.Item1 == 6 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    private void OnCustomizePlusScaleChange(ushort c, Guid g)
    {
        if (!_dalamudUtil.IsOnFrameworkThread)
        {
            return;
        }
        var obj = _dalamudUtil.GetCharacterFromObjectTableByIndex(c);
        _mareMediator.Publish(new CustomizePlusMessage(obj == null ? null : _dalamudUtil.GetOwnedObjectKindOnFramework(obj.Address), obj == null));
    }

    public void Dispose()
    {
        _customizePlusOnScaleUpdate.Unsubscribe(OnCustomizePlusScaleChange);
    }
}
