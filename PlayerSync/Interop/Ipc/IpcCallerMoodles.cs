using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
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
        if (characterPtr == nint.Zero) return;
        _mareMediator.Publish(new MoodlesMessage(characterPtr));
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

    public async Task<string?> GetStatusAsync(nint address)
    {
        if (!APIAvailable) return null;

        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(() => _moodlesGetStatus.InvokeFunc(address)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Get Moodles Status");
            return null;
        }
    }

    public async Task SetStatusAsync(nint pointer, string status)
    {
        if (!APIAvailable) return;

        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _moodlesSetStatus.InvokeAction(pointer, status)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }

    public async Task RevertStatusAsync(nint pointer)
    {
        if (!APIAvailable) return;

        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _moodlesRevertStatus.InvokeAction(pointer)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Revert Moodles Status");
        }
    }
}
