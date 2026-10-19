using Lumina.Data.Files;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.FileCache;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class CharacterAnalyzer : MediatorSubscriberBase, IDisposable
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataAnalyzer _xivDataAnalyzer;
    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource _baseAnalysisCts = new();
    private string _lastDataHash = string.Empty;
    public CharacterAnalysisMetrics? LastMetrics { get; private set; }

    public CharacterAnalyzer(ILogger<CharacterAnalyzer> logger, MareMediator mediator, FileCacheManager fileCacheManager, XivDataAnalyzer modelAnalyzer)
        : base(logger, mediator)
    {
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            _baseAnalysisCts = _baseAnalysisCts.CancelRecreate();
            var token = _baseAnalysisCts.Token;
            _ = BaseAnalysis(msg.CharacterData, token);
        });
        _fileCacheManager = fileCacheManager;
        _xivDataAnalyzer = modelAnalyzer;
    }

    public int CurrentFile { get; internal set; }
    public bool IsAnalysisRunning => _analysisCts != null;
    public int TotalFiles { get; internal set; }
    internal Dictionary<ObjectKind, Dictionary<string, FileDataEntry>> LastAnalysis { get; private set; } = [];

    public void CancelAnalyze()
    {
        _analysisCts?.CancelDispose();
        _analysisCts = null;
    }

    public async Task ComputeAnalysis(bool print = true, bool recalculate = false)
    {
        Logger.LogDebug("=== Calculating Character Analysis ===");

        var analysisCts = _analysisCts?.CancelRecreate() ?? new CancellationTokenSource();
        _analysisCts = analysisCts;
        var cancelToken = analysisCts.Token;

        try
        {
            var allFiles = LastAnalysis.SelectMany(value => value.Value.Select(data => data.Value)).ToList();
            var remaining = allFiles.GroupBy(file => file.Hash, StringComparer.OrdinalIgnoreCase).Where(group => recalculate || group.Any(file => !file.IsComputed)).ToList();

            TotalFiles = remaining.Count;
            CurrentFile = 0;

            if (remaining.Count > 0)
            {
                Logger.LogDebug("=== Computing {amount} remaining files ===", remaining.Count);

                Mediator.Publish(new HaltScanMessage(nameof(CharacterAnalyzer)));
                try
                {
                    foreach (var fileGroup in remaining)
                    {
                        cancelToken.ThrowIfCancellationRequested();

                        var file = fileGroup.First();
                        Logger.LogDebug("Computing file {file}", file.FilePaths[0]);
                        await file.ComputeSizes(_fileCacheManager, cancelToken).ConfigureAwait(false);

                        foreach (var matchingFile in fileGroup)
                        {
                            matchingFile.OriginalSize = file.OriginalSize;
                            matchingFile.CompressedSize = file.CompressedSize;
                        }

                        CurrentFile++;
                    }
                }
                catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
                {
                    Logger.LogDebug("Character analysis was cancelled");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to analyze files");
                }
                finally
                {
                    Mediator.Publish(new ResumeScanMessage(nameof(CharacterAnalyzer)));
                }
            }

            Mediator.Publish(new CharacterDataAnalyzedMessage());
            LastMetrics = BuildMetrics(LastAnalysis);

            if (print) PrintAnalysis();
        }
        finally
        {
            if (ReferenceEquals(_analysisCts, analysisCts))
            {
                analysisCts.CancelDispose();
                _analysisCts = null;
            }
        }
    }

    public void Dispose()
    {
        _analysisCts?.CancelDispose();
        _analysisCts = null;
        _baseAnalysisCts.CancelDispose();
    }

    private async Task BaseAnalysis(CharacterData charaData, CancellationToken token)
    {
        if (string.Equals(charaData.DataHash.Value, _lastDataHash, StringComparison.Ordinal)) return;

        Dictionary<ObjectKind, Dictionary<string, FileDataEntry>> lastAnalysis = [];
        Dictionary<string, long> trianglesByHash = new(StringComparer.OrdinalIgnoreCase);

        List<string> hashes = charaData.FileReplacements.Values
            .SelectMany(replacements => replacements)
            .Select(replacement => replacement.Hash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.OrdinalIgnoreCase) // is the StringComparer actually needed?
            .ToList();

        var fileCachesByHash = await _fileCacheManager.GetAllFileCacheEntitiesByHashes(hashes, token).ConfigureAwait(false);
        // We only want the local mods from Penumbra for the player
        fileCachesByHash = fileCachesByHash.ToDictionary(fileCache => fileCache.Key, fileCache => fileCache.Value.Where(cache => cache.Store == FileCacheStore.Penumbra).ToList());

        foreach (var obj in charaData.FileReplacements)
        {
            Dictionary<string, FileDataEntry> data = new(StringComparer.OrdinalIgnoreCase);
            foreach (var fileEntry in obj.Value)
            {
                token.ThrowIfCancellationRequested();

                if (!fileCachesByHash.TryGetValue(fileEntry.Hash, out var fileCacheEntries) || !fileCacheEntries.Any())
                {
                    continue;
                }

                var filePath = fileCacheEntries[0].ResolvedFilepath;
                var extension = Path.GetExtension(filePath);
                var ext = string.IsNullOrEmpty(extension) ? "unk?" : extension[1..];

                if (!trianglesByHash.TryGetValue(fileEntry.Hash, out var tris))
                {
                    tris = await _xivDataAnalyzer.GetTrianglesByHash(fileEntry.Hash).ConfigureAwait(false);
                    trianglesByHash[fileEntry.Hash] = tris;
                }

                var originalSize = fileCacheEntries.Where(entry => entry.Size > 0).Select(entry => entry.Size).DefaultIfEmpty(0).Min();
                var compressedSize = fileCacheEntries.Where(entry => entry.CompressedSize > 0).Select(entry => entry.CompressedSize).DefaultIfEmpty(0).Min();
                data[fileEntry.Hash] = new FileDataEntry(fileEntry.Hash, ext,
                    [.. fileEntry.GamePaths],
                    fileCacheEntries.Select(fileCache => fileCache.ResolvedFilepath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    originalSize,
                    compressedSize,
                    tris);
            }

            lastAnalysis[obj.Key] = data;
        }

        token.ThrowIfCancellationRequested();
        LastAnalysis = lastAnalysis;

        Mediator.Publish(new CharacterDataAnalyzedMessage());
        LastMetrics = BuildMetrics(LastAnalysis);

        _lastDataHash = charaData.DataHash.Value;
    }

    private void PrintAnalysis()
    {
        if (LastAnalysis.Count == 0) return;
        foreach (var kvp in LastAnalysis)
        {
            int fileCounter = 1;
            int totalFiles = kvp.Value.Count;
            Logger.LogInformation("=== Analysis for {obj} ===", kvp.Key);

            foreach (var entry in kvp.Value.OrderBy(b => b.Value.GamePaths.OrderBy(p => p, StringComparer.Ordinal).First(), StringComparer.Ordinal))
            {
                Logger.LogInformation("File {x}/{y}: {hash}", fileCounter++, totalFiles, entry.Key);
                foreach (var path in entry.Value.GamePaths)
                {
                    Logger.LogInformation("  Game Path: {path}", path);
                }
                if (entry.Value.FilePaths.Count > 1) Logger.LogInformation("  Multiple fitting files detected for {key}", entry.Key);
                foreach (var filePath in entry.Value.FilePaths)
                {
                    Logger.LogInformation("  File Path: {path}", filePath);
                }
                Logger.LogInformation("  Size: {size}, Compressed: {compressed}", UiSharedService.ByteToString(entry.Value.OriginalSize),
                    UiSharedService.ByteToString(entry.Value.CompressedSize));
            }
        }
        foreach (var kvp in LastAnalysis)
        {
            Logger.LogInformation("=== Detailed summary by file type for {obj} ===", kvp.Key);
            foreach (var entry in kvp.Value.Select(v => v.Value).GroupBy(v => v.FileType, StringComparer.Ordinal))
            {
                Logger.LogInformation("{ext} files: {count}, size extracted: {size}, size compressed: {sizeComp}", entry.Key, entry.Count(),
                    UiSharedService.ByteToString(entry.Sum(v => v.OriginalSize)), UiSharedService.ByteToString(entry.Sum(v => v.CompressedSize)));
            }
            Logger.LogInformation("=== Total summary for {obj} ===", kvp.Key);
            Logger.LogInformation("Total files: {count}, size extracted: {size}, size compressed: {sizeComp}", kvp.Value.Count,
            UiSharedService.ByteToString(kvp.Value.Sum(v => v.Value.OriginalSize)), UiSharedService.ByteToString(kvp.Value.Sum(v => v.Value.CompressedSize)));
        }

        Logger.LogInformation("=== Total summary for all currently present objects ===");
        Logger.LogInformation("Total files: {count}, size extracted: {size}, size compressed: {sizeComp}",
            LastAnalysis.Values.Sum(v => v.Values.Count),
            UiSharedService.ByteToString(LastAnalysis.Values.Sum(c => c.Values.Sum(v => v.OriginalSize))),
            UiSharedService.ByteToString(LastAnalysis.Values.Sum(c => c.Values.Sum(v => v.CompressedSize))));
        Logger.LogInformation("IMPORTANT NOTES:\n\r- For Sync up- and downloads only the compressed size is relevant.\n\r- An unusually high total files count beyond 200 and up will also increase your download time to others significantly.");
    }

    internal sealed record FileDataEntry(string Hash, string FileType, List<string> GamePaths, List<string> FilePaths, long OriginalSize, long CompressedSize, long Triangles)
    {
        public bool IsComputed => OriginalSize > 0 && CompressedSize > 0;
        public async Task ComputeSizes(FileCacheManager fileCacheManager, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var fileCacheEntries = await fileCacheManager.GetAllFileCacheEntitiesByHash(Hash, token).ConfigureAwait(false);
            fileCacheEntries = fileCacheEntries.Where(caches => caches.Store == FileCacheStore.Penumbra).ToList();
            List<FileCacheEntity> entries = [];

            foreach (var fileCacheEntry in fileCacheEntries)
            {
                token.ThrowIfCancellationRequested();

                var validated = await fileCacheManager.ValidateFileCacheAsync(fileCacheEntry, token).ConfigureAwait(false);
                if (validated != null && string.Equals(validated.Hash, Hash, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(validated);
                }
            }

            if (entries.Count == 0)
            {
                return;
            }

            var compressedsize = await fileCacheManager.GetCompressedFileData(Hash, token).ConfigureAwait(false);
            var normalSize = new FileInfo(entries[0].ResolvedFilepath).Length;
            var compressedSize = compressedsize.Item2.LongLength;

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                entry.Size = normalSize;
                entry.CompressedSize = compressedSize;
                await fileCacheManager.UpdateFileCache(entry, computeProperties: false, cancellationToken: token).ConfigureAwait(false);
            }

            OriginalSize = normalSize;
            CompressedSize = compressedSize;
        }
        public long OriginalSize { get; set; } = OriginalSize;
        public long CompressedSize { get; set; } = CompressedSize;
        public long Triangles { get; set; } = Triangles;

        public Lazy<string> Format = new(() =>
        {
            switch (FileType)
            {
                case "tex":
                    {
                        try
                        {
                            using var stream = new FileStream(FilePaths[0], FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var reader = new BinaryReader(stream);
                            reader.BaseStream.Position = 4;
                            var format = (TexFile.TextureFormat)reader.ReadInt32();
                            return format.ToString();
                        }
                        catch
                        {
                            return "Unknown";
                        }
                    }
                default:
                    return string.Empty;
            }
        });
    }

    private static CharacterAnalysisMetrics BuildMetrics(IReadOnlyDictionary<ObjectKind, Dictionary<string, FileDataEntry>> analysis)
    {
        var vramByKind = new Dictionary<ObjectKind, long>();
        var trisByKind = new Dictionary<ObjectKind, long>();

        foreach (var (kind, filesByHash) in analysis)
        {
            long vram = filesByHash.Values
                .Where(f => string.Equals(f.FileType, "tex", StringComparison.Ordinal))
                .Sum(f => f.OriginalSize);

            long tris = filesByHash.Values
                .Where(f => string.Equals(f.FileType, "mdl", StringComparison.Ordinal))
                .Sum(f => f.Triangles);

            vramByKind[kind] = vram;
            trisByKind[kind] = tris;
        }

        return new CharacterAnalysisMetrics
        {
            TotalVramBytes = vramByKind.Values.Sum(),
            TotalTriangles = trisByKind.Values.Sum(),
            VramBytesByKind = vramByKind,
            TrianglesByKind = trisByKind
        };
    }

    public sealed class CharacterAnalysisMetrics
    {
        public long TotalVramBytes { get; init; }
        public long TotalTriangles { get; init; }
        public Dictionary<ObjectKind, long> VramBytesByKind { get; init; } = new();
        public Dictionary<ObjectKind, long> TrianglesByKind { get; init; } = new();
    }

}
