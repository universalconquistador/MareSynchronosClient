using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Dto.Stage;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stagehand.Api;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    ActiveStageState State { get; }

    StageFullInfoDto StageFullInfo { get; }
}

public interface IStageDisplayService
{
    IReadOnlyList<IActiveStage> GetActiveStages();
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

        private SpinLock _stateLock = new();
        public ActiveStageState State { get; private set; } = ActiveStageState.Unloaded;

        private CancellationTokenSource _loadCancellationTokenSource = new(); // Can be cancelled outside _stateLock, but _state must be set to the new state (in _stateLock) before doing so
        private Task _loadTask = Task.CompletedTask;
        private int _loadContentsVersion = -1;

        private CancellationTokenSource _unloadCancellationTokenSource = new(); // Can be cancelled outside _stateLock, but _state must be set to the new state (in _stateLock) before doing so
        private Task _unloadTask = Task.CompletedTask;

        public string UniqueId { get; }

        public ActiveStage(StageFullInfoDto stageFullInfo, ILogger logger, IFramework framework, FileDownloadManager fileDownloadManager)
        {
            StageFullInfo = stageFullInfo;
            _logger = logger;
            _framework = framework;
            _fileDownloadManager = fileDownloadManager;
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

                _logger.LogDebug("[{uniqueId}] {method} Beginning download of files.", UniqueId, nameof(LoadInternalAsync));

                // TODO: Implement download!
                await Task.Delay(500 + Random.Shared.Next(2000)).ConfigureAwait(false);

                _logger.LogDebug("[{uniqueId}] {method} Beginning instantiation.", UniqueId, nameof(LoadInternalAsync));

                // TODO: Implement instantiation!
                await Task.Delay(50 + Random.Shared.Next(200)).ConfigureAwait(false);

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

                // TODO: Implement unload!
                await Task.Delay(50 + Random.Shared.Next(200)).ConfigureAwait(false);

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
    private readonly MareConfigService _mareConfigService;
    private readonly ApiController _apiController;
    private readonly IpcCallerStagehand _ipcCallerStagehand;
    private readonly FileDownloadManagerFactory _fileDownloadManagerFactory;

    private ConcurrentDictionary<string, ActiveStage> _activeStages = new();
    private int _stageDisplayEnabled = 0;
    public bool IsStageDisplayEnabled => _stageDisplayEnabled == 1;

    public StageDisplayService(ILogger<StageDisplayService> logger, MareMediator mediator, IFramework framework, MareConfigService mareConfigService, ApiController apiController, IpcCallerStagehand ipcCallerStagehand, FileDownloadManagerFactory fileDownloadManagerFactory)
        : base(logger, mediator)
    {
        _framework = framework;
        _mareConfigService = mareConfigService;
        _apiController = apiController;
        _ipcCallerStagehand = ipcCallerStagehand;
        _fileDownloadManagerFactory = fileDownloadManagerFactory;
    }

    public IReadOnlyList<IActiveStage> GetActiveStages()
    {
        return _activeStages.Values.ToArray();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ipcCallerStagehand.ApiAvailableChanged += OnStagehandApiAvailableChanged;
        _ipcCallerStagehand.StagehandApi.LocationChanged += OnStagehandLocationChanged;
        Mediator.Subscribe<StageSettingsChangedMessage>(this, _ => RefreshStageDisplayEnabled());
        Mediator.Subscribe<ConnectedMessage>(this, _ => RefreshStageDisplayEnabled());
        Mediator.Subscribe<DisconnectedMessage>(this, _ => RefreshStageDisplayEnabled());
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

    // Enable state is based on a few things:
    // - Player logged in and connected to the sync server
    // - Stagehand API available
    // - Stage features enabled
    private void RefreshStageDisplayEnabled()
    {
        var isConnected = _apiController.IsConnected;
        var isApiAvailable = _ipcCallerStagehand.APIAvailable;
        var isFeatureEnabled = _mareConfigService.Current.EnableStageFeatures;

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
                        _ => new ActiveStage(stage, Logger, _framework, _fileDownloadManagerFactory.Create()),
                        (sid, liveStage) =>
                        {
                            // By updating the StageFullInfo, the next call to LoadAsync will reload the stage if its contents have been updated
                            liveStage.StageFullInfo = stage;
                            return liveStage;
                        });
                    stageLoadTasks.Add(activeStage.LoadAsync());
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
