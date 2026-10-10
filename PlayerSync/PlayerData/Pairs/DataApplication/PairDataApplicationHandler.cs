using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Penumbra.Api.Enums;
using PlayerSync.FileCache;
using PlayerSync.Validation;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace MareSynchronos.PlayerData.Pairs;

public sealed class PairDataApplicationHandler : DisposableMediatorSubscriberBase
{
    private sealed record DataApplication
    {
        public required Guid ApplicationBase;
        public required CharacterData CharacterData;
        public required long Sequence;
        public bool ForceApplyCustomization = false;
        public Dictionary<ObjectKind, HashSet<PlayerChanges>> CharacterDataToUpdate = [];
        public bool UpdateManip => CharacterDataToUpdate.Values.Any(v => v.Any(p => p == PlayerChanges.ModManip));
        public bool UpdateModdedPaths => CharacterDataToUpdate.Values.Any(v => v.Any(p => p == PlayerChanges.ModFiles));
    }

    private static readonly SemaphoreSlim _dataApplicationGate = new(4, 4);
    private static readonly IImmutableList<string> FileExtensionsToValidate = [".avfx", ".tmb"];
    private static readonly ConcurrentDictionary<string, byte> _filesThatHaveBeenVerifiedGood = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> _filesThatHaveBeenVerifiedBad = new(StringComparer.OrdinalIgnoreCase);
    private static int _pendingDataIpcCounts = 0;

    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileDownloadManager _downloadManager;
    private readonly FileCacheManager _fileDbManager;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IpcManager _ipcManager;
    private readonly PlayerPerformanceService _playerPerformanceService;
    private readonly PluginWarningNotificationService _pluginWarningNotificationManager;
    private readonly PlayerPerformanceConfigService _performanceConfig;
    private readonly MareConfigService _configService;
    private readonly ZoneSyncConfigService _zoneSyncConfig;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IDataManager _dataManager;
    //private readonly JsonlStreamLogger _jsonlStreamLogger;

    private readonly ICompressedAlternateManager _compressedAlternateManager;
    private readonly Dictionary<ObjectKind, Guid?> _customizeIds = [];
    private readonly Dictionary<ObjectKind, bool> _lociRegistrations = [];
    private readonly object _cachedDataLock = new();
    private readonly object _dataApplicationRunningLock = new();
    // https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.-ctor?view=net-10.0#system-collections-concurrent-concurrentdictionary-2-ctor(system-collections-generic-ienumerable((system-collections-generic-keyvaluepair((-0-1))))-system-collections-generic-iequalitycomparer((-0)))
    private readonly ConcurrentDictionary<ObjectKind, ConcurrentDictionary<PlayerChanges, double>> _frameworkTimings = 
        new(Enum.GetValues<ObjectKind>().Select(kind => 
        new KeyValuePair<ObjectKind, ConcurrentDictionary<PlayerChanges, double>>(kind,new ConcurrentDictionary<PlayerChanges, double>(Enum.GetValues<PlayerChanges>().Select(change => 
        new KeyValuePair<PlayerChanges, double>(change, 0d))))));

    private GameObjectHandler? _charaHandler;
    private CharacterData? _cachedData = null;
    private CharacterData? _pendingData = null;
    private Guid? _penumbraCollection;
    private CancellationTokenSource? _dataApplicationCancellationTokenSource = new();
    private bool _forceApplyMods = false;
    private int _redrawOnNextApplication;
    private int _isRedrawVanillaRunning = 0;
    private bool _isVanillaEnforced;
    private bool _isLocked = false;
    private long _currentVersion = -1;
    private long _pendingVersion = -1;
    private bool _isPairValid = false;
    private Guid? _runningApplicationId = null;
    private bool _initialized;

    public PairDataApplicationHandler(ILogger<PairDataApplicationHandler> logger, Pair pair,
        GameObjectHandlerFactory gameObjectHandlerFactory,
        IpcManager ipcManager, FileDownloadManager transferManager,
        PluginWarningNotificationService pluginWarningNotificationManager,
        DalamudUtilService dalamudUtil, IHostApplicationLifetime lifetime,
        FileCacheManager fileDbManager, MareMediator mediator,
        PlayerPerformanceService playerPerformanceService,
        ICompressedAlternateManager compressedAlternateManager,
        MareConfigService configService,
        PlayerPerformanceConfigService performanceConfig,
        ZoneSyncConfigService zoneSyncConfig,
        IDataManager dataManager
        //JsonlStreamLogger jsonlStreamLogger
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
        _compressedAlternateManager = compressedAlternateManager;
        _performanceConfig = performanceConfig;
        _dataManager = dataManager;
        _configService = configService;
        _zoneSyncConfig = zoneSyncConfig;
        _isVanillaEnforced = IsVanillaEnforced();
        //_jsonlStreamLogger = jsonlStreamLogger;

        Mediator.Subscribe<PenumbraInitializedMessage>(this, (_) =>
        {
            _penumbraCollection = _ipcManager.Penumbra.CreateTemporaryCollectionAsync(logger, Pair.UserData.UID).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!pair.IsVisible && _charaHandler != null)
            {
                _charaHandler = null;
            }
        });

        Mediator.Subscribe<ClassJobChangedMessage>(this, (msg) =>
        {
            if (msg.GameObjectHandler == _charaHandler)
            {
                Interlocked.Exchange(ref _redrawOnNextApplication, 1);
            }
        });

        Mediator.Subscribe<PlayerIdleEndMessage>(this, _ =>
        {
            if (!Pair.IsVisible)
            {
                Logger.LogTrace("Removing deferred pair data for {uid} after IDLE has become ACTIVE.", Pair.UserData.UID);
                SetPairCharacterData(null);
            }
        });

        Mediator.Subscribe<PlayerDrawEndMessage>(this, (msg) =>
        {
            if (msg.PlayerName == Pair.PlayerName && _isVanillaEnforced)
            {
                CheckForVanillaLoadingOfPair();
            }
        });

        LastAppliedDataBytes = -1;
    }

    public bool HasCachedData
    {
        get
        {
            lock (_cachedDataLock)
            {
                return _cachedData != null || _pendingData != null;
            }
        }
    }
    public bool HasDataApplicationTaskRunning
    {
        get
        {
            lock (_dataApplicationRunningLock)
            {
                return _runningApplicationId != null;
            }
        }
    }
    // Maps from hash in the last applied data to compressed hash
    public ConcurrentDictionary<string, string> ActiveCompressionRedirects { get; private set; } = new ConcurrentDictionary<string, string>();
    public IReadOnlyDictionary<ObjectKind, ConcurrentDictionary<PlayerChanges, double>> DataApplicationTiming => _frameworkTimings;
    public ConcurrentDictionary<string, string> MissingFiles { get; private set; } = new ConcurrentDictionary<string, string>();
    public long LastAppliedDataBytes { get; private set; }
    public long PendingDataSequenceNumber => Volatile.Read(ref _pendingVersion);
    public DateTimeOffset? LastDataApplicationTime { get; private set; } = DateTimeOffset.MinValue;
    private Pair Pair { get; set; }
    private string PlayerName => Pair.PlayerName ?? string.Empty;
    private Task? _pairDownloadTask;
    private bool IsCachedDataApplication => _currentVersion == _pendingVersion;

    public override string ToString()
    {
        return Pair == null ? base.ToString() ?? string.Empty : Pair.UserData.AliasOrUID + ":" + PlayerName + ":" + (Pair.IsVisible ? "HasChar" : "NoChar");
    }

    public void Initialize(GameObjectHandler charaHandler)
    {
        _charaHandler = charaHandler;
        _penumbraCollection ??= _ipcManager.Penumbra.CreateTemporaryCollectionAsync(Logger, Pair.UserData.UID).ConfigureAwait(false).GetAwaiter().GetResult();

        _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var visibility = _dalamudUtil.GetFrameworkCharacterVisibilityByContentId(Pair.ContendId);
            if (visibility?.Ready == true)
            {
                _dalamudUtil.TryActOnFramework(Pair.ContendId, (chara, _) =>
                {
                    _ipcManager.Penumbra.AssignTemporaryCollection(Logger, _penumbraCollection.Value, chara.ObjectIndex);
                }, out _);
            }
        }).GetAwaiter().GetResult();
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        Mediator.Subscribe<HonorificReadyMessage>(this, (__) =>
        {
            if (string.IsNullOrEmpty(_cachedData?.HonorificData))
            {
                return;
            }

            Logger.LogTrace("Reapplying Honorific data for {this}", this);
            string honorificData = string.IsNullOrEmpty(_cachedData.HonorificData) ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(_cachedData.HonorificData));
            _dalamudUtil.RunOnFrameworkThread(() =>
            {
                _dalamudUtil.TryGetPlayerAddressAndIndexOnFrameworkByContentId(Pair.ContendId, out _, out var index);
                _ipcManager.Honorific.SetTitleOnFramework(index, honorificData);
            }).GetAwaiter().GetResult();
        });

        Mediator.Subscribe<PetNamesReadyMessage>(this, (__) =>
        {
            if (string.IsNullOrEmpty(_cachedData?.PetNamesData))
            {
                return;
            }

            Logger.LogTrace("Reapplying Pet Names data for {this}", this);
            _dalamudUtil.RunOnFrameworkThread(() =>
            {
                _dalamudUtil.TryGetPlayerAddressAndIndexOnFrameworkByContentId(Pair.ContendId, out _, out var index);
                _ipcManager.PetNames.SetPlayerDataOnFramework(index, _cachedData.PetNamesData);
            }).GetAwaiter().GetResult();
        });

        Mediator.Subscribe<LociReadyMessage>(this, (__) =>
        {
            if (_cachedData == null)
            {
                return;
            }
            if (!_cachedData.LociData.TryGetValue(ObjectKind.Player, out var data) || string.IsNullOrEmpty(data))
            {
                return;
            }

            Logger.LogTrace("Reapplying Loci data for {this}", this);

            _dalamudUtil.RunOnFrameworkThread(() =>
            {
                _dalamudUtil.TryActOnFramework(Pair.ContendId, (chara, address) =>
                {
                    bool registered = _ipcManager.Loci.RegisterActorOnFramework(address);
                    _lociRegistrations[ObjectKind.Player] = registered;
                    if (registered)
                    {
                        _ipcManager.Loci.SetActorManagerOnFramework(address, data);
                    }
                }, out _);
            }).GetAwaiter().GetResult();
        });
    }

    // When a pair enters or exits the ObjectTable frame-to-frame, we treat that has "visible" vs not
    // If they are mid data application then become no longer visible, we need to stop processing their data
    // It is very important to keep and honor the CTokens throughout the pipeline
    public void UpdateVisibility(bool visible)
    {
        if (!visible)
        {
            if (_isPairValid)
            {
                _isPairValid = false;
                var cts = Interlocked.Exchange(ref _dataApplicationCancellationTokenSource, null);
                if (cts != null)
                {
                    try
                    {
                        // We need to wait for the cancelled tasks to resolve before disposing of the CT
                        // We really want to preserve a predictable state for a pair at all times
                        _ = cts.CancelAsync().ContinueWith((_) => cts.Dispose(), TaskScheduler.Default);
                    }
                    catch (ObjectDisposedException)
                    {
                        //
                    }
                }
            }

            return;
        }

        if (visible && !_isPairValid)
        {
            _isPairValid = true;

            // It is questionable how much of this is needed vs letter Glam/Pen handle it with cached player state
            // This at least validates any file issues, but realistically this should be reviewed in depth
            if (HasCachedData)
            {
                Logger.LogDebug("Pair {pair} has cached data {data}", Pair.PairUIDName, HasCachedData);
                // this is OK because it is not a SameThreadMessage
                Mediator.Publish(new ReapplyCachedPairDataMessage(Pair, Force: true));
            }
        }
    }

    public async Task<Dictionary<ObjectKind, List<FileReplacementData>>?> TryGetMissingFileReplacementData()
    {
        var cachedData = GetCopyOfPairCharacterData();
        if (cachedData == null)
        {
            return null;
        }

        Dictionary<ObjectKind, List<FileReplacementData>> returnData = [];

        HashSet<string> hashes = cachedData.FileReplacements.Values
            .SelectMany(f => f)
            .Where(f => string.IsNullOrWhiteSpace(f.FileSwapPath) && !string.IsNullOrWhiteSpace(f.Hash))
            .Select(f => f.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (hashes.Count == 0)
        {
            return returnData;
        }

        var fileCachesByHash = await _fileDbManager.GetFirstFileCacheEntitiesByHashes(hashes, CancellationToken.None).ConfigureAwait(false);

        foreach (var fileReplacements in cachedData.FileReplacements)
        {
            List<FileReplacementData>? missingFiles = null;

            foreach (var fileReplacement in fileReplacements.Value)
            {
                if (!string.IsNullOrWhiteSpace(fileReplacement.FileSwapPath) || string.IsNullOrWhiteSpace(fileReplacement.Hash))
                {
                    continue;
                }

                if (fileCachesByHash.TryGetValue(fileReplacement.Hash, out var fileCache) && fileCache != null)
                {
                    continue;
                }

                missingFiles ??= [];
                missingFiles.Add(fileReplacement);
            }

            if (missingFiles != null)
            {
                returnData[fileReplacements.Key] = missingFiles;
            }
        }

        return returnData;
    }

    public bool TryStartDataApplication(Guid appId, bool overwriteRunningApplication = false)
    {
        lock (_dataApplicationRunningLock)
        {
            if (_runningApplicationId != null && !overwriteRunningApplication)
            {
                Logger.LogTrace("Tried to start data application but cannot! Running: {id} Called: {cid} Overwrite: {overwrite}",
                    _runningApplicationId, appId, overwriteRunningApplication);

                return false;
            }

            Logger.LogTrace("Start for data application Id: {id} Overwrite: {overwrite}", appId, overwriteRunningApplication);
            _runningApplicationId = appId;

            return true;
        }
    }

    public void EndDataApplcation(Guid appId)
    {
        lock (_dataApplicationRunningLock)
        {
            if (_runningApplicationId == appId)
            {
                Logger.LogTrace("End for data application {id}", appId);
                _runningApplicationId = null;
            }
            else
            {
                Logger.LogTrace("End for data application called for a different application id! Running: {id} Called: {cid}", _runningApplicationId, appId);
            }
        }
    }

    public Dictionary<PlayerChanges, bool> GetPairPlayerChanges()
    {
        var cachedData = _cachedData;

        return new Dictionary<PlayerChanges, bool>
        {
            [PlayerChanges.ModFiles] = cachedData?.FileReplacements.GetValueOrDefault(ObjectKind.Player)?.Count > 0,
            [PlayerChanges.Glamourer] = !string.IsNullOrWhiteSpace(cachedData?.GlamourerData.GetValueOrDefault(ObjectKind.Player)),
            [PlayerChanges.Customize] = !string.IsNullOrWhiteSpace(cachedData?.CustomizePlusData.GetValueOrDefault(ObjectKind.Player)),
            [PlayerChanges.Loci] = !string.IsNullOrWhiteSpace(cachedData?.LociData.GetValueOrDefault(ObjectKind.Player)),
            [PlayerChanges.Heels] = !string.IsNullOrWhiteSpace(cachedData?.HeelsData),
            [PlayerChanges.Honorific] = !string.IsNullOrWhiteSpace(cachedData?.HonorificData),
            [PlayerChanges.Moodles] = !string.IsNullOrWhiteSpace(cachedData?.MoodlesData),
            [PlayerChanges.PetNames] = !string.IsNullOrWhiteSpace(cachedData?.PetNamesData)
        };
    }

    internal void SetUploading(bool isUploading = true)
    {
        Logger.LogTrace("Setting {this} uploading {uploading}", this, isUploading);
        if (_charaHandler != null)
        {
            Mediator.Publish(new PlayerUploadingMessage(_charaHandler, isUploading));
        }
    }

    public CharacterData? GetCopyOfPairCharacterData()
    {
        lock (_cachedDataLock)
        {
            return _cachedData?.DeepClone();
        }
    }

    public async Task HandleOptionalPluginDataAsync(AddonPlugin plugin, CharacterData charaData)
    {
        if (_isVanillaEnforced)
        {
            return;
        }

        if (_currentVersion < 0)
        {
            Logger.LogDebug("Called to apply addon data for {pair} but pair hasn't finished full data application yet!", Pair.PairUIDName);

            return;
        }
        lock (_cachedDataLock)
        {
            if (_cachedData == null)
            {
                Logger.LogDebug("Called to apply addon data but cached data is null.");
                _cachedData = new();
            }
        }

        bool isCharacterInvalid = _charaHandler == null || !Pair.IsVisible;

        string honorificData = !string.IsNullOrEmpty(charaData.HonorificData) ? Encoding.UTF8.GetString(Convert.FromBase64String(charaData.HonorificData)) : string.Empty;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            try
            {
                if (!_dalamudUtil.TryGetPlayerAddressAndIndexOnFrameworkByContentId(Pair.ContendId, out var address, out var index))
                {
                    return;
                }

                switch (plugin)
                {
                    case AddonPlugin.Honorific:
                        if (charaData.HonorificData == null || string.Equals(_cachedData!.HonorificData, charaData.HonorificData, StringComparison.Ordinal))
                        {
                            return;
                        }
                        UpdateCachedAddonData(plugin, charaData);
                        if (isCharacterInvalid)
                        {
                            return;
                        }
                        _ipcManager.Honorific.SetTitleOnFramework(index, honorificData);

                        return;

                    case AddonPlugin.Heels:
                        if (string.Equals(_cachedData!.HeelsData, charaData.HeelsData, StringComparison.Ordinal))
                        {
                            return;
                        }
                        UpdateCachedAddonData(plugin, charaData);
                        if (isCharacterInvalid)
                        {
                            return;
                        }
                        _ipcManager.Heels.SetOffsetForPlayerOnFramework(index, charaData.HeelsData);

                        return;

                    case AddonPlugin.Moodles:
                        if (string.Equals(_cachedData!.MoodlesData, charaData.MoodlesData, StringComparison.Ordinal))
                        {
                            return;
                        }
                        UpdateCachedAddonData(plugin, charaData);
                        if (isCharacterInvalid)
                        {
                            return;
                        }
                        _ipcManager.Moodles.SetStatusOnFramework(address, charaData.MoodlesData);

                        return;

                    case AddonPlugin.PetNames:
                        if (string.Equals(_cachedData!.PetNamesData, charaData.PetNamesData, StringComparison.Ordinal))
                        {
                            return;
                        }
                        UpdateCachedAddonData(plugin, charaData);
                        if (isCharacterInvalid)
                        {
                            return;
                        }
                        _ipcManager.PetNames.SetPlayerDataOnFramework(index, charaData.PetNamesData);

                        return;

                    case AddonPlugin.Loci:
                        UpdateCachedAddonData(plugin, charaData);
                        if (isCharacterInvalid)
                        {
                            return;
                        }
                        try
                        {
                            if (!_lociRegistrations.GetValueOrDefault(ObjectKind.Player, false))
                            {
                                _lociRegistrations[ObjectKind.Player] = _ipcManager.Loci.RegisterActorOnFramework(address);
                            }
                            var lociDataToApply = charaData.LociData.GetValueOrDefault(ObjectKind.Player, string.Empty);
                            _ipcManager.Loci.SetActorManagerOnFramework(address, lociDataToApply);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Failed to set Loci data for {uid}", Pair.UserData.UID);
                        }

                        return;

                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to apply Addon data for {pair}", Pair.PairUIDName);
            }
        }).ConfigureAwait(false);
    }

    public async Task ApplyCharacterDataAsync(Guid applicationBase, CharacterData characterData, long sequence, CancellationToken topLevelToken, bool forceApplyCustomization = false)
    {
        if (characterData == null)
        {
            Logger.LogWarning("[BASE-{appBase}] Tried to apply character data for {pair} but character data was null!", applicationBase, Pair.PairUIDName);

            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, "Pair was in an invalid state during cached data application");
        }
        if (sequence < _currentVersion)
        {
            Logger.LogDebug("[BASE-{appBase}] Skipping outdated character data sequence {sequence}; current sequence is {current}",
                applicationBase, sequence, _currentVersion);

            return;
        }

        _pendingData = characterData.DeepClone(); // this is used as a contingency if pair visibility changes during data application for only one side and is later treated as cached
        _pendingVersion = sequence;

        _dataApplicationCancellationTokenSource = _dataApplicationCancellationTokenSource.CancelRecreate();
        var dataApplicationToken = _dataApplicationCancellationTokenSource.Token;
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(dataApplicationToken, topLevelToken);
        var linkedToken = linkedCts.Token;

        DataApplication dataApplication = new()
        {
            ApplicationBase = applicationBase,
            CharacterData = characterData,
            Sequence = sequence,
            ForceApplyCustomization = forceApplyCustomization
        };

        await ValidatePairDataApplicationReadiness(dataApplication, linkedToken).ConfigureAwait(false);
        var moddedPaths = await DownloadAndVerifyCharacterModFilesAsync(dataApplication, linkedToken).ConfigureAwait(false);
        if (moddedPaths == null)
        {
            return;
        }
        await ValidateCharacterModFilesAsync(dataApplication, moddedPaths, linkedToken).ConfigureAwait(false);
        await ApplyDataApplicationAsync(dataApplication, moddedPaths, linkedToken).ConfigureAwait(false);
    }

    public async Task ApplyCachedCharacterDataASync(Guid applicationBase, CancellationToken topLevelToken, bool forceApplyCustomization = false)
    {
        var charaData = GetCopyOfPairCharacterData() ?? _pendingData;
        if (charaData == null)
        {
            Logger.LogWarning("[BASE-{appId}] Called to apply cached data for {pair} but no cached or pending data exists", applicationBase, Pair.PairUIDName);

            return; // probably don't just return, throw or something; to think about later
        }

        await ApplyCharacterDataAsync(applicationBase, charaData, _currentVersion, topLevelToken, forceApplyCustomization).ConfigureAwait(false);
    }

    private void SetPairCharacterData(CharacterData? data)
    {
        lock (_cachedDataLock)
        {
            _cachedData = data?.DeepClone();
            _pendingData = null;
        }
    }

    private Task ValidatePairDataApplicationReadiness(DataApplication dataApplication, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var applicationBase = dataApplication.ApplicationBase;
        var characterData = dataApplication.CharacterData;
        var forceApplyCustomization = dataApplication.ForceApplyCustomization;

        Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Starting,
            Pair.PlayerName, Pair.UserData, nameof(ValidatePairDataApplicationReadiness), EventSeverity.Informational,
            $"Preparing to start data application for {Pair.PairUIDName}")));

        var cachedData = GetCopyOfPairCharacterData();
        characterData = RemoveNotSyncedFiles(characterData)!;

        // Check if we are in combat or performing and need to defer pair loading
        if ((_configService.Current.AutoPauseDataApplicationWhenPerforming && _dalamudUtil.IsPerforming) || _dalamudUtil.IsInCombat)
        {
            Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairDataApplicationHandler), EventSeverity.Warning,
                "Cannot apply character data: you are in combat or performing music, deferring application")));

            Logger.LogDebug("[BASE-{appBase}] Received data but player is in combat or performing", applicationBase);
            SetUploading(isUploading: false);

            throw new PairDataApplicationException(PairDataExceptionReason.CombatOrPerformance,
                "Character data application deferred because the player is in combat or performing.");
        }

        // Check if the pair has become an invalid state and we need to defer loading
        if (_charaHandler == null || !Pair.IsVisible)
        {
            Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairDataApplicationHandler), EventSeverity.Warning,
                "Cannot apply character data: Receiving Player is in an invalid state, deferring application")));
            Logger.LogDebug("[BASE-{appBase}] Received data but player was in invalid state, charaHandlerIsNull: {charaIsNull}, playerPointerIsNull: {ptrIsNull}",
                applicationBase, _charaHandler == null, !Pair.IsVisible);

            var hasDiffMods = characterData.CheckUpdatedData(applicationBase, cachedData, Logger,
                Pair.CachedPlayer!, forceApplyCustomization, forceApplyMods: false)
                .Any(p => p.Value.Contains(PlayerChanges.ModManip) || p.Value.Contains(PlayerChanges.ModFiles));

            _forceApplyMods = hasDiffMods || _forceApplyMods || (!Pair.IsVisible && !HasCachedData);

            Logger.LogDebug("[BASE-{appBase}] Setting data: {hash}, forceApplyMods: {force}", applicationBase, characterData.DataHash.Value, _forceApplyMods);

            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, "Character data application deferred due to invalid pair state.");
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
            Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Completed, Pair.PlayerName, Pair.UserData, nameof(ValidatePairDataApplicationReadiness), EventSeverity.Informational,
                $"Pair {Pair.PairUIDName} is being filtered.")));

            return Task.CompletedTask;
        }

        Logger.LogDebug("[BASE-{appbase}] Applying data for {player}, forceApplyCustomization: {forced}, forceApplyMods: {forceMods}",
            applicationBase, this, forceApplyCustomization, _forceApplyMods);
        Logger.LogDebug("[BASE-{appbase}] Hash for data is {newHash}, current cache hash is {oldHash}",
            applicationBase, characterData.DataHash.Value, cachedData?.DataHash.Value ?? "NODATA");

        // Check if the pairs data is what we already expect, if we're not forcing it, just stop here
        if (string.Equals(characterData.DataHash.Value, cachedData?.DataHash.Value ?? string.Empty, StringComparison.Ordinal) && !forceApplyCustomization)
        {
            _currentVersion = _pendingVersion;
            Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Completed, Pair.PlayerName, Pair.UserData, nameof(ValidatePairDataApplicationReadiness), EventSeverity.Informational,
                $"Pair {Pair.PairUIDName} already has this data applied.")));

            return Task.CompletedTask;
        }

        // More safety/sanity checks
        if (_dalamudUtil.IsInCutscene || _dalamudUtil.IsOccupiedInCutSceneEvent || _dalamudUtil.IsInGpose || !_ipcManager.Penumbra.APIAvailable || !_ipcManager.Glamourer.APIAvailable)
        {
            Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairDataApplicationHandler), EventSeverity.Warning,
                "Cannot apply character data: you are in GPose, a Cutscene or Penumbra/Glamourer is not available")));
            Logger.LogInformation("[BASE-{appbase}] Application of data for {player} while in cutscene/gpose or Penumbra/Glamourer unavailable, returning",
                applicationBase, this);

            throw new PairDataApplicationException(PairDataExceptionReason.GposeOrCutscene, "Character data application deferred because the player is in a cutscene or gposing.");
        }

        Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairDataApplicationHandler), EventSeverity.Informational, "Applying Character Data")));

        Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Processing, Pair.PlayerName, Pair.UserData, nameof(ValidatePairDataApplicationReadiness), EventSeverity.Informational,
                $"Processing data for {Pair.PairUIDName}.")));

        _forceApplyMods |= forceApplyCustomization;

        // This is a huge check worth reviewing, this builds tha actual "changes" to apply. This dictionary gets passed through 4 method calls before doing anything
        dataApplication.CharacterDataToUpdate = characterData.CheckUpdatedData(applicationBase, cachedData ?? new(), Logger, Pair.CachedPlayer!, forceApplyCustomization, _forceApplyMods);
        var charaDataToUpdate = dataApplication.CharacterDataToUpdate;

        if (_charaHandler != null && _forceApplyMods)
        {
            _forceApplyMods = false;
        }

        // Redraw if the pair changed jobs (as noted by their GameObjectHandler)
        if (charaDataToUpdate.TryGetValue(ObjectKind.Player, out var player) && Interlocked.Exchange(ref _redrawOnNextApplication, 0) == 1)
        {
            player.Add(PlayerChanges.ForcedRedraw);
        }

        // Check for any pair data for optional plugins that we don't have installed/enabled
        if (charaDataToUpdate.TryGetValue(ObjectKind.Player, out var playerChanges))
        {
            _pluginWarningNotificationManager.NotifyForMissingPlugins(Pair.UserData, PlayerName!, playerChanges);
        }

        Logger.LogDebug("[BASE-{appbase}] Downloading and applying character for {name}", applicationBase, this);

        // Move on to downloading
        //return DownloadAndCheckCharacterModFilesAsync(dataApplication, cancellationToken);
        return Task.CompletedTask;
    }

    // This step figures out what mod files we need for the pair and downloads them
    private async Task<Dictionary<(string GamePath, string? Hash), string>?> DownloadAndVerifyCharacterModFilesAsync(DataApplication dataApplication, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var applicationBase = dataApplication.ApplicationBase;
        var updatedData = dataApplication.CharacterDataToUpdate;
        var charaData = dataApplication.CharacterData;

        if (!updatedData.Any())
        {
            Logger.LogDebug("[BASE-{appBase}] Nothing to update for {obj}", applicationBase, this);
            SetPairCharacterData(charaData);
            _currentVersion = _pendingVersion;
            LastDataApplicationTime = DateTimeOffset.UtcNow;
            Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Completed,
                Pair.PlayerName, Pair.UserData, nameof(DownloadAndVerifyCharacterModFilesAsync), EventSeverity.Informational,
                $"Pair {Pair.PairUIDName} has no updates to apply.")));

            return null;
        }

        Stopwatch stopwatch;
        Dictionary<(string GamePath, string? Hash), string> moddedPaths = [];
        HashSet<string> locallyPresentFiles;
        List<FileReplacementData> toDownloadReplacements;
        int numberOfFilesToDownload = -1;
        var compressedAlternateUsage = ComputeCompressedAlternateUsage(); // check if we are using comp alts or not for this pair

        //var updateModdedPaths = updatedData.Values.Any(v => v.Any(p => p == PlayerChanges.ModFiles));
        if (dataApplication.UpdateModdedPaths)
        {
            int maxAttempts = 10;
            int attempts = 0;
            ActiveCompressionRedirects.Clear();
            // This part does a ton of lifting, but basically checks for mod files we do not have yet that we will need for this pair
            (toDownloadReplacements, locallyPresentFiles, moddedPaths) = await TryCalculateModdedDictionary(applicationBase, charaData,
                compressedAlternateUsage, ActiveCompressionRedirects, cancellationToken).ConfigureAwait(false);

            Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Downloading, Pair.PlayerName,
                Pair.UserData, nameof(DownloadAndVerifyCharacterModFilesAsync), EventSeverity.Informational,
                $"Starting checks and file downloads for {Pair.PairUIDName}")));

            stopwatch = Stopwatch.StartNew();
            // We go through the download loop up to 10 times in case any of the downloads fail. This is honestly hacky and should be better handled based on failure reason.
            while (toDownloadReplacements.Count > 0 && attempts < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                attempts++;

                if (_pairDownloadTask != null && !_pairDownloadTask.IsCompleted)
                {
                    Logger.LogDebug("[BASE-{appBase}] Finishing prior running download task for player {pair}", applicationBase, Pair.PairUIDName);
                    await _pairDownloadTask.ConfigureAwait(false);
                }

                Logger.LogDebug("[BASE-{appBase}] Downloading/checking for missing files for player {pair}", applicationBase, Pair.PairUIDName);

                Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Downloading, PlayerName,
                    Pair.UserData, nameof(DownloadAndVerifyCharacterModFilesAsync), EventSeverity.Informational,
                    $"Downloading {toDownloadReplacements.Count} files for {Pair.PairUIDName}")));

                Dictionary<string, string> compressionSubstitutions = new Dictionary<string, string>();
                // This gets a list of file download dtos from the file server that we need for this pair, not the actual files.
                // This contains meta data and download links for each file.
                var toDownloadFiles = await _downloadManager.InitiateDownloadList(_charaHandler!.Name, toDownloadReplacements
                    .Select(f => f.Hash)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(), compressedAlternateUsage, compressionSubstitutions, locallyPresentFiles, 0, cancellationToken).ConfigureAwait(false);
                if (numberOfFilesToDownload < 0)
                {
                    numberOfFilesToDownload = toDownloadFiles.Count;
                }

                // basically check the meta data for each of the files before we even download them to see if it'll exceed thresholds
                if (!_playerPerformanceService.ComputeAndAutoPauseOnVRAMUsageThresholds(Pair, charaData, toDownloadFiles))
                {
                    _downloadManager.ClearDownload();

                    Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Cancelled,
                        Pair.PlayerName, Pair.UserData, nameof(DownloadAndVerifyCharacterModFilesAsync), EventSeverity.Warning,
                        $"Pair {Pair.PairUIDName} exceeded performance thresholds.")));

                    throw new PairDataApplicationException(PairDataExceptionReason.ThresholdViolation, "Pair vioalates the VRAM/triangle threshold.");
                }

                Mediator.Publish(new EventMessage(new Event(PlayerName, Pair.UserData, nameof(PairDataApplicationHandler), EventSeverity.Informational,
                    $"Starting download for {toDownloadReplacements.Count} files")));

                // start download for needed files
                _pairDownloadTask = Task.Run(async () => await _downloadManager.DownloadFiles(new DownloadBatchInfo(_charaHandler!.Name, "Player", _charaHandler),
                    toDownloadReplacements, compressionSubstitutions, cancellationToken).ConfigureAwait(false));

                await _pairDownloadTask.ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // check again if we have files we still need to download, if so, this loop begins again
                (toDownloadReplacements, locallyPresentFiles, moddedPaths) = await TryCalculateModdedDictionary(applicationBase, charaData,
                    compressedAlternateUsage, ActiveCompressionRedirects, cancellationToken).ConfigureAwait(false);

                if (toDownloadReplacements.TrueForAll(c => _downloadManager.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, c.Hash, StringComparison.Ordinal))))
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                (toDownloadReplacements, locallyPresentFiles, moddedPaths) = await TryCalculateModdedDictionary(applicationBase, charaData,
                    compressedAlternateUsage, ActiveCompressionRedirects, cancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();

            // we do a bunch of rechecks and file validation for stuff to make sure nothing wound up missing

            var hashes = moddedPaths.Keys.Select(key => key.Hash).Where(hash => !string.IsNullOrWhiteSpace(hash)).Select(hash => hash!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rescan = await _fileDbManager.GetFirstFileCacheEntitiesByHashes(hashes, cancellationToken).ConfigureAwait(false);
            var foundHashes = rescan.Where(entry => entry.Value != null).Select(entry => entry.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingHashes = hashes.Except(foundHashes, StringComparer.OrdinalIgnoreCase).ToArray();
            var missingMappedPaths = moddedPaths.Where(entry => entry.Key.Hash != null && !File.Exists(entry.Value)).ToArray();
            if (missingHashes.Length > 0 || missingMappedPaths.Length > 0)
            {
                Logger.LogWarning("[BASE-{appBase}] Missing hashes: {hashes}; missing mapped files: {paths}", 
                    applicationBase, string.Join(", ", missingHashes), string.Join(", ", missingMappedPaths.Select(entry => $"{entry.Key.GamePath} [{entry.Key.Hash}] => {entry.Value}")));
            }

            if (toDownloadReplacements.Count > 0)
            {
                MissingFiles.Clear();

                foreach (var missing in toDownloadReplacements)
                {
                    var sourceHashes = ActiveCompressionRedirects
                        .Where(redirect => string.Equals(redirect.Value, missing.Hash, StringComparison.OrdinalIgnoreCase))
                        .Select(redirect => redirect.Key)
                        .Append(missing.Hash)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var dtoEntries = charaData.FileReplacements
                        .SelectMany(kind => kind.Value
                        .Where(replacement => sourceHashes.Contains(replacement.Hash))
                        .Select(replacement => $"{kind.Key} [{replacement.Hash}]: {string.Join(", ", replacement.GamePaths)}"))
                        .ToArray();

                    MissingFiles.TryAdd(missing.Hash, string.Join(", ", missing.GamePaths));

                    Logger.LogWarning(
                        "[BASE-{appBase}] Missing files for {pair}: Requested Hash: {hash}, Requested Paths: {paths}, DTO entries: {dtoEntries}",
                        applicationBase, Pair.PairUIDName, missing.Hash, string.Join(", ", missing.GamePaths), string.Join(" | ", dtoEntries));
                }

                Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Failed, Pair.PlayerName, Pair.UserData,
                    nameof(DownloadAndVerifyCharacterModFilesAsync), EventSeverity.Error, $"At least one file failed to download for {Pair.PairUIDName}")));

                //Logger.LogError("Still missing files for {pair}: {missing}", Pair.PairUIDName, string.Join(", ", toDownloadReplacements.Select(file => file.Hash)));
            }

            if (numberOfFilesToDownload > 0) // we may not have needed to download anything, so don't report it
            {
                Logger.LogDebug("[BASE-{appBase}] Downloaded {count} file(s) in {attempts} total attempt(s) for {player}:{uid} in {elapsedMs}ms",
                applicationBase, numberOfFilesToDownload, attempts, PlayerName, Pair.UserData.UID, stopwatch.ElapsedMilliseconds);
            }

            // VRAM/Tris threshold checks
            if (!await _playerPerformanceService.CheckBothThresholds(Pair, charaData).ConfigureAwait(false))
            {
                Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Cancelled, Pair.PlayerName,
                    Pair.UserData, nameof(DownloadAndVerifyCharacterModFilesAsync), EventSeverity.Warning,
                    $"Pair {Pair.PairUIDName} exceeded performance thresholds.")));

                throw new PairDataApplicationException(PairDataExceptionReason.ThresholdViolation, "Pair vioalates the VRAM/triangle threshold.");
            }

            // height threshold check
            if (!_playerPerformanceService.CheckForRspHeight(Pair, charaData))
            {
                Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Cancelled, Pair.PlayerName,
                    Pair.UserData, nameof(DownloadAndVerifyCharacterModFilesAsync), EventSeverity.Warning,
                    $"Pair {Pair.PairUIDName} exceeded height threshold.")));

                throw new PairDataApplicationException(PairDataExceptionReason.ThresholdViolation, "Pair vioalates the height threshold.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Downloading, Pair.PlayerName,
            Pair.UserData, nameof(DownloadAndVerifyCharacterModFilesAsync), EventSeverity.Informational,
            $"Finished file checks and downloads for {Pair.PairUIDName}")));

        //await ApplyDataApplicationAsync(dataApplication, moddedPaths, cancellationToken).ConfigureAwait(false);
        return moddedPaths;
    }

    private static readonly string[] ExpansionNames = [
        "A Realm Reborn",
        "Heavensward",
        "Stormblood",
        "Shadowbringers",
        "Endwalker",
        "Dawntrail",
        "Evercold",
    ];

    private async Task ValidateCharacterModFilesAsync(DataApplication dataApplication, Dictionary<(string GamePath, string? Hash), string> moddedPaths, CancellationToken cancellationToken)
    {
        var applicationBase = dataApplication.ApplicationBase;
        var charaData = dataApplication.CharacterData;

        var startTime = Stopwatch.GetTimestamp();

        // Proactively check the incoming files for specific known sources of crashes
        if (_configService.Current.EnableValidationChecks)
        {
            Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Applying, Pair.PlayerName,
                Pair.UserData, nameof(ApplyDataApplicationAsync), EventSeverity.Informational,
                $"Performing file validation checks for {Pair.PairUIDName}")));

            var replacementPaths = charaData.FileReplacements.Values
                .SelectMany(value => value)
                .SelectMany(replacement => replacement.GamePaths)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var file in moddedPaths.ToArray()) // .ToArray() so we can modify the dictionary as we loop through it
            {
                if (file.Key.Hash == null)
                {
                    // Validate vanilla redirection
                    int expansionId = ValidationUtils.ParseExpansionId(file.Value);
                    if (!_dalamudUtil.IsExpansionInstalled(expansionId))
                    {
                        _filesThatHaveBeenVerifiedBad.TryAdd(file.Value, 0);
                        string expansionName = expansionId < ExpansionNames.Length ? ExpansionNames[expansionId] : expansionId.ToString();
                        Logger.LogWarning("{uid} ({name}): File {dest} to be used as {source} is from the {expansionName} expansion which is not installed.",
                            Pair.UserData.UID, PlayerName, file.Value, file.Key.GamePath, expansionName);
                        moddedPaths.Remove(file.Key);
                    }
                }
                else
                {
                    // Validate modded file
                    var extension = Path.GetExtension(file.Key.GamePath);
                    if (_filesThatHaveBeenVerifiedBad.ContainsKey(file.Key.Hash))
                    {
                        Logger.LogWarning("{uid} ({name}): File {hash} to be used as {gamePath} looks like it could crash game and will be ignored.",
                            Pair.UserData.UID, PlayerName, file.Key.Hash, file.Key.GamePath);
                        moddedPaths.Remove(file.Key);

                        continue;
                    }
                    if (!FileExtensionsToValidate.Contains(extension, StringComparer.OrdinalIgnoreCase) || _filesThatHaveBeenVerifiedGood.ContainsKey(file.Key.Hash))
                    {
                        continue;
                    }

                    var filePath = file.Value;
                    if (filePath != null)
                    {
                        try
                        {
                            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                            var validationMessages = FileValidation.ValidateFile(_dataManager.Excel, _dalamudUtil.InstalledExpansions, bytes, extension, path => path.Contains('/')
                                && (_dataManager.FileExists(path) || replacementPaths.Contains(path))).ToArray();

                            if (validationMessages.Length > 0)
                            {
                                var messageString = string.Join(", ", validationMessages.Select(message => $"[{message.ID}]: {message.Title} ({message.Level})"));

                                if (validationMessages.Any(message => message.Level == MessageLevel.Crash))
                                {
                                    _filesThatHaveBeenVerifiedBad.TryAdd(file.Key.Hash, 0);
                                    Logger.LogWarning("{uid} ({name}): File {hash} to be used as {gamePath} looks like it could crash game and will be ignored. \n  {description}",
                                        Pair.UserData.UID, PlayerName, file.Key.Hash, file.Key.GamePath, messageString);
                                    moddedPaths.Remove(file.Key);

                                    continue;
                                }
                                else
                                {
                                    _filesThatHaveBeenVerifiedGood.TryAdd(file.Key.Hash, 0);
                                    Logger.LogDebug("{uid} ({name}): File {hash} to be used as {gamePath} looks like it might have some mistakes, but will still be used. \n  {description}",
                                        Pair.UserData.UID, PlayerName, file.Key.Hash, file.Key.GamePath, messageString);
                                }
                            }

                            _filesThatHaveBeenVerifiedGood.TryAdd(file.Key.Hash, 0);
                        }
                        catch (IOException ex)
                        {
                            Logger.LogWarning(ex, "Couldn't read downloaded file {filePath} for validation!", filePath);
                        }
                    }
                }
            }
        }

        var totalTime = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
        Logger.LogDebug("[BASE-{appBase}] Mod validation for {pair} took {totalTime:F3}ms", applicationBase, Pair.PairUIDName, totalTime);
    }

    private async Task ApplyDataApplicationAsync(DataApplication dataApplication, Dictionary<(string GamePath, string? Hash), string> moddedPaths, CancellationToken cancellationToken)
    {
        var applicationId = Guid.NewGuid();
        var applicationBase = dataApplication.ApplicationBase;
        var charaData = dataApplication.CharacterData;
        var updatedData = dataApplication.CharacterDataToUpdate;

        if (_penumbraCollection == null)
        {
            Logger.LogError("No penumbra collection exists for {pair}!", Pair.PlayerName);

            Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Failed, Pair.PlayerName,
                Pair.UserData, nameof(ApplyDataApplicationAsync), EventSeverity.Error,
                $"No Penumbra collection exists for {Pair.PairUIDName}")));

            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, "Pair does not have a valid Penumbra collection applied.");
        }

        try
        {
            // do height checking
            if (!_playerPerformanceService.CheckForRspHeight(Pair, charaData))
            {
                Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Cancelled, Pair.PlayerName,
                    Pair.UserData, nameof(ApplyDataApplicationAsync), EventSeverity.Warning,
                        $"Pair {Pair.PairUIDName} exceeded height threshold.")));

                throw new PairDataApplicationException(PairDataExceptionReason.ThresholdViolation, "Pair violates the height threshold.");
            }

            Logger.LogDebug("[BASE-{applicationId}] Starting application task for {this}: {appId}", applicationBase, this, applicationId);
            Logger.LogDebug("[{applicationId}] Waiting for initial draw for for {handler}", applicationId, _charaHandler);
            var drawResult = await _dalamudUtil.WaitWhileCharacterIsDrawing(Logger, _charaHandler!, applicationId, 30000, cancellationToken).ConfigureAwait(false);
            if (drawResult != DrawWaitResult.Ready)
            {
                throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState,
                    $"Character data application deferred because the player draw wait ended with {drawResult}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var moddedPathsData = moddedPaths.GroupBy(entry => entry.Key.GamePath, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group
                    .OrderByDescending(entry => entry.Key.Hash == null)
                    .ThenBy(entry => entry.Key.Hash, StringComparer.Ordinal)
                    .First().Value, StringComparer.Ordinal);

            if (dataApplication.UpdateModdedPaths)
            {
                Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Applying, Pair.PlayerName,
                    Pair.UserData, nameof(ApplyDataApplicationAsync), EventSeverity.Informational,
                    $"Applying Penumbra updates for {Pair.PairUIDName}")));
       

                var duplicatePaths = moddedPaths.GroupBy(entry => entry.Key.GamePath, StringComparer.Ordinal).Where(group => group.Count() > 1).ToList();
                foreach (var duplicate in duplicatePaths)
                {
                    Logger.LogWarning("[BASE-{applicationId}] {pair} Multiple replacements target {gamePath}: {replacements}",
                        applicationId, Pair.PairUIDName, duplicate.Key, string.Join(", ", duplicate.Select(entry => $"Hash: {entry.Key.Hash ?? "SWAP"}, Path:{entry.Value}")));
                }
            }

            // This runs against each object kind, player, pet, etc. and applies the changes
            bool penumbraApplied = false;
            foreach (var kind in updatedData)
            {
                Mediator.Publish(new EventMessage(new DataApplicationEvent(applicationBase, DataApplicationStatus.Applying, Pair.PlayerName, 
                    Pair.UserData, nameof(ApplyDataApplicationAsync), EventSeverity.Informational, $"Applying character data of type {kind.ToString()} for {Pair.PairUIDName}")));

                penumbraApplied = await ApplyCustomizationDataAsync(dataApplication, kind.Key, moddedPathsData, penumbraApplied, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (dataApplication.UpdateModdedPaths)
            {
                LastAppliedDataBytes = -1;
                foreach (var path in moddedPathsData.Values.Distinct(StringComparer.OrdinalIgnoreCase).Select(v => new FileInfo(v)).Where(p => p.Exists))
                {
                    if (LastAppliedDataBytes == -1) LastAppliedDataBytes = 0;

                    LastAppliedDataBytes += path.Length;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            _forceApplyMods = true;
            Logger.LogDebug(ex, "[{applicationId}] Player became invalid during application", applicationId);

            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState,
                "Character data application deferred because the player became invalid during application.");
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Any(e => e is NullReferenceException or ArgumentNullException or InvalidDataException))
        {
            _forceApplyMods = true;
            Logger.LogDebug("[{applicationId}] Cancelled, player turned null during application", applicationId);
            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState,
                "Character data application deferred because the player became invalid during application.");
        }
        catch (PairDataApplicationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[{applicationId}] Cancelled", applicationId);
            throw;
        }

        Logger.LogDebug("[{applicationId}] Application finished", applicationId);

        SetPairCharacterData(charaData);
        _currentVersion = _pendingVersion;
        LastDataApplicationTime = DateTimeOffset.UtcNow;
    }

    private async Task<bool> ApplyCustomizationDataAsync(DataApplication dataApplication, ObjectKind objectKind, Dictionary<string, string> moddedPathsData, bool penumbraApplied, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var applicationId = dataApplication.ApplicationBase;
        var charaData = dataApplication.CharacterData;
        var characterChanges = dataApplication.CharacterDataToUpdate[objectKind];

        if (_charaHandler == null)
        {
            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, "Character data application deferred because the player became unavailable");
        }

        string? handlerInfo = await WaitForCharacterToDrawAsync(applicationId, objectKind, cancellationToken).ConfigureAwait(false);
        if (handlerInfo == null)
        {
            return penumbraApplied;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orderedCharacterChanges = characterChanges.OrderBy(change => (int)change).ToArray();

        // calculate plugin data before calling Framework thread

        string? glamourerData = null;
        if (characterChanges.Contains(PlayerChanges.Glamourer))
        {
            charaData.GlamourerData.TryGetValue(objectKind, out glamourerData);
        }

        bool hasScale = false;
        string? decodedScale = null;
        if (characterChanges.Contains(PlayerChanges.Customize) && charaData.CustomizePlusData.TryGetValue(objectKind, out var encodedScale))
        {
            decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(encodedScale));
            hasScale = true;
        }

        string honorificData = string.Empty;
        if (characterChanges.Contains(PlayerChanges.Honorific) && !string.IsNullOrEmpty(charaData.HonorificData))
        {
            honorificData = Encoding.UTF8.GetString(Convert.FromBase64String(charaData.HonorificData));
        }

        string lociData = characterChanges.Contains(PlayerChanges.Loci) ? charaData.LociData.GetValueOrDefault(objectKind, string.Empty) : string.Empty;

        var timings = _frameworkTimings[objectKind];
        foreach (var change in timings.Keys)
        {
            timings[change] = 0d;
        }

        PlayerChanges? lastFinishedUpdate = null;

        // yes I know _frameworkTimings exists, it was added after this and I was lazy to clean it all up
        double penumbraAssignTempTime = 0;
        double penumbraSetTempTime = 0;
        double penumbraSetManipTime = 0;
        double customizeSetTime = 0;
        double customizeRevertTime = 0;
        double heelsTime = 0;
        double honorificTime = 0;
        double glamourerTime = 0;
        double moodlesTime = 0;
        double lociRegisterTime = 0;
        double lociSetTime = 0;
        double petNamesTime = 0;
        double redrawTime = 0;

        // 3 groups within one framework call: Glamourer -> Everything else -> Redraw
        var changeGroup = new[]
        {
            orderedCharacterChanges.Where(change => change == PlayerChanges.Glamourer).ToArray(),
            orderedCharacterChanges.Where(change => change != PlayerChanges.Glamourer && change != PlayerChanges.ForcedRedraw).ToArray(),
            orderedCharacterChanges.Where(change => change == PlayerChanges.ForcedRedraw).Take(1).ToArray(),
        };

        var penumbraCollection = _penumbraCollection!.Value;
        var updateModdedPaths = dataApplication.UpdateModdedPaths;
        var updateManip = dataApplication.UpdateManip;

        Logger.LogDebug("[{applicationId}] Applying Customization Data for {handler}", applicationId, handlerInfo);

        Interlocked.Increment(ref _pendingDataIpcCounts);
        Logger.LogTrace("Number of pending data applications via IPCs: {count}", Volatile.Read(ref _pendingDataIpcCounts));
        try
        {
            await _dataApplicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                bool nonPlayerObjectAndNull = false;
                string? failureReason = null;
                PlayerChanges? failedChange = null;

                bool playerIsStillValid = await _dalamudUtil.RunOnFrameworkThread(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (var changes in changeGroup)
                    {
                        if (changes.Length == 0)
                        {
                            continue;
                        }

                        bool groupValid = _dalamudUtil.TryActOnFramework(Pair.ContendId,
                            (player, playerAddress) =>
                            {
                                nint characterAddress = _dalamudUtil.GetObjectAddressByObjectKindAndAddress(objectKind, playerAddress);
                                var chara = _dalamudUtil.GetCharacterFromObjectTableByAddressWhenAlreadyOnFrameworkThread(characterAddress);
                                if (chara == null)
                                {
                                    if (objectKind != ObjectKind.Player)
                                    {
                                        nonPlayerObjectAndNull = true;
                                        return;
                                    }

                                    throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, "Character data application deferred because the player became unavailable");
                                }

                                var playerIndex = player.ObjectIndex;
                                var characterIndex = chara.ObjectIndex;

                                if (!penumbraApplied && (updateModdedPaths || updateManip))
                                {
                                    if (updateModdedPaths)
                                    {
                                        IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Penumbra.AssignTemporaryCollection(Logger, penumbraCollection, playerIndex), out penumbraAssignTempTime);
                                        IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Penumbra.SetTemporaryModsOnFramework(Logger, applicationId, penumbraCollection, 
                                            moddedPathsData, Pair.UserData.UID), out penumbraSetTempTime);
                                        _frameworkTimings[objectKind][PlayerChanges.ModFiles] = penumbraAssignTempTime + penumbraSetTempTime;
                                    }
                                    if (updateManip)
                                    {
                                        IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Penumbra.SetManipulationDataOnFramework(Logger, applicationId, penumbraCollection, 
                                            charaData.ManipulationData, Pair.UserData.UID), out penumbraSetManipTime);
                                        _frameworkTimings[objectKind][PlayerChanges.ModManip] = penumbraSetManipTime;
                                    }
                                    penumbraApplied = true;
                                }

                                foreach (var change in changes)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    Logger.LogTrace("[{applicationId}] {change} requested for {kind}", applicationId, change.ToString(), objectKind.ToString());
                                    switch (change)
                                    {
                                        case PlayerChanges.Glamourer:
                                            if (glamourerData != null)
                                            {
                                                IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Glamourer.ApplyAllOnFramework(characterIndex, glamourerData, applicationId), out glamourerTime);
                                            }
                                            _frameworkTimings[objectKind][change] = glamourerTime;
                                            break;

                                        case PlayerChanges.Customize:
                                            if (hasScale)
                                            {
                                                _customizeIds[objectKind] = IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.CustomizePlus.SetBodyScaleOnFramework(characterIndex, decodedScale!), out customizeSetTime);
                                            }
                                            else if (_customizeIds.TryGetValue(objectKind, out var customizeId))
                                            {
                                                IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.CustomizePlus.RevertByIdFromFramework(customizeId), out customizeRevertTime);
                                                _customizeIds.Remove(objectKind);
                                            }
                                            _frameworkTimings[objectKind][change] = customizeRevertTime + customizeSetTime;
                                            break;

                                        case PlayerChanges.Heels:
                                            IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Heels.SetOffsetForPlayerOnFramework(playerIndex, charaData.HeelsData), out heelsTime);
                                            _frameworkTimings[objectKind][change] = heelsTime;
                                            break;

                                        case PlayerChanges.Honorific:
                                            IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Honorific.SetTitleOnFramework(playerIndex, honorificData), out honorificTime);
                                            _frameworkTimings[objectKind][change] = honorificTime;
                                            break;

                                        case PlayerChanges.Moodles:
                                            IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Moodles.SetStatusOnFramework(playerAddress, charaData.MoodlesData), out moodlesTime);
                                            _frameworkTimings[objectKind][change] = moodlesTime;
                                            break;

                                        case PlayerChanges.Loci:
                                            if (!_lociRegistrations.GetValueOrDefault(objectKind, false))
                                            {
                                                _lociRegistrations[objectKind] = IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Loci.RegisterActorOnFramework(characterAddress), out lociRegisterTime);
                                            }
                                            IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Loci.SetActorManagerOnFramework(characterAddress, lociData), out lociSetTime);
                                            _frameworkTimings[objectKind][change] = lociRegisterTime + lociSetTime;
                                            break;

                                        case PlayerChanges.PetNames:
                                            IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.PetNames.SetPlayerDataOnFramework(playerIndex, charaData.PetNamesData), out petNamesTime);
                                            _frameworkTimings[objectKind][change] = petNamesTime;
                                            break;

                                        case PlayerChanges.ForcedRedraw:
                                            if (objectKind == ObjectKind.Player)
                                            {
                                                _charaHandler.IsRedrawingViaIpcRequest = true;
                                            }
                                            var redrawRequested = IIpcCaller.GetIPCTimeToCompletion(() => _ipcManager.Penumbra.RedrawOnFramework(Logger, applicationId, characterAddress), out redrawTime);
                                            if (!redrawRequested && objectKind == ObjectKind.Player)
                                            {
                                                _charaHandler.IsRedrawingViaIpcRequest = false;

                                                throw new PairDataApplicationException(PairDataExceptionReason.IpcInvocationError, "Penumbra redraw request failed");
                                            }
                                            Pair.LastLoadedSoundSinceRedrawTime = null;
                                            _frameworkTimings[objectKind][change] = redrawTime;
                                            break;
                                    }

                                    lastFinishedUpdate = change;
                                }
                            },
                            out var actionFailureReason);

                        if (!groupValid)
                        {
                            failureReason = actionFailureReason;
                            failedChange = changes[0];

                            return false;
                        }
                        if (nonPlayerObjectAndNull)
                        {
                            return true;
                        }
                    }

                    return true;
                }).ConfigureAwait(false);

                if (!playerIsStillValid)
                {
                    Logger.LogWarning("[{applicationId}] Data application failed for {ident}/{objectKind}. Failed change: {failedChange} last completed change: {lastCompletedChange} reason: {failureReason}",
                        applicationId, Pair.HashedCID, objectKind, failedChange, lastFinishedUpdate, failureReason);

                    throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState,"Character data application deferred because the player changed or became unavailable.");
                }
                if (nonPlayerObjectAndNull)
                {
                    return penumbraApplied;
                }
                
                // we can't simply wait for the IsRedrawingViaIpcRequest to turn false because the character could become invalid
                if (objectKind == ObjectKind.Player && orderedCharacterChanges.Contains(PlayerChanges.ForcedRedraw))
                {
                    try
                    {
                        // so we need to wait for it properly
                        await WaitForCharacterToDrawAsync(applicationId, objectKind, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _charaHandler.IsRedrawingViaIpcRequest = false;
                    }
                }
            }
            finally
            {
                _dataApplicationGate.Release();
            }

            return penumbraApplied;
        }
        finally
        {
            Interlocked.Decrement(ref _pendingDataIpcCounts);
            Logger.LogTrace("Number of pending data applications via IPCs: {count}", Volatile.Read(ref _pendingDataIpcCounts));

            double totalIpcTime = customizeSetTime + customizeRevertTime + heelsTime + honorificTime +
                glamourerTime + moodlesTime + lociRegisterTime + lociSetTime + petNamesTime + redrawTime +
                penumbraAssignTempTime + penumbraSetManipTime + penumbraSetTempTime;

            double penumbraMods = penumbraAssignTempTime + penumbraSetManipTime + penumbraSetTempTime;
            double otherMods = customizeSetTime + customizeRevertTime + heelsTime + honorificTime +
                glamourerTime + moodlesTime + lociRegisterTime + lociSetTime + petNamesTime;

            if (totalIpcTime > 20d)
            {
                Logger.LogWarning("[{applicationId}] Total Data Application for {pair} took {time:F3}ms Penumbra manip: {manip:F3}ms other mods: {mods:F3}ms", 
                    applicationId, Pair.PairUIDName, totalIpcTime, penumbraMods, otherMods);
            }
            else
            {
                if (penumbraMods > 20d)
                {
                    Logger.LogWarning("[{applicationId}] Data Application for {pair} of Penumbra manip took {time:F3}ms", applicationId, Pair.PairUIDName, penumbraMods);
                }

                if (otherMods > 20d)
                {
                    Logger.LogWarning("[{applicationId}] Data Application for {pair} of mod data took {time:F3}ms", applicationId, Pair.PairUIDName, otherMods);
                }
            }

            Logger.LogTrace(
                "[{applicationId}] Framework IPC times for {player} {objectKind}: PenumbraAssignTemp {PenumbraAssignTemp:F3}ms PenumbraSetTemp {PenumbraSetTemp:F3}ms PenumbraSetManip {PenumbraSetManip:F3}ms CustomizeSet {CustomizeSetMs:F3}ms CustomizeRevert {CustomizeRevertMs:F3}ms Heels {HeelsMs:F3}ms Honorific {HonorificMs:F3}ms Glamourer {GlamourerMs:F3}ms Moodles {MoodlesMs:F3}ms LociRegister {LociRegisterMs:F3}ms LociSet {LociSetMs:F3}ms PetNames {PetNamesMs:F3}ms Redraw {RedrawMs:F3}ms Total framework time: {TotalMs:F3}ms",
                applicationId, Pair.PairUIDName, objectKind, penumbraAssignTempTime, penumbraSetTempTime, penumbraSetManipTime, customizeSetTime, customizeRevertTime, heelsTime, honorificTime,
                glamourerTime, moodlesTime, lociRegisterTime, lociSetTime, petNamesTime, redrawTime, totalIpcTime);
        }
    }

    private async Task<string?> WaitForCharacterToDrawAsync(Guid applicationId, ObjectKind objectKind, CancellationToken linkedToken)
    {
        linkedToken.ThrowIfCancellationRequested();

        if (_charaHandler == null)
        {
            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, "Character data application deferred because the player became unavailable");
        }

        var result = await _dalamudUtil.WaitWhileCharacterIsDrawing(Logger, Pair.ContendId, objectKind, applicationId, 30000, linkedToken).ConfigureAwait(false);
        if (result == DrawWaitResult.Missing && objectKind != ObjectKind.Player)
        {
            return null;
        }
        if (result != DrawWaitResult.Ready)
        {
            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, $"Draw wait for {objectKind} ended with {result}");
        }

        return $"{Pair.PairUIDName}/{objectKind}";
    }

    private async Task<(List<FileReplacementData> MissingFiles, HashSet<string> LocallyPresentFiles, Dictionary<(string GamePath, string? Hash), string> ModdedDictionary)> TryCalculateModdedDictionary(Guid applicationBase, CharacterData charaData, CompressedAlternateUsage compressedAlternateUsage, ConcurrentDictionary<string, string> compressionSubstitutions, CancellationToken token)
    {
        Stopwatch st = Stopwatch.StartNew();
        List<FileReplacementData> missingFiles = [];
        HashSet<string> locallyPresentFiles = [];
        Dictionary<(string GamePath, string? Hash), string> moddedDictionary = [];
        Dictionary<(string GamePath, string? Hash), string> outputDict = [];
        List<FileReplacementData> replacementList = [];
        List<FileReplacementData> fileSwapList = [];
        HashSet<string> sourceHashes = new(StringComparer.Ordinal);
        HashSet<string> nonTextureHashes = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> invalidPaths = new(StringComparer.OrdinalIgnoreCase);
        int invalidReplacementCount = 0;

        foreach (var replacements in charaData.FileReplacements.Values)
        {
            foreach (var replacement in replacements)
            {
                if (!string.IsNullOrWhiteSpace(replacement.FileSwapPath))
                {
                    fileSwapList.Add(replacement);

                    continue;
                }

                if (string.IsNullOrWhiteSpace(replacement.Hash))
                {
                    invalidReplacementCount++;

                    foreach (var gamePath in replacement.GamePaths)
                    {
                        invalidPaths.Add(gamePath);
                    }

                    continue;
                }

                replacementList.Add(replacement);
                sourceHashes.Add(replacement.Hash);
                if (replacement.GamePaths.Length == 0 || replacement.GamePaths.Any(path => !path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase) 
                && !path.EndsWith(".atex", StringComparison.OrdinalIgnoreCase)))
                {
                    nonTextureHashes.Add(replacement.Hash);
                }
            }
        }

        if (invalidReplacementCount > 0)
        {
            Logger.LogWarning("[BASE-{applicationBase}] Ignoring {count} replacement(s) without a hash or file swap path for {pair} Paths: {paths}",
                applicationBase, invalidReplacementCount, Pair.PairUIDName, string.Join(", ", invalidPaths));
        }

        try
        {
            if (compressedAlternateUsage != CompressedAlternateUsage.AlwaysSourceQuality
                && compressedAlternateUsage != CompressedAlternateUsage.CompressedNewDownloads
                && compressedAlternateUsage != CompressedAlternateUsage.AlwaysCompressed)
            {
                throw new ArgumentException("Invalid compressed alternate usage specified!", nameof(compressedAlternateUsage));
            }

            Dictionary<string, (bool Confirmed, string? AlternateHash)> alternateInfo = [];
            HashSet<string> initialLookupHashes = new(StringComparer.OrdinalIgnoreCase);

            if (compressedAlternateUsage == CompressedAlternateUsage.AlwaysSourceQuality)
            {
                initialLookupHashes.UnionWith(sourceHashes);
            }
            else
            {
                foreach (var sourceHash in sourceHashes)
                {
                    token.ThrowIfCancellationRequested();

                    if (nonTextureHashes.Contains(sourceHash))
                    {
                        alternateInfo[sourceHash] = (true, null);
                        initialLookupHashes.Add(sourceHash);

                        continue;
                    }

                    var confirmed = _compressedAlternateManager.TryGetCachedCompressedAlternate(sourceHash, out var alternateHash);
                    alternateInfo[sourceHash] = (confirmed, alternateHash);

                    if (compressedAlternateUsage == CompressedAlternateUsage.AlwaysCompressed && confirmed && alternateHash != null)
                    {
                        initialLookupHashes.Add(alternateHash);
                    }
                    else
                    {
                        initialLookupHashes.Add(sourceHash);
                    }
                }
            }

            var fileCachesByHash = await _fileDbManager.GetFirstFileCacheEntitiesByHashes(initialLookupHashes, token).ConfigureAwait(false);

            // filter out non-tex files and files smaller than 4KB since we're never going to have alternates for them anyway
            if (compressedAlternateUsage != CompressedAlternateUsage.AlwaysSourceQuality)
            {
                foreach (var sourceHash in sourceHashes)
                {
                    if (!nonTextureHashes.Contains(sourceHash) && fileCachesByHash.TryGetValue(sourceHash, out var sourceFileCache))
                    {
                        if (sourceFileCache != null && sourceFileCache.Size >= 0 && sourceFileCache.Size < 4096)
                        {
                            _compressedAlternateManager.SetCompressedAlternate(sourceHash, null, neverWillHaveAlternate: true);
                            alternateInfo[sourceHash] = (true, null);
                        }
                    }
                }
            }

            if (compressedAlternateUsage == CompressedAlternateUsage.CompressedNewDownloads)
            {
                HashSet<string> alternateHashesToLookup = new(StringComparer.OrdinalIgnoreCase);

                foreach (var sourceHash in sourceHashes)
                {
                    fileCachesByHash.TryGetValue(sourceHash, out var sourceFileCache);
                    var alternate = alternateInfo[sourceHash];

                    if (sourceFileCache == null && alternate.Confirmed && alternate.AlternateHash != null)
                    {
                        alternateHashesToLookup.Add(alternate.AlternateHash);
                    }
                }

                if (alternateHashesToLookup.Count > 0)
                {
                    var alternateFileCaches = await _fileDbManager.GetFirstFileCacheEntitiesByHashes(alternateHashesToLookup, token).ConfigureAwait(false);

                    foreach (var alternateFileCache in alternateFileCaches)
                    {
                        fileCachesByHash[alternateFileCache.Key] = alternateFileCache.Value;
                    }
                }
            }

            foreach (var item in replacementList)
            {
                token.ThrowIfCancellationRequested();

                var sourceHash = item.Hash;
                var selectedHash = sourceHash;
                var alternateSelected = false;

                fileCachesByHash.TryGetValue(sourceHash, out var fileCache);

                if (compressedAlternateUsage != CompressedAlternateUsage.AlwaysSourceQuality)
                {
                    var alternate = alternateInfo[sourceHash];
                    var useAlternate = alternate.Confirmed && alternate.AlternateHash != null
                        && (compressedAlternateUsage == CompressedAlternateUsage.AlwaysCompressed || fileCache == null);

                    if (useAlternate)
                    {
                        selectedHash = alternate.AlternateHash!;
                        alternateSelected = true;
                        compressionSubstitutions[sourceHash] = selectedHash;
                        fileCachesByHash.TryGetValue(selectedHash, out fileCache);

                        Logger.LogTrace("CompressSubstitution[{character}]: {path} is [{new}] instead of source [{old}] (TryCalculateModdedDictionary)",
                                PlayerName, string.Join(',', item.GamePaths), selectedHash, sourceHash);
                    }
                    else if (compressedAlternateUsage == CompressedAlternateUsage.AlwaysCompressed && !alternate.Confirmed)
                    {
                        if (fileCache != null)
                        {
                            Logger.LogTrace("CompressSubstitution[{character}]: sending {path} source [{old}] for re-download to check for alternates (TryCalculateModdedDictionary)",
                                    PlayerName, string.Join(',', item.GamePaths), sourceHash);

                            locallyPresentFiles.Add(sourceHash);
                        }

                        fileCache = null;
                    }
                }

                if (fileCache != null)
                {
                    if (!Path.HasExtension(fileCache.ResolvedFilepath))
                    {
                        fileCache = _fileDbManager.MigrateFileHashToExtension(fileCache, Path.GetExtension(item.GamePaths[0]).TrimStart('.'));
                        fileCachesByHash[selectedHash] = fileCache;
                    }

                    foreach (var gamePath in item.GamePaths)
                    {
                        outputDict[(gamePath, selectedHash)] = fileCache.ResolvedFilepath;
                    }
                }
                else
                {
                    Logger.LogTrace("Missing file: {hash}", selectedHash);

                    if (alternateSelected)
                    {
                        missingFiles.Add(new FileReplacementData()
                        {
                            GamePaths = item.GamePaths,
                            Hash = selectedHash,
                        });
                    }
                    else
                    {
                        missingFiles.Add(item);
                    }
                }
            }

            moddedDictionary = outputDict;

            foreach (var item in fileSwapList)
            {
                foreach (var gamePath in item.GamePaths)
                {
                    Logger.LogTrace("[BASE-{appBase}] Adding file swap for {path}: {fileSwap}", applicationBase, gamePath, item.FileSwapPath);
                    moddedDictionary[(gamePath, null)] = item.FileSwapPath;
                }
            }
        }
        catch (OperationCanceledException)
        {
            locallyPresentFiles = [];
            Logger.LogDebug("[BASE-{appBase}] Calculation of replacements was cancelled", applicationBase);
        }
        catch (Exception ex)
        {
            locallyPresentFiles = [];
            Logger.LogError(ex, "[BASE-{appBase}] Something went wrong during calculation replacements", applicationBase);
        }

        st.Stop();
        Logger.LogDebug("[BASE-{appBase}] ModdedPaths calculated in {time}ms, missing files: {count}, total files: {total}",
            applicationBase, st.ElapsedMilliseconds, missingFiles.Count, moddedDictionary.Count);

        return (missingFiles, locallyPresentFiles, moddedDictionary);
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

    // Do not call this from a RunOnFramwork Task!
    private async Task RevertCustomizationDataAsync(ObjectKind objectKind, string name, Guid applicationId, CancellationToken cancelToken)
    {
        Logger.LogDebug("[{applicationId}] Reverting all Customization for {alias}/{name} {objectKind}", applicationId, Pair.UserData.AliasOrUID, name, objectKind);

        if (_customizeIds.TryGetValue(objectKind, out var customizeId))
        {
            _customizeIds.Remove(objectKind);
        }

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            if (!_dalamudUtil.TryGetPlayerAddressAndIndexOnFrameworkByContentId(Pair.ContendId, out var playerAddress, out _))
            {
                Logger.LogWarning("[{applicationId}] Reverting of {kind} for {pair} failed, no target address", applicationId, objectKind, Pair.PairUIDName);

                return;
            }

            if (objectKind == ObjectKind.Player)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                Logger.LogDebug("[{applicationId}] Starting revert player for {name} on the framework thread.", applicationId, name);

                try
                {
                    _ipcManager.Glamourer.RevertOnFramework(Logger, applicationId, playerAddress);
                    _ipcManager.Heels.RestoreOffsetForPlayerFromFramework(playerAddress);
                    _ipcManager.CustomizePlus.RevertByIdFromFramework(customizeId);
                    _ipcManager.Honorific.ClearTitleFromFramework(playerAddress);
                    _ipcManager.Moodles.RevertStatusOnFramework(playerAddress);
                    _ipcManager.PetNames.ClearPlayerDataOnFramework(playerAddress);
                    _ipcManager.Loci.UnregisterActorFromFramework(playerAddress);
                    _ipcManager.Penumbra.RedrawOnFramework(Logger, applicationId, playerAddress);
                }
                finally
                {
                    stopwatch.Stop();
                    Logger.LogDebug("[{applicationId}] Finished revert player for {name} on the framework thread in {ms}ms", applicationId, name, stopwatch.ElapsedMilliseconds);
                }
            }
            else if (objectKind == ObjectKind.MinionOrMount)
            {
                var minionOrMount = _dalamudUtil.GetMinionOrMountPtrFromFramework(playerAddress);
                if (minionOrMount != nint.Zero)
                {
                    _ipcManager.CustomizePlus.RevertByIdFromFramework(customizeId);
                    _ipcManager.Loci.UnregisterBuddyFromFramework(name, minionOrMount);
                    _ipcManager.Glamourer.RevertOnFramework(Logger, applicationId, minionOrMount);
                    _ipcManager.Penumbra.RedrawOnFramework(Logger, applicationId, minionOrMount);
                }
            }
            else if (objectKind == ObjectKind.Pet)
            {
                var pet = _dalamudUtil.GetPetPtrFromFramework(playerAddress);
                if (pet != nint.Zero)
                {
                    _ipcManager.CustomizePlus.RevertByIdFromFramework(customizeId);
                    _ipcManager.Loci.UnregisterBuddyFromFramework(name, pet);
                    _ipcManager.Glamourer.RevertOnFramework(Logger, applicationId, pet);
                    _ipcManager.Penumbra.RedrawOnFramework(Logger, applicationId, pet);
                }
            }
            else if (objectKind == ObjectKind.Companion)
            {
                var companion = _dalamudUtil.GetCompanionPtrFromFramework(playerAddress);
                if (companion != nint.Zero)
                {
                    _ipcManager.CustomizePlus.RevertByIdFromFramework(customizeId);
                    _ipcManager.Loci.UnregisterBuddyFromFramework(name, companion);
                    _ipcManager.Glamourer.RevertOnFramework(Logger, applicationId, companion);
                    _ipcManager.Penumbra.RedrawOnFramework(Logger, applicationId, companion);
                }
            }

        }).ConfigureAwait(false);

        Pair.LastLoadedSoundSinceRedrawTime = null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Stopwatch stopwatch = Stopwatch.StartNew();
        SetUploading(isUploading: false);
        var name = Pair.PlayerName;
        Logger.LogDebug("Disposing {name} ({user})", name, Pair);
        try
        {
            Guid applicationId = Guid.NewGuid();
            _dataApplicationCancellationTokenSource?.CancelDispose();
            _dataApplicationCancellationTokenSource = null;
            _downloadManager.Dispose();

            if (!string.IsNullOrEmpty(name))
            {
                Mediator.Publish(new EventMessage(new Event(name, Pair.UserData, nameof(PairDataApplicationHandler), EventSeverity.Informational, "Disposing User")));
            }

            // THIS IS REQUIRED else the plugin unload will lock the game (feel free to fix)
            if (_lifetime.ApplicationStopping.IsCancellationRequested) return;

            if (_dalamudUtil is { IsZoning: false, IsInCutscene: false } && !string.IsNullOrEmpty(name))
            {
                Logger.LogTrace("[{applicationId}] Restoring state for {name} ({OnlineUser})", applicationId, name, Pair.UserPair);

                if (_penumbraCollection != null)
                {
                    Logger.LogDebug("[{applicationId}] Removing Temp Collection for {name} ({user})", applicationId, name, Pair.UserPair);
                    _dalamudUtil.RunOnFrameworkThread(() =>
                    {
                        _ipcManager.Penumbra.RemoveTemporaryCollectionOnFramework(Logger, applicationId, _penumbraCollection.Value);

                    }).GetAwaiter().GetResult();
                }

                if (!Pair.IsVisible)
                {
                    Logger.LogDebug("[{applicationId}] Restoring Glamourer for {name} ({user})", applicationId, name, Pair.UserPair);
                    _dalamudUtil.RunOnFrameworkThread(() =>
                    {
                        _ipcManager.Glamourer.RevertByNameOnFramework(Logger, name, applicationId);

                    }).GetAwaiter().GetResult();
                }
                else
                {
                    using var cts = new CancellationTokenSource();
                    cts.CancelAfter(TimeSpan.FromSeconds(60));

                    Logger.LogInformation("[{applicationId}] CachedData is null {isNull}, contains things: {contains}",
                        applicationId, _cachedData == null, _cachedData?.FileReplacements.Any() ?? false);

                    foreach (KeyValuePair<ObjectKind, List<FileReplacementData>> item in _cachedData?.FileReplacements ?? _pendingData?.FileReplacements ?? [])
                    {
                        try
                        {
                            // NOTE: THIS CAN FREEZE THE GAME - Archon, can you give me any details on this comment?
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
            _penumbraCollection = null;
            SetPairCharacterData(null);
            stopwatch.Stop();
            Logger.LogDebug("Disposing {name} complete in {elapsedMs}ms", name, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task ReloadAndLockVanillaState(Guid applicationBase)
    {
        if (Interlocked.Exchange(ref _isRedrawVanillaRunning, 1) == 1)
        {
            return;
        }

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            try
            {
                if (!_dalamudUtil.TryGetPlayerAddressAndIndexOnFrameworkByContentId(Pair.ContendId, out var address, out var index))
                {
                    return;
                }
                _ipcManager.Glamourer.RevertOnFramework(Logger, applicationBase, address);
                var charState = _ipcManager.Glamourer.GetCharacterCustomizationOnFramework(index);
                _ipcManager.Glamourer.ApplyAllOnFramework(index, charState, Guid.NewGuid());
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[BASE-{appbase}] Filter task failed for {player}", applicationBase, this);
            }
            finally
            {
                Interlocked.Exchange(ref _isRedrawVanillaRunning, 0);
            }
        }).ConfigureAwait(false);
    }

    // this is old and needs updating
    private void CheckForVanillaLoadingOfPair()
    {
        if (!Pair.IsVisible)
        {
            Logger.LogTrace("Vanilla check pair not visible");
        }

        var applicationBase = Guid.NewGuid();

        Logger.LogInformation("[BASE-{appbase}] Filtering enabled; scheduling penumbra/glamourer apply for {player}", applicationBase, this);

        _ = ReloadAndLockVanillaState(applicationBase);
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

    private void UpdateCachedAddonData(AddonPlugin addonPlugin, CharacterData characterData)
    {
        lock (_cachedDataLock)
        {
            if (addonPlugin == AddonPlugin.Heels)
            {
                _cachedData!.HeelsData = characterData.HeelsData;
            }
            else if (addonPlugin == AddonPlugin.Honorific)
            {
                _cachedData!.HonorificData = characterData.HonorificData;
            }
            else if (addonPlugin == AddonPlugin.Moodles)
            {
                _cachedData!.MoodlesData = characterData.MoodlesData;
            }
            else if (addonPlugin == AddonPlugin.Loci)
            {
                _cachedData!.LociData = characterData.LociData;
            }
            else if (addonPlugin == AddonPlugin.PetNames)
            {
                _cachedData!.PetNamesData = characterData.PetNamesData;
            }
            else
            {
                throw new InvalidDataException("Got an unknown AddonPlugin type!");
            }
        }
    }

    private CharacterData? RemoveNotSyncedFiles(CharacterData? data)
    {
        Logger.LogTrace("Removing not synced files");
        if (data == null)
        {
            Logger.LogTrace("Nothing to remove");
            return data;
        }

        // permissions
        bool disableIndividualAnimations = Pair.UserPair.OtherPermissions.IsDisableAnimations() || Pair.UserPair.OwnPermissions.IsDisableAnimations();
        bool disableIndividualVFX = Pair.UserPair.OtherPermissions.IsDisableVFX() || Pair.UserPair.OwnPermissions.IsDisableVFX();
        bool disableIndividualSounds = Pair.UserPair.OtherPermissions.IsDisableSounds() || Pair.UserPair.OwnPermissions.IsDisableSounds();

        // global filtering
        bool filterBiDiPairs = _configService.Current.DoFilteringBidirectionDirectPairs;
        bool isDirectPaired = Pair.UserPair.IndividualPairStatus == IndividualPairStatus.Bidirectional;
        bool overrideFilterPair = !filterBiDiPairs && isDirectPaired;

        bool overrideFilterUid = _configService.Current.UIDsToOverrideFilter.Contains(Pair.UserPair.User.UID, StringComparer.OrdinalIgnoreCase)
            || _configService.Current.UIDsToOverrideFilter.Contains(Pair.UserPair.User.Alias, StringComparer.OrdinalIgnoreCase);

        Logger.LogTrace("Disable: Sounds: {disableIndividualSounds}, Anims: {disableIndividualAnims}; " +
            "VFX: {disableGroupSounds}", disableIndividualSounds, disableIndividualAnimations, disableIndividualVFX);

        // global filtering
        bool filterAnimations = _configService.Current.FilterAnimations;
        bool filterSounds = _configService.Current.FilterSounds;
        bool filterVfx = _configService.Current.FilterVfx;
        bool filterMinionsMounts = _configService.Current.FilterMinionsAndMounts;
        bool filterPets = _configService.Current.FilterPets;

        // zonesync filtering
        bool filterZoneSyncAnimations = _zoneSyncConfig.Current.ZoneSyncFilterAnimations;
        bool filterZoneSyncSounds = _zoneSyncConfig.Current.ZoneSyncFilterSounds;
        bool filterZoneSyncVfx = _zoneSyncConfig.Current.ZoneSyncFilterVfx;

        bool zoneSyncFiltering = Pair.IsZoneSyncOnlyPair && (filterZoneSyncAnimations || filterZoneSyncSounds || filterZoneSyncVfx);
        bool hasDisabledIndevidual = disableIndividualAnimations || disableIndividualSounds || disableIndividualVFX;
        bool hasDisabledViaFilter = filterAnimations || filterSounds || filterVfx || filterMinionsMounts || filterPets;
        bool filterUserPerms = hasDisabledViaFilter && !overrideFilterUid && !overrideFilterPair;
        if (filterUserPerms || hasDisabledIndevidual || zoneSyncFiltering)
        {
            Logger.LogTrace("Data cleaned up: Animations disabled: {disableAnimations}, Sounds disabled: {disableSounds}, VFX disabled: {disableVFX}",
                disableIndividualAnimations, disableIndividualSounds, disableIndividualVFX);
            foreach (var objectKind in data.FileReplacements.Select(k => k.Key))
            {
                if ((filterUserPerms && filterSounds) || disableIndividualSounds || (zoneSyncFiltering && filterZoneSyncSounds))
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (((filterUserPerms && filterAnimations) || disableIndividualAnimations || (zoneSyncFiltering && filterZoneSyncAnimations)) && objectKind != ObjectKind.MinionOrMount)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if ((filterUserPerms && filterVfx) || disableIndividualVFX || (zoneSyncFiltering && filterZoneSyncVfx))
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (filterUserPerms && filterMinionsMounts && objectKind == ObjectKind.MinionOrMount)
                    data.FileReplacements[objectKind] = [];
                if (filterUserPerms && filterPets && (objectKind == ObjectKind.Pet || objectKind == ObjectKind.Companion))
                    data.FileReplacements[objectKind] = [];
            }
        }

        return data;
    }
}
