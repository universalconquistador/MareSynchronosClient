using Microsoft.Extensions.Caching.Memory;
using PlayerSync.Utils;

namespace MareSynchronos.FileCache;

// https://www.nuget.org/packages/microsoft.extensions.caching.memory/
// https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-10.0

// If this works, refactor a base class so we can create a cache for comp alt usage? Add to this one?

public sealed class FileCacheMemoryCache : IDisposable
{
    private readonly record struct HashCacheKey(string Hash);
    private readonly record struct PathCacheKey(FileCacheStore Store, string FilePathWithinFileStore);

    private const long DefaultSizeLimit = 20_000; // this is like one "unit" and not the actual memory footprint
    private static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromMinutes(30); // absolute timeout
    private static readonly TimeSpan NegativeAbsoluteExpiration = TimeSpan.FromSeconds(20); // these are db misses
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(5); // timeout if not hits after this time

    private readonly TimeSpan _absoluteExpiration;
    private readonly MemoryCache _cache;
    private readonly TimeSpan _negativeAbsoluteExpiration;
    private readonly TimeSpan _slidingExpiration;
    private readonly long _sizeLimit;

    private int _disposed;
    private long _version; // this is used to avoid race conditions

    public FileCacheMemoryCache(long sizeLimit = DefaultSizeLimit, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpiration = null, TimeSpan? negativeAbsoluteExpiration = null)
    {
        _slidingExpiration = slidingExpiration ?? SlidingExpiration;
        _absoluteExpiration = absoluteExpiration ?? AbsoluteExpiration;
        _negativeAbsoluteExpiration = negativeAbsoluteExpiration ?? NegativeAbsoluteExpiration;
        _sizeLimit = sizeLimit;

        _cache = new MemoryCache(new MemoryCacheOptions
        {
            CompactionPercentage = 0.25,
            ExpirationScanFrequency = TimeSpan.FromMinutes(1),
            SizeLimit = sizeLimit,
            TrackStatistics = true
        });
    }

    public long SizeLimit => _sizeLimit;
    public bool IsMemoryCacheEnabled { get; set; } = true;

    public MemoryCacheStatistics? GetCurrentStatistics()
    {
        return _cache.GetCurrentStatistics();
    }

    /// <summary>
    /// This should be called before a db operation and passed in to a mem cache set operation after the db query.
    /// This ensure the db hasn't changed from what the cache knows about
    /// </summary>
    /// <returns>The current cache version</returns>
    public long GetVersion()
    {
        return Volatile.Read(ref _version);
    }

    public bool TryGetFileCachesByHash(string hash, out FileCacheEntity[] fileCaches)
    {
        var key = new HashCacheKey(StringUtils.NormalizeHash(hash));
        if (_cache.TryGetValue(key, out FileCacheEntity[]? cached) && cached != null)
        {
            fileCaches = cached;

            return true;
        }

        fileCaches = [];

        return false;
    }

    public bool TryGetFileCacheByPath(FileCacheStore store, string filePathWithinFileStore, out FileCacheEntity? fileCache)
    {
        return _cache.TryGetValue(new PathCacheKey(store, StringUtils.NormalizeFilePathWithinFileStore(filePathWithinFileStore)), out fileCache) && fileCache != null;
    }

    /// <summary>
    /// Sets the mem cache for the hash to FileCacheEntities as well as store+path cache entries
    /// </summary>
    /// <param name="hash">The hash to be used as a cache key</param>
    /// <param name="fileCaches">The FileCacheEntities to be stored</param>
    /// <param name="version">The mem cache version</param>
    /// <returns></returns>
    public bool SetFileCachesByHash(string hash, IReadOnlyCollection<FileCacheEntity> fileCaches, long version)
    {
        if (!IsMemoryCacheEnabled)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(fileCaches);
        if (version != GetVersion())
        {
            return false;
        }

        var key = new HashCacheKey(StringUtils.NormalizeHash(hash));
        FileCacheEntity[] entries = [.. fileCaches];
        ClearCachesByHashInternal(key); // remove any existing caches for this hash

        foreach (var fileCache in entries)
        {
            SetFileCacheByPathInternal(fileCache); // we need to also set the caches for the store+path (the thing we care about)
        }

        // we set the hash to the list of available file cache entities (from the db)
        _cache.Set(key, entries, entries.Length == 0 ? CreateNegativeEntryOptions() : CreateEntryOptions(Math.Max(1, entries.LongLength)));

        if (version == GetVersion())
        {
            return true;
        }

        _cache.Remove(key);
        foreach (var fileCache in entries)
        {
            _cache.Remove(CreatePathCacheKey(fileCache.Store, fileCache.FilePathWithinFileStore));
        }

        return false;
    }

    /// <summary>
    /// Sets the cache entry for the store+path for the FileCacheEntity
    /// </summary>
    /// <param name="fileCache">The FileCacheEntity to derive the store+path from and set as the cache value</param>
    /// <param name="version">The version of the mem cache</param>
    /// <returns></returns>
    public bool SetFileCacheByPath(FileCacheEntity fileCache, long version)
    {
        if (!IsMemoryCacheEnabled)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(fileCache);
        if (version != GetVersion())
        {
            return false;
        }

        var key = CreatePathCacheKey(fileCache.Store, fileCache.FilePathWithinFileStore);
        _cache.Set(key, fileCache, CreateEntryOptions(1));

        if (version == GetVersion())
        {
            return true;
        }

        _cache.Remove(key);

        return false;
    }

    /// <summary>
    /// Invalidates a cache entry removing it from the mem cache
    /// </summary>
    /// <param name="fileCache">The FileCacheEntity to invalidate</param>
    public void Invalidate(FileCacheEntity fileCache)
    {
        ArgumentNullException.ThrowIfNull(fileCache);
        Invalidate(fileCache.Hash, fileCache.Store, fileCache.FilePathWithinFileStore);
    }

    /// <summary>
    /// Invalidates a cache entry removing it from the mem cache based on the FileCacheEntity hash, store, and path
    /// </summary>
    /// <param name="hash">The key of the cache entry</param>
    /// <param name="store">Used as a composite for the cache key (store+path)</param>
    /// <param name="filePathWithinFileStore">Used as a composite for the cache key (store+path)</param>
    public void Invalidate(string hash, FileCacheStore store, string filePathWithinFileStore)
    {
        Interlocked.Increment(ref _version);
        ClearCachesByHashInternal(new HashCacheKey(StringUtils.NormalizeHash(hash)));
        ClearCachesByPathInternal(CreatePathCacheKey(store, filePathWithinFileStore));
    }

    /// <summary>
    /// Invalidates a cache entry removing it from the mem cache based on the FileCacheEntity hash, store, and path
    /// This will remove a hash from every store within the mem cache
    /// </summary>
    /// <param name="hash">The key of the cache entry</param>
    /// <param name="filePathWithinFileStore">Used as a composite for the cache key (store+path)</param>
    public void Invalidate(string hash, string filePathWithinFileStore)
    {
        Interlocked.Increment(ref _version);
        ClearCachesByHashInternal(new HashCacheKey(StringUtils.NormalizeHash(hash)));
        ClearCachesByPathInternal(CreatePathCacheKey(FileCacheStore.Penumbra, filePathWithinFileStore));
        ClearCachesByPathInternal(CreatePathCacheKey(FileCacheStore.Cache, filePathWithinFileStore));
    }

    public void Clear()
    {
        Interlocked.Increment(ref _version);
        _cache.Compact(1.0);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Increment(ref _version);
        _cache.Dispose();
    }

    private static PathCacheKey CreatePathCacheKey(FileCacheStore store, string filePathWithinFileStore)
    {
        return new PathCacheKey(store, StringUtils.NormalizeFilePathWithinFileStore(filePathWithinFileStore));
    }

    private MemoryCacheEntryOptions CreateEntryOptions(long size)
    {
        return new MemoryCacheEntryOptions().SetSize(size).SetSlidingExpiration(_slidingExpiration).SetAbsoluteExpiration(_absoluteExpiration);
    }

    private MemoryCacheEntryOptions CreateNegativeEntryOptions()
    {
        return new MemoryCacheEntryOptions().SetSize(1).SetAbsoluteExpiration(_negativeAbsoluteExpiration);
    }

    private void SetFileCacheByPathInternal(FileCacheEntity fileCache)
    {
        _cache.Set(CreatePathCacheKey(fileCache.Store, fileCache.FilePathWithinFileStore), fileCache, CreateEntryOptions(1));
    }

    private void ClearCachesByHashInternal(HashCacheKey key)
    {
        if (_cache.TryGetValue(key, out FileCacheEntity[]? fileCaches) && fileCaches != null)
        {
            foreach (var fileCache in fileCaches)
            {
                _cache.Remove(CreatePathCacheKey(fileCache.Store, fileCache.FilePathWithinFileStore));
            }
        }

        _cache.Remove(key);
    }

    private void ClearCachesByPathInternal(PathCacheKey key)
    {
        if (_cache.TryGetValue(key, out FileCacheEntity? fileCache) && fileCache != null)
        {
            _cache.Remove(new HashCacheKey(StringUtils.NormalizeHash(fileCache.Hash)));
        }

        _cache.Remove(key);
    }
}
