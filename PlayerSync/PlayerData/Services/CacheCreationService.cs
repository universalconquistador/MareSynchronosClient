using MareSynchronos.API.Data.Enum;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Services;

public sealed class CacheCreationService : DisposableMediatorSubscriberBase
{
    private readonly SemaphoreSlim _cacheCreateLock = new(1);
    private readonly HashSet<ObjectKind> _cachesToCreate = [];
    private readonly PlayerDataFactory _characterDataFactory;
    private readonly HashSet<ObjectKind> _currentlyCreating = [];
    private readonly HashSet<ObjectKind> _debouncedObjectCache = [];
    private readonly CharacterData _playerData = new();
    private readonly Dictionary<ObjectKind, GameObjectHandler> _playerRelatedObjects = [];
    private readonly CancellationTokenSource _runtimeCts = new();
    private CancellationTokenSource _creationCts = new();
    private CancellationTokenSource _debounceCts = new();
    private bool _haltCharaDataCreation;
    private bool _isZoning = false;

    // Experimental
    private readonly SemaphoreSlim _playerChangesLock = new(1);

    private readonly HashSet<PlayerChanges> _debouncedPlayerChanges = [];
    private readonly HashSet<PlayerChanges> _queuedPlayerChanges = [];
    private readonly HashSet<PlayerChanges> _currentlyProcessingPlayerChanges = [];
    private readonly Queue<PlayerChanges> _playerChangesToUpdate = [];

    private CancellationTokenSource _playerChangesCts = new();
    private CancellationTokenSource _playerChangesDebounceCts = new();

    public CacheCreationService(ILogger<CacheCreationService> logger, MareMediator mediator, GameObjectHandlerFactory gameObjectHandlerFactory,
        PlayerDataFactory characterDataFactory, DalamudUtilService dalamudUtil) : base(logger, mediator)
    {
        _characterDataFactory = characterDataFactory;

        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (msg) => _isZoning = true);
        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (msg) => _isZoning = false);

        Mediator.Subscribe<HaltCharaDataCreation>(this, (msg) =>
        {
            _haltCharaDataCreation = !msg.Resume;
        });

        Mediator.Subscribe<CreateCacheForObjectMessage>(this, (msg) =>
        {
            Logger.LogDebug("Received CreateCacheForObject for {handler}, updating", msg.ObjectToCreateFor);
            AddCacheToCreate(msg.ObjectToCreateFor.ObjectKind);
        });

        _playerRelatedObjects[ObjectKind.Player] = gameObjectHandlerFactory.Create(ObjectKind.Player, dalamudUtil.GetPlayerPtr, isWatched: true)
            .GetAwaiter().GetResult();
        _playerRelatedObjects[ObjectKind.MinionOrMount] = gameObjectHandlerFactory.Create(ObjectKind.MinionOrMount, () => dalamudUtil.GetMinionOrMountPtr(), isWatched: true)
            .GetAwaiter().GetResult();
        _playerRelatedObjects[ObjectKind.Pet] = gameObjectHandlerFactory.Create(ObjectKind.Pet, () => dalamudUtil.GetPetPtr(), isWatched: true)
            .GetAwaiter().GetResult();
        _playerRelatedObjects[ObjectKind.Companion] = gameObjectHandlerFactory.Create(ObjectKind.Companion, () => dalamudUtil.GetCompanionPtr(), isWatched: true)
            .GetAwaiter().GetResult();

        Mediator.Subscribe<ClassJobChangedMessage>(this, (msg) =>
        {
            if (msg.GameObjectHandler == _playerRelatedObjects[ObjectKind.Player])
            {
                AddCacheToCreate(ObjectKind.Player);
                AddCacheToCreate(ObjectKind.Pet);
            }
        });

        Mediator.Subscribe<ClearCacheForObjectMessage>(this, (msg) =>
        {
            if (msg.ObjectToCreateFor.ObjectKind == ObjectKind.Pet)
            {
                Logger.LogTrace("Received clear cache for {obj}, ignoring", msg.ObjectToCreateFor);
                return;
            }
            Logger.LogDebug("Clearing cache for {obj}", msg.ObjectToCreateFor);
            AddCacheToCreate(msg.ObjectToCreateFor.ObjectKind);
        });

        // Penumbra
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, (msg) =>
        {
            Logger.LogDebug("Received Penumbra Mod settings change, updating everything");
            AddCacheToCreate(ObjectKind.Player);
            AddCacheToCreate(ObjectKind.Pet);
            AddCacheToCreate(ObjectKind.MinionOrMount);
            AddCacheToCreate(ObjectKind.Companion);
        });

        // Glamourer
        Mediator.Subscribe<GlamourerChangedMessage>(this, (msg) =>
        {
            if (_isZoning) return;
            var changedType = _playerRelatedObjects.FirstOrDefault(f => f.Value.Address == msg.Address);
            if (!default(KeyValuePair<ObjectKind, GameObjectHandler>).Equals(changedType))
            {
                Logger.LogDebug("Received GlamourerChangedMessage for {kind}", changedType);
                AddCacheToCreate(changedType.Key);
            }
        });

        // Customize+
        Mediator.Subscribe<CustomizePlusMessage>(this, (msg) =>
        {
            if (_isZoning) return;
            foreach (var item in _playerRelatedObjects
                .Where(item => msg.Address == null
                || item.Value.Address == msg.Address).Select(k => k.Key))
            {
                Logger.LogDebug("Received CustomizePlus change, updating {obj}", item);
                AddCacheToCreate(item);
            }
        });

        // Heels
        Mediator.Subscribe<HeelsOffsetMessage>(this, (msg) =>
        {
            if (_isZoning)
            {
                return;
            }

            Logger.LogDebug("Received Heels Offset change, updating player changes");
            AddPlayerAddonPluginChangesToUpdate(PlayerChanges.Heels);
        });

        // Honorific
        Mediator.Subscribe<HonorificMessage>(this, (msg) =>
        {
            if (_isZoning)
            {
                return;
            }

            Logger.LogTrace("Current Honorific title: {old}  Changed title: {new}", _playerData.HonorificData, msg.NewHonorificTitle);
            if (!string.Equals(msg.NewHonorificTitle, _playerData.HonorificData, StringComparison.Ordinal))
            {
                Logger.LogDebug("Received Honorific change, updating player changes");
                AddPlayerAddonPluginChangesToUpdate(PlayerChanges.Honorific);
            }
        });

        // Pet Names
        Mediator.Subscribe<PetNamesMessage>(this, (msg) =>
        {
            if (_isZoning)
            {
                return;
            }

            if (!string.Equals(msg.PetNicknamesData, _playerData.PetNamesData, StringComparison.Ordinal))
            {
                Logger.LogDebug("Received Pet Nicknames change, updating player changes");
                AddPlayerAddonPluginChangesToUpdate(PlayerChanges.PetNames);
            }
        });

        // Moodles
        Mediator.Subscribe<MoodlesMessage>(this, (msg) =>
        {
            if (_isZoning)
            {
                return;
            }

            var changedType = _playerRelatedObjects.FirstOrDefault(f => f.Value.Address == msg.Address);
            if (!default(KeyValuePair<ObjectKind, GameObjectHandler>).Equals(changedType) && changedType.Key == ObjectKind.Player)
            {
                Logger.LogDebug("Received Moodles change, updating player changes");
                AddPlayerAddonPluginChangesToUpdate(PlayerChanges.Moodles);
            }
        });

        // Loci
        Mediator.Subscribe<LociUpdateMessage>(this, (msg) =>
        {
            if (_isZoning)
            {
                return;
            }

            var changedType = _playerRelatedObjects.FirstOrDefault(f => f.Value.Address == msg.Address);
            if (!default(KeyValuePair<ObjectKind, GameObjectHandler>).Equals(changedType))
            {
                Logger.LogDebug("Received Loci change for {kind}", changedType);
                AddCacheToCreate(changedType.Key);
            }
        });

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (msg) =>
        {
            ProcessCacheCreation();
            ProcessAddonPluginChanges();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _playerRelatedObjects.Values.ToList().ForEach(p => p.Dispose());
        _runtimeCts.Cancel();
        _runtimeCts.Dispose();
        _creationCts.Cancel();
        _creationCts.Dispose();

        // Experimental
        _playerChangesCts.Cancel();
        _playerChangesCts.Dispose();
        _playerChangesDebounceCts.Cancel();
        _playerChangesDebounceCts.Dispose();
        _playerChangesLock.Dispose();
    }

    private void AddCacheToCreate(ObjectKind kind = ObjectKind.Player)
    {
        _debounceCts.Cancel();
        _debounceCts.Dispose();
        _debounceCts = new();
        var token = _debounceCts.Token;
        _cacheCreateLock.Wait();
        _debouncedObjectCache.Add(kind);
        _cacheCreateLock.Release();

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            Logger.LogTrace("Debounce complete, inserting objects to create for: {obj}", string.Join(", ", _debouncedObjectCache));
            await _cacheCreateLock.WaitAsync(token).ConfigureAwait(false);
            foreach (var item in _debouncedObjectCache)
            {
                _cachesToCreate.Add(item);
            }
            _debouncedObjectCache.Clear();
            _cacheCreateLock.Release();
        });
    }

    private void ProcessCacheCreation()
    {
        if (_isZoning || _haltCharaDataCreation) return;

        if (_cachesToCreate.Count == 0) return;

        if (_playerRelatedObjects.Any(p => p.Value.CurrentDrawCondition is
            not (GameObjectHandler.DrawCondition.None or GameObjectHandler.DrawCondition.DrawObjectZero or GameObjectHandler.DrawCondition.ObjectZero)))
        {
            Logger.LogDebug("Waiting for draw to finish before executing cache creation");
            return;
        }

        _creationCts.Cancel();
        _creationCts.Dispose();
        _creationCts = new();
        _cacheCreateLock.Wait(_creationCts.Token);
        var objectKindsToCreate = _cachesToCreate.ToList();
        foreach (var creationObj in objectKindsToCreate)
        {
            _currentlyCreating.Add(creationObj);
        }
        _cachesToCreate.Clear();
        _cacheCreateLock.Release();

        _ = Task.Run(async () =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_creationCts.Token, _runtimeCts.Token);

            await Task.Delay(TimeSpan.FromSeconds(1), linkedCts.Token).ConfigureAwait(false);

            Logger.LogDebug("Creating Caches for {objectKinds}", string.Join(", ", objectKindsToCreate));

            try
            {
                Dictionary<ObjectKind, CharacterDataFragment?> createdData = [];
                foreach (var objectKind in _currentlyCreating)
                {
                    createdData[objectKind] = await _characterDataFactory.BuildCharacterData(_playerRelatedObjects[objectKind], linkedCts.Token).ConfigureAwait(false);
                }

                foreach (var kvp in createdData)
                {
                    _playerData.SetFragment(kvp.Key, kvp.Value);
                }

                Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI()));
                _currentlyCreating.Clear();
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("Cache Creation cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "Error during Cache Creation Processing");
            }
            finally
            {
                Logger.LogDebug("Cache Creation complete");
            }
        });
    }

    // Experimental
    private void AddPlayerAddonPluginChangesToUpdate(PlayerChanges playerChanges)
    {
        _playerChangesDebounceCts.Cancel();
        _playerChangesDebounceCts.Dispose();
        _playerChangesDebounceCts = new();

        var token = _playerChangesDebounceCts.Token;

        _playerChangesLock.Wait();
        try
        {
            _debouncedPlayerChanges.Add(playerChanges); // use hashset to dedupe PlayerChanges
        }
        finally
        {
            _playerChangesLock.Release();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false); // if this is too low, set higher, but updates take longer to propogate

                await _playerChangesLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    Logger.LogTrace("Player change debounce complete, queueing changes: {changes}", string.Join(", ", _debouncedPlayerChanges));

                    foreach (var item in _debouncedPlayerChanges)
                    {
                        if (_queuedPlayerChanges.Add(item))
                        {
                            _playerChangesToUpdate.Enqueue(item);
                        }
                    }

                    _debouncedPlayerChanges.Clear();
                }
                finally
                {
                    _playerChangesLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogTrace("Player change debounce cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "Error during player change debounce processing");
            }
        });
    }

    // Experimental
    private void ProcessAddonPluginChanges()
    {
        if (_isZoning || _haltCharaDataCreation) return;

        if (!HasPendingPlayerChanges()) return;

        bool fullPlayerUpdateQueued = false;

        if (_debouncedObjectCache.Contains(ObjectKind.Player)) // our changes will get picked up by the full update
        {
            Logger.LogDebug("Object Cache already contains ObjectKind.Player, skipping update.");
            // If an addon plugin is ever not fully updated when ObjectKind == Player, this will need to be updated.
            fullPlayerUpdateQueued = true;
        } 

        if (_playerRelatedObjects.Any(p => p.Value.CurrentDrawCondition is
            not (GameObjectHandler.DrawCondition.None or GameObjectHandler.DrawCondition.DrawObjectZero or GameObjectHandler.DrawCondition.ObjectZero)))
        {
            Logger.LogDebug("Waiting for draw to finish before processing player changes");
            return;
        }

        _playerChangesCts.Cancel();
        _playerChangesCts.Dispose();
        _playerChangesCts = new();

        var processingToken = _playerChangesCts.Token;
        var playerChangesToProcess = new List<PlayerChanges>();

        _playerChangesLock.Wait(processingToken);
        try
        {
            while (_playerChangesToUpdate.TryDequeue(out var playerChange))
            {
                _queuedPlayerChanges.Remove(playerChange);

                if (fullPlayerUpdateQueued) continue;

                if (_currentlyProcessingPlayerChanges.Add(playerChange))
                {
                    playerChangesToProcess.Add(playerChange);
                }
            }
        }
        finally
        {
            _playerChangesLock.Release();
        }

        if (playerChangesToProcess.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(processingToken, _runtimeCts.Token);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), linkedCts.Token).ConfigureAwait(false); // if this is too low, set higher, but updates take longer to propogate

                Logger.LogDebug("Processing player changes: {changes}", string.Join(", ", playerChangesToProcess));

                foreach (var change in playerChangesToProcess)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();

                    var data = await _characterDataFactory.GetAddonPluginPlayerData(change).ConfigureAwait(false);

                    _playerData.SetAddonPluginPlayerData(change, data); // update our own local data for each type so we can detect future changes

                    Mediator.Publish(new AddonPluginChangesCreatedMessage(change, data)); // this could be aggregated with a list of plugins so we only send 1 update
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("Player change processing cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "Error during player change processing");
            }
            finally
            {
                await _playerChangesLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    foreach (var playerChange in playerChangesToProcess)
                    {
                        _currentlyProcessingPlayerChanges.Remove(playerChange);
                    }
                }
                finally
                {
                    _playerChangesLock.Release();
                }

                Logger.LogDebug("Player change processing complete");
            }
        });
    }

    // Experimental
    private bool HasPendingPlayerChanges()
    {
        _playerChangesLock.Wait();
        try
        {
            return _playerChangesToUpdate.Count != 0;
        }
        finally
        {
            _playerChangesLock.Release();
        }
    }
}