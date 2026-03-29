using System.Diagnostics.CodeAnalysis;
using VfxEditor.TmbFormat;
using VfxEditor.TmbFormat.Entries;

namespace PlayerSync.Validation.Tmb;

public static class TmbValidation
{
    public const uint Magic = 0x424C4D54;

    public static ValidationFailure TMB001 = new ValidationFailure(nameof(TMB001), "TMB path empty", "The TMB file refers to an empty game path.");
    public static ValidationFailure TMB002 = new ValidationFailure(nameof(TMB002), "TMB path wrong", "The TMB file refers to a game path that does not exist.");

    public static bool IsInvalidFile(byte[] fileData, Func<string, bool>? validatePath, [NotNullWhen(true)] out ValidationFailure? failure)
    {
        using var stream = new MemoryStream(fileData);
        using var reader = new BinaryReader(stream);

        var tmbFile = new TmbFile(reader, verify: true);

        Console.WriteLine(tmbFile.Verified);

        foreach (var actor in tmbFile.Actors)
        {
            foreach (var track in actor.Tracks)
            {
                foreach (var entry in track.Entries)
                {
                    if (entry is C002
                        || entry is C009
                        || entry is C010
                        || entry is C012
                        || entry is C063
                        || entry is C173)
                    {
                        var path = GetOffsetStringValue(entry, "Path");

                        if (string.IsNullOrEmpty(path))
                        {
                            failure = TMB001;
                            return true;
                        }

                        // TODO: The path is not supposed to be a full game path, it's just a segment like "cbem_sp12_2lp". How do we turn this into a full game path to validate?
                        // Verify with MotionTimeline sheet
                        if (validatePath != null && !validatePath.Invoke(path))
                        {
                            failure = TMB002;
                            return true;
                        }
                    }
                }
            }
        }

        failure = null;
        return false;
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
