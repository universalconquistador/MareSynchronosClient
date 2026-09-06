using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayerSync.FileCache;
using Stagehand.Api;
using Stagehand.Definitions;
using Stagehand.Definitions.ModResources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PlayerSync.PlayerData.Services;

public enum ActiveStageState
{
    Unloaded = 0,
    Loading = 1,
    Loaded = 2,
    Unloading = 3,
}

public interface IActiveStage
{
    bool IsHidden { get; }
    ActiveStageState State { get; }

    StageFullInfoDto StageFullInfo { get; }
}

public interface IStageDisplayService
{
    IReadOnlyList<IActiveStage> GetActiveStages();
    bool TryGetActiveStage(string stageId, [NotNullWhen(true)] out IActiveStage? activeStage);

    void SetStageHidden(string stageId, bool hidden);
}

/// <summary>
/// Shows the Stagehand stages the user is subscribed to for the current location.
/// </summary>
internal class StageDisplayService : MediatorSubscriberBase, IStageDisplayService, IHostedService, IDisposable
{
    private sealed class ActiveStage : IActiveStage
    {
        public StageFullInfoDto StageFullInfo { get; set; }

        private readonly ILogger _logger;
        private readonly IFramework _framework;
        private readonly FileDownloadManager _fileDownloadManager;
        private readonly FileCacheManager _fileCacheManager;
        private readonly IpcCallerStagehand _ipcCallerStagehand;
        private readonly ICompressedAlternateManager _compressedAlternateManager;

        private SpinLock _stateLock = new();
        public ActiveStageState State { get; private set; } = ActiveStageState.Unloaded;

        private CancellationTokenSource _loadCancellationTokenSource = new(); // Can be cancelled outside _stateLock, but _state must be set to the new state (in _stateLock) before doing so
        private Task _loadTask = Task.CompletedTask;
        private int _loadContentsVersion = -1;

        private CancellationTokenSource _unloadCancellationTokenSource = new(); // Can be cancelled outside _stateLock, but _state must be set to the new state (in _stateLock) before doing so
        private Task _unloadTask = Task.CompletedTask;

        public string UniqueId { get; }
        public bool IsHidden { get; set; }

        public ActiveStage(StageFullInfoDto stageFullInfo, bool isHidden, ILogger logger, IFramework framework, FileDownloadManager fileDownloadManager, FileCacheManager fileCacheManager, IpcCallerStagehand ipcCallerStagehand, ICompressedAlternateManager compressedAlternateManager)
        {
            IsHidden = isHidden;
            StageFullInfo = stageFullInfo;
            _logger = logger;
            _framework = framework;
            _fileDownloadManager = fileDownloadManager;
            _fileCacheManager = fileCacheManager;
            _ipcCallerStagehand = ipcCallerStagehand;
            _compressedAlternateManager = compressedAlternateManager;
            UniqueId = $"{stageFullInfo.SID}:{Guid.NewGuid()}";
        }

        public Task LoadAsync()
        {
            Task resultTask;

            bool lockTaken = false;
            while (!lockTaken)
            {
                _stateLock.TryEnter(Timeout.Infinite, ref lockTaken);
            }

            try
            {
                ActiveStageState previousState = State;
                State = ActiveStageState.Loading;
                var stageInfo = StageFullInfo;

                if (previousState != ActiveStageState.Loading || _loadContentsVersion != stageInfo.Contents.Revision)
                {
                    CancellationTokenSource? tokenToCancel = null;
                    Task? taskToAwait = null;
                    if (previousState == ActiveStageState.Loading)
                    {
                        tokenToCancel = _loadCancellationTokenSource;
                        taskToAwait = _loadTask;
                    }
                    else if (previousState == ActiveStageState.Unloading)
                    {
                        tokenToCancel = _unloadCancellationTokenSource;
                        taskToAwait = _unloadTask;
                    }

                    CancellationTokenSource newTokenSource = new();
                    _loadCancellationTokenSource = newTokenSource;
                    _loadContentsVersion = stageInfo.Contents.Revision;
                    resultTask = Task.Run(() => LoadInternalAsync(taskToAwait, tokenToCancel, stageInfo, newTokenSource.Token));
                    _loadTask = resultTask;
                }
                else
                {
                    // Load is already in progress and the stage has not changed, so just return the existing load task and let it continue
                    resultTask = _loadTask;
                }
            }
            finally
            {
                _stateLock.Exit();
            }

            return resultTask;
        }

        // Returns a dictionary of requested hash to disk path (and the disk path could be the path to the comp alt)
        private async Task<Dictionary<string, string>> DownloadFilesAsync(List<(string Hash, string Extension)> hashes, CompressedAlternateUsage compressedAlternateUsage, int storeId, CancellationToken cancelToken)
        {
            Dictionary<string, string> hashToCompAltHash = new();
            HashSet<string> locallyPresentFileSet = new();
            Dictionary<string, string> hashToDiskPath = new();

            // Compute the hashes that need to be downloaded by weeding out the ones that are already in the cache on disk
            for (int i = hashes.Count - 1; i >= 0; i--)
            {
                var hash = hashes[i];

                var fileCache = _fileCacheManager.GetFileCacheByHash(hash.Hash);

                bool compressedAlternateConfirmed = _compressedAlternateManager.TryGetCachedCompressedAlternate(hash.Hash, out string? compressedAlternateHash);

                // Adjust `hash` and `fileCache` according to the given policy for compressed alternates
                if (compressedAlternateUsage == CompressedAlternateUsage.AlwaysSourceQuality)
                {
                    // Nothing to do here--carry on as usual.
                }
                else if (compressedAlternateUsage == CompressedAlternateUsage.CompressedNewDownloads)
                {
                    // Only use compressed alternates if the original file is not present in the cache.
                    if (fileCache == null && compressedAlternateConfirmed && compressedAlternateHash != null)
                    {
                        _logger.LogTrace("CompressSubstitution[{character}]: {old} is {new} (TryCalculateModdedDictionary)", UniqueId, hash, compressedAlternateHash);
                        hashToCompAltHash[hash.Hash] = compressedAlternateHash;
                        fileCache = _fileCacheManager.GetFileCacheByHash(compressedAlternateHash);
                        hash.Hash = compressedAlternateHash;

                    }
                }
                else if (compressedAlternateUsage == CompressedAlternateUsage.AlwaysCompressed)
                {
                    if (compressedAlternateConfirmed)
                    {
                        // We are certain about the existence of any compressed alternates. If there are, use it. If there aren't, carry on as usual.
                        if (compressedAlternateHash != null)
                        {
                            _logger.LogTrace("CompressSubstitution[{character}]: {old} is {new} (TryCalculateModdedDictionary)", UniqueId, hash, compressedAlternateHash);
                            hashToCompAltHash[hash.Hash] = compressedAlternateHash;
                            fileCache = _fileCacheManager.GetFileCacheByHash(compressedAlternateHash);
                            hash.Hash = compressedAlternateHash;
                        }
                    }
                    else
                    {
                        if (fileCache != null)
                        {
                            _logger.LogTrace("CompressSubstitution[{character}]: sending {hash} for re-download to check for alternates (TryCalculateModdedDictionary)", UniqueId, hash);
                            // Record this hash to send to the download function as 'locally present', so that when it checks for alternates,
                            // it won't re-download this original file if none exist.
                            locallyPresentFileSet.Add(hash.Hash);
                            hashToDiskPath[hash.Hash] = fileCache.ResolvedFilepath;
                        }

                        // We don't know whether there are any compressed alternates, so mark this file as needing downloading.
                        // Once the download starts, if there aren't any, the actual download will be skipped.
                        fileCache = null;
                    }
                }
                else
                {
                    throw new ArgumentException("Invalid compressed alternate usage specified!", nameof(compressedAlternateUsage));
                }

                if (fileCache == null)
                {
                    // Need to fetch info for this file, either because we don't have the original or because we don't know whether it has a comp alt
                    hashes[i] = hash;
                    _logger.LogTrace("Missing file: {hash}", hash);
                }
                else
                {
                    hashes.RemoveAt(i);
                    // This might be the comp alt hash--we'll distribute it back to the uncompressed hash later on
                    hashToDiskPath[hash.Hash] = fileCache.ResolvedFilepath;
                }
            }

            var filesToDownload = await _fileDownloadManager.InitiateDownloadList(UniqueId, hashes.Select(h => h.Hash).ToList(), compressedAlternateUsage, hashToCompAltHash, locallyPresentFileSet, storeId, cancelToken).ConfigureAwait(false);
            if (filesToDownload.Count > 0)
            {
                await _fileDownloadManager.DownloadFiles(new DownloadBatchInfo(StageFullInfo.Customize.DisplayName, "Stage", GameObject: null), hashes.Select(h => new FileReplacementData() { Hash = h.Hash, GamePaths = [$"{h.Hash}{h.Extension}"] }).ToList(), hashToCompAltHash, cancelToken).ConfigureAwait(false);
            }

            // Now fill in the downloaded path for each of the hashes we tried to download
            foreach (var hash in hashes)
            {
                var fileCache = _fileCacheManager.GetFileCacheByHash(hash.Hash);

                // Was a comp alt was found that we didn't anticipate from the compression cache service?
                if (fileCache == null && hashToCompAltHash.TryGetValue(hash.Hash, out var compHash))
                {
                    fileCache = _fileCacheManager.GetFileCacheByHash(compHash);
                    if (fileCache != null)
                    {
                        hashToDiskPath[compHash] = fileCache.ResolvedFilepath;
                    }
                }

                if (fileCache != null)
                {
                    hashToDiskPath[hash.Hash] = fileCache.ResolvedFilepath;
                }
                else
                {
                    _logger.LogWarning("Somehow stage mod file {hash} was still missing after all the mods were downloaded!", hash.Hash);
                }
            }

            // Distribute the disk path of comp alts to the original requesting hashes
            foreach (var compAltPair in hashToCompAltHash)
            {
                hashToDiskPath[compAltPair.Key] = hashToDiskPath[compAltPair.Value];
            }

            return hashToDiskPath;
        }

        private async Task ReconstituteStageDefinitionAsync(StageDefinition stageDefinition, StageContentsDto contents, Dictionary<string, string> modHashToDiskPath, CancellationToken ct)
        {
            foreach (var mod in contents.Mods)
            {
                ct.ThrowIfCancellationRequested();
                if (modHashToDiskPath.TryGetValue(mod.Hash, out var modDiskPath))
                {
                    if (!stageDefinition.EmbeddedModpacks.TryGetValue(mod.ModpackId, out var modpack))
                    {
                        modpack = new EmbeddedModpackDefinition();
                        stageDefinition.EmbeddedModpacks[mod.ModpackId] = modpack;
                    }

                    // TODO: Use a disk mod instead of loading the mod bytes into memory and embedding them in the definition!
                    var modBytes = await File.ReadAllBytesAsync(modDiskPath, ct).ConfigureAwait(false);
                    modpack.ModdedResources[mod.GamePath] = new EmbeddedModResourceDefinition() { CompressedDataBytes = modBytes, CompressionScheme = ModCompressionScheme.None };
                }
                else
                {
                    _logger.LogWarning("Stage mod {hash} for {path} not found in cache!", mod.Hash, mod.GamePath);
                }
            }
        }

        private async Task LoadInternalAsync(Task? toAwait, CancellationTokenSource? toCancel, StageFullInfoDto stageInfo, CancellationToken cancelToken)
        {
            if (toCancel != null)
            {
                _logger.LogDebug("[{uniqueId}] {method} cancelling previous load/unload task.", UniqueId, nameof(LoadInternalAsync));
                await toCancel.CancelAsync().ConfigureAwait(false);
            }

            if (toAwait != null)
            {
                _logger.LogDebug("[{uniqueId}] {method} awaiting previous load/unload task.", UniqueId, nameof(LoadInternalAsync));
                try
                {
                    await toAwait.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[{uniqueId}] {method} exception raised in previous load/unload task.", UniqueId, nameof(LoadInternalAsync));
                }
            }

            try
            {
                cancelToken.ThrowIfCancellationRequested();

                _logger.LogDebug("[{uniqueId}] {method} Beginning download of mod files.", UniqueId, nameof(LoadInternalAsync));

                var modHashes = stageInfo.Contents.Mods.Select(mod => (mod.Hash, mod.GamePath)).DistinctBy(pair => pair.Hash, StringComparer.Ordinal).ToList();
                var modHashToDiskPath = await DownloadFilesAsync(modHashes, CompressedAlternateUsage.AlwaysCompressed, 1, cancelToken).ConfigureAwait(false);

                _logger.LogDebug("[{uniqueId}] {method} Downloading definition file {hash}.", UniqueId, nameof(LoadInternalAsync), stageInfo.Contents.StageFileHash);
                var stageHash = (Hash: stageInfo.Contents.StageFileHash, GamePath: "stage.json");
                var stageHashToDiskPath = await DownloadFilesAsync(new() { stageHash }, CompressedAlternateUsage.AlwaysSourceQuality, 2, cancelToken).ConfigureAwait(false);

                _logger.LogDebug("[{uniqueId}] {method} Loading definition file {hash}.", UniqueId, nameof(LoadInternalAsync), stageInfo.Contents.StageFileHash);
                StageDefinition? stageDefinition;
                using (var definitionFileStream = new FileStream(stageHashToDiskPath[stageHash.Hash], FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (!StageDefinition.TryParseJSONStream(definitionFileStream, out stageDefinition))
                    {
                        throw new Exception("Stage JSON could not be parsed!");
                    }
                }

                await ReconstituteStageDefinitionAsync(stageDefinition, stageInfo.Contents, modHashToDiskPath, cancelToken).ConfigureAwait(false);

                _logger.LogDebug("[{uniqueId}] {method} Beginning instantiation.", UniqueId, nameof(LoadInternalAsync));

                string serializedDefinition = stageDefinition.ToDefinitionString();
                await _framework.RunOnFrameworkThread(() =>
                {
                    _ipcCallerStagehand.StagehandApi.TryCreateOrUpdateTemporaryStage(serializedDefinition, UniqueId, $"{UniqueId} ({stageInfo.Customize.DisplayName})");
                }).ConfigureAwait(false);

                _logger.LogDebug("[{uniqueId}] {method} Finished loading.", UniqueId, nameof(LoadInternalAsync));
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("[{uniqueId}] {method} Load cancelled.", UniqueId, nameof(LoadInternalAsync));
            }

            bool lockTaken = false;
            while (!lockTaken)
            {
                _stateLock.TryEnter(Timeout.Infinite, ref lockTaken);
            }

            try
            {
                if (!cancelToken.IsCancellationRequested)
                {
                    State = ActiveStageState.Loaded;
                }
            }
            finally
            {
                _stateLock.Exit();
            }
        }

        public Task UnloadAsync()
        {
            Task resultTask;

            bool lockTaken = false;
            while (!lockTaken)
            {
                _stateLock.TryEnter(Timeout.Infinite, ref lockTaken);
            }

            try
            {
                ActiveStageState previousState = State;
                State = ActiveStageState.Unloading;

                CancellationTokenSource? tokenToCancel = null;
                Task? taskToAwait = null;
                if (previousState == ActiveStageState.Loading)
                {
                    tokenToCancel = _loadCancellationTokenSource;
                    taskToAwait = _loadTask;
                }
                else if (previousState == ActiveStageState.Unloading)
                {
                    tokenToCancel = _unloadCancellationTokenSource;
                    taskToAwait = _unloadTask;
                }

                CancellationTokenSource newTokenSource = new();
                _unloadCancellationTokenSource = newTokenSource;
                resultTask = Task.Run(() => UnloadInternalAsync(taskToAwait, tokenToCancel, newTokenSource.Token));
                _unloadTask = resultTask;
            }
            finally
            {
                _stateLock.Exit();
            }

            return resultTask;
        }

        private async Task UnloadInternalAsync(Task? toAwait, CancellationTokenSource? toCancel, CancellationToken cancelToken)
        {
            if (toCancel != null)
            {
                _logger.LogDebug("[{uniqueId}] {method} cancelling previous load/unload task.", UniqueId, nameof(UnloadInternalAsync));
                await toCancel.CancelAsync().ConfigureAwait(false);
            }

            if (toAwait != null)
            {
                _logger.LogDebug("[{uniqueId}] {method} awaiting previous load/unload task.", UniqueId, nameof(UnloadInternalAsync));
                try
                {
                    await toAwait.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[{uniqueId}] {method} exception raised in previous load/unload task.", UniqueId, nameof(UnloadInternalAsync));
                }
            }

            try
            {
                _logger.LogDebug("[{uniqueId}] {method} Beginning unload.", UniqueId, nameof(LoadInternalAsync));

                // Waiting here can cause a deadlock, so just fire and forget
                _ = _framework.RunOnFrameworkThread(() =>
                {
                    _ipcCallerStagehand.StagehandApi.TryDestroyTemporaryStage(UniqueId);
                });

                _logger.LogDebug("[{uniqueId}] {method} Finished unloading.", UniqueId, nameof(LoadInternalAsync));
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("[{uniqueId}] {method} Unload cancelled.", UniqueId, nameof(UnloadInternalAsync));
            }

            bool lockTaken = false;
            while (!lockTaken)
            {
                _stateLock.TryEnter(Timeout.Infinite, ref lockTaken);
            }

            try
            {
                if (!cancelToken.IsCancellationRequested)
                {
                    State = ActiveStageState.Unloaded;
                }
            }
            finally
            {
                _stateLock.Exit();
            }
            _fileDownloadManager.Dispose();
        }
    }

    private readonly IFramework _framework;
    private readonly StageConfigService _stageConfigService;
    private readonly ApiController _apiController;
    private readonly FileCacheManager _fileCacheManager;
    private readonly IpcCallerStagehand _ipcCallerStagehand;
    private readonly FileDownloadManagerFactory _fileDownloadManagerFactory;
    private readonly ICompressedAlternateManager _compressedAlternateManager;

    private ConcurrentDictionary<string, ActiveStage> _activeStages = new();
    private int _stageDisplayEnabled = 0;
    public bool IsStageDisplayEnabled => _stageDisplayEnabled == 1;

    public StageDisplayService(ILogger<StageDisplayService> logger, MareMediator mediator, IFramework framework, StageConfigService stageConfigService, ApiController apiController, FileCacheManager fileCacheManager, IpcCallerStagehand ipcCallerStagehand, FileDownloadManagerFactory fileDownloadManagerFactory, ICompressedAlternateManager compressedAlternateManager)
        : base(logger, mediator)
    {
        _framework = framework;
        _stageConfigService = stageConfigService;
        _apiController = apiController;
        _fileCacheManager = fileCacheManager;
        _ipcCallerStagehand = ipcCallerStagehand;
        _fileDownloadManagerFactory = fileDownloadManagerFactory;
        _compressedAlternateManager = compressedAlternateManager;
    }

    public IReadOnlyList<IActiveStage> GetActiveStages()
    {
        return _activeStages.Values.ToArray();
    }

    public bool TryGetActiveStage(string stageId, [NotNullWhen(true)] out IActiveStage? activeStage)
    {
        var result = _activeStages.TryGetValue(stageId, out var activeStageImpl);
        activeStage = activeStageImpl;
        return result;
    }

    public void SetStageHidden(string stageId, bool hidden)
    {
        if (hidden)
        {
            if (_stageConfigService.Current.HiddenStageIds.Add(stageId))
            {
                if (_activeStages.TryGetValue(stageId, out var activeStage) && !activeStage.IsHidden)
                {
                    activeStage.IsHidden = true;
                    _ = activeStage.UnloadAsync();
                }
                _stageConfigService.Save();
            }
        }
        else
        {
            if (_stageConfigService.Current.HiddenStageIds.Remove(stageId))
            {
                if (_activeStages.TryGetValue(stageId, out var activeStage) && activeStage.IsHidden)
                {
                    activeStage.IsHidden = false;
                    _ = activeStage.LoadAsync();
                }
                _stageConfigService.Save();
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ipcCallerStagehand.ApiAvailableChanged += OnStagehandApiAvailableChanged;
        _ipcCallerStagehand.StagehandApi.LocationChanged += OnStagehandLocationChanged;
        Mediator.Subscribe<StageSettingsChangedMessage>(this, _ => RefreshStageDisplayEnabled());
        Mediator.Subscribe<ConnectedMessage>(this, _ => RefreshStageDisplayEnabled());
        Mediator.Subscribe<DisconnectedMessage>(this, _ => RefreshStageDisplayEnabled());
        Mediator.Subscribe<StageSubscriptionsChangedMessage>(this, OnStageSubscriptionsChanged);
        Mediator.Subscribe<StageSubscribedContentsChangedMessage>(this, OnStageSubscribedContentsChanged);
        Mediator.Subscribe<StageSubscribedStateChangedMessage>(this, OnStageSubscribedStateChanged);
        Mediator.Subscribe<StageCustomizeChangedMessage>(this, OnStageCustomizeChanged);
        RefreshStageDisplayEnabled();

        return Task.CompletedTask;
    }

    private void OnStagehandLocationChanged(StageLocation obj)
    {
        if (IsStageDisplayEnabled)
        {
            var _ = ReloadAllStages();
        }
    }

    private void OnStagehandApiAvailableChanged(bool isApiAvailable)
    {
        RefreshStageDisplayEnabled();
    }

    private static bool StageIsInLocation(StageStateDto state, StageLocation location)
    {
        return state.LocationWorldId == location.WorldId
            && state.LocationTerritoryId == location.TerritoryId
            && state.LocationWardId == location.WardId
            && state.LocationDivisionId == location.DivisionId
            && state.LocationHouseId == location.HouseId
            && state.LocationRoomId == location.RoomId;
    }

    private void OnStageSubscriptionsChanged(StageSubscriptionsChangedMessage message)
    {
        if (!IsStageDisplayEnabled)
        {
            return;
        }
        foreach (var removed in message.RemovedSubscribedStageIds)
        {
            if (_activeStages.TryRemove(removed, out var activeStage))
            {
                _ = activeStage.UnloadAsync();
            }
        }

        var currentLocation = _ipcCallerStagehand.StagehandApi.GetLocation();
        foreach (var stage in message.AddedSubscribedStages)
        {
            if (StageIsInLocation(stage.State, currentLocation))
            {
                var activeStage = _activeStages.AddOrUpdate(stage.SID,
                    _ => new ActiveStage(stage, _stageConfigService.Current.HiddenStageIds.Contains(stage.SID), Logger, _framework, _fileDownloadManagerFactory.Create(), _fileCacheManager, _ipcCallerStagehand, _compressedAlternateManager),
                    (sid, liveStage) =>
                    {
                        // By updating the StageFullInfo, the next call to LoadAsync will reload the stage if its contents have been updated
                        liveStage.StageFullInfo = stage;
                        return liveStage;
                    });
                if (!activeStage.IsHidden)
                {
                    _ = activeStage.LoadAsync();
                }
            }
        }
    }

    private void OnStageSubscribedContentsChanged(StageSubscribedContentsChangedMessage message)
    {
        if (!IsStageDisplayEnabled)
        {
            return;
        }

        if (_activeStages.TryGetValue(message.StageId, out var activeStage) && !activeStage.IsHidden)
        {
            activeStage.StageFullInfo = new()
            {
                Info = activeStage.StageFullInfo.Info,
                Customize = activeStage.StageFullInfo.Customize,
                SubscriptionState = activeStage.StageFullInfo.SubscriptionState,
                State = activeStage.StageFullInfo.State,
                Contents = message.NewContents
            };
            _ = activeStage.LoadAsync();
        }
    }

    private void OnStageSubscribedStateChanged(StageSubscribedStateChangedMessage message)
    {
        if (!IsStageDisplayEnabled)
        {
            return;
        }

        // If the stage in question is or was in the current location, do a full refresh of the active stages
        var currentLocation = _ipcCallerStagehand.StagehandApi.GetLocation();
        bool isNowCurrentLocation = currentLocation.WorldId == message.NewState.LocationWorldId
                && currentLocation.TerritoryId == message.NewState.LocationTerritoryId
                && currentLocation.WardId == message.NewState.LocationWardId
                && currentLocation.DivisionId == message.NewState.LocationDivisionId
                && currentLocation.HouseId == message.NewState.LocationHouseId
                && currentLocation.RoomId == message.NewState.LocationRoomId;
        if (_activeStages.ContainsKey(message.StageId) || isNowCurrentLocation)
        {
            _ = SetupStagesForCurrentLocation();
        }
    }

    private void OnStageCustomizeChanged(StageCustomizeChangedMessage message)
    {
        if (_activeStages.TryGetValue(message.StageId, out var activeStage))
        {
            activeStage.StageFullInfo.Customize = message.NewCustomize;
        }
    }

    // Enable state is based on a few things:
    // - Player logged in and connected to the sync server
    // - Stagehand API available
    // - Stage features enabled
    private void RefreshStageDisplayEnabled()
    {
        var isConnected = _apiController.IsConnected;
        var isApiAvailable = _ipcCallerStagehand.APIAvailable;
        var isFeatureEnabled = _stageConfigService.Current.EnableStageFeatures;

        var isEnabled = isConnected && isApiAvailable && isFeatureEnabled;

        var oldEnabled = Interlocked.Exchange(ref _stageDisplayEnabled, isEnabled ? 1 : 0) == 1;

        if (isEnabled && !oldEnabled)
        {
            OnStageDisplayEnabled();
        }
        else if (!isEnabled && oldEnabled)
        {
            OnStageDisplayDisabled();
        }
    }

    private void OnStageDisplayEnabled()
    {
        Logger.LogDebug("Enabling stage display...");
        _ = SetupStagesForCurrentLocation();
    }

    private async Task SetupStagesForCurrentLocation()
    {
        var location = _ipcCallerStagehand.StagehandApi.GetLocation();
        Logger.LogDebug("Querying the list of subscribed stages for location {location}...", location.ToString());
        var stages = await _apiController.StageGetSubscribedForLocation((int)location.WorldId, location.TerritoryId, location.WardId, location.DivisionId, location.HouseId, location.RoomId).ConfigureAwait(false);

        Logger.LogDebug("Received list of subscribed stages for location {location}: {stages}", location.ToString(), string.Join(',', stages.Select(stage => $"{stage.SID}: {stage.Customize.DisplayName}")));

        if (IsStageDisplayEnabled)
        {
            List<Task> stageLoadTasks = new();

            // Start unloading any existing stages that aren't in the server response
            HashSet<string> stageSids = new(stages.Select(stage => stage.SID));
            foreach (var sid in _activeStages.Keys)
            {
                if (!stageSids.Contains(sid) && _activeStages.Remove(sid, out var activeStage))
                {
                    stageLoadTasks.Add(activeStage.UnloadAsync());
                }
            }

            if (stages.Length > 0)
            {
                long timestampStart = Stopwatch.GetTimestamp();
                
                // Load/reload any stages that are in the server response
                foreach (var stage in stages)
                {
                    var activeStage = _activeStages.AddOrUpdate(stage.SID,
                        _ => new ActiveStage(stage, _stageConfigService.Current.HiddenStageIds.Contains(stage.SID), Logger, _framework, _fileDownloadManagerFactory.Create(), _fileCacheManager, _ipcCallerStagehand, _compressedAlternateManager),
                        (sid, liveStage) =>
                        {
                            // By updating the StageFullInfo, the next call to LoadAsync will reload the stage if its contents have been updated
                            liveStage.StageFullInfo = stage;
                            return liveStage;
                        });
                    if (!activeStage.IsHidden)
                    {
                        stageLoadTasks.Add(activeStage.LoadAsync());
                    }
                }

                await Task.WhenAll(stageLoadTasks).ConfigureAwait(false);

                TimeSpan duration = Stopwatch.GetElapsedTime(timestampStart);
                Logger.LogDebug("Setup stages for current location in {duration}.", duration);
            }
        }
    }

    private async Task UnloadAllStages()
    {
        var oldStageDictionary = Interlocked.Exchange(ref _activeStages, new());

        await Task.WhenAll(oldStageDictionary.Values.Select(activeStage => activeStage.UnloadAsync())).ConfigureAwait(false);
        oldStageDictionary.Clear();
    }

    private async Task ReloadAllStages()
    {
        await UnloadAllStages().ConfigureAwait(false);
        await SetupStagesForCurrentLocation().ConfigureAwait(false);
    }

    private void OnStageDisplayDisabled()
    {
        Logger.LogDebug("Disabling stage display...");
        var _ = UnloadAllStages();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _ipcCallerStagehand.ApiAvailableChanged -= OnStagehandApiAvailableChanged;
        _ipcCallerStagehand.StagehandApi.LocationChanged -= OnStagehandLocationChanged;
        UnsubscribeAll();

        await UnloadAllStages().ConfigureAwait(false);
    }

    public void Dispose()
    {
    }
}
