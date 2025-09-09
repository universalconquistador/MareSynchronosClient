using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MareSynchronos.WebAPI.Files;

public sealed class FileUploadManager : DisposableMediatorSubscriberBase
{
    private readonly FileCacheManager _fileDbManager;
    private readonly MareConfigService _mareConfigService;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _uploadCancellationTokenSource = new();

    private readonly ConcurrentDictionary<string, UploadFileTransfer> _pendingUploads = new(StringComparer.Ordinal);

    public FileUploadManager(ILogger<FileUploadManager> logger, MareMediator mediator,
        MareConfigService mareConfigService,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileDbManager,
        ServerConfigurationManager serverManager) : base(logger, mediator)
    {
        _mareConfigService = mareConfigService;
        _orchestrator = orchestrator;
        _fileDbManager = fileDbManager;
        _serverManager = serverManager;

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            Reset();
        });
    }

    public bool IsUploading => CurrentUploadCount > 0;
    public List<FileTransfer> CurrentUploadList
    {
        get
        {
            return new List<FileTransfer>(_pendingUploads.Values);
        }
    }

    public int CurrentUploadCount
    {
        get
        {
            return _pendingUploads.Count;
        }
    }

    public bool CancelUpload()
    {
        foreach (var upload in _pendingUploads.Values)
        {
            upload.Cancel();
            _pendingUploads.TryRemove(new KeyValuePair<string, UploadFileTransfer>(upload.Hash, upload));
        }

        return false;
    }

    public void SkipUnstartedFiles()
    {
        foreach (var upload in _pendingUploads.Values)
        {
            upload.Skip = true;
            if (!upload.Started)
            {
                _pendingUploads.TryRemove(new KeyValuePair<string, UploadFileTransfer>(upload.Hash, upload));
            }
        }
    }

    public async Task DeleteAllFiles()
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");

        await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.ServerFilesDeleteAllFullPath(_orchestrator.FilesCdnUri!)).ConfigureAwait(false);
    }

    // Returns the hashes of any files that could not be uploaded (e.g. forbidden or somehow not on the client)
    public async Task<List<string>> UploadFiles(List<string> hashesToUpload, IProgress<string> progress, CancellationToken? ct = null)
    {
        Logger.LogDebug("Trying to upload files");
        var filesPresentLocally = hashesToUpload.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);
        var locallyMissingFiles = hashesToUpload.Except(filesPresentLocally, StringComparer.Ordinal).ToList();
        if (locallyMissingFiles.Any())
        {
            return locallyMissingFiles;
        }

        progress.Report($"Starting parallel upload for {filesPresentLocally.Count} files");

        using (ProfiledScope.BeginLoggedScope(Logger, "UploadFiles() parallel upload"))
        {
            var filesToUpload = await FilesSend([.. filesPresentLocally], [], ct ?? CancellationToken.None).ConfigureAwait(false);

            if (filesToUpload.Exists(f => f.IsForbidden))
            {
                return [.. filesToUpload.Where(f => f.IsForbidden).Select(f => f.Hash)];
            }

            if (filesToUpload.Count > 0)
            {
                var uploadTask = Parallel.ForEachAsync(filesToUpload, new ParallelOptions()
                {
                    MaxDegreeOfParallelism = filesToUpload.Count,
                    CancellationToken = ct ?? CancellationToken.None,
                },
                async (fileToUpload, token) =>
                {
                    using (ProfiledScope.BeginLoggedScope(Logger, "UploadFiles() waiting for slot for " + fileToUpload.Hash))
                    {
                        await _orchestrator.WaitForUploadSlotAsync(token).ConfigureAwait(false);
                    }

                    // We could compress all at once before waiting for the parallel upload slot, but might as well stagger compression
                    // just to avoid any possible CPU hitch from trying to compress too many files at once.
                    Logger.LogDebug("[{hash}] Compressing", fileToUpload.Hash);
                    (string, byte[]) compressedData;
                    using (ProfiledScope.BeginLoggedScope(Logger, "UploadFiles() compressing " + fileToUpload.Hash))
                    {
                        compressedData = await _fileDbManager.GetCompressedFileData(fileToUpload.Hash, token).ConfigureAwait(false);
                    }

                    Logger.LogDebug("[{hash}] Starting upload for {filePath}", compressedData.Item1, _fileDbManager.GetFileCacheByHash(compressedData.Item1)!.ResolvedFilepath);
                    using (ProfiledScope.BeginLoggedScope(Logger, "UploadFiles() uploading " + fileToUpload.Hash))
                    {
                        await UploadFile(compressedData.Item2, fileToUpload.Hash, false, token).ConfigureAwait(false);
                    }
                    _orchestrator.ReleaseUploadSlot();
                });

                await uploadTask.ConfigureAwait(false);
            }
        }

        return [];
    }

    public async Task<CharacterData> UploadFiles(CharacterData data, List<UserData> visiblePlayers)
    {
        SkipUnstartedFiles();

        _uploadCancellationTokenSource = new CancellationTokenSource();
        var uploadToken = _uploadCancellationTokenSource.Token;
        Logger.LogDebug("Sending Character data {hash} to service {url}", data.DataHash.Value, _serverManager.CurrentApiUrl);

        HashSet<string> unverifiedUploads = GetUnverifiedFiles(data);
        if (unverifiedUploads.Any())
        {
            await UploadUnverifiedFiles(unverifiedUploads, visiblePlayers, uploadToken).ConfigureAwait(false);
            Logger.LogInformation("Upload complete for {hash}", data.DataHash.Value);
        }

        foreach (var kvp in data.FileReplacements)
        {
            data.FileReplacements[kvp.Key].RemoveAll(i => _orchestrator.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, i.Hash, StringComparison.OrdinalIgnoreCase)));
        }

        return data;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Reset();
    }

    private async Task<List<UploadFileDto>> FilesSend(List<string> hashes, List<string> uids, CancellationToken ct)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        FilesSendDto filesSendDto = new()
        {
            FileHashes = hashes,
            UIDs = uids
        };
        var response = await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.ServerFilesFilesSendFullPath(_orchestrator.FilesCdnUri!), filesSendDto, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? [];
    }

    private HashSet<string> GetUnverifiedFiles(CharacterData data)
    {
        HashSet<string> unverifiedUploadHashes = new(StringComparer.Ordinal);
        foreach (var item in data.FileReplacements.SelectMany(c => c.Value.Where(f => string.IsNullOrEmpty(f.FileSwapPath)).Select(v => v.Hash).Distinct(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList())
        {
            if (!_verifiedUploadedHashes.TryGetValue(item, out var verifiedTime))
            {
                verifiedTime = DateTime.MinValue;
            }

            if (verifiedTime < DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)))
            {
                Logger.LogTrace("Verifying {item}, last verified: {date}", item, verifiedTime);
                unverifiedUploadHashes.Add(item);
            }
        }

        return unverifiedUploadHashes;
    }

    private void Reset()
    {
        _uploadCancellationTokenSource?.Cancel();
        _uploadCancellationTokenSource?.Dispose();
        _uploadCancellationTokenSource = null;

        foreach (var transfer in _pendingUploads.Values)
        {
            transfer.Cancel();
            _pendingUploads.TryRemove(new KeyValuePair<string, UploadFileTransfer>(transfer.Hash, transfer));
        }

        _verifiedUploadedHashes.Clear();
    }

    private async Task UploadFile(byte[] compressedFile, string fileHash, bool postProgress, CancellationToken uploadToken)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");

        Logger.LogInformation("[{hash}] Uploading {size}", fileHash, UiSharedService.ByteToString(compressedFile.Length));

        if (uploadToken.IsCancellationRequested) return;

        try
        {
            await UploadFileStream(compressedFile, fileHash, _mareConfigService.Current.UseAlternativeFileUpload, postProgress, uploadToken).ConfigureAwait(false);
            _verifiedUploadedHashes[fileHash] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            if (!_mareConfigService.Current.UseAlternativeFileUpload && ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "[{hash}] Error during file upload, trying alternative file upload", fileHash);
                await UploadFileStream(compressedFile, fileHash, munged: true, postProgress, uploadToken).ConfigureAwait(false);
            }
            else
            {
                Logger.LogWarning(ex, "[{hash}] File upload cancelled", fileHash);
            }
        }
    }

    private async Task UploadFileStream(byte[] compressedFile, string fileHash, bool munged, bool postProgress, CancellationToken uploadToken)
    {
        if (munged)
        {
            FileDownloadManager.MungeBuffer(compressedFile.AsSpan());
        }

        using var ms = new MemoryStream(compressedFile);

        Progress<UploadProgress>? prog = !postProgress ? null : new((prog) =>
        {
            try
            {
                if (_pendingUploads.TryGetValue(fileHash, out var upload))
                {
                    upload.Transferred = prog.Uploaded;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[{hash}] Could not set upload progress", fileHash);
            }
        });

        var streamContent = new ProgressableStreamContent(ms, prog);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        HttpResponseMessage response;
        if (!munged)
            response = await _orchestrator.SendRequestStreamAsync(HttpMethod.Post, MareFiles.ServerFilesUploadFullPath(_orchestrator.FilesCdnUri!, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        else
            response = await _orchestrator.SendRequestStreamAsync(HttpMethod.Post, MareFiles.ServerFilesUploadMunged(_orchestrator.FilesCdnUri!, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        Logger.LogDebug("[{hash}] Upload Status: {status}", fileHash, response.StatusCode);
    }

    private async Task PerformUpload(UploadFileTransfer transfer, CancellationToken token)
    {
        using (ProfiledScope.BeginLoggedScope(Logger, "UploadUnverifiedFiles() waiting for slot for " + transfer.Hash))
        {
            try
            {
                await _orchestrator.WaitForUploadSlotAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "UploadUnverifiedFiles() wait for slot encountered exception for {hash}", transfer.Hash);
                _pendingUploads.TryRemove(new KeyValuePair<string, UploadFileTransfer>(transfer.Hash, transfer));
                throw;
            }
        }

        try
        {
            if (transfer.Skip)
            {
                Logger.LogDebug("[{hash}] Skipping compression and upload", transfer.Hash);
                return;
            }

            transfer.Started = true;

            // We could compress all at once before waiting for the parallel upload slot, but might as well stagger compression
            // just to avoid any possible CPU hitch from trying to compress too many files at once.
            (string, byte[]) compressedData;
            using (ProfiledScope.BeginLoggedScope(Logger, "UploadUnverifiedFiles() compressing " + transfer.Hash))
            {
                compressedData = await _fileDbManager.GetCompressedFileData(transfer.Hash, token).ConfigureAwait(false);
            }

            transfer.Total = compressedData.Item2.Length;

            Logger.LogDebug("[{hash}] Starting upload for {filePath}", compressedData.Item1, _fileDbManager.GetFileCacheByHash(compressedData.Item1)!.ResolvedFilepath);
            using (ProfiledScope.BeginLoggedScope(Logger, "UploadUnverifiedFiles() uploading " + transfer.Hash))
            {
                await UploadFile(compressedData.Item2, transfer.Hash, true, token).ConfigureAwait(false);
            }

            _verifiedUploadedHashes[transfer.Hash] = DateTime.UtcNow;
        }
        finally
        {
            _orchestrator.ReleaseUploadSlot();

            _pendingUploads.TryRemove(new KeyValuePair<string, UploadFileTransfer>(transfer.Hash, transfer));
        }
    }

    private async Task UploadUnverifiedFiles(HashSet<string> unverifiedUploadHashes, List<UserData> visiblePlayers, CancellationToken uploadToken)
    {
        unverifiedUploadHashes = unverifiedUploadHashes.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);

        Logger.LogDebug("Verifying {count} files sequentially", unverifiedUploadHashes.Count);
        var filesToUpload = await FilesSend([.. unverifiedUploadHashes], visiblePlayers.Select(p => p.UID).ToList(), uploadToken).ConfigureAwait(false);

        using (ProfiledScope.BeginLoggedScope(Logger, "UploadUnverifiedFiles() parallel v2 upload"))
        {
            var task = Parallel.ForEachAsync(filesToUpload.Where(f => !f.IsForbidden).DistinctBy(f => f.Hash), new ParallelOptions()
            {
                MaxDegreeOfParallelism = filesToUpload.Count,
                CancellationToken = uploadToken,
            }, async (file, token) =>
            {
                var upload = _pendingUploads.GetOrAdd(file.Hash, hash =>
                {
                    var transfer = new UploadFileTransfer(file, token)
                    {
                        Total = new FileInfo(_fileDbManager.GetFileCacheByHash(file.Hash)!.ResolvedFilepath).Length
                    };

                    if (file.IsForbidden)
                    {
                        // If there isn't an entry in the forbidden transfers list for this hash, add this one
                        if (_orchestrator.ForbiddenTransfers.TrueForAll(f => !string.Equals(f.Hash, file.Hash, StringComparison.Ordinal)))
                        {
                            _orchestrator.ForbiddenTransfers.Add(transfer);
                        }

                        _verifiedUploadedHashes[file.Hash] = DateTime.UtcNow;
                        transfer.CompletionTask = Task.CompletedTask;
                    }
                    else if (transfer.CanBeTransferred && !transfer.IsTransferred)
                    {
                        transfer.CompletionTask = PerformUpload(transfer, transfer.CancellationToken);
                    }

                    return transfer;
                });

                await upload.CompletionTask.ConfigureAwait(false);
            });

            await task.ConfigureAwait(false);
        }
    }
}