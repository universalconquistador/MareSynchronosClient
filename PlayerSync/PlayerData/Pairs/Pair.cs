using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.PlayerData.Pairs;

public class Pair
{
    private readonly ILogger<Pair> _logger;
    private readonly PairHandlerFactory _cachedPlayerFactory;
    private readonly PairDataApplicationHandlerFactory _pairDataApplicationHandlerFactory;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly MareConfigService _configService;
    private readonly MareMediator _mediator;

    private readonly SemaphoreSlim _creationSemaphore = new(1);

    private OnlineUserIdentDto? _onlineUserIdentDto = null;
    private bool? _hasProfile = null;

    public Pair(ILogger<Pair> logger, UserFullPairDto userPair, PairHandlerFactory cachedPlayerFactory, PairDataApplicationHandlerFactory pairDataApplicationHandlerFactory,
        MareMediator mediator, ServerConfigurationManager serverConfigurationManager, MareConfigService mareConfigService)
    {
        _logger = logger;
        UserPair = userPair;
        _cachedPlayerFactory = cachedPlayerFactory;
        _pairDataApplicationHandlerFactory = pairDataApplicationHandlerFactory;
        _serverConfigurationManager = serverConfigurationManager;
        _configService = mareConfigService;
        _mediator = mediator;
    }

    public bool HasCachedPlayer => CachedPlayer != null && !string.IsNullOrEmpty(CachedPlayer.PlayerName) && _onlineUserIdentDto != null;
    public bool HasDataHandler => DataHandler != null;
    public bool HasCachedData => DataHandler?.HasCachedData ?? false;
    public IndividualPairStatus IndividualPairStatus => UserPair.IndividualPairStatus;
    public bool IsDirectlyPaired => IndividualPairStatus != IndividualPairStatus.None;
    public bool IsOneSidedPair => IndividualPairStatus == IndividualPairStatus.OneSided;
    public bool IsOnline { get; set; } = false;
    public bool IsModelReady => CachedPlayer?.IsModelReady ?? false;
    public bool IsPaired => IndividualPairStatus == IndividualPairStatus.Bidirectional || UserPair.Groups.Any();
    public bool IsZoneSyncOnlyPair => IndividualPairStatus != IndividualPairStatus.Bidirectional && UserPair.Groups.All(g => g.StartsWith(Constants.GroupZoneSyncPrefix));
    public bool IsPaused => UserPair.OwnPermissions.IsPaused();
    public bool IsVisible => CachedPlayer?.IsVisible ?? false;
    public bool CanApplyModdedData => HasCachedPlayer && HasDataHandler && IsVisible && IsModelReady && !HasDataApplicationTaskRunning &&
        DateTimeOffset.UtcNow - LastDataApplicationTime > TimeSpan.FromMilliseconds(250);
    public string? PlayerName => CachedPlayer?.PlayerName ?? string.Empty;
    public string PairUIDName => $"{UserData.UID}:{(string.IsNullOrWhiteSpace(PlayerName) ? "NULLPLAYER" : PlayerName)}";
    public ConcurrentDictionary<string, string> ActiveCompressionRedirects => DataHandler?.ActiveCompressionRedirects ?? [];
    public bool HasDataApplicationTaskRunning => DataHandler?.HasDataApplicationTaskRunning ?? false;
    public long PendingDataSequenceNumber => DataHandler?.PendingDataSequenceNumber ?? 0;
    public long LastAppliedDataBytes => DataHandler?.LastAppliedDataBytes ?? -1;
    public DateTimeOffset LastDataApplicationTime => DataHandler?.LastDataApplicationTime ?? DateTimeOffset.MinValue;
    public IReadOnlyDictionary<ObjectKind, ConcurrentDictionary<PlayerChanges, double>>? DataApplicationTiming => DataHandler?.DataApplicationTiming;
    public ConcurrentDictionary<string, string> MissingFiles => DataHandler?.MissingFiles ?? [];
    public long LastAppliedDataTris { get; set; } = -1;
    public long LastAppliedApproximateVRAMBytes { get; set; } = -1;
    public int LastAppliedCompressedAlternates { get; set; } = -1;
    public DateTimeOffset? LastLoadedSoundSinceRedrawTime { get; set; } = null;
    public string LastLoadedSoundSinceRedraw { get; set; } = string.Empty;
    public string HashedCID => _onlineUserIdentDto?.Ident ?? string.Empty;
    public ulong ContendId => CachedPlayer?.ContentId ?? 0;
    public nint Address => CachedPlayer?.PlayerCharacter ?? nint.Zero;
    public UserData UserData => UserPair.User;
    public ulong PlayerCharacterId => CachedPlayer?.PlayerCharacterId ?? ulong.MaxValue;
    public UserFullPairDto UserPair { get; set; }
    public PairHandler? CachedPlayer { get; private set; }
    private PairDataApplicationHandler? DataHandler { get; set; }

    public bool HasProfile
    {
        get
        {
            if (_hasProfile == null)
                return UserPair.User.HasProfile ?? false;

            return _hasProfile.Value;
        }
        set
        {
            _hasProfile = value;
        }
    }

    public override string ToString()
    {
        return UserData.AliasOrUID + ":" + PlayerName + ":" + (IsVisible ? "HasChar" : "NoChar");
    }

    public void UpdateVisibility(bool visible)
    {
        CachedPlayer?.UpdateVisibility(visible);
        DataHandler?.UpdateVisibility(visible);
    }

    public void Initialize(string playerName, ulong contendId)
    {
        _creationSemaphore.Wait();

        bool hadErrors = false;

        try
        {
            CreateCachedPlayer();
            CreateDataHandler();

            var cachedPlayer = CachedPlayer;
            if (cachedPlayer != null && !string.IsNullOrEmpty(playerName))
            {
                cachedPlayer.Initialize(playerName, contendId);
                var dataHandler = DataHandler;
                if (dataHandler != null)
                {
                    var handler = cachedPlayer.CharacterHandler;
                    if (handler != null)
                    {
                        dataHandler.Initialize(handler);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to Initialize pair!");
            hadErrors = true;
            try
            {
                DataHandler?.Dispose();
                CachedPlayer?.Dispose();
            }
            finally
            {
                DataHandler = null;
                CachedPlayer = null;
            }

            throw;
        }
        finally
        {
            _creationSemaphore.Release();
        }

        if (!hadErrors)
        {
            _mediator.Publish(new EventMessage(new DataApplicationEvent(Guid.Empty, DataApplicationStatus.Waiting, PlayerName, UserData,
                nameof(Initialize), EventSeverity.Informational, $"Initialized {PairUIDName}, waiting for data from pair...")));
        }
    }

    public Dictionary<PlayerChanges, bool> GetPairPlayerChanges()
    {
        return DataHandler?.GetPairPlayerChanges() ?? [];
    }

    public CharacterData? GetCopyOfPairCharacterData()
    {
        return DataHandler?.GetCopyOfPairCharacterData();
    }

    public Task<Dictionary<ObjectKind, List<FileReplacementData>>?> TryGetMissingFileReplacementData()
    {
        return DataHandler!.TryGetMissingFileReplacementData();
    }

    public bool TryStartFullDataApplication(Guid applId, bool overwriteRunningApplication = false)
    {
        return DataHandler?.TryStartDataApplication(applId, overwriteRunningApplication) ?? false;
    }

    public void EndDataApplication(Guid appId)
    {
        if (DataHandler != null)
        {
            DataHandler.EndDataApplcation(appId);
        }
    }

    public void ApplyAddonPluginUpdate(OnlineUserCharaDataDto data)
    {
        if (CachedPlayer == null || DataHandler == null || _configService.Current.FilterMods) // don't try and apply addon data since the cachedData will be null (and we don't want to anyway)
        {
            return;
        }

        _ = DataHandler.HandleOptionalPluginDataAsync(data.AddonPlugin!.Value, data.CharaData);
    }

    public Task ApplyDataAsync(Guid applicationBase, OnlineUserCharaDataDto data, long sequence, CancellationToken topLevelToken, bool forced = false)
    {
        if (CachedPlayer == null || DataHandler == null)
        {
            _logger.LogDebug("Called to apply player data but CachedPlayer is null: {player} or DataHandler is null: {hanlder}", CachedPlayer == null, DataHandler == null);

            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, $"Cannot apply data for {PairUIDName}, no cached player exists!");
        }

        return DataHandler.ApplyCharacterDataAsync(applicationBase, data.CharaData, sequence, topLevelToken, forced);
    }

    public void ReapplyCachedData(bool forced = false)
    {
        if (HasCachedData)
        {
            _mediator.Publish(new ReapplyCachedPairDataMessage(this, forced));
        }
    }

    public Task ApplyCachedDataAync(Guid applicationBase, CancellationToken topLevelToken, bool forceApplyCustomization = false)
    {
        if (CachedPlayer == null || DataHandler == null)
        {
            _logger.LogDebug("Called to apply player data but CachedPlayer is null: {player} or DataHandler is null: {hanlder}", CachedPlayer == null, DataHandler == null);

            throw new PairDataApplicationException(PairDataExceptionReason.InvalidCharacterState, "Pair was in an invalid state during cached data application");
        }

        return DataHandler.ApplyCachedCharacterDataASync(applicationBase, topLevelToken, forceApplyCustomization);
    }

    public bool SetOnlinePlayerDto(OnlineUserIdentDto? dto = null)
    {
        _creationSemaphore.Wait();

        try
        {
            if (CachedPlayer != null)
            {
                return true;
            }

            if (dto == null && _onlineUserIdentDto == null)
            {
                CachedPlayer?.Dispose();
                CachedPlayer = null;
                DataHandler?.Dispose();
                DataHandler = null;

                return false;
            }
            if (dto != null)
            {
                _onlineUserIdentDto = dto;
            }
        }
        finally
        {
            _creationSemaphore.Release();
        }

        return true;
    }

    public string? GetNote()
    {
        return _serverConfigurationManager.GetNoteForUid(UserData.UID);
    }

    public string GetPauseReason()
    {
        return PauseReasonText.FromPauseReason(_serverConfigurationManager.GetPauseReasonForUid(UserData.UID));
    }

    public string GetPlayerNameHash()
    {
        return CachedPlayer != null ? CachedPlayer.PlayerNameHash : HashedCID ?? string.Empty;
    }

    public bool HasAnyConnection()
    {
        return UserPair.Groups.Any() || UserPair.IndividualPairStatus != IndividualPairStatus.None;
    }

    public void MarkOffline(bool wait = true)
    {
        if (wait)
            _creationSemaphore.Wait();

        try
        {
            var dataHandler = DataHandler;
            DataHandler = null;
            dataHandler?.Dispose();

            var player = CachedPlayer;
            CachedPlayer = null;
            player?.Dispose();

            _onlineUserIdentDto = null;
            IsOnline = false;
        }
        finally
        {
            if (wait)
                _creationSemaphore.Release();
        }
    }

    public void SetNote(string note)
    {
        _serverConfigurationManager.SetNoteForUid(UserData.UID, note);
    }

    internal void SetIsUploading()
    {
        DataHandler?.SetUploading();
    }

    private void CreateCachedPlayer()
    {
        if (CachedPlayer != null || _onlineUserIdentDto == null)
        {
            return;
        }

        CachedPlayer?.Dispose();
        _logger.LogTrace("Creating cached player for {pair}", HashedCID);
        CachedPlayer = _cachedPlayerFactory.Create(this);
    }

    private void CreateDataHandler()
    {
        if (DataHandler != null || _onlineUserIdentDto == null)
        {
            return;
        }

        DataHandler?.Dispose();
        _logger.LogTrace("Creating data handler for {pair}", HashedCID);
        DataHandler = _pairDataApplicationHandlerFactory.Create(this);
    }
}
