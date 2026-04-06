using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Diagnostics.CodeAnalysis;
using VfxEditor.TmbFormat;
using VfxEditor.TmbFormat.Entries;

namespace PlayerSync.Validation.Tmb;

public static class TmbValidation
{
    /// <summary>
    /// The sequence of starting bytes that identify a TMB file.
    /// </summary>
    public const uint TmbMagic = 0x424C4D54;

    public static readonly ValidationMessage TMB000A = new ValidationMessage(nameof(TMB000A), "TMB file invalid", "The TMB file could not be loaded.", MessageLevel.Crash);
    public static readonly ValidationMessage TMB000B = new ValidationMessage(nameof(TMB000B), "TMB verification failed", "The TMB file was not verified.", MessageLevel.Warning);

    public static readonly ValidationMessage TMB002A = new ValidationMessage(nameof(TMB002A), "TMB C002 path empty", "The TMB file has a TMB entry (C002) whose Path is empty.", MessageLevel.Crash);
    public static readonly ValidationMessage TMB002B = new ValidationMessage(nameof(TMB002B), "TMB C002 path invalid", "The TMB file has a TMB entry (C002) whose Path is invalid.", MessageLevel.Crash);
    public static readonly ValidationMessage TMB002C = new ValidationMessage(nameof(TMB002C), "TMB C002 path missing", "The TMB file has a TMB entry (C002) whose Path refers to a resource that cannot be found.", MessageLevel.Warning);

    public static readonly ValidationMessage TMB009A = new ValidationMessage(nameof(TMB009A), "TMB C009 path empty", "The TMB file has an Animation (PAP) entry (C009) whose Path is empty.", MessageLevel.Warning);
    public static readonly ValidationMessage TMB009B = new ValidationMessage(nameof(TMB009B), "TMB C009 path invalid", "The TMB file has an Animation (PAP) entry (C009) whose Path is invalid.", MessageLevel.Warning);

    public static readonly ValidationMessage TMB010A = new ValidationMessage(nameof(TMB010A), "TMB C010 path empty", "The TMB file has an Animation entry (C010) whose Path is empty.", MessageLevel.Warning);
    public static readonly ValidationMessage TMB010B = new ValidationMessage(nameof(TMB010B), "TMB C010 path invalid", "The TMB file has an Animation entry (C010) whose Path is invalid.", MessageLevel.Warning);

    public static readonly ValidationMessage TMB012A = new ValidationMessage(nameof(TMB012A), "TMB C012 path empty", "The TMB file has a VFX entry (C012) whose Path is empty.", MessageLevel.Crash);
    public static readonly ValidationMessage TMB012B = new ValidationMessage(nameof(TMB012B), "TMB C012 path invalid", "The TMB file has a VFX entry (C012) whose Path is invalid.", MessageLevel.Crash);
    public static readonly ValidationMessage TMB012C = new ValidationMessage(nameof(TMB012C), "TMB C012 path missing", "The TMB file has a VFX entry (C012) whose Path refers to a resource that cannot be found.", MessageLevel.Warning);

    public static readonly ValidationMessage TMB063A = new ValidationMessage(nameof(TMB063A), "TMB C063 path empty", "The TMB file has an Audio entry (C063) whose Path is empty.", MessageLevel.Warning);
    public static readonly ValidationMessage TMB063B = new ValidationMessage(nameof(TMB063B), "TMB C063 path invalid", "The TMB file has an Audio entry (C063) whose path is invalid.", MessageLevel.Crash);
    public static readonly ValidationMessage TMB063C = new ValidationMessage(nameof(TMB063C), "TMB C063 path missing", "The TMB file has an Audio entry (C063) whose Path refers to a resource that cannot be found.", MessageLevel.Warning);

    public static readonly ValidationMessage TMB173A = new ValidationMessage(nameof(TMB173A), "TMB C173 path empty", "The TMB file has an Async VFX entry (C173) whose Path is empty.", MessageLevel.Crash);
    public static readonly ValidationMessage TMB173B = new ValidationMessage(nameof(TMB173B), "TMB C173 path invalid", "The TMB file has an Async VFX entry (C173) whose Path is invalid.", MessageLevel.Crash);
    public static readonly ValidationMessage TMB173C = new ValidationMessage(nameof(TMB173C), "TMB C173 path missing", "The TMB file has an Async VFX entry (C173) whose Path refers to a resource that cannot be found.", MessageLevel.Warning);

    public static IEnumerable<ValidationMessage> ValidateTmbFile(ExcelModule excelModule, byte[] fileData, Func<string, bool>? validatePath)
    {
        using var stream = new MemoryStream(fileData);
        using var reader = new BinaryReader(stream);

        TmbFile? tmbFile = null;
        try
        {
            tmbFile = new TmbFile(reader, verify: false);
        }
        catch (Exception) // Can throw if e.g. the VfxEditor.Dalamud static properties are accessed when not initialized (e.g. from a unit test).
        { } // Can't yield return inside a catch

        if (tmbFile == null)
        {
            yield return TMB000A;
            yield break;
        }

        //if (tmbFile.Verified != VfxEditor.Utils.VerifiedStatus.VERIFIED)
        //{
        //    yield return TMB000B;
        //    yield break;
        //}

        foreach (var actor in tmbFile.Actors)
        {
            foreach (var track in actor.Tracks)
            {
                foreach (var entry in track.Entries)
                {
                    if (entry is C002) // TMB
                    {
                        // Path is full game path minus the .tmb e.g. chara/action/battle/magic_swl_end in chara/action/human_sp/c0101/human_sp233.tmb
                        var path = GetOffsetStringValue(entry, "Path");

                        if (string.IsNullOrEmpty(path))
                        {
                            yield return TMB002A;
                        }
                        else if (!ValidationUtils.IsPathParseable(path, validateExtension: false))
                        {
                            yield return TMB002B;
                        }
                        else if (validatePath != null && !validatePath.Invoke($"{path}.tmb"))
                        {
                            yield return TMB002C;
                        }
                    }
                    else if (entry is C009) // Animation (PAP Only)
                    {
                        // Path is hypothesized to be e.g. cbem_sp12_2lp as found in MotionTimeline or MotionTimelineAdvanceBlend
                        var path = GetOffsetStringValue(entry, "Path");

                        if (string.IsNullOrEmpty(path))
                        {
                            yield return TMB009A;
                        }
                        else if (!excelModule.GetSheet<MotionTimeline>().Any(timeline => timeline.Filename.ToString() == path)
                            && !excelModule.GetSheet<MotionTimelineAdvanceBlend>().Any(timeline => timeline.Unknown0.ToString() == path))
                        {
                            yield return TMB009B;
                        }
                    }
                    else if (entry is C010) // Animation
                    {
                        // Path is e.g. cbem_sp12_2lp as found in MotionTimeline or MotionTimelineAdvanceBlend
                        var path = GetOffsetStringValue(entry, "Path");

                        if (string.IsNullOrEmpty(path))
                        {
                            yield return TMB010A;
                        }
                        else if (!excelModule.GetSheet<MotionTimeline>().Any(timeline => timeline.Filename.ToString() == path)
                            && !excelModule.GetSheet<MotionTimelineAdvanceBlend>().Any(timeline => timeline.Unknown0.ToString() == path))
                        {
                            yield return TMB010B;
                        }
                    }
                    else if (entry is C012) // VFX 
                    {
                        // Path is full game path e.g. vfx/common/eff/pop_tlep1t0h.avfx
                        var path = GetOffsetStringValue(entry, "Path");

                        if (string.IsNullOrEmpty(path))
                        {
                            yield return TMB012A;
                        }
                        else if (!ValidationUtils.IsPathParseable(path, validateExtension: true))
                        {
                            yield return TMB012B;
                        }
                        else if (validatePath != null && !validatePath.Invoke(path))
                        {
                            yield return TMB012C;
                        }
                    }
                    else if (entry is C063) // Sound
                    {
                        // Path is a full game path, e.g. sound/vfx/monster/SE_Vfx_Monster_Bom_jibaku.scd in chara/action/mon_sp/m0026/mon_sp001.tmb
                        var path = GetOffsetStringValue(entry, "Path");

                        if (string.IsNullOrEmpty(path))
                        {
                            yield return TMB063A;
                        }
                        else if (!ValidationUtils.IsPathParseable(path, validateExtension: false))
                        {
                            yield return TMB063B;
                        }
                        else if (validatePath != null && !validatePath.Invoke(path))
                        {
                            yield return TMB063C;
                        }
                    }
                    else if (entry is C173) // Async VFX
                    {
                        // Path is probably the same as VFX, so a full game path?
                        var path = GetOffsetStringValue(entry, "Path");

                        if (string.IsNullOrEmpty(path))
                        {
                            yield return TMB173A;
                        }
                        else if (!ValidationUtils.IsPathParseable(path, validateExtension: true))
                        {
                            yield return TMB173B;
                        }
                        else if (validatePath != null && !validatePath.Invoke(path))
                        {
                            yield return TMB173C;
                        }
                    }
                }
            }
        }
    }

    private static string? GetOffsetStringValue(TmbEntry entry, string fieldName)
    {
        // Ugh sure wish I didn't have to do this. Why are all the properties of track entries private????
        var entryType = entry.GetType();
        var field = entryType.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var offsetString = (TmbOffsetString?)field?.GetValue(entry);
        return offsetString?.Value;
    }
}
