using System.IO.Compression;
using System.Text;
using MareSynchronos.Interop.Meta;
using Newtonsoft.Json.Linq;

namespace MareSynchronos.Interop;

public static class GlamourerDecoder
{
    /// <summary>
    /// Decode a Glamourer string to get subrace/gender data
    /// Glamourer credit to Ottermandias https://github.com/Ottermandias/Glamourer
    /// </summary>
    public static (RspData.SubRace SubRace, RspData.Gender Gender, int Height) GetRaceAndGender(string encoded)
    {
        if (encoded == null)
            throw new ArgumentNullException(nameof(encoded));

        var raw = Convert.FromBase64String(encoded);
        byte[] magic = { 0x1F, 0x8B, 0x08 };
        int idx = IndexOf(raw, magic);
        byte[] jsonBytes;
        if (idx == -1)
        {
            jsonBytes = raw;
        }
        else
        {
            using var src = new MemoryStream(raw, idx, raw.Length - idx);
            using var gzip = new GZipStream(src, CompressionMode.Decompress);
            using var dst = new MemoryStream();
            gzip.CopyTo(dst);
            jsonBytes = dst.ToArray();
        }
        string json = Encoding.UTF8.GetString(jsonBytes);
        var root = JObject.Parse(json);

        var customizeToken = root["Customize"]
                             ?? throw new InvalidOperationException("JSON does not contain 'Customize' object.");

        if (customizeToken is not JObject customize)
            throw new InvalidOperationException("'Customize' is not a JSON object.");

        var heightContainer = customize["Height"]
                            ?? throw new InvalidOperationException("JSON does not contain 'Customize.Height'.");

        var heightValueToken = heightContainer["Value"]
                             ?? throw new InvalidOperationException("JSON does not contain 'Customize.Height.Value'.");

        // The subraces are called Clans but we reference them as subrace in the code (or even race, but these are different than subrace)
        var raceContainer = customize["Clan"]
                            ?? throw new InvalidOperationException("JSON does not contain 'Customize.Clan'.");

        var raceValueToken = raceContainer["Value"]
                             ?? throw new InvalidOperationException("JSON does not contain 'Customize.Clan.Value'.");

        var genderContainer = customize["Gender"]
                              ?? throw new InvalidOperationException("JSON does not contain 'Customize.Gender'.");

        var genderValueToken = genderContainer["Value"]
                               ?? throw new InvalidOperationException("JSON does not contain 'Customize.Gender.Value'.");

        int heightInt = heightValueToken.Value<int>();
        int raceInt = raceValueToken.Value<int>();
        int genderInt = genderValueToken.Value<int>();

        var subRace = (RspData.SubRace)raceInt;
        var gender = (RspData.Gender)genderInt;

        return (subRace, gender, heightInt);
    }

    private static int IndexOf(byte[] buffer, byte[] pattern)
    {
        if (pattern.Length == 0) return 0;

        for (int i = 0; i <= buffer.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match) return i;
        }

        return -1;
    }
}
