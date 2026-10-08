using Dalamud.Game.ClientState.Objects.Types;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;


namespace MareSynchronos.PlayerData.Factories;

public class PlayerDataFactory
{
    private static readonly TimeSpan ReportErrorIntervalMinutes = TimeSpan.FromMinutes(5);

    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileCacheManager _fileCacheManager;
    private readonly IpcManager _ipcManager;
    private readonly ILogger<PlayerDataFactory> _logger;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly XivDataAnalyzer _modelAnalyzer;
    private readonly MareMediator _mareMediator;
    private readonly TransientResourceManager _transientResourceManager;
    private bool _isFirstTimeLoading = true;

    private DateTimeOffset _nextErrorTime = DateTimeOffset.MinValue;

    public PlayerDataFactory(ILogger<PlayerDataFactory> logger, DalamudUtilService dalamudUtil, IpcManager ipcManager,
        TransientResourceManager transientResourceManager, FileCacheManager fileReplacementFactory,
        PerformanceCollectorService performanceCollector, XivDataAnalyzer modelAnalyzer, MareMediator mareMediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _ipcManager = ipcManager;
        _transientResourceManager = transientResourceManager;
        _fileCacheManager = fileReplacementFactory;
        _performanceCollector = performanceCollector;
        _modelAnalyzer = modelAnalyzer;
        _mareMediator = mareMediator;
        _logger.LogTrace("Creating {this}", nameof(PlayerDataFactory));
    }

    public Task<CharacterDataFragment?> BuildCharacterData(GameObjectHandler playerRelatedObject, CancellationToken token)
    {
        return BuildCharacterData(playerRelatedObject, null, token);
    }

    public async Task<CharacterDataFragment?> BuildLocalCharacterData(CancellationToken token)
    {
        return await BuildCharacterData(null, () =>
        {
            var localPlayer = _dalamudUtil.GetPlayerCharacter();
            if (localPlayer == null)
            {
                return null;
            }

            return _dalamudUtil.IsInGpose ? _dalamudUtil.GetGposeCharacterFromObjectTableByName(localPlayer.Name.ToString()) : localPlayer;
        }, token).ConfigureAwait(false);
    }

    private async Task<CharacterDataFragment?> BuildCharacterData(GameObjectHandler? playerRelatedObject, Func<ICharacter?>? characterFunc, CancellationToken token)
    {
        if (!_ipcManager.Initialized)
        {
            throw new InvalidOperationException("Penumbra or Glamourer is not connected");
        }

        if (playerRelatedObject == null && characterFunc == null)
        {
            return null;
        }
        var objectKind = playerRelatedObject?.ObjectKind ?? ObjectKind.Player;

        try
        {
            if (_isFirstTimeLoading)
            {
                await _dalamudUtil.RunOnFrameworkThread(() =>
                {
                    var address = _dalamudUtil.GetPlayerPtr();
                    _ipcManager.Penumbra.RedrawOnFramework(_logger, Guid.Empty, address);

                    var minionOrMount = _dalamudUtil.GetMinionOrMountPtrFromFramework(address);
                    if (minionOrMount != IntPtr.Zero)
                    {
                        _ipcManager.Penumbra.RedrawOnFramework(_logger, Guid.Empty, minionOrMount);
                    }
                    var companion = _dalamudUtil.GetCompanionPtrFromFramework(address);
                    if (companion != IntPtr.Zero)
                    {
                        _ipcManager.Penumbra.RedrawOnFramework(_logger, Guid.Empty, companion);
                    }
                    var pet = _dalamudUtil.GetPetPtrFromFramework(address);
                    if (pet != IntPtr.Zero)
                    {
                        _ipcManager.Penumbra.RedrawOnFramework(_logger, Guid.Empty, pet);
                    }
                }).ConfigureAwait(false);

                _isFirstTimeLoading = false;
            }

            return await _performanceCollector.LogPerformance(this, $"CreateCharacterData>{objectKind}", async () =>
            {
                return await CreateCharacterData(playerRelatedObject, characterFunc, token).ConfigureAwait(false);
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Cancelled creating Character data for {object}", playerRelatedObject);
            throw;
        }
        catch (Exception e) when (e.GetBaseException() is NullReferenceException) // shouldn't happen but there are race conditions like when zoning/teleporting
        {
            _logger.LogError(e, "Failed to create {object} data", playerRelatedObject);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create {object} data", playerRelatedObject);

            if (objectKind == ObjectKind.Player)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now > _nextErrorTime)
                {
                    _nextErrorTime = now.Add(ReportErrorIntervalMinutes);
                    _mareMediator.Publish(new NotificationMessage("Character Data Failed", "One or more errors occurred when creating character data. " +
                        "Check the /xllog for more details.", NotificationType.Error));
                }
            }
        }

        return null;
    }

    private async Task<CharacterDataFragment?> CreateCharacterData(GameObjectHandler? playerRelatedObject, Func<ICharacter?>? characterFunc, CancellationToken ct)
    {
        var objectKind = playerRelatedObject?.ObjectKind ?? ObjectKind.Player;
        CharacterDataFragment fragment = objectKind == ObjectKind.Player ? new CharacterDataFragmentPlayer() : new();

        _logger.LogDebug("Building character data for {obj}", playerRelatedObject);

        // wait until chara is not drawing and present so nothing spontaneously explodes
        DrawWaitResult waitResult;
        if (characterFunc == null)
        {
            waitResult = await _dalamudUtil.WaitWhileCharacterIsDrawing(_logger, playerRelatedObject!, Guid.NewGuid(), 30000, ct: ct).ConfigureAwait(false);
        }
        else
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var state = await _dalamudUtil.RunOnFrameworkThread(() =>
                {
                    var actor = characterFunc();

                    return actor == null ? DrawState.Missing : _dalamudUtil.GetCharacterDrawStateFromFramework(actor.Address, actor.ObjectIndex).State;
                }).ConfigureAwait(false);
                if (state != DrawState.Loading || timer.ElapsedMilliseconds >= 30000)
                {
                    waitResult = state == DrawState.Ready ? DrawWaitResult.Ready : state == DrawState.Missing ? DrawWaitResult.Missing : DrawWaitResult.TimedOut;

                    break;
                }

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        if (waitResult == DrawWaitResult.Missing)
        {
            return null;
        }
        if (waitResult != DrawWaitResult.Ready)
        {
            throw new InvalidDataException($"Cannot collect {objectKind}: draw wait ended with {waitResult}.");
        }

        ct.ThrowIfCancellationRequested();

        Dictionary<string, List<ushort>>? boneIndices;
        Dictionary<string, HashSet<string>>? resolvedPaths;
        DateTime start = DateTime.UtcNow;
        var captured = await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var actor = characterFunc == null ? playerRelatedObject!.GetGameObject() as ICharacter : characterFunc();

            if (actor == null)
            {
                throw new InvalidDataException("Character became unavailable during capture.");
            }

            return (Bones: objectKind == ObjectKind.Player ? _modelAnalyzer.GetSkeletonBoneIndices(actor.Address) : null, Paths: _ipcManager.Penumbra.GetCharacterDataOnFramework(actor.ObjectIndex));
        }).ConfigureAwait(false);

        boneIndices = captured.Bones;
        resolvedPaths = captured.Paths;
        if (resolvedPaths == null)
        {
            throw new InvalidOperationException("Penumbra returned null data");
        }

        ct.ThrowIfCancellationRequested();

        fragment.FileReplacements = new HashSet<FileReplacement>(resolvedPaths.Select(c => new FileReplacement([.. c.Value], c.Key)), FileReplacementComparer.Instance)
            .Where(p => p.HasFileReplacement).ToHashSet();
        fragment.FileReplacements.RemoveWhere(c => c.GamePaths.Any(g => !CacheMonitor.AllowedFileExtensions.Any(e => g.EndsWith(e, StringComparison.OrdinalIgnoreCase))));

        ct.ThrowIfCancellationRequested();

        _logger.LogDebug("== Static Replacements ==");
        foreach (var replacement in fragment.FileReplacements.Where(i => i.HasFileReplacement).OrderBy(i => i.GamePaths.First(), StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogDebug("=> {repl}", replacement);
            ct.ThrowIfCancellationRequested();
        }

        await _transientResourceManager.WaitForRecording(ct).ConfigureAwait(false);

        // if it's pet then it's summoner, if it's summoner we actually want to keep all filereplacements alive at all times
        // or we get into redraw city for every change and nothing works properly
        if (objectKind == ObjectKind.Pet)
        {
            foreach (var item in fragment.FileReplacements.Where(i => i.HasFileReplacement).SelectMany(p => p.GamePaths))
            {
                if (_transientResourceManager.AddTransientResource(objectKind, item))
                {
                    _logger.LogDebug("Marking static {item} for Pet as transient", item);
                }
            }

            _logger.LogTrace("Clearing {count} Static Replacements for Pet", fragment.FileReplacements.Count);
            fragment.FileReplacements.Clear();
        }

        ct.ThrowIfCancellationRequested();

        _logger.LogDebug("Handling transient update for {obj}", playerRelatedObject);

        // remove all potentially gathered paths from the transient resource manager that are resolved through static resolving
        _transientResourceManager.ClearTransientPaths(objectKind, fragment.FileReplacements.SelectMany(c => c.GamePaths).ToList());

        // get all remaining paths and resolve them
        var transientPaths = ManageSemiTransientData(objectKind);
        var resolvedTransientPaths = await GetFileReplacementsFromPaths(transientPaths, new HashSet<string>(StringComparer.Ordinal)).ConfigureAwait(false);

        _logger.LogDebug("== Transient Replacements ==");
        foreach (var replacement in resolvedTransientPaths.Select(c => new FileReplacement([.. c.Value], c.Key)).OrderBy(f => f.ResolvedPath, StringComparer.Ordinal))
        {
            _logger.LogDebug("=> {repl}", replacement);
            fragment.FileReplacements.Add(replacement);
        }

        // clean up all semi transient resources that don't have any file replacement (aka null resolve)
        _transientResourceManager.CleanUpSemiTransientResources(objectKind, [.. fragment.FileReplacements]);

        ct.ThrowIfCancellationRequested();

        // make sure we only return data that actually has file replacements
        fragment.FileReplacements = new HashSet<FileReplacement>(fragment.FileReplacements.Where(v => v.HasFileReplacement).OrderBy(v => v.ResolvedPath, StringComparer.Ordinal), FileReplacementComparer.Instance);

        // gather up data from ipc
        var playerFragment = fragment as CharacterDataFragmentPlayer;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var chara = characterFunc == null ? playerRelatedObject!.GetGameObject() as ICharacter : characterFunc();
            if (chara == null)
            {
                throw new InvalidOperationException("Player character became unavailable while collecting IPC data.");
            }

            var index = chara.ObjectIndex;
            var address = chara.Address;

            fragment.GlamourerString = _ipcManager.Glamourer.GetCharacterCustomizationOnFramework(index);
            fragment.CustomizePlusScale = _ipcManager.CustomizePlus.GetScaleOnFramework(index);
            fragment.LociData = _ipcManager.Loci.GetActorManagerOnFramework(address);

            if (playerFragment != null)
            {
                playerFragment.ManipulationString = _ipcManager.Penumbra.GetMetaManipulationsOnFramework();
                playerFragment.HonorificData = _ipcManager.Honorific.GetTitleOnFramework();
                playerFragment.HeelsData = _ipcManager.Heels.GetOffsetOnFramework();
                playerFragment.MoodlesData = _ipcManager.Moodles.GetStatusOnFramework(address) ?? string.Empty;
                playerFragment.PetNamesData = _ipcManager.PetNames.GetLocalNamesOnFramework();
            }
        }).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        fragment.CustomizePlusScale = string.IsNullOrEmpty(fragment.CustomizePlusScale) ? string.Empty
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(fragment.CustomizePlusScale));
        if (playerFragment != null)
        {
            playerFragment.HonorificData = string.IsNullOrEmpty(playerFragment.HonorificData) ? string.Empty
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(playerFragment.HonorificData));
        }

        // remove missing mods files

        var toCompute = fragment.FileReplacements.Where(f => !f.IsFileSwap).ToArray();
        _logger.LogDebug("Getting Hashes for {amount} Files", toCompute.Length);
        // This is one of the worst code paths ever, it essentially tries to account for every possible scenario why we might not have a valid file cache and
        // tries to create/correct for it. This really should be thought through, especially if ResolvedPath is reworked.
        var computedPaths = await _fileCacheManager.GetOrCreateValidatedFileCachesByPathsAsync(toCompute.Select(c => c.ResolvedPath).ToArray(), ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        foreach (var file in toCompute)
        {
            file.Hash = computedPaths[file.ResolvedPath]?.Hash ?? string.Empty;
        }

        var removed = fragment.FileReplacements.RemoveWhere(f => !f.IsFileSwap && string.IsNullOrEmpty(f.Hash));
        if (removed > 0)
        {
            _logger.LogDebug("Removed {amount} of invalid files", removed);
        }

        ct.ThrowIfCancellationRequested();

        if (objectKind == ObjectKind.Player)
        {
            try
            {
                await VerifyPlayerAnimationBones(boneIndices, (fragment as CharacterDataFragmentPlayer)!, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException e)
            {
                _logger.LogDebug(e, "Cancelled during player animation verification");
                throw;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to verify player animations, continuing without further verification");
            }
        }

        _logger.LogInformation("Building character data for {obj} took {time}ms", objectKind, TimeSpan.FromTicks(DateTime.UtcNow.Ticks - start.Ticks).TotalMilliseconds);

        return fragment;
    }

    private async Task VerifyPlayerAnimationBones(Dictionary<string, List<ushort>>? boneIndices, CharacterDataFragmentPlayer fragment, CancellationToken ct)
    {
        if (boneIndices == null) return;

        foreach (var kvp in boneIndices)
        {
            _logger.LogDebug("Found {skellyname} ({idx} bone indices) on player: {bones}", kvp.Key, kvp.Value.Any() ? kvp.Value.Max() : 0, string.Join(',', kvp.Value));
        }

        if (boneIndices.All(u => u.Value.Count == 0)) return;

        int noValidationFailed = 0;
        foreach (var file in fragment.FileReplacements.Where(f => !f.IsFileSwap && f.GamePaths.First().EndsWith("pap", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            ct.ThrowIfCancellationRequested();

            var skeletonIndices = await _dalamudUtil.RunOnFrameworkThread(() => _modelAnalyzer.GetBoneIndicesFromPap(file.Hash)).ConfigureAwait(false);
            bool validationFailed = false;
            if (skeletonIndices != null)
            {
                // 105 is the maximum vanilla skellington spoopy bone index
                if (skeletonIndices.All(k => k.Value.Max() <= 105))
                {
                    _logger.LogTrace("All indices of {path} are <= 105, ignoring", file.ResolvedPath);
                    continue;
                }

                _logger.LogDebug("Verifying bone indices for {path}, found {x} skeletons", file.ResolvedPath, skeletonIndices.Count);

                foreach (var boneCount in skeletonIndices.Select(k => k).ToList())
                {
                    if (boneCount.Value.Max() > boneIndices.SelectMany(b => b.Value).Max())
                    {
                        _logger.LogWarning("Found more bone indices on the animation {path} skeleton {skl} (max indice {idx}) than on any player related skeleton (max indice {idx2})",
                            file.ResolvedPath, boneCount.Key, boneCount.Value.Max(), boneIndices.SelectMany(b => b.Value).Max());
                        validationFailed = true;
                        break;
                    }
                }
            }

            if (validationFailed)
            {
                noValidationFailed++;
                _logger.LogDebug("Removing {file} from sent file replacements and transient data", file.ResolvedPath);
                fragment.FileReplacements.Remove(file);
                foreach (var gamePath in file.GamePaths)
                {
                    _transientResourceManager.RemoveTransientResource(ObjectKind.Player, gamePath);
                }
            }

        }

        if (noValidationFailed > 0)
        {
            _mareMediator.Publish(new NotificationMessage("Invalid Skeleton Setup",
                $"Your client is attempting to send {noValidationFailed} animation files with invalid bone data. Those animation files have been removed from your sent data. " +
                $"Verify that you are using the correct skeleton for those animation files (Check /xllog for more information).",
                NotificationType.Warning, TimeSpan.FromSeconds(10)));
        }
    }

    private async Task<IReadOnlyDictionary<string, string[]>> GetFileReplacementsFromPaths(HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        var forwardPaths = forwardResolve.ToArray();
        var reversePaths = reverseResolve.ToArray();
        Dictionary<string, List<string>> resolvedPaths = new(StringComparer.Ordinal);
        var (forward, reverse) = await _ipcManager.Penumbra.ResolvePathsAsync(forwardPaths, reversePaths).ConfigureAwait(false);
        for (int i = 0; i < forwardPaths.Length; i++)
        {
            var filePath = forward[i].ToLowerInvariant();
            if (resolvedPaths.TryGetValue(filePath, out var list))
            {
                list.Add(forwardPaths[i].ToLowerInvariant());
            }
            else
            {
                resolvedPaths[filePath] = [forwardPaths[i].ToLowerInvariant()];
            }
        }

        for (int i = 0; i < reversePaths.Length; i++)
        {
            var filePath = reversePaths[i].ToLowerInvariant();
            if (resolvedPaths.TryGetValue(filePath, out var list))
            {
                list.AddRange(reverse[i].Select(c => c.ToLowerInvariant()));
            }
            else
            {
                resolvedPaths[filePath] = new List<string>(reverse[i].Select(c => c.ToLowerInvariant()).ToList());
            }
        }

        return resolvedPaths.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.OrdinalIgnoreCase).AsReadOnly();
    }

    private HashSet<string> ManageSemiTransientData(ObjectKind objectKind)
    {
        _transientResourceManager.PersistTransientResources(objectKind);

        HashSet<string> pathsToResolve = new(StringComparer.Ordinal);
        foreach (var path in _transientResourceManager.GetSemiTransientResources(objectKind).Where(path => !string.IsNullOrEmpty(path)))
        {
            pathsToResolve.Add(path);
        }

        return pathsToResolve;
    }

    public async Task<string> GetAddonPluginPlayerData(PlayerChanges playerChanges)
    {
        string data = await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var chara = _dalamudUtil.GetPlayerCharacter();
            if (chara == null)
            {
                return string.Empty;
            }

            switch (playerChanges)
            {
                case PlayerChanges.Honorific:
                    return _ipcManager.Honorific.GetTitleOnFramework();

                case PlayerChanges.Heels:
                    return _ipcManager.Heels.GetOffsetOnFramework();

                case PlayerChanges.Moodles:
                    return _ipcManager.Moodles.GetStatusOnFramework(chara.Address) ?? string.Empty;

                case PlayerChanges.PetNames:
                    return _ipcManager.PetNames.GetLocalNamesOnFramework();

                default:
                    return string.Empty;
            }
        }).ConfigureAwait(false);

        return playerChanges == PlayerChanges.Honorific && !string.IsNullOrEmpty(data) ? Convert.ToBase64String(Encoding.UTF8.GetBytes(data)) : data;
    }
}
