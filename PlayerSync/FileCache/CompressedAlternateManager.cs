using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace PlayerSync.FileCache
{
    /// <summary>
    /// Tracks the availability and application of compressed alternate versions of files,
    /// and caches these to disk.
    /// </summary>
    public interface ICompressedAlternateManager
    {
        bool TryGetCachedCompressedAlternate(string sourceFileHash, out string? compressedAlternateHash);
        void SetCompressedAlternate(string sourceFileHash, string? compressedAlternateHash);
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="AlternateHash">The hash of the compressed alternate file, or <c>null</c> if there is none.</param>
    /// <param name="LastChecked"></param>
    internal record struct CompressedAlternateEntry(string? AlternateHash, DateTimeOffset LastChecked);

    internal class CompressedAlternateManager : IHostedService, ICompressedAlternateManager
    {
        // How many minutes until we re-ask the server whether there are any compressed alternates again
        private const float CacheLifespanMinutes = 60.0f;
        // How many seconds between writing the cache of compressed alternates to disk (if there are any changes to write)
        private const float CacheFlushSeconds = 15.0f;

        private readonly ILogger _logger;
        private readonly MareConfigService _configService;
        private readonly ConcurrentDictionary<string, CompressedAlternateEntry> _entryDictionary = new(StringComparer.Ordinal);
        private volatile bool _hasChanges = false;

        private readonly string _cacheFilename;
        private Timer? _cacheWriteTimer;

        public CompressedAlternateManager(ILogger<CompressedAlternateManager> logger, MareConfigService configService)
        {
            _logger = logger;
            _configService = configService;

            _cacheFilename = Path.Combine(_configService.ConfigurationDirectory, "CompressedAlternateCache.json");
            _hasChanges = false;
        }

        public void SetCompressedAlternate(string sourceFileHash, string? compressedAlternateHash)
        {
            _entryDictionary.AddOrUpdate(sourceFileHash, new CompressedAlternateEntry(compressedAlternateHash, DateTimeOffset.UtcNow), (key, existing) => new CompressedAlternateEntry(compressedAlternateHash, DateTimeOffset.UtcNow));
            _hasChanges = true;
        }

        public bool TryGetCachedCompressedAlternate(string sourceFileHash, out string? compressedAlternateHash)
        {
            if (_entryDictionary.TryGetValue(sourceFileHash, out var entry)
                && (entry.AlternateHash != null || (DateTimeOffset.UtcNow - entry.LastChecked).TotalMinutes < CacheLifespanMinutes))
            {
                compressedAlternateHash = entry.AlternateHash;
                return true;
            }
            else
            {
                compressedAlternateHash = null;
                return false;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _entryDictionary.Clear();
            await ReadCacheFile().ConfigureAwait(false);
            _cacheWriteTimer = new Timer(FlushCache, state: null, TimeSpan.FromSeconds(CacheFlushSeconds), TimeSpan.FromSeconds(CacheFlushSeconds));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cacheWriteTimer != null)
            {
                await _cacheWriteTimer.DisposeAsync().ConfigureAwait(false);
            }
        }

        private void FlushCache(object? _)
        {
            if (_hasChanges)
            {
                _hasChanges = false;
                WriteCacheFile();

                _logger.LogDebug("Wrote {count} cached compressed alternate entries to {filename}.", _entryDictionary.Count, _cacheFilename);
            }
        }

        private void WriteCacheFile()
        {
            //using (var stream = new FileStream(_cacheFilename, FileMode.Create))
            //using (var writer = new Utf8JsonWriter(stream))
            //{
            //    writer.WriteStartObject();

            //    foreach (var cachePair in _entryDictionary)
            //    {
            //        writer.WritePropertyName(cachePair.Key);
            //        JsonSerializer.Serialize(writer, cachePair.Value);
            //    }

            //    writer.WriteEndObject();
            //}
        }

        private async Task ReadCacheFile()
        {
            //try
            //{
            //    using (var stream = new FileStream(_cacheFilename, FileMode.Open))
            //    {
            //        var entries = await JsonSerializer.DeserializeAsync<Dictionary<string, CompressedAlternateEntry>>(stream).ConfigureAwait(false);
            //        if (entries != null)
            //        {
            //            foreach (var pair in entries)
            //            {
            //                _entryDictionary[pair.Key] = pair.Value;
            //            }
            //        }
            //        _logger.LogDebug("Loaded {count} alternate cache entries from {filename}.", entries?.Count.ToString() ?? "null", _cacheFilename);
            //    }
            //}
            //catch (FileNotFoundException)
            //{
            //    _logger.LogDebug("Compressed alternate cache file {filename} did not exist when trying to load it.", _cacheFilename);
            //}
            //catch (Exception ex)
            //{
            //    _logger.LogError(ex, "Failed to read compressed alternate cache file!");
            //}
        }
    }
}
