using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum DungeonStyle { Halls, Caves, Keep }

    // One kind of dungeon: how it is laid out, what it looks like and what lives in it.
    // A gate always holds the same kind, picked from the ones that suit the land around it.
    public class DungeonVariant
    {
        public string kind, tagline, wall, floor;
        public string[] nouns;

        // Layout
        public DungeonStyle style = DungeonStyle.Halls;
        public int minRooms = 7, maxRooms = 12, levelsPerRoom = 10;
        public Vector2Int roomWidth = new Vector2Int(12, 20), roomHeight = new Vector2Int(11, 17);
        public int roomGap = 5, corridorWidth = 3, extraLinks = 1;
        public float wallHeight = 3.4f;
        public bool pillars;

        // Light
        public Color ambient = new Color(0.3f, 0.3f, 0.38f);
        public Color torch = new Color(1f, 0.66f, 0.38f), flame = new Color(1f, 0.65f, 0.25f, 0.9f);
        public float torchRange = 12f, torchIntensity = 4f;
        public int torchSpacing = 7;

        // Monsters and treasure
        public MonsterArchetype[] roles;    // null: everything that lives in the area
        public float rareChance = 0.2f, eliteChance = 0.4f, warbandChance = -1f;
        public int areaPerPack = 80, minPacks = 2, maxPacks = 4, extraChests;
        public float spawnDistance = 15f;
        public string wardenTitle = "Warden";
        public MonsterArchetype wardenRole = MonsterArchetype.Brute;

        public static readonly DungeonVariant Crypt = new DungeonVariant
        {
            kind = "Crypt",
            tagline = "Torchlit halls and restless guards",
            wall = "dgn_crypt_wall",
            floor = "dgn_crypt_floor",
            nouns = new[] { "Crypt", "Barrow", "Catacombs" },
            pillars = true,
        };

        public static readonly DungeonVariant Cavern = new DungeonVariant
        {
            kind = "Cavern",
            tagline = "Winding caves lit by pale fungus",
            wall = "dgn_cavern_wall",
            floor = "dgn_cavern_floor",
            nouns = new[] { "Hollow", "Caves", "Grotto" },
            style = DungeonStyle.Caves,
            minRooms = 8,
            roomGap = 4,
            corridorWidth = 4,
            extraLinks = 2,
            wallHeight = 3f,
            ambient = new Color(0.34f, 0.4f, 0.42f),
            torch = new Color(0.45f, 0.95f, 0.85f),
            flame = new Color(0.5f, 1f, 0.85f, 0.85f),
            torchRange = 14f,
            torchIntensity = 5f,
            torchSpacing = 6,
            roles = new[] { MonsterArchetype.Chaser, MonsterArchetype.Swarmling, MonsterArchetype.Charger, MonsterArchetype.Brute },
            rareChance = 0.15f,
            eliteChance = 0.3f,
            areaPerPack = 70,
            wardenTitle = "Alpha",
        };

        public static readonly DungeonVariant Warren = new DungeonVariant
        {
            kind = "Warren",
            tagline = "A maze of small rooms, with more to find",
            wall = "dgn_warren_wall",
            floor = "dgn_warren_floor",
            nouns = new[] { "Warren", "Burrows", "Tunnels" },
            minRooms = 12,
            maxRooms = 18,
            levelsPerRoom = 8,
            roomWidth = new Vector2Int(8, 13),
            roomHeight = new Vector2Int(7, 12),
            roomGap = 3,
            extraLinks = 4,
            wallHeight = 2.6f,
            ambient = new Color(0.32f, 0.28f, 0.26f),
            torchRange = 10f,
            torchIntensity = 3.4f,
            roles = new[] { MonsterArchetype.Swarmling, MonsterArchetype.Skirmisher, MonsterArchetype.Exploder, MonsterArchetype.Chaser },
            rareChance = 0.15f,
            eliteChance = 0.3f,
            areaPerPack = 50,
            minPacks = 1,
            maxPacks = 2,
            extraChests = 1,
            spawnDistance = 10f,
            wardenTitle = "Overseer",
            wardenRole = MonsterArchetype.Skirmisher,
        };

        public static readonly DungeonVariant Stronghold = new DungeonVariant
        {
            kind = "Stronghold",
            tagline = "Ordered halls held by elite soldiers",
            wall = "dgn_keep_wall",
            floor = "dgn_keep_floor",
            nouns = new[] { "Keep", "Bastion", "Hold" },
            style = DungeonStyle.Keep,
            wallHeight = 4f,
            pillars = true,
            ambient = new Color(0.32f, 0.33f, 0.4f),
            torch = new Color(1f, 0.74f, 0.48f),
            torchIntensity = 4.6f,
            torchSpacing = 5,
            roles = new[] { MonsterArchetype.Guardian, MonsterArchetype.Spitter, MonsterArchetype.Brute, MonsterArchetype.Caster },
            rareChance = 0.15f,
            eliteChance = 0.55f,
            warbandChance = 0.3f,
            areaPerPack = 100,
            minPacks = 1,
            maxPacks = 3,
            wardenTitle = "Commander",
            wardenRole = MonsterArchetype.Guardian,
        };

        public static readonly DungeonVariant Forge = new DungeonVariant
        {
            kind = "Forge",
            tagline = "Huge burning chambers, thick with rares",
            wall = "dgn_forge_wall",
            floor = "dgn_forge_floor",
            nouns = new[] { "Forge", "Foundry", "Furnace" },
            minRooms = 6,
            maxRooms = 9,
            levelsPerRoom = 12,
            roomWidth = new Vector2Int(17, 27),
            roomHeight = new Vector2Int(15, 21),
            corridorWidth = 4,
            ambient = new Color(0.36f, 0.24f, 0.2f),
            torch = new Color(1f, 0.42f, 0.16f),
            flame = new Color(1f, 0.42f, 0.1f, 0.95f),
            torchIntensity = 4.8f,
            torchSpacing = 6,
            roles = new[] { MonsterArchetype.Charger, MonsterArchetype.Caster, MonsterArchetype.Exploder, MonsterArchetype.Brute },
            rareChance = 0.35f,
            eliteChance = 0.35f,
            areaPerPack = 110,
            wardenTitle = "Forgemaster",
        };

        public static readonly DungeonVariant Sunken = new DungeonVariant
        {
            kind = "Sunken Halls",
            tagline = "Broad, dripping passages and things that spit",
            wall = "dgn_sunken_wall",
            floor = "dgn_sunken_floor",
            nouns = new[] { "Cistern", "Vault", "Cellars" },
            corridorWidth = 5,
            extraLinks = 2,
            pillars = true,
            ambient = new Color(0.22f, 0.32f, 0.34f),
            torch = new Color(0.55f, 0.9f, 0.72f),
            flame = new Color(0.6f, 1f, 0.7f, 0.85f),
            torchRange = 13f,
            torchIntensity = 3.6f,
            roles = new[] { MonsterArchetype.Spitter, MonsterArchetype.Caster, MonsterArchetype.Chaser, MonsterArchetype.Swarmling },
            wardenTitle = "Drowned Lord",
        };

        static readonly Dictionary<Biome, (DungeonVariant variant, int weight)[]> byBiome = new Dictionary<Biome, (DungeonVariant, int)[]>
        {
            { Biome.Forest, new[] { (Cavern, 4), (Warren, 3), (Crypt, 2) } },
            { Biome.Marsh, new[] { (Sunken, 5), (Cavern, 2), (Crypt, 2) } },
            { Biome.Ruins, new[] { (Crypt, 4), (Stronghold, 4), (Warren, 2) } },
            { Biome.Ember, new[] { (Forge, 5), (Stronghold, 2), (Cavern, 2) } },
        };

        static readonly Dictionary<Biome, string[]> adjectives = new Dictionary<Biome, string[]>
        {
            { Biome.Forest, new[] { "Rootbound", "Mossy", "Thorned" } },
            { Biome.Marsh, new[] { "Drowned", "Mirewater", "Sunken" } },
            { Biome.Ruins, new[] { "Forgotten", "Silent", "Ashen" } },
            { Biome.Ember, new[] { "Cinder", "Scorched", "Burning" } },
        };

        static Biome Known(Biome biome) => byBiome.ContainsKey(biome) ? biome : Biome.Ruins;

        public static DungeonVariant For(Biome biome, int seed)
        {
            var table = byBiome[Known(biome)];
            int total = 0;
            foreach (var entry in table) total += entry.weight;
            int roll = new System.Random(seed ^ 0x2F6E1).Next(total);
            foreach (var entry in table)
            {
                roll -= entry.weight;
                if (roll < 0) return entry.variant;
            }
            return table[0].variant;
        }

        public string NameFor(Biome biome, int seed)
        {
            var r = new System.Random(seed ^ 0x51C3A);
            var words = adjectives[Known(biome)];
            return $"The {words[r.Next(words.Length)]} {nouns[r.Next(nouns.Length)]}";
        }

        public int RoomCount(int level) => Mathf.Clamp(minRooms + level / levelsPerRoom, minRooms, maxRooms);

        // The area's monsters that fit this kind of dungeon, or all of them if too few do.
        public List<MonsterSpecies> Roster(List<MonsterSpecies> all)
        {
            if (roles == null) return all;
            var picked = all.FindAll(s => System.Array.IndexOf(roles, s.archetype) >= 0);
            return picked.Count >= 2 ? picked : all;
        }
    }
}
