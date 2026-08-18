using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MareSynchronos.Services;

// Key is the group GUID in v4 and the file name on v3.
public sealed record PreloadGroup(string Key, string Name, IReadOnlyList<string> FilePaths);

public sealed record PreloadTarget(string ModRoot, string ModName, IReadOnlyList<PreloadGroup> Groups);

public sealed class PreloaderService
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly IpcManager _ipcManager;
    private readonly MareMediator _mediator;
    private readonly ILogger<PreloaderService> _logger;

    private const string MetaFile = "meta.json";
    private const string LegacyDefaultMod = "default_mod.json";
    private const string LegacyGroupGlob = "group_*.json";
    private const int MetaJsonLayoutVersion = 4;

    private const int MaxReportedFailures = 5;

    // We treat the default mod replacements as a virtual group that can be selected.
    private const string DefaultGroupKey = "::default::";
    private const string DefaultGroupName = "Default";

    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    public PreloaderService(
        FileCacheManager fileCacheManager,
        FileUploadManager fileUploadManager,
        IpcManager ipcManager,
        MareMediator mediator,
        ILogger<PreloaderService> logger)
    {
        _fileCacheManager = fileCacheManager;
        _fileUploadManager = fileUploadManager;
        _ipcManager = ipcManager;
        _mediator = mediator;
        _logger = logger;
    }

    /// <param name="modDirectoryName">
    /// Use with caution, this is safe today because we get the results from IpcCallerPenumbra.GetMods. There is no guard against traversing out of the directory.
    /// </param>
    internal (PreloadTarget? Target, string? Error) TryReadMod(string modDirectoryName, string modName)
    {
        var modRoot = string.Empty;
        try
        {
            var modDirectory = _ipcManager.Penumbra.ModDirectory;
            if (string.IsNullOrEmpty(modDirectory))
                return (null, "Penumbra's mod folder is not available.");

            modRoot = Path.Combine(modDirectory, modDirectoryName);

            var groups = new List<PreloadGroup>();

            if (!TryCollectModFiles(modRoot, groups))
                return (null, $"No Penumbra mod metadata ({MetaFile}) in {modDirectoryName}.");

            return (new PreloadTarget(modRoot, modName, groups), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read mod at {modRoot}", modRoot);
            return (null, $"Could not read mod: {ex.Message}");
        }
    }

    public async Task UploadAsync(PreloadTarget target, IReadOnlySet<string> selectedGroups)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            _mediator.Publish(new NotificationMessage("Preload In Progress",
                "A preload is already running. Wait for it to finish.", NotificationType.Warning));
            return;
        }

        try
        {
            var filePaths = target.Groups
                .Where(g => selectedGroups.Contains(g.Key))
                .SelectMany(g => g.FilePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (filePaths.Length == 0)
            {
                _mediator.Publish(new NotificationMessage("No Files Found", "That selection replaces no files.", NotificationType.Info));
                return;
            }

            _mediator.Publish(new NotificationMessage("Preload Started",
                $"Found {filePaths.Length} file(s), uploading...", NotificationType.Info));

            var cacheEntries = _fileCacheManager.GetFileCachesByPaths(filePaths);
            var hashes = cacheEntries.Values
                .Where(e => e != null)
                .Select(e => e!.Hash)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Files that never resolved to a cache entry (missing on disk, outside the
            // Penumbra mod folder, etc.) can't be uploaded — count them as failures.
            var uncached = cacheEntries
                .Where(kv => kv.Value == null)
                .Select(kv => kv.Key)
                .ToList();

            var progress = new Progress<string>(msg => _logger.LogDebug("{msg}", msg));
            var (failed, actuallyUploaded) = await _fileUploadManager.UploadFiles(hashes, progress).ConfigureAwait(false);
            var alreadyUploaded = Math.Max(0, hashes.Count - actuallyUploaded - failed.Count);

            // Upload failures come back as hashes; map them to file names.
            var uploadFailures = failed
                .Select(h => cacheEntries.FirstOrDefault(kv =>
                    kv.Value != null && string.Equals(kv.Value!.Hash, h, StringComparison.OrdinalIgnoreCase)).Key ?? h)
                .Select(p => $"{Path.GetFileName(p)} (upload failed)");

            // Uncached files never resolved to a cache entry and are already paths.
            var uncachedFailures = uncached
                .Select(p => $"{Path.GetFileName(p)} (file missing)");

            var failedNames = uploadFailures.Concat(uncachedFailures).ToList();

            _mediator.Publish(new NotificationMessage("Preload Complete",
                $"Preload done — {actuallyUploaded} uploaded, {alreadyUploaded} already on server, {failedNames.Count} failed.", NotificationType.Info));

            if (failedNames.Count > 0)
            {
                _logger.LogWarning("Preload of {mod} failed {count} file(s): {files}",
                    target.ModName, failedNames.Count, string.Join(", ", failedNames));

                var shown = string.Join(", ", failedNames.Take(MaxReportedFailures));
                var more = failedNames.Count > MaxReportedFailures
                    ? $" and {failedNames.Count - MaxReportedFailures} more (see /xllog)" : string.Empty;
                _mediator.Publish(new NotificationMessage("Preload Failures", $"Failed files: {shown}{more}", NotificationType.Warning));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preload failed for {modRoot}", target.ModRoot);
            _mediator.Publish(new NotificationMessage("Preload Failed", $"Preload failed: {ex.Message}", NotificationType.Error));
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private bool TryCollectModFiles(string modRoot, List<PreloadGroup> groups)
    {
        var hasMetadata = false;
        var defaultFiles = new List<string>();

        var metaPath = Path.Combine(modRoot, MetaFile);
        JsonDocument? meta = null;
        var fileVersion = 0;

        if (File.Exists(metaPath) && TryParse(metaPath, out var parsed))
        {
            meta = parsed;
            hasMetadata = true;
            if (meta.RootElement.TryGetProperty("FileVersion", out var version) && version.TryGetInt32(out var number))
                fileVersion = number;
        }

        using (meta)
        {
            if (fileVersion >= MetaJsonLayoutVersion)
            {
                var root = meta!.RootElement;

                if (root.TryGetProperty("DefaultData", out var defaultData))
                    CollectFiles(defaultData, modRoot, defaultFiles);

                if (root.TryGetProperty("Groups", out var groupArray) && groupArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var group in groupArray.EnumerateArray())
                    {
                        var fallback = $"Group {groups.Count + 1}";
                        AddGroup(modRoot, group, StringOf(group, "Id", fallback), StringOf(group, "Name", fallback), groups);
                    }
                }
            }
            else
            {
                CollectLegacyFiles(modRoot, defaultFiles, groups, ref hasMetadata);
            }
        }

        // Listed first, so the files Penumbra always applies are the first thing offered.
        if (defaultFiles.Count > 0)
        {
            groups.Insert(0, new PreloadGroup(DefaultGroupKey, DefaultGroupName,
                [.. defaultFiles.Distinct(StringComparer.OrdinalIgnoreCase)]));
        }

        return hasMetadata;
    }

    /// TODO: remove this and cleanup surrounding code once everyone is on penumbra v4 metadata schema
    private void CollectLegacyFiles(string modRoot, List<string> defaultFiles, List<PreloadGroup> groups, ref bool hasMetadata)
    {
        foreach (var file in EnumDir(modRoot, LegacyDefaultMod))
        {
            if (!TryParse(file, out var doc))
            {
                LogUnreadable(file, modRoot);
                continue;
            }
            using (doc)
            {
                hasMetadata = true;
                CollectFiles(doc.RootElement, modRoot, defaultFiles);
            }
        }

        foreach (var file in EnumDir(modRoot, LegacyGroupGlob).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParse(file, out var doc))
            {
                LogUnreadable(file, modRoot);
                continue;
            }
            using (doc)
            {
                hasMetadata = true;
                AddGroup(modRoot, doc.RootElement, Path.GetFileName(file),
                    StringOf(doc.RootElement, "Name", Path.GetFileNameWithoutExtension(file)), groups);
            }
        }
    }

    private void AddGroup(string modRoot, JsonElement group, string key, string name, List<PreloadGroup> into)
    {
        var files = new List<string>();

        if (group.ValueKind == JsonValueKind.Object
            && group.TryGetProperty("Options", out var options) && options.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in options.EnumerateArray())
                CollectFiles(option, modRoot, files);
        }

        into.Add(new PreloadGroup(key, name, [.. files.Distinct(StringComparer.OrdinalIgnoreCase)]));
    }

    private static string StringOf(JsonElement element, string property, string fallback)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return fallback;
    }

    private void CollectFiles(JsonElement owner, string modRoot, List<string> into)
    {
        if (owner.ValueKind != JsonValueKind.Object) return;
        if (!owner.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Object) return;

        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modRoot));

        foreach (var entry in files.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String) continue;
            var relative = entry.Value.GetString();
            if (string.IsNullOrWhiteSpace(relative)) continue;

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(rootFull, relative));
            }
            catch (Exception)
            {
                // Rooted, too long, or otherwise unusable as a path.
                full = string.Empty;
            }

            // A malformed "..\" must not pull in files from outside the mod folder.
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping {path} — resolves outside {modRoot}", relative, modRoot);
                continue;
            }
            into.Add(full);
        }
    }

    private void LogUnreadable(string file, string modRoot)
        => _logger.LogWarning("{file} in {modRoot} could not be parsed; its files will not be uploaded",
            Path.GetFileName(file), modRoot);

    private bool TryParse(string path, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read {path}", path);
            document = null!;
            return false;
        }
    }

    private string[] EnumDir(string dir, string pattern)
    {
        try
        {
            return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate {dir}", dir);
            return [];
        }
    }
}
