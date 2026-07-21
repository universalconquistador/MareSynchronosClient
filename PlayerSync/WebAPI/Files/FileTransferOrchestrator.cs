using MareSynchronos.API.Data;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;

namespace MareSynchronos.WebAPI.Files;

public class FileTransferOrchestrator : DisposableMediatorSubscriberBase
{
    private readonly HttpClient _httpClient;
    private readonly MareConfigService _mareConfig;
    private readonly TokenProvider _tokenProvider;

    private readonly ConcurrentDictionary<string, byte> _hashesReportedError404 = new(StringComparer.OrdinalIgnoreCase);

    // Download slots
    private readonly object _semaphoreModificationLock = new();
    private int _availableDownloadSlots;
    private SemaphoreSlim _downloadSemaphore;
    private int CurrentlyUsedDownloadSlots => _availableDownloadSlots - _downloadSemaphore.CurrentCount;

    // Upload slots
    private readonly object _uploadModificationLock = new();
    private int _availableUploadSlots;
    private SemaphoreSlim _uploadSemaphore;
    private int CurrentlyUsedUploadSlots => _availableUploadSlots - _uploadSemaphore.CurrentCount;

    public FileTransferOrchestrator(ILogger<FileTransferOrchestrator> logger, MareConfigService mareConfig,
        MareMediator mediator, TokenProvider tokenProvider, HttpClient httpClient) : base(logger, mediator)
    {
        _mareConfig = mareConfig;
        _tokenProvider = tokenProvider;
        _httpClient = httpClient;
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PlayerSync", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;

        _availableDownloadSlots = Math.Clamp(mareConfig.Current.ParallelDownloads, 1, 100);
        _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);

        _availableUploadSlots = Math.Clamp(mareConfig.Current.ParallelUploads, 1, 100);
        // temp for now until a better config validation framework can be worked out
        if (_availableDownloadSlots != mareConfig.Current.ParallelDownloads || _availableUploadSlots != mareConfig.Current.ParallelUploads)
        {
            mareConfig.Current.ParallelDownloads = _availableDownloadSlots;
            mareConfig.Current.ParallelUploads = _availableUploadSlots;
            mareConfig.Save();
        }

        _uploadSemaphore = new(_availableUploadSlots, _availableUploadSlots);

        Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            FilesCdnUri = msg.Connection.ServerInfo.FileServerAddress;
        });

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            FilesCdnUri = null;
        });
    }

    public Uri? FilesCdnUri { private set; get; }
    public List<FileTransfer> ForbiddenTransfers { get; } = [];
    public bool IsInitialized => FilesCdnUri != null;
    public HttpRequestHeaders DefaultRequestHeaders => _httpClient.DefaultRequestHeaders;
    public int TimeZoneUtcOffsetMinutes
    {
        get
        {
            int result = LongitudinalRegion.FromLocalSystemTimeZone().UtcOffsetMinutes;

            if (_mareConfig.Current.OverrideCdnTimeZone)
            {
                var overrideTimeZoneId = _mareConfig.Current.OverrideCdnTimeZoneId;
                if (!string.IsNullOrEmpty(overrideTimeZoneId) && LongitudinalRegion.FromTimeZoneId(overrideTimeZoneId) is var region && region.HasValue)
                {
                    result = region.Value.UtcOffsetMinutes;
                }
                else
                {
                    result = 0;
                }
            }

            return result;
        }
    }

    public void ReleaseDownloadSlot()
    {
        try
        {
            _downloadSemaphore.Release();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        catch (SemaphoreFullException)
        {
            // ignore
        }
    }

    public void ReleaseUploadSlot()
    {
        try
        {
            _uploadSemaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // ignore
        }
    }

    public bool TryAddHashReportedError404(string fileHash)
    {
        return _hashesReportedError404.TryAdd(fileHash, 0);
    }

    private sealed class DownloadTask()
    {
        public Task Task { get; set; } = null!;
        public Progress<long> Progress { get; set; } = null!;
        public DownloadStatus CurrentStatus { get; set; } = DownloadStatus.Initializing;
        public event Action<DownloadStatus>? StatusUpdated;

        public void RaiseStatusUpdated(DownloadStatus status)
        {
            StatusUpdated?.Invoke(status);
            CurrentStatus = status;
        }
    }
    private readonly ConcurrentDictionary<string, DownloadTask> _runningDownloads = new();

    /// <summary>
    /// Starts the given download function if none is currently running for the given hash, or just
    /// attaches the given callbacks to the existing download if one is already in progress for the hash.
    /// </summary>
    /// <param name="hash">The mod file's hash.</param>
    /// <param name="progressCallback">A callback that is invoked when bytes of the file are downloaded.</param>
    /// <param name="statusCallback">A callback that is invoked when the status of the file download changes.</param>
    /// <param name="downloadFunc">The function to actually perform the download.</param>
    /// <returns>A Task that can be used to wait for the download to complete.</returns>
    public Task GetOrStartDownloadTask(string hash, EventHandler<long> progressCallback, Action<DownloadStatus> statusCallback, Func<string, IProgress<long>, Action<DownloadStatus>, Task> downloadFunc)
    {
        var result = _runningDownloads.AddOrUpdate(hash, (hash) =>
        {
            // If we are starting a new download, set up the Progress, attach handlers, and kick off the download
            var progress = new Progress<long>(value => progressCallback.Invoke(null, value));
            var result = new DownloadTask() { Progress = progress };
            result.StatusUpdated += statusCallback;
            Func<string, Progress<long>, Task> func = async (hash, progress) =>
            {
                try
                {
                    await downloadFunc.Invoke(hash, progress, status => result.RaiseStatusUpdated(status));
                }
                finally
                {
                    _runningDownloads.TryRemove(hash, out _);
                }
            };
            result.Task = func.Invoke(hash, progress);

            return result;
        }, (hash, task) =>
        {
            Logger.LogDebug("Additional character waiting on {hash}!", hash);

            // Otherwise just attach to the callbacks on the existing download
            task.Progress.ProgressChanged += progressCallback;
            task.StatusUpdated += statusCallback;

            // If we're joining the existing download late, notify about its status
            statusCallback.Invoke(task.CurrentStatus);

            return task;
        });

        return result.Task;
    }

    public async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead,
        bool withToken = true)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        return await SendRequestInternalAsync(requestMessage, ct, httpCompletionOption, withToken).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestAsync<T>(HttpMethod method, Uri uri, T content, CancellationToken ct,
        bool withToken = true) where T : class
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        if (content is not ByteArrayContent)
            requestMessage.Content = JsonContent.Create(content);
        else
            requestMessage.Content = content as ByteArrayContent;
        return await SendRequestInternalAsync(requestMessage, ct, withToken: withToken).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestStreamAsync(HttpMethod method, Uri uri, ProgressableStreamContent content,
        CancellationToken ct, bool withToken = true)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = content;
        return await SendRequestInternalAsync(requestMessage, ct, withToken: withToken).ConfigureAwait(false);
    }

    public async Task WaitForDownloadSlotAsync(CancellationToken token)
    {
        lock (_semaphoreModificationLock)
        {
            if (_availableDownloadSlots != _mareConfig.Current.ParallelDownloads && _availableDownloadSlots == _downloadSemaphore.CurrentCount)
            {
                _availableDownloadSlots = _mareConfig.Current.ParallelDownloads;
                _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);
            }
        }

        await _downloadSemaphore.WaitAsync(token).ConfigureAwait(false);
        Mediator.Publish(new DownloadLimitChangedMessage());
    }

    public long DownloadLimitPerSlot()
    {
        var limit = _mareConfig.Current.DownloadSpeedLimitInBytes;
        if (limit <= 0) return 0;
        limit = _mareConfig.Current.DownloadSpeedType switch
        {
            MareConfiguration.Models.DownloadSpeeds.Bps => limit,
            MareConfiguration.Models.DownloadSpeeds.KBps => limit * 1024,
            MareConfiguration.Models.DownloadSpeeds.MBps => limit * 1024 * 1024,
            _ => limit,
        };
        var currentUsedDlSlots = CurrentlyUsedDownloadSlots;
        var avaialble = _availableDownloadSlots;
        var currentCount = _downloadSemaphore.CurrentCount;
        var dividedLimit = limit / (currentUsedDlSlots == 0 ? 1 : currentUsedDlSlots);
        if (dividedLimit < 0)
        {
            Logger.LogWarning("Calculated Bandwidth Limit is negative, returning Infinity: {value}, CurrentlyUsedDownloadSlots is {currentSlots}, " +
                "DownloadSpeedLimit is {limit}, available slots: {avail}, current count: {count}", dividedLimit, currentUsedDlSlots, limit, avaialble, currentCount);
            return long.MaxValue;
        }
        return Math.Clamp(dividedLimit, 1, long.MaxValue);
    }

    public async Task WaitForUploadSlotAsync(CancellationToken token)
    {
        lock (_semaphoreModificationLock)
        {
            if (_availableUploadSlots != _mareConfig.Current.ParallelUploads && _availableUploadSlots == _uploadSemaphore.CurrentCount)
            {
                _availableUploadSlots = _mareConfig.Current.ParallelUploads;
                _uploadSemaphore = new(_availableUploadSlots, _availableUploadSlots);
            }
        }

        await _uploadSemaphore.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRequestInternalAsync(HttpRequestMessage requestMessage,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, bool withToken = true)
    {
        if (withToken)
        {
            var token = await _tokenProvider.GetToken().ConfigureAwait(false);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            requestMessage.Headers.Authorization = null;
        }

        if (requestMessage.Content != null && requestMessage.Content is not StreamContent && requestMessage.Content is not ByteArrayContent)
        {
            var content = await ((JsonContent)requestMessage.Content).ReadAsStringAsync().ConfigureAwait(false);
            Logger.LogDebug("Sending {method} to {uri} (Content: {content})", requestMessage.Method, requestMessage.RequestUri, content);
        }
        else
        {
            Logger.LogDebug("Sending {method} to {uri}", requestMessage.Method, requestMessage.RequestUri);
        }

        try
        {
            if (ct != null)
                return await _httpClient.SendAsync(requestMessage, httpCompletionOption, ct.Value).ConfigureAwait(false);
            return await _httpClient.SendAsync(requestMessage, httpCompletionOption).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during SendRequestInternal for {uri}", requestMessage.RequestUri);
            throw;
        }
    }
}