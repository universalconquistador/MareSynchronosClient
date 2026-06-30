using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.WebAPI.Files;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MareSynchronos.Services;

public sealed class PreloaderService
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;

    private sealed record PenumbraOption(
        [property: JsonPropertyName("Files")] Dictionary<string, string>? Files);
    private sealed record PenumbraGroup(
        [property: JsonPropertyName("Options")] List<PenumbraOption>? Options);

    public PreloaderService(
        FileCacheManager fileCacheManager,
        FileUploadManager fileUploadManager,
        DalamudUtilService dalamudUtil,
        IChatGui chat,
        IPluginLog log)
    {
        _fileCacheManager = fileCacheManager;
        _fileUploadManager = fileUploadManager;
        _dalamudUtil = dalamudUtil;
        _chat = chat;
        _log = log;
    }

    public async Task RunAsync(string jsonPath)
    {
        Task Print(string msg) => _dalamudUtil.RunOnFrameworkThread(() => _chat.Print(msg));
        Task PrintError(string msg) => _dalamudUtil.RunOnFrameworkThread(() => _chat.PrintError(msg));

        try
        {
            var json = await File.ReadAllTextAsync(jsonPath).ConfigureAwait(false);
            var group = JsonSerializer.Deserialize<PenumbraGroup>(json);

            var modDir = Path.GetDirectoryName(jsonPath)!;
            var filePaths = (group?.Options ?? [])
                .SelectMany(o => o.Files ?? [])
                .Select(kv => Path.Combine(modDir, kv.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (filePaths.Length == 0)
            {
                await Print("[PlayerSync] No files found in that group.").ConfigureAwait(false);
                return;
            }

            await Print($"[PlayerSync] Found {filePaths.Length} file(s), uploading...").ConfigureAwait(false);

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

            var progress = new Progress<string>(msg => _log.Debug("[PreloadPlaylist] {msg}", msg));
            var failed = await _fileUploadManager.UploadFiles(hashes, progress).ConfigureAwait(false);

            var pushed = hashes.Count - failed.Count;

            // Upload failures come back as hashes; map them to file names.
            var uploadFailures = failed
                .Select(h => cacheEntries.FirstOrDefault(kv =>
                    kv.Value != null && string.Equals(kv.Value!.Hash, h, StringComparison.OrdinalIgnoreCase)).Key ?? h)
                .Select(p => $"{Path.GetFileName(p)} (upload failed)");

            // Uncached files never resolved to a cache entry and are already paths.
            var uncachedFailures = uncached
                .Select(p => $"{Path.GetFileName(p)} (file missing)");

            var failedNames = uploadFailures.Concat(uncachedFailures).ToList();

            await Print($"[PlayerSync] Preload done — {pushed} uploaded, {failedNames.Count} failed.").ConfigureAwait(false);
            if (failedNames.Count > 0)
                await PrintError($"[PlayerSync] Failed files: {string.Join(", ", failedNames)}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "PreloadPlaylist failed");
            await PrintError($"[PlayerSync] Preload failed: {ex.Message}").ConfigureAwait(false);
        }
    }
}
