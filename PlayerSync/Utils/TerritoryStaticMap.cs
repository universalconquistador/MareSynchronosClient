#nullable enable
using System.Collections.Generic;
namespace TerritoryTools
{
    public static class TerritoryStaticMap
    {
        public static List<uint> TownTerritoryIds { get; set; } = new List<uint>([128, 129, 130, 131, 132, 133, 144,
            388, 418, 419, 478, 635, 628, 759, 819, 820, 962, 963, 1185, 1186]);
        public static bool IsTown(uint id) => TownTerritoryIds.Contains(id);

        /// <summary>
        /// Territory IDs that are always excluded from ZoneSync regardless of user settings.
        /// Covers private or unsupported areas not caught by duty-bound or instance number checks.
        /// </summary>
        public static HashSet<uint> ForbiddenZoneSyncTerritoryIds { get; } =
        [
            392, // Sanctum of the Twelve
            393, // Sanctum of the Twelve
            425, // Company Workshop
            890 // Island Sanctuary
        ];
    }
}