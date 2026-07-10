using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayerSync.FileCache;
using PlayerSync.Validation;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MareSynchronos.PlayerData.Handlers;

public sealed class PairHandler : DisposableMediatorSubscriberBase
{
    private sealed record CombatData(Guid ApplicationId, CharacterData CharacterData, bool Forced);

    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileDownloadManager _downloadManager;
    private readonly FileCacheManager _fileDbManager;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IpcManager _ipcManager;
    private readonly PlayerPerformanceService _playerPerformanceService;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly PluginWarningNotificationService _pluginWarningNotificationManager;
    private readonly PlayerPerformanceConfigService _performanceConfig;
    private readonly MareConfigService _configService;
    private readonly PlayerIdleStatusService _idleStatusService;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ICompressedAlternateManager _compressedAlternateManager;
    private readonly IDataManager _dataManager;

    private readonly Dictionary<ObjectKind, Guid?> _customizeIds = [];
    private readonly Dictionary<ObjectKind, bool> _lociRegistrations = [];

    private GameObjectHandler? _charaHandler;
    private CharacterData? _cachedData = null;
    private CombatData? _dataReceivedInDowntime;
    private Guid _applicationId;
    private Guid? _penumbraCollection;
    private Task? _applicationTask;
    private CancellationTokenSource? _downloadCancellationTokenSource = new();
    private CancellationTokenSource? _applicationCancellationTokenSource = new();
    private bool _forceApplyMods = false;
    private bool _isVisible;
    private bool _redrawOnNextApplication = false;
    private int _isRedrawVanillaRunning = 0;
    private bool _isVanillaEnforced;
    private bool _isLocked = false;

    public PairHandler(ILogger<PairHandler> logger, Pair pair,
        GameObjectHandlerFactory gameObjectHandlerFactory,
        IpcManager ipcManager, FileDownloadManager transferManager,
        PluginWarningNotificationService pluginWarningNotificationManager,
        DalamudUtilService dalamudUtil, IHostApplicationLifetime lifetime,
        FileCacheManager fileDbManager, MareMediator mediator,
        PlayerPerformanceService playerPerformanceService,
        ServerConfigurationManager serverConfigManager,
        ICompressedAlternateManager compressedAlternateManager,
        MareConfigService configService,
        PlayerPerformanceConfigService performanceConfig,
        IDataManager dataManager,
        PlayerIdleStatusService playerIdleStatusService
        ) : base(logger, mediator)
    {
        Pair = pair;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _downloadManager = transferManager;
        _pluginWarningNotificationManager = pluginWarningNotificationManager;
        _dalamudUtil = dalamudUtil;
        _lifetime = lifetime;
        _fileDbManager = fileDbManager;
        _playerPerformanceService = playerPerformanceService;
        _serverConfigManager = serverConfigManager;
        _compressedAlternateManager = compressedAlternateManager;
        _performanceConfig = performanceConfig;
        _dataManager = dataManager;
        _configService = configService;
        _idleStatusService = playerIdleStatusService;
        _isVanillaEnforced = IsVanillaEnforced();

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());

        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) =>
        {
            _downloadCancellationTokenSource?.CancelDispose();
            _charaHandler?.Invalidate();
            IsVisible = false;
        });

        Mediator.Subscribe<PenumbraInitializedMessage>(this, (_) =>
        {
            _penumbraCollection = _ipcManager.Penumbra.CreateTemporaryCollectionAsync(logger, Pair.UserData.UID).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!IsVisible && _charaHandler != null)
            {
                PlayerName = string.Empty;
                _charaHandler.Dispose();
                _charaHandler = null;
            }
        });

        Mediator.Subscribe<ClassJobChangedMessage>(this, (msg) =>
        {
            if (msg.GameObjectHandler == _charaHandler)
            {
                _redrawOnNextApplication = true;
            }
        });

        Mediator.Subscribe<CombatOrPerformanceEndMessage>(this, (msg) =>
        {
            if (IsVisible && _dataReceivedInDowntime != null)
            {
                ApplyCharacterData(_dataReceivedInDowntime.ApplicationId,
                    _dataReceivedInDowntime.CharacterData, _dataReceivedInDowntime.Forced);
                _dataReceivedInDowntime = null;
            }
        });

        Mediator.Subscribe<CombatOrPerformanceStartMessage>(this, _ =>
        {
            _dataReceivedInDowntime = null;
            _downloadCancellationTokenSource = _downloadCancellationTokenSource?.CancelRecreate();
            _applicationCancellationTokenSource = _applicationCancellationTokenSource?.CancelRecreate();
        });

        Mediator.Subscribe<PlayerIdleStartMessage>(this, _ =>
        {
            _dataReceivedInDowntime = null;
            _downloadCancellationTokenSource = _downloadCancellationTokenSource?.CancelRecreate();
            _applicationCancellationTokenSource = _applicationCancellationTokenSource?.CancelRecreate();
        });

        Mediator.Subscribe<PlayerIdleEndMessage>(this, _ =>
        {
            if (IsVisible && _dataReceivedInDowntime != null)
            {
                Logger.LogTrace("Applying deferred pair data for {uid} after IDLE has become ACTIVE.", Pair.UserData.UID);
                ApplyCharacterData(_dataReceivedInDowntime.ApplicationId,
                    _dataReceivedInDowntime.CharacterData, _dataReceivedInDowntime.Forced);
                _dataReceivedInDowntime = null;
            }
            else if(!IsVisible && _dataReceivedInDowntime != null)
            {
                Logger.LogTrace("Removing deferred pair data for {uid} after IDLE has become ACTIVE.", Pair.UserData.UID);
                // we should really consider just doing what a lot of the Dispose does here, but that could be a lot of pairs if idle for a long time...
                _charaHandler?.Invalidate();
                _cachedData = null; // there could be weird one-way visibility concerns with doing this
                _dataReceivedInDowntime = null;
            }
        });

        if (!_configService.Current.DebugDisableSoundIndicators)
        {
            Mediator.Subscribe<PenumbraResourceLoadMessage>(this, OnPenumbraResourceLoaded);
        }

        Mediator.Subscribe<PlayerDrawEndMessage>(this, (msg) =>
        {
            if (msg.PlayerName == Pair.PlayerName && _isVanillaEnforced)
            {
                CheckForVanillaLoadingOfPair();
            }
        });

        LastAppliedDataBytes = -1;
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                string text = "User Visibility Changed, now: " + (_isVisible ? "Is Visible" : "Is not Visible");
                Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairHandler), EventSeverity.Informational, text)));
                Mediator.Publish(new RefreshUiMessage());
            }
        }
    }

    public long LastAppliedDataBytes { get; private set; }
    public Pair Pair { get; private set; }
    public nint PlayerCharacter => _charaHandler?.Address ?? nint.Zero;
    public unsafe uint PlayerCharacterId => (_charaHandler?.Address ?? nint.Zero) == nint.Zero
        ? uint.MaxValue
        : ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)_charaHandler!.Address)->EntityId;
    public string? PlayerName { get; private set; }
    public string PlayerNameHash => Pair.Ident;
    public nint LastCompanionPtr { get; private set; } = nint.Zero;
    public nint LastMinionOrMountPtr { get; private set; } = nint.Zero;
    public nint LastPetPtr { get; private set; } = nint.Zero;
    private Task? _pairDownloadTask;

    // Maps from hash in the last applied data to compressed hash
    public ConcurrentDictionary<string, string> ActiveCompressionRedirects { get; private set; } = new ConcurrentDictionary<string, string>();

    public override string ToString()
    {
        return Pair == null ? base.ToString() ?? string.Empty : Pair.UserData.AliasOrUID + ":" + PlayerName + ":" + (PlayerCharacter != nint.Zero ? "HasChar" : "NoChar");
    }

    internal void SetUploading(bool isUploading = true)
    {
        Logger.LogTrace("Setting {this} uploading {uploading}", this, isUploading);
        if (_charaHandler != null)
        {
            Mediator.Publish(new PlayerUploadingMessage(_charaHandler, isUploading));
        }
    }

    /// <summary>
    /// Creates a GameObjectHandler for a PairHandler and creates and assigns a Penumbra Collection
    /// </summary>
    /// <param name="name"></param>
    public void Initialize(string name)
    {
        PlayerName = name;

        _charaHandler = _gameObjectHandlerFactory.Create(ObjectKind.Player, () => _dalamudUtil.GetPlayerCharacterFromCachedTableByIdent(Pair.Ident), isWatched: false).GetAwaiter().GetResult();

        _serverConfigManager.AutoPopulateNoteForUid(Pair.UserData.UID, name);

        Mediator.Subscribe<HonorificReadyMessage>(this, async (_) =>
        {
            if (string.IsNullOrEmpty(_cachedData?.HonorificData)) return;
            Logger.LogTrace("Reapplying Honorific data for {this}", this);
            await _ipcManager.Honorific.SetTitleAsync(PlayerCharacter, _cachedData.HonorificData).ConfigureAwait(false);
        });

        Mediator.Subscribe<PetNamesReadyMessage>(this, async (_) =>
        {
            if (string.IsNullOrEmpty(_cachedData?.PetNamesData)) return;
            Logger.LogTrace("Reapplying Pet Names data for {this}", this);
            await _ipcManager.PetNames.SetPlayerData(PlayerCharacter, _cachedData.PetNamesData).ConfigureAwait(false);
        });

        Mediator.Subscribe<LociReadyMessage>(this, async _ =>
        {
            if (_cachedData is null) return;
            if (!_cachedData.LociData.TryGetValue(ObjectKind.Player, out var data) || string.IsNullOrEmpty(data)) return;
            if (_charaHandler.Address == nint.Zero) return;
            Logger.LogTrace("Reapplying Loci data for {this}", this);
            if (await _ipcManager.Loci.RegisterActor(_charaHandler.Address).ConfigureAwait(false))
                _lociRegistrations[ObjectKind.Player] = true;
            await _ipcManager.Loci.SetActorManager(_charaHandler.Address, data).ConfigureAwait(false);
        });

        _penumbraCollection = _ipcManager.Penumbra.CreateTemporaryCollectionAsync(Logger, Pair.UserData.UID).ConfigureAwait(false).GetAwaiter().GetResult();
        _ipcManager.Penumbra.AssignTemporaryCollectionAsync(Logger, _penumbraCollection.Value, _charaHandler.GetGameObject()!.ObjectIndex).GetAwaiter().GetResult();
    }

    public void ApplyCharacterData(Guid applicationBase, CharacterData characterData, bool forceApplyCustomization = false)
    {
        _ = ApplyCharacterDataAsync(applicationBase, characterData, forceApplyCustomization);
    }

    /// <summary>
    /// Step 3
    /// This step provides many of the preflight checks prior to starting any downloads.
    /// There is a LOT going on here, best to review carefully.
    /// </summary>
    public Task ApplyCharacterDataAsync(Guid applicationBase, CharacterData characterData, bool forceApplyCustomization = false)
    {
        // Check if we are in combat or performing and need to defer pair loading
        if (_dalamudUtil.IsInCombatOrPerforming)
        {
            Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairHandler), EventSeverity.Warning,
                "Cannot apply character data: you are in combat or performing music, deferring application")));
            Logger.LogDebug("[BASE-{appBase}] Received data but player is in combat or performing", applicationBase);
            _dataReceivedInDowntime = new(applicationBase, characterData, forceApplyCustomization);
            SetUploading(isUploading: false);

            return Task.CompletedTask;
        }

        // Check if the pair is in an invalid state and we need to defer loading
        if (_charaHandler == null || PlayerCharacter == IntPtr.Zero)
        {
            Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairHandler), EventSeverity.Warning,
                "Cannot apply character data: Receiving Player is in an invalid state, deferring application")));
            Logger.LogDebug("[BASE-{appBase}] Received data but player was in invalid state, charaHandlerIsNull: {charaIsNull}, playerPointerIsNull: {ptrIsNull}",
                applicationBase, _charaHandler == null, PlayerCharacter == IntPtr.Zero);

            var hasDiffMods = characterData.CheckUpdatedData(applicationBase, _cachedData, Logger,
                this, forceApplyCustomization, forceApplyMods: false)
                .Any(p => p.Value.Contains(PlayerChanges.ModManip) || p.Value.Contains(PlayerChanges.ModFiles));

            _forceApplyMods = hasDiffMods || _forceApplyMods || (PlayerCharacter == IntPtr.Zero && _cachedData == null);
            _cachedData = characterData;

            Logger.LogDebug("[BASE-{appBase}] Setting data: {hash}, forceApplyMods: {force}", applicationBase, _cachedData.DataHash.Value, _forceApplyMods);

            return Task.CompletedTask;
        }

        // Reset pair upload status
        SetUploading(isUploading: false);

        // Check pair data if we are filtering
        if (_isVanillaEnforced && !_isLocked)
        {
            _isLocked = true;
            CheckForVanillaLoadingOfPair();
        }

        // We don't apply data to vanilla pairs (when we're filtering them)
        if (_isVanillaEnforced)
        {
            return Task.CompletedTask;
        }

        Logger.LogDebug("[BASE-{appbase}] Applying data for {player}, forceApplyCustomization: {forced}, forceApplyMods: {forceMods}",
            applicationBase, this, forceApplyCustomization, _forceApplyMods);
        Logger.LogDebug("[BASE-{appbase}] Hash for data is {newHash}, current cache hash is {oldHash}",
            applicationBase, characterData.DataHash.Value, _cachedData?.DataHash.Value ?? "NODATA");

        // Check if the pairs data is what we already expect, if we're not forcing it, just stop here
        if (string.Equals(characterData.DataHash.Value, _cachedData?.DataHash.Value ?? string.Empty, StringComparison.Ordinal) && !forceApplyCustomization)
        {
            return Task.CompletedTask;
        }

        // More safety/sanity checks
        if (_dalamudUtil.IsInCutscene || _dalamudUtil.IsOccupiedInCutSceneEvent || _dalamudUtil.IsInGpose || !_ipcManager.Penumbra.APIAvailable || !_ipcManager.Glamourer.APIAvailable)
        {
            Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairHandler), EventSeverity.Warning,
                "Cannot apply character data: you are in GPose, a Cutscene or Penumbra/Glamourer is not available")));
            Logger.LogInformation("[BASE-{appbase}] Application of data for {player} while in cutscene/gpose or Penumbra/Glamourer unavailable, returning",
                applicationBase, this);

            return Task.CompletedTask;
        }

        Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairHandler), EventSeverity.Informational, "Applying Character Data")));

        _forceApplyMods |= forceApplyCustomization;

        // This is a huge check worth reviewing, this builds tha actual "changes" to apply. This dictionary gets passed through 4 method calls before doing anything
        var charaDataToUpdate = characterData.CheckUpdatedData(applicationBase, _cachedData?.DeepClone() ?? new(), Logger, this, forceApplyCustomization, _forceApplyMods);

        if (_charaHandler != null && _forceApplyMods)
        {
            _forceApplyMods = false;
        }

        // This should be investigated more
        if (_redrawOnNextApplication && charaDataToUpdate.TryGetValue(ObjectKind.Player, out var player))
        {
            player.Add(PlayerChanges.ForcedRedraw);
            _redrawOnNextApplication = false;
        }

        // Check for any pair data for optional plugins that we don't have installed/enabled
        if (charaDataToUpdate.TryGetValue(ObjectKind.Player, out var playerChanges))
        {
            _pluginWarningNotificationManager.NotifyForMissingPlugins(Pair.UserData, PlayerName!, playerChanges);
        }

        Logger.LogDebug("[BASE-{appbase}] Downloading and applying character for {name}", applicationBase, this);

        // Move on to downloading
        return DownloadAndApplyCharacter(applicationBase, characterData.DeepClone(), charaDataToUpdate);
    }

    /// <summary>
    /// Step 4
    /// Prepare update data and fire and forget async task
    /// </summary>
    private Task DownloadAndApplyCharacter(Guid applicationBase, CharacterData charaData, Dictionary<ObjectKind, HashSet<PlayerChanges>> updatedData)
    {
        if (!updatedData.Any())
        {
            Logger.LogDebug("[BASE-{appBase}] Nothing to update for {obj}", applicationBase, this);

            return Task.CompletedTask;
        }

        var updateModdedPaths = updatedData.Values.Any(v => v.Any(p => p == PlayerChanges.ModFiles));
        var updateManip = updatedData.Values.Any(v => v.Any(p => p == PlayerChanges.ModManip));

        _downloadCancellationTokenSource = _downloadCancellationTokenSource?.CancelRecreate() ?? new CancellationTokenSource();
        var downloadToken = _downloadCancellationTokenSource.Token;

        return DownloadAndApplyCharacterAsync(applicationBase, charaData, updatedData, updateModdedPaths, updateManip, downloadToken);
    }

    /// <summary>
    /// Step 5
    /// This is he start of the spaghetti monster to check comp alts, download files, check VRAM/Tris.
    /// </summary>
    private async Task DownloadAndApplyCharacterAsync(Guid applicationBase, CharacterData charaData, Dictionary<ObjectKind, HashSet<PlayerChanges>> updatedData,
        bool updateModdedPaths, bool updateManip, CancellationToken downloadToken)
    {
        Stopwatch stopwatch;
        Dictionary<(string GamePath, string? Hash), string> moddedPaths = [];
        HashSet<string> locallyPresentFiles;
        int numberOfFilesToDownload = -1;
        var compressedAlternateUsage = ComputeCompressedAlternateUsage(); // check if we are using comp alts or not for this pair

        if (updateModdedPaths)
        {
            int maxAttempts = 10;
            int attempts = 0;
            ActiveCompressionRedirects.Clear();
            // This part does a ton of lifting, but basically checks for mod files we do not have yet that we will need for this pair
            List<FileReplacementData> toDownloadReplacements = TryCalculateModdedDictionary(applicationBase, charaData, compressedAlternateUsage, ActiveCompressionRedirects, out locallyPresentFiles, out moddedPaths, downloadToken);

            stopwatch = Stopwatch.StartNew();
            // We go through the download loop up to 10 times in case any of the downloads fail. This is honestly hacky and should be better handled based on failure reason.
            while (toDownloadReplacements.Count > 0 && attempts < maxAttempts && !downloadToken.IsCancellationRequested)
            {
                attempts++;

                if (_pairDownloadTask != null && !_pairDownloadTask.IsCompleted)
                {
                    Logger.LogDebug("[BASE-{appBase}] Finishing prior running download task for player {name}, {kind}", applicationBase, PlayerName, updatedData);
                    await _pairDownloadTask.ConfigureAwait(false);
                }

                Logger.LogDebug("[BASE-{appBase}] Downloading missing files for player {name}, {kind}", applicationBase, PlayerName, updatedData);

                Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairHandler), EventSeverity.Informational,
                    $"Starting download for {toDownloadReplacements.Count} files")));
                Dictionary<string, string> compressionSubstitutions = new Dictionary<string, string>();
                // This gets a list of file download dtos from the file server that we need for this pair, not the actual files. This contains meta data and download links for each file.
                var toDownloadFiles = await _downloadManager.InitiateDownloadList(_charaHandler!, toDownloadReplacements, compressedAlternateUsage, compressionSubstitutions, locallyPresentFiles, downloadToken).ConfigureAwait(false);
                if (numberOfFilesToDownload < 0)
                {
                    numberOfFilesToDownload = toDownloadFiles.Count;
                }
                // basically check the meta data for each of the files before we even download them to see if it'll exceed thresholds
                if (!_playerPerformanceService.ComputeAndAutoPauseOnVRAMUsageThresholds(this, charaData, toDownloadFiles))
                {
                    _downloadManager.ClearDownload();

                    return;
                }

                // start background task to download needed files
                _pairDownloadTask = Task.Run(async () => await _downloadManager.DownloadFiles(_charaHandler!, toDownloadReplacements, compressionSubstitutions, downloadToken).ConfigureAwait(false));

                await _pairDownloadTask.ConfigureAwait(false);

                if (downloadToken.IsCancellationRequested)
                {
                    Logger.LogTrace("[BASE-{appBase}] Detected cancellation", applicationBase);

                    return;
                }

                // check again if we have files we still need to download, if so, this loop begins again
                toDownloadReplacements = TryCalculateModdedDictionary(applicationBase, charaData, compressedAlternateUsage, ActiveCompressionRedirects, out locallyPresentFiles, out moddedPaths, downloadToken);

                if (toDownloadReplacements.TrueForAll(c => _downloadManager.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, c.Hash, StringComparison.Ordinal))))
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), downloadToken).ConfigureAwait(false);
            }

            stopwatch.Stop();

            // we should have all of our files by now, if not, we need to investigate/mitigate the root cause
            if (toDownloadReplacements.Count > 0 && !downloadToken.IsCancellationRequested)
            {
                Logger.LogError("[BASE-{appBase}] Failed to download {count} hashes for {player}:{uid} Hashes: {hashes}", 
                    applicationBase, toDownloadReplacements.Count, PlayerName, Pair.UserData.UID, string.Join(',', toDownloadReplacements.Select(file => file.Hash)));
                throw new InvalidOperationException($"Failed to download one or more required files for {PlayerName}:{Pair.UserData.UID}");
            }

            if (numberOfFilesToDownload > 0) // we may not have needed to download anything, so don't report it
            {
                Logger.LogDebug("[BASE-{appBase}] Downloaded {count} file(s) in {attempts} total attempt(s) for {player}:{uid} in {elapsedMs}ms",
                applicationBase, numberOfFilesToDownload, attempts, PlayerName, Pair.UserData.UID, stopwatch.ElapsedMilliseconds);
            }

            if (!await _playerPerformanceService.CheckBothThresholds(this, charaData).ConfigureAwait(false))
            {
                return;
            }

            if (!_playerPerformanceService.CheckForRspHeight(this, charaData))
            {
                return;
            }
        }

        downloadToken.ThrowIfCancellationRequested();

        var appToken = _applicationCancellationTokenSource?.Token;
        while ((!_applicationTask?.IsCompleted ?? false) && !downloadToken.IsCancellationRequested && (!appToken?.IsCancellationRequested ?? false))
        {
            // block until current application is done
            Logger.LogDebug("[BASE-{appBase}] Waiting for current data application (Id: {id}) for player ({handler}) to finish", applicationBase, _applicationId, PlayerName);
            await Task.Delay(250).ConfigureAwait(false);
        }

        if (downloadToken.IsCancellationRequested || (appToken?.IsCancellationRequested ?? false))
        {
            return;
        }

        _applicationCancellationTokenSource = _applicationCancellationTokenSource.CancelRecreate() ?? new CancellationTokenSource();
        var token = _applicationCancellationTokenSource.Token;

        _applicationTask = ApplyCharacterDataAsync(applicationBase, charaData, updatedData, updateModdedPaths, updateManip, moddedPaths, token);
        await _applicationTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Step 6
    /// This does file validation checks, then sets mods in Penumbra, then addons plugins + glamourer, then redraw
    /// </summary>
    private async Task ApplyCharacterDataAsync(Guid applicationBase, CharacterData charaData, Dictionary<ObjectKind, HashSet<PlayerChanges>> updatedData, bool updateModdedPaths, bool updateManip,
        Dictionary<(string GamePath, string? Hash), string> moddedPaths, CancellationToken token)
    {
        _applicationId = Guid.NewGuid();

        if (_penumbraCollection is null)
        {
            Logger.LogError("No penumbra collection exists for {pair}!", Pair.PlayerName);
            return;
        }

        // Proactively check the incoming files for specific known sources of crashes
        if (_configService.Current.EnableValidationChecks)
        {
            foreach (var file in moddedPaths.ToArray()) // .ToArray() so we can modify the dictionary as we loop through it
            {
                if (file.Key.Hash == null)
                {
                    continue;
                }

                var extension = Path.GetExtension(file.Key.GamePath);
                var filePath = file.Value;

                if (filePath != null)
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(filePath);
                        var validationMessages = FileValidation.ValidateFile(_dataManager.Excel, bytes, extension, path => path.Contains('/') && (_dataManager.FileExists(path) || charaData.FileReplacements.Values.SelectMany(value => value).Any(replacement => replacement.GamePaths.Contains(path)))).ToArray();

                        if (validationMessages.Length > 0)
                        {
                            var messageString = string.Join(", ", validationMessages.Select(message => $"[{message.ID}]: {message.Title} ({message.Level})"));

                            if (validationMessages.Any(message => message.Level == MessageLevel.Crash))
                            {
                                Logger.LogWarning("{uid} ({name}): File {hash} to be used as {gamePath} looks like it could crash game and will be ignored. \n  {description}", Pair.UserData.UID, PlayerName, file.Key.Hash, file.Key.GamePath, messageString);
                                moddedPaths.Remove(file.Key);
                            }
                            else
                            {
                                Logger.LogInformation("{uid} ({name}): File {hash} to be used as {gamePath} looks like it might have some mistakes, but will still be used. \n  {description}", Pair.UserData.UID, PlayerName, file.Key.Hash, file.Key.GamePath, messageString);
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        Logger.LogWarning(ex, "Couldn't read downloaded file {filePath} for validation!", filePath);
                    }
                }
            }
        }

        try
        {
            Logger.LogDebug("[BASE-{applicationId}] Starting application task for {this}: {appId}", applicationBase, this, _applicationId);
            Logger.LogDebug("[{applicationId}] Waiting for initial draw for for {handler}", _applicationId, _charaHandler);
            await _dalamudUtil.WaitWhileCharacterIsDrawing(Logger, _charaHandler!, _applicationId, 10000, true, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            if (updateModdedPaths)
            {
                // ensure collection is set
                // This call can sometimes fail with a no ref if the player is no longer visible
                // The catch clause covers the mitigating result- this should be reworked.
                var objIndex = await _dalamudUtil.RunOnFrameworkThread(() => _charaHandler!.GetGameObject()!.ObjectIndex).ConfigureAwait(false);

                await _ipcManager.Penumbra.AssignTemporaryCollectionAsync(Logger, _penumbraCollection.Value, objIndex).ConfigureAwait(false);
                string? pairUid = String.IsNullOrWhiteSpace(Pair.UserData.UID) ? null : Pair.UserData.UID;
                await _ipcManager.Penumbra.SetTemporaryModsAsync(Logger, _applicationId, _penumbraCollection.Value,
                    moddedPaths.ToDictionary(k => k.Key.GamePath, k => k.Value, StringComparer.Ordinal), pairUid).ConfigureAwait(false);

                LastAppliedDataBytes = -1;
                foreach (var path in moddedPaths.Values.Distinct(StringComparer.OrdinalIgnoreCase).Select(v => new FileInfo(v)).Where(p => p.Exists))
                {
                    if (LastAppliedDataBytes == -1) LastAppliedDataBytes = 0;

                    LastAppliedDataBytes += path.Length;
                }
            }

            // do height checking
            _playerPerformanceService.CheckForRspHeight(this, charaData);

            if (updateManip)
            {
                await _ipcManager.Penumbra.SetManipulationDataAsync(Logger, _applicationId, _penumbraCollection.Value, charaData.ManipulationData).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            // This runs against each object kind, player, pet, etc. and applies the changes
            foreach (var kind in updatedData)
            {
                await ApplyCustomizationDataAsync(_applicationId, kind, charaData, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }

            _cachedData = charaData;

            Logger.LogDebug("[{applicationId}] Application finished", _applicationId);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Any(e => e is NullReferenceException or ArgumentNullException))
        {
            IsVisible = false;
            _forceApplyMods = true;
            _cachedData = charaData;
            Logger.LogDebug("[{applicationId}] Cancelled, player turned null during application", _applicationId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[{applicationId}] Cancelled", _applicationId);
        }
    }

    /// <summary>
    /// Step 7
    /// Applies changes by object kind
    /// </summary>
    private async Task ApplyCustomizationDataAsync(Guid applicationId, KeyValuePair<ObjectKind, HashSet<PlayerChanges>> changes, CharacterData charaData, CancellationToken token)
    {
        if (PlayerCharacter == nint.Zero)
        {
            return;
        }

        bool needsToBeRedrawn = false;
        var ptr = PlayerCharacter;
        var handler = changes.Key switch
        {
            ObjectKind.Player => _charaHandler!,
            ObjectKind.Companion => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetCompanionPtr(ptr), isWatched: false).ConfigureAwait(false),
            ObjectKind.MinionOrMount => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetMinionOrMountPtr(ptr), isWatched: false).ConfigureAwait(false),
            ObjectKind.Pet => await _gameObjectHandlerFactory.Create(changes.Key, () => _dalamudUtil.GetPetPtr(ptr), isWatched: false).ConfigureAwait(false),
            _ => throw new NotSupportedException("ObjectKind not supported: " + changes.Key)
        };

        try
        {
            if (handler.Address == nint.Zero)
            {
                return;
            }

            Logger.LogDebug("[{applicationId}] Applying Customization Data for {handler}", applicationId, handler);
            await _dalamudUtil.WaitWhileCharacterIsDrawing(Logger, handler, applicationId, 5000, false, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            foreach (var change in changes.Value.OrderBy(p => (int)p))
            {
                Logger.LogDebug("[{applicationId}] Processing {change} for {handler}", applicationId, change, handler);
                switch (change)
                {
                    case PlayerChanges.Customize:
                        if (charaData.CustomizePlusData.TryGetValue(changes.Key, out var customizePlusData))
                        {
                            _customizeIds[changes.Key] = await _ipcManager.CustomizePlus.SetBodyScaleAsync(handler.Address, customizePlusData).ConfigureAwait(false);
                        }
                        else if (_customizeIds.TryGetValue(changes.Key, out var customizeId))
                        {
                            await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(false);
                            _customizeIds.Remove(changes.Key);
                        }
                        needsToBeRedrawn = true;
                        break;

                    case PlayerChanges.Heels:
                        await _ipcManager.Heels.SetOffsetForPlayerAsync(handler.Address, charaData.HeelsData).ConfigureAwait(false);
                        break;

                    case PlayerChanges.Honorific:
                        await _ipcManager.Honorific.SetTitleAsync(handler.Address, charaData.HonorificData).ConfigureAwait(false);
                        break;

                    case PlayerChanges.Glamourer:
                        if (charaData.GlamourerData.TryGetValue(changes.Key, out var glamourerData))
                        {
                            Logger.LogTrace("[{appId}] Glamourer data: {data}", applicationId, glamourerData);
                            await _ipcManager.Glamourer.ApplyAllNoRedrawAsync(Logger, handler, glamourerData, applicationId, token).ConfigureAwait(false);
                            needsToBeRedrawn = true;
                        }
                        break;

                    case PlayerChanges.Moodles:
                        //
                        // TEMP: Disabling Moodles->Loci compatibility until we get the go-ahead from Cordelia to add the corresponding Loci->Moodles compatibility.
                        //
                        //// If we have Loci, but not Moodles, and get Moodles data when LociData does not exist, convert to LociData and apply as a fallback.
                        //var useFallback = !_ipcManager.Moodles.APIAvailable && _ipcManager.Loci.APIAvailable;
                        //if (useFallback && (!charaData.LociData.TryGetValue(changes.Key, out var lociData) || string.IsNullOrEmpty(lociData)))
                        //{
                        //    var converted = _ipcManager.Loci.ConvertToLociData(charaData.MoodlesData);
                        //    if (!_lociRegistrations.GetValueOrDefault(changes.Key, false))
                        //    {
                        //        _lociRegistrations[changes.Key] = await _ipcManager.Loci.RegisterActor(handler.Address).ConfigureAwait(false);
                        //    }
                        //    await _ipcManager.Loci.SetActorManager(handler.Address, converted).ConfigureAwait(false);
                        //}
                        //else
                        {
                            await _ipcManager.Moodles.SetStatusAsync(handler.Address, charaData.MoodlesData).ConfigureAwait(false);
                        }
                        break;

                    case PlayerChanges.Loci:
                        // Ensure registry
                        if (!_lociRegistrations.GetValueOrDefault(changes.Key, false))
                        {
                            _lociRegistrations[changes.Key] = await _ipcManager.Loci.RegisterActor(handler.Address).ConfigureAwait(false);
                        }
                        var lociDataToApply = charaData.LociData.GetValueOrDefault(changes.Key, string.Empty);
                        await _ipcManager.Loci.SetActorManager(handler.Address, lociDataToApply).ConfigureAwait(false);
                        break;

                    case PlayerChanges.PetNames:
                        await _ipcManager.PetNames.SetPlayerData(handler.Address, charaData.PetNamesData).ConfigureAwait(false);
                        break;

                    case PlayerChanges.ForcedRedraw:
                        Pair.LastLoadedSoundSinceRedraw = null;
                        needsToBeRedrawn = true;
                        break;

                    default:
                        break;
                }
                token.ThrowIfCancellationRequested();
            }

            if (needsToBeRedrawn)
            {
                await _ipcManager.Penumbra.RedrawAsync(Logger, handler, applicationId, token).ConfigureAwait(false);
            }

        }
        finally
        {
            if (handler != _charaHandler)
            {
                handler.Dispose();
            }
        }
    }

    private List<FileReplacementData> TryCalculateModdedDictionary(Guid applicationBase, CharacterData charaData, CompressedAlternateUsage compressedAlternateUsage, ConcurrentDictionary<string, string> compressionSubstitutions, out HashSet<string> locallyPresentFiles, out Dictionary<(string GamePath, string? Hash), string> moddedDictionary, CancellationToken token)
    {
        Stopwatch st = Stopwatch.StartNew();
        ConcurrentBag<FileReplacementData> missingFiles = [];
        moddedDictionary = [];
        ConcurrentDictionary<(string GamePath, string? Hash), string> outputDict = new();
        bool hasMigrationChanges = false;
        var locallyPresentFileSet = new ConcurrentDictionary<string, object?>();

        try
        {
            var replacementList = charaData.FileReplacements.SelectMany(k => k.Value.Where(v => string.IsNullOrEmpty(v.FileSwapPath))).ToList();
            Parallel.ForEach(replacementList, new ParallelOptions()
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = 4
            },
            (item) =>
            {
                token.ThrowIfCancellationRequested();

                FileReplacementData replacementItem = item;

                var fileCache = _fileDbManager.GetFileCacheByHash(replacementItem.Hash);

                string? compressedAlternateHash = null;
                bool compressedAlternateConfirmed = _compressedAlternateManager.TryGetCachedCompressedAlternate(replacementItem.Hash, out compressedAlternateHash);

                // Adjust replacementItem and fileCache according to the given policy for compressed alternates
                if (compressedAlternateUsage == CompressedAlternateUsage.AlwaysSourceQuality)
                {
                    // Nothing to do here--carry on as usual.
                }
                else if (compressedAlternateUsage == CompressedAlternateUsage.CompressedNewDownloads)
                {
                    // Only use compressed alternates if the original file is not present in the cache.
                    if (fileCache == null && compressedAlternateConfirmed && compressedAlternateHash != null)
                    {
                        Logger.LogTrace("CompressSubstitution[{character}]: {path} is [{new}] instead of source [{old}] (TryCalculateModdedDictionary)", PlayerName, string.Join(',', replacementItem.GamePaths), compressedAlternateHash, replacementItem.Hash);
                        compressionSubstitutions[replacementItem.Hash] = compressedAlternateHash;
                        fileCache = _fileDbManager.GetFileCacheByHash(compressedAlternateHash);
                        replacementItem = new FileReplacementData()
                        {
                            GamePaths = replacementItem.GamePaths,
                            Hash = compressedAlternateHash,
                        };

                    }
                }
                else if (compressedAlternateUsage == CompressedAlternateUsage.AlwaysCompressed)
                {
                    if (compressedAlternateConfirmed)
                    {
                        // We are certain about the existence of any compressed alternates. If there are, use it. If there aren't, carry on as usual.
                        if (compressedAlternateHash != null)
                        {
                            Logger.LogTrace("CompressSubstitution[{character}]: {path} is [{new}] instead of source [{old}] (TryCalculateModdedDictionary)", PlayerName, string.Join(',', replacementItem.GamePaths), compressedAlternateHash, replacementItem.Hash);
                            compressionSubstitutions[replacementItem.Hash] = compressedAlternateHash;
                            fileCache = _fileDbManager.GetFileCacheByHash(compressedAlternateHash);
                            replacementItem = new FileReplacementData()
                            {
                                GamePaths = replacementItem.GamePaths,
                                Hash = compressedAlternateHash,
                            };
                        }
                    }
                    else
                    {
                        Logger.LogTrace("CompressSubstitution[{character}]: sending {path} source [{old}] for re-download to check for alternates (TryCalculateModdedDictionary)", PlayerName, string.Join(',', replacementItem.GamePaths), replacementItem.Hash);
                        // Record this hash to send to the download function as 'locally present', so that when it checks for alternates,
                        // it won't re-download this original file if none exist.
                        locallyPresentFileSet[replacementItem.Hash] = null;

                        // We don't know whether there are any compressed alternates, so mark this file as needing downloading.
                        // Once the download starts, if there aren't any, the actual download will be skipped.
                        fileCache = null;
                    }
                }
                else
                {
                    throw new ArgumentException("Invalid compressed alternate usage specified!", nameof(compressedAlternateUsage));
                }

                if (fileCache != null)
                {
                    if (string.IsNullOrEmpty(new FileInfo(fileCache.ResolvedFilepath).Extension))
                    {
                        hasMigrationChanges = true;
                        fileCache = _fileDbManager.MigrateFileHashToExtension(fileCache, replacementItem.GamePaths[0].Split(".")[^1]);
                    }

                    foreach (var gamePath in replacementItem.GamePaths)
                    {
                        outputDict[(gamePath, replacementItem.Hash)] = fileCache.ResolvedFilepath;
                    }
                }
                else
                {
                    Logger.LogTrace("Missing file: {hash}", replacementItem.Hash);
                    missingFiles.Add(replacementItem);
                }
            });

            moddedDictionary = outputDict.ToDictionary(k => k.Key, k => k.Value);
            locallyPresentFiles = locallyPresentFileSet.Keys.ToHashSet();

            foreach (var item in charaData.FileReplacements.SelectMany(k => k.Value.Where(v => !string.IsNullOrEmpty(v.FileSwapPath))).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    Logger.LogTrace("[BASE-{appBase}] Adding file swap for {path}: {fileSwap}", applicationBase, gamePath, item.FileSwapPath);
                    moddedDictionary[(gamePath, null)] = item.FileSwapPath;
                }
            }
        }
        catch (Exception ex)
        {
            locallyPresentFiles = new HashSet<string>();
            Logger.LogError(ex, "[BASE-{appBase}] Something went wrong during calculation replacements", applicationBase);
        }
        if (hasMigrationChanges) _fileDbManager.WriteOutFullCsvImmediate();
        st.Stop();
        Logger.LogDebug("[BASE-{appBase}] ModdedPaths calculated in {time}ms, missing files: {count}, total files: {total}", applicationBase, st.ElapsedMilliseconds, missingFiles.Count, moddedDictionary.Keys.Count);
        
        return [.. missingFiles];
    }

    private void OnPenumbraResourceLoaded(PenumbraResourceLoadMessage resourceLoad)
    {
        if (resourceLoad.GameObject == PlayerCharacter
            || (LastCompanionPtr != nint.Zero && resourceLoad.GameObject == LastCompanionPtr)
            || (LastMinionOrMountPtr != nint.Zero && resourceLoad.GameObject == LastMinionOrMountPtr)
            || (LastPetPtr != nint.Zero && resourceLoad.GameObject == LastPetPtr))
        {
            // If the load was for a sound file, remember that
            if (resourceLoad.GamePath.EndsWith(".scd", StringComparison.OrdinalIgnoreCase))
            {
                Pair.LastLoadedSoundSinceRedraw = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// Determines whether to use compressed alternate files for this pair.
    /// </summary>
    /// <returns></returns>
    private CompressedAlternateUsage ComputeCompressedAlternateUsage()
    {
        // whitelist check
        if (_performanceConfig.Current.UIDsToOverride
            .Exists(uid => string.Equals(uid, Pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, Pair.UserData.UID, StringComparison.Ordinal)))
        {
            return CompressedAlternateUsage.AlwaysSourceQuality;
        }

        // TODO: Implement finer-grained rules around whether this pair should use compressed alternate files
        return _performanceConfig.Current.TextureCompressionModeOrDefault;
    }

    private async Task RevertCustomizationDataAsync(ObjectKind objectKind, string name, Guid applicationId, CancellationToken cancelToken)
    {
        nint address = _dalamudUtil.GetPlayerCharacterFromCachedTableByIdent(Pair.Ident);
        if (address == nint.Zero)
        {
            return;
        }

        Logger.LogDebug("[{applicationId}] Reverting all Customization for {alias}/{name} {objectKind}", applicationId, Pair.UserData.AliasOrUID, name, objectKind);

        if (_customizeIds.TryGetValue(objectKind, out var customizeId))
        {
            _customizeIds.Remove(objectKind);
        }

        if (objectKind == ObjectKind.Player)
        {
            using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () => address, isWatched: false).ConfigureAwait(false);

            tempHandler.CompareNameAndThrow(name); // FFFFFFFFFFFFFFF
            Logger.LogDebug("[{applicationId}] Restoring Customization and Equipment for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
            await _ipcManager.Glamourer.RevertAsync(Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(false);

            tempHandler.CompareNameAndThrow(name); // FFFFFFFFFFFFFFF
            Logger.LogDebug("[{applicationId}] Restoring Heels for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
            await _ipcManager.Heels.RestoreOffsetForPlayerAsync(address).ConfigureAwait(false);

            tempHandler.CompareNameAndThrow(name); // FFFFFFFFFFFFFFF
            Logger.LogDebug("[{applicationId}] Restoring C+ for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
            await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(false);

            tempHandler.CompareNameAndThrow(name); // FFFFFFFFFFFFFFF
            Logger.LogDebug("[{applicationId}] Restoring Honorific for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
            await _ipcManager.Honorific.ClearTitleAsync(address).ConfigureAwait(false);

            Logger.LogDebug("[{applicationId}] Restoring Moodles for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
            await _ipcManager.Moodles.RevertStatusAsync(address).ConfigureAwait(false);

            Logger.LogDebug("[{applicationId}] Restoring Pet Nicknames for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
            await _ipcManager.PetNames.ClearPlayerData(address).ConfigureAwait(false);

            Logger.LogDebug("[{applicationId}] Unregistering Loci for {alias}/{name}", applicationId, Pair.UserData.AliasOrUID, name);
            await _ipcManager.Loci.UnregisterActor(address).ConfigureAwait(false);
        }
        else if (objectKind == ObjectKind.MinionOrMount)
        {
            var minionOrMount = await _dalamudUtil.GetMinionOrMountAsync(address).ConfigureAwait(false);
            if (minionOrMount != nint.Zero)
            {
                await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(false);
                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.MinionOrMount, () => minionOrMount, isWatched: false).ConfigureAwait(false);
                await _ipcManager.Loci.UnregisterBuddy(name, tempHandler.Name).ConfigureAwait(false);
                await _ipcManager.Glamourer.RevertAsync(Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(false);
                await _ipcManager.Penumbra.RedrawAsync(Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Pet)
        {
            var pet = await _dalamudUtil.GetPetAsync(address).ConfigureAwait(false);
            if (pet != nint.Zero)
            {
                await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(false);
                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Pet, () => pet, isWatched: false).ConfigureAwait(false);
                await _ipcManager.Loci.UnregisterBuddy(name, tempHandler.Name).ConfigureAwait(false);
                await _ipcManager.Glamourer.RevertAsync(Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(false);
                await _ipcManager.Penumbra.RedrawAsync(Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(false);
            }
        }
        else if (objectKind == ObjectKind.Companion)
        {
            var companion = await _dalamudUtil.GetCompanionAsync(address).ConfigureAwait(false);
            if (companion != nint.Zero)
            {
                await _ipcManager.CustomizePlus.RevertByIdAsync(customizeId).ConfigureAwait(false);
                using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Pet, () => companion, isWatched: false).ConfigureAwait(false);
                await _ipcManager.Loci.UnregisterBuddy(name, tempHandler.Name).ConfigureAwait(false);
                await _ipcManager.Glamourer.RevertAsync(Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(false);
                await _ipcManager.Penumbra.RedrawAsync(Logger, tempHandler, applicationId, cancelToken).ConfigureAwait(false);
            }
        }

        Pair.LastLoadedSoundSinceRedraw = null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        SetUploading(isUploading: false);
        var name = PlayerName;
        Logger.LogDebug("Disposing {name} ({user})", name, Pair);
        try
        {
            Guid applicationId = Guid.NewGuid();
            _applicationCancellationTokenSource?.CancelDispose();
            _applicationCancellationTokenSource = null;
            _downloadCancellationTokenSource?.CancelDispose();
            _downloadCancellationTokenSource = null;
            _downloadManager.Dispose();
            _charaHandler?.Dispose();
            _charaHandler = null;

            if (!string.IsNullOrEmpty(name))
            {
                Mediator.Publish(new EventMessage(new Event(name, Pair.UserData, nameof(PairHandler), EventSeverity.Informational, "Disposing User")));
            }

            if (_lifetime.ApplicationStopping.IsCancellationRequested) return;

            if (_dalamudUtil is { IsZoning: false, IsInCutscene: false } && !string.IsNullOrEmpty(name))
            {
                Logger.LogTrace("[{applicationId}] Restoring state for {name} ({OnlineUser})", applicationId, name, Pair.UserPair);
                Logger.LogDebug("[{applicationId}] Removing Temp Collection for {name} ({user})", applicationId, name, Pair.UserPair);
                if (_penumbraCollection is not null)
                    _ipcManager.Penumbra.RemoveTemporaryCollectionAsync(Logger, applicationId, _penumbraCollection.Value).GetAwaiter().GetResult();
                if (!IsVisible)
                {
                    Logger.LogDebug("[{applicationId}] Restoring Glamourer for {name} ({user})", applicationId, name, Pair.UserPair);
                    _ipcManager.Glamourer.RevertByNameAsync(Logger, name, applicationId).GetAwaiter().GetResult();
                }
                else
                {
                    using var cts = new CancellationTokenSource();
                    cts.CancelAfter(TimeSpan.FromSeconds(60));

                    Logger.LogInformation("[{applicationId}] CachedData is null {isNull}, contains things: {contains}", applicationId, _cachedData == null, _cachedData?.FileReplacements.Any() ?? false);

                    foreach (KeyValuePair<ObjectKind, List<FileReplacementData>> item in _cachedData?.FileReplacements ?? [])
                    {
                        try
                        {
                            // NOTE: THIS CAN FREEZE THE GAME
                            RevertCustomizationDataAsync(item.Key, name, applicationId, cts.Token).GetAwaiter().GetResult();
                        }
                        catch (InvalidOperationException ex)
                        {
                            Logger.LogWarning(ex, "Failed disposing player (not present anymore?)");
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error on disposal of {name}", name);
        }
        finally
        {
            PlayerName = null;
            _penumbraCollection = null;
            _cachedData = null;
            Logger.LogDebug("Disposing {name} complete", name);
        }
    }

    // This framework update tick is used by the PairHandler to check if the pair's CharacterHandler is still valid or not.
    // If we have a valid CharacterHandler but the pair was not visible, we mark them as visible and reapply cached data if available.
    // If we no longer have a valid CharacterHandler, we zero out the Ptrs and invalidate the CharacterHandler
    private void FrameworkUpdate()
    {
        if (_charaHandler?.Address != nint.Zero) // We have a valid GameObjectHandler for this Pair
        {
            // Update pointers this frame as to not dereference old/stale/invalid ptr
            LastCompanionPtr = _dalamudUtil.GetCompanionPtr(PlayerCharacter);
            LastMinionOrMountPtr = _dalamudUtil.GetMinionOrMountPtr(PlayerCharacter);
            LastPetPtr = _dalamudUtil.GetPetPtr(PlayerCharacter);

            if (!IsVisible) // We can now "see" the player in game
            {
                Guid appData = Guid.NewGuid();
                IsVisible = true; // This is the only time we mark a pair as visible = true
                if (_cachedData != null) // Apply cached data so we're not always recreating the player
                {
                    Logger.LogTrace("[BASE-{appBase}] {this} visibility changed, now: {visi}, cached data exists", appData, this, IsVisible);

                    _ = Task.Run(() =>
                    {
                        ApplyCharacterData(appData, _cachedData!, forceApplyCustomization: true);
                    });
                }
                else
                {
                    Logger.LogTrace("{this} visibility changed, now: {visi}, no cached data exists", this, IsVisible);
                }
            }
        }
        else // We no longer have a valid GameObjectHandler for this Pair
        {
            LastCompanionPtr = nint.Zero;
            LastMinionOrMountPtr = nint.Zero;
            LastPetPtr = nint.Zero;

            if (IsVisible)
            {
                IsVisible = false;
                _charaHandler.Invalidate();
                _downloadCancellationTokenSource?.CancelDispose();
                _downloadCancellationTokenSource = null;
                Logger.LogTrace("{this} visibility changed, now: {visi}", this, IsVisible);
            }
        }
    }

    private async Task ReloadAndLockVanillaState(nint address, Guid applicationBase)
    {
        if (Interlocked.Exchange(ref _isRedrawVanillaRunning, 1) == 1)
        {
            return;
        }

        try
        {
            Logger.LogInformation("[BASE-{appbase}] Filter task started for {player}", applicationBase, this);

            // grab a temp character handler since Glamourer will just pass a nint anyway
            using GameObjectHandler tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player, () => address, isWatched: false).ConfigureAwait(false);

            // revert their Glamourer state, this will unlock and "revert" to their new vanilla
            await _ipcManager.Glamourer.RevertAsync(Logger, tempHandler, applicationBase, CancellationToken.None).ConfigureAwait(false);

            // grab their new vanilla glam state
            var charState = await _ipcManager.Glamourer.GetCharacterCustomizationAsync(tempHandler.Address).ConfigureAwait(false);

            // apply the glamourer state and lock the target
            await _ipcManager.Glamourer.ApplyAllAsync(Logger, tempHandler, charState, Guid.NewGuid(), CancellationToken.None).ConfigureAwait(false);

            Logger.LogInformation("[BASE-{appbase}] Filter task completed for {player}", applicationBase, this);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[BASE-{appbase}] Filter task failed for {player}", applicationBase, this);
        }
        finally
        {
            Interlocked.Exchange(ref _isRedrawVanillaRunning, 0);
        }
    }

    private void CheckForVanillaLoadingOfPair()
    {
        var address = Pair.Address;

        if (address == nint.Zero)
        {
            Logger.LogTrace("Vanilla check bad address");
        }
            
        if (_charaHandler != null && _charaHandler.Address != address)
        {
            Logger.LogTrace("Vanilla check not address for {this}", this);
        }   

        if (!Pair.IsVisible)
        {
            Logger.LogTrace("Vanilla check pair not visible");
        }

        var applicationBase = Guid.NewGuid();

        Logger.LogInformation("[BASE-{appbase}] Filtering enabled; scheduling penumbra/glamourer apply for {player}", applicationBase, this);

        _ = ReloadAndLockVanillaState(address, applicationBase);
    }

    private bool IsVanillaEnforced()
    {
        bool filterBiDiPairs = _configService.Current.DoFilteringBidirectionDirectPairs;
        bool isDirectPaired = Pair.IndividualPairStatus == IndividualPairStatus.Bidirectional;
        bool overrideFilterPair = !filterBiDiPairs && isDirectPaired;

        bool overrideFilterUid = _configService.Current.UIDsToOverrideFilter.Contains(Pair.UserData.UID, StringComparer.OrdinalIgnoreCase)
            || _configService.Current.UIDsToOverrideFilter.Contains(Pair.UserData.Alias, StringComparer.OrdinalIgnoreCase);

        return (_configService.Current.FilterMods && !overrideFilterPair && !overrideFilterUid);
    }


    // It'd be nice if plugins were classes and not enums and strings

    public async Task HandleOptionalPluginDataAsync(AddonPlugin plugin, CharacterData charaData)
    {
        bool isCharacterInvalid = false;
        bool isCharacterInDowntime = false;

        if ((_charaHandler == null || (PlayerCharacter == IntPtr.Zero)) && _cachedData is not null)
        {
            isCharacterInvalid = true;
        }

        if (_dalamudUtil.IsInCombatOrPerforming || _idleStatusService.IsPlayerIdle)
        {
            if (_dataReceivedInDowntime is null)
            {
                return; // we only update partials after a full cache data is present
            }

            isCharacterInDowntime = true;
        }

        switch (plugin)
        {
            case AddonPlugin.Honorific:
                if (isCharacterInvalid)
                {
                    _cachedData!.HonorificData = charaData.HonorificData;

                    return;
                }

                if (isCharacterInDowntime)
                {
                    _dataReceivedInDowntime!.CharacterData.HonorificData = charaData.HonorificData;

                    return;
                }

                if (string.Equals(_cachedData!.HonorificData, charaData.HonorificData, StringComparison.Ordinal))
                {
                    return;
                }

                await ApplyHonorificDataASync(charaData).ConfigureAwait(false);

                return;

            case AddonPlugin.Heels:
                if (isCharacterInvalid)
                {
                    _cachedData!.HeelsData = charaData.HeelsData;

                    return;
                }

                if (isCharacterInDowntime)
                {
                    _dataReceivedInDowntime!.CharacterData.HeelsData = charaData.HeelsData;

                    return;
                }

                if (string.Equals(_cachedData!.HeelsData, charaData.HeelsData, StringComparison.Ordinal))
                {
                    Logger.LogDebug("Got Heels Data that is identical, applying anyway...");
                    //return; // this may be a bug? or something, need to investigate (freeze/resume state for livepose)
                }

                await ApplyHeelsDataAsync(charaData).ConfigureAwait(false);

                return;

            case AddonPlugin.Moodles:
                if (isCharacterInvalid)
                {
                    _cachedData!.MoodlesData = charaData.MoodlesData;

                    return;
                }

                if (isCharacterInDowntime)
                {
                    _dataReceivedInDowntime!.CharacterData.MoodlesData = charaData.MoodlesData;

                    return;
                }

                if (string.Equals(_cachedData!.MoodlesData, charaData.MoodlesData, StringComparison.Ordinal))
                {
                    return;
                }

                await ApplyMoodlesDataAsync(charaData).ConfigureAwait(false);

                return;

            case AddonPlugin.PetNames:
                if (isCharacterInvalid)
                {
                    _cachedData!.PetNamesData = charaData.PetNamesData;

                    return;
                }

                if (isCharacterInDowntime)
                {
                    _dataReceivedInDowntime!.CharacterData.PetNamesData = charaData.PetNamesData;

                    return;
                }

                if (string.Equals(_cachedData!.PetNamesData, charaData.PetNamesData, StringComparison.Ordinal))
                {
                    return;
                }

                await ApplyPetNicknamesDataAsync(charaData).ConfigureAwait(false);

                return;

            case AddonPlugin.Loci:
                if (isCharacterInvalid)
                {
                    _cachedData!.LociData = charaData.LociData;

                    return;
                }

                if (isCharacterInDowntime)
                {
                    _dataReceivedInDowntime!.CharacterData.LociData = charaData.LociData;

                    return;
                }

                // not going to bother with a dictionary equality right now

                await ApplyLociDataASync(charaData).ConfigureAwait(false);

                return;

            default:
                return;
        }
    }

    private async Task ApplyHonorificDataASync(CharacterData charaData)
    {
        try
        {
            await _ipcManager.Honorific.SetTitleAsync(_charaHandler!.Address, charaData.HonorificData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set Honorific data for {uid}", Pair.UserData.UID);
        }
    }

    private async Task ApplyHeelsDataAsync(CharacterData charaData)
    {
        try
        {
            await _ipcManager.Heels.SetOffsetForPlayerAsync(_charaHandler!.Address, charaData.HeelsData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set Heels data for {uid}", Pair.UserData.UID);
        }
    }

    private async Task ApplyMoodlesDataAsync(CharacterData charaData)
    {
        try
        {
            await _ipcManager.Moodles.SetStatusAsync(_charaHandler!.Address, charaData.MoodlesData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set Moodles data for {uid}", Pair.UserData.UID);
        }
    }

    private async Task ApplyLociDataASync(CharacterData charaData)
    {
        if (charaData.LociData is null)
        {
            return;
        }

        try
        {
            if (!_lociRegistrations.GetValueOrDefault(ObjectKind.Player, false))
            {
                _lociRegistrations[ObjectKind.Player] = await _ipcManager.Loci.RegisterActor(_charaHandler!.Address).ConfigureAwait(false);
            }

            var lociDataToApply = charaData.LociData.GetValueOrDefault(ObjectKind.Player, string.Empty);

            await _ipcManager.Loci.SetActorManager(_charaHandler!.Address, lociDataToApply).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set Loci data for {uid}", Pair.UserData.UID);
        }
    }

    private async Task ApplyPetNicknamesDataAsync(CharacterData charaData)
    {
        try
        {
            await _ipcManager.PetNames.SetPlayerData(_charaHandler!.Address, charaData.PetNamesData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set Pet Nicknames data for {uid}", Pair.UserData.UID);
        }
    }
}
