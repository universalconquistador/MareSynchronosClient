using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerPetNames : IIpcCaller
{
    private readonly ILogger<IpcCallerPetNames> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;

    private readonly ICallGateSubscriber<object> _petnamesReady;
    private readonly ICallGateSubscriber<object> _petnamesDisposing;
    private readonly ICallGateSubscriber<(uint, uint)> _apiVersion;
    private readonly ICallGateSubscriber<bool> _enabled;

    private readonly ICallGateSubscriber<string, object> _playerDataChanged;
    private readonly ICallGateSubscriber<string> _getPlayerData;
    private readonly ICallGateSubscriber<string, object> _setPlayerData;
    private readonly ICallGateSubscriber<ushort, object> _clearPlayerData;

    public IpcCallerPetNames(ILogger<IpcCallerPetNames> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil,
        MareMediator mareMediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;

        _petnamesReady = pi.GetIpcSubscriber<object>("PetRenamer.OnReady");
        _petnamesDisposing = pi.GetIpcSubscriber<object>("PetRenamer.OnDisposing");
        _apiVersion = pi.GetIpcSubscriber<(uint, uint)>("PetRenamer.ApiVersion");
        _enabled = pi.GetIpcSubscriber<bool>("PetRenamer.IsEnabled");

        _playerDataChanged = pi.GetIpcSubscriber<string, object>("PetRenamer.OnPlayerDataChanged");
        _getPlayerData = pi.GetIpcSubscriber<string>("PetRenamer.GetPlayerData");
        _setPlayerData = pi.GetIpcSubscriber<string, object>("PetRenamer.SetPlayerData");
        _clearPlayerData = pi.GetIpcSubscriber<ushort, object>("PetRenamer.ClearPlayerData");

        _petnamesReady.Subscribe(OnPetNicknamesReady);
        _petnamesDisposing.Subscribe(OnPetNicknamesDispose);
        _playerDataChanged.Subscribe(OnLocalPetNicknamesDataChange);

        CheckAPI();
    }

    public bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            APIAvailable = _enabled?.InvokeFunc() ?? false;
            if (APIAvailable)
            {
                APIAvailable = _apiVersion?.InvokeFunc() is { Item1: 4, Item2: >= 0 };
            }
        }
        catch
        {
            APIAvailable = false;
        }
    }

    private void OnPetNicknamesReady()
    {
        CheckAPI();
        _mareMediator.Publish(new PetNamesReadyMessage());
    }

    private void OnPetNicknamesDispose()
    {
        _mareMediator.Publish(new PetNamesMessage(string.Empty));
    }

    public string GetLocalNamesOnFramework()
    {
        if (!APIAvailable)
        {
            return string.Empty;
        }

        try
        {
            string localNameData = _getPlayerData.InvokeFunc();

            return string.IsNullOrEmpty(localNameData) ? string.Empty : localNameData;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not obtain Pet Nicknames data");
        }

        return string.Empty;
    }

    public void SetPlayerDataOnFramework(ushort index, string playerData)
    {
        if (!APIAvailable)
        {
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(playerData))
            {
                _clearPlayerData.InvokeAction(index);
            }
            else
            {
                _setPlayerData.InvokeAction(playerData);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply Pet Nicknames data");
        }
    }

    public unsafe void ClearPlayerDataOnFramework(nint address)
    {
        if (!APIAvailable)
        {
            return;
        }

        _clearPlayerData.InvokeAction(((BattleChara*)address)->ObjectIndex);
    }

    private void OnLocalPetNicknamesDataChange(string data)
    {
        _mareMediator.Publish(new PetNamesMessage(data));
    }

    public void Dispose()
    {
        _petnamesReady.Unsubscribe(OnPetNicknamesReady);
        _petnamesDisposing.Unsubscribe(OnPetNicknamesDispose);
        _playerDataChanged.Unsubscribe(OnLocalPetNicknamesDataChange);
    }
}
