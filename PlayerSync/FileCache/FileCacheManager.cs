using K4os.Compression.LZ4.Legacy;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayerSync.FileCache;
using PlayerSync.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace MareSynchronos.FileCache;

public sealed class FileCacheManager : MediatorSubscriberBase, IHostedService, IDisposable
{
    private sealed record FileStoreRoots(string? Penumbra, string? Cache); // this is to provide faster full path resolution

    private const string LegacyCachePrefix = "{cache}";
    private const string LegacyPenumbraPrefix = "{penumbra}";

    // di
    private readonly MareConfigService _configService;
    private readonly FileCompactor _fileCompactor;
    private readonly IpcManager _ipcManager;
    private readonly ILogger<FileCacheManager> _logger;
    private readonly IDatabaseProvider<FileCacheDbContext> _db;

    private readonly FileCacheMemoryCache _memoryCache = new(3000000L, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(3000));
    private readonly SemaphoreSlim _databaseWriteSemaphore = new(1, 1);
    private readonly string _oldDataFile;
    private readonly HashSet<long> _dbFilesToUpdateTicks = new HashSet<long>();
    private readonly object _dbFilesToUpdateTicksLock = new();

    private CancellationTokenSource? _startupCancellationTokenSource;
    private CancellationTokenSource? _dbFilesToUpdateTicksTokenSource;
    private CancellationTokenSource? _dbLifetimeCts;
    private Task? _startupTask;
    private Task? _dbFilesToUpdateTicksTask;
    private FileStoreRoots _fileStoreRoots = new(null, null);
    private DateTimeOffset _lastMemCacheUpdateTime = DateTimeOffset.MinValue;


    public FileCacheManager(ILogger<FileCacheManager> logger, IpcManager ipcManager, MareConfigService configService,
        MareMediator mediator, FileCompactor fileCompactor, IDatabaseProvider<FileCacheDbContext> databaseProvider) : base(logger, mediator)
    {
        _logger = logger;
        _ipcManager = ipcManager;
        _configService = configService;
        _fileCompactor = fileCompactor;
        _db = databaseProvider;
        _oldDataFile = Path.Combine(configService.ConfigurationDirectory, "FileCache.csv");

        RefreshFileStoreRoots(); // TODO: The UI Shared Service must call this right before .Save()

        Mediator.Subscribe<PenumbraDirectoryChangedMessage>(this, (_) => RefreshFileStoreRoots());
        Mediator.Subscribe<DisconnectedMessage>(this, (__) => _ = FlushFileCacheTicksAsync(CancellationToken.None));
    }

    public string CacheFolder => _configService.Current.CacheFolder;
    public (bool IsReady, DatabaseState State) DatabaseStatus { get; private set; } = (false, DatabaseState.Starting);
    public bool IsCacheReady { get; private set; } = false;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        DatabaseStatus = (false, DatabaseState.Starting);
        _logger.LogInformation("Starting FileCacheManager");

        await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _db.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _databaseWriteSemaphore.Release();
        }

        _startupCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Must run this on a different threae to not block Dalamud init
        _startupTask = Task.Run(() => RunStartupAsync(_startupCancellationTokenSource.Token), CancellationToken.None);

        _dbFilesToUpdateTicksTokenSource = _dbFilesToUpdateTicksTokenSource.CancelRecreate();
        _dbFilesToUpdateTicksTask = UpdateFileCacheTicks(_dbFilesToUpdateTicksTokenSource.Token);

        _dbLifetimeCts = _dbLifetimeCts.CancelRecreate();

        _logger.LogInformation("Started FileCacheManager startup sequence");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        DatabaseStatus = (false, DatabaseState.Stopping);

        // if you know of an easier/complete shut down sequence that won't throw dispose exceptions, let me know

        var startupCancellationTokenSource = _startupCancellationTokenSource;
        var dbFilesToUpdateTicksTokenSource = _dbFilesToUpdateTicksTokenSource;
        var dbLifetimeCts = _dbLifetimeCts;
        var startupTask = _startupTask;
        var dbFilesToUpdateTicksTask = _dbFilesToUpdateTicksTask;

        await Task.WhenAll(
            startupCancellationTokenSource?.CancelAsync() ?? Task.CompletedTask,
            dbFilesToUpdateTicksTokenSource?.CancelAsync() ?? Task.CompletedTask,
            dbLifetimeCts?.CancelAsync() ?? Task.CompletedTask)
            .WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(false);

        List<Task> backgroundTasks = [];

        if (startupTask != null)
        {
            backgroundTasks.Add(startupTask);
        }

        if (dbFilesToUpdateTicksTask != null)
        {
            backgroundTasks.Add(dbFilesToUpdateTicksTask);
        }

        var backgroundTaskCompletion = Task.WhenAll(backgroundTasks);

        try
        {
            await backgroundTaskCompletion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (backgroundTaskCompletion.IsCanceled)
        {
            //
        }
        catch (Exception ex)
        {
            _logger.LogWarning(backgroundTaskCompletion.Exception ?? ex, "[DATABASE] One or more background tasks failed while stopping");
        }

        try
        {
            await FlushFileCacheTicksAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DATABASE] Could not flush pending file access updates while stopping");
        }

        await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _db.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _databaseWriteSemaphore.Release();
        }

        _memoryCache.Clear();

        startupCancellationTokenSource?.Dispose();
        dbFilesToUpdateTicksTokenSource?.Dispose();
        dbLifetimeCts?.Dispose();

        _startupCancellationTokenSource = null;
        _dbFilesToUpdateTicksTokenSource = null;
        _dbLifetimeCts = null;
        _startupTask = null;
        _dbFilesToUpdateTicksTask = null;

        DatabaseStatus = (false, DatabaseState.Stopped);
    }

    public void RefreshFileStoreRoots()
    {
        var roots = new FileStoreRoots(NormalizeFileStoreRoot(_ipcManager.Penumbra.ModDirectory), NormalizeFileStoreRoot(_configService.Current.CacheFolder));

        // we need to check this so we're not nuking the mem cache every time
        if (roots == _fileStoreRoots)
        {
            return;
        }

        _fileStoreRoots = roots;

        _memoryCache.Clear();
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
    }

    #region PUBLIC METHODS

    /// <summary>
    /// Get all FileCacheEntity from the database. Note this can be expensive!
    /// Only use this when needed and not on a Framework thread.
    /// </summary>
    /// <returns>A list of all FileCacheEntity present in the database</returns>
    public async Task<List<FileCacheEntity>> GetAllFileCacheEntitiesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = _db.CreateDbContext();
        var fileCaches = await db.FileCaches.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var fileCache in fileCaches)
        {
            // I hate this, we should figure out a better way of dealing with the "dynamic" store location
            fileCache.SetResolvedFilePath(ResolveFilePathFromFileStore(fileCache.Store, fileCache.FilePathWithinFileStore));
        }

        return fileCaches;
    }

    /// <summary>
    /// This is used for file scans and directory checks, do not use it for regular lookups!
    /// This method includes no memory caching.
    /// </summary>
    public async Task<List<FileCacheEntity>> GetAllNonMemoryFrontedNonValidatedFileCacheEntitiesByPath(string path, FileCacheStore store, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var filePathWithinFileStore = GetFilePathWithinFileStore(path, store);
        if (filePathWithinFileStore == null)
        {
            return [];
        }

        await using var db = _db.CreateDbContext();
        var fileCaches = await db.FileCaches
            .AsNoTracking()
            .Where(fileCache => fileCache.Store == store && (fileCache.FilePathWithinFileStore == filePathWithinFileStore)) // this is an indexed search
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var fileCache in fileCaches)
        {
            fileCache.SetResolvedFilePath(ResolveFilePathFromFileStore(fileCache.Store, fileCache.FilePathWithinFileStore));
        }

        return fileCaches;
    }

    /// <summary>
    /// Gets the first validated FileCacheEntity from the database.
    /// The validation ensures the file exists on disk, else it is not valid for return.
    /// </summary>
    /// <param name="hash">The hash of the file</param>
    /// <returns>The first valid FileCacheEntity, or null if there are none</returns>
    public FileCacheEntity? GetFirstFileCacheEntityByHash(string hash, bool validate = true, bool cacheResults = true)
    {
        return GetFirstFileCacheEntityByHash(hash, _dbLifetimeCts.Token, validate, cacheResults).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="hash"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    //public async Task<FileCacheEntity?> GetFirstFileCacheEntityByHash(string hash, CancellationToken cancellationToken, bool validate = true, bool cacheResults = true)
    //{
    //    var fileCaches = await GetFirstFileCacheEntitiesByHashes([hash], cancellationToken, validate, cacheResults).ConfigureAwait(false);

    //    return fileCaches.TryGetValue(hash, out var fileCache) ? fileCache : null;
    //}
    public async Task<FileCacheEntity?> GetFirstFileCacheEntityByHash(string hash, CancellationToken cancellationToken, bool validate = true, bool cacheResults = true)
    {
        hash = StringUtils.NormalizeHash(hash);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _dbLifetimeCts.Token);
        var linkedToken = linkedCts.Token;
        var fileCaches = await GetAllMemoryFrontedNonVerifiedFileCacheEntitiesByHashesInternal([hash], cacheResults, linkedToken).ConfigureAwait(false);
        if (!fileCaches.TryGetValue(hash, out var matchingFileCaches))
        {
            return null;
        }

        FileCacheEntity? fileCache = null;
        foreach (var matchingFileCache in matchingFileCaches)
        {
            linkedToken.ThrowIfCancellationRequested();
            var validatedFileCache = validate ? await ValidateFileCacheAsync(matchingFileCache, linkedToken).ConfigureAwait(false) : matchingFileCache;
            fileCache ??= validatedFileCache;
        }
        AddToFileCacheAccessUpdates(fileCache);
        LogMemoryCacheUsage();

        return fileCache;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="hashes"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<Dictionary<string, FileCacheEntity?>> GetFirstFileCacheEntitiesByHashes(IEnumerable<string> hashes, CancellationToken cancellationToken, bool validate = true, bool cacheResults = true)
    {
        ArgumentNullException.ThrowIfNull(hashes);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _dbLifetimeCts.Token);
        var linkedToken = linkedCts.Token;

        var fileCachesByHash = await GetAllMemoryFrontedNonVerifiedFileCacheEntitiesByHashesInternal(hashes, cacheResults, linkedToken).ConfigureAwait(false);       

        if (fileCachesByHash.Count == 0)
        {
            return [];
        }

        LogMemoryCacheUsage();

        if (validate)
        {
            await ValidateFileCacheEntitiesAsync(fileCachesByHash, linkedToken).ConfigureAwait(false);
        }

        var result = fileCachesByHash.ToDictionary(fileCache => fileCache.Key, fileCache => fileCache.Value.FirstOrDefault());
        AddToFileCacheAccessUpdates(result.Values);
        
        return result;
    }

    // If you are using this in a loop, consider refactoring to use GetAllFileCacheEntitiesByHashes instead
    public async Task<List<FileCacheEntity>> GetAllFileCacheEntitiesByHash(string hash, CancellationToken cancellationToken, bool cacheResults = true)
    {
        var fileCachesByHash = await GetAllMemoryFrontedNonVerifiedFileCacheEntitiesByHashesInternal([hash], cacheResults, cancellationToken).ConfigureAwait(false);

        return fileCachesByHash[hash];
    }

    public async Task<Dictionary<string, List<FileCacheEntity>>> GetAllFileCacheEntitiesByHashes(IEnumerable<string> hashes, CancellationToken cancellationToken, bool cacheResults = true)
    {
        var fileCachesByHash = await GetAllMemoryFrontedNonVerifiedFileCacheEntitiesByHashesInternal(hashes, cacheResults, cancellationToken).ConfigureAwait(false);

        LogMemoryCacheUsage();

        return fileCachesByHash;
    }

    // This is a wild series of checks for player cache creation service
    public async Task<Dictionary<string, FileCacheEntity?>> GetOrCreateValidatedFileCachesByPathsAsync(string[] paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Dictionary<string, FileCacheEntity?> result = new(StringComparer.OrdinalIgnoreCase);
        if (paths.Length == 0)
        {
            return result;
        }

        var cacheRoot = StringUtils.NormalizePath(_configService.Current.CacheFolder).TrimEnd('\\') + "\\";
        var penumbraRoot = string.IsNullOrWhiteSpace(_ipcManager.Penumbra.ModDirectory) ? null : StringUtils.NormalizePath(_ipcManager.Penumbra.ModDirectory).TrimEnd('\\') + "\\";
        List<(string OriginalPath, (FileCacheStore Store, string FilePathWithinFileStore)? StorePath)> distinctPaths = [];

        foreach (var originalPath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalizedPath = StringUtils.NormalizePath(originalPath);
            var isCachePath = normalizedPath.StartsWith(cacheRoot, StringComparison.Ordinal);
            var isPenumbraPath = penumbraRoot != null && normalizedPath.StartsWith(penumbraRoot, StringComparison.Ordinal);
            (FileCacheStore Store, string FilePathWithinFileStore)? storePath = null;

            if (isCachePath && (!isPenumbraPath || cacheRoot.Length >= penumbraRoot!.Length))
            {
                storePath = (FileCacheStore.Cache, normalizedPath[cacheRoot.Length..]);
            }
            else if (isPenumbraPath)
            {
                storePath = (FileCacheStore.Penumbra, normalizedPath[penumbraRoot!.Length..]);
            }

            distinctPaths.Add((originalPath, storePath));
        }

        var storePaths = distinctPaths.Where(path => path.StorePath.HasValue).Select(path => path.StorePath.GetValueOrDefault()).Distinct().ToList();

        Dictionary<(FileCacheStore Store, string FilePathWithinFileStore), FileCacheEntity> existingFileCaches = [];
        Dictionary<(FileCacheStore Store, string FilePathWithinFileStore), long> memCacheVersions = [];
        List<(FileCacheStore Store, string FilePathWithinFileStore)> cachesToCheck = [];

        foreach (var storePath in storePaths)
        {
            if (_memoryCache.TryGetFileCacheByPath(storePath.Store, storePath.FilePathWithinFileStore, out var fileCache) && fileCache != null)
            {
                existingFileCaches[(storePath.Store, storePath.FilePathWithinFileStore)] = fileCache;
            }
            else
            {
                cachesToCheck.Add(storePath);
            }
        }

        if (cachesToCheck.Count > 0)
        {
            await using var db = _db.CreateDbContext();

            foreach (var cacheToCheck in cachesToCheck.GroupBy(path => path.Store))
            {
                var store = cacheToCheck.Key;

                // we shouldn't need to chunk this as I don't think any player has 500 mod files at once?
                // but the overall methedology here is to ensure every db operation is bound by someone sane limits
                // future self here: people can have well over 900 mod files, into the thousands
                foreach (var cacheBatch in cacheToCheck.Select(path => path.FilePathWithinFileStore).Chunk(500))
                {
                    var batch = cacheBatch.ToArray();
                    var cacheVersion = _memoryCache.GetVersion();
                    var entities = await db.FileCaches
                        .AsNoTracking()
                        .Where(fileCache => fileCache.Store == store && batch
                        .Contains(fileCache.FilePathWithinFileStore))
                        .ToListAsync(cancellationToken).ConfigureAwait(false);

                    foreach (var entity in entities)
                    {
                        entity.SetResolvedFilePath(ResolveFilePathFromFileStore(entity.Store, entity.FilePathWithinFileStore));

                        var key = (entity.Store, entity.FilePathWithinFileStore);
                        existingFileCaches[key] = entity;
                        memCacheVersions[key] = cacheVersion;
                    }
                }
            }
        }

        List<(string OriginalPath, FileCacheStore Store, string FilePathWithinFileStore)> entriesToCreate = [];

        foreach (var path in distinctPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!path.StorePath.HasValue)
            {
                result[path.OriginalPath] = null;

                continue;
            }

            var storePath = path.StorePath.Value;
            var key = (storePath.Store, storePath.FilePathWithinFileStore);

            if (existingFileCaches.TryGetValue(key, out var entity))
            {
                var validated = await ValidateFileCacheAsync(entity, cancellationToken).ConfigureAwait(false);
                result[path.OriginalPath] = validated;

                if (validated != null && memCacheVersions.TryGetValue(key, out var cacheVersion))
                {
                    _memoryCache.SetFileCacheByPath(validated, cacheVersion);
                }

                continue;
            }

            entriesToCreate.Add((path.OriginalPath, storePath.Store, storePath.FilePathWithinFileStore));
        }

        foreach (var storeGroup in entriesToCreate.GroupBy(entry => entry.Store))
        {
            var createdEntities = await CreateFileCacheEntities(storeGroup.Select(entry => entry.OriginalPath), storeGroup.Key, cancellationToken).ConfigureAwait(false);
            var createdEntitiesByPath = createdEntities.ToDictionary(entity => entity.FilePathWithinFileStore, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in storeGroup)
            {
                if (createdEntitiesByPath.TryGetValue(entry.FilePathWithinFileStore, out var createdEntity))
                {
                    result[entry.OriginalPath] = createdEntity;
                }
                else
                {
                    result[entry.OriginalPath] = await GetFileCacheEntityByPath(entry.FilePathWithinFileStore, entry.Store, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        LogMemoryCacheUsage();

        return result;
    }

    // Used for directory services for the FSW
    public async Task<List<FileCacheEntity>> GetAllFileCacheEntitiesUnderPath(string path, FileCacheStore store, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var filePathWithinFileStore = GetFilePathWithinFileStore(path, store);
        if (filePathWithinFileStore == null)
        {
            return [];
        }

        var pathPrefix = filePathWithinFileStore.TrimEnd('\\') + "\\";

        await using var db = _db.CreateDbContext();
        return await db.FileCaches
            .AsNoTracking()
            .Where(fileCache => fileCache.Store == store && (fileCache.FilePathWithinFileStore == filePathWithinFileStore 
            || fileCache.FilePathWithinFileStore.StartsWith(pathPrefix)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// This is used for file scans and file downloads, do not use it for player/pair mod lookups!
    /// This method does not use memory caching.
    /// </summary>
    public async Task<FileCacheEntity?> UpdateOrCreateFileCacheEntityByPath(string path, FileCacheStore store, CancellationToken cancellationToken = default, bool forceUpdate = false, bool markAccessed = false, string? verifiedHash = null)
    {
        if (verifiedHash != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return null;
            }
            var filePathWithinFileStore = GetFilePathWithinFileStore(fileInfo.FullName, store);
            if (filePathWithinFileStore == null)
            {
                return null;
            }
            var fileCache = new FileCacheEntity(verifiedHash, filePathWithinFileStore, store, markAccessed && store == FileCacheStore.Cache ? DateTime.UtcNow.Ticks : fileInfo.LastWriteTimeUtc.Ticks, fileInfo.LastWriteTimeUtc.Ticks, fileInfo.Length);
            fileCache.SetResolvedFilePath(fileInfo.FullName);
            var updatedFileCaches = await UpdateOrCreateFileCacheEntitiesAsync([fileCache], store, cancellationToken, markAccessed).ConfigureAwait(false);

            return updatedFileCaches.FirstOrDefault();
        }

        var fileCaches = await UpdateOrCreateFileCacheEntitiesByPathsAsync([path], store, cancellationToken, forceUpdate, markAccessed).ConfigureAwait(false);

        return fileCaches.FirstOrDefault();
    }

    /// <summary>
    /// This is used for file scans and file downloads, do not use it for player/pair mod lookups!
    /// This method does not use memory caching.
    /// </summary>
    public async Task<List<FileCacheEntity>> UpdateOrCreateFileCacheEntitiesByPathsAsync(List<string> paths, FileCacheStore store, CancellationToken cancellationToken = default, bool forceUpdate = false, bool markAccessed = false)
    {
        ArgumentNullException.ThrowIfNull(paths);

        ConcurrentDictionary<string, (string FullPath, long LastModifiedDateTicks, long Size)> fileCachesToCheck = [];

        void CheckPath(string path)
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return;
            }

            var filePathWithinFileStore = GetFilePathWithinFileStore(fileInfo.FullName, store);
            if (filePathWithinFileStore == null)
            {
                return;
            }

            fileCachesToCheck[filePathWithinFileStore] = (fileInfo.FullName, fileInfo.LastWriteTimeUtc.Ticks, fileInfo.Length);
        }

        if (paths.Count > 32)
        {
            Parallel.ForEach(
                paths,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 4
                }, CheckPath);
        }
        else
        {
            foreach (var path in paths)
            {
                CheckPath(path);
            }
        }

        if (fileCachesToCheck.IsEmpty)
        {
            return [];
        }

        Dictionary<string, FileCacheEntity> fileCachesByPath = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> pathsToUpdate = new(fileCachesToCheck.Keys, StringComparer.OrdinalIgnoreCase);

        if (!forceUpdate)
        {
            await using var db = _db.CreateDbContext();

            foreach (var pathBatch in fileCachesToCheck.Keys.Chunk(500)) // keep it something sane
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = pathBatch.ToArray();
                var existingFileCaches = await db.FileCaches
                    .AsNoTracking()
                    .Where(fileCache => fileCache.Store == store && batch
                    .Contains(fileCache.FilePathWithinFileStore))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                foreach (var existingFileCache in existingFileCaches)
                {
                    var fileCache = fileCachesToCheck[existingFileCache.FilePathWithinFileStore];
                    if (existingFileCache.LastModifiedDateTicks != fileCache.LastModifiedDateTicks || existingFileCache.Size != fileCache.Size)
                    {
                        continue;
                    }

                    existingFileCache.SetResolvedFilePath(fileCache.FullPath);
                    fileCachesByPath[existingFileCache.FilePathWithinFileStore] = existingFileCache;
                    pathsToUpdate.Remove(existingFileCache.FilePathWithinFileStore);
                }
            }
        }

        var updatePaths = pathsToUpdate.ToArray();
        ConcurrentBag<FileCacheEntity> fileCachesToUpdate = [];

        void CreateFileCache(string filePathWithinFileStore)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileCache = fileCachesToCheck[filePathWithinFileStore];

            try
            {
                var hash = StringUtils.NormalizeHash(Crypto.GetFileHash(fileCache.FullPath));

                var entity = new FileCacheEntity(
                    hash,
                    filePathWithinFileStore,
                    store,
                    markAccessed && store == FileCacheStore.Cache ? DateTime.UtcNow.Ticks : fileCache.LastModifiedDateTicks,
                    fileCache.LastModifiedDateTicks,
                    fileCache.Size);

                entity.SetResolvedFilePath(fileCache.FullPath);
                fileCachesToUpdate.Add(entity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DATABASE] Failed to create cache entry for {path} in store {store}", fileCache.FullPath, store);
            }
        }

        if (updatePaths.Length > 4)
        {
            Parallel.ForEach(
                updatePaths,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 4
                },
                CreateFileCache);
        }
        else
        {
            foreach (var path in updatePaths)
            {
                CreateFileCache(path);
            }
        }

        var createdFileCaches = fileCachesToUpdate.ToList();

        foreach (var fileCache in createdFileCaches)
        {
            _logger.LogTrace("[DATABASE] Updating or creating cache entry for {path} in store {store}", fileCache.ResolvedFilepath, store);
        }

        var updatedFileCaches = await UpdateOrCreateFileCacheEntitiesAsync(createdFileCaches, store, cancellationToken, markAccessed).ConfigureAwait(false);

        foreach (var fileCache in updatedFileCaches)
        {
            fileCachesByPath[fileCache.FilePathWithinFileStore] = fileCache;
        }

        foreach (var fileCache in updatedFileCaches)
        {
            _logger.LogTrace("[DATABASE] Updating or creating cache entity for {name} success: {success}", fileCache.ResolvedFilepath, true);
        }

        return fileCachesByPath.Values.ToList();
    }

    /// <summary>
    /// Deletes a FileCacheEntity from the database.
    /// </summary>
    /// <param name="fileCache">The FileCacheEntity to be removed.</param>
    /// <returns>True if the FileCacheEntity was deleted, else false.</returns>
    public async Task<bool> RemoveFileCacheEntity(FileCacheEntity fileCache, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileCache);

        var removedEntries = await RemoveFileCacheEntitiesInternalAsync([(fileCache.Hash, fileCache.FilePathWithinFileStore, fileCache.Store)], cancellationToken).ConfigureAwait(false);

        return removedEntries > 0;
    }

    /// <summary>
    /// Deletes a list FileCacheEntity from the database.
    /// </summary>
    /// <param name="fileCaches">The list of FileCacheEntity to remove</param>
    /// <returns>The number of FileCacheEntity removed from the database</returns>
    public async Task<int> RemoveFileCacheEntities(List<FileCacheEntity> fileCaches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileCaches);

        var entries = fileCaches.Select(fileCache => (fileCache.Hash, fileCache.FilePathWithinFileStore, fileCache.Store)).ToList();

        return await RemoveFileCacheEntitiesInternalAsync(entries, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a FileCacheEntity in the database with a provided FileCacheEntity and optionally recomputes the properties.
    /// Since a calling method can create their own FileCacheEntity outside of the database context, we need to validate the inputs.
    /// This is a legacy carry over and should be reworked/removed.
    /// </summary>
    /// <param name="fileCache">The FileCacheEntity to update</param>
    /// <param name="computeProperties">Whether or not to compute fields such as file hash, modified, and size</param>
    /// <returns>The updated FileCacheEntity</returns>
    public async Task<FileCacheEntity?> UpdateFileCache(FileCacheEntity fileCache, bool computeProperties = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileCache);

        fileCache.Hash = StringUtils.NormalizeHash(fileCache.Hash);
        fileCache.FilePathWithinFileStore = StringUtils.NormalizeFilePathWithinFileStore(fileCache.FilePathWithinFileStore);
        fileCache.SetResolvedFilePath(ResolveFilePathFromFileStore(fileCache.Store, fileCache.FilePathWithinFileStore));

        if (computeProperties)
        {
            var fileInfo = new FileInfo(fileCache.ResolvedFilepath);
            fileCache.Hash = StringUtils.NormalizeHash(Crypto.GetFileHash(fileCache.ResolvedFilepath));
            fileCache.LastModifiedDateTicks = fileInfo.LastWriteTimeUtc.Ticks;
            fileCache.Size = fileInfo.Length;
            fileCache.CompressedSize = -1;
        }

        _logger.LogTrace("[DATABASE] Updating file cache entry for {path}", fileCache.ResolvedFilepath);

        await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var db = _db.CreateDbContext();
            FileCacheEntity? existing;

            existing = await db.FileCaches.SingleOrDefaultAsync(entry => entry.Id == fileCache.Id, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                return null;
            }

            var existingFileCacheToInvalidate = (existing.Hash, existing.Store, existing.FilePathWithinFileStore);

            existing.Hash = fileCache.Hash;
            existing.FilePathWithinFileStore = fileCache.FilePathWithinFileStore;
            existing.LastModifiedDateTicks = fileCache.LastModifiedDateTicks;
            existing.Size = fileCache.Size;
            existing.CompressedSize = fileCache.CompressedSize;
            // not changing file store

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            existing.SetResolvedFilePath(ResolveFilePathFromFileStore(existing.Store, existing.FilePathWithinFileStore));
            _memoryCache.Invalidate(existingFileCacheToInvalidate.Hash, existingFileCacheToInvalidate.Store, existingFileCacheToInvalidate.FilePathWithinFileStore);
            _memoryCache.Invalidate(existing);

            return existing;
        }
        finally
        {
            _databaseWriteSemaphore.Release();
        }
    }

    /// <summary>
    /// Checks if a FileCacheEntity is valid/exists on disk, or needs to be updated
    /// </summary>
    /// <param name="fileCache">The FileCacheEntity to check</param>
    /// <returns>The valid/updated FileCacheEntity, or null if not valid</returns>
    public async Task<FileCacheEntity?> ValidateFileCacheAsync(FileCacheEntity fileCache, CancellationToken cancellationToken)
    {
        var validationResult = ValidateFileCacheEntity(fileCache);

        if (validationResult.State == FileState.RequireDeletion)
        {
            await RemoveFileCacheEntity(fileCache, cancellationToken).ConfigureAwait(false);

            return null;
        }

        if (validationResult.State == FileState.RequireUpdate)
        {
            return await UpdateOrCreateFileCacheEntityByPath(fileCache.ResolvedFilepath, fileCache.Store, cancellationToken).ConfigureAwait(false);
        }

        return fileCache;
    }

    /// <summary>
    /// Checks that all files in the local cache exists on disk and that their hash matches what we expect (not modified/swapped by something else)
    /// This does NOT check if the file type/data is correct.
    /// </summary>
    public async Task<List<FileCacheEntity>> ValidateLocalIntegrity(IProgress<(int, int, FileCacheEntity)> progress, CancellationToken cancellationToken)
    {
        Mediator.Publish(new HaltScanMessage(nameof(ValidateLocalIntegrity)));
        List<FileCacheEntity> brokenEntities = [];

        try
        {
            _logger.LogInformation("[DATABASE] Validating local storage");

            List<FileCacheEntity> cacheEntries;
            await using (var db = _db.CreateDbContext())
            {
                cacheEntries = await db.FileCaches
                    .AsNoTracking()
                    .Where(fileCache => fileCache.Store == FileCacheStore.Cache)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            int i = 0;
            foreach (var fileCache in cacheEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                fileCache.SetResolvedFilePath(ResolveFilePathFromFileStore(fileCache.Store, fileCache.FilePathWithinFileStore));
                _logger.LogTrace("[DATABASE] Validating {file}", fileCache.ResolvedFilepath);
                progress.Report((i, cacheEntries.Count, fileCache));
                i++;

                if (!File.Exists(fileCache.ResolvedFilepath))
                {
                    brokenEntities.Add(fileCache);

                    continue;
                }

                try
                {
                    var computedHash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
                    if (!string.Equals(computedHash, fileCache.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("[DATABASE] Failed to validate {file}, got hash {got}, expected hash {expected}", 
                            fileCache.ResolvedFilepath, computedHash, fileCache.Hash);
                        brokenEntities.Add(fileCache);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "[DATABASE] Error during validation of {file}", fileCache.ResolvedFilepath);
                    brokenEntities.Add(fileCache);
                }
            }

            List<FileCacheEntity> removedEntities = [];

            if (brokenEntities.Count > 0)
            {
                await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await using var db = _db.CreateDbContext();

                    foreach (var brokenEntityBatch in brokenEntities.Chunk(500)) // we're batching for performance reasons/long running querries 
                    {
                        try
                        {
                            var ids = brokenEntityBatch.Select(fileCache => fileCache.Id).ToArray();
                            await db.FileCaches
                                .Where(fileCache => ids
                                .Contains(fileCache.Id))
                                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                            removedEntities.AddRange(brokenEntityBatch);

                            foreach (var brokenEntity in brokenEntityBatch)
                            {
                                _memoryCache.Invalidate(brokenEntity);
                                _logger.LogTrace("[DATABASE] Removed broken file with hash {hash} and path {path}", brokenEntity.Hash, brokenEntity.FilePathWithinFileStore);
                            }
                        }
                        catch (Exception ex)
                        {
                            foreach (var brokenEntity in brokenEntityBatch)
                            {
                                _logger.LogWarning(ex, "Failed to remove file cache entry for {hash} {path}", brokenEntity.Hash, brokenEntity.FilePathWithinFileStore);
                            }
                        }
                    }
                }
                finally
                {
                    _databaseWriteSemaphore.Release();
                }
            }

            foreach (var brokenEntity in removedEntities)
            {
                try
                {
                    File.Delete(brokenEntity.ResolvedFilepath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete {file}", brokenEntity.ResolvedFilepath);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("[DATABASE] {method} cancelled", nameof(ValidateLocalIntegrity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DATABASE] {method} failed", nameof(ValidateLocalIntegrity));
        }
        finally
        {
            Mediator.Publish(new ResumeScanMessage(nameof(ValidateLocalIntegrity)));
        }

        return brokenEntities;
    }

    /// <summary>
    /// This is used by the file storage scanner if we are exceeding our storage quota.
    /// This method will delete files from disk, oldest accessed first, up to the bytesToReclaim byte count.
    /// </summary>
    public async Task<(long ReclaimedBytes, int DeletedFiles)> ReclaimLocalCacheStorageAsync(long bytesToReclaim, CancellationToken cancellationToken = default)
    {
        if (bytesToReclaim <= 0)
        {
            return (0, 0);
        }

        const int batchSize = 250; // how many files we're checking at a time, keep it sane
        long reclaimedBytes = 0;
        long lastAccessedDateTicks = -1;
        long lastId = 0;
        int deletedFiles = 0;

        while (reclaimedBytes < bytesToReclaim)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await using var db = _db.CreateDbContext();
                var fileCaches = await db.FileCaches
                    .AsNoTracking()
                    .Where(fileCache => fileCache.Store == FileCacheStore.Cache)
                    .Where(fileCache => // this is effectivley a pagination strategy, honestly probably overkill but the user could drop their storage by a LOT
                        fileCache.LastAccessedDateTicks > lastAccessedDateTicks 
                        || (fileCache.LastAccessedDateTicks == lastAccessedDateTicks 
                        && fileCache.Id > lastId))
                    .OrderBy(fileCache => fileCache.LastAccessedDateTicks)
                    .ThenBy(fileCache => fileCache.Id)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (fileCaches.Count == 0)
                {
                    break;
                }

                List<FileCacheEntity> fileCachesToRemove = [];

                foreach (var fileCache in fileCaches)
                {
                    // update the pagination values
                    cancellationToken.ThrowIfCancellationRequested();
                    lastAccessedDateTicks = fileCache.LastAccessedDateTicks;
                    lastId = fileCache.Id;

                    lock (_dbFilesToUpdateTicksLock)
                    {
                        // don't remove something if we're about to update the tick time on it
                        if (_dbFilesToUpdateTicks.Contains(fileCache.Id))
                        {
                            continue;
                        }
                    }

                    string filePath;
                    try
                    {
                        filePath = ResolveFilePathFromFileStore(fileCache.Store, fileCache.FilePathWithinFileStore);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not resolve cache file {path} for storage cleanup", fileCache.FilePathWithinFileStore);

                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists)
                    {
                        // if the file somehow doesn't exist on disk, may as well remove it from the db
                        fileCachesToRemove.Add(fileCache);

                        continue;
                    }

                    long fileSizeOnDisk;
                    try
                    {
                        // we could blindly trust what's in the db for Size, but this is safer
                        fileSizeOnDisk = _fileCompactor.GetFileSizeOnDisk(fileInfo);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not determine the disk size of {path}, skipping it during storage cleanup", filePath);

                        continue;
                    }

                    try
                    {
                        lock (_dbFilesToUpdateTicksLock)
                        {
                            if (_dbFilesToUpdateTicks.Contains(fileCache.Id))
                            {
                                continue;
                            }

                            _logger.LogTrace("Deleting cache file {path} using {size} bytes on disk", filePath, fileSizeOnDisk);
                            File.Delete(filePath);
                        }

                        fileCachesToRemove.Add(fileCache);
                        reclaimedBytes += fileSizeOnDisk;
                        deletedFiles++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete cache file {path}", filePath);
                    }

                    // do the check of how much storage we've freed up
                    // don't keep deleting files just because we batched them, only delete up until what is needed
                    // we should always land within the file size of the most recently deleted file, so 4K-40MB or so, unless it's a massive .scd...
                    if (reclaimedBytes >= bytesToReclaim)
                    {
                        break;
                    }
                }

                // this will be the files that either didn't exist or were deleted during the cleanup
                if (fileCachesToRemove.Count > 0)
                {
                    var fileCacheIds = fileCachesToRemove.Select(fileCache => fileCache.Id).ToArray();
                    await db.FileCaches
                        .Where(fileCache => fileCacheIds
                        .Contains(fileCache.Id))
                        .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

                    lock (_dbFilesToUpdateTicksLock)
                    {
                        // if there were races between operations, remove these since they're already gone
                        _dbFilesToUpdateTicks.ExceptWith(fileCacheIds);
                    }

                    foreach (var fileCache in fileCachesToRemove)
                    {
                        // doubtful old files were in the mem cache, but remove them anyway
                        // probably useful when users have their storage set really low
                        _memoryCache.Invalidate(fileCache.Hash, fileCache.Store, fileCache.FilePathWithinFileStore);
                    }
                }
            }
            finally
            {
                _databaseWriteSemaphore.Release();
            }
        }

        _logger.LogInformation("[DATABASE] Storage cleanup deleted {count} files and reclaimed approximately {reclaimedBytes} of the requested {requestedBytes}",
            deletedFiles, reclaimedBytes.ToByteString(), bytesToReclaim.ToByteString());

        return (reclaimedBytes, deletedFiles);
    }

    /// <summary>
    /// This will nuke the PlayerSync local cache folder contents, then wipe the database and clear the mem cache.
    /// This is super risky and should probably contain sanity checks that we don't try and nuke the C: drive or something
    /// </summary>
    public async Task ClearLocalStorageAsync(CancellationToken cancellationToken = default)
    {
        Mediator.Publish(new HaltScanMessage(nameof(ClearLocalStorageAsync)));

        var hasEnteredSemaphore = false;
        var deletedFiles = 0;
        var failedFiles = 0;
        var deletedEntries = 0;

        try
        {
            await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            hasEnteredSemaphore = true;

            var cacheFolder = _configService.Current.CacheFolder;

            if (!string.IsNullOrWhiteSpace(cacheFolder) && Directory.Exists(cacheFolder))
            {
                await Task.Run(() =>
                {
                    foreach (var file in Directory.EnumerateFiles(cacheFolder))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            File.Delete(file);
                            deletedFiles++;
                        }
                        catch (Exception ex)
                        {
                            failedFiles++;
                            _logger.LogWarning(ex, "Failed to delete cache file {file}", file);
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);
            }

            await using var db = _db.CreateDbContext();
            deletedEntries = await db.FileCaches
                .Where(fileCache => fileCache.Store == FileCacheStore.Cache)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            _memoryCache.Clear();

            _logger.LogInformation("[DATABASE] Cleared local storage: deleted {files} files, failed to delete {failedFiles} files and removed {entries} database entries", 
                deletedFiles, failedFiles, deletedEntries);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("[DATABASE] Clearing local storage was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DATABASE] Failed to clear local storage");
        }
        finally
        {
            _memoryCache.Clear();

            if (hasEnteredSemaphore)
            {
                _databaseWriteSemaphore.Release();
            }

            Mediator.Publish(new ResumeScanMessage(nameof(ClearLocalStorageAsync)));
        }
    }

    /// <summary>
    /// Checks if a file cache file exists on disk or not and if has file changes
    /// </summary>
    /// <param name="fileCache">The FileCacheEntity to check</param>
    /// <returns>A tuple of the file state and the file cache entry</returns>
    public (FileState State, FileCacheEntity FileCache) ValidateFileCacheEntity(FileCacheEntity fileCache)
    {
        ArgumentNullException.ThrowIfNull(fileCache);

        fileCache.SetResolvedFilePath(ResolveFilePathFromFileStore(fileCache.Store, fileCache.FilePathWithinFileStore));
        var fileInfo = new FileInfo(fileCache.ResolvedFilepath);

        if (!fileInfo.Exists)
        {
            return (FileState.RequireDeletion, fileCache);
        }

        if (fileInfo.LastWriteTimeUtc.Ticks != fileCache.LastModifiedDateTicks)
        {
            return (FileState.RequireUpdate, fileCache);
        }

        return (FileState.Valid, fileCache);
    }

    /// <summary>
    /// Used for file migrations, the extentions are needed for various utility functions like bc7 conversion, etc.
    /// </summary>
    internal FileCacheEntity MigrateFileHashToExtension(FileCacheEntity fileCache, string ext)
    {
        ArgumentNullException.ThrowIfNull(fileCache);
        ArgumentException.ThrowIfNullOrWhiteSpace(ext);

        var originalPath = ResolveFilePathFromFileStore(fileCache.Store, fileCache.FilePathWithinFileStore);
        var normalizedExtension = ext.Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedExtension))
        {
            throw new ArgumentException("A valid file extension must be provided.", nameof(ext));
        }

        var extensionPath = originalPath + "." + normalizedExtension;
        var newFilePathWithinFileStore = StringUtils.NormalizeFilePathWithinFileStore(fileCache.FilePathWithinFileStore + "." + normalizedExtension);
        List<(string Hash, FileCacheStore Store, string FilePathWithinFileStore)> fileCachesToInvalidate = [];
        FileCacheEntity? migratedFileCache = null;
        Exception? migrationException = null;

        _databaseWriteSemaphore.Wait();

        try
        {
            try
            {
                File.Move(originalPath, extensionPath, overwrite: true);

                var movedFileInfo = new FileInfo(extensionPath);

                using var db = _db.CreateDbContext();
                var fileCaches = db.FileCaches.Where(entry => entry.Store == fileCache.Store && (entry.FilePathWithinFileStore == fileCache.FilePathWithinFileStore 
                    || entry.FilePathWithinFileStore == newFilePathWithinFileStore)).ToList();
                fileCachesToInvalidate.AddRange(fileCaches.Select(entry => (entry.Hash, entry.Store, entry.FilePathWithinFileStore)));

                var oldCache = fileCaches.SingleOrDefault(entry => string.Equals(entry.FilePathWithinFileStore, fileCache.FilePathWithinFileStore, StringComparison.Ordinal));
                var newCache = fileCaches.SingleOrDefault(entry => string.Equals(entry.FilePathWithinFileStore, newFilePathWithinFileStore, StringComparison.Ordinal));

                if (oldCache != null && newCache != null && oldCache.Id != newCache.Id)
                {
                    db.FileCaches.Remove(newCache);
                }

                migratedFileCache = oldCache ?? newCache;

                if (migratedFileCache == null)
                {
                    migratedFileCache = new FileCacheEntity(StringUtils.NormalizeHash(fileCache.Hash), newFilePathWithinFileStore, fileCache.Store, 
                        fileCache.LastAccessedDateTicks, movedFileInfo.LastWriteTimeUtc.Ticks, movedFileInfo.Length, fileCache.CompressedSize);
                    db.FileCaches.Add(migratedFileCache);
                }
                else
                {
                    migratedFileCache.Hash = StringUtils.NormalizeHash(fileCache.Hash);
                    migratedFileCache.FilePathWithinFileStore = newFilePathWithinFileStore;
                    migratedFileCache.LastModifiedDateTicks = movedFileInfo.LastWriteTimeUtc.Ticks;
                    migratedFileCache.Size = movedFileInfo.Length;
                    migratedFileCache.CompressedSize = fileCache.CompressedSize;
                }

                migratedFileCache.SetResolvedFilePath(extensionPath);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                migrationException = ex;

                try
                {
                    if (!File.Exists(originalPath) && File.Exists(extensionPath))
                    {
                        File.Move(extensionPath, originalPath, overwrite: true);
                    }
                }
                catch (Exception rollbackException)
                {
                    _logger.LogWarning(rollbackException, "Failed to restore file from {newPath} to {oldPath} after migration failure", extensionPath, originalPath);
                }
            }
        }
        finally
        {
            _databaseWriteSemaphore.Release();
        }

        if (migrationException != null || migratedFileCache == null)
        {
            _logger.LogWarning(migrationException, "Failed to migrate entity {entity}", fileCache.FilePathWithinFileStore);

            if (File.Exists(originalPath))
            {
                fileCache.SetResolvedFilePath(originalPath);
            }

            return fileCache;
        }

        foreach (var fileCacheToInvalidate in fileCachesToInvalidate)
        {
            _memoryCache.Invalidate(fileCacheToInvalidate.Hash, fileCacheToInvalidate.Store, fileCacheToInvalidate.FilePathWithinFileStore);
        }

        _memoryCache.Invalidate(fileCache);
        _memoryCache.Invalidate(migratedFileCache);

        _logger.LogTrace("Migrated from {oldPath} to {newPath}", originalPath, migratedFileCache.ResolvedFilepath);

        return migratedFileCache;
    }

    /// <summary>
    /// Creates a string of the full path on disk to the storage cached file
    /// </summary>
    /// <returns>The full file path on disk</returns>
    public string GetCacheFilePath(string hash, string extension)
    {
        // Local storage uses lowercase file names, while server DTOs use uppercase hashes.
        return Path.Combine(_configService.Current.CacheFolder, hash.ToLowerInvariant() + "." + extension);
    }

    /// <summary>
    /// Get the compressed byte data of a given file hash.
    /// Note this will return the first valid file cache of available stores.
    /// </summary>
    /// <param name="fileHash">The hash of the file</param>
    /// <returns>A tuple of the passed hash and compressed data bytes</returns>
    /// <exception cref="FileNotFoundException"></exception>
    public async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        var fileCacheEntity = await GetFirstFileCacheEntityByHash(fileHash, uploadToken).ConfigureAwait(false);
        if (fileCacheEntity == null)
        {
            throw new FileNotFoundException($"No valid file cache entry exists for hash {fileHash}.");
        }

        var fileCache = fileCacheEntity.ResolvedFilepath;

        return (fileHash, LZ4Wrapper.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(false), 0, (int)new FileInfo(fileCache).Length));
    }

    #endregion PUBLIC METHODS

    #region PRIVATE METHODS

    // Creates proper FileCacheEntity for the database
    private List<FileCacheEntity> MakeFileCacheEntitiesFromPathAndStore(IEnumerable<string> paths, FileCacheStore store, CancellationToken cancellationToken)
    {
        Dictionary<string, FileCacheEntity> fileCaches = new(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo fi = new(path);
            if (!fi.Exists)
            {
                continue;
            }

            var filePathWithinFileStore = GetFilePathWithinFileStore(fi.FullName, store);
            if (filePathWithinFileStore == null)
            {
                continue;
            }            

            try
            {
                var hash = StringUtils.NormalizeHash(Crypto.GetFileHash(fi.FullName));
                var lastModifiedDateTicks = fi.LastWriteTimeUtc.Ticks;
                var entity = new FileCacheEntity(hash, filePathWithinFileStore, store, lastModifiedDateTicks, lastModifiedDateTicks, fi.Length);
                entity.SetResolvedFilePath(fi.FullName);
                fileCaches[entity.FilePathWithinFileStore] = entity;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DATABASE] Failed to create cache entry for {path} in store {store}", path, store);
            }
        }

        return [.. fileCaches.Values];
    }

    /// <summary>
    /// Creates multiple file cache entities from the given paths and the file store
    /// </summary>
    /// <param name="paths">An IEnumerable of paths to files on disk</param>
    /// <param name="store">The file store within which this is to be placed</param>
    /// <returns>A list containing the created FileCacheEntity</returns>
    private async Task<List<FileCacheEntity>> CreateFileCacheEntities(IEnumerable<string> paths, FileCacheStore store, CancellationToken cancellationToken = default)
    {
        var fileCaches = MakeFileCacheEntitiesFromPathAndStore(paths, store, cancellationToken);

        foreach (var fileCache in fileCaches)
        {
            _logger.LogTrace("[DATABASE] Creating cache entry for {path} in store {store}", fileCache.ResolvedFilepath, store);
        }

        var addedFileCaches = await AddFileCacheEntriesAsync(fileCaches, store, cancellationToken).ConfigureAwait(false);

        foreach (var fileCache in addedFileCaches)
        {
            _logger.LogTrace("[DATABASE] Creating cache entity for {name} success: {success}", fileCache.ResolvedFilepath, true);
        }

        return addedFileCaches;
    }

    private async Task<Dictionary<string, List<FileCacheEntity>>> GetAllMemoryFrontedNonVerifiedFileCacheEntitiesByHashesInternal(IEnumerable<string> hashes, bool cacheResults, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashes);

        HashSet<string> normalizedHashes = new(StringComparer.Ordinal);

        foreach (var hash in hashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            normalizedHashes.Add(StringUtils.NormalizeHash(hash)); // protect against the empty/whitespace hashes
        }

        Dictionary<string, List<FileCacheEntity>> fileCachesByHash = [];
        List<string> uncachedHashes = [];

        foreach (var hash in normalizedHashes)
        {
            if (_memoryCache.TryGetFileCachesByHash(hash, out var fileCaches))
            {
                fileCachesByHash[hash] = fileCaches.ToList();
            }
            else
            {
                uncachedHashes.Add(hash);
            }
        }

        if (uncachedHashes.Count == 0)
        {
            return fileCachesByHash;
        }

        await using var db = _db.CreateDbContext();

        foreach (var hashBatch in uncachedHashes.Chunk(1000))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cacheVersion = _memoryCache.GetVersion();
            var entities = await db.FileCaches
                .AsNoTracking()
                .Where(fileCache => hashBatch.Contains(fileCache.Hash))
                .OrderBy(fileCache => fileCache.Hash)
                .ThenBy(fileCache => fileCache.Store)
                .ThenBy(fileCache => fileCache.Id)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            Dictionary<string, List<FileCacheEntity>> entitiesByHash = [];

            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entity.SetResolvedFilePath(ResolveFilePathFromFileStore(entity.Store, entity.FilePathWithinFileStore));

                if (!entitiesByHash.TryGetValue(entity.Hash, out var matchingEntities))
                {
                    matchingEntities = [];
                    entitiesByHash.Add(entity.Hash, matchingEntities);
                }

                matchingEntities.Add(entity);
            }

            foreach (var hash in hashBatch)
            {
                var matchingEntities = entitiesByHash.TryGetValue(hash, out var hashEntities) ? hashEntities : [];

                fileCachesByHash[hash] = matchingEntities;
                if (cacheResults)
                {
                    _memoryCache.SetFileCachesByHash(hash, matchingEntities, cacheVersion);
                }
            }
        }

        return fileCachesByHash;
    }

    /// <summary>
    /// Gets the first FileCacheEntity from the database from a given file path and file store.
    /// </summary>
    /// <param name="filePathWithinFileStore">The file path on disk within the file store</param>
    /// <param name="store">The file store in which to check for the file</param>
    /// <returns>A matching FileCacheEntity if it exists, else null</returns>
    private async Task<FileCacheEntity?> GetFileCacheEntityByPath(string filePathWithinFileStore, FileCacheStore store, CancellationToken cancellationToken = default)
    {
        filePathWithinFileStore = StringUtils.NormalizeFilePathWithinFileStore(filePathWithinFileStore);
        if (string.IsNullOrEmpty(filePathWithinFileStore))
        {
            return null;
        }

        if (_memoryCache.TryGetFileCacheByPath(store, filePathWithinFileStore, out var cachedFileCache) && cachedFileCache != null)
        {
            return cachedFileCache;
        }

        await using var db = _db.CreateDbContext();
        var cacheVersion = _memoryCache.GetVersion();
        var fileCache = await db.FileCaches.AsNoTracking().SingleOrDefaultAsync(entry => entry.Store == store
            && entry.FilePathWithinFileStore == filePathWithinFileStore, cancellationToken).ConfigureAwait(false);

        if (fileCache != null)
        {
            fileCache.SetResolvedFilePath(ResolveFilePathFromFileStore(fileCache.Store, fileCache.FilePathWithinFileStore));
            _memoryCache.SetFileCacheByPath(fileCache, cacheVersion);
        }

        LogMemoryCacheUsage();

        return fileCache;
    }

    /// <summary>
    /// This is used for file scans, do not use it for regular lookups!
    /// This method does not have memory caching
    /// </summary>
    private async Task<List<FileCacheEntity>> UpdateOrCreateFileCacheEntitiesAsync(List<FileCacheEntity> fileCaches, FileCacheStore store, CancellationToken cancellationToken, bool markAccessed)
    {
        ArgumentNullException.ThrowIfNull(fileCaches);

        if (fileCaches.Count == 0)
        {
            return [];
        }

        await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var db = _db.CreateDbContext();
            List<FileCacheEntity> returnFileCaches = [];

            foreach (var cacheBatch in fileCaches.Chunk(500))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filePaths = cacheBatch.Select(fileCache => fileCache.FilePathWithinFileStore).ToArray();

                var existingFileCaches = await db.FileCaches
                    .Where(fileCache => fileCache.Store == store && filePaths
                    .Contains(fileCache.FilePathWithinFileStore))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var existingFileCachesByPath = existingFileCaches.ToDictionary(fileCache => fileCache.FilePathWithinFileStore, StringComparer.OrdinalIgnoreCase);

                List<(string Hash, FileCacheStore Store, string FilePathWithinFileStore)> oldCacheKeys = [];
                List<FileCacheEntity> processedFileCaches = [];

                foreach (var fileCache in cacheBatch)
                {
                    if (existingFileCachesByPath.TryGetValue(fileCache.FilePathWithinFileStore, out var existingFileCache))
                    {
                        oldCacheKeys.Add((existingFileCache.Hash, existingFileCache.Store, existingFileCache.FilePathWithinFileStore));

                        var hashChanged = !string.Equals(existingFileCache.Hash, fileCache.Hash, StringComparison.OrdinalIgnoreCase);

                        existingFileCache.Hash = fileCache.Hash;
                        existingFileCache.LastModifiedDateTicks = fileCache.LastModifiedDateTicks;
                        if (markAccessed && store == FileCacheStore.Cache)
                        {
                            existingFileCache.LastAccessedDateTicks = Math.Max(existingFileCache.LastAccessedDateTicks, fileCache.LastAccessedDateTicks);
                        }
                        existingFileCache.Size = fileCache.Size;
                        if (hashChanged)
                        {
                            existingFileCache.CompressedSize = fileCache.CompressedSize;
                        }

                        existingFileCache.SetResolvedFilePath(fileCache.ResolvedFilepath);
                        processedFileCaches.Add(existingFileCache);
                    }
                    else
                    {
                        db.FileCaches.Add(fileCache);
                        processedFileCaches.Add(fileCache);
                    }
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                foreach (var oldCacheKey in oldCacheKeys)
                {
                    _memoryCache.Invalidate(oldCacheKey.Hash, oldCacheKey.Store, oldCacheKey.FilePathWithinFileStore);
                }

                foreach (var fileCache in processedFileCaches)
                {
                    _memoryCache.Invalidate(fileCache);
                }

                returnFileCaches.AddRange(processedFileCaches);
                db.ChangeTracker.Clear();
            }

            return returnFileCaches;
        }
        finally
        {
            _databaseWriteSemaphore.Release();
        }
    }

    // Adds file cache entities to the database
    private async Task<List<FileCacheEntity>> AddFileCacheEntriesAsync(IReadOnlyCollection<FileCacheEntity> fileCaches, FileCacheStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileCaches);

        if (fileCaches.Count == 0)
        {
            return [];
        }

        await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var db = _db.CreateDbContext();
            HashSet<string> existingPaths = new(StringComparer.OrdinalIgnoreCase);

            foreach (var filePathBatch in fileCaches.Select(fileCache => fileCache.FilePathWithinFileStore).Chunk(500))
            {
                var filePaths = filePathBatch.ToArray();
                var fileCacheEntries = await db.FileCaches
                    .AsNoTracking()
                    .Where(fileCache => fileCache.Store == store && filePaths.Contains(fileCache.FilePathWithinFileStore))
                    .Select(fileCache => fileCache.FilePathWithinFileStore)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                existingPaths.UnionWith(fileCacheEntries);
            }

            var addedFileCaches = fileCaches.Where(fileCache => !existingPaths.Contains(fileCache.FilePathWithinFileStore)).ToList();
            foreach (var fileCacheBatch in addedFileCaches.Chunk(1000))
            {
                db.FileCaches.AddRange(fileCacheBatch);
                await db.SaveChangesAsync(false, cancellationToken).ConfigureAwait(false);

                db.ChangeTracker.Clear(); // batching within the same db context

                foreach (var fileCache in fileCacheBatch)
                {
                    _memoryCache.Invalidate(fileCache);
                }
            }

            return addedFileCaches;
        }
        finally
        {
            _databaseWriteSemaphore.Release();
        }
    }

    // Deletes file cache entries from teh DB and returns the number of removed entries
    // the tuple parameter is based on the uniqueness of entries in the db
    private async Task<int> RemoveFileCacheEntitiesInternalAsync(List<(string Hash, string FilePathWithinFileStore, FileCacheStore Store)> fileCaches, CancellationToken cancellationToken)
    {
        Dictionary<(FileCacheStore Store, string FilePathWithinFileStore), (string Hash, string FilePathWithinFileStore, FileCacheStore Store)> cachesToRemove = [];

        // We could get past a non db FileCacheEntity so we should only trust what we know
        foreach (var fileCache in fileCaches)
        {
            var hash = StringUtils.NormalizeHash(fileCache.Hash);
            var filePathWithinFileStore = StringUtils.NormalizeFilePathWithinFileStore(fileCache.FilePathWithinFileStore);
            cachesToRemove[(fileCache.Store, filePathWithinFileStore)] = (hash, filePathWithinFileStore, fileCache.Store);
        }

        if (cachesToRemove.Count == 0)
        {
            return 0;
        }

        await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var db = _db.CreateDbContext();
            var removedEntries = 0;

            foreach (var storeGroup in cachesToRemove.Values.GroupBy(fileCache => fileCache.Store))
            {
                var store = storeGroup.Key;

                foreach (var fileCacheBatch in storeGroup.Chunk(500)) // keep it sane
                {
                    var fileCachesToRemove = fileCacheBatch.ToArray();
                    var filePaths = fileCachesToRemove.Select(fileCache => fileCache.FilePathWithinFileStore).ToArray();
                    var expectedHashes = fileCachesToRemove.ToDictionary(fileCache => fileCache.FilePathWithinFileStore, fileCache => fileCache.Hash, StringComparer.Ordinal);
                    var existingEntries = await db.FileCaches
                        .AsNoTracking()
                        .Where(fileCache => fileCache.Store == store && filePaths
                        .Contains(fileCache.FilePathWithinFileStore))
                        .Select(fileCache => new { fileCache.Id, fileCache.Hash, fileCache.FilePathWithinFileStore })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);

                    var entriesToRemove = existingEntries
                        .Where(fileCache => expectedHashes
                        .TryGetValue(fileCache.FilePathWithinFileStore, out var expectedHash) && string.Equals(fileCache.Hash, expectedHash, StringComparison.Ordinal))
                        .ToArray();

                    if (entriesToRemove.Length > 0)
                    {
                        var ids = entriesToRemove.Select(fileCache => fileCache.Id).ToArray();
                        removedEntries += await db.FileCaches.Where(fileCache => ids.Contains(fileCache.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                    }

                    foreach (var fileCache in fileCachesToRemove)
                    {
                        _memoryCache.Invalidate(fileCache.Hash, fileCache.Store, fileCache.FilePathWithinFileStore);
                    }

                    foreach (var fileCache in entriesToRemove)
                    {
                        _logger.LogTrace("[DATABASE] Removed file with hash {hash} and path {path}", fileCache.Hash, fileCache.FilePathWithinFileStore);
                    }
                }
            }

            return removedEntries;
        }
        finally
        {
            _databaseWriteSemaphore.Release();
        }
    }

    private async Task ValidateFileCacheEntitiesAsync(Dictionary<string, List<FileCacheEntity>> fileCachesByHash, CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(fileCachesByHash,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 4
            },
            async (fileCachePair, token) =>
            {
                var fileCaches = fileCachePair.Value;
                int validCount = 0;

                for (int index = 0; index < fileCaches.Count; index++)
                {
                    token.ThrowIfCancellationRequested();

                    var validatedFileCache = await ValidateFileCacheAsync(fileCaches[index], token).ConfigureAwait(false);

                    if (validatedFileCache != null)
                    {
                        fileCaches[validCount++] = validatedFileCache;
                    }
                }

                if (validCount < fileCaches.Count)
                {
                    fileCaches.RemoveRange(validCount, fileCaches.Count - validCount);
                }
            }).ConfigureAwait(false);
    }

    // Logs the mem cache state when logging is set to "Trace"
    private void LogMemoryCacheUsage()
    {
        // don't take time to pull stats unless trace logging is enabled
        if (!_logger.IsEnabled(LogLevel.Trace) || !_memoryCache.IsMemoryCacheEnabled)
        {
            return;
        }

        if (DateTimeOffset.UtcNow - _lastMemCacheUpdateTime < TimeSpan.FromSeconds(5))
        {
            return;
        }
        _lastMemCacheUpdateTime = DateTimeOffset.UtcNow;

        var statistics = _memoryCache.GetCurrentStatistics();
        if (statistics == null)
        {
            _logger.LogWarning("[MEMORY CACHE] Statistics are not enabled");

            return;
        }

        var estimatedSize = statistics.CurrentEstimatedSize ?? 0;
        var totalRequests = statistics.TotalHits + statistics.TotalMisses;
        var hitRate = totalRequests == 0 ? 0d : (double)statistics.TotalHits / totalRequests;
        var sizeUsage = _memoryCache.SizeLimit == 0 ? 0d : (double)estimatedSize / _memoryCache.SizeLimit;
        var managedMemoryMiB = GC.GetTotalMemory(false) / 1024d / 1024d; // this isn't just PlayerSync, but it helps with relational comparisons

        _logger.LogTrace("[MEMORY CACHE] Entries: {entries}, size: {size}/{sizeLimit} units ({sizeUsage:P1}), hits: {hits}, " +
            "misses: {misses}, hit rate: {hitRate:P1}, total managed memory: {managedMemoryMiB:F2} MiB",
            statistics.CurrentEntryCount, estimatedSize, _memoryCache.SizeLimit, sizeUsage, statistics.TotalHits, statistics.TotalMisses,
            hitRate, managedMemoryMiB);
    }

    #endregion PRIVATE METHODS

    #region HELPER METHODS

    private void AddToFileCacheAccessUpdates(FileCacheEntity? fileCache)
    {
        if (fileCache != null)
        {
            AddToFileCacheAccessUpdates([fileCache]);
        }
    }

    private void AddToFileCacheAccessUpdates(IEnumerable<FileCacheEntity?> fileCaches)
    {
        lock (_dbFilesToUpdateTicksLock)
        {
            foreach (var fileCache in fileCaches)
            {
                if (fileCache != null && fileCache.Store == FileCacheStore.Cache)
                {
                    _dbFilesToUpdateTicks.Add(fileCache.Id);
                }
            }
        }
    }

    private string? GetFileStoreRoot(FileCacheStore store)
    {
        return store switch
        {
            FileCacheStore.Penumbra => _fileStoreRoots.Penumbra,
            FileCacheStore.Cache => _fileStoreRoots.Cache,
            _ => throw new InvalidDataException("Unknown file store!")
        };
    }

    private string? GetFilePathWithinFileStore(string filePath, FileCacheStore store)
    {
        var fileStoreRoot = GetFileStoreRoot(store);
        if (fileStoreRoot == null)
        {
            return null;
        }

        var normalizedFilePath = StringUtils.NormalizePath(filePath);
        if (!normalizedFilePath.StartsWith(fileStoreRoot, StringComparison.Ordinal))
        {
            return null;
        }

        var filePathWithinFileStore = normalizedFilePath[fileStoreRoot.Length..];

        return filePathWithinFileStore.Length == 0 ? null : filePathWithinFileStore;
    }

    private string ResolveFilePathFromFileStore(FileCacheStore store, string filePathWithinFileStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePathWithinFileStore);

        var fileStoreRoot = GetFileStoreRoot(store);
        if (fileStoreRoot == null)
        {
            throw new InvalidOperationException($"The directory for {store} is missing!");
        }

        var resolvedFilePath = StringUtils.NormalizePath(Path.Combine(fileStoreRoot, filePathWithinFileStore));

        if (!resolvedFilePath.StartsWith(fileStoreRoot, StringComparison.Ordinal)
            || resolvedFilePath.Length != fileStoreRoot.Length + filePathWithinFileStore.Length
            || !resolvedFilePath.AsSpan(fileStoreRoot.Length).Equals(filePathWithinFileStore.AsSpan(), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The store {store} is not correct for this file cache path");
        }

        return resolvedFilePath;
    }

    private static string? NormalizeFileStoreRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return StringUtils.NormalizePath(path).TrimEnd('\\') + "\\";
    }

    #endregion HELPER METHODS

    private async Task PreloadMostRecentlyAccessedFileCacheEntitiesAsync(CancellationToken cancellationToken)
    {
        const int batchSize = 10000;
        var preloadCount = 100000;

        if (!_memoryCache.IsMemoryCacheEnabled || preloadCount <= 0)
        {
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        HashSet<string> hashesToPreload = new(StringComparer.OrdinalIgnoreCase);

        // this uses our db pagination strategy
        long lastAccessedDateTicks = long.MaxValue;
        long lastId = long.MaxValue;
        int selectedEntityCount = 0;
        int cachedHashCount = 0;
        int cachedEntityCount = 0;

        _logger.LogInformation("[DATABASE] Preloading up to {count} recently accessed file cache entities", preloadCount);

        await using var db = _db.CreateDbContext();

        // doing pagination here
        while (selectedEntityCount < preloadCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var take = Math.Min(batchSize, preloadCount - selectedEntityCount);
            // this was changed to include both stores since we obviously use the Penumbra store for other players
            var recentEntities = await db.FileCaches
                .AsNoTracking()
                .Where(fileCache => (fileCache.LastAccessedDateTicks < lastAccessedDateTicks) || (fileCache.LastAccessedDateTicks == lastAccessedDateTicks && fileCache.Id < lastId))
                .OrderByDescending(fileCache => fileCache.LastAccessedDateTicks)
                .ThenByDescending(fileCache => fileCache.Id)
                .Select(fileCache => new
                {
                    fileCache.Hash,
                    fileCache.LastAccessedDateTicks,
                    fileCache.Id
                })
                .Take(take)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (recentEntities.Length == 0)
            {
                break;
            }

            foreach (var entity in recentEntities)
            {
                hashesToPreload.Add(entity.Hash);
            }

            selectedEntityCount += recentEntities.Length;

            var lastEntity = recentEntities[^1];
            lastAccessedDateTicks = lastEntity.LastAccessedDateTicks;
            lastId = lastEntity.Id;
        }

        foreach (var hashBatch in hashesToPreload.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cacheVersion = _memoryCache.GetVersion();
            var entities = await db.FileCaches
                .AsNoTracking()
                .Where(fileCache => EF.Parameter(hashBatch).Contains(fileCache.Hash)) // this is an EF 10 thing else it will error
                .OrderBy(fileCache => fileCache.Hash)
                .ThenBy(fileCache => fileCache.Store)
                .ThenBy(fileCache => fileCache.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            Dictionary<string, List<FileCacheEntity>> entitiesByHash = [];

            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // this is probably not needed and a waste of cpu time
                entity.SetResolvedFilePath(ResolveFilePathFromFileStore(entity.Store, entity.FilePathWithinFileStore));

                if (!entitiesByHash.TryGetValue(entity.Hash, out var matchingEntities))
                {
                    matchingEntities = new List<FileCacheEntity>(1);
                    entitiesByHash.Add(entity.Hash, matchingEntities);
                }

                matchingEntities.Add(entity);
            }

            foreach (var hash in hashBatch)
            {
                FileCacheEntity[] matchingEntities = entitiesByHash.TryGetValue(hash, out var hashEntities) ? [.. hashEntities] : [];

                if (_memoryCache.SetFileCachesByHash(hash, matchingEntities, cacheVersion))
                {
                    cachedHashCount++;
                    cachedEntityCount += matchingEntities.Length;
                }
            }
        }

        IsCacheReady = true;

        stopwatch.Stop();

        _logger.LogInformation("[DATABASE] Preloaded {entityCount} file cache entities across {hashCount} hashes in {elapsedMilliseconds}ms", 
            cachedEntityCount, cachedHashCount, stopwatch.ElapsedMilliseconds);

        LogMemoryCacheUsage();
    }

    // This updates the ticks once a minute in a batch vs every time a file is accessed
    private async Task UpdateFileCacheTicks(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await FlushFileCacheTicksAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while updating file cache entity ticks");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            //
        }
    }

    private async Task FlushFileCacheTicksAsync(CancellationToken cancellationToken)
    {
        await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            HashSet<long> cachesToUpdate;
            lock (_dbFilesToUpdateTicksLock)
            {
                cachesToUpdate = [.. _dbFilesToUpdateTicks];
                _dbFilesToUpdateTicks.Clear();
            }

            if (cachesToUpdate.Count == 0)
            {
                return;
            }

            _logger.LogTrace("[DATABASE] Updating LastAccessedDateTicks for {count} db entries", cachesToUpdate.Count);

            try
            {
                await using var db = _db.CreateDbContext();
                var lastAccessedDateTicks = DateTime.UtcNow.Ticks;

                foreach (var cacheBatch in cachesToUpdate.Chunk(500))
                {
                    var cacheIds = cacheBatch.ToArray();
                    await db.FileCaches
                        .Where(cache => cacheIds.Contains(cache.Id) && cache.LastAccessedDateTicks < lastAccessedDateTicks)
                        .ExecuteUpdateAsync(properties => properties
                        .SetProperty(cache => cache.LastAccessedDateTicks, lastAccessedDateTicks), cancellationToken) // this updates in bulk via sql vs creating entities
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                lock (_dbFilesToUpdateTicksLock)
                {
                    _dbFilesToUpdateTicks.UnionWith(cachesToUpdate); // readd if error so we don't lose track
                }

                throw;
            }
        }
        finally
        {
            _databaseWriteSemaphore.Release();
        }
    }

    // This runs as a seperate thread to not block the Dalamud loading of the plugin and the plugin itself starting
    // The reason being the MigrateCsvToDatabaseAsync can take up to a minute for slower PCs/large data sets
    private async Task RunStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await MigrateCsvToDatabaseAsync(cancellationToken).ConfigureAwait(false);

            DatabaseStatus = (false, DatabaseState.Validating);

            await using (var db = _db.CreateDbContext())
            {
                var hasPenumbraEntries = await db.FileCaches.AsNoTracking().AnyAsync(fileCache => fileCache.Store == FileCacheStore.Penumbra, cancellationToken).ConfigureAwait(false);
                if (hasPenumbraEntries && (!_ipcManager.Penumbra.APIAvailable || string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory)))
                {
                    Mediator.Publish(new NotificationMessage("Penumbra not connected", "Could not load local file cache data. " +
                        "Penumbra is not connected or not properly set up. Please enable and/or configure Penumbra properly to use PlayerSync. " +
                        "After, reload PlayerSync in the Plugin installer.", MareConfiguration.Models.NotificationType.Error));
                }
            }

            DatabaseStatus = (false, DatabaseState.CacheInit);
            //_ = Task.Run(async () => await PreloadMostRecentlyAccessedFileCacheEntitiesAsync(_dbLifetimeCts.Token).ConfigureAwait(false), _dbLifetimeCts.Token);
            await PreloadMostRecentlyAccessedFileCacheEntitiesAsync(_dbLifetimeCts.Token).ConfigureAwait(false);

            DatabaseStatus = (true, DatabaseState.Ready);
            _logger.LogInformation("Database initialization completed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DatabaseStatus = (false, DatabaseState.Stopped);
            _logger.LogInformation("Database initialization was cancelled");
        }
        catch (Exception ex)
        {
            DatabaseStatus = (false, DatabaseState.Failed);
            _logger.LogError(ex, "Database initialization failed");
        }
    }

    // This does the CSV to Database migration
    private async Task MigrateCsvToDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_oldDataFile) || _configService.Current.DataHasBeenMigratedToNewDb)
        {
            return;
        }

        _logger.LogInformation("[DATABASE] Found legacy file cache CSV {path}, starting migration", _oldDataFile);
        DatabaseStatus = (false, DatabaseState.Migrating);

        const int batchSize = 5000;
        Dictionary<(FileCacheStore Store, string FilePathWithinFileStore), FileCacheEntity> batch = [];
        long processedEntries = 0;
        long lineNumber = 0;
        int skippedEntries = 0;

        try
        {
            await _databaseWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await using var db = _db.CreateDbContext();
                // do this as an atomic operation, the transaction must succeed else it rolls back
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                async Task UpsertBatchAsync()
                {
                    if (batch.Count == 0)
                    {
                        return;
                    }

                    Dictionary<(FileCacheStore Store, string FilePathWithinFileStore), FileCacheEntity> existingEntries = [];

                    foreach (var storeGroup in batch.Values.GroupBy(fileCache => fileCache.Store))
                    {
                        var store = storeGroup.Key;
                        var filePaths = storeGroup.Select(fileCache => fileCache.FilePathWithinFileStore).ToArray();
                        var entries = await db.FileCaches.Where(fileCache => fileCache.Store == store && filePaths.Contains(fileCache.FilePathWithinFileStore)).ToListAsync(cancellationToken).ConfigureAwait(false);

                        foreach (var entry in entries)
                        {
                            existingEntries[(entry.Store, entry.FilePathWithinFileStore)] = entry;
                        }
                    }

                    List<FileCacheEntity> newEntries = [];

                    foreach (var fileCache in batch.Values)
                    {
                        if (existingEntries.TryGetValue((fileCache.Store, fileCache.FilePathWithinFileStore), out var existing))
                        {
                            existing.Hash = fileCache.Hash;
                            existing.LastModifiedDateTicks = fileCache.LastModifiedDateTicks;
                            existing.LastAccessedDateTicks = fileCache.LastAccessedDateTicks;
                            existing.Size = fileCache.Size;
                            existing.CompressedSize = fileCache.CompressedSize;
                        }
                        else
                        {
                            newEntries.Add(fileCache);
                        }
                    }

                    db.FileCaches.AddRange(newEntries);
                    await db.SaveChangesAsync(false, cancellationToken).ConfigureAwait(false);
                    db.ChangeTracker.Clear(); // clear the tracked ef entities since we're using the same db context (because transaction)
                    batch.Clear();
                }

                foreach (var entry in File.ReadLines(_oldDataFile))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lineNumber++;

                    try
                    {
                        var splitEntry = entry.Split('|', StringSplitOptions.None);
                        if (splitEntry.Length < 3)
                        {
                            throw new InvalidDataException($"CSV fields appear malformed/incorrect, number of fields: {splitEntry.Length}.");
                        }

                        var hash = splitEntry[0].Trim().ToUpperInvariant();
                        var legacyPath = splitEntry[1];

                        if (hash.Length != 40 || !hash.All(Uri.IsHexDigit))
                        {
                            throw new InvalidDataException($"Hash appears malformed/incorrect: '{hash}'");
                        }

                        if (!long.TryParse(splitEntry[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastModifiedDateTicks))
                        {
                            throw new InvalidDataException($"Last modified tick value appears malformed/incorrect: '{splitEntry[2]}'");
                        }

                        FileCacheStore store;
                        string filePathWithinFileStore;

                        if (legacyPath.StartsWith(LegacyPenumbraPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            store = FileCacheStore.Penumbra;
                            filePathWithinFileStore = legacyPath[LegacyPenumbraPrefix.Length..];
                        }
                        else if (legacyPath.StartsWith(LegacyCachePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            store = FileCacheStore.Cache;
                            filePathWithinFileStore = legacyPath[LegacyCachePrefix.Length..];
                        }
                        else
                        {
                            throw new InvalidDataException($"Unknown legacy file store path: '{legacyPath}'.");
                        }

                        filePathWithinFileStore = StringUtils.NormalizeFilePathWithinFileStore(filePathWithinFileStore);

                        if (string.IsNullOrWhiteSpace(filePathWithinFileStore))
                        {
                            throw new InvalidDataException("The file path within the file store is empty");
                        }

                        long size = -1;
                        long compressedSize = -1;

                        if (splitEntry.Length > 3 && long.TryParse(splitEntry[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
                        {
                            size = parsedSize;
                        }

                        if (splitEntry.Length > 4 && long.TryParse(splitEntry[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCompressedSize))
                        {
                            compressedSize = parsedCompressedSize;
                        }

                        var fileCache = new FileCacheEntity(hash, filePathWithinFileStore, store, lastModifiedDateTicks, lastModifiedDateTicks, size, compressedSize);
                        batch[(fileCache.Store, fileCache.FilePathWithinFileStore)] = fileCache;
                        processedEntries++;
                    }
                    catch (Exception ex)
                    {
                        skippedEntries++;
                        _logger.LogWarning(ex, "[DATABASE] Failed to migrate CSV entry on line {lineNumber}, skipping it", lineNumber);

                        continue;
                    }

                    if (batch.Count >= batchSize)
                    {
                        _logger.LogInformation("[DATABASE] Starting database batch after parsing {count} entries", processedEntries);
                        await UpsertBatchAsync().ConfigureAwait(false);
                        _logger.LogInformation("[DATABASE] Finished database batch with {count} total entries processed", processedEntries);
                    }
                }

                await UpsertBatchAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); // this is the final "save" for the entire transaction
                _memoryCache.Clear();
            }
            finally
            {
                _databaseWriteSemaphore.Release();
            }

            _configService.Current.DataHasBeenMigratedToNewDb = true;
            _configService.Save();
            _logger.LogInformation("[DATABASE] Migrated {count} legacy file cache entries, skipped {skipped} entries", processedEntries, skippedEntries);
            //var migratedCsvPath = _oldDataFile + ".migrated";

            //try
            //{
            //    File.Move(_oldDataFile, migratedCsvPath, overwrite: true);
            //    _logger.LogInformation("[DATABASE] Migrated {count} legacy file cache entries, skipped {skipped} entries, and renamed {path} to {migratedPath}", processedEntries, skippedEntries, _oldDataFile, migratedCsvPath);
            //}
            //catch (Exception ex)
            //{
            //    _logger.LogWarning(ex, "[DATABASE] CSV migration completed but {path} could not be renamed to {migratedPath}; migration will safely run again next startup", _oldDataFile, migratedCsvPath);
            //}
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("[DATABASE] Legacy CSV migration was cancelled after processing {count} entries, migration will restart next startup", processedEntries);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DATABASE] Legacy CSV migration failed after processing {count} entries, migration will run again next startup", processedEntries);
        }
    }
}
