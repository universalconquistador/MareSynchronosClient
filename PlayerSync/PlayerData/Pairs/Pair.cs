using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs;

public class Pair
{
    private readonly ILogger<Pair> _logger;
    private readonly PairHandlerFactory _cachedPlayerFactory;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly MareConfigService _configService;
    private readonly ZoneSyncConfigService _zoneSyncConfig;

    private readonly SemaphoreSlim _creationSemaphore = new(1);

    private CancellationTokenSource _applicationCts = new();
    private OnlineUserIdentDto? _onlineUserIdentDto = null;
    private bool? _hasProfile = null;

    public Pair(ILogger<Pair> logger, UserFullPairDto userPair, PairHandlerFactory cachedPlayerFactory,
        MareMediator mediator, ServerConfigurationManager serverConfigurationManager, MareConfigService mareConfigService, ZoneSyncConfigService zoneSyncConfig)
    {
        _logger = logger;
        UserPair = userPair;
        _cachedPlayerFactory = cachedPlayerFactory;
        _serverConfigurationManager = serverConfigurationManager;
        _configService = mareConfigService;
        _zoneSyncConfig = zoneSyncConfig;
    }

    public bool HasCachedPlayer => CachedPlayer != null && !string.IsNullOrEmpty(CachedPlayer.PlayerName) && _onlineUserIdentDto != null;
    public IndividualPairStatus IndividualPairStatus => UserPair.IndividualPairStatus;
    public bool IsDirectlyPaired => IndividualPairStatus != IndividualPairStatus.None;
    public bool IsOneSidedPair => IndividualPairStatus == IndividualPairStatus.OneSided;
    public bool IsOnline { get; set; } = false;

    public bool IsPaired => IndividualPairStatus == IndividualPairStatus.Bidirectional || UserPair.Groups.Any();
    public bool IsZoneSyncOnlyPair => IndividualPairStatus != IndividualPairStatus.Bidirectional && UserPair.Groups.All(g => g.StartsWith(Constants.GroupZoneSyncPrefix));
    public bool IsPaused => UserPair.OwnPermissions.IsPaused();
    public bool IsVisible => CachedPlayer?.IsVisible ?? false;
    public CharacterData? LastReceivedCharacterData { get; set; }
    public string? PlayerName => CachedPlayer?.PlayerName ?? string.Empty;
    public long LastAppliedDataBytes => CachedPlayer?.LastAppliedDataBytes ?? -1;
    public long LastAppliedDataTris { get; set; } = -1;
    public long LastAppliedApproximateVRAMBytes { get; set; } = -1;
    public int LastAppliedCompressedAlternates { get; set; } = -1;
    public DateTimeOffset? LastLoadedSoundSinceRedraw { get; set; } = null;
    public string Ident => _onlineUserIdentDto?.Ident ?? string.Empty;
   
    public nint Address => CachedPlayer?.PlayerCharacter ?? nint.Zero;
    
    public UserData UserData => UserPair.User;

    public UserFullPairDto UserPair { get; set; }
    private PairHandler? CachedPlayer { get; set; }
    public unsafe uint PlayerCharacterId => CachedPlayer?.PlayerCharacterId ?? uint.MaxValue;

    public bool HasProfile
    {
        get
        {
            if (_hasProfile is null) 
                return UserPair.User.HasProfile ?? false;

            return _hasProfile.Value;
        }
        set
        {
            _hasProfile = value;
        }
    }

    public void Initialize(string playerName)
    {
        CreateCachedPlayer();

        if (CachedPlayer != null && !string.IsNullOrEmpty(playerName))
        {
            CachedPlayer.Initialize(playerName);
        }
    }

    public void ApplyAddonPluginUpdate(OnlineUserCharaDataDto data)
    {
        if (CachedPlayer == null)
        {
            return;
        }
        _ = CachedPlayer.HandleOptionalPluginDataAsync(data.AddonPlugin!.Value, data.CharaData);
    }

    public void ApplyData(Guid applicationBase, OnlineUserCharaDataDto data)
    {
        _ = ApplyDataAsync(applicationBase, data);
    }

    ///// <summary>
    ///// Step 1
    ///// Ensures a cached player exists, or, waits up to 120 seconds for the cache player to be created, before applying character data from the received dto.
    ///// </summary>
    public async Task ApplyDataAsync(Guid applicationBase, OnlineUserCharaDataDto data)
    {
        _applicationCts = _applicationCts.CancelRecreate();
        LastReceivedCharacterData = data.CharaData;

        if (CachedPlayer == null)
        {
            _logger.LogDebug("[BASE-{appBase}] Received Data for {uid} but CachedPlayer does not exist, waiting", applicationBase, data.User.UID);

            using var timeoutCts = new CancellationTokenSource();
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));

            var appToken = _applicationCts.Token;
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, appToken);

            try
            {
                while (CachedPlayer == null && !combined.Token.IsCancellationRequested)
                {
                    await Task.Delay(250, combined.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (combined.IsCancellationRequested)
            {
                return;
            }

            _logger.LogDebug("[BASE-{appBase}] Applying delayed data for {uid}", applicationBase, data.User.UID);
        }

        await ApplyLastReceivedDataAsync(applicationBase: applicationBase).ConfigureAwait(false);
    }

    public void ApplyLastReceivedData(bool forced = false, Guid? applicationBase = null)
    {
        if (applicationBase == null)
        {
            applicationBase = Guid.NewGuid();
        }
        _ = ApplyLastReceivedDataAsync(forced, applicationBase.Value);
    }

    ///// <summary>
    ///// Step 2
    ///// This is called as part of the data application pipeline as well as manually to reapply pair data.
    ///// </summary>
    public Task ApplyLastReceivedDataAsync(bool forced = false, Guid? applicationBase = null)
    {
        if (CachedPlayer == null || LastReceivedCharacterData == null)
        {
            _logger.LogDebug("Called to apply last received data but CachedPlayer is null: {player} and CharacterData is null: {data}", CachedPlayer == null, LastReceivedCharacterData == null);

            return Task.CompletedTask;
        }

        if (applicationBase == null)
        {
            applicationBase = Guid.NewGuid();
        }

        // Called from the PairHandler of this pair.
        // This is kinda hacky as RemoveNotSyncedFiles handles perms/filtering by removing files before they are applied.
        return CachedPlayer.ApplyCharacterDataAsync(applicationBase.Value, RemoveNotSyncedFiles(LastReceivedCharacterData.DeepClone())!, forced);
    }

    public void SetOnlinePlayerDto(OnlineUserIdentDto? dto = null)
    {
        _creationSemaphore.Wait();

        try
        {
            if (CachedPlayer != null) return;

            if (dto == null && _onlineUserIdentDto == null)
            {
                CachedPlayer?.Dispose();
                CachedPlayer = null;
                return;
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
    }

    public string? GetNote()
    {
        return _serverConfigurationManager.GetNoteForUid(UserData.UID);
    }

    public string GetPauseReason()
    {
        var reasonCode = _serverConfigurationManager.GetPauseReasonForUid(UserData.UID);
        return reasonCode switch
        {
            PauseReason.None => "Unknown",
            PauseReason.Manual => "Manually paused by user",
            PauseReason.Permanent => "Permanently paused by user",
            PauseReason.ThresholdVram => "Exceeded VRAM threshold",
            PauseReason.ThresholdTriangles => "Exceeded triangles threshold",
            PauseReason.ThresholdHeight => "Exceeded height threshold",
            PauseReason.PauseSyncshell => "In a paused syncshell",
            PauseReason.PauseAllPairs => "User paused all pairs",
            PauseReason.PauseAllSyncs => "User paused all syncshells",
            _ => string.Empty
        };
    }

    public string GetPlayerNameHash()
    {
        return CachedPlayer != null ? CachedPlayer.PlayerNameHash : Ident ?? string.Empty;
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
            LastReceivedCharacterData = null;
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
        CachedPlayer?.SetUploading();
    }

    private void CreateCachedPlayer()
    {
        if (CachedPlayer != null || _onlineUserIdentDto == null)
        {
            return;
        }

        CachedPlayer?.Dispose();
        _logger.LogTrace("Creating cached player for {pair}", Ident);
        CachedPlayer = _cachedPlayerFactory.Create(this);
    }

    private CharacterData? RemoveNotSyncedFiles(CharacterData? data)
    {
        _logger.LogTrace("Removing not synced files");
        if (data == null)
        {
            _logger.LogTrace("Nothing to remove");
            return data;
        }

        // permissions
        bool disableIndividualAnimations = UserPair.OtherPermissions.IsDisableAnimations() || UserPair.OwnPermissions.IsDisableAnimations();
        bool disableIndividualVFX = UserPair.OtherPermissions.IsDisableVFX() || UserPair.OwnPermissions.IsDisableVFX();
        bool disableIndividualSounds = UserPair.OtherPermissions.IsDisableSounds() || UserPair.OwnPermissions.IsDisableSounds();

        // global filtering
        bool filterBiDiPairs = _configService.Current.DoFilteringBidirectionDirectPairs;
        bool isDirectPaired = UserPair.IndividualPairStatus == IndividualPairStatus.Bidirectional;
        bool overrideFilterPair = !filterBiDiPairs && isDirectPaired;

        bool overrideFilterUid = _configService.Current.UIDsToOverrideFilter.Contains(UserPair.User.UID, StringComparer.OrdinalIgnoreCase) 
            || _configService.Current.UIDsToOverrideFilter.Contains(UserPair.User.Alias, StringComparer.OrdinalIgnoreCase);

        _logger.LogTrace("Disable: Sounds: {disableIndividualSounds}, Anims: {disableIndividualAnims}; " +
            "VFX: {disableGroupSounds}", disableIndividualSounds, disableIndividualAnimations, disableIndividualVFX);

        // global filtering
        bool filterAnimations = _configService.Current.FilterAnimations;
        bool filterSounds = _configService.Current.FilterSounds;
        bool filterVfx = _configService.Current.FilterVfx;
        bool filterMinionsMounts = _configService.Current.FilterMinionsAndMounts;
        bool filterPets = _configService.Current.FilterPets;

        // zonesync filtering
        bool filterZoneSyncAnimations = _zoneSyncConfig.Current.ZoneSyncFilterAnimations;
        bool filterZoneSyncSounds = _zoneSyncConfig.Current.ZoneSyncFilterSounds;
        bool filterZoneSyncVfx = _zoneSyncConfig.Current.ZoneSyncFilterVfx;

        bool zoneSyncFiltering = IsZoneSyncOnlyPair && (filterZoneSyncAnimations || filterZoneSyncSounds || filterZoneSyncVfx);
        bool hasDisabledIndevidual = disableIndividualAnimations || disableIndividualSounds || disableIndividualVFX;
        bool hasDisabledViaFilter = filterAnimations || filterSounds || filterVfx || filterMinionsMounts || filterPets;
        bool filterUserPerms = hasDisabledViaFilter && !overrideFilterUid && !overrideFilterPair;
        if (filterUserPerms || hasDisabledIndevidual || zoneSyncFiltering)
        {
            _logger.LogTrace("Data cleaned up: Animations disabled: {disableAnimations}, Sounds disabled: {disableSounds}, VFX disabled: {disableVFX}",
                disableIndividualAnimations, disableIndividualSounds, disableIndividualVFX);
            foreach (var objectKind in data.FileReplacements.Select(k => k.Key))
            {
                if ((filterUserPerms && filterSounds) || disableIndividualSounds || (zoneSyncFiltering && filterZoneSyncSounds))
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if ((filterUserPerms && filterAnimations) || disableIndividualAnimations || (zoneSyncFiltering && filterZoneSyncAnimations))
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if ((filterUserPerms && filterVfx) || disableIndividualVFX || (zoneSyncFiltering && filterZoneSyncVfx))
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (filterUserPerms && filterMinionsMounts && objectKind == ObjectKind.MinionOrMount)
                    data.FileReplacements[objectKind] = [];
                if (filterUserPerms && filterPets && (objectKind == ObjectKind.Pet || objectKind == ObjectKind.Companion))
                    data.FileReplacements[objectKind] = [];
            }
        }

        return data;
    }
}
