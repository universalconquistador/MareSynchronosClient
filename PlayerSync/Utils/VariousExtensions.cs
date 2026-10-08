using Dalamud.Game.ClientState.Objects.Types;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.PlayerData.Pairs;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MareSynchronos.Utils;

public static class VariousExtensions
{
    public static string ToByteString(this int bytes, bool addSuffix = true)
    {
        string[] suffix = ["B", "KiB", "MiB", "GiB", "TiB"];
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return addSuffix ? $"{dblSByte:0.00} {suffix[i]}" : $"{dblSByte:0.00}";
    }

    public static string ToByteString(this long bytes, bool addSuffix = true)
    {
        string[] suffix = ["B", "KiB", "MiB", "GiB", "TiB"];
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return addSuffix ? $"{dblSByte:0.00} {suffix[i]}" : $"{dblSByte:0.00}";
    }

    public static void CancelDispose(this CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
            cts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // swallow it
        }
    }

    public static CancellationTokenSource CancelRecreate(this CancellationTokenSource? cts)
    {
        cts?.CancelDispose();
        return new CancellationTokenSource();
    }

    private static bool TryGetMtrlTexHashChanges(IReadOnlyList<FileReplacementData> oldRepls, IReadOnlyList<FileReplacementData> newRepls, out List<(string Path, string OldHash, string NewHash)> changed)
    {
        static Dictionary<string, string> BuildMap(IReadOnlyList<FileReplacementData> repls)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in repls)
            {
                var hash = r.Hash ?? string.Empty;
                foreach (var p in r.GamePaths)
                {
                    if (p.EndsWith(".mtrl", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
                    {
                        // we don't care if there are multiple of the same path
                        map[p] = hash;
                    }
                }
            }

            return map;
        }

        var oldMap = BuildMap(oldRepls);
        var newMap = BuildMap(newRepls);
        changed = new List<(string, string, string)>();

        foreach (var (path, oldHash) in oldMap)
        {
            if (!newMap.TryGetValue(path, out var newHash))
                continue;

            if (!string.Equals(oldHash, newHash, StringComparison.OrdinalIgnoreCase))
                changed.Add((path, oldHash, newHash));
        }

        return changed.Count > 0;
    }

    //public static Dictionary<ObjectKind, HashSet<PlayerChanges>> CheckUpdatedData(this CharacterData newData, Guid applicationBase,
    //    CharacterData? oldData, ILogger logger, PairHandler cachedPlayer, bool forceApplyCustomization, bool forceApplyMods)
    //{
    //    oldData ??= new();
    //    var charaDataToUpdate = new Dictionary<ObjectKind, HashSet<PlayerChanges>>();
    //    foreach (ObjectKind objectKind in Enum.GetValues<ObjectKind>())
    //    {
    //        charaDataToUpdate[objectKind] = [];
    //        oldData.FileReplacements.TryGetValue(objectKind, out var existingFileReplacements);
    //        newData.FileReplacements.TryGetValue(objectKind, out var newFileReplacements);
    //        oldData.GlamourerData.TryGetValue(objectKind, out var existingGlamourerData);
    //        newData.GlamourerData.TryGetValue(objectKind, out var newGlamourerData);

    //        bool hasNewButNotOldFileReplacements = newFileReplacements != null && existingFileReplacements == null;
    //        bool hasOldButNotNewFileReplacements = existingFileReplacements != null && newFileReplacements == null;

    //        bool hasNewButNotOldGlamourerData = newGlamourerData != null && existingGlamourerData == null;
    //        bool hasOldButNotNewGlamourerData = existingGlamourerData != null && newGlamourerData == null;

    //        bool hasNewAndOldFileReplacements = newFileReplacements != null && existingFileReplacements != null;
    //        bool hasNewAndOldGlamourerData = newGlamourerData != null && existingGlamourerData != null;

    //        if (hasNewButNotOldFileReplacements || hasOldButNotNewFileReplacements || hasNewButNotOldGlamourerData || hasOldButNotNewGlamourerData)
    //        {
    //            logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Some new data arrived: NewButNotOldFiles:{hasNewButNotOldFileReplacements}," +
    //                " OldButNotNewFiles:{hasOldButNotNewFileReplacements}, NewButNotOldGlam:{hasNewButNotOldGlamourerData}, OldButNotNewGlam:{hasOldButNotNewGlamourerData}) => {change}, {change2}",
    //                applicationBase,
    //                cachedPlayer, objectKind, hasNewButNotOldFileReplacements, hasOldButNotNewFileReplacements, hasNewButNotOldGlamourerData, hasOldButNotNewGlamourerData, PlayerChanges.ModFiles, PlayerChanges.Glamourer);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.ModFiles);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.Glamourer);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
    //        }
    //        else
    //        {
    //            if (hasNewAndOldFileReplacements)
    //            {
    //                bool listsAreEqual = oldData.FileReplacements[objectKind].SequenceEqual(newData.FileReplacements[objectKind], PlayerData.Data.FileReplacementDataComparer.Instance);
    //                if (!listsAreEqual || forceApplyMods)
    //                {
    //                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (FileReplacements not equal) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.ModFiles);
    //                    charaDataToUpdate[objectKind].Add(PlayerChanges.ModFiles);
    //                    if (forceApplyMods || objectKind != ObjectKind.Player)
    //                    {
    //                        charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
    //                    }
    //                    else
    //                    {
    //                        var existingFace = existingFileReplacements.Where(g => g.GamePaths.Any(p => p.Contains("/face/", StringComparison.OrdinalIgnoreCase)))
    //                            .OrderBy(g => string.IsNullOrEmpty(g.Hash) ? g.FileSwapPath : g.Hash, StringComparer.OrdinalIgnoreCase).ToList();
    //                        var existingHair = existingFileReplacements.Where(g => g.GamePaths.Any(p => p.Contains("/hair/", StringComparison.OrdinalIgnoreCase)))
    //                            .OrderBy(g => string.IsNullOrEmpty(g.Hash) ? g.FileSwapPath : g.Hash, StringComparer.OrdinalIgnoreCase).ToList();
    //                        var existingTail = existingFileReplacements.Where(g => g.GamePaths.Any(p => p.Contains("/tail/", StringComparison.OrdinalIgnoreCase)))
    //                            .OrderBy(g => string.IsNullOrEmpty(g.Hash) ? g.FileSwapPath : g.Hash, StringComparer.OrdinalIgnoreCase).ToList();
    //                        var newFace = newFileReplacements.Where(g => g.GamePaths.Any(p => p.Contains("/face/", StringComparison.OrdinalIgnoreCase)))
    //                            .OrderBy(g => string.IsNullOrEmpty(g.Hash) ? g.FileSwapPath : g.Hash, StringComparer.OrdinalIgnoreCase).ToList();
    //                        var newHair = newFileReplacements.Where(g => g.GamePaths.Any(p => p.Contains("/hair/", StringComparison.OrdinalIgnoreCase)))
    //                            .OrderBy(g => string.IsNullOrEmpty(g.Hash) ? g.FileSwapPath : g.Hash, StringComparer.OrdinalIgnoreCase).ToList();
    //                        var newTail = newFileReplacements.Where(g => g.GamePaths.Any(p => p.Contains("/tail/", StringComparison.OrdinalIgnoreCase)))
    //                            .OrderBy(g => string.IsNullOrEmpty(g.Hash) ? g.FileSwapPath : g.Hash, StringComparer.OrdinalIgnoreCase).ToList();
    //                        var existingTransients = existingFileReplacements.Where(g => g.GamePaths.Any(g => !g.EndsWith("mdl") && !g.EndsWith("tex") && !g.EndsWith("mtrl")))
    //                            .OrderBy(g => string.IsNullOrEmpty(g.Hash) ? g.FileSwapPath : g.Hash, StringComparer.OrdinalIgnoreCase).ToList();
    //                        var newTransients = newFileReplacements.Where(g => g.GamePaths.Any(g => !g.EndsWith("mdl") && !g.EndsWith("tex") && !g.EndsWith("mtrl")))
    //                            .OrderBy(g => string.IsNullOrEmpty(g.Hash) ? g.FileSwapPath : g.Hash, StringComparer.OrdinalIgnoreCase).ToList();

    //                        logger.LogTrace("[BASE-{appbase}] ExistingFace: {of}, NewFace: {fc}; ExistingHair: {eh}, NewHair: {nh}; ExistingTail: {et}, NewTail: {nt}; ExistingTransient: {etr}, NewTransient: {ntr}", applicationBase,
    //                            existingFace.Count, newFace.Count, existingHair.Count, newHair.Count, existingTail.Count, newTail.Count, existingTransients.Count, newTransients.Count);

    //                        var differentFace = !existingFace.SequenceEqual(newFace, PlayerData.Data.FileReplacementDataComparer.Instance);
    //                        var differentHair = !existingHair.SequenceEqual(newHair, PlayerData.Data.FileReplacementDataComparer.Instance);
    //                        var differentTail = !existingTail.SequenceEqual(newTail, PlayerData.Data.FileReplacementDataComparer.Instance);
    //                        var differenTransients = !existingTransients.SequenceEqual(newTransients, PlayerData.Data.FileReplacementDataComparer.Instance);
    //                        if (differentFace || differentHair || differentTail || differenTransients)
    //                        {
    //                            logger.LogDebug("[BASE-{appbase}] Different Subparts: Face: {face}, Hair: {hair}, Tail: {tail}, Transients: {transients} => {change}", applicationBase,
    //                                differentFace, differentHair, differentTail, differenTransients, PlayerChanges.ForcedRedraw);
    //                            charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
    //                        }
    //                    }

    //                    try
    //                    {
    //                        if (TryGetMtrlTexHashChanges(existingFileReplacements, newFileReplacements, out var changed))
    //                        {
    //                            logger.LogDebug("[BASE-{appBase}] {object}/{kind} .mtrl/.tex hash changes={Count}. Changes: {Changes}", applicationBase,
    //                                cachedPlayer, objectKind, changed.Count, string.Join(", ", changed.Select(c => $"{c.Path} {c.OldHash} -> {c.NewHash}"))
    //                            );
    //                            if (changed.Count > 0)
    //                            {
    //                                charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
    //                            }
    //                        }
    //                    }
    //                    catch (Exception ex)
    //                    {
    //                        logger.LogDebug("[BASE-{appBase}] {object}/{kind} .mtrl/.tex failed to calculate changes", applicationBase, cachedPlayer, objectKind);
    //                    }
    //                }
    //            }

    //            if (hasNewAndOldGlamourerData)
    //            {
    //                bool glamourerDataDifferent = !string.Equals(oldData.GlamourerData[objectKind], newData.GlamourerData[objectKind], StringComparison.Ordinal);
    //                if (glamourerDataDifferent || forceApplyCustomization)
    //                {
    //                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Glamourer different) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Glamourer);
    //                    charaDataToUpdate[objectKind].Add(PlayerChanges.Glamourer);
    //                }
    //            }
    //        }

    //        oldData.CustomizePlusData.TryGetValue(objectKind, out var oldCustomizePlusData);
    //        newData.CustomizePlusData.TryGetValue(objectKind, out var newCustomizePlusData);

    //        oldCustomizePlusData ??= string.Empty;
    //        newCustomizePlusData ??= string.Empty;

    //        bool customizeDataDifferent = !string.Equals(oldCustomizePlusData, newCustomizePlusData, StringComparison.Ordinal);
    //        if (customizeDataDifferent || (forceApplyCustomization && !string.IsNullOrEmpty(newCustomizePlusData)))
    //        {
    //            logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff customize data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Customize);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.Customize);
    //        }

    //        if (objectKind != ObjectKind.Player) continue;

    //        bool manipDataDifferent = !string.Equals(oldData.ManipulationData, newData.ManipulationData, StringComparison.Ordinal);
    //        if (manipDataDifferent || forceApplyMods)
    //        {
    //            logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff manip data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.ModManip);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.ModManip);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.ForcedRedraw);
    //        }

    //        bool heelsOffsetDifferent = !string.Equals(oldData.HeelsData, newData.HeelsData, StringComparison.Ordinal);
    //        if (heelsOffsetDifferent || (forceApplyCustomization && !string.IsNullOrEmpty(newData.HeelsData)))
    //        {
    //            logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff heels data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Heels);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.Heels);
    //        }

    //        bool honorificDataDifferent = !string.Equals(oldData.HonorificData, newData.HonorificData, StringComparison.Ordinal);
    //        if (honorificDataDifferent || (forceApplyCustomization && !string.IsNullOrEmpty(newData.HonorificData)))
    //        {
    //            logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff honorific data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Honorific);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.Honorific);
    //        }

    //        bool moodlesDataDifferent = !string.Equals(oldData.MoodlesData, newData.MoodlesData, StringComparison.Ordinal);
    //        if (moodlesDataDifferent || (forceApplyCustomization && !string.IsNullOrEmpty(newData.MoodlesData)))
    //        {
    //            logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff moodles data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Moodles);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.Moodles);
    //        }

    //        var oldLociData = oldData.LociData.GetValueOrDefault(objectKind, string.Empty);
    //        var newLociData = newData.LociData.GetValueOrDefault(objectKind, string.Empty);

    //        bool lociDataDifferent = !string.Equals(oldLociData, newLociData, StringComparison.Ordinal);
    //        if (lociDataDifferent || (forceApplyCustomization && !string.IsNullOrEmpty(newLociData)))
    //        {
    //            logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff loci data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Loci);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.Loci);
    //        }

    //        bool petNamesDataDifferent = !string.Equals(oldData.PetNamesData, newData.PetNamesData, StringComparison.Ordinal);
    //        if (petNamesDataDifferent || (forceApplyCustomization && !string.IsNullOrEmpty(newData.PetNamesData)))
    //        {
    //            logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff petnames data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.PetNames);
    //            charaDataToUpdate[objectKind].Add(PlayerChanges.PetNames);
    //        }
    //    }

    //    foreach (KeyValuePair<ObjectKind, HashSet<PlayerChanges>> data in charaDataToUpdate.ToList())
    //    {
    //        if (!data.Value.Any()) charaDataToUpdate.Remove(data.Key);
    //        else charaDataToUpdate[data.Key] = [.. data.Value.OrderByDescending(p => (int)p)];
    //    }

    //    return charaDataToUpdate;
    //}

    public static Dictionary<ObjectKind, HashSet<PlayerChanges>> CheckUpdatedData(this CharacterData newData, Guid applicationBase,
        CharacterData? oldData, ILogger logger, PairHandler cachedPlayer, bool forceApplyCustomization, bool forceApplyMods)
    {
        oldData ??= new CharacterData();

        var objectKinds = Enum.GetValues<ObjectKind>();
        var charaDataToUpdate = new Dictionary<ObjectKind, HashSet<PlayerChanges>>(objectKinds.Length);
        var comparer = PlayerData.Data.FileReplacementDataComparer.Instance;

        foreach (var objectKind in objectKinds)
        {
            var changes = new HashSet<PlayerChanges>();

            oldData.FileReplacements.TryGetValue(objectKind, out var existingFileReplacements);
            newData.FileReplacements.TryGetValue(objectKind, out var newFileReplacements);
            oldData.GlamourerData.TryGetValue(objectKind, out var existingGlamourerData);
            newData.GlamourerData.TryGetValue(objectKind, out var newGlamourerData);

            var hasNewButNotOldFileReplacements = newFileReplacements != null && existingFileReplacements == null;
            var hasOldButNotNewFileReplacements = existingFileReplacements != null && newFileReplacements == null;
            var hasNewButNotOldGlamourerData = newGlamourerData != null && existingGlamourerData == null;
            var hasOldButNotNewGlamourerData = existingGlamourerData != null && newGlamourerData == null;

            if (hasNewButNotOldFileReplacements || hasOldButNotNewFileReplacements || hasNewButNotOldGlamourerData || hasOldButNotNewGlamourerData)
            {
                logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Some new data arrived: NewButNotOldFiles:{hasNewButNotOldFileReplacements},"
                        + " OldButNotNewFiles:{hasOldButNotNewFileReplacements}, NewButNotOldGlam:{hasNewButNotOldGlamourerData}, OldButNotNewGlam:{hasOldButNotNewGlamourerData}) => {change}, {change2}",
                        applicationBase, cachedPlayer, objectKind, hasNewButNotOldFileReplacements, hasOldButNotNewFileReplacements,
                        hasNewButNotOldGlamourerData, hasOldButNotNewGlamourerData, PlayerChanges.ModFiles, PlayerChanges.Glamourer);

                changes.Add(PlayerChanges.ModFiles);
                changes.Add(PlayerChanges.Glamourer);
                changes.Add(PlayerChanges.ForcedRedraw);
            }
            else
            {
                if (existingFileReplacements != null && newFileReplacements != null && (forceApplyMods || !existingFileReplacements.SequenceEqual(newFileReplacements, comparer)))
                {
                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (FileReplacements not equal) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.ModFiles);

                    changes.Add(PlayerChanges.ModFiles);

                    if (forceApplyMods || objectKind != ObjectKind.Player)
                    {
                        changes.Add(PlayerChanges.ForcedRedraw);
                    }
                    else
                    {
                        var (existingFace, existingHair, existingTail, existingTransients) = GetRedrawSubparts(existingFileReplacements);
                        var (newFace, newHair, newTail, newTransients) = GetRedrawSubparts(newFileReplacements);

                        logger.LogTrace("[BASE-{appbase}] ExistingFace: {of}, NewFace: {fc}; ExistingHair: {eh}, NewHair: {nh}; ExistingTail: {et}, NewTail: {nt}; ExistingTransient: {etr}, NewTransient: {ntr}",
                                applicationBase, existingFace.Count, newFace.Count, existingHair.Count, newHair.Count, existingTail.Count, newTail.Count, existingTransients.Count, newTransients.Count);

                        var differentFace = !existingFace.SequenceEqual(newFace, comparer);
                        var differentHair = !existingHair.SequenceEqual(newHair, comparer);
                        var differentTail = !existingTail.SequenceEqual(newTail, comparer);
                        var differentTransients = !existingTransients.SequenceEqual(newTransients, comparer);

                        if (differentFace || differentHair || differentTail || differentTransients)
                        {
                            logger.LogDebug("[BASE-{appbase}] Different Subparts: Face: {face}, Hair: {hair}, Tail: {tail}, Transients: {transients} => {change}",
                                    applicationBase, differentFace, differentHair, differentTail, differentTransients, PlayerChanges.ForcedRedraw);

                            changes.Add(PlayerChanges.ForcedRedraw);
                        }
                    }

                    try
                    {
                        if (TryGetMtrlTexHashChanges(existingFileReplacements, newFileReplacements, out var changed))
                        {
                            logger.LogDebug("[BASE-{appBase}] {object}/{kind} .mtrl/.tex hash changes={Count}. Changes: {Changes}",
                                    applicationBase, cachedPlayer, objectKind, changed.Count, string.Join(", ", changed.Select(c => $"{c.Path} {c.OldHash} -> {c.NewHash}")));

                            if (changed.Count > 0)
                            {
                                changes.Add(PlayerChanges.ForcedRedraw);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        logger.LogDebug("[BASE-{appBase}] {object}/{kind} .mtrl/.tex failed to calculate changes", applicationBase, cachedPlayer, objectKind);
                    }
                }

                if (existingGlamourerData != null && newGlamourerData != null && (!string.Equals(existingGlamourerData, newGlamourerData, StringComparison.Ordinal) || forceApplyCustomization))
                {
                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Glamourer different) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Glamourer);

                    changes.Add(PlayerChanges.Glamourer);
                }
            }

            oldData.CustomizePlusData.TryGetValue(objectKind, out var oldCustomizePlusData);
            newData.CustomizePlusData.TryGetValue(objectKind, out var newCustomizePlusData);
            oldCustomizePlusData ??= string.Empty;
            newCustomizePlusData ??= string.Empty;

            if (!string.Equals(oldCustomizePlusData, newCustomizePlusData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newCustomizePlusData)))
            {
                logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff customize data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Customize);

                changes.Add(PlayerChanges.Customize);
            }

            if (objectKind == ObjectKind.Player)
            {
                if (!string.Equals(oldData.ManipulationData, newData.ManipulationData, StringComparison.Ordinal) || forceApplyMods)
                {
                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff manip data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.ModManip);

                    changes.Add(PlayerChanges.ModManip);
                    changes.Add(PlayerChanges.ForcedRedraw);
                }

                if (!string.Equals(oldData.HeelsData, newData.HeelsData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newData.HeelsData)))
                {
                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff heels data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Heels);

                    changes.Add(PlayerChanges.Heels);
                }

                if (!string.Equals(oldData.HonorificData, newData.HonorificData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newData.HonorificData)))
                {
                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff honorific data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Honorific);

                    changes.Add(PlayerChanges.Honorific);
                }

                if (!string.Equals(oldData.MoodlesData, newData.MoodlesData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newData.MoodlesData)))
                {
                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff moodles data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Moodles);

                    changes.Add(PlayerChanges.Moodles);
                }

                var oldLociData = oldData.LociData.GetValueOrDefault(objectKind, string.Empty);
                var newLociData = newData.LociData.GetValueOrDefault(objectKind, string.Empty);

                if (!string.Equals(oldLociData, newLociData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newLociData)))
                {
                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff loci data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.Loci);

                    changes.Add(PlayerChanges.Loci);
                }

                if (!string.Equals(oldData.PetNamesData, newData.PetNamesData, StringComparison.Ordinal) || (forceApplyCustomization && !string.IsNullOrEmpty(newData.PetNamesData)))
                {
                    logger.LogDebug("[BASE-{appBase}] Updating {object}/{kind} (Diff petnames data) => {change}", applicationBase, cachedPlayer, objectKind, PlayerChanges.PetNames);

                    changes.Add(PlayerChanges.PetNames);
                }
            }

            if (changes.Count > 0)
            {
                charaDataToUpdate.Add(objectKind, changes.OrderByDescending(p => (int)p).ToHashSet());
            }
        }

        return charaDataToUpdate;

        static (List<FileReplacementData> Face, List<FileReplacementData> Hair, List<FileReplacementData> Tail, List<FileReplacementData> Transients) GetRedrawSubparts(List<FileReplacementData> replacements)
        {
            var replacementParts = new List<(FileReplacementData Replacement, bool Face, bool Hair, bool Tail, bool Transients)>();

            foreach (var replacement in replacements)
            {
                bool hasFace = false;
                bool hasHair = false;
                bool hasTail = false;
                bool hasTransients = false;

                foreach (var path in replacement.GamePaths)
                {
                    if (!hasFace && path.Contains("/face/", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFace = true;
                    }

                    if (!hasHair && path.Contains("/hair/", StringComparison.OrdinalIgnoreCase))
                    {
                        hasHair = true;
                    }

                    if (!hasTail && path.Contains("/tail/", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTail = true;
                    }

                    if (!hasTransients && !path.EndsWith("mdl") && !path.EndsWith("tex") && !path.EndsWith("mtrl"))
                    {
                        hasTransients = true;
                    }

                    if (hasFace && hasHair && hasTail && hasTransients)
                    {
                        break;
                    }
                }

                if (hasFace || hasHair || hasTail || hasTransients)
                {
                    replacementParts.Add((replacement, hasFace, hasHair, hasTail, hasTransients));
                }
            }

            var face = new List<FileReplacementData>();
            var hair = new List<FileReplacementData>();
            var tail = new List<FileReplacementData>();
            var transients = new List<FileReplacementData>();

            foreach (var replacement in replacementParts.OrderBy(static c => string.IsNullOrEmpty(c.Replacement.Hash) ? c.Replacement.FileSwapPath : c.Replacement.Hash, StringComparer.OrdinalIgnoreCase))
            {
                if (replacement.Face)
                {
                    face.Add(replacement.Replacement);
                }

                if (replacement.Hair)
                {
                    hair.Add(replacement.Replacement);
                }

                if (replacement.Tail)
                {
                    tail.Add(replacement.Replacement);
                }

                if (replacement.Transients)
                {
                    transients.Add(replacement.Replacement);
                }
            }

            return (face, hair, tail, transients);
        }
    }

    public static T DeepClone<T>(this T obj)
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj))!;
    }

    public static unsafe int? ObjectTableIndex(this IGameObject? gameObject)
    {
        if (gameObject == null || gameObject.Address == IntPtr.Zero)
        {
            return null;
        }

        return ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)gameObject.Address)->ObjectIndex;
    }
}