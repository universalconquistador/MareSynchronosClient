using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin;
using Microsoft.Extensions.Logging;
using Stagehand.Api;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerStagehand : IIpcCaller
{
    private readonly ILogger _logger;
    private readonly IStagehandApiConsumer _stagehandApi;

    public bool APIAvailable => AvailableApiRevision.HasValue;
    public ApiRevision? AvailableApiRevision { get; private set; } = null;
    public IStagehandApi StagehandApi => _stagehandApi;
    public event Action<bool>? ApiAvailableChanged;

    public IpcCallerStagehand(ILogger<IpcCallerStagehand> logger, IDalamudPluginInterface dalamudPluginInterface)
    {
        _logger = logger;

        _stagehandApi = Stagehand.Api.StagehandApi.CreateIpcClient(dalamudPluginInterface);

        CheckAPI();
    }

    public void CheckAPI()
    {
        bool wasAvailable = APIAvailable;
        var isAvailable = _stagehandApi.CheckApiAvailability() == StagehandApiAvailability.Available;

        if (isAvailable)
        {
            AvailableApiRevision = _stagehandApi.GetPluginApiRevision();
            if (!wasAvailable)
            {
                _logger.LogInformation("Stagehand API rev {major}.{minor} connected.", AvailableApiRevision.Value.Major, AvailableApiRevision.Value.Minor);
                ApiAvailableChanged?.Invoke(isAvailable);
            }
        }
        else if (!isAvailable)
        {
            AvailableApiRevision = null;
            if (wasAvailable)
            {
                _logger.LogInformation("Stagehand API disconnected.");
                ApiAvailableChanged?.Invoke(isAvailable);
            }
        }
    }

    public void Dispose()
    {
        _stagehandApi.Dispose();
    }
}
