using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
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
        void SetCompressedAlternate(string sourceFileHash, string? compressedAlternateHash, bool neverWillHaveAlternate);
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="AlternateHash">The hash of the compressed alternate file, or <c>null</c> if there is none.</param>
    /// <param name="LastChecked"></param>
    internal record struct CompressedAlternateEntry(string? AlternateHash, DateTimeOffset NextCheck);

    internal class CompressedAlternateManager : MediatorSubscriberBase, IHostedService, ICompressedAlternateManager
    {
        // How many minutes until we re-ask the server whether there are any compressed alternates for a potentially
        // compressable file with no alternate yet
        private const float CacheLifespanMinutes = 2.0f;

        private readonly MareConfigService _configService;
        private readonly ConcurrentDictionary<string, CompressedAlternateEntry> _entryDictionary = new(StringComparer.Ordinal);
        private string? _fileServiceUrl = null;

        public CompressedAlternateManager(ILogger<CompressedAlternateManager> logger, MareConfigService configService, MareMediator mediator)
            : base(logger, mediator)
        {
            _configService = configService;
            Mediator.Subscribe<ConnectedMessage>(this, OnServiceConnected);
        }

        private void OnServiceConnected(ConnectedMessage message)
        {
            var newFileServiceUrl = message.Connection.ServerInfo.FileServerAddress.ToString();
            if (_fileServiceUrl != null && _fileServiceUrl != newFileServiceUrl)
            {
                Logger.LogDebug("Clearing comp alt cache because connecting to a different file server.");
                _entryDictionary.Clear();
            }

            _fileServiceUrl = newFileServiceUrl;
        }

        public void SetCompressedAlternate(string sourceFileHash, string? compressedAlternateHash, bool neverWillHaveAlternate)
        {
            var nextCheckTime = (compressedAlternateHash != null || neverWillHaveAlternate) ? DateTimeOffset.MaxValue : (DateTimeOffset.UtcNow + TimeSpan.FromMinutes(CacheLifespanMinutes));
            _entryDictionary[sourceFileHash] = new CompressedAlternateEntry(compressedAlternateHash, nextCheckTime);
        }

        public bool TryGetCachedCompressedAlternate(string sourceFileHash, out string? compressedAlternateHash)
        {
            if (_entryDictionary.TryGetValue(sourceFileHash, out var entry)
                && (entry.AlternateHash != null || DateTimeOffset.UtcNow < entry.NextCheck))
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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _entryDictionary.Clear();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            UnsubscribeAll();
            return Task.CompletedTask;
        }
    }
}
