using System.Runtime.InteropServices;

namespace PlayerSync.Validation.Pap;


// A lot of this is reimplemented from VFXEditor but without all the parts we don't need
// https://github.com/0ceal0t/Dalamud-VFXEditor/blob/main/VFXEditor/Formats/PapFormat/PapFile.cs
//

public static class PapValidation
{
    public const uint PapMagic = 0x20706170; // pap

    public static readonly ValidationMessage PAP000A = new ValidationMessage(nameof(PAP000A), "PAP file invalid", "The PAP container or an embedded timeline could not be read.", MessageLevel.Warning);
    public static readonly ValidationMessage PAP000B = new ValidationMessage(nameof(PAP000B), "PAP version unsupported", "This PAP version cannot be checked by the validation.", MessageLevel.Warning);
    public static readonly ValidationMessage PAP198A = new ValidationMessage(nameof(PAP198A), "PAP C198 invalid", "The PAP contains a C198 entry whose SummonId value is not valid.", MessageLevel.Crash);
    public static readonly ValidationMessage PAP198B = new ValidationMessage(nameof(PAP198B), "PAP C198 undefined", "The PAP contains a C198 entry whose SummonId value is not valid.", MessageLevel.Warning);

    // We're parsing the pap to get to the embedded tmb to parse the tmb for the SummonId
    // https://github.com/0ceal0t/Dalamud-VFXEditor/blob/main/VFXEditor/Formats/TmbFormat/Entries/C198.cs

    private const uint SupportedVersion = 0x00020001;
    private const uint TmbMagic = 0x424C4D54; // tmb
    private const uint C198Magic = 0x38393143; // C198
    private const int HeaderSize = 0x1A;
    private const int AnimationSize = 0x28;
    private const int TmbHeaderSize = 0x0C;
    private const int RecordHeaderSize = 8;
    private const int C198Size = 0x28;
    private const int SummonIdOffset = 0x1C;

    public static IEnumerable<ValidationMessage> ValidatePapFile(byte[] fileData)
    {
        if (fileData.Length >= sizeof(uint))
        {
            var magic = MemoryMarshal.Read<uint>(fileData);
            if (magic != PapMagic)
            {
                yield return PAP000A;
                yield break;
            }
        }
        else
        {
            yield return PAP000A;
            yield break;
        }

        if (fileData.Length < HeaderSize)
        {
            yield return PAP000A;
            yield break;
        }

        using var stream = new MemoryStream(fileData);
        using var reader = new BinaryReader(stream);

        stream.Position = sizeof(uint);
        var version = reader.ReadUInt32();
        if (version != SupportedVersion)
        {
            yield return PAP000B;
            yield break;
        }

        var animationCount = reader.ReadInt16();
        stream.Position = 0x0E;
        var infoOffset = reader.ReadInt32();
        var havokOffset = reader.ReadInt32();
        var timelineOffset = reader.ReadInt32();
        if (animationCount < 0 || infoOffset < HeaderSize || havokOffset < infoOffset || timelineOffset < havokOffset 
            || timelineOffset > fileData.Length || (long)infoOffset + (long)AnimationSize * animationCount > havokOffset)
        {
            yield return PAP000A;
            yield break;
        }

        int nextTimeline = timelineOffset;
        for (int animationIndex = 0; animationIndex < animationCount; animationIndex++)
        {
            int start = nextTimeline;
            if (start > fileData.Length - TmbHeaderSize)
            {
                yield return PAP000A;
                yield break;
            }
            stream.Position = start;
            var tmbMagic = reader.ReadUInt32();
            if (tmbMagic != TmbMagic)
            {
                yield return PAP000A;
                yield break;
            }

            var size = reader.ReadInt32();
            var recordCount = reader.ReadInt32();
            if (size < TmbHeaderSize || size > fileData.Length - start || recordCount < 0
                || recordCount > (size - TmbHeaderSize) / RecordHeaderSize)
            {
                yield return PAP000A;
                yield break;
            }

            int end = start + size;
            int position = start + TmbHeaderSize;
            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                if (position > end - RecordHeaderSize)
                {
                    yield return PAP000A;
                    yield break;
                }

                stream.Position = position;
                var recordMagic = reader.ReadUInt32();
                var recordSize = reader.ReadInt32();
                if (recordSize < RecordHeaderSize || recordSize > end - position)
                {
                    yield return PAP000A;
                    yield break;
                }

                if (recordMagic == C198Magic)
                {
                    if (recordSize != C198Size)
                    {
                        yield return PAP000A;
                        yield break;
                    }

                    stream.Position = position + SummonIdOffset;
                    var summonId = reader.ReadByte();
                    if (summonId != 0 || summonId != 1)
                    {
                        ValidationMessage message = summonId == 8 ? PAP198A : PAP198B;
                        yield return message;
                    }
                }

                position += recordSize;
            }

            nextTimeline = end;
            if (animationIndex < animationCount - 1)
            {
                // VFXEditor preserves alignment relative to the first TMB, not file offset 0
                int padding = (4 - ((end - timelineOffset) & 3)) & 3;
                if (padding > fileData.Length - end)
                {
                    yield return PAP000A;
                    yield break;
                }
                nextTimeline += padding;
            }
        }

        if (nextTimeline != fileData.Length)
        {
            yield return PAP000A;
        }
    }
}
