using K4os.Compression.LZ4.Legacy;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace MareSynchronos.FileCache;

public sealed class FileCacheManager : IHostedService
{
    public const string CachePrefix = "{cache}";
    public const string CsvSplit = "|";
    public const string PenumbraPrefix = "{penumbra}";
    private readonly MareConfigService _configService;
    private readonly MareMediator _mareMediator;
    private readonly string _csvPath;
    //private readonly ConcurrentDictionary<string, List<FileCacheEntity>> _fileCaches = new(StringComparer.Ordinal); // Key: mod file hash, value: locations of that file on disk
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, FileCacheEntity>> _fileCaches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileCacheEntity> _byPrefixedPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _csvDebounceDelay = TimeSpan.FromSeconds(2);
    private Timer? _csvDebounceTimer;
    private int _csvDirty;
    private volatile bool _suppressCsvWrites;

    private readonly SemaphoreSlim _getCachesByPathsSemaphore = new(1, 1);
    private readonly object _fileWriteLock = new();
    private readonly IpcManager _ipcManager;
    private readonly ILogger<FileCacheManager> _logger;
    public string CacheFolder => _configService.Current.CacheFolder;

    public FileCacheManager(ILogger<FileCacheManager> logger, IpcManager ipcManager, MareConfigService configService, MareMediator mareMediator)
    {
        _logger = logger;
        _ipcManager = ipcManager;
        _configService = configService;
        _mareMediator = mareMediator;
        _csvPath = Path.Combine(configService.ConfigurationDirectory, "FileCache.csv");
    }

    private string CsvBakPath => _csvPath + ".bak";

    public FileCacheEntity? CreateCacheEntry(string path)
    {
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        _logger.LogTrace("Creating cache entry for {path}", path);
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_configService.Current.CacheFolder.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_configService.Current.CacheFolder.ToLowerInvariant(), CachePrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public FileCacheEntity? CreateFileEntry(string path)
    {
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        _logger.LogTrace("Creating file entry for {path}", path);
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_ipcManager.Penumbra.ModDirectory!.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_ipcManager.Penumbra.ModDirectory!.ToLowerInvariant(), PenumbraPrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public List<FileCacheEntity> GetAllFileCaches() => _fileCaches.Values.SelectMany(v => v.Values).ToList();

    public List<FileCacheEntity> GetAllFileCachesByHash(string hash, bool ignoreCacheEntries = false, bool validate = true)
    {
        List<FileCacheEntity> output = [];
        if (_fileCaches.TryGetValue(hash, out var dict))
        {
            foreach (var fileCache in dict.Values.Where(c => ignoreCacheEntries ? !c.IsCacheEntry : true).ToList())
            {
                if (!validate) output.Add(fileCache);
                else
                {
                    var validated = GetValidatedFileCache(fileCache);
                    if (validated != null) output.Add(validated);
                }
            }
        }

        return output;
    }

    public Task<List<FileCacheEntity>> ValidateLocalIntegrity(IProgress<(int, int, FileCacheEntity)> progress, CancellationToken cancellationToken)
    {
        _mareMediator.Publish(new HaltScanMessage(nameof(ValidateLocalIntegrity)));
        List<FileCacheEntity> brokenEntities = [];

        try
        {
            _logger.LogInformation("Validating local storage");

            // Snapshot current cache entries (only cache files)
            var cacheEntries = _fileCaches.SelectMany(v => v.Value.Values).Where(v => v.IsCacheEntry).ToList();

            int i = 0;
            foreach (var fileCache in cacheEntries)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("ValidateLocalIntegrity cancelled");
                    break;
                }

                _logger.LogInformation("Validating {file}", fileCache.ResolvedFilepath);
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
                        _logger.LogInformation(
                            "Failed to validate {file}, got hash {got}, expected hash {expected}",
                            fileCache.ResolvedFilepath, computedHash, fileCache.Hash);
                        brokenEntities.Add(fileCache);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Error during validation of {file}", fileCache.ResolvedFilepath);
                    brokenEntities.Add(fileCache);
                }
            }

            // Remove & delete broken files
            foreach (var brokenEntity in brokenEntities)
            {
                try
                {
                    RemoveHashedFile(brokenEntity.Hash, brokenEntity.PrefixedFilePath);

                    try
                    {
                        File.Delete(brokenEntity.ResolvedFilepath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete {file}", brokenEntity.ResolvedFilepath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to remove file cache entry for {hash} {path}",
                        brokenEntity.Hash, brokenEntity.PrefixedFilePath);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ValidateLocalIntegrity cancelled via OperationCanceledException");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ValidateLocalIntegrity failed unexpectedly");
        }
        finally
        {
            _mareMediator.Publish(new ResumeScanMessage(nameof(ValidateLocalIntegrity)));
        }

        return Task.FromResult(brokenEntities);
    }

    public string GetCacheFilePath(string hash, string extension)
    {
        // Local storage uses lowercase file names, while server DTOs use uppercase hashes.
        return Path.Combine(_configService.Current.CacheFolder, hash.ToLowerInvariant() + "." + extension);
    }

    public async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        var fileCache = GetFileCacheByHash(fileHash)!.ResolvedFilepath;
        return (fileHash, LZ4Wrapper.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(false), 0,
            (int)new FileInfo(fileCache).Length));
    }

    public FileCacheEntity? GetFileCacheByHash(string hash)
    {
        if (_fileCaches.TryGetValue(hash, out var dict))
        {
            var item = dict.Values.OrderBy(p => p.PrefixedFilePath.Contains(PenumbraPrefix) ? 0 : 1).FirstOrDefault();
            if (item != null) return GetValidatedFileCache(item);
        }
        return null;
    }

    private static string NormalizePath(string path)
    => path.Replace("/", "\\", StringComparison.OrdinalIgnoreCase)
           .Replace("\\\\", "\\", StringComparison.Ordinal)
           .ToLowerInvariant();

    private string? TryToPrefixedPath(string normalizedPath)
    {
        var modDir = _ipcManager.Penumbra.ModDirectory;
        if (!string.IsNullOrEmpty(modDir))
        {
            var modNorm = NormalizePath(modDir);
            if (!modNorm.EndsWith("\\", StringComparison.Ordinal)) modNorm += "\\";
            if (normalizedPath.StartsWith(modNorm, StringComparison.Ordinal))
                return PenumbraPrefix + "\\" + normalizedPath.Substring(modNorm.Length);
        }

        var cacheNorm = NormalizePath(_configService.Current.CacheFolder);
        if (!cacheNorm.EndsWith("\\", StringComparison.Ordinal)) cacheNorm += "\\";
        if (normalizedPath.StartsWith(cacheNorm, StringComparison.Ordinal))
            return CachePrefix + "\\" + normalizedPath.Substring(cacheNorm.Length);

        return null;
    }

    private FileCacheEntity? GetFileCacheByPath(string path)
    {
        var normalized = NormalizePath(path);
        var prefixed = TryToPrefixedPath(normalized);

        if (prefixed != null && _byPrefixedPath.TryGetValue(prefixed, out var entry))
            return GetValidatedFileCache(entry);

        // Not found: create entry if the file exists and is in a known folder.
        if (prefixed != null && prefixed.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase))
            return CreateCacheEntry(path);

        return CreateFileEntry(path);
    }



    public Dictionary<string, FileCacheEntity?> GetFileCachesByPaths(string[] paths)
    {
        _getCachesByPathsSemaphore.Wait();

        try
        {
            Dictionary<string, FileCacheEntity?> result = new(StringComparer.OrdinalIgnoreCase);

            foreach (var originalPath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var normalized = NormalizePath(originalPath);
                var prefixed = TryToPrefixedPath(normalized);

                if (prefixed != null && _byPrefixedPath.TryGetValue(prefixed, out var entity))
                {
                    result[originalPath] = GetValidatedFileCache(entity);
                    continue;
                }

                // Cache entry or mod directory entry
                if (prefixed != null && prefixed.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase))
                    result[originalPath] = CreateCacheEntry(originalPath);
                else
                    result[originalPath] = CreateFileEntry(originalPath);
            }

            return result;
        }
        finally
        {
            _getCachesByPathsSemaphore.Release();
        }
    }


    public void RemoveHashedFile(string hash, string prefixedFilePath)
    {
        // Remove from path index if it matches this hash.
        if (_byPrefixedPath.TryGetValue(prefixedFilePath, out var existing)
            && string.Equals(existing.Hash, hash, StringComparison.OrdinalIgnoreCase))
        {
            _byPrefixedPath.TryRemove(prefixedFilePath, out _);
        }

        if (_fileCaches.TryGetValue(hash, out var dict))
        {
            dict.TryRemove(prefixedFilePath, out _);
            _logger.LogTrace("Removed from DB: 1 file (if present) with hash {hash} and path {path}", hash, prefixedFilePath);
            if (dict.IsEmpty)
                _fileCaches.TryRemove(hash, out _);
        }

        MarkCsvDirty();
    }

    public void UpdateHashedFile(FileCacheEntity fileCache, bool computeProperties = true)
    {
        _logger.LogTrace("Updating hash for {path}", fileCache.ResolvedFilepath);
        var oldHash = fileCache.Hash;
        var prefixedPath = fileCache.PrefixedFilePath;
        if (computeProperties)
        {
            var fi = new FileInfo(fileCache.ResolvedFilepath);
            fileCache.Size = fi.Length;
            fileCache.CompressedSize = null;
            fileCache.Hash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
            fileCache.LastModifiedDateTicks = fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        RemoveHashedFile(oldHash, prefixedPath);
        AddHashedFile(fileCache);
    }

    public (FileState State, FileCacheEntity FileCache) ValidateFileCacheEntity(FileCacheEntity fileCache)
    {
        fileCache = ReplacePathPrefixes(fileCache);
        FileInfo fi = new(fileCache.ResolvedFilepath);
        if (!fi.Exists)
        {
            return (FileState.RequireDeletion, fileCache);
        }
        if (!string.Equals(fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            return (FileState.RequireUpdate, fileCache);
        }

        return (FileState.Valid, fileCache);
    }

    public void WriteOutFullCsv()
    {
        MarkCsvDirty();
    }

    public void WriteOutFullCsvImmediate()
    {
        WriteOutFullCsvNow(force: true);
    }

    private void MarkCsvDirty()
    {
        if (_suppressCsvWrites) return;

        System.Threading.Interlocked.Exchange(ref _csvDirty, 1);

        _csvDebounceTimer ??= new Timer(_ =>
        {
            try { WriteOutFullCsvNow(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to write FileCache.csv"); }
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _csvDebounceTimer.Change(_csvDebounceDelay, Timeout.InfiniteTimeSpan);
    }

    private void WriteOutFullCsvNow(bool force = false)
    {
        var wasDirty = System.Threading.Interlocked.Exchange(ref _csvDirty, 0);
        if (!force && wasDirty == 0)
            return;

        lock (_fileWriteLock)
        {
            var entries = _byPrefixedPath.Values
                .OrderBy(f => f.PrefixedFilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            StringBuilder sb = new();
            foreach (var entry in entries)
            {
                sb.Append(entry.Hash.ToLowerInvariant())
                  .Append(CsvSplit).Append(entry.PrefixedFilePath)
                  .Append(CsvSplit).Append(entry.LastModifiedDateTicks)
                  .Append(CsvSplit).Append(entry.Size ?? -1)
                  .Append(CsvSplit).Append(entry.CompressedSize ?? -1)
                  .AppendLine();
            }

            if (File.Exists(_csvPath))
            {
                File.Copy(_csvPath, CsvBakPath, overwrite: true);
            }

            try
            {
                File.WriteAllText(_csvPath, sb.ToString());
                if (File.Exists(CsvBakPath)) File.Delete(CsvBakPath);
            }
            catch
            {
                File.WriteAllText(CsvBakPath, sb.ToString());
            }
        }
    }

    internal FileCacheEntity MigrateFileHashToExtension(FileCacheEntity fileCache, string ext)
    {
        try
        {
            RemoveHashedFile(fileCache.Hash, fileCache.PrefixedFilePath);
            var extensionPath = fileCache.ResolvedFilepath.ToUpper(CultureInfo.InvariantCulture) + "." + ext;
            File.Move(fileCache.ResolvedFilepath, extensionPath, overwrite: true);
            var newHashedEntity = new FileCacheEntity(fileCache.Hash, fileCache.PrefixedFilePath + "." + ext, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            newHashedEntity.SetResolvedFilePath(extensionPath);
            AddHashedFile(newHashedEntity);
            _logger.LogTrace("Migrated from {oldPath} to {newPath}", fileCache.ResolvedFilepath, newHashedEntity.ResolvedFilepath);
            return newHashedEntity;
        }
        catch (Exception ex)
        {
            AddHashedFile(fileCache);
            _logger.LogWarning(ex, "Failed to migrate entity {entity}", fileCache.PrefixedFilePath);
            return fileCache;
        }
    }


    private void AddHashedFile(FileCacheEntity fileCache)
    {
        // Ensure path index stays authoritative (prefixed path uniquely identifies a file location).
        if (_byPrefixedPath.TryGetValue(fileCache.PrefixedFilePath, out var existing)
            && !string.Equals(existing.Hash, fileCache.Hash, StringComparison.OrdinalIgnoreCase))
        {
            // Remove stale mapping from the old hash dictionary.
            if (_fileCaches.TryGetValue(existing.Hash, out var existingDict))
            {
                existingDict.TryRemove(existing.PrefixedFilePath, out _);
                if (existingDict.IsEmpty)
                    _fileCaches.TryRemove(existing.Hash, out _);
            }
        }

        _byPrefixedPath[fileCache.PrefixedFilePath] = fileCache;

        var dict = _fileCaches.GetOrAdd(fileCache.Hash, _ => new ConcurrentDictionary<string, FileCacheEntity>(StringComparer.OrdinalIgnoreCase));
        dict[fileCache.PrefixedFilePath] = fileCache;

        MarkCsvDirty();
    }

    private FileCacheEntity? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? hash = null)
    {
        hash ??= Crypto.GetFileHash(fileInfo.FullName);
        var entity = new FileCacheEntity(hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileInfo.Length);
        entity = ReplacePathPrefixes(entity);
        AddHashedFile(entity);

        var result = GetFileCacheByPath(fileInfo.FullName);
        _logger.LogTrace("Creating cache entity for {name} success: {success}", fileInfo.FullName, (result != null));
        return result;
    }

    private FileCacheEntity? GetValidatedFileCache(FileCacheEntity fileCache)
    {
        var resultingFileCache = ReplacePathPrefixes(fileCache);
        resultingFileCache = Validate(resultingFileCache);
        return resultingFileCache;
    }

    private FileCacheEntity ReplacePathPrefixes(FileCacheEntity fileCache)
    {
        if (fileCache.PrefixedFilePath.StartsWith(PenumbraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(PenumbraPrefix, _ipcManager.Penumbra.ModDirectory, StringComparison.Ordinal));
        }
        else if (fileCache.PrefixedFilePath.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(CachePrefix, _configService.Current.CacheFolder, StringComparison.Ordinal));
        }

        return fileCache;
    }

    private FileCacheEntity? Validate(FileCacheEntity fileCache)
    {
        var file = new FileInfo(fileCache.ResolvedFilepath);
        if (!file.Exists)
        {
            RemoveHashedFile(fileCache.Hash, fileCache.PrefixedFilePath);
            return null;
        }

        if (!string.Equals(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            UpdateHashedFile(fileCache);
        }

        return fileCache;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting FileCacheManager");

        lock (_fileWriteLock)
        {
            try
            {
                _logger.LogInformation("Checking for {bakPath}", CsvBakPath);

                if (File.Exists(CsvBakPath))
                {
                    _logger.LogInformation("{bakPath} found, moving to {csvPath}", CsvBakPath, _csvPath);

                    File.Move(CsvBakPath, _csvPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to move BAK to ORG, deleting BAK");
                try
                {
                    if (File.Exists(CsvBakPath))
                        File.Delete(CsvBakPath);
                }
                catch (Exception ex1)
                {
                    _logger.LogWarning(ex1, "Could not delete bak file");
                }
            }
        }

        if (File.Exists(_csvPath))
        {
            if (!_ipcManager.Penumbra.APIAvailable || string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory))
            {
                _mareMediator.Publish(new NotificationMessage("Penumbra not connected",
                    "Could not load local file cache data. Penumbra is not connected or not properly set up. Please enable and/or configure Penumbra properly to use PlayerSync. After, reload PlayerSync in the Plugin installer.",
                    MareConfiguration.Models.NotificationType.Error));
            }

            _logger.LogInformation("{csvPath} found, parsing", _csvPath);

            _suppressCsvWrites = true;
            bool success = false;
            string[] entries = [];
            int attempts = 0;
            while (!success && attempts < 10)
            {
                try
                {
                    _logger.LogInformation("Attempting to read {csvPath}", _csvPath);
                    entries = File.ReadAllLines(_csvPath);
                    success = true;
                }
                catch (Exception ex)
                {
                    attempts++;
                    _logger.LogWarning(ex, "Could not open {file}, trying again", _csvPath);
                    Thread.Sleep(100);
                }
            }

            if (!entries.Any())
            {
                _logger.LogWarning("Could not load entries from {path}, continuing with empty file cache", _csvPath);
            }

            _logger.LogInformation("Found {amount} files in {path}", entries.Length, _csvPath);

            Dictionary<string, bool> processedFiles = new(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var splittedEntry = entry.Split(CsvSplit, StringSplitOptions.None);
                try
                {
                    var hash = splittedEntry[0].ToUpperInvariant();
                    if (hash.Length != 40) throw new InvalidOperationException("Expected Hash length of 40, received " + hash.Length);
                    var path = splittedEntry[1];
                    var time = splittedEntry[2];

                    if (processedFiles.ContainsKey(path))
                    {
                        _logger.LogWarning("Already processed {file}, ignoring", path);
                        continue;
                    }

                    processedFiles.Add(path, value: true);

                    long size = -1;
                    long compressed = -1;
                    if (splittedEntry.Length > 3)
                    {
                        if (long.TryParse(splittedEntry[3], CultureInfo.InvariantCulture, out long result))
                        {
                            size = result;
                        }
                        if (long.TryParse(splittedEntry[4], CultureInfo.InvariantCulture, out long resultCompressed))
                        {
                            compressed = resultCompressed;
                        }
                    }
                    AddHashedFile(ReplacePathPrefixes(new FileCacheEntity(hash, path, time, size, compressed)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize entry {entry}, ignoring", entry);
                }
            }

            _suppressCsvWrites = false;

            if (processedFiles.Count != entries.Length)
            {
                WriteOutFullCsvImmediate();
            }
        }

        _logger.LogInformation("Started FileCacheManager");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _suppressCsvWrites = false;
        WriteOutFullCsvImmediate();
        _csvDebounceTimer?.Dispose();
        return Task.CompletedTask;
    }
}
