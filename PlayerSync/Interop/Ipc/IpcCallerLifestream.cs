using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerLifestream : IIpcCaller
{
    private readonly ICallGateSubscriber<IDalamudPlugin> _lifestreamInstance;
    private readonly ICallGateSubscriber<string, object> _lifestreamExecuteCommand;

    private readonly ILogger<IpcCallerLifestream> _logger;

    public IpcCallerLifestream(ILogger<IpcCallerLifestream> logger, IDalamudPluginInterface pluginInterface, DalamudUtilService dalamudUtil, MareMediator mareMediator)
    {
        _logger = logger;

        _lifestreamInstance = pluginInterface.GetIpcSubscriber<IDalamudPlugin>("Lifestream.Instance");
        _lifestreamExecuteCommand = pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");

        CheckAPI();
    }

    public bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        try
        {
            APIAvailable = _lifestreamInstance.HasFunction && _lifestreamExecuteCommand.HasAction;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed checking Lifestream IPC availability.");
            APIAvailable = false;
        }
    }

    public bool TryExecuteCommand(string command)
    {
        if (!APIAvailable || string.IsNullOrWhiteSpace(command))
            return false;

        try
        {
            _lifestreamExecuteCommand.InvokeAction(command);
            return true;
        }
        catch (IpcNotReadyError exception)
        {
            _logger.LogDebug(exception, "Lifestream ExecuteCommand IPC is not ready.");
            return false;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}