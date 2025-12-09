using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Meta
{
    /// <summary>
    /// RSP manipulation helper for Penumbra V1 ("META0001") meta blobs
    /// Penumbra credit to Ottermandias https://github.com/xivdev/Penumbra
    /// This is only used to read values, nothing is modified in flight
    /// - Assumes the base64 string is: GZip version byte + "META0001" + sections
    /// - Walks IMC, EQP, EQDP, EST to locate the RSP section
    /// </summary>
    public static class ManipRsp
    {
        // ConvertManipsV1
        private const int SizeImcIdentifier = 8;
        private const int SizeImcEntry = 6;

        private const int SizeEqpIdentifier = 4;
        private const int SizeEqpEntry = 8;

        private const int SizeEqdpIdentifier = 6;
        private const int SizeEqdpEntry = 2;

        private const int SizeEstIdentifier = 6;
        private const int SizeEstEntry = 2;

        private const int SizeRspIdentifier = 2; // SubRace (byte) + RspAttribute (byte)
        private const int SizeRspEntry = 4;

        /// <summary>
        /// Used to change for RSP Height changes in a mod manipulation string
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="base64Input"></param>
        /// <param name="boundsBySubRace"></param>
        /// <param name="base64Output"></param>
        /// <param name="valIn"></param>
        /// <param name="valOut"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public static bool ClampRspHeights(ILogger logger, string base64Input, IReadOnlyDictionary<RspData.SubRace, RspData.RspHeightBounds> boundsBySubRace, 
            out string base64Output, out float valIn, out float valOut)
        {
            if (string.IsNullOrWhiteSpace(base64Input))
                throw new ArgumentException("Manipulation string is null or empty.", nameof(base64Input));

            if (boundsBySubRace == null)
                throw new ArgumentNullException(nameof(boundsBySubRace));

            base64Output = base64Input;
            valIn = 0f;
            valOut = 0f;

            // Decode base64 and decompress GZip
            var compressed = Convert.FromBase64String(base64Input);
            byte[] decompressed;

            using (var input = new MemoryStream(compressed))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                decompressed = output.ToArray();
            }

            if (decompressed.Length < 1 + 8)
                throw new InvalidDataException("Decompressed data too short for version + META header.");

            // Version byte
            var version = decompressed[0];
            if (version != 1)
                throw new NotSupportedException($"Unsupported meta version {version}, expected 1.");

            // Meta section -> "META0001"
            if (!IsMetaHeader(decompressed, 1, "META0001"))
                throw new InvalidDataException("META0001 header not found at expected position.");

            // Walk sections to reach RSP (IMC -> EQP -> EQDP -> EST -> RSP)
            var offset = 1 + 8; // version + header

            int imcCount = ReadInt32(decompressed, ref offset);
            offset += imcCount * (SizeImcIdentifier + SizeImcEntry);

            int eqpCount = ReadInt32(decompressed, ref offset);
            offset += eqpCount * (SizeEqpIdentifier + SizeEqpEntry);

            int eqdpCount = ReadInt32(decompressed, ref offset);
            offset += eqdpCount * (SizeEqdpIdentifier + SizeEqdpEntry);

            int estCount = ReadInt32(decompressed, ref offset);
            offset += estCount * (SizeEstIdentifier + SizeEstEntry);

            // RspCount and start of Rsp records
            int rspCount = ReadInt32(decompressed, ref offset);
            int rspDataStart = offset;
            int rspRecordSize = SizeRspIdentifier + SizeRspEntry;
            int rspDataLength = rspCount * rspRecordSize;

            if (rspDataStart + rspDataLength > decompressed.Length)
                throw new InvalidDataException("Rsp section exceeds available data.");

            bool modified = false;

            // Walk Rsp entries
            for (int i = 0; i < rspCount; i++)
            {
                int entryOffset = rspDataStart + i * rspRecordSize;

                var subRace = (RspData.SubRace)decompressed[entryOffset + 0];
                var attribute = (RspData.RspAttribute)decompressed[entryOffset + 1];

                float currentValue = BitConverter.ToSingle(decompressed, entryOffset + SizeRspIdentifier);
                valIn = currentValue;

                if (!boundsBySubRace.TryGetValue(subRace, out var bounds) || bounds == null)
                    continue;

                float newValue = currentValue;

                switch (attribute)
                {
                    case RspData.RspAttribute.MaleMinSize:
                    case RspData.RspAttribute.MaleMaxSize:
                        logger.LogTrace("RSP: {val} {min} {max}", currentValue, bounds.MaleMin, bounds.MaleMax);
                        newValue = Clamp(currentValue, bounds.MaleMin, bounds.MaleMax);
                        valOut = newValue;
                        break;

                    case RspData.RspAttribute.FemaleMinSize:
                    case RspData.RspAttribute.FemaleMaxSize:
                        logger.LogTrace("RSP: {val} {min} {max}", currentValue, bounds.FemaleMin, bounds.FemaleMax);
                        newValue = Clamp(currentValue, bounds.FemaleMin, bounds.FemaleMax);
                        valOut = newValue;
                        break;

                    default:
                        valIn = currentValue;
                        valOut = currentValue;
                        continue;
                }

                if (float.IsNaN(newValue) || float.IsInfinity(newValue))
                    continue;

                if (!newValue.Equals(currentValue))
                {
                    modified = true;
                    var valueBytes = BitConverter.GetBytes(newValue);
                    Buffer.BlockCopy(valueBytes, 0, decompressed, entryOffset + SizeRspIdentifier, SizeRspEntry);
                }
            }

            // If nothing changed, return original
            if (!modified)
            {
                valIn = 0;
                valOut = 0;
                base64Output = base64Input;
                return false;
            }

            // Re-compress the modified buffer, return base64
            byte[] recompressed;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                {
                    gzip.Write(decompressed, 0, decompressed.Length);
                }

                recompressed = output.ToArray();
            }

            base64Output = Convert.ToBase64String(recompressed);
            return true;
        }

        private static int ReadInt32(byte[] buffer, ref int offset)
        {
            if (offset + 4 > buffer.Length)
                throw new InvalidDataException("Unexpected end of data while reading Int32.");

            int value = BitConverter.ToInt32(buffer, offset);
            offset += 4;
            return value;
        }

        private static bool IsMetaHeader(byte[] buffer, int offset, string expected)
        {
            if (offset + expected.Length > buffer.Length)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                if (buffer[offset + i] != (byte)expected[i])
                    return false;
            }

            return true;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value >= min && value <= max) return value;
            if (value < min) value = min;
            if (value > max) value = max;
            return value;
        }
    }
}
