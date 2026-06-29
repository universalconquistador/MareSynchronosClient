using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace MareSynchronos.PlayerData.Pairs;

public sealed class PairLifecycleManager : DisposableMediatorSubscriberBase
{
    private readonly MareConfigService _configurationService;
    private readonly PairManager _pairManager;
    private readonly DalamudUtilService _dalamudUtilService;
    private int _isInitializePairsRunning;
    private bool _isZoning = false;

    public PairLifecycleManager(ILogger<PairLifecycleManager> logger, PairManager pairManager, DalamudUtilService dalamudUtilService,
                MareConfigService configurationService, MareMediator mediator)
        : base(logger, mediator)
    {
        _configurationService = configurationService;
        _pairManager = pairManager;
        _dalamudUtilService = dalamudUtilService;

        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) => _isZoning = true);
        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => _isZoning = false);
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => InitializePairs());

        Logger.LogTrace("{class} created.", nameof(PairLifecycleManager));
    }

    private void InitializePairs()
    {
        if (Interlocked.Exchange(ref _isInitializePairsRunning, 1) == 1)
            return;

        try
        {
            if (_isZoning)
            {
                return;
            }

            var visiblePlayerIdents = _dalamudUtilService.GetVisiblePlayerIdents();
            if (visiblePlayerIdents == null || !visiblePlayerIdents.Any())
            {
                return;
            }

            var allUninitializedPairs = _pairManager.GetAllUninitializedPairs();
            var pairsToInitialize = allUninitializedPairs.Where(pair => visiblePlayerIdents.Contains(pair.Ident, StringComparer.OrdinalIgnoreCase));
            if (pairsToInitialize == null || !pairsToInitialize.Any())
            {
                return;
            }

            foreach (var pair in pairsToInitialize)
            {
                if (string.IsNullOrEmpty(pair.PlayerName))
                {
                    var pc = _dalamudUtilService.FindPlayerByNameHash(pair.Ident); // This kicks everything off once we can discern the Pair's character name
                    if (pc == default((string, nint))) return;
                    Logger.LogDebug("One-Time Initializing {pair}", pair);
                    pair.Initialize(pc.Name);
                    Logger.LogDebug("One-Time Initialized {pair}", pair);
                    Mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PairHandler), EventSeverity.Informational,
                        $"Initializing User For Character {pc.Name}")));
                }
            }
        }
        finally
        {
            Volatile.Write(ref _isInitializePairsRunning, 0);
        }
    }
}