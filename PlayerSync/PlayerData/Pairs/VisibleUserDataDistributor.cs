using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs;

public class VisibleUserDataDistributor : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileUploadManager _fileTransferManager;
    private readonly PairManager _pairManager;
    private readonly MareConfigService _configService;
    private readonly FileCacheManager _fileCacheManager;
    private CharacterData? _lastCreatedData;
    private CharacterData? _uploadingCharacterData = null;
    private readonly List<UserData> _previouslyVisiblePlayers = [];
    private Task<CharacterData>? _fileUploadTask = null;
    private readonly HashSet<UserData> _usersToPushDataTo = [];
    private readonly SemaphoreSlim _pushDataSemaphore = new(1, 1);
    private readonly CancellationTokenSource _runtimeCts = new();
    private readonly HashSet<string> _filesTooLargeOrEmptyHasBeenNotified = new HashSet<string>();


    public VisibleUserDataDistributor(ILogger<VisibleUserDataDistributor> logger, ApiController apiController, DalamudUtilService dalamudUtil,
        PairManager pairManager, MareMediator mediator, FileUploadManager fileTransferManager, MareConfigService mareConfigService, FileCacheManager fileCacheManager) : base(logger, mediator)
    {
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _fileTransferManager = fileTransferManager;
        _configService = mareConfigService;
        _fileCacheManager = fileCacheManager;
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            var newData = msg.CharacterData;
            if (_lastCreatedData == null || (!string.Equals(newData.DataHash.Value, _lastCreatedData.DataHash.Value, StringComparison.Ordinal)))
            {
                _lastCreatedData = newData;
                Logger.LogTrace("Storing new data hash {hash}", newData.DataHash.Value);
                PushToAllVisibleUsers(forced: true);
            }
            else
            {
                Logger.LogTrace("Data hash {hash} equal to stored data", newData.DataHash.Value);
            }
        });

        Mediator.Subscribe<ConnectedMessage>(this, (_) => PushToAllVisibleUsers());
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => _previouslyVisiblePlayers.Clear());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runtimeCts.Cancel();
            _runtimeCts.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PushToAllVisibleUsers(bool forced = false)
    {
        foreach (var user in _pairManager.GetVisibleUsers())
        {
            _usersToPushDataTo.Add(user);
        }

        if (_usersToPushDataTo.Count > 0)
        {
            Logger.LogDebug("Pushing data {hash} for {count} visible players", _lastCreatedData?.DataHash.Value ?? "UNKNOWN", _usersToPushDataTo.Count);
            PushCharacterData(forced);
        }
    }

    private void FrameworkOnUpdate()
    {
        if (!_dalamudUtil.GetIsPlayerPresent() || !_apiController.IsConnected) return;

        var allVisibleUsers = _pairManager.GetVisibleUsers();
        var newVisibleUsers = allVisibleUsers.Except(_previouslyVisiblePlayers).ToList();
        _previouslyVisiblePlayers.Clear();
        _previouslyVisiblePlayers.AddRange(allVisibleUsers);
        if (newVisibleUsers.Count == 0) return;

        Logger.LogDebug("Scheduling character data push of {data} to {users}",
            _lastCreatedData?.DataHash.Value ?? string.Empty,
            string.Join(", ", newVisibleUsers.Select(k => k.AliasOrUID)));
        foreach (var user in newVisibleUsers)
        {
            _usersToPushDataTo.Add(user);
        }
        PushCharacterData();
    }

    private void PushCharacterData(bool forced = false)
    {
        if (_lastCreatedData == null || _usersToPushDataTo.Count == 0) return;

        _ = Task.Run(async () =>
        {
            forced |= _uploadingCharacterData?.DataHash != _lastCreatedData.DataHash;

            if (_fileUploadTask == null || (_fileUploadTask?.IsCompleted ?? false) || forced)
            {
                _uploadingCharacterData = _lastCreatedData.DeepClone();
                RemoveFilesThatAreTooLargeOrEmpty(); // psync allows for 200 MiB to the server, 300 MiB once decompressed
                Logger.LogDebug("Starting UploadTask for {hash}, Reason: TaskIsNull: {task}, TaskIsCompleted: {taskCpl}, Forced: {frc}",
                    _lastCreatedData.DataHash, _fileUploadTask == null, _fileUploadTask?.IsCompleted ?? false, forced);
                _fileUploadTask = _fileTransferManager.UploadFiles(_uploadingCharacterData, [.. _usersToPushDataTo]);
            }

            if (_fileUploadTask != null)
            {
                var dataToSend = await _fileUploadTask.ConfigureAwait(false);
                await _pushDataSemaphore.WaitAsync(_runtimeCts.Token).ConfigureAwait(false);
                try
                {
                    if (_usersToPushDataTo.Count == 0) return;
                    Logger.LogDebug("Pushing {data} to {users}", dataToSend.DataHash, string.Join(", ", _usersToPushDataTo.Select(k => k.AliasOrUID)));
                    await _apiController.PushCharacterData(dataToSend, [.. _usersToPushDataTo]).ConfigureAwait(false);
                    _usersToPushDataTo.Clear();
                }
                finally
                {
                    _pushDataSemaphore.Release();
                }
            }
        });
    }

    private void RemoveFilesThatAreTooLargeOrEmpty()
    {
        try
        {
            Dictionary<ObjectKind, List<FileReplacementData>> filesTooLargeorEmpty = new();
            foreach (var fileReplacements in _uploadingCharacterData!.FileReplacements)
            {
                foreach (var replacement in fileReplacements.Value)
                {
                    bool mustRemoveReplacementData = false;

                    var cache = _fileCacheManager.GetFileCacheByHash(replacement.Hash);
                    if (cache == null)
                    {
                        continue;
                    }

                    // check for empty files
                    if (cache.Size != null && cache.Size == 0)
                    {
                        mustRemoveReplacementData = true;
                    }

                    // check for files that are larger than what the server allows for
                    else if ((cache.Size != null && cache.Size > 300 * 1024 * 1024) || (cache.CompressedSize != null && cache.CompressedSize > 200 * 1024 * 1024))
                    {
                        Logger.LogWarning("File {file} exceeds the size limit to sync and will not be sent. (300 MiB, or 200 MiB compressed)", cache.ResolvedFilepath);

                        if (!_filesTooLargeOrEmptyHasBeenNotified.Contains(cache.Hash) && _configService.Current.ShowFileUnableToSyncNotification)
                        {
                            var isTexFile = cache.ResolvedFilepath.EndsWith(".tex", StringComparison.OrdinalIgnoreCase);
                            var texInfo = isTexFile ? " Ensure it is compressed and consider reducing its resolution." : "";
                            Mediator.Publish(new NotificationMessage("File Size Error", $"The file {cache.ResolvedFilepath} exceeds the size limit and will not sync. (300 MiB, or 200 MiB compressed)" + texInfo, MareConfiguration.Models.NotificationType.Warning));
                            _filesTooLargeOrEmptyHasBeenNotified.Add(cache.Hash);
                        }

                        mustRemoveReplacementData = true;
                    }

                    if (mustRemoveReplacementData)
                    {
                        if (!filesTooLargeorEmpty.ContainsKey(fileReplacements.Key))
                        {
                            // create an empty replacements list for this ObjectKind if ObjectKind not already in the dictionary
                            filesTooLargeorEmpty[fileReplacements.Key] = [];
                        }
                        filesTooLargeorEmpty[fileReplacements.Key].Add(replacement);
                    }
                }
            }

            // check each ObjectKind that is present for any items in its replacement list to remove
            foreach (var replacements in filesTooLargeorEmpty)
            {
                foreach (var replacement in replacements.Value)
                {
                    _uploadingCharacterData.FileReplacements[replacements.Key].Remove(replacement);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to properly detect if files to upload are too large or empty.");
        }   
    }
}