using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace MareSynchronos.FileCache;

public sealed class CacheMonitor : DisposableMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileCompactor _fileCompactor;
    private readonly FileCacheManager _fileDbManager;
    private readonly IpcManager _ipcManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly SemaphoreSlim _fileScannerSemaphore = new(1, 1);
    private readonly object _scanStateLock = new();
    private readonly object _fswLock = new();
    private long _currentFileProgress = 0;
    private CancellationTokenSource _scanCancellationTokenSource = new();
    private readonly CancellationTokenSource _periodicCalculationTokenSource = new();
    public static readonly IImmutableList<string> AllowedFileExtensions =
        [".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk", ".json"];
    private static readonly HashSet<string> AllowedFileExtension = new(AllowedFileExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan ScannerDebounce = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WatcherMaximumDebounce = TimeSpan.FromSeconds(30);
    private const int ScanBatchSize = 500;
    private const int WatcherBufferSize = 64 * 1024;
    private int _disposed;
    private int _isScanRunning;
    private bool _haveNotifiedLowDiskSpace = false;
    private bool _hasFirstScanCompleted = false;

    private CancellationTokenSource _penumbraWatcherCancellationTokenSource = new();
    private CancellationTokenSource _playerSyncWatcherCancellationTokenSource = new();
    private long _penumbraWatcherChangeVersion;
    private long _playerSyncWatcherChangeVersion;
    private int _penumbraWatcherExecutionRunning;
    private int _playerSyncWatcherExecutionRunning;


    public CacheMonitor(ILogger<CacheMonitor> logger, IpcManager ipcManager, MareConfigService configService,
        FileCacheManager fileDbManager, MareMediator mediator, PerformanceCollectorService performanceCollector, DalamudUtilService dalamudUtil,
        FileCompactor fileCompactor) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _configService = configService;
        _fileDbManager = fileDbManager;
        _performanceCollector = performanceCollector;
        _dalamudUtil = dalamudUtil;
        _fileCompactor = fileCompactor;
        Mediator.Subscribe<PenumbraInitializedMessage>(this, (_) =>
        {
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
            StartPlayerSyncWatcher(configService.Current.CacheFolder);
            InvokeScan();
        });
        Mediator.Subscribe<HaltScanMessage>(this, (msg) => HaltScan(msg.Source));
        Mediator.Subscribe<ResumeScanMessage>(this, (msg) => ResumeScan(msg.Source));
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            StartPlayerSyncWatcher(configService.Current.CacheFolder);
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
            InvokeScan();
        });
        Mediator.Subscribe<PenumbraDirectoryChangedMessage>(this, (msg) =>
        {
            StartPenumbraWatcher(msg.ModDirectory);
            InvokeScan();
        });
        if (_ipcManager.Penumbra.APIAvailable && !string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory))
        {
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
        }
        if (configService.Current.HasValidSetup())
        {
            StartPlayerSyncWatcher(configService.Current.CacheFolder);
            InvokeScan();
        }

        _ = RunPeriodicStorageCalculationAsync(_periodicCalculationTokenSource.Token);
    }

    public long CurrentFileProgress => Interlocked.Read(ref _currentFileProgress);
    public long FileCacheSize { get; set; }
    public long FileCacheDriveFree { get; set; }
    public ConcurrentDictionary<string, int> HaltScanLocks { get; } = new(StringComparer.Ordinal);
    public bool IsScanRunning => Volatile.Read(ref _isScanRunning) != 0;
    public long TotalFiles { get; private set; }
    public long TotalFilesStorage { get; private set; }
    public FileSystemWatcher? PenumbraWatcher { get; private set; }
    public FileSystemWatcher? PlayerSyncWatcher { get; private set; }

    #region FILE SYSTEM WATCHERS

    private readonly record struct WatcherChange(WatcherChangeTypes ChangeType, long Version, string? OldFullPath = null);
    private readonly Dictionary<string, WatcherChange> _penumbraChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WatcherChange> _playerSyncChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);

    public void StopMonitoring()
    {
        Logger.LogInformation("Stopping monitoring of Penumbra and PlayerSync storage folders");
        lock (_fswLock)
        {
            PlayerSyncWatcher?.Dispose();
            PenumbraWatcher?.Dispose();
            PlayerSyncWatcher = null;
            PenumbraWatcher = null;
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            lock (_playerSyncChanges)
            {
                _playerSyncWatcherCancellationTokenSource = _playerSyncWatcherCancellationTokenSource.CancelRecreate();
                _playerSyncChanges.Clear();
                Interlocked.Increment(ref _playerSyncWatcherChangeVersion);
            }

            lock (_penumbraChanges)
            {
                _penumbraWatcherCancellationTokenSource = _penumbraWatcherCancellationTokenSource.CancelRecreate();
                _penumbraChanges.Clear();
                Interlocked.Increment(ref _penumbraWatcherChangeVersion);
            }
        }
    }

    public bool StorageisNTFS { get; private set; } = false;

    public void StartPlayerSyncWatcher(string? playerSyncPath)
    {
        lock (_fswLock)
        {
            StartPlayerSyncWatcherInternal(playerSyncPath);
        }
    }

    private void StartPlayerSyncWatcherInternal(string? playerSyncPath)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        PlayerSyncWatcher?.Dispose();
        lock (_playerSyncChanges)
        {
            _playerSyncWatcherCancellationTokenSource = _playerSyncWatcherCancellationTokenSource.CancelRecreate();
            _playerSyncChanges.Clear();
            Interlocked.Increment(ref _playerSyncWatcherChangeVersion);
        }
        if (string.IsNullOrEmpty(playerSyncPath) || !Directory.Exists(playerSyncPath))
        {
            PlayerSyncWatcher = null;
            Logger.LogWarning("PlayerSync file path is not set, FSW cannot start!");

            return;
        }

        DriveInfo di = new(new DirectoryInfo(_configService.Current.CacheFolder).Root.FullName);
        StorageisNTFS = string.Equals("NTFS", di.DriveFormat, StringComparison.OrdinalIgnoreCase);
        Logger.LogInformation("PlayerSync Storage is on NTFS drive: {isNtfs}", StorageisNTFS);

        Logger.LogDebug("Initializing PlayerSync FSW on {path}", playerSyncPath);
        PlayerSyncWatcher = new()
        {
            Path = playerSyncPath,
            InternalBufferSize = WatcherBufferSize,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = false,
        };

        PlayerSyncWatcher.Changed += PlayerSyncWatcherFileChanged;
        PlayerSyncWatcher.Created += PlayerSyncWatcherFileChanged;
        PlayerSyncWatcher.Deleted += PlayerSyncWatcherFileChanged;
        PlayerSyncWatcher.Renamed += PlayerSyncWatcherRenamed;
        PlayerSyncWatcher.Error += WatcherError;
        PlayerSyncWatcher.EnableRaisingEvents = true;
    }

    private void PlayerSyncWatcherFileChanged(object sender, FileSystemEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Logger.LogTrace("PlayerSync FSW: FileChanged: {change} => {path}", e.ChangeType, e.FullPath);

        if (e.ChangeType is not (WatcherChangeTypes.Changed or WatcherChangeTypes.Created or WatcherChangeTypes.Deleted) 
            || !IsAllowedFile(e.FullPath) || !IsCacheFileName(e.FullPath))
        {
            return;
        }

        lock (_playerSyncChanges)
        {
            var version = Interlocked.Increment(ref _playerSyncWatcherChangeVersion);
            _playerSyncChanges[e.FullPath] = new(e.ChangeType == WatcherChangeTypes.Created ? WatcherChangeTypes.Changed : e.ChangeType, version);
        }

        _ = PlayerSyncWatcherExecution();
    }

    private void PlayerSyncWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Logger.LogTrace("PlayerSync FSW: Renamed: {oldPath} => {newPath}", e.OldFullPath, e.FullPath);

        var hasChange = false;
        lock (_playerSyncChanges)
        {
            if (IsAllowedFile(e.OldFullPath) && IsCacheFileName(e.OldFullPath))
            {
                var oldVersion = Interlocked.Increment(ref _playerSyncWatcherChangeVersion);
                _playerSyncChanges[e.OldFullPath] = new(WatcherChangeTypes.Deleted, oldVersion);
                hasChange = true;
            }

            if (IsAllowedFile(e.FullPath) && IsCacheFileName(e.FullPath))
            {
                var newVersion = Interlocked.Increment(ref _playerSyncWatcherChangeVersion);
                _playerSyncChanges[e.FullPath] = new(WatcherChangeTypes.Changed, newVersion);
                hasChange = true;
            }
        }

        if (hasChange)
        {
            _ = PlayerSyncWatcherExecution();
        }
    }

    public void StartPenumbraWatcher(string? penumbraPath)
    {
        lock (_fswLock)
        {
            StartPenumbraWatcherInternal(penumbraPath);
        }
    }

    private void StartPenumbraWatcherInternal(string? penumbraPath)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        PenumbraWatcher?.Dispose();
        lock (_penumbraChanges)
        {
            _penumbraWatcherCancellationTokenSource = _penumbraWatcherCancellationTokenSource.CancelRecreate();
            _penumbraChanges.Clear();
            Interlocked.Increment(ref _penumbraWatcherChangeVersion);
        }
        if (string.IsNullOrEmpty(penumbraPath) || !Directory.Exists(penumbraPath))
        {
            PenumbraWatcher = null;
            Logger.LogError("[FSW] Penumbra is not connected or no valid config path exists!");

            return;
        }

        Logger.LogDebug("[FSW] Initializing Penumbra FSW on {path}", penumbraPath);
        PenumbraWatcher = new()
        {
            Path = penumbraPath,
            InternalBufferSize = WatcherBufferSize,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*",
            IncludeSubdirectories = true
        };

        PenumbraWatcher.Changed += PenumbraWatcherFileChanged;
        PenumbraWatcher.Created += PenumbraWatcherFileChanged;
        PenumbraWatcher.Deleted += PenumbraWatcherFileChanged;
        PenumbraWatcher.Renamed += PenumbraWatcherRenamed;
        PenumbraWatcher.Error += WatcherError;
        PenumbraWatcher.EnableRaisingEvents = true;
    }

    private void PenumbraWatcherFileChanged(object sender, FileSystemEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0 || e.ChangeType is not (WatcherChangeTypes.Changed or WatcherChangeTypes.Created or WatcherChangeTypes.Deleted))
        {
            return;
        }

        var isDirectory = Directory.Exists(e.FullPath);
        if ((e.ChangeType == WatcherChangeTypes.Changed && (isDirectory || !IsScannablePenumbraFile(e.FullPath)))
            || (e.ChangeType == WatcherChangeTypes.Created && !isDirectory && !IsScannablePenumbraFile(e.FullPath)))
        {
            return;
        }

        lock (_penumbraChanges)
        {
            var version = Interlocked.Increment(ref _penumbraWatcherChangeVersion);
            if (_penumbraChanges.TryGetValue(e.FullPath, out var existingChange) && !string.IsNullOrWhiteSpace(existingChange.OldFullPath))
            {
                _penumbraChanges[e.FullPath] = e.ChangeType == WatcherChangeTypes.Deleted
                    ? new(e.ChangeType, version, existingChange.OldFullPath)
                    : new(WatcherChangeTypes.Renamed, version, existingChange.OldFullPath);
            }
            else
            {
                _penumbraChanges[e.FullPath] = new(e.ChangeType == WatcherChangeTypes.Created && !isDirectory ? WatcherChangeTypes.Changed : e.ChangeType, version);
            }
        }

        Logger.LogTrace("Penumbra FSW: {change} - {path}", e.ChangeType, e.FullPath);

        _ = PenumbraWatcherExecution();
    }

    private void PenumbraWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Logger.LogTrace("[FSW] Penumbra FSW Renamed: {oldPath} => {newPath}", e.OldFullPath, e.FullPath);

        lock (_penumbraChanges)
        {
            var version = Interlocked.Increment(ref _penumbraWatcherChangeVersion);
            var oldFullPath = e.OldFullPath;

            if (_penumbraChanges.Remove(e.OldFullPath, out var existingChange) && !string.IsNullOrWhiteSpace(existingChange.OldFullPath))
            {
                oldFullPath = existingChange.OldFullPath;
            }

            _penumbraChanges[e.FullPath] = new(e.ChangeType, version, oldFullPath);
        }

        _ = PenumbraWatcherExecution();
    }

    private void WatcherError(object sender, ErrorEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Logger.LogWarning(e.GetException(), "File system watcher failed, running a full file scan");

        InvokeScan();
    }

    private async Task PlayerSyncWatcherExecution()
    {
        if (Interlocked.CompareExchange(ref _playerSyncWatcherExecutionRunning, 1, 0) != 0)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(5);
        var debounceStartedAt = Stopwatch.GetTimestamp();
        try
        {
            CancellationToken token;
            lock (_playerSyncChanges)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                token = _playerSyncWatcherCancellationTokenSource.Token;
            }

            while (!token.IsCancellationRequested)
            {
                var changeVersion = Interlocked.Read(ref _playerSyncWatcherChangeVersion);
                await Task.Delay(delay, token).ConfigureAwait(false);
                if (changeVersion != Interlocked.Read(ref _playerSyncWatcherChangeVersion) && Stopwatch.GetElapsedTime(debounceStartedAt) < WatcherMaximumDebounce)
                {
                    continue;
                }

                while (HaltScanLocks.Any(entry => entry.Value > 0) || !_fileDbManager.DatabaseStatus.IsReady)
                {
                    await Task.Delay(250, token).ConfigureAwait(false);
                }

                var isAbleToRun = false;
                try
                {
                    await _fileScannerSemaphore.WaitAsync(token).ConfigureAwait(false);
                    isAbleToRun = true;
                    token.ThrowIfCancellationRequested();

                    while (HaltScanLocks.Any(entry => entry.Value > 0) || !_fileDbManager.DatabaseStatus.IsReady)
                    {
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }

                    Dictionary<string, WatcherChange> changes;
                    lock (_playerSyncChanges)
                    {
                        token.ThrowIfCancellationRequested();
                        if (changeVersion != Interlocked.Read(ref _playerSyncWatcherChangeVersion) && Stopwatch.GetElapsedTime(debounceStartedAt) < WatcherMaximumDebounce)
                        {
                            continue;
                        }
                        changes = _playerSyncChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.OrdinalIgnoreCase);
                    }

                    if (changes.Count == 0)
                    {
                        return;
                    }

                    await ProcessPlayerSyncWatcherChanges(changes, token).ConfigureAwait(false);

                    lock (_playerSyncChanges)
                    {
                        foreach (var change in changes)
                        {
                            if (_playerSyncChanges.TryGetValue(change.Key, out var currentChange) && currentChange.Version == change.Value.Version)
                            {
                                _playerSyncChanges.Remove(change.Key);
                            }
                        }

                        if (_playerSyncChanges.Count == 0)
                        {
                            return;
                        }

                        debounceStartedAt = Stopwatch.GetTimestamp();
                    }
                }
                finally
                {
                    if (isAbleToRun) _fileScannerSemaphore.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error processing PlayerSync file watcher changes");
        }
        finally
        {
            Volatile.Write(ref _playerSyncWatcherExecutionRunning, 0);
            lock (_playerSyncChanges)
            {
                if (Volatile.Read(ref _disposed) == 0 && _playerSyncChanges.Count > 0) _ = PlayerSyncWatcherExecution();
            }
        }
    }

    private async Task ProcessPlayerSyncWatcherChanges(Dictionary<string, WatcherChange> changes, CancellationToken cancellationToken)
    {
        var changedPaths = changes.Where(change => change.Value.ChangeType == WatcherChangeTypes.Changed).Select(change => change.Key).ToList();
        var deletedPaths = changes.Where(change => change.Value.ChangeType == WatcherChangeTypes.Deleted
            && !change.Key.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
            && !change.Key.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(change => change.Key)
            .ToList();

        foreach (var path in changedPaths)
        {
            Logger.LogDebug("PlayerSync FSW Change: Changed - {path}", path);
        }

        foreach (var path in deletedPaths)
        {
            Logger.LogDebug("PlayerSync FSW Change: Deleted - {path}", path);
        }

        List<FileCacheEntity> fileCachesToRemove = [];

        foreach (var path in deletedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(path))
            {
                changedPaths.Add(path);

                continue;
            }

            fileCachesToRemove.AddRange(await _fileDbManager.GetAllNonMemoryFrontedNonValidatedFileCacheEntitiesByPath(path, FileCacheStore.Cache, cancellationToken).ConfigureAwait(false));
        }

        if (changedPaths.Count > 0)
        {
            await _fileDbManager.UpdateOrCreateFileCacheEntitiesByPathsAsync(changedPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(), FileCacheStore.Cache, cancellationToken).ConfigureAwait(false);
        }

        if (fileCachesToRemove.Count > 0)
        {
            await _fileDbManager.RemoveFileCacheEntities(fileCachesToRemove
                .DistinctBy(fileCache => fileCache.Id)
                .ToList(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PenumbraWatcherExecution()
    {
        if (Interlocked.CompareExchange(ref _penumbraWatcherExecutionRunning, 1, 0) != 0)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(10);
        var debounceStartedAt = Stopwatch.GetTimestamp();
        try
        {
            CancellationToken token;
            lock (_penumbraChanges)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                token = _penumbraWatcherCancellationTokenSource.Token;
            }

            while (!token.IsCancellationRequested)
            {
                var changeVersion = Interlocked.Read(ref _penumbraWatcherChangeVersion);

                await Task.Delay(delay, token).ConfigureAwait(false);

                if (changeVersion != Interlocked.Read(ref _penumbraWatcherChangeVersion) && Stopwatch.GetElapsedTime(debounceStartedAt) < WatcherMaximumDebounce)
                {
                    continue;
                }

                while (HaltScanLocks.Any(entry => entry.Value > 0) || !_fileDbManager.DatabaseStatus.IsReady)
                {
                    await Task.Delay(250, token).ConfigureAwait(false);
                }

                var isAbleToRun = false;
                try
                {
                    await _fileScannerSemaphore.WaitAsync(token).ConfigureAwait(false);
                    isAbleToRun = true;
                    token.ThrowIfCancellationRequested();

                    while (HaltScanLocks.Any(entry => entry.Value > 0) || !_fileDbManager.DatabaseStatus.IsReady)
                    {
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }

                    Dictionary<string, WatcherChange> changes;
                    lock (_penumbraChanges)
                    {
                        token.ThrowIfCancellationRequested();
                        if (changeVersion != Interlocked.Read(ref _penumbraWatcherChangeVersion) && Stopwatch.GetElapsedTime(debounceStartedAt) < WatcherMaximumDebounce)
                        {
                            continue;
                        }

                        changes = _penumbraChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.OrdinalIgnoreCase);
                    }

                    if (changes.Count == 0)
                    {
                        return;
                    }

                    await ProcessPenumbraWatcherChanges(changes, token).ConfigureAwait(false);

                    lock (_penumbraChanges)
                    {
                        foreach (var change in changes)
                        {
                            if (_penumbraChanges.TryGetValue(change.Key, out var currentChange) && currentChange.Version == change.Value.Version)
                            {
                                _penumbraChanges.Remove(change.Key);
                            }
                        }

                        if (_penumbraChanges.Count == 0)
                        {
                            return;
                        }

                        debounceStartedAt = Stopwatch.GetTimestamp();
                    }
                }
                finally
                {
                    if (isAbleToRun) _fileScannerSemaphore.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error processing Penumbra file watcher changes");
        }
        finally
        {
            Volatile.Write(ref _penumbraWatcherExecutionRunning, 0);
            lock (_penumbraChanges)
            {
                if (Volatile.Read(ref _disposed) == 0 && _penumbraChanges.Count > 0) _ = PenumbraWatcherExecution();
            }
        }
    }

    private async Task ProcessPenumbraWatcherChanges(Dictionary<string, WatcherChange> changes, CancellationToken cancellationToken)
    {
        var changedPaths = changes.Where(change => change.Value.ChangeType == WatcherChangeTypes.Changed).Select(change => change.Key).ToList();
        var createdPaths = changes.Where(change => change.Value.ChangeType == WatcherChangeTypes.Created).Select(change => change.Key).ToList();
        var deletedPaths = GetRootPaths(changes
            .Where(change => change.Value.ChangeType == WatcherChangeTypes.Deleted)
            .SelectMany(change => string.IsNullOrWhiteSpace(change.Value.OldFullPath)
                ? new List<string> { change.Key }
                : new List<string> { change.Key, change.Value.OldFullPath! }));
        var renamedPaths = changes.Where(change => change.Value.ChangeType == WatcherChangeTypes.Renamed && !string.IsNullOrWhiteSpace(change.Value.OldFullPath)).ToList();
        List<string> directoriesToScan = [];
        List<FileCacheEntity> fileCachesToRemove = [];

        foreach (var path in changedPaths)
        {
            Logger.LogDebug("[FSW] Penumbra FSW Change: Changed - {path}", path);
        }

        foreach (var path in createdPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.LogDebug("[FSW] Penumbra FSW Change: Created - {path}", path);

            if (Directory.Exists(path))
            {
                directoriesToScan.Add(path);
            }
            else if (File.Exists(path) && IsScannablePenumbraFile(path))
            {
                changedPaths.Add(path);
            }
        }

        foreach (var path in deletedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.LogDebug("[FSW] Penumbra FSW Change: Deleted - {path}", path);

            if (Directory.Exists(path))
            {
                directoriesToScan.Add(path);
            }
            else if (File.Exists(path) && IsScannablePenumbraFile(path))
            {
                changedPaths.Add(path);
            }
            else
            {
                fileCachesToRemove.AddRange(await _fileDbManager.GetAllFileCacheEntitiesUnderPath(path, FileCacheStore.Penumbra, cancellationToken).ConfigureAwait(false));
            }
        }

        foreach (var renamedPath in renamedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var oldPath = renamedPath.Value.OldFullPath!;
            var newPath = renamedPath.Key;

            Logger.LogDebug("[FSW] Penumbra FSW Change: Renamed - {oldPath} => {newPath}", oldPath, newPath);

            if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(newPath) && IsScannablePenumbraFile(newPath))
                {
                    changedPaths.Add(newPath);
                }

                continue;
            }

            if (Directory.Exists(oldPath))
            {
                directoriesToScan.Add(oldPath);
            }
            else if (File.Exists(oldPath) && IsScannablePenumbraFile(oldPath))
            {
                changedPaths.Add(oldPath);
            }
            else
            {
                fileCachesToRemove.AddRange(await _fileDbManager.GetAllFileCacheEntitiesUnderPath(oldPath, FileCacheStore.Penumbra, cancellationToken).ConfigureAwait(false));
            }

            if (Directory.Exists(newPath))
            {
                directoriesToScan.Add(newPath);
            }
            else if (File.Exists(newPath) && IsScannablePenumbraFile(newPath))
            {
                changedPaths.Add(newPath);
            }
        }

        foreach (var directory in GetRootPaths(directoriesToScan))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UpdatePenumbraModFilesAsync(directory, cancellationToken).ConfigureAwait(false);

            if (!Directory.Exists(directory))
            {
                fileCachesToRemove.AddRange(await _fileDbManager.GetAllFileCacheEntitiesUnderPath(directory, FileCacheStore.Penumbra, cancellationToken).ConfigureAwait(false));
            }

            var directoryPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
            changedPaths.RemoveAll(path => Path.GetFullPath(path).StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase));
        }

        if (changedPaths.Count > 0)
        {
            await _fileDbManager.UpdateOrCreateFileCacheEntitiesByPathsAsync(changedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), 
                FileCacheStore.Penumbra, cancellationToken).ConfigureAwait(false);
        }

        if (fileCachesToRemove.Count > 0)
        {
            await _fileDbManager.RemoveFileCacheEntities(fileCachesToRemove.DistinctBy(fileCache => fileCache.Id).ToList(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> UpdatePenumbraModFilesAsync(string modPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(modPath))
        {
            return 0;
        }

        var processedFiles = 0;

        await RunOnLowestPriorityThread(() =>
        {
            try
            {
                foreach (var fileBatch in Directory.EnumerateFiles(modPath, "*.*", SearchOption.AllDirectories).Where(IsScannablePenumbraFile).Chunk(ScanBatchSize))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WaitForScanResume(cancellationToken);
                    var batch = fileBatch.ToList();
                    _fileDbManager.UpdateOrCreateFileCacheEntitiesByPathsAsync(batch, FileCacheStore.Penumbra, cancellationToken).GetAwaiter().GetResult();
                    processedFiles += batch.Count;
                }
            }
            catch (DirectoryNotFoundException) when (!Directory.Exists(modPath))
            {
                // The directory was deleted or moved before we could catch it
            }
        }, cancellationToken).ConfigureAwait(false);

        return processedFiles;
    }

    private static List<string> GetRootPaths(IEnumerable<string> paths)
    {
        List<string> rootPaths = [];

        foreach (var path in paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length))
        {
            if (!rootPaths.Any(rootPath => string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                rootPaths.Add(path);
            }
        }

        return rootPaths;
    }

    #endregion FILE SYSTEM WATCHERS

    #region SCANNING

    public void InvokeScan()
    {
        var newCancellationTokenSource = new CancellationTokenSource();
        var ct = newCancellationTokenSource.Token;
        CancellationTokenSource previousCancellationTokenSource;

        lock (_scanStateLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                newCancellationTokenSource.Dispose();

                return;
            }

            previousCancellationTokenSource = _scanCancellationTokenSource;
            _scanCancellationTokenSource = newCancellationTokenSource;
        }

        _fileDbManager.RefreshFileStoreRoots();
        previousCancellationTokenSource.CancelDispose();
        _ = RunFullFileScanAsync(ct);
    }

    private async Task RunFullFileScanAsync(CancellationToken ct)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        var isAbleToRun = false;

        try
        {
            while (!_fileDbManager.DatabaseStatus.IsReady)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }

            await Task.Delay(ScannerDebounce, ct).ConfigureAwait(false);

            while (HaltScanLocks.Any(entry => entry.Value > 0) || !_fileDbManager.DatabaseStatus.IsReady)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            while (_dalamudUtil.IsOnFrameworkThread)
            {
                Logger.LogWarning("Scanner is on framework, waiting for leaving thread before continuing");
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            await _fileScannerSemaphore.WaitAsync(ct).ConfigureAwait(false);
            isAbleToRun = true;

            while (HaltScanLocks.Any(entry => entry.Value > 0) || !_fileDbManager.DatabaseStatus.IsReady)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            if (!_fileDbManager.IsCacheReady) // we don't need it for this scan, but for disk i/o we wait
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            if (!_hasFirstScanCompleted)
            {
                Logger.LogDebug("First file scan scheduled, waiting 30 seconds before starting...");
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }

            Volatile.Write(ref _isScanRunning, 1);
            TotalFiles = 0;
            TotalFilesStorage = 0;
            Interlocked.Exchange(ref _currentFileProgress, 0);

            Logger.LogDebug("Starting Full File Scan");
            await RunOnLowestPriorityThread(() => _performanceCollector.LogPerformance(this, $"FullFileScan", () => FullFileScan(ct)), ct).ConfigureAwait(false);

            _hasFirstScanCompleted = true;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Full File Scan cancelled");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during Full File Scan");
        }
        finally
        {
            stopwatch.Stop();

            if (isAbleToRun)
            {
                Logger.LogInformation("Full file scan of {files} files and {entry} cache entries finished in {ms}ms",
                    TotalFiles.ToString(), TotalFilesStorage.ToString(), stopwatch.ElapsedMilliseconds);
                TotalFiles = 0;
                TotalFilesStorage = 0;
                Interlocked.Exchange(ref _currentFileProgress, 0);
                Volatile.Write(ref _isScanRunning, 0);
                _fileScannerSemaphore.Release();
            }
        }
    }

    private async Task RunPeriodicStorageCalculationAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);

            while (!(_hasFirstScanCompleted && _fileDbManager.IsCacheReady)) // we don't need it for this scan, but for disk i/o we wait
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    while (_dalamudUtil.IsOnFrameworkThread)
                    {
                        await Task.Delay(100, ct).ConfigureAwait(false);
                    }

                    _fileDbManager.RefreshFileStoreRoots();
                    Logger.LogDebug("File storage scan starting...");
                    await RunOnLowestPriorityThread(() => RecalculateFileCacheSize(ct), ct).ConfigureAwait(false);
                    Logger.LogDebug("File storage scan finished...");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error during file storage scan");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            //
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Periodic storage calculation task stopped");
        }
    }

    private static Task RunOnLowestPriorityThread(Action action, CancellationToken ct)
    {
        return Task.Factory.StartNew(() =>
        {
            var previousThreadPriority = Thread.CurrentThread.Priority;
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                action();
            }
            finally
            {
                Thread.CurrentThread.Priority = previousThreadPriority;
            }
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void RecalculateFileCacheSize(CancellationToken token)
    {
        if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
        {
            FileCacheSize = 0;
            Logger.LogDebug("File directory scan returning with no dir for CacheFolder.");
            return;
        }

        FileCacheSize = -1;
        DriveInfo di = new(new DirectoryInfo(_configService.Current.CacheFolder).Root.FullName);
        try
        {
            FileCacheDriveFree = di.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not determine drive size for Storage Folder {folder}", _configService.Current.CacheFolder);
        }

        long fileCacheSize = 0;
        foreach (var path in Directory.EnumerateFiles(_configService.Current.CacheFolder))
        {
            token.ThrowIfCancellationRequested();

            var file = new FileInfo(path);
            long size;
            try
            {
                size = _fileCompactor.GetFileSizeOnDisk(file, StorageisNTFS);
            }
            catch
            {
                Logger.LogDebug("File directory scan returning with error on FileCompactor.");
                size = 0;
            }

            fileCacheSize += size;
        }

        // check for low disk space
        try
        {
            var total = di.TotalSize;
            if (total > 0)
            {
                var freeSpace = (double)di.AvailableFreeSpace / total;
                bool buffer = di.AvailableFreeSpace < 5L * 1024 * 1024 * 1024;
                if (freeSpace < 0.01 && buffer && !_haveNotifiedLowDiskSpace)
                {
                    _haveNotifiedLowDiskSpace = true;

                    Logger.LogError("Your PlayerSync storage drive is almost 100% full!");
                    Mediator.Publish(new NotificationMessage("Local Storage Full", $"Your PlayerSync storage drive is almost 100% full! Please check: {di.Name}", 
                        MareConfiguration.Models.NotificationType.Error));
                }
                else
                {
                    if (_haveNotifiedLowDiskSpace)
                    {
                        _haveNotifiedLowDiskSpace = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Cannot calculate remaining free space for storage drive: {drive}", di.Name);
        }

        FileCacheSize = fileCacheSize;

        const long maximumCleanupHeadroom = 5L * 1024 * 1024 * 1024;
        var maxCacheInBytes = (long)(_configService.Current.MaxLocalCacheInGiB * 1024d * 1024d * 1024d);
        var cleanupHeadroom = Math.Min(maxCacheInBytes / 20, maximumCleanupHeadroom);
        var space = maxCacheInBytes - cleanupHeadroom;

        Logger.LogDebug("File directory is within bounds: {bounds}, Current size: {currentSize} Limit: {limit} Maximum: {maximum}",
            FileCacheSize <= space, FileCacheSize.ToByteString(), space.ToByteString(), maxCacheInBytes.ToByteString());
        if (FileCacheSize <= space)
        {
            return;
        }

        token.ThrowIfCancellationRequested();

        var bytesToDelete = FileCacheSize - space;
        Logger.LogInformation("File directory is almost full! Requesting {bytesToDelete} bytes be deleted to reach {bytes} bytes", bytesToDelete.ToByteString(), space.ToByteString());

        var isAbleToRun = false;
        try
        {
            WaitForScanResume(token);
            while (!_fileDbManager.DatabaseStatus.IsReady)
            {
                token.ThrowIfCancellationRequested();
                Thread.Sleep(250);
            }

            _fileScannerSemaphore.Wait(token);
            isAbleToRun = true;
            WaitForScanResume(token);

            while (!_fileDbManager.DatabaseStatus.IsReady)
            {
                token.ThrowIfCancellationRequested();
                Thread.Sleep(250);
            }

            var fileCleanupResults = _fileDbManager.ReclaimLocalCacheStorageAsync(bytesToDelete, token).GetAwaiter().GetResult();
            FileCacheSize = Math.Max(0, FileCacheSize - fileCleanupResults.ReclaimedBytes);

            if (fileCleanupResults.ReclaimedBytes < bytesToDelete)
            {
                Logger.LogWarning("Storage cleared {reclaimedBytes} of the requested {requestedBytes} bytes because no cache files could be deleted",
                    fileCleanupResults.ReclaimedBytes, bytesToDelete);
            }
        }
        finally
        {
            if (isAbleToRun)
            {
                _fileScannerSemaphore.Release();
            }
        }
    }

    public void HaltScan(string source)
    {
        HaltScanLocks.AddOrUpdate(source, 1, (_, count) => count + 1);
    }

    public void ResetLocks()
    {
        HaltScanLocks.Clear();
    }

    public void ResumeScan(string source)
    {
        HaltScanLocks.AddOrUpdate(source, 0, (_, count) => Math.Max(0, count - 1));
    }

    private void WaitForScanResume(CancellationToken cancellationToken)
    {
        while (HaltScanLocks.Any(entry => entry.Value > 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(250);
        }
    }

    private void FullFileScan(CancellationToken ct)
    {
        TotalFiles = 0;
        var penumbraDir = _ipcManager.Penumbra.ModDirectory;

        if (string.IsNullOrEmpty(penumbraDir) || !Directory.Exists(penumbraDir))
        {
            Logger.LogWarning("Penumbra directory is not set or does not exist.");
            return;
        }

        if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
        {
            Logger.LogWarning("PlayerSync Cache directory is not set or does not exist.");
            return;
        }

        if (!_ipcManager.Penumbra.APIAvailable)
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        ct.ThrowIfCancellationRequested();
        Logger.LogDebug("Getting files from {penumbra} and {storage}", penumbraDir, _configService.Current.CacheFolder);

        WaitForScanResume(ct);
        List<FileCacheEntity> fileCaches = _fileDbManager.GetAllFileCacheEntitiesAsync(ct).GetAwaiter().GetResult();
        TotalFilesStorage = fileCaches.Count;
        Dictionary<string, FileCacheEntity> penumbraFileCaches = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FileCacheEntity> playerSyncFileCaches = new(StringComparer.OrdinalIgnoreCase);

        foreach (var fileCache in fileCaches)
        {
            if (fileCache.Store == FileCacheStore.Penumbra)
            {
                penumbraFileCaches[fileCache.ResolvedFilepath] = fileCache;
            }
            else if (fileCache.Store == FileCacheStore.Cache)
            {
                playerSyncFileCaches[fileCache.ResolvedFilepath] = fileCache;
            }
        }

        fileCaches = [];
        List<string> filesToUpdateOrCreate = new(ScanBatchSize);
        List<FileCacheEntity> filesToRemove = [];
        long newFileCount = 0;

        void ProcessFileBatch(FileCacheStore store)
        {
            if (filesToUpdateOrCreate.Count == 0)
            {
                return;
            }

            ct.ThrowIfCancellationRequested();
            WaitForScanResume(ct);

            try
            {
                _fileDbManager.UpdateOrCreateFileCacheEntitiesByPathsAsync(filesToUpdateOrCreate, store, ct).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed processing file scan batch");

                foreach (var file in filesToUpdateOrCreate)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        _fileDbManager.UpdateOrCreateFileCacheEntityByPath(file, store, ct).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception fileException)
                    {
                        Logger.LogWarning(fileException, "Failed adding or updating {file}", file);
                    }
                }
            }
            finally
            {
                filesToUpdateOrCreate.Clear();
            }
        }

        void ProcessPenumbraFile(string file)
        {
            ct.ThrowIfCancellationRequested();
            WaitForScanResume(ct);

            if (!IsScannablePenumbraFile(file))
            {
                if (penumbraFileCaches.Remove(file, out var excludedFileCache))
                {
                    filesToRemove.Add(excludedFileCache);
                }

                return;
            }

            TotalFiles++;

            if (penumbraFileCaches.Remove(file, out var existingFileCache))
            {
                try
                {
                    var validationResult = _fileDbManager.ValidateFileCacheEntity(existingFileCache);
                    if (validationResult.State == FileState.RequireUpdate)
                    {
                        filesToUpdateOrCreate.Add(file);
                    }
                    if (validationResult.State == FileState.RequireDeletion)
                    {
                        filesToRemove.Add(existingFileCache);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed validating {path}", file);
                }
            }
            else
            {
                newFileCount++;
                filesToUpdateOrCreate.Add(file);
            }

            Interlocked.Increment(ref _currentFileProgress);
            if (filesToUpdateOrCreate.Count >= ScanBatchSize)
            {
                ProcessFileBatch(FileCacheStore.Penumbra);
            }
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(penumbraDir, "*.*", SearchOption.TopDirectoryOnly))
            {
                ProcessPenumbraFile(file);
            }

            foreach (var folder in Directory.EnumerateDirectories(penumbraDir))
            {
                ct.ThrowIfCancellationRequested();
                WaitForScanResume(ct);

                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                    {
                        ProcessPenumbraFile(file);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not enumerate path {path}", folder);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not enumerate path {path}", penumbraDir);
        }

        ProcessFileBatch(FileCacheStore.Penumbra);

        try
        {
            foreach (var file in Directory.EnumerateFiles(_configService.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                WaitForScanResume(ct);

                if (!IsCacheFileName(file))
                {
                    if (playerSyncFileCaches.Remove(file, out var excludedFileCache))
                    {
                        filesToRemove.Add(excludedFileCache);
                    }

                    continue;
                }

                TotalFiles++;

                if (playerSyncFileCaches.Remove(file, out var existingFileCache))
                {
                    try
                    {
                        var validationResult = _fileDbManager.ValidateFileCacheEntity(existingFileCache);
                        if (validationResult.State == FileState.RequireUpdate)
                        {
                            filesToUpdateOrCreate.Add(file);
                        }
                        if (validationResult.State == FileState.RequireDeletion)
                        {
                            filesToRemove.Add(existingFileCache);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed validating {path}", file);
                    }
                }
                else
                {
                    newFileCount++;
                    filesToUpdateOrCreate.Add(file);
                }

                Interlocked.Increment(ref _currentFileProgress);
                if (filesToUpdateOrCreate.Count >= ScanBatchSize)
                {
                    ProcessFileBatch(FileCacheStore.Cache);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not enumerate path {path}", _configService.Current.CacheFolder);
        }

        ProcessFileBatch(FileCacheStore.Cache);
        Logger.LogTrace("Scanner added or validated files and found {notScanned} new files", newFileCount);

        var remainingFileCaches = penumbraFileCaches.Values.Concat(playerSyncFileCaches.Values).ToList();
        TotalFiles += remainingFileCaches.Count;
        penumbraFileCaches.Clear();
        playerSyncFileCaches.Clear();
        Dictionary<FileCacheStore, List<string>> remainingFilesToUpdate = [];

        foreach (var fileCache in remainingFileCaches)
        {
            ct.ThrowIfCancellationRequested();
            WaitForScanResume(ct);

            try
            {
                var validationResult = _fileDbManager.ValidateFileCacheEntity(fileCache);

                if (validationResult.State == FileState.RequireUpdate)
                {
                    Logger.LogTrace("To update: {path}", validationResult.FileCache.ResolvedFilepath);

                    if (!remainingFilesToUpdate.TryGetValue(fileCache.Store, out var updatePaths))
                    {
                        updatePaths = [];
                        remainingFilesToUpdate[fileCache.Store] = updatePaths;
                    }

                    updatePaths.Add(validationResult.FileCache.ResolvedFilepath);
                }
                else if (validationResult.State == FileState.RequireDeletion)
                {
                    Logger.LogTrace("To delete: {path}", validationResult.FileCache.ResolvedFilepath);
                    filesToRemove.Add(validationResult.FileCache);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed validating {path}", fileCache.ResolvedFilepath);
            }

            Interlocked.Increment(ref _currentFileProgress);
        }

        remainingFileCaches.Clear();

        foreach (var storeGroup in remainingFilesToUpdate)
        {
            foreach (var updateBatch in storeGroup.Value.Chunk(ScanBatchSize))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    _fileDbManager.UpdateOrCreateFileCacheEntitiesByPathsAsync(updateBatch.ToList(), storeGroup.Key, ct).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed updating remaining files for store {store}", storeGroup.Key);
                }
            }
        }

        if (filesToRemove.Count > 0)
        {
            try
            {
                _fileDbManager.RemoveFileCacheEntities(filesToRemove, ct).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed removing missing file cache entries");
            }
        }

        Logger.LogTrace("Scanner validated existing db files");
        Logger.LogDebug("Scan complete");
        ct.ThrowIfCancellationRequested();

        if (!_configService.Current.InitialScanComplete)
        {
            _configService.Current.InitialScanComplete = true;
            _configService.Save();
            StartPlayerSyncWatcher(_configService.Current.CacheFolder);
        }
    }

    #endregion SCANNING

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        lock (_fswLock)
        {
            PenumbraWatcher?.Dispose();
            PlayerSyncWatcher?.Dispose();
            PenumbraWatcher = null;
            PlayerSyncWatcher = null;
        }
        lock (_scanStateLock)
        {
            _scanCancellationTokenSource.CancelDispose();
        }
        lock (_penumbraChanges)
        {
            _penumbraWatcherCancellationTokenSource.CancelDispose();
            _penumbraChanges.Clear();
        }
        lock (_playerSyncChanges)
        {
            _playerSyncWatcherCancellationTokenSource.CancelDispose();
            _playerSyncChanges.Clear();
        }
        _periodicCalculationTokenSource.CancelDispose();
        base.Dispose(disposing);
    }

    private static bool IsAllowedFile(string path)
    {
        return AllowedFileExtension.Contains(Path.GetExtension(path));
    }

    private static bool IsScannablePenumbraFile(string path)
    {
        var normalizedPath = path.Replace('/', '\\');

        return IsAllowedFile(path)
            && !normalizedPath.Contains(@"\bg\", StringComparison.OrdinalIgnoreCase)
            && !normalizedPath.Contains(@"\bgcommon\", StringComparison.OrdinalIgnoreCase)
            && !normalizedPath.Contains(@"\ui\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCacheFileName(string path)
    {
        var fileName = Path.GetFileName(path);

        return fileName.Length == 40 || (fileName.Length > 40 && fileName[40] == '.');
    }
}
