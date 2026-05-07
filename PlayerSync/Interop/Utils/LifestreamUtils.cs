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
