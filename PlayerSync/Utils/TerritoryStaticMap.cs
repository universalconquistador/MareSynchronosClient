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
        public static HashSet<uint> AllowedZoneSyncTerritoryIds { get; } =
        [
            // === InstanceContent - Group (468) ===
            142, // Halatali - the Dragon's Neck
            151, // The World of Darkness - the World of Darkness
            159, // The Wanderer's Palace - the Wanderer's Palace
            160, // Pharos Sirius - Pharos Sirius
            167, // Amdapor Keep - Amdapor Keep
            171, // Dzemael Darkhold - Dzemael Darkhold
            172, // Aurum Vale - the Aurum Vale
            174, // Labyrinth of the Ancients - the Labyrinth of the Ancients
            188, // The Wanderer's Palace - the Wanderer's Palace (Hard)
            189, // Amdapor Keep - Amdapor Keep (Hard)
            190, // Central Shroud - Under the Armor
            191, // East Shroud - Pulling Poison Posies
            192, // South Shroud - Stinging Back
            193, // IC-06 Central Decks - the Final Coil of Bahamut - Turn 1
            194, // IC-06 Regeneration Grid - the Final Coil of Bahamut - Turn 2
            195, // IC-06 Main Bridge - the Final Coil of Bahamut - Turn 3
            196, // The Burning Heart - the Final Coil of Bahamut - Turn 4
            214, // Middle La Noscea - Basic Training: Enemy Parties
            215, // Western Thanalan - Basic Training: Enemy Strongholds
            216, // Central Thanalan - Hero on the Half Shell
            219, // Central Shroud - Flicking Sticks and Taking Names
            220, // South Shroud - All's Well that Ends in the Well
            221, // Upper La Noscea - More than a Feeler
            222, // Lower La Noscea - Annoy the Void
            223, // Coerthas Central Highlands - Shadow and Claw
            241, // Upper Aetheroacoustic Exploratory Site - the Binding Coil of Bahamut - Turn 1
            242, // Lower Aetheroacoustic Exploratory Site - the Binding Coil of Bahamut - Turn 2
            243, // The Ragnarok - the Binding Coil of Bahamut - Turn 3
            244, // Ragnarok Drive Cylinder - the Binding Coil of Bahamut - Turn 4
            245, // Ragnarok Central Core - the Binding Coil of Bahamut - Turn 5
            281, // The Whorleater - the Whorleater (Hard)
            292, // Bowl of Embers - the Bowl of Embers (Hard)
            293, // The Navel - the Navel (Hard)
            294, // The Howling Eye - the Howling Eye (Hard)
            295, // Bowl of Embers - the Bowl of Embers (Extreme)
            296, // The Navel - the Navel (Extreme)
            297, // The Howling Eye - the Howling Eye (Extreme)
            298, // Coerthas Central Highlands - Long Live the Queen
            299, // Mor Dhona - Ward Up
            300, // Mor Dhona - Solemn Trinity
            348, // Porta Decumana - the Minstrel's Ballad: Ultima's Bane
            349, // Copperbell Mines - Copperbell Mines (Hard)
            350, // Haukke Manor - Haukke Manor (Hard)
            353, // Kugane Ohashi - Special Event I
            354, // The Dancing Plague - Special Event II
            355, // Dalamud's Shadow - the Second Coil of Bahamut - Turn 1
            356, // The Outer Coil - the Second Coil of Bahamut - Turn 2
            357, // Central Decks - the Second Coil of Bahamut - Turn 3
            358, // The Holocharts - the Second Coil of Bahamut - Turn 4
            359, // The Whorleater - the Whorleater (Extreme)
            360, // Halatali - Halatali (Hard)
            361, // Hullbreaker Isle - Hullbreaker Isle
            362, // Brayflox's Longstop - Brayflox's Longstop (Hard)
            363, // The Lost City of Amdapor - the Lost City of Amdapor
            364, // Thornmarch - Thornmarch (Extreme)
            365, // Stone Vigil - the Stone Vigil (Hard)
            366, // Griffin Crossing - Battle on the Big Bridge
            367, // The Sunken Temple of Qarn - the Sunken Temple of Qarn (Hard)
            368, // The Weeping Saint - A Relic Reborn: the Chimera
            369, // Hall of the Bestiarii - A Relic Reborn: the Hydra
            372, // Syrcus Tower - Syrcus Tower
            373, // The Tam-Tara Deepcroft - the Tam-Tara Deepcroft (Hard)
            374, // The Striking Tree - the Striking Tree (Hard)
            375, // The Striking Tree - the Striking Tree (Extreme)
            376, // Carteneau Flats: Borderland Ruins - the Borderland Ruins (Secure)
            377, // Akh Afah Amphitheatre - the Akh Afah Amphitheatre (Hard)
            378, // Akh Afah Amphitheatre - the Akh Afah Amphitheatre (Extreme)
            380, // Dalamud's Shadow - the Second Coil of Bahamut (Savage) - Turn 1
            381, // The Outer Coil - the Second Coil of Bahamut (Savage) - Turn 2
            382, // Central Decks - the Second Coil of Bahamut (Savage) - Turn 3
            383, // The Holocharts - the Second Coil of Bahamut (Savage) - Turn 4
            387, // Sastasha - Sastasha (Hard)
            394, // South Shroud - Urth's Fount
            396, // Amdapor Keep - Battle in the Big Keep
            420, // Neverreap - Neverreap
            426, // The Chrysalis - the Chrysalis
            430, // The Fractal Continuum - the Fractal Continuum
            431, // Seal Rock - Seal Rock (Seize)
            432, // Thok ast Thok - Thok ast Thok (Hard)
            436, // The Limitless Blue - the Limitless Blue (Hard)
            437, // Singularity Reactor - the Singularity Reactor
            442, // The Fist of the Father - Alexander - The Fist of the Father
            443, // The Cuff of the Father - Alexander - The Cuff of the Father
            444, // The Arm of the Father - Alexander - The Arm of the Father
            445, // The Burden of the Father - Alexander - The Burden of the Father
            446, // Thok ast Thok - Thok ast Thok (Extreme)
            447, // The Limitless Blue - the Limitless Blue (Extreme)
            448, // Singularity Reactor - the Minstrel's Ballad: Thordan's Reign
            449, // The Fist of the Father - Alexander - The Fist of the Father (Savage)
            450, // The Cuff of the Father - Alexander - The Cuff of the Father (Savage)
            451, // The Arm of the Father - Alexander - The Arm of the Father (Savage)
            452, // The Burden of the Father - Alexander - The Burden of the Father (Savage)
            508, // Void Ark - the Void Ark
            509, // The Gilded Araya - the Gilded Araya
            510, // Pharos Sirius - Pharos Sirius (Hard)
            511, // Saint Mocianne's Arboretum - Saint Mocianne's Arboretum
            517, // Containment Bay S1T7 - Containment Bay S1T7
            519, // The Lost City of Amdapor - the Lost City of Amdapor (Hard)
            520, // The Fist of the Son - Alexander - The Fist of the Son
            521, // The Cuff of the Son - Alexander - The Cuff of the Son
            522, // The Arm of the Son - Alexander - The Arm of the Son
            523, // The Burden of the Son - Alexander - The Burden of the Son
            524, // Containment Bay S1T7 - Containment Bay S1T7 (Extreme)
            529, // The Fist of the Son - Alexander - The Fist of the Son (Savage)
            530, // The Cuff of the Son - Alexander - The Cuff of the Son (Savage)
            531, // The Arm of the Son - Alexander - The Arm of the Son (Savage)
            532, // The Burden of the Son - Alexander - The Burden of the Son (Savage)
            554, // The Fields of Glory - the Fields of Glory (Shatter)
            556, // The Weeping City of Mhach - the Weeping City of Mhach
            557, // Hullbreaker Isle - Hullbreaker Isle (Hard)
            558, // The Aquapolis - the Aquapolis
            559, // Steps of Faith - the Final Steps of Faith
            561, // The Palace of the Dead - the Palace of the Dead (Floors 1-10)
            562, // The Palace of the Dead - the Palace of the Dead (Floors 11-20)
            563, // The Palace of the Dead - the Palace of the Dead (Floors 21-30)
            564, // The Palace of the Dead - the Palace of the Dead (Floors 31-40)
            565, // The Palace of the Dead - the Palace of the Dead (Floors 41-50)
            566, // Steps of Faith - the Minstrel's Ballad: Nidhogg's Rage
            571, // Haunted Manor - the Haunted Manor
            576, // Containment Bay P1T6 - Containment Bay P1T6
            577, // Containment Bay P1T6 - Containment Bay P1T6 (Extreme)
            578, // The Great Gubal Library - the Great Gubal Library (Hard)
            580, // Eyes of the Creator - Alexander - The Eyes of the Creator
            581, // Breath of the Creator - Alexander - The Breath of the Creator
            582, // Heart of the Creator - Alexander - The Heart of the Creator
            583, // Soul of the Creator - Alexander - The Soul of the Creator
            584, // Eyes of the Creator - Alexander - The Eyes of the Creator (Savage)
            585, // Breath of the Creator - Alexander - The Breath of the Creator (Savage)
            586, // Heart of the Creator - Alexander - The Heart of the Creator (Savage)
            587, // Soul of the Creator - Alexander - The Soul of the Creator (Savage)
            593, // The Palace of the Dead - the Palace of the Dead (Floors 51-60)
            594, // The Palace of the Dead - the Palace of the Dead (Floors 61-70)
            595, // The Palace of the Dead - the Palace of the Dead (Floors 71-80)
            596, // The Palace of the Dead - the Palace of the Dead (Floors 81-90)
            597, // The Palace of the Dead - the Palace of the Dead (Floors 91-100)
            598, // The Palace of the Dead - the Palace of the Dead (Floors 101-110)
            599, // The Palace of the Dead - the Palace of the Dead (Floors 111-120)
            600, // The Palace of the Dead - the Palace of the Dead (Floors 121-130)
            601, // The Palace of the Dead - the Palace of the Dead (Floors 131-140)
            602, // The Palace of the Dead - the Palace of the Dead (Floors 141-150)
            603, // The Palace of the Dead - the Palace of the Dead (Floors 151-160)
            604, // The Palace of the Dead - the Palace of the Dead (Floors 161-170)
            605, // The Palace of the Dead - the Palace of the Dead (Floors 171-180)
            606, // The Palace of the Dead - the Palace of the Dead (Floors 181-190)
            607, // The Palace of the Dead - the Palace of the Dead (Floors 191-200)
            617, // Sohm Al - Sohm Al (Hard)
            627, // Dun Scaith - Dun Scaith
            637, // Containment Bay Z1T9 - Containment Bay Z1T9
            638, // Containment Bay Z1T9 - Containment Bay Z1T9 (Extreme)
            662, // Kugane Castle - Kugane Castle
            663, // The Temple of the Fist - the Temple of the Fist
            674, // The Blessed Treasury - the Pool of Tribute
            677, // The Blessed Treasury - the Pool of Tribute (Extreme)
            679, // The Royal Airship Landing - the Royal Menagerie
            691, // Deltascape V1.0 - Deltascape V1.0
            692, // Deltascape V2.0 - Deltascape V2.0
            693, // Deltascape V3.0 - Deltascape V3.0
            694, // Deltascape V4.0 - Deltascape V4.0
            695, // Deltascape V1.0 - Deltascape V1.0 (Savage)
            696, // Deltascape V2.0 - Deltascape V2.0 (Savage)
            697, // Deltascape V3.0 - Deltascape V3.0 (Savage)
            698, // Deltascape V4.0 - Deltascape V4.0 (Savage)
            712, // The Lost Canals of Uznair - the Lost Canals of Uznair
            719, // Emanation - Emanation
            720, // Emanation - Emanation (Extreme)
            725, // The Lost Canals of Uznair - the Hidden Canals of Uznair
            729, // Astragalos - Astragalos
            730, // Transparency - the Minstrel's Ballad: Shinryu's Domain
            733, // The Binding Coil of Bahamut - the Unending Coil of Bahamut (Ultimate)
            734, // The Royal City of Rabanastre - the Royal City of Rabanastre
            741, // Sanctum of the Twelve - the Valentione's Ceremony
            742, // Hells' Lid - Hells' Lid
            743, // The Fractal Continuum - the Fractal Continuum (Hard)
            746, // The Jade Stoa - the Jade Stoa
            748, // Sigmascape V1.0 - Sigmascape V1.0
            749, // Sigmascape V2.0 - Sigmascape V2.0
            750, // Sigmascape V3.0 - Sigmascape V3.0
            751, // Sigmascape V4.0 - Sigmascape V4.0
            752, // Sigmascape V1.0 - Sigmascape V1.0 (Savage)
            753, // Sigmascape V2.0 - Sigmascape V2.0 (Savage)
            754, // Sigmascape V3.0 - Sigmascape V3.0 (Savage)
            755, // Sigmascape V4.0 - Sigmascape V4.0 (Savage)
            758, // The Jade Stoa - the Jade Stoa (Extreme)
            761, // The Great Hunt - the Great Hunt
            762, // The Great Hunt - the Great Hunt (Extreme)
            768, // The Swallow's Compass - the Swallow's Compass
            776, // The Ridorana Lighthouse - the Ridorana Lighthouse
            777, // Ultimacy - the Weapon's Refrain (Ultimate)
            778, // Castrum Fluminis - Castrum Fluminis
            779, // Castrum Fluminis - the Minstrel's Ballad: Tsukuyomi's Pain
            788, // Saint Mocianne's Arboretum - Saint Mocianne's Arboretum (Hard)
            791, // Hidden Gorge - Hidden Gorge
            794, // The Shifting Altars of Uznair - the Shifting Altars of Uznair
            798, // Psiscape V1.0 - Alphascape V1.0
            799, // Psiscape V2.0 - Alphascape V2.0
            800, // The Interdimensional Rift - Alphascape V3.0
            801, // The Interdimensional Rift - Alphascape V4.0
            802, // Psiscape V1.0 - Alphascape V1.0 (Savage)
            803, // Psiscape V2.0 - Alphascape V2.0 (Savage)
            804, // The Interdimensional Rift - Alphascape V3.0 (Savage)
            805, // The Interdimensional Rift - Alphascape V4.0 (Savage)
            806, // Kugane Ohashi - Kugane Ohashi
            810, // Hells' Kier - Hells' Kier
            811, // Hells' Kier - Hells' Kier (Extreme)
            821, // Dohn Mheg - Dohn Mheg
            822, // Mt. Gulg - Mt. Gulg
            823, // The Qitana Ravel - the Qitana Ravel
            824, // The Wreath of Snakes - the Wreath of Snakes
            825, // The Wreath of Snakes - the Wreath of Snakes (Extreme)
            826, // The Orbonne Monastery - the Orbonne Monastery
            836, // Malikah's Well - Malikah's Well
            837, // Holminster Switch - Holminster Switch
            838, // Amaurot - Amaurot
            840, // The Twinning - the Twinning
            841, // Akadaemia Anyder - Akadaemia Anyder
            845, // The Dancing Plague - the Dancing Plague
            846, // The Crown of the Immaculate - the Crown of the Immaculate
            847, // The Dying Gasp - the Dying Gasp
            848, // The Crown of the Immaculate - the Crown of the Immaculate (Extreme)
            849, // The Core - Eden's Gate: Resurrection
            850, // The Halo - Eden's Gate: Descent
            851, // The Nereus Trench - Eden's Gate: Inundation
            852, // Atlas Peak - Eden's Gate: Sepulture
            853, // The Core - Eden's Gate: Resurrection (Savage)
            854, // The Halo - Eden's Gate: Descent (Savage)
            855, // The Nereus Trench - Eden's Gate: Inundation (Savage)
            856, // Atlas Peak - Eden's Gate: Sepulture (Savage)
            858, // The Dancing Plague - the Dancing Plague (Extreme)
            879, // The Dungeons of Lyhe Ghiah - The Dungeons of Lyhe Ghiah
            882, // The Copied Factory - the Copied Factory
            884, // The Grand Cosmos - the Grand Cosmos
            885, // The Dying Gasp - the Minstrel's Ballad: Hades's Elegy
            887, // Liminal Space - the Epic of Alexander (Ultimate)
            888, // Onsal Hakair - Onsal Hakair (Danshig Naadam)
            897, // Cinder Drift - Cinder Drift
            898, // Anamnesis Anyder - Anamnesis Anyder
            900, // The Endeavor - Ocean Fishing
            902, // The Gandof Thunder Plains - Eden's Verse: Fulmination
            903, // Ashfall - Eden's Verse: Furor
            904, // The Halo - Eden's Verse: Iconoclasm
            905, // Great Glacier - Eden's Verse: Refulgence
            906, // The Gandof Thunder Plains - Eden's Verse: Fulmination (Savage)
            907, // Ashfall - Eden's Verse: Furor (Savage)
            908, // The Halo - Eden's Verse: Iconoclasm (Savage)
            909, // Great Glacier - Eden's Verse: Refulgence (Savage)
            912, // Cinder Drift - Cinder Drift (Extreme)
            913, // Transmission Control - Memoria Misera (Extreme)
            916, // The Heroes' Gauntlet - the Heroes' Gauntlet
            917, // The Puppets' Bunker - the Puppets' Bunker
            922, // The Seat of Sacrifice - the Seat of Sacrifice
            923, // The Seat of Sacrifice - the Seat of Sacrifice (Extreme)
            924, // The Shifting Oubliettes of Lyhe Ghiah - the Shifting Oubliettes of Lyhe Ghiah
            933, // Matoya's Relict - Matoya's Relict
            934, // Castrum Marinum Drydocks - Castrum Marinum
            935, // Castrum Marinum Drydocks - Castrum Marinum (Extreme)
            938, // Paglth'an - Paglth'an
            940, // The Battlehall - Triple Triad Open Tournament
            941, // The Battlehall - Triple Triad Invitational Parlor
            942, // Sphere of Naught - Eden's Promise: Umbra
            943, // Laxan Loft - Eden's Promise: Litany
            944, // Bygone Gaol - Eden's Promise: Anamorphosis
            945, // The Garden of Nowhere - Eden's Promise: Eternity
            946, // Sphere of Naught - Eden's Promise: Umbra (Savage)
            947, // Laxan Loft - Eden's Promise: Litany (Savage)
            948, // Bygone Gaol - Eden's Promise: Anamorphosis (Savage)
            949, // The Garden of Nowhere - Eden's Promise: Eternity (Savage)
            950, // G-Savior Deck - the Cloud Deck
            951, // G-Savior Deck - the Cloud Deck (Extreme)
            952, // The Tower of Zot - the Tower of Zot
            966, // The Tower at Paradigm's Breach - the Tower at Paradigm's Breach
            968, // Medias Res - Dragonsong's Reprise (Ultimate)
            969, // The Tower of Babil - the Tower of Babil
            970, // Vanaspati - Vanaspati
            973, // The Dead Ends - the Dead Ends
            974, // Ktisis Hyperboreia - Ktisis Hyperboreia
            976, // Smileton - Smileton
            978, // The Aitiascope - the Aitiascope
            986, // The Stigma Dreamscape - the Stigma Dreamscape
            992, // The Dark Inside - the Dark Inside
            993, // The Dark Inside - the Minstrel's Ballad: Zodiark's Fall
            994, // The Phantoms' Feast - The Phantoms' Feast
            995, // The Mothercrystal - the Mothercrystal
            996, // The Mothercrystal - the Minstrel's Ballad: Hydaelyn's Call
            997, // The Final Day - the Final Day
            998, // The Final Day - the Minstrel's Ballad: Endsinger's Aria
            1000, // The Excitatron 6000 - the Excitatron 6000
            1002, // The Gates of Pandæmonium - Asphodelos: The First Circle
            1003, // The Gates of Pandæmonium - Asphodelos: The First Circle (Savage)
            1004, // The Stagnant Limbo - Asphodelos: The Second Circle
            1005, // The Stagnant Limbo - Asphodelos: The Second Circle (Savage)
            1006, // The Fervid Limbo - Asphodelos: The Third Circle
            1007, // The Fervid Limbo - Asphodelos: The Third Circle (Savage)
            1008, // The Sanguine Limbo - Asphodelos: The Fourth Circle
            1009, // The Sanguine Limbo - Asphodelos: The Fourth Circle (Savage)
            1036, // Sastasha - Sastasha
            1037, // The Tam-Tara Deepcroft - the Tam-Tara Deepcroft
            1038, // Copperbell Mines - Copperbell Mines
            1039, // The Thousand Maws of Toto-Rak - the Thousand Maws of Toto-Rak
            1040, // Haukke Manor - Haukke Manor
            1041, // Brayflox's Longstop - Brayflox's Longstop
            1042, // Stone Vigil - the Stone Vigil
            1043, // Castrum Meridianum - Castrum Meridianum
            1044, // The Praetorium - the Praetorium
            1045, // Bowl of Embers - the Bowl of Embers
            1046, // The Navel - the Navel
            1047, // The Howling Eye - the Howling Eye
            1048, // Porta Decumana - the Porta Decumana
            1050, // Alzadaal's Legacy - Alzadaal's Legacy
            1054, // Aglaia - Aglaia
            1062, // Snowcloak - Snowcloak
            1063, // The Keeper of the Lake - the Keeper of the Lake
            1064, // Sohm Al - Sohm Al
            1065, // The Aery - the Aery
            1066, // The Vault - the Vault
            1067, // Thornmarch - Thornmarch (Hard)
            1069, // The Sil'dihn Subterrane - the Sil'dihn Subterrane
            1070, // The Fell Court of Troia - the Fell Court of Troia
            1071, // Storm's Crown - Storm's Crown
            1072, // Storm's Crown - Storm's Crown (Extreme)
            1075, // Another Sil'dihn Subterrane - Another Sil'dihn Subterrane
            1076, // Another Sil'dihn Subterrane - Another Sil'dihn Subterrane (Savage)
            1081, // The Caustic Purgatory - Abyssos: The Fifth Circle
            1082, // The Caustic Purgatory - Abyssos: The Fifth Circle (Savage)
            1083, // The Pestilent Purgatory - Abyssos: The Sixth Circle
            1084, // The Pestilent Purgatory - Abyssos: The Sixth Circle (Savage)
            1085, // The Hollow Purgatory - Abyssos: The Seventh Circle
            1086, // The Hollow Purgatory - Abyssos: The Seventh Circle (Savage)
            1087, // Stygian Insenescence Cells - Abyssos: The Eighth Circle
            1088, // Stygian Insenescence Cells - Abyssos: The Eighth Circle (Savage)
            1095, // Mount Ordeals - Mount Ordeals
            1096, // Mount Ordeals - Mount Ordeals (Extreme)
            1097, // Lapis Manalis - Lapis Manalis
            1099, // Eureka Orthos - Eureka Orthos (Floors 1-10)
            1100, // Eureka Orthos - Eureka Orthos (Floors 11-20)
            1101, // Eureka Orthos - Eureka Orthos (Floors 21-30)
            1102, // Eureka Orthos - Eureka Orthos (Floors 31-40)
            1103, // Eureka Orthos - Eureka Orthos (Floors 41-50)
            1104, // Eureka Orthos - Eureka Orthos (Floors 51-60)
            1105, // Eureka Orthos - Eureka Orthos (Floors 61-70)
            1106, // Eureka Orthos - Eureka Orthos (Floors 71-80)
            1107, // Eureka Orthos - Eureka Orthos (Floors 81-90)
            1108, // Eureka Orthos - Eureka Orthos (Floors 91-100)
            1109, // The Great Gubal Library - the Great Gubal Library
            1110, // Aetherochemical Research Facility - the Aetherochemical Research Facility
            1111, // The Antitower - the Antitower
            1112, // Sohr Khai - Sohr Khai
            1113, // Xelphatol - Xelphatol
            1114, // Baelsar's Wall - Baelsar's Wall
            1118, // Euphrosyne - Euphrosyne
            1122, // The Interdimensional Rift - the Omega Protocol (Ultimate)
            1123, // The Shifting Gymnasion Agonon - the Shifting Gymnasion Agonon
            1126, // The Aetherfont - the Aetherfont
            1136, // The Gilded Araya - the Gilded Araya
            1137, // Mount Rokkon - Mount Rokkon
            1140, // The Voidcast Dais - the Voidcast Dais
            1141, // The Voidcast Dais - the Voidcast Dais (Extreme)
            1142, // The Sirensong Sea - the Sirensong Sea
            1143, // Bardam's Mettle - Bardam's Mettle
            1144, // Doma Castle - Doma Castle
            1145, // Castrum Abania - Castrum Abania
            1146, // Ala Mhigo - Ala Mhigo
            1147, // The Aetherial Slough - Anabaseios: The Ninth Circle
            1148, // The Aetherial Slough - Anabaseios: The Ninth Circle (Savage)
            1149, // The Dæmons' Nest - Anabaseios: The Tenth Circle
            1150, // The Dæmons' Nest - Anabaseios: The Tenth Circle (Savage)
            1151, // The Chamber of Fourteen - Anabaseios: The Eleventh Circle
            1152, // The Chamber of Fourteen - Anabaseios: The Eleventh Circle (Savage)
            1153, // Ascension - Anabaseios: The Twelfth Circle
            1154, // Ascension - Anabaseios: The Twelfth Circle (Savage)
            1155, // Another Mount Rokkon - Another Mount Rokkon
            1156, // Another Mount Rokkon - Another Mount Rokkon (Savage)
            1163, // The Endeavor - Ocean Fishing
            1164, // The Lunar Subterrane - the Lunar Subterrane
            1167, // Ihuykatumu - Ihuykatumu
            1168, // The Abyssal Fracture - the Abyssal Fracture
            1169, // The Abyssal Fracture - the Abyssal Fracture (Extreme)
            1172, // The Drowned City of Skalla - the Drowned City of Skalla
            1173, // The Burn - the Burn
            1174, // The Ghimlyt Dark - the Ghimlyt Dark
            1176, // Aloalo Island - Aloalo Island
            1178, // Thaleia - Thaleia
            1179, // Another Aloalo Island - Another Aloalo Island
            1180, // Another Aloalo Island - Another Aloalo Island (Savage)
            1193, // Worqor Zormor - Worqor Zormor
            1194, // The Skydeep Cenote - the Skydeep Cenote
            1195, // Worqor Lar Dor - Worqor Lar Dor
            1196, // Worqor Lar Dor - Worqor Lar Dor (Extreme)
            1198, // Vanguard - Vanguard
            1199, // Alexandria - Alexandria
            1200, // Summit of Everkeep - Everkeep
            1201, // Summit of Everkeep - Everkeep (Extreme)
            1202, // Interphos - the Interphos
            1203, // Tender Valley - Tender Valley
            1204, // Strayborough - The Strayborough Deadwalk
            1208, // Origenics - Origenics
            1209, // Cenote Ja Ja Gural - Cenote Ja Ja Gural
            1225, // Scratching Ring - AAC Light-heavyweight M1
            1226, // Scratching Ring - AAC Light-heavyweight M1 (Savage)
            1227, // Lovely Lovering - AAC Light-heavyweight M2
            1228, // Lovely Lovering - AAC Light-heavyweight M2 (Savage)
            1229, // Blasting Ring - AAC Light-heavyweight M3
            1230, // Blasting Ring - AAC Light-heavyweight M3 (Savage)
            1231, // The Thundering - AAC Light-heavyweight M4
            1232, // The Thundering - AAC Light-heavyweight M4 (Savage)
            1238, // A Future Rewritten - Futures Rewritten (Ultimate)
            1241, // Sphere of Naught - the Cloud of Darkness (Chaotic)
            1242, // Yuweyawata - Yuweyawata Field Station
            1243, // Interphos - the Minstrel's Ballad: Sphene's Burden
            1245, // Halatali - Halatali
            1248, // Jeuno: The First Walk - Jeuno: The First Walk
            1256, // Groovy Ring - AAC Cruiserweight M1
            1257, // Groovy Ring - AAC Cruiserweight M1 (Savage)
            1258, // Rebel Ring - AAC Cruiserweight M2
            1259, // Rebel Ring - AAC Cruiserweight M2 (Savage)
            1260, // Demolition Site - AAC Cruiserweight M3
            1261, // Demolition Site - AAC Cruiserweight M3 (Savage)
            1262, // Hunter's Ring - AAC Cruiserweight M4
            1263, // Hunter's Ring - AAC Cruiserweight M4 (Savage)
            1266, // The Underkeep - the Underkeep
            1267, // The Sunken Temple of Qarn - the Sunken Temple of Qarn
            1270, // Recollection - Recollection
            1271, // Recollection - Recollection (Extreme)
            1273, // Carteneau Flats: Borderland Ruins - the Borderland Ruins (Secure)
            1279, // Vault Oneiron - Vault Oneiron
            1281, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 1-10)
            1282, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 11-20)
            1283, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 21-30)
            1284, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 31-40)
            1285, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 41-50)
            1286, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 51-60)
            1287, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 61-70)
            1288, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 71-80)
            1289, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 81-90)
            1290, // Pilgrim's Traverse - Pilgrim's Traverse (Stones 91-100)
            1292, // The Meso Terminal - the Meso Terminal
            1295, // The Ageless Necropolis - the Ageless Necropolis
            1296, // The Ageless Necropolis - the Minstrel's Ballad: Necron's Embrace
            1298, // The Pâtisserie - the Pâtisserie
            1300, // The Windward Wilds - the Windward Wilds
            1303, // Cutter's Cry - Cutter's Cry
            1304, // San d'Oria: The Second Walk - San d'Oria: The Second Walk
            1306, // The Windward Wilds - the Windward Wilds (Extreme)
            1307, // Hell on Rails - Hell on Rails
            1308, // Hell on Rails - Hell on Rails (Extreme)
            1311, // Pilgrim's Traverse - the Final Verse (Quantum)
            1313, // Worqor Chirteh - Worqor Chirteh (Triumph)
            1314, // Mistwake - Mistwake
            1315, // The Merchant's Tale - The Merchant's Tale
            1316, // The Merchant's Tale - The Merchant's Tale (Advanced)
            1317, // Another Merchant's Tale - Another Merchant's Tale
            1320, // Ring Noir - AAC Heavyweight M1
            1321, // Ring Noir - AAC Heavyweight M1 (Savage)
            1322, // The X-Ring - AAC Heavyweight M2
            1323, // The X-Ring - AAC Heavyweight M2 (Savage)
            1324, // The Crown - AAC Heavyweight M3
            1325, // The Crown - AAC Heavyweight M3 (Savage)
            1326, // Arcadia - AAC Heavyweight M4
            1327, // Arcadia - AAC Heavyweight M4 (Savage)
            1330, // Dzemael Darkhold - Dzemael Darkhold
            1331, // Aurum Vale - the Aurum Vale
            1333, // Pilgrim's Traverse - the Final Verse
            1345, // The Clyteum - the Clyteum
            1361, // The Unmaking - the Unmaking
            1362, // The Unmaking - the Unmaking (Extreme)
            1366, // Dusk Vigil - the Dusk Vigil
            1367, // Shisui of the Violet Tides - Shisui of the Violet Tides
            1368, // Windurst: The Third Walk - Windurst: The Third Walk
            1372, // Transparency - Shinryu's Domain (Unreal)

            // === PublicContent (18) ===
            579, // The Battlehall - The Triple Triad Battlehall
            732, // Eureka Anemos - the Forbidden Land, Eureka Anemos
            763, // Eureka Pagos - the Forbidden Land, Eureka Pagos
            790, // Ul'dah - Steps of Nald - the Calamity Retold
            792, // The Fall of Belah'dia - Leap of Faith
            795, // Eureka Pyros - the Forbidden Land, Eureka Pyros
            827, // Eureka Hydatos - the Forbidden Land, Eureka Hydatos
            899, // The Falling City of Nym - Leap of Faith
            901, // The Diadem - the Diadem
            920, // Bozjan Southern Front - the Bozjan Southern Front
            929, // The Diadem - the Diadem
            936, // Delubrum Reginae - Delubrum Reginae
            937, // Delubrum Reginae - Delubrum Reginae (Savage)
            939, // The Diadem - the Diadem
            975, // Zadnor - Zadnor
            1098, // Sylphstep - Leap of Faith
            1165, // Blunderville - Blunderville
            1252, // South Horn - the Occult Crescent: South Horn

            // === Other CFC - PartyContent/GoldSaucer (10) ===
            149, // The Feasting Grounds (old PvP)
            512, // The Diadem (Easy)
            514, // The Diadem
            515, // The Diadem (Hard)
            589, // Chocobo Square - LoVM: Player Battle (RP)
            590, // Chocobo Square - LoVM: Tournament
            591, // Chocobo Square - LoVM: Player Battle (Non-RP)
            624, // The Diadem - Hunting Grounds (Easy)
            625, // The Diadem - Hunting Grounds
            656, // The Diadem - Trials of the Matron
        ];
    }
}