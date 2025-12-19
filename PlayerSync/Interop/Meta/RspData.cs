namespace MareSynchronos.Interop.Meta
{
    public static class RspData
    {
        public class RspHeightBounds
        {
            public float MaleMin { get; set; }
            public float MaleMax { get; set; }
            public float FemaleMin { get; set; }
            public float FemaleMax { get; set; }
        }

        public enum SubRace : byte
        {
            Unknown = 0,
            Midlander = 1,
            Highlander = 2,
            Wildwood = 3,
            Duskwight = 4,
            Plainsfolk = 5,
            Dunesfolk = 6,
            SeekerOfTheSun = 7,
            KeeperOfTheMoon = 8,
            Seawolf = 9,
            Hellsguard = 10,
            Raen = 11,
            Xaela = 12,
            Helion = 13,
            Lost = 14,
            Rava = 15,
            Veena = 16,
        }

        public enum Gender : byte
        {
            Male = 0,
            Female = 1
        }

        public enum RspAttribute : byte
        {
            MaleMinSize = 0,
            MaleMaxSize = 1,
            MaleMinTail = 2,
            MaleMaxTail = 3,
            FemaleMinSize = 4,
            FemaleMaxSize = 5,
            FemaleMinTail = 6,
            FemaleMaxTail = 7,
            BustMinX = 8,
            BustMinY = 9,
            BustMinZ = 10,
            BustMaxX = 11,
            BustMaxY = 12,
            BustMaxZ = 13,
            NumAttributes = 14,
        }

        // Pull this from the game eventually, at least for default values...
        public static Dictionary<SubRace, RspHeightBounds> CreateDefaultRspHeightBounds() => new()
        {
            [SubRace.Midlander] = new RspHeightBounds
            {
                MaleMin = 0.960f,
                MaleMax = 1.04f,
                FemaleMin = 0.960f,
                FemaleMax = 1.04f,
            },
            [SubRace.Highlander] = new RspHeightBounds
            {
                MaleMin = 1.056f,
                MaleMax = 1.144f,
                FemaleMin = 1.056f,
                FemaleMax = 1.144f,
            },
            [SubRace.Wildwood] = new RspHeightBounds
            {
                MaleMin = 0.961f,
                MaleMax = 1.039f,
                FemaleMin = 0.961f,
                FemaleMax = 1.039f,
            },
            [SubRace.Duskwight] = new RspHeightBounds
            {
                MaleMin = 0.961f,
                MaleMax = 1.039f,
                FemaleMin = 0.961f,
                FemaleMax = 1.039f,
            },
            [SubRace.Plainsfolk] = new RspHeightBounds
            {
                MaleMin = 0.945f,
                MaleMax = 1.055f,
                FemaleMin = 0.945f,
                FemaleMax = 1.055f,
            },
            [SubRace.Dunesfolk] = new RspHeightBounds
            {
                MaleMin = 0.945f,
                MaleMax = 1.055f,
                FemaleMin = 0.945f,
                FemaleMax = 1.055f,
            },
            [SubRace.SeekerOfTheSun] = new RspHeightBounds
            {
                MaleMin = 0.910f,
                MaleMax = 0.990f,
                FemaleMin = 0.960f,
                FemaleMax = 1.040f,
            },
            [SubRace.KeeperOfTheMoon] = new RspHeightBounds
            {
                MaleMin = 0.910f,
                MaleMax = 0.990f,
                FemaleMin = 0.960f,
                FemaleMax = 1.040f,
            },
            [SubRace.Seawolf] = new RspHeightBounds
            {
                MaleMin = 0.962f,
                MaleMax = 1.038f,
                FemaleMin = 1.00f,
                FemaleMax = 1.160f,
            },
            [SubRace.Hellsguard] = new RspHeightBounds
            {
                MaleMin = 0.962f,
                MaleMax = 1.038f,
                FemaleMin = 1.00f,
                FemaleMax = 1.160f,
            },
            [SubRace.Raen] = new RspHeightBounds
            {
                MaleMin = 1.160f,
                MaleMax = 1.240f,
                FemaleMin = 0.930f,
                FemaleMax = 1.010f,
            },
            [SubRace.Xaela] = new RspHeightBounds
            {
                MaleMin = 1.160f,
                MaleMax = 1.240f,
                FemaleMin = 0.930f,
                FemaleMax = 1.010f,
            },
            [SubRace.Helion] = new RspHeightBounds
            {
                MaleMin = 0.892f,
                MaleMax = 0.968f,
                FemaleMin = 0.986f,
                FemaleMax = 1.066f,
            },
            [SubRace.Lost] = new RspHeightBounds
            {
                MaleMin = 0.892f,
                MaleMax = 0.968f,
                FemaleMin = 0.986f,
                FemaleMax = 1.066f,
            },
            [SubRace.Rava] = new RspHeightBounds
            {
                MaleMin = 0.984f,
                MaleMax = 1.066f,
                FemaleMin = 1.111f,
                FemaleMax = 1.189f,
            },
            [SubRace.Veena] = new RspHeightBounds
            {
                MaleMin = 0.984f,
                MaleMax = 1.066f,
                FemaleMin = 1.111f,
                FemaleMax = 1.189f,
            },
        };
    }
}
