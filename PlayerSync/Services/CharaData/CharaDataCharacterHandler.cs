using MareSynchronos.Interop.Ipc;
using MareSynchronos.PlayerData;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class CharaDataCharacterHandler : DisposableMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly IpcManager _ipcManager;
    private readonly HashSet<HandledCharaDataEntry> _handledCharaData = [];

    public IEnumerable<HandledCharaDataEntry> HandledCharaData
    {
        get
        {
            lock (_handledCharaData)
            {
                return _handledCharaData.ToArray();
            }
        }
    }

    public CharaDataCharacterHandler(ILogger<CharaDataCharacterHandler> logger, MareMediator mediator, DalamudUtilService dalamudUtilService, IpcManager ipcManager) : base(logger, mediator)
    {
        _dalamudUtilService = dalamudUtilService;
        _ipcManager = ipcManager;
        mediator.Subscribe<GposeEndMessage>(this, (__) =>
        {
            foreach (var chara in HandledCharaData)
            {
                _ = RevertHandledCharaSafely(chara);
            }
        });
        mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, (_) => HandleCutsceneFrameworkUpdate());
    }

    public async Task RevertChara(string name, Guid? cPlusId, bool isGpose = false)
    {
        Guid applicationId = Guid.NewGuid();
        bool gposeEnded = await _dalamudUtilService.RunOnFrameworkThread(() =>
        {
            bool gposeEnded = isGpose && !_dalamudUtilService.IsInGpose;
            var actor = isGpose && !gposeEnded
                ? _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name)
                : _dalamudUtilService.GetCharacterFromObjectTableByName(name);
            if (gposeEnded || actor == null)
            {
                _ipcManager.Glamourer.RevertByNameOnFramework(Logger, name, applicationId);
            }
            else
            {
                _ipcManager.Glamourer.RevertOnFramework(Logger, applicationId, actor.Address);
            }
            _ipcManager.CustomizePlus.RevertByIdFromFramework(cPlusId);

            return gposeEnded;
        }).ConfigureAwait(false);

        await Task.Delay(1000).ConfigureAwait(false);

        bool redrawing = await _dalamudUtilService.RunOnFrameworkThread(() =>
        {
            var actor = isGpose && !gposeEnded
                        ? _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name)
                        : _dalamudUtilService.GetCharacterFromObjectTableByName(name);
            if (actor == null)
            {
                return false;
            }

            return _ipcManager.Penumbra.RedrawOnFramework(Logger, applicationId, actor.Address);
        }).ConfigureAwait(false);

        if (redrawing)
        {
            await Task.Delay(20).ConfigureAwait(false);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 3000)
            {
                var state = await _dalamudUtilService.RunOnFrameworkThread(() =>
                {
                    var actor = isGpose && !gposeEnded
                        ? _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name)
                        : _dalamudUtilService.GetCharacterFromObjectTableByName(name);

                    return actor == null ? DrawState.Missing : _dalamudUtilService.GetCharacterDrawStateFromFramework(actor.Address).State;
                }).ConfigureAwait(false);
                if (state != DrawState.Loading)
                {
                    break;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    public Task<bool> RevertHandledChara(string name, bool isGpose = false)
    {
        HandledCharaDataEntry? handled;
        lock (_handledCharaData)
        {
            handled = _handledCharaData.FirstOrDefault(entry => entry.IsGpose == isGpose && string.Equals(entry.Name, name, StringComparison.Ordinal));
            if (handled != null)
            {
                _handledCharaData.Remove(handled);
            }
        }
        if (handled == null)
        {
            return Task.FromResult(false);
        }

        return RevertHandledCharaAsync(handled);
    }

    public Task RevertHandledChara(HandledCharaDataEntry? handled)
    {
        if (handled == null)
        {
            return Task.CompletedTask;
        }
        lock (_handledCharaData)
        {
            _handledCharaData.Remove(handled);
        }

        return RevertChara(handled.Name, handled.CustomizePlus, handled.IsGpose);
    }

    internal void AddHandledChara(HandledCharaDataEntry handledCharaDataEntry)
    {
        lock (_handledCharaData)
        {
            _handledCharaData.Add(handledCharaDataEntry);
        }
    }

    public void UpdateHandledData(Dictionary<string, CharaDataMetaInfoExtendedDto?> newData)
    {
        foreach (var handledData in HandledCharaData)
        {
            if (newData.TryGetValue(handledData.MetaInfo.FullId, out var metaInfo) && metaInfo != null)
            {
                handledData.MetaInfo = metaInfo;
            }
        }
    }

    private async Task RevertHandledCharaSafely(HandledCharaDataEntry handled)
    {
        try
        {
            await RevertHandledChara(handled).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to revert {name}", handled.Name);
        }
    }

    private void HandleCutsceneFrameworkUpdate()
    {
        if (!_dalamudUtilService.IsInGpose)
        {
            return;
        }
        foreach (var entry in HandledCharaData)
        {
            if (entry.IsGpose && _dalamudUtilService.GetGposeCharacterFromObjectTableByName(entry.Name) == null)
            {
                _ = RevertHandledCharaSafely(entry);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var chara in HandledCharaData)
        {
            _ = RevertHandledCharaSafely(chara);
        }
    }

    private async Task<bool> RevertHandledCharaAsync(HandledCharaDataEntry handled)
    {
        await RevertChara(handled.Name, handled.CustomizePlus, handled.IsGpose).ConfigureAwait(false);

        return true;
    }
}
