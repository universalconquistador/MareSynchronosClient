using System;
using System.Collections.Generic;
using System.Text;

namespace PlayerSync.Validation.Avfx;

public static class AvfxValidation
{
    public const uint AvfxMagic = 0x41564658;

    public static readonly ValidationMessage AVFX001A = new ValidationMessage(nameof(AVFX001A), "AVFX texture path empty", "The AVFX file has an empty texture path.", MessageLevel.Warning); // TODO: Verify message level
    public static readonly ValidationMessage AVFX001B = new ValidationMessage(nameof(AVFX001B), "AVFX texture path invalid", "The AVFX file has an invalid texture path.", MessageLevel.Warning); // TODO: Verify message level
    public static readonly ValidationMessage AVFX001C = new ValidationMessage(nameof(AVFX001C), "AVFX texture path missing", "The AVFX file has a missing texture path.", MessageLevel.Warning); // TODO: Verify message level
    public static readonly ValidationMessage AVFX001D = new ValidationMessage(nameof(AVFX001D), "AVFX texture path expansion missing", "The AVFX file has a texture path that references an expansion that is not installed.", MessageLevel.Crash);

    public static IEnumerable<ValidationMessage> ValidateAvfxFile(ulong installedExpansions, byte[] fileData, Func<string, bool>? validatePath)
    {
        using var stream = new MemoryStream(fileData);
        using var reader = new BinaryReader(stream);

        // TODO: Implement!
        yield break;
    }
}
