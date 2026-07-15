using System;
using System.Collections.Generic;
using System.Text;

namespace PlayerSync.Validation.Avfx;

public static class AvfxValidation
{
    public const uint AvfxMagic = 0x41564658; // 'XFVA'

    // Magic number
    public static readonly ValidationMessage AVFX000A = new ValidationMessage(nameof(AVFX000A), "AVFX wrong magic number", "The AVFX header has an incorrect magic number (first four bytes).", MessageLevel.Crash);

    // Length
    public static readonly ValidationMessage AVFX001A = new ValidationMessage(nameof(AVFX001A), "AVFX too small", "The AVFX header has a length greater than the remaining size of the file.", MessageLevel.Crash);
    public static readonly ValidationMessage AVFX001B = new ValidationMessage(nameof(AVFX001B), "AVFX extra data", "The AVFX file is longer than the header's declared size.", MessageLevel.Warning);

    // Texture paths
    //public static readonly ValidationMessage AVFX100A = new ValidationMessage(nameof(AVFX100A), "AVFX texture path empty", "The AVFX file has an empty texture path.", MessageLevel.Warning); // TODO: Verify message level
    public static readonly ValidationMessage AVFX100B = new ValidationMessage(nameof(AVFX100B), "AVFX texture path invalid", "The AVFX file has an invalid texture path.", MessageLevel.Warning); // TODO: Verify message level
    public static readonly ValidationMessage AVFX100C = new ValidationMessage(nameof(AVFX100C), "AVFX texture path missing", "The AVFX file has a missing texture path.", MessageLevel.Warning); // TODO: Verify message level
    public static readonly ValidationMessage AVFX100D = new ValidationMessage(nameof(AVFX100D), "AVFX texture path expansion missing", "The AVFX file has a texture path that references an expansion that is not installed.", MessageLevel.Crash);

    public static IEnumerable<ValidationMessage> ValidateAvfxFile(byte[] fileData, ulong installedExpansions, Func<string, bool>? validatePath)
    {
        using var stream = new MemoryStream(fileData);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadUInt32();
        if (magic != AvfxMagic)
        {
            yield return AVFX000A;
            yield break;
        }

        var totalSize = reader.ReadUInt32();
        if (totalSize < stream.Length - stream.Position)
        {
            yield return AVFX001B;
        }
        else if (totalSize > stream.Length - stream.Position)
        {
            yield return AVFX001A;
            yield break;
        }

        uint childrenRead = 0;
        while (childrenRead < totalSize)
        {
            var childId = reader.ReadUInt32();
            var childSize = reader.ReadUInt32();
            var padding = CalculatePadding(childSize);
            childrenRead += sizeof(uint) + sizeof(uint);
            var childEnd = stream.Position + childSize;

            if (childId == TexTagId)
            {
                foreach (var message in ValidateTexTag(reader, childSize, installedExpansions, validatePath))
                {
                    yield return message;
                }
            }

            childrenRead += childSize + padding;
            stream.Position = childEnd + padding;
        }
    }

    private const uint TexTagId = 0x00546578; // 'xeT\0'
    private static IEnumerable<ValidationMessage> ValidateTexTag(BinaryReader reader, uint size, ulong installedExpansions, Func<string, bool>? validatePath)
    {
        if (size > 1)
        {
            string gamePath = Encoding.ASCII.GetString(reader.ReadBytes((int)(size - 1))); // - 1: ignore the ending null

            if (!ValidationUtils.IsPathParseable(gamePath, validateExtension: true))
            {
                yield return AVFX100B;
            }
            else if (validatePath != null && !validatePath.Invoke(gamePath))
            {
                yield return AVFX100C;
            }
            else if (!ValidationUtils.IsExpansionInstalled(installedExpansions, ValidationUtils.ParseExpansionId(gamePath)))
            {
                yield return AVFX100D;
            }
        }
    }

    private static uint CalculatePadding(uint size) => size % 4 == 0 ? 0 : 4 - size % 4;
}
