using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerMoodles : IIpcCaller
{
    private const int SupportedApiVersion = 4;

    private readonly ICallGateSubscriber<int> _moodlesApiVersion;
    private readonly ICallGateSubscriber<nint, object> _moodlesOnChange;
    private readonly ICallGateSubscriber<nint, string> _moodlesGetStatus;
    private readonly ICallGateSubscriber<nint, string, object> _moodlesSetStatus;
    private readonly ICallGateSubscriber<nint, object> _moodlesRevertStatus;
    private readonly ILogger<IpcCallerMoodles> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;

    private bool _subscribed;

    public IpcCallerMoodles(
        ILogger<IpcCallerMoodles> logger,
        IDalamudPluginInterface pi,
        DalamudUtilService dalamudUtil,
        MareMediator mareMediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;

        _moodlesApiVersion = pi.GetIpcSubscriber<int>("Moodles.Version");
        _moodlesOnChange = pi.GetIpcSubscriber<nint, object>("Moodles.StatusManagerModified");
        _moodlesGetStatus = pi.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtrV2");
        _moodlesSetStatus = pi.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
        _moodlesRevertStatus = pi.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtrV2");

        CheckAPI();
    }

    private void OnMoodlesChange(nint characterPtr)
    {
        if (characterPtr == nint.Zero)
        {
            return;
        }

        _mareMediator.Publish(new MoodlesMessage(_dalamudUtil.GetOwnedObjectKindOnFramework(characterPtr)));
    }

    public void SetStatusOnFramework(nint address, string status)
    {
        if (!APIAvailable || address == nint.Zero)
        {
            return;
        }

        try
        {
            _moodlesSetStatus.InvokeAction(address, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not Set Moodles Status");
        }
    }

    public bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        bool available;
        try
        {
            available = _moodlesApiVersion.InvokeFunc() == SupportedApiVersion;
        }
        catch
        {
            available = false;
        }

        if (available && !_subscribed)
        {
            _moodlesOnChange.Subscribe(OnMoodlesChange);
            _subscribed = true;
        }
        else if (!available && _subscribed)
        {
            _moodlesOnChange.Unsubscribe(OnMoodlesChange);
            _subscribed = false;
        }

        APIAvailable = available;
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            _moodlesOnChange.Unsubscribe(OnMoodlesChange);
            _subscribed = false;
        }
    }

    public string GetStatusOnFramework(nint address)
    {
        if (!APIAvailable)
        {
            return string.Empty;
        }

        try
        {
            return _moodlesGetStatus.InvokeFunc(address);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Get Moodles Status");

            return string.Empty;
        }
    }

    public unsafe void RevertStatusOnFramework(nint address)
    {
        if (!APIAvailable)
        {
            return;
        }

        _moodlesRevertStatus.InvokeAction(((BattleChara*)address)->ObjectIndex);
    }
}