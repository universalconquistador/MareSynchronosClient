using Dalamud.Utility;
using K4os.Compression.LZ4.Legacy;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace MareSynchronos.WebAPI.Files;

public partial class FileDownloadManager : DisposableMediatorSubscriberBase
{
    private readonly Dictionary<string, FileDownloadStatus> _downloadStatus;
    private readonly FileCompactor _fileCompactor;
    private readonly FileCacheManager _fileDbManager;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly List<ThrottledStream> _activeDownloadStreams;

    public FileDownloadManager(ILogger<FileDownloadManager> logger, MareMediator mediator,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor) : base(logger, mediator)
    {
        _downloadStatus = new Dictionary<string, FileDownloadStatus>(StringComparer.Ordinal);
        _orchestrator = orchestrator;
        _fileDbManager = fileCacheManager;
        _fileCompactor = fileCompactor;
        _activeDownloadStreams = [];

        Mediator.Subscribe<DownloadLimitChangedMessage>(this, (msg) =>
        {
            if (!_activeDownloadStreams.Any()) return;
            var newLimit = _orchestrator.DownloadLimitPerSlot();
            Logger.LogTrace("Setting new Download Speed Limit to {newLimit}", newLimit);
            foreach (var stream in _activeDownloadStreams)
            {
                stream.BandwidthLimit = newLimit;
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

    public async Task DownloadFiles(GameObjectHandler gameObject, List<FileReplacementData> fileReplacementDto, CancellationToken ct)
    {
        Mediator.Publish(new HaltScanMessage(nameof(DownloadFiles)));
        try
        {
            await DownloadFilesInternal(gameObject, fileReplacementDto, ct).ConfigureAwait(false);
        }
        catch
        {
            ClearDownload();
        }
        finally
        {
            Mediator.Publish(new DownloadFinishedMessage(gameObject));
            Mediator.Publish(new ResumeScanMessage(nameof(DownloadFiles)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        ClearDownload();
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
        base.Dispose(disposing);
    }

    private static byte MungeByte(int byteOrEof)
    {
        if (byteOrEof == -1)
        {
            throw new EndOfStreamException();
        }

        return (byte)(byteOrEof ^ 42);
    }

    private static (string fileHash, long fileLengthBytes) ReadBlockFileHeader(FileStream fileBlockStream)
    {
        List<char> hashName = [];
        List<char> fileLength = [];
        var separator = (char)MungeByte(fileBlockStream.ReadByte());
        if (separator != '#') throw new InvalidDataException("Data is invalid, first char is not #");

        bool readHash = false;
        while (true)
        {
            int readByte = fileBlockStream.ReadByte();
            if (readByte == -1)
                throw new EndOfStreamException();

            var readChar = (char)MungeByte(readByte);
            if (readChar == ':')
            {
                readHash = true;
                continue;
            }
            if (readChar == '#') break;
            if (!readHash) hashName.Add(readChar);
            else fileLength.Add(readChar);
        }
        return (string.Join("", hashName), long.Parse(string.Join("", fileLength)));
    }

    private async Task DownloadAndMungeFileHttpClient(string downloadGroup, Guid requestId, List<DownloadFileTransfer> fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
    {
        Logger.LogDebug("GUID {requestId} on server {uri} for files {files}", requestId, fileTransfer[0].DownloadUri, string.Join(", ", fileTransfer.Select(c => c.Hash).ToList()));

        await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

        _downloadStatus[downloadGroup].DownloadStatus = DownloadStatus.Downloading;

        var requestUrl = MareFiles.CacheGetFullPath(fileTransfer[0].DownloadUri, requestId);

        Logger.LogDebug("Downloading {requestUrl} for request {id}", requestUrl, requestId);

        await DownloadFileThrottled(requestUrl, tempPath, progress, MungeBuffer, ct, withToken: true).ConfigureAwait(false);
    }

    private delegate void DownloadDataCallback(Span<byte> data);

    private async Task DownloadFileThrottled(Uri requestUrl, string destinationFilename, IProgress<long> progress, DownloadDataCallback? callback, CancellationToken ct, bool withToken = true)
    {
        HttpResponseMessage response = null!;
        try
        {
            response = await _orchestrator.SendRequestAsync(HttpMethod.Get, requestUrl, ct, HttpCompletionOption.ResponseHeadersRead, withToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Error during download of {requestUrl}, HttpStatusCode: {code}", requestUrl, ex.StatusCode);
            if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                throw new InvalidDataException($"Http error {ex.StatusCode} (cancelled: {ct.IsCancellationRequested}): {requestUrl}", ex);
            }
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
                _activeDownloadStreams.Add(stream);
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
                _activeDownloadStreams.Remove(stream);
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<List<DownloadFileTransfer>> InitiateDownloadList(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
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

        CurrentDownloads = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
            .Where(d => d.CanBeTransferred).ToList();

        return CurrentDownloads;
    }

    private async Task DownloadFilesInternal(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
    {
        // Separate out the files with direct download URLs
        var directDownloads = new List<DownloadFileTransfer>();
        var batchDownloads = new List<DownloadFileTransfer>();
        foreach (var download in CurrentDownloads)
        {
            if (!string.IsNullOrEmpty(download.DirectDownloadUrl))
            {
                directDownloads.Add(download);
            }
            else
            {
                batchDownloads.Add(download);
            }
        }

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

        var downloadBatches = batchDownloads.GroupBy(f => f.DownloadUri.Host + ":" + f.DownloadUri.Port, StringComparer.Ordinal).ToArray();

        Logger.LogInformation("Downloading {direct} files directly, and {batchtotal} in {batches} batches.", directDownloads.Count, batchDownloads.Count, downloadBatches.Count());

        // Create download status trackers for the batch downloads
        foreach (var downloadBatch in downloadBatches)
        {
            _downloadStatus[downloadBatch.Key] = new FileDownloadStatus()
            {
                DownloadStatus = DownloadStatus.Initializing,
                TotalBytes = downloadBatch.Sum(c => c.Total),
                TotalFiles = 1,
                TransferredBytes = 0,
                TransferredFiles = 0
            };
        }

        Mediator.Publish(new DownloadStartedMessage(gameObjectHandler, _downloadStatus));

        // Start downloading each of the batches
        var batchDownloadsTask = downloadBatches.Length == 0 ? Task.CompletedTask : Parallel.ForEachAsync(downloadBatches, new ParallelOptions()
        {
            MaxDegreeOfParallelism = downloadBatches.Count(),
            CancellationToken = ct,
        },
        async (fileGroup, token) =>
        {
            // let server predownload files
            var requestIdResponse = await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.RequestEnqueueFullPath(fileGroup.First().DownloadUri),
                fileGroup.Select(c => c.Hash), token).ConfigureAwait(false);
            Logger.LogDebug("Sent request for {n} files on server {uri} with result {result}", fileGroup.Count(), fileGroup.First().DownloadUri,
                await requestIdResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false));

            Guid requestId = Guid.Parse((await requestIdResponse.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim('"'));

            Logger.LogDebug("GUID {requestId} for {n} files on server {uri}", requestId, fileGroup.Count(), fileGroup.First().DownloadUri);

            var blockFile = _fileDbManager.GetCacheFilePath(requestId.ToString("N"), "blk");
            FileInfo fi = new(blockFile);
            try
            {
                _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForSlot;
                await _orchestrator.WaitForDownloadSlotAsync(token).ConfigureAwait(false);
                _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForQueue;
                Progress<long> progress = new((bytesDownloaded) =>
                {
                    try
                    {
                        if (!_downloadStatus.TryGetValue(fileGroup.Key, out FileDownloadStatus? value)) return;
                        value.TransferredBytes += bytesDownloaded;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Could not set download progress");
                    }
                });
                await DownloadAndMungeFileHttpClient(fileGroup.Key, requestId, [.. fileGroup], blockFile, progress, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("{dlName}: Detected cancellation of download, partially extracting files for {id}", fi.Name, gameObjectHandler);
            }
            catch (Exception ex)
            {
                _orchestrator.ReleaseDownloadSlot();
                File.Delete(blockFile);
                Logger.LogError(ex, "{dlName}: Error during download of {id}", fi.Name, requestId);
                ClearDownload();
                return;
            }

            FileStream? fileBlockStream = null;
            try
            {
                if (_downloadStatus.TryGetValue(fileGroup.Key, out var status))
                {
                    status.TransferredFiles = 1;
                    status.DownloadStatus = DownloadStatus.Decompressing;
                }
                fileBlockStream = File.OpenRead(blockFile);
                while (fileBlockStream.Position < fileBlockStream.Length)
                {
                    (string fileHash, long fileLengthBytes) = ReadBlockFileHeader(fileBlockStream);

                    try
                    {
                        var fileExtension = fileReplacement.First(f => string.Equals(f.Hash, fileHash, StringComparison.OrdinalIgnoreCase)).GamePaths[0].Split(".")[^1];
                        var filePath = _fileDbManager.GetCacheFilePath(fileHash, fileExtension);
                        Logger.LogDebug("{dlName}: Decompressing {file}:{le} => {dest}", fi.Name, fileHash, fileLengthBytes, filePath);

                        byte[] compressedFileContent = new byte[fileLengthBytes];
                        var readBytes = await fileBlockStream.ReadAsync(compressedFileContent, CancellationToken.None).ConfigureAwait(false);
                        if (readBytes != fileLengthBytes)
                        {
                            throw new EndOfStreamException();
                        }
                        MungeBuffer(compressedFileContent);

                        var decompressedFile = LZ4Wrapper.Unwrap(compressedFileContent);
                        await _fileCompactor.WriteAllBytesAsync(filePath, decompressedFile, CancellationToken.None).ConfigureAwait(false);

                        PersistFileToStorage(fileHash, filePath);
                    }
                    catch (EndOfStreamException)
                    {
                        Logger.LogWarning("{dlName}: Failure to extract file {fileHash}, stream ended prematurely", fi.Name, fileHash);
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning(e, "{dlName}: Error during decompression", fi.Name);
                    }
                }
            }
            catch (EndOfStreamException)
            {
                Logger.LogDebug("{dlName}: Failure to extract file header data, stream ended", fi.Name);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{dlName}: Error during block file read", fi.Name);
            }
            finally
            {
                _orchestrator.ReleaseDownloadSlot();
                if (fileBlockStream != null)
                    await fileBlockStream.DisposeAsync().ConfigureAwait(false);
                File.Delete(blockFile);
            }
        });

        // Start downloading each of the direct downloads
        var directDownloadsTask = directDownloads.Count == 0 ? Task.CompletedTask : Parallel.ForEachAsync(directDownloads, new ParallelOptions()
        {
            MaxDegreeOfParallelism = directDownloads.Count,
            CancellationToken = ct,
        },
        async (directDownload, token) =>
        {
            var downloadTracker = _downloadStatus[directDownload.DirectDownloadUrl!];

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

            var tempFilename = _fileDbManager.GetCacheFilePath(directDownload.Hash, "bin");

            try
            {
                downloadTracker.DownloadStatus = DownloadStatus.WaitingForSlot;
                await _orchestrator.WaitForDownloadSlotAsync(token).ConfigureAwait(false);

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
                Logger.LogError(ex, "{hash}: Error during direct download.", directDownload.Hash);
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
            downloadTracker = _downloadStatus.GetValueOrDefault(directDownload.DirectDownloadUrl!);
            downloadTracker.TransferredFiles = 1;
            downloadTracker.DownloadStatus = DownloadStatus.Decompressing;

            try
            {
                var fileExtension = fileReplacement.First(f => string.Equals(f.Hash, directDownload.Hash, StringComparison.OrdinalIgnoreCase)).GamePaths[0].Split(".")[^1];
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

        // Wait for all the batches and direct downloads to complete
        await Task.WhenAll(batchDownloadsTask, directDownloadsTask).ConfigureAwait(false);

        Logger.LogDebug("Download end: {id}", gameObjectHandler);

        ClearDownload();
    }

    private async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes, CancellationToken ct)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        var response = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.ServerFilesGetSizesFullPath(_orchestrator.FilesCdnUri!), hashes, ct).ConfigureAwait(false);
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

    private async Task WaitForDownloadReady(List<DownloadFileTransfer> downloadFileTransfer, Guid requestId, CancellationToken downloadCt)
    {
        bool alreadyCancelled = false;
        try
        {
            CancellationTokenSource localTimeoutCts = new();
            localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            CancellationTokenSource composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);

            while (!_orchestrator.IsDownloadReady(requestId))
            {
                try
                {
                    await Task.Delay(250, composite.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    if (downloadCt.IsCancellationRequested) throw;

                    var req = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCheckQueueFullPath(downloadFileTransfer[0].DownloadUri, requestId),
                        downloadFileTransfer.Select(c => c.Hash).ToList(), downloadCt).ConfigureAwait(false);
                    req.EnsureSuccessStatusCode();
                    localTimeoutCts.Dispose();
                    composite.Dispose();
                    localTimeoutCts = new();
                    localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);
                }
            }

            localTimeoutCts.Dispose();
            composite.Dispose();

            Logger.LogDebug("Download {requestId} ready", requestId);
        }
        catch (TaskCanceledException)
        {
            try
            {
                await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(false);
                alreadyCancelled = true;
            }
            catch
            {
                // ignore whatever happens here
            }

            throw;
        }
        finally
        {
            if (downloadCt.IsCancellationRequested && !alreadyCancelled)
            {
                try
                {
                    await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(false);
                }
                catch
                {
                    // ignore whatever happens here
                }
            }
            _orchestrator.ClearDownloadRequest(requestId);
        }
    }
}