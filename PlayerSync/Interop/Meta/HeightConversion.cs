
namespace MareSynchronos.Interop.Meta;

public static class HeightConversion
{
    private static readonly Dictionary<(RspData.SubRace SubRace, RspData.Gender Gender), float> HeightFactorCmPerRsp
        = new()
    {
        // Midlander
        { (RspData.SubRace.Midlander,  RspData.Gender.Male),   174.8692308f },
        { (RspData.SubRace.Midlander,  RspData.Gender.Female), 163.8788462f },

        // Highlander
        { (RspData.SubRace.Highlander, RspData.Gender.Male),   174.9580420f },
        { (RspData.SubRace.Highlander, RspData.Gender.Female), 163.8566434f },

        // Elezen (Wildwood / Duskwight)
        { (RspData.SubRace.Wildwood,   RspData.Gender.Male),   201.9287777f },
        { (RspData.SubRace.Wildwood,   RspData.Gender.Female), 190.9278152f },
        { (RspData.SubRace.Duskwight,  RspData.Gender.Male),   201.9287777f },
        { (RspData.SubRace.Duskwight,  RspData.Gender.Female), 190.9278152f },

        // Lalafell (Plainsfolk / Dunesfolk)
        { (RspData.SubRace.Plainsfolk, RspData.Gender.Male),    91.96966825f },
        { (RspData.SubRace.Plainsfolk, RspData.Gender.Female),  91.96966825f },
        { (RspData.SubRace.Dunesfolk,  RspData.Gender.Male),    91.96966825f },
        { (RspData.SubRace.Dunesfolk,  RspData.Gender.Female),  91.96966825f },

        // Miqo'te (Seeker of the Sun / Keeper of the Moon)
        { (RspData.SubRace.SeekerOfTheSun,  RspData.Gender.Male),   174.9777778f },
        { (RspData.SubRace.SeekerOfTheSun,  RspData.Gender.Female), 155.8192308f },
        { (RspData.SubRace.KeeperOfTheMoon, RspData.Gender.Male),   174.9777778f },
        { (RspData.SubRace.KeeperOfTheMoon, RspData.Gender.Female), 155.8192308f },

        // Roegadyn (Seawolf / Hellsguard)
        { (RspData.SubRace.Seawolf,    RspData.Gender.Male),   221.9441233f },
        { (RspData.SubRace.Seawolf,    RspData.Gender.Female), 192.0327586f },
        { (RspData.SubRace.Hellsguard, RspData.Gender.Male),   221.9441233f },
        { (RspData.SubRace.Hellsguard, RspData.Gender.Female), 192.0327586f },

        // Au Ra (Raen / Xaela)
        { (RspData.SubRace.Raen,       RspData.Gender.Male),   174.9322581f },
        { (RspData.SubRace.Raen,       RspData.Gender.Female), 156.9267327f },
        { (RspData.SubRace.Xaela,      RspData.Gender.Male),   174.9322581f },
        { (RspData.SubRace.Xaela,      RspData.Gender.Female), 156.9267327f },

        // Hrothgar (Hellion / Lost)
        { (RspData.SubRace.Helion,     RspData.Gender.Male),   219.8884298f },
        { (RspData.SubRace.Helion,     RspData.Gender.Female), 187.0450281f },
        { (RspData.SubRace.Lost,       RspData.Gender.Male),   219.8884298f },
        { (RspData.SubRace.Lost,       RspData.Gender.Female), 187.0450281f },

        // Viera (Rava / Veena)
        { (RspData.SubRace.Rava,       RspData.Gender.Male),   174.8930582f },
        { (RspData.SubRace.Rava,       RspData.Gender.Female), 160.8595458f },
        { (RspData.SubRace.Veena,      RspData.Gender.Male),   174.8930582f },
        { (RspData.SubRace.Veena,      RspData.Gender.Female), 160.8595458f },
    };

    public static float GetHeightFactorCm(RspData.SubRace subRace, RspData.Gender gender)
    {
        if (!HeightFactorCmPerRsp.TryGetValue((subRace, gender), out var factor))
            throw new KeyNotFoundException($"No height factor defined for {subRace} {gender}.");
        return factor;
    }

    public static float GetCharacterHeightCm(RspData.SubRace subRace, RspData.Gender gender, float rspHeightValue)
    {
        if (rspHeightValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(rspHeightValue), "RSP height must be positive.");

        float factor = GetHeightFactorCm(subRace, gender);
        return factor * rspHeightValue;
    }

    public static float GetRspFromHeightCm(RspData.SubRace subRace, RspData.Gender gender, float heightCm)
    {
        if (heightCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightCm), "Height must be positive.");

        float factor = GetHeightFactorCm(subRace, gender);
        return heightCm / factor;
    }

    public static float GetMaxRspForGlobalHeight(RspData.SubRace subRace, RspData.Gender gender, float globalMaxHeightCm)
    {
        if (globalMaxHeightCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(globalMaxHeightCm), "Global max must be positive.");

        float factor = GetHeightFactorCm(subRace, gender);
        return globalMaxHeightCm / factor;
    }

    public static float FeetInchesToCm(int feet, int inches)
    {
        int totalInches = feet * 12 + inches;
        return totalInches * 2.54f;
    }

    public static void CmToFeetInches(float cm, out int feet, out int inches)
    {
        int totalInches = (int)Math.Round(cm / 2.54f);
        if (totalInches < 0) totalInches = 0;

        feet = totalInches / 12;
        inches = totalInches % 12;
    }
}
