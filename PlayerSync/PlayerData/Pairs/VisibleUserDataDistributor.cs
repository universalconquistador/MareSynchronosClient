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
    private readonly TimeSpan _cacheCreationDelay = TimeSpan.FromSeconds(5);

    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileUploadManager _fileTransferManager;
    private readonly PairManager _pairManager;
    private readonly MareConfigService _configService;
    private readonly FileCacheManager _fileCacheManager;

    private readonly List<UserData> _previouslyVisiblePlayers = [];
    private readonly object _pushDataLock = new();
    private readonly HashSet<UserData> _usersToPushDataTo = [];
    private readonly SemaphoreSlim _pushDataSemaphore = new(1, 1);
    private readonly CancellationTokenSource _runtimeCts = new();
    private readonly HashSet<string> _filesTooLargeHashes = new HashSet<string>();

    private Task<CharacterData>? _fileUploadTask = null;
    private CharacterData? _lastCreatedData;
    private CharacterData? _uploadingCharacterData = null;
    private int _cacheCreationRequestCount = 0;
    private DateTimeOffset _lastCacheCreationRequest = DateTimeOffset.MinValue;


    public VisibleUserDataDistributor(ILogger<VisibleUserDataDistributor> logger, ApiController apiController, DalamudUtilService dalamudUtil,
        PairManager pairManager, MareMediator mediator, FileUploadManager fileTransferManager, MareConfigService mareConfigService, FileCacheManager fileCacheManager) : base(logger, mediator)
    {
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _fileTransferManager = fileTransferManager;
        _configService = mareConfigService;
        _fileCacheManager = fileCacheManager;

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

        Mediator.Subscribe<PairOfflineMessage>(this, (msg) =>
        {
            lock (_pushDataLock)
            {
                _previouslyVisiblePlayers.Remove(msg.Pair.UserData);
                _usersToPushDataTo.Remove(msg.Pair.UserData);
            }
            
        });

        Mediator.Subscribe<DisconnectedMessage>(this, (_) =>
        {
            lock (_pushDataLock)
            {
                _previouslyVisiblePlayers.Clear();
            }
        });

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());
        Mediator.Subscribe<AddonPluginChangesCreatedMessage>(this, (msg) => PushAddonPluginPlayerChanges(msg.PlayerChanges, msg.Data));
        Mediator.Subscribe<ConnectedMessage>(this, (_) => PushToAllVisibleUsers());
        
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
        if (!_dalamudUtil.GetIsPlayerPresent() || !_apiController.IsConnected)
        {
            return;
        }

        var allVisibleUsers = _pairManager.GetVisibleUsers();
        List<UserData> newVisibleUsers;

        lock (_pushDataLock)
        {
            newVisibleUsers = allVisibleUsers.Except(_previouslyVisiblePlayers).ToList();

            _previouslyVisiblePlayers.Clear();
            _previouslyVisiblePlayers.AddRange(allVisibleUsers);
        }

        if (newVisibleUsers.Count == 0)
        {
            return;
        }

        Logger.LogDebug("Scheduling character data push of {data} to {users}", _lastCreatedData?.DataHash.Value ?? string.Empty, string.Join(", ", newVisibleUsers.Select(k => k.AliasOrUID)));
        foreach (var user in newVisibleUsers)
        {
            _usersToPushDataTo.Add(user);
        }

        PushCharacterData();
    }

    private bool HasCacheOrCreateIfEmpty()
    {
        if (_lastCreatedData == null || string.IsNullOrEmpty(_lastCreatedData?.DataHash.Value))
        {
            _cacheCreationRequestCount++;
            Logger.LogDebug("Requested to push character data but character data was null. Total requests: {count}.", _cacheCreationRequestCount);

            var now = DateTimeOffset.UtcNow;
            if (now - _lastCacheCreationRequest < _cacheCreationDelay)
            {
                return false;
            }

            _lastCacheCreationRequest = now;
            Logger.LogDebug("Sending request to create player cache");
            Mediator.Publish(new CreateCacheForEverythingMessage()); // this will eventually call back here via CharacterDataCreatedMessage

            return false;
        }

        return true;
    }

    private void PushCharacterData(bool forced = false)
    {
        if (_usersToPushDataTo.Count == 0 || !HasCacheOrCreateIfEmpty())
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            forced |= _uploadingCharacterData?.DataHash != _lastCreatedData!.DataHash;

            if (_fileUploadTask == null || (_fileUploadTask?.IsCompleted ?? false) || forced)
            {
                _uploadingCharacterData = _lastCreatedData.DeepClone();

                RemoveFilesThatAreTooLarge(); // psync allows for 200 MiB to the server, 300 MiB once decompressed

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
                    if (_usersToPushDataTo.Count == 0)
                    {
                        return;
                    }

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

    private void RemoveFilesThatAreTooLarge()
    {
        try
        {
            // check for files that are larger than what the server allows for
            Dictionary<ObjectKind, List<FileReplacementData>> filesTooLarge = new();
            foreach (var fileReplacements in _uploadingCharacterData!.FileReplacements)
            {
                foreach (var replacement in fileReplacements.Value)
                {
                    var cache = _fileCacheManager.GetFileCacheByHash(replacement.Hash);
                    if (cache == null)
                    {
                        continue;
                    }

                    if ((cache.Size != null && cache.Size > 300 * 1024 * 1024) || (cache.CompressedSize != null && cache.CompressedSize > 200 * 1024 * 1024))
                    {
                        Logger.LogWarning("File {file} exceeds the size limit to sync and will not be sent. (300 MiB, or 200 MiB compressed)", cache.ResolvedFilepath);
                        if (!filesTooLarge.Keys.Contains(fileReplacements.Key))
                        {
                            filesTooLarge[fileReplacements.Key] = new();
                        }
                        filesTooLarge[fileReplacements.Key].Add(replacement);

                        if (!_filesTooLargeHashes.Contains(cache.Hash) && _configService.Current.ShowFileUnableToSyncNotification)
                        {
                            var isTexFile = cache.ResolvedFilepath.EndsWith(".tex", StringComparison.OrdinalIgnoreCase);
                            var texInfo = isTexFile ? " Ensure it is compressed and consider reducing its resolution." : "";
                            Mediator.Publish(new NotificationMessage("File Size Error", $"The file {cache.ResolvedFilepath} exceeds the size limit and will not sync. (300 MiB, or 200 MiB compressed)" 
                                + texInfo, MareConfiguration.Models.NotificationType.Warning));

                            _filesTooLargeHashes.Add(cache.Hash);
                        }
                    }
                }
            }
            foreach (var replacements in filesTooLarge)
            {
                foreach (var replacement in replacements.Value)
                {
                    _uploadingCharacterData.FileReplacements[replacements.Key].Remove(replacement);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to properly detect if files to upload are too large.");
        }   
    }

    private void PushAddonPluginPlayerChanges(PlayerChanges playerChanges, string data)
    {
        foreach (var user in _pairManager.GetVisibleUsers())
        {
            _usersToPushDataTo.Add(user);
        }

        if (_usersToPushDataTo.Count > 0)
        {
            Logger.LogDebug("Pushing only addon plugin changes for {count} users", _usersToPushDataTo.Count);
            _ = PushAddonPluginPlayerChangesInternal(playerChanges, data);
        }
    }

    // Send full CharacterData for now to maintain backwards compatability.
    private async Task PushAddonPluginPlayerChangesInternal(PlayerChanges playerChanges, string data)
    {
        if (_usersToPushDataTo.Count == 0 || !HasCacheOrCreateIfEmpty())
        {
            return;
        }

        //CharacterData characterData = new();
        AddonPlugin plugin;
        
        switch (playerChanges)
        {
            case PlayerChanges.Honorific:
                //characterData.HonorificData = data;
                _lastCreatedData.HonorificData = data;
                plugin = AddonPlugin.Honorific;
                break;

            case PlayerChanges.Heels:
                //characterData.HeelsData = data;
                _lastCreatedData.HeelsData = data;
                plugin = AddonPlugin.Heels;
                break;

            case PlayerChanges.Moodles:
                //characterData.MoodlesData = data;
                _lastCreatedData.MoodlesData = data;
                plugin = AddonPlugin.Moodles;
                break;

            case PlayerChanges.PetNames:
                //characterData.PetNamesData = data;
                _lastCreatedData.PetNamesData = data;
                plugin = AddonPlugin.PetNames;
                break;

            default:
                return;
        }

        try
        {
            _uploadingCharacterData = _lastCreatedData.DeepClone(); // do this so we don't push again on next DelayedFrameworkUpdate

            await _pushDataSemaphore.WaitAsync(_runtimeCts.Token).ConfigureAwait(false);
            //await _apiController.UserPushData(new([.. _usersToPushDataTo], characterData, null, plugin)).ConfigureAwait(false);
            await _apiController.UserPushData(new([.. _usersToPushDataTo], _uploadingCharacterData, null, plugin)).ConfigureAwait(false);
            
        }
        finally
        {
            _usersToPushDataTo.Clear();
            _pushDataSemaphore.Release();
        }
    }
}