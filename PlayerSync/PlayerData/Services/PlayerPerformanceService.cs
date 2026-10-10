using MareSynchronos.API.Data;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.Interop.Meta;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.Services;

public class PlayerPerformanceService
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataAnalyzer _xivDataAnalyzer;
    private readonly ILogger<PlayerPerformanceService> _logger;
    private readonly MareMediator _mediator;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;
    private readonly ConcurrentDictionary<string, bool> _warnedForPlayers = new(StringComparer.Ordinal);

    public PlayerPerformanceService(ILogger<PlayerPerformanceService> logger, MareMediator mediator,
        PlayerPerformanceConfigService playerPerformanceConfigService, FileCacheManager fileCacheManager,
        XivDataAnalyzer xivDataAnalyzer)
    {
        _logger = logger;
        _mediator = mediator;
        _playerPerformanceConfigService = playerPerformanceConfigService;
        _fileCacheManager = fileCacheManager;
        _xivDataAnalyzer = xivDataAnalyzer;
    }

    public async Task<bool> CheckBothThresholds(Pair pair, CharacterData charaData)
    {
        var config = _playerPerformanceConfigService.Current;
        bool notPausedAfterVram = await ComputeAndAutoPauseOnVRAMUsageThresholds(pair, charaData, []).ConfigureAwait(false);
        if (!notPausedAfterVram) return false;
        bool notPausedAfterTris = await CheckTriangleUsageThresholds(pair, charaData).ConfigureAwait(false);
        if (!notPausedAfterTris) return false;

        if (config.UIDsToIgnore
            .Exists(uid => string.Equals(uid, pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pair.UserData.UID, StringComparison.Ordinal)))
            return true;


        var vramUsage = pair.LastAppliedApproximateVRAMBytes;
        var triUsage = pair.LastAppliedDataTris;

        bool isPrefPerm = pair.UserPair.OwnPermissions.HasFlag(API.Data.Enum.UserPermissions.Sticky);

        bool exceedsTris = CheckForThreshold(config.WarnOnExceedingThresholds, config.TrisWarningThresholdThousands * 1000,
            triUsage, config.WarnOnPreferredPermissionsExceedingThresholds, isPrefPerm);
        bool exceedsVram = CheckForThreshold(config.WarnOnExceedingThresholds, config.VRAMSizeWarningThresholdMiB * 1024 * 1024,
            vramUsage, config.WarnOnPreferredPermissionsExceedingThresholds, isPrefPerm);

        if (_warnedForPlayers.TryGetValue(pair.UserData.UID, out bool hadWarning) && hadWarning)
        {
            _warnedForPlayers[pair.UserData.UID] = exceedsTris || exceedsVram;
            return true;
        }

        _warnedForPlayers[pair.UserData.UID] = exceedsTris || exceedsVram;

        if (exceedsVram)
        {
            _mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PlayerPerformanceService), EventSeverity.Warning,
                $"Exceeds VRAM threshold: ({UiSharedService.ByteToString(vramUsage, addSuffix: true)}/{config.VRAMSizeWarningThresholdMiB} MiB)")));
        }

        if (exceedsTris)
        {
            _mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PlayerPerformanceService), EventSeverity.Warning,
                $"Exceeds triangle threshold: ({triUsage}/{config.TrisAutoPauseThresholdThousands * 1000} triangles)")));
        }

        if (exceedsTris || exceedsVram)
        {
            string warningText = string.Empty;
            if (exceedsTris && !exceedsVram)
            {
                warningText = $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeds your configured triangle warning threshold (" +
                    $"{triUsage}/{config.TrisWarningThresholdThousands * 1000} triangles).";
            }
            else if (!exceedsTris)
            {
                warningText = $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeds your configured VRAM warning threshold (" +
                    $"{UiSharedService.ByteToString(vramUsage, true)}/{config.VRAMSizeWarningThresholdMiB} MiB).";
            }
            else
            {
                warningText = $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeds both VRAM warning threshold (" +
                    $"{UiSharedService.ByteToString(vramUsage, true)}/{config.VRAMSizeWarningThresholdMiB} MiB) and " +
                    $"triangle warning threshold ({triUsage}/{config.TrisWarningThresholdThousands * 1000} triangles).";
            }

            _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeds performance threshold(s)",
                warningText, NotificationType.Warning));
        }

        return true;
    }

    public async Task<bool> CheckTriangleUsageThresholds(Pair pair, CharacterData charaData)
    {
        var config = _playerPerformanceConfigService.Current;

        long triUsage = 0;

        if (!charaData.FileReplacements.TryGetValue(API.Data.Enum.ObjectKind.Player, out List<FileReplacementData>? playerReplacements))
        {
            pair.LastAppliedDataTris = 0;
            return true;
        }

        var moddedModelHashes = playerReplacements
            .Where(p => string.IsNullOrWhiteSpace(p.FileSwapPath) && !string.IsNullOrWhiteSpace(p.Hash)
            && p.GamePaths.Any(g => g.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Hash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var hash in moddedModelHashes)
        {
            triUsage += await _xivDataAnalyzer.GetTrianglesByHash(hash).ConfigureAwait(false);
        }

        pair.LastAppliedDataTris = triUsage;

        _logger.LogDebug("Calculated VRAM usage for {p}", pair.PairUIDName);

        // no warning of any kind on ignored pairs
        if (config.UIDsToIgnore
            .Exists(uid => string.Equals(uid, pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pair.UserData.UID, StringComparison.Ordinal)))
            return true;

        bool isPrefPerm = pair.UserPair.OwnPermissions.HasFlag(API.Data.Enum.UserPermissions.Sticky);

        // now check auto pause
        if (CheckForThreshold(config.AutoPausePlayersExceedingThresholds, config.TrisAutoPauseThresholdThousands * 1000,
            triUsage, config.AutoPausePlayersWithPreferredPermissionsExceedingThresholds, isPrefPerm))
        {
            var pauseDuration = config.PauseDurationAutoPauseExceedingThresholds;

            if (config.WarnOnPausedExceedingThresholds)
            {
                _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) automatically paused",
                    $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeded your configured triangle auto pause threshold (" +
                    $"{triUsage}/{config.TrisAutoPauseThresholdThousands * 1000} triangles)" +
                    $" and has been automatically paused " + GetPauseDurationText(pauseDuration), NotificationType.Warning));
            }

            _mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PlayerPerformanceService), EventSeverity.Warning,
                $"Exceeds triangle threshold: automatically paused ({triUsage}/{config.TrisAutoPauseThresholdThousands * 1000} triangles)")));

            _mediator.Publish(new PauseMessage(pair.UserData, PauseReason.ThresholdTriangles, pauseDuration));

            return false;
        }

        return true;
    }

    public async Task<bool> ComputeAndAutoPauseOnVRAMUsageThresholds(Pair pair, CharacterData charaData, List<DownloadFileTransfer> toDownloadFiles, CancellationToken cancellationToken = default)
    {
        var config = _playerPerformanceConfigService.Current;
        var compressionRedirects = pair.ActiveCompressionRedirects;

        long vramUsage = 0;

        if (!charaData.FileReplacements.TryGetValue(API.Data.Enum.ObjectKind.Player, out List<FileReplacementData>? playerReplacements))
        {
            pair.LastAppliedApproximateVRAMBytes = 0;
            return true;
        }

        var moddedTextureHashes = playerReplacements
            .Where(p => string.IsNullOrWhiteSpace(p.FileSwapPath)&& !string.IsNullOrWhiteSpace(p.Hash) 
            && p.GamePaths.Any(g => g.EndsWith(".tex", StringComparison.OrdinalIgnoreCase)))
            .Select(p => compressionRedirects.GetValueOrDefault(p.Hash, p.Hash))
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fileCaches = await _fileCacheManager.GetFirstFileCacheEntitiesByHashes(moddedTextureHashes.Except(toDownloadFiles.Select(file => file.Hash), StringComparer.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);
        foreach (var hash in moddedTextureHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long fileSize = 0;
            var download = toDownloadFiles.Find(f => string.Equals(hash, f.Hash, StringComparison.OrdinalIgnoreCase));
            if (download != null)
            {
                fileSize = download.TotalRaw;
            }
            else
            {
                if (!fileCaches.TryGetValue(hash.Trim().ToUpperInvariant(), out var fileEntry) || fileEntry == null)
                {
                    continue;
                }
                if (fileEntry.Size < 0)
                {
                    fileEntry.Size = new FileInfo(fileEntry.ResolvedFilepath).Length;
                    await _fileCacheManager.UpdateFileCache(fileEntry, computeProperties: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                fileSize = fileEntry.Size;
            }
            vramUsage += fileSize;
        }

        pair.LastAppliedApproximateVRAMBytes = vramUsage;

        _logger.LogDebug("Calculated VRAM usage for {p}", pair.PairUIDName);

        pair.LastAppliedCompressedAlternates = compressionRedirects.Count;

        // no warning of any kind on ignored pairs
        if (config.UIDsToIgnore.Exists(uid => string.Equals(uid, pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pair.UserData.UID, StringComparison.Ordinal)))
        {
            return true;
        }

        bool isPrefPerm = pair.UserPair.OwnPermissions.HasFlag(API.Data.Enum.UserPermissions.Sticky);

        // now check auto pause
        if (CheckForThreshold(config.AutoPausePlayersExceedingThresholds, config.VRAMSizeAutoPauseThresholdMiB * 1024 * 1024,
            vramUsage, config.AutoPausePlayersWithPreferredPermissionsExceedingThresholds, isPrefPerm))
        {
            var pauseDuration = config.PauseDurationAutoPauseExceedingThresholds;

            if (config.WarnOnPausedExceedingThresholds)
            {
                _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) automatically paused",
                $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeded your configured VRAM auto pause threshold (" +
                $"{UiSharedService.ByteToString(vramUsage, addSuffix: true)}/{config.VRAMSizeAutoPauseThresholdMiB}MiB)" +
                $" and has been automatically paused " + GetPauseDurationText(pauseDuration),
                MareConfiguration.Models.NotificationType.Warning));
            }

            _mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PlayerPerformanceService), EventSeverity.Warning,
                $"Exceeds VRAM threshold: automatically paused ({UiSharedService.ByteToString(vramUsage, addSuffix: true)}/{config.VRAMSizeAutoPauseThresholdMiB} MiB)")));

            _mediator.Publish(new PauseMessage(pair.UserData, PauseReason.ThresholdVram, pauseDuration));

            return false;
        }

        return true;
    }

    private static bool CheckForThreshold(bool thresholdEnabled, long threshold, long value, bool checkForPrefPerm, bool isPrefPerm) =>
        thresholdEnabled && threshold > 0 && threshold < value && ((checkForPrefPerm && isPrefPerm) || !isPrefPerm);

    // Height check
    public bool CheckForRspHeight(Pair pair, CharacterData charaData)
    {
        // don't check stuff if it's not needed
        if (!(_playerPerformanceConfigService.Current.AutoPausePlayersExceedingHeightThresholds || _playerPerformanceConfigService.Current.WarnOnAutoHeightExceedingThreshold))
            return true;

        // whitelist check
        if (_playerPerformanceConfigService.Current.UIDsToIgnoreForHeightPausing
            .Exists(uid => string.Equals(uid, pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pair.UserData.UID, StringComparison.Ordinal)))
            return true;

        // Check if we are pausing direct pairs or not
        bool noAutoPauseDirectPairs = _playerPerformanceConfigService.Current.NoAutoPauseDirectPairs;
        if ((pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional) && noAutoPauseDirectPairs)
            return true;

        var doAutoPausing = _playerPerformanceConfigService.Current.AutoPausePlayersExceedingHeightThresholds;
        var maxHeight = _playerPerformanceConfigService.Current.MaxHeightAbsolute;
        var manualHeight = _playerPerformanceConfigService.Current.MaxHeightManual;
        var multiMax = _playerPerformanceConfigService.Current.MaxHeightMultiplier;
        var shouldWarn = _playerPerformanceConfigService.Current.WarnOnAutoHeightExceedingThreshold;
        var shouldWarnOnPaused = _playerPerformanceConfigService.Current.WarnOnPausedAutoHeightExceedingThreshold;
        var pauseRspExceeded = false;

        // grab player data from glamourer string
        _logger.LogTrace("Glamourer Dictionary: {data}", charaData.GlamourerData[API.Data.Enum.ObjectKind.Player].ToString());
        var (race, gender, height) = GlamourerDecoder.GetRaceAndGender(charaData.GlamourerData[API.Data.Enum.ObjectKind.Player].ToString());
        _logger.LogTrace("Glamourer data: {race} {gender}", race, gender);

        // grab rsp data from penumbra manip string
        ManipRsp.GetRspHeightValues(_logger, charaData.ManipulationData, race, gender, out bool hasMin, out float minRsp, out bool hasMax, out float maxRsp);
        _logger.LogTrace("Get RSP Data: {race} {gender} {hasin} {minrsp} {hasmax} {maxrsp}", race, gender, hasMin, minRsp, hasMax, maxRsp);
        ManipRsp.GetDefaultRspValues(race, gender, out var defaultRspMin, out var defaultRspMax);

        // check for rsp vals and calc player height in cm
        var minVal = hasMin ? minRsp : defaultRspMin;
        var maxVal = hasMax ? maxRsp : defaultRspMax;
        var actualRsp = ManipRsp.GetRspFromSlider(minVal, maxVal, height);
        var maxVanillaHeight = HeightConversion.GetCharacterHeightCm(race, gender, defaultRspMax);
        var actualHeight = HeightConversion.GetCharacterHeightCm(race, gender, actualRsp);
        var maxThreshold = maxVanillaHeight * (multiMax / 100f);

        if (!manualHeight)
        {
            pauseRspExceeded = actualHeight > maxThreshold;
        }
        else
        {
            pauseRspExceeded = actualHeight > maxHeight;
        }

        if (shouldWarn && pauseRspExceeded && !doAutoPausing)
        {
            var threshold = manualHeight ? maxHeight : maxThreshold;
            _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) threshold warning",
            $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeded your configured player height threshold. Their height: {actualHeight:F2}, your threshold: {threshold:F2}",
            MareConfiguration.Models.NotificationType.Warning));
        }

        if (pauseRspExceeded && doAutoPausing)
        {
            _logger.LogWarning("Pair {name} exceeds your height min/max threshold and will be paused.", pair.PairUIDName);
            var pauseDuration = _playerPerformanceConfigService.Current.PauseDurationAutoPauseExceedingHeightThresholds;
            if (shouldWarnOnPaused)
            {
                var threshold = manualHeight ? maxHeight : maxThreshold;
                _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) automatically paused",
                $"Player {pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeded your configured player height threshold and has been automatically paused " + GetPauseDurationText(pauseDuration) +
                $"Their height: {actualHeight:F2}, your threshold: {threshold:F2}",
                MareConfiguration.Models.NotificationType.Warning));
            }
            _mediator.Publish(new PauseMessage(pair.UserData, PauseReason.ThresholdHeight, pauseDuration));

            return false;
        }

        return true;
    }

    private static string GetPauseDurationText(PauseDuration pauseDuration)
    {
        return pauseDuration switch
        {
            PauseDuration.ThirtyMinutes => "for 30 minutes. ",
            PauseDuration.FourHours => "for 4 hours. ",
            PauseDuration.EightHours => "for 8 hours. ",
            PauseDuration.Indefinitely => "until you unpause them. ",
            _ => string.Empty
        };
    }
}