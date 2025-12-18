using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Meta
{
    /// <summary> 
    /// RSP helper for Penumbra V1 ("META0001") meta blobs
    /// Penumbra credit to Ottermandias https://github.com/xivdev/Penumbra
    /// This is only used to read values, nothing is modified in flight
    /// - Assumes the base64 string is: GZip version byte + "META0001" + sections 
    /// - Walks IMC, EQP, EQDP, EST to locate the RSP section 
    /// </summary>
    public static class ManipRsp
    {
        // ConvertManipsV1 constants
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
        /// Extract the RSP min/max height values (if present) for a given subrace+gender
        /// from a META0001 manipulation blob.
        ///
        /// </summary>
        /// <param name="logger">Logger for trace output.</param>
        /// <param name="base64Input">Base64-encoded META0001 blob.</param>
        /// <param name="targetSubRace">Subrace to inspect.</param>
        /// <param name="targetGender">Gender to inspect (Male=0, Female=1).</param>
        /// <param name="hasMin">True if a min-height RSP entry was found.</param>
        /// <param name="minValue">The min-height RSP value if present.</param>
        /// <param name="hasMax">True if a max-height RSP entry was found.</param>
        /// <param name="maxValue">The max-height RSP value if present.</param>
        public static void GetRspHeightValues(ILogger logger, string base64Input, RspData.SubRace targetSubRace, RspData.Gender targetGender,
            out bool hasMin, out float minValue, out bool hasMax, out float maxValue)
        {
            if (string.IsNullOrWhiteSpace(base64Input))
                throw new ArgumentException("Manipulation string is null or empty.", nameof(base64Input));

            hasMin = false;
            hasMax = false;
            minValue = 0f;
            maxValue = 0f;

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

            var version = decompressed[0];
            if (version != 1)
                throw new NotSupportedException($"Unsupported meta version {version}, expected 1.");

            if (!IsMetaHeader(decompressed, 1, "META0001"))
                throw new InvalidDataException("META0001 header not found at expected position.");

            // Walk sections to reach RSP
            var offset = 1 + 8; // version + header
            int imcCount = ReadInt32(decompressed, ref offset);
            offset += imcCount * (SizeImcIdentifier + SizeImcEntry);
            int eqpCount = ReadInt32(decompressed, ref offset);
            offset += eqpCount * (SizeEqpIdentifier + SizeEqpEntry);
            int eqdpCount = ReadInt32(decompressed, ref offset);
            offset += eqdpCount * (SizeEqdpIdentifier + SizeEqdpEntry);
            int estCount = ReadInt32(decompressed, ref offset);
            offset += estCount * (SizeEstIdentifier + SizeEstEntry);

            int rspCount = ReadInt32(decompressed, ref offset);
            int rspDataStart = offset;
            int rspRecordSize = SizeRspIdentifier + SizeRspEntry;
            int rspDataLength = rspCount * rspRecordSize;

            if (rspDataStart + rspDataLength > decompressed.Length)
                throw new InvalidDataException("Rsp section exceeds available data.");

            for (int i = 0; i < rspCount; i++)
            {
                int entryOffset = rspDataStart + i * rspRecordSize;

                var subRace = (RspData.SubRace)decompressed[entryOffset + 0];
                var attribute = (RspData.RspAttribute)decompressed[entryOffset + 1];

                if (subRace != targetSubRace)
                    continue;

                float currentValue = BitConverter.ToSingle(decompressed, entryOffset + SizeRspIdentifier);

                switch (targetGender)
                {
                    case RspData.Gender.Male:
                        if (attribute == RspData.RspAttribute.MaleMinSize)
                        {
                            logger.LogTrace("RSP MaleMinSize: {val}", currentValue);
                            hasMin = true;
                            minValue = currentValue;
                        }
                        else if (attribute == RspData.RspAttribute.MaleMaxSize)
                        {
                            logger.LogTrace("RSP MaleMaxSize: {val}", currentValue);
                            hasMax = true;
                            maxValue = currentValue;
                        }
                        break;

                    case RspData.Gender.Female:
                        if (attribute == RspData.RspAttribute.FemaleMinSize)
                        {
                            logger.LogTrace("RSP FemaleMinSize: {val}", currentValue);
                            hasMin = true;
                            minValue = currentValue;
                        }
                        else if (attribute == RspData.RspAttribute.FemaleMaxSize)
                        {
                            logger.LogTrace("RSP FemaleMaxSize: {val}", currentValue);
                            hasMax = true;
                            maxValue = currentValue;
                        }
                        break;

                    default:
                        break;
                }
            }
        }

        public static void GetDefaultRspValues(RspData.SubRace subrace, RspData.Gender gender, out float minRsp, out float maxRsp)
        {
            minRsp = 0f;
            maxRsp = 0f;

            if (!RspData.CreateDefaultRspHeightBounds().TryGetValue(subrace, out var bounds) || bounds == null)
                return;

            if (gender == RspData.Gender.Male)
            {
                minRsp = bounds.MaleMin;
                maxRsp = bounds.MaleMax;
            }
            if (gender == RspData.Gender.Female)
            {
                minRsp = bounds.FemaleMin;
                maxRsp = bounds.FemaleMax;
            }
        }

        public static float GetRspFromSlider(float min, float max, int slider)
        {
            slider = Math.Clamp(slider, 0, 100);

            float t = slider / 100f;
            return min + (max - min) * t;
        }

        public static int GetSliderFromRsp(float min, float max, float rsp)
        {
            float t = (rsp - min) / (max - min);
            t = Math.Clamp(t, 0f, 1f);
            return (int)MathF.Round(t * 100f);
        }

        private static int ReadInt32(byte[] buffer, ref int offset)
        {
            if (offset + 4 > buffer.Length)
                throw new InvalidDataException("Unexpected end of data Int32.");

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
    }
}
