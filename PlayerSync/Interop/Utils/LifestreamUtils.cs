global using AddressBookEntry = (string Name, int World, int City, int Ward, int PropertyType, int Plot, int Apartment, bool ApartmentSubdivision, bool AliasEnabled, string Alias);

namespace MareSynchronos.Interop.Utils
{
    public enum ResidentialAetheryteKind
    {
        Uldah = 9,
        Gridania = 2,
        Limsa = 8,
        Foundation = 70,
        Kugane = 111,
    }

    public static class LifestreamUtils
    {
        public static ResidentialAetheryteKind? GetResidentialAetheryteKindFromTerritoryId(int id)
        {
            return id switch
            {
                339 => ResidentialAetheryteKind.Limsa,
                340 => ResidentialAetheryteKind.Gridania,
                341 => ResidentialAetheryteKind.Uldah,
                641 => ResidentialAetheryteKind.Kugane,
                979 => ResidentialAetheryteKind.Foundation,
                608 => ResidentialAetheryteKind.Limsa,
                609 => ResidentialAetheryteKind.Gridania,
                610 => ResidentialAetheryteKind.Uldah,
                655 => ResidentialAetheryteKind.Kugane,
                999 => ResidentialAetheryteKind.Foundation,
                _ => null
            };
        }

        public static string GetResidentialDistrictName(ResidentialAetheryteKind kind)
        {
            return kind switch
            {
                ResidentialAetheryteKind.Uldah => "Goblet",
                ResidentialAetheryteKind.Gridania => "Lavender Beds",
                ResidentialAetheryteKind.Limsa => "Mist",
                ResidentialAetheryteKind.Foundation => "Empyreum",
                ResidentialAetheryteKind.Kugane => "Shirogane",
                _ => "Unknown"
            };
        }
    }
}
