using Dalamud.Utility;
using K4os.Compression.LZ4.Legacy;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using PlayerSync.FileCache;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace MareSynchronos.WebAPI.Files;

public partial class FileDownloadManager : DisposableMediatorSubscriberBase
{
    private readonly ConcurrentDictionary<string, FileDownloadStatus> _downloadStatus;
    private readonly FileCompactor _fileCompactor;
    private readonly FileCacheManager _fileDbManager;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly ICompressedAlternateManager _compressedAlternateManager;

    private readonly object _downloadInfoLock = new object();
    private readonly List<ThrottledStream> _activeDownloadStreams;
    private static int _downloadHaltRefCount;

    public FileDownloadManager(ILogger<FileDownloadManager> logger, MareMediator mediator,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor, ICompressedAlternateManager compressedAlternateManager) : base(logger, mediator)
    {
        _downloadStatus = new ConcurrentDictionary<string, FileDownloadStatus>(StringComparer.Ordinal);
        _orchestrator = orchestrator;
        _fileDbManager = fileCacheManager;
        _fileCompactor = fileCompactor;
        _compressedAlternateManager = compressedAlternateManager;
        _activeDownloadStreams = [];

        Mediator.Subscribe<DownloadLimitChangedMessage>(this, (msg) =>
        {
            lock (_downloadInfoLock)
            {
                if (!_activeDownloadStreams.Any()) return;
                var newLimit = _orchestrator.DownloadLimitPerSlot();
                Logger.LogTrace("Setting new Download Speed Limit to {newLimit}", newLimit);
                foreach (var stream in _activeDownloadStreams)
                {
                    stream.BandwidthLimit = newLimit;
                }
            }
        });
    }

    public List<DownloadFileTransfer> CurrentDownloads { get; private set; } = [];

    public List<FileTransfer> ForbiddenTransfers => _orchestrator.ForbiddenTransfers;

    public bool IsDownloading => !CurrentDownloads.Any();

    public static void MungeBuffer(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; ++i)
        {
            buffer[i] ^= 42;
        }
    }

    public void ClearDownload()
    {
        CurrentDownloads.Clear();
        _downloadStatus.Clear();
    }

    public async Task DownloadFiles(GameObjectHandler gameObject, List<FileReplacementData> fileReplacementDto, Dictionary<string, string> compressionSubstitutions, CancellationToken ct)
    {
        var shouldPublishHalt = Interlocked.Increment(ref _downloadHaltRefCount) == 1;
        if (shouldPublishHalt)
            SafePublish(() => Mediator.Publish(new HaltScanMessage(nameof(DownloadFiles))),
                        "HaltScan(DownloadFiles)");

        try
        {
            await DownloadFilesInternal(gameObject, fileReplacementDto, compressionSubstitutions, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ClearDownload();
            Logger.LogInformation("DownloadFiles cancelled");
        }
        catch (Exception ex)
        {
            ClearDownload();
            Logger.LogError(ex, "DownloadFiles failed unexpectedly");
            // swallow to reach finally and decrement the refcount
        }
        finally
        {
            SafePublish(() => Mediator.Publish(new DownloadFinishedMessage(gameObject)), "DownloadFinishedMessage");

            var newCount = Interlocked.Decrement(ref _downloadHaltRefCount);
            if (newCount == 0)
                SafePublish(() => Mediator.Publish(new ResumeScanMessage(nameof(DownloadFiles))), "ResumeScan(DownloadFiles)");
            else if (newCount < 0)
            {
                Interlocked.Exchange(ref _downloadHaltRefCount, 0);
                Logger.LogError("DownloadFiles halt refcount went negative; resetting to 0");
                SafePublish(() => Mediator.Publish(new ResumeScanMessage(nameof(DownloadFiles))), "ResumeScan(DownloadFiles/reset)");
            }
        }
    }

    private void SafePublish(Action publish, string what)
    {
        try { publish(); }
        catch (Exception ex) { Logger.LogWarning(ex, "{what} publish threw", what); }
    }

    protected override void Dispose(bool disposing)
    {
        ClearDownload();
        lock (_downloadInfoLock)
        {
            foreach (var stream in _activeDownloadStreams.ToList())
            {
                try
                {
                    stream.Dispose();
                }
                catch
                {
                    // do nothing
                    //
                }
            }
        }
        base.Dispose(disposing);
    }

    private delegate void DownloadDataCallback(Span<byte> data);

    private async Task DownloadFileThrottled(Uri requestUrl, string destinationFilename, IProgress<long> progress, DownloadDataCallback? callback, CancellationToken ct, bool withToken = true)
    {
        HttpResponseMessage response = null!;
        try
        {
            response = await _orchestrator.SendRequestAsync(HttpMethod.Get, requestUrl, ct, HttpCompletionOption.ResponseHeadersRead, withToken).ConfigureAwait(false);

            var headersBuilder = new StringBuilder();
            if (response.RequestMessage != null)
            {
                headersBuilder.AppendLine("DefaultRequestHeaders:");
                foreach (var header in _orchestrator.DefaultRequestHeaders)
                {
                    foreach (var value in header.Value)
                    {
                        headersBuilder.AppendLine($"\"{header.Key}\": \"{value}\"");
                    }
                }
                headersBuilder.AppendLine("RequestMessage.Headers:");
                foreach (var header in response.RequestMessage.Headers)
                {
                    foreach (var value in header.Value)
                    {
                        headersBuilder.AppendLine($"\"{header.Key}\": \"{value}\"");
                    }
                }
                if (response.RequestMessage.Content != null)
                {
                    headersBuilder.AppendLine("RequestMessage.Content.Headers:");
                    foreach (var header in response.RequestMessage.Content.Headers)
                    {
                        foreach (var value in header.Value)
                        {
                            headersBuilder.AppendLine($"\"{header.Key}\": \"{value}\"");
                        }
                    }
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                // Dump some helpful debugging info
                string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Logger.LogWarning("Unsuccessful status code for {requestUrl} is {statusCode}, request headers: \n{headers}\n, response text: \n\"{responseText}\"", requestUrl, response.StatusCode, headersBuilder.ToString(), responseText);

                // Raise an exception etc
                response.EnsureSuccessStatusCode();
            }
            else
            {
                Logger.LogDebug("Successful response for {requestUrl} is {statusCode}, request headers: \n{headers}", requestUrl, response.StatusCode, headersBuilder.ToString());
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Error during download of {requestUrl}, HttpStatusCode: {code}", requestUrl, ex.StatusCode);
            if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                throw new InvalidDataException($"Http error {ex.StatusCode} (cancelled: {ct.IsCancellationRequested}): {requestUrl}", ex);
            }

            return;
        }

        ThrottledStream? stream = null;
        try
        {
            var fileStream = File.Create(destinationFilename);
            await using (fileStream.ConfigureAwait(false))
            {
                var bufferSize = response.Content.Headers.ContentLength > 1024 * 1024 ? 65536 : 8196;
                var buffer = new byte[bufferSize];

                var bytesRead = 0;
                var limit = _orchestrator.DownloadLimitPerSlot();
                Logger.LogTrace("Starting Download with a speed limit of {limit} to {tempPath}", limit, destinationFilename);
                stream = new ThrottledStream(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), limit);

                lock (_downloadInfoLock)
                {
                    _activeDownloadStreams.Add(stream);
                }

                while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    if (callback != null)
                    {
                        callback.Invoke(buffer.AsSpan(0, bytesRead));
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

                    progress.Report(bytesRead);
                }

                Logger.LogDebug("{requestUrl} downloaded to {tempPath}", requestUrl, destinationFilename);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                if (!destinationFilename.IsNullOrEmpty())
                    File.Delete(destinationFilename);
            }
            catch
            {
                // ignore if file deletion fails
            }
            throw;
        }
        finally
        {
            if (stream != null)
            {
                lock (_downloadInfoLock)
                {
                    _activeDownloadStreams.Remove(stream);
                }

                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<List<DownloadFileTransfer>> InitiateDownloadList(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CompressedAlternateUsage compressedAlternateUsage, Dictionary<string, string> compressedSubstitutions, HashSet<string> locallyPresentFiles, CancellationToken ct)
    {
        Logger.LogDebug("Download start: {id}", gameObjectHandler.Name);

        List<DownloadFileDto> downloadFileInfoFromService =
        [
            .. await FilesGetSizes(fileReplacement.Select(f => f.Hash).Distinct(StringComparer.Ordinal).ToList(), ct).ConfigureAwait(false),
        ];

        Logger.LogDebug("Files with size 0 or less: {files}", string.Join(", ", downloadFileInfoFromService.Where(f => f.Size <= 0).Select(f => f.Hash)));

        foreach (var dto in downloadFileInfoFromService.Where(c => c.IsForbidden))
        {
            if (!_orchestrator.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal)))
            {
                _orchestrator.ForbiddenTransfers.Add(new DownloadFileTransfer(dto));
            }
        }

        for (int i = 0; i < downloadFileInfoFromService.Count; i++)
        {
            var file = downloadFileInfoFromService[i];

            _compressedAlternateManager.SetCompressedAlternate(file.Hash, file.CompressedAlternateFileDownload?.Hash, file.WillNotBeCompressed);
            bool usingAlternate = false;
            if (file.CompressedAlternateFileDownload != null)
            {
                // Assume that compressed alternates do not themselves have a compressed alternate
                _compressedAlternateManager.SetCompressedAlternate(file.CompressedAlternateFileDownload.Hash, compressedAlternateHash: null, neverWillHaveAlternate: true);

                // If the compressed alternate usage requests it, download the alternate instead.
                if (compressedAlternateUsage == CompressedAlternateUsage.CompressedNewDownloads || compressedAlternateUsage == CompressedAlternateUsage.AlwaysCompressed)
                {
                    usingAlternate = true;
                    Logger.LogDebug("Preparing to download compressed file {compressed} instead of original file {original}.", file.CompressedAlternateFileDownload.Hash, file.Hash);
                    downloadFileInfoFromService[i] = file.CompressedAlternateFileDownload;
                    compressedSubstitutions[file.Hash] = file.CompressedAlternateFileDownload.Hash;

                    if (!locallyPresentFiles.Contains(file.CompressedAlternateFileDownload.Hash) && _fileDbManager.GetFileCacheByHash(file.CompressedAlternateFileDownload.Hash) != null)
                    {
                        Logger.LogDebug("But compressed alternate {file} is already downloaded! Not downloading again.", file.CompressedAlternateFileDownload.Hash);
                        locallyPresentFiles.Add(file.CompressedAlternateFileDownload.Hash);
                    }
                }
            }

            // If we were asked to download a locally present file, we were just checking if it has an alternate; no need to redownload it.
            // If we were asked to download a file and we will use its compressed alternate which is already present then no need to redownload it.
            if ((locallyPresentFiles.Contains(file.Hash) && !usingAlternate)
                || (usingAlternate && locallyPresentFiles.Contains(downloadFileInfoFromService[i].Hash)))
            {
                downloadFileInfoFromService.RemoveAt(i);
                i--;
            }
        }

        CurrentDownloads = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
            .Where(d => d.CanBeTransferred).ToList();

        return CurrentDownloads;
    }

    private async Task DownloadFilesInternal(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, Dictionary<string, string> compressionSubstitutions, CancellationToken ct)
    {
        // Separate out the files with direct download URLs
        var directDownloads = CurrentDownloads.Where(download => !string.IsNullOrEmpty(download.DirectDownloadUrl)).ToList();

        // Create download status trackers for the direct downloads
        foreach (var directDownload in directDownloads)
        {
            _downloadStatus[directDownload.DirectDownloadUrl!] = new FileDownloadStatus()
            {
                DownloadStatus = DownloadStatus.Initializing,
                TotalBytes = directDownload.Total,
                TotalFiles = 1,
                TransferredBytes = 0,
                TransferredFiles = 0
            };
        }

        Logger.LogInformation("Downloading {direct} files directly.", directDownloads.Count);
        if (directDownloads.Count < CurrentDownloads.Count)
        {
            Logger.LogWarning("NOTE: {legacy} files did not have direct download URLs and cannot be downloaded.", CurrentDownloads.Count - directDownloads.Count);
        }

        Mediator.Publish(new DownloadStartedMessage(gameObjectHandler, _downloadStatus));

        // Start downloading each of the direct downloads
        var directDownloadsTask = directDownloads.Count == 0 ? Task.CompletedTask : Parallel.ForEachAsync(directDownloads, new ParallelOptions()
        {
            MaxDegreeOfParallelism = directDownloads.Count,
            CancellationToken = ct,
        },
        async (directDownload, token) =>
        {
            if (!_downloadStatus.TryGetValue(directDownload.DirectDownloadUrl!, out var downloadTracker))
                return;

            Progress<long> progress = new((bytesDownloaded) =>
            {
                try
                {
                    if (!_downloadStatus.TryGetValue(directDownload.DirectDownloadUrl!, out FileDownloadStatus? value)) return;
                    value.TransferredBytes += bytesDownloaded;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not set download progress");
                }
            });

            try
            {
                downloadTracker.DownloadStatus = DownloadStatus.WaitingForSlot;
                await _orchestrator.WaitForDownloadSlotAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                Logger.LogDebug("{hash}: Detected cancellation of file queued for direct download, canceling.", directDownload.Hash);
                ClearDownload();
                return;
            }

            var tempFilename = _fileDbManager.GetCacheFilePath(directDownload.Hash, "bin");
            try
            {
                // Download the compressed file directly
                downloadTracker.DownloadStatus = DownloadStatus.Downloading;
                Logger.LogDebug("Beginning direct download of {hash} from {url}", directDownload.Hash, directDownload.DirectDownloadUrl!);
                await DownloadFileThrottled(new Uri(directDownload.DirectDownloadUrl!), tempFilename, progress, null, token, withToken: false).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                Logger.LogDebug("{hash}: Detected cancellation of direct download, discarding file.", directDownload.Hash);
                _orchestrator.ReleaseDownloadSlot();
                File.Delete(tempFilename);
                Logger.LogDebug(ex, "{hash}: Error during direct download.", directDownload.Hash);
                ClearDownload();
                return;
            }
            catch (Exception ex)
            {
                _orchestrator.ReleaseDownloadSlot();
                File.Delete(tempFilename);
                Logger.LogError(ex, "{hash}: Error during direct download.", directDownload.Hash);
                ClearDownload();
                return;
            }

            // Decompress from tempFilename to finalFilename
            // TODO: Really we shouldn't stream to a temp file only to read it all into one buffer to decompress and write back out
            downloadTracker.TransferredFiles = 1;
            downloadTracker.DownloadStatus = DownloadStatus.Decompressing;

            try
            {
                var fileExtension = fileReplacement.First(f => string.Equals(compressionSubstitutions.GetValueOrDefault(f.Hash, f.Hash), directDownload.Hash, StringComparison.OrdinalIgnoreCase)).GamePaths[0].Split(".")[^1];
                var finalFilename = _fileDbManager.GetCacheFilePath(directDownload.Hash, fileExtension);
                Logger.LogDebug("Decompressing direct download {hash} from {compressedFile} to {finalFile}", directDownload.Hash, tempFilename, finalFilename);
                byte[] compressedBytes = await File.ReadAllBytesAsync(tempFilename).ConfigureAwait(false);
                var decompressedBytes = LZ4Wrapper.Unwrap(compressedBytes);
                await _fileCompactor.WriteAllBytesAsync(finalFilename, decompressedBytes, CancellationToken.None).ConfigureAwait(false);
                PersistFileToStorage(directDownload.Hash, finalFilename);
                Logger.LogDebug("Finished direct download of {hash}.", directDownload.Hash);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception downloading {hash} from {url}", directDownload.Hash, directDownload.DirectDownloadUrl!);
            }
            finally
            {
                _orchestrator.ReleaseDownloadSlot();
                File.Delete(tempFilename);
            }
        });

        // Wait for all the direct downloads to complete
        await directDownloadsTask.ConfigureAwait(false);

        Logger.LogDebug("Download end: {id}", gameObjectHandler);

        ClearDownload();
    }

    private async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes, CancellationToken ct)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        var response = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.ServerFilesGetSizesFullPath(_orchestrator.FilesCdnUri!, _orchestrator.TimeZoneUtcOffsetMinutes), hashes, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<DownloadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? [];
    }

    private void PersistFileToStorage(string fileHash, string filePath)
    {
        var fi = new FileInfo(filePath);
        Func<DateTime> RandomDayInThePast()
        {
            DateTime start = new(1995, 1, 1, 1, 1, 1, DateTimeKind.Local);
            Random gen = new();
            int range = (DateTime.Today - start).Days;
            return () => start.AddDays(gen.Next(range));
        }

        fi.CreationTime = RandomDayInThePast().Invoke();
        fi.LastAccessTime = DateTime.Today;
        fi.LastWriteTime = RandomDayInThePast().Invoke();
        try
        {
            var entry = _fileDbManager.CreateCacheEntry(filePath);
            if (entry != null && !string.Equals(entry.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogError("Hash mismatch after extracting, got {hash}, expected {expectedHash}, deleting file", entry.Hash, fileHash);
                File.Delete(filePath);
                _fileDbManager.RemoveHashedFile(entry.Hash, entry.PrefixedFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error creating cache entry");
        }
    }
}
