using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum Cell : byte { Ground, Path, Water, Lava, Wall, Plateau, Hub }

    // Deterministic infinite world: everything about a position comes from the world seed.
    // Difficulty grows with distance from the village at the origin.
    public static class WorldGen
    {
        public const int ChunkSize = 24;
        public const float HubRadius = 48f;     // the village; no monsters inside
        public const float SafeRadius = 62f;    // no camps this close
        public const float PlateauHeight = 2.4f; // cliffs rise toward -Z

        public static int Seed = 20261028;

        // The copied village's area; generated terrain stays out of it.
        public static RectInt HubRect = new RectInt(-45, -30, 90, 90);

        // ---------- Difficulty ----------

        public static float DistanceFromHub(Vector2 p) => p.magnitude;

        public static int LevelAt(Vector2 p) => Mathf.Max(1, 1 + Mathf.FloorToInt(Mathf.Max(0f, p.magnitude - HubRadius) / 22f));

        public static float EliteChance(Vector2 p) => Mathf.Min(0.35f, 0.08f + p.magnitude * 0.0004f);
        public static float RareChance(Vector2 p) => Mathf.Min(0.22f, 0.03f + p.magnitude * 0.0002f);

        // Better items further out (added to rarity rolls).
        public static float MagicFind(int monsterLevel) => Mathf.Min(1.5f, monsterLevel * 0.012f);

        // ---------- Noise ----------

        static float Noise(float x, float y, float scale, int salt)
        {
            float ox = (Hash(salt, 0) % 10000) * 0.731f, oy = (Hash(salt, 1) % 10000) * 0.517f;
            return Mathf.PerlinNoise(x * scale + ox, y * scale + oy);
        }

        public static uint Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)Seed * 2654435761u;
                h ^= (uint)a * 2246822519u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)b * 3266489917u;
                h *= 668265263u;
                h ^= h >> 15;
                return h;
            }
        }

        public static float Hash01(int a, int b) => (Hash(a, b) & 0xFFFFFF) / (float)0x1000000;

        // ---------- Biomes ----------

        public static Biome BiomeAt(Vector2 p)
        {
            float d = p.magnitude;
            if (d < HubRadius) return Biome.Village;
            if (d < 130f) return Biome.Forest; // a gentle first ring
            float heat = Noise(p.x, p.y, 0.0035f, 11) + (d - 130f) * 0.0006f;
            float wet = Noise(p.x, p.y, 0.0042f, 12);
            if (heat > 0.66f) return Biome.Ember;
            if (wet > 0.58f) return Biome.Marsh;
            if (heat < 0.42f && wet < 0.45f) return Biome.Ruins;
            return Biome.Forest;
        }

        // ---------- Cells ----------

        public static bool IsHub(int x, int y) => HubRect.Contains(new Vector2Int(x, y));

        public static Cell CellAt(int x, int y)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = p.magnitude;
            if (IsHub(x, y)) return Cell.Hub;

            // Roads: ridges of low-frequency noise make long meandering trails.
            float r = Noise(x, y, 0.018f, 21);
            float r2 = Noise(x, y, 0.011f, 22);
            bool path = Mathf.Abs(r - 0.5f) < 0.022f || Mathf.Abs(r2 - 0.5f) < 0.016f;
            // Four roads leave the village so you never start lost.
            if (d < 140f && (Mathf.Abs(x) < 2 || Mathf.Abs(y) < 2)) path = true;
            if (path) return Cell.Path;

            var biome = BiomeAt(p);
            float blob = Noise(x, y, 0.07f, 31);
            float height = Noise(x, y, 0.035f, 41);
            if (d > HubRadius + 12f && height > 0.72f) return Cell.Plateau;
            switch (biome)
            {
                case Biome.Marsh: if (blob > 0.64f) return Cell.Water; break;
                case Biome.Ember: if (blob > 0.68f) return Cell.Lava; break;
                case Biome.Ruins:
                    // Broken straight walls on a coarse grid.
                    if ((x % 13 == 0 || y % 11 == 0) && Noise(x, y, 0.2f, 33) > 0.55f) return Cell.Wall;
                    break;
            }
            return Cell.Ground;
        }

        public static bool Walkable(Cell c) => c == Cell.Ground || c == Cell.Path || c == Cell.Hub;

        // ---------- Regions (for monster species) ----------

        public const int RegionSize = 96;

        static readonly Dictionary<long, ZoneDefinition> regions = new Dictionary<long, ZoneDefinition>();
        static readonly string[] placeA = { "Whispering", "Hollow", "Ashen", "Sunken", "Weeping", "Crimson", "Silent", "Forgotten", "Howling", "Gloomy", "Burning", "Broken", "Shivering", "Rotting", "Thorned", "Sorrow" };
        static readonly Dictionary<Biome, string[]> placeB = new Dictionary<Biome, string[]>
        {
            { Biome.Forest, new[] { "Woods", "Glade", "Thicket", "Grove", "Wilds", "Vale" } },
            { Biome.Marsh, new[] { "Bog", "Mire", "Fen", "Marsh", "Swamp", "Mud Flats" } },
            { Biome.Ruins, new[] { "Ruins", "Halls", "Necropolis", "Citadel", "Crypts", "Remnants" } },
            { Biome.Ember, new[] { "Wastes", "Cinders", "Caldera", "Ashlands", "Pits", "Furnace" } },
            { Biome.Village, new[] { "Village" } },
        };

        // A region is a patch of the map with its own monster species and name.
        public static ZoneDefinition RegionAt(Vector2 p)
        {
            var biome = BiomeAt(p);
            if (biome == Biome.Village) return GameDatabase.Instance.FindZone("village");
            int rx = Mathf.FloorToInt(p.x / RegionSize), ry = Mathf.FloorToInt(p.y / RegionSize);
            long key = ((long)rx << 32) ^ (uint)ry ^ ((long)biome << 60);
            if (regions.TryGetValue(key, out var z) && z != null) return z;

            var template = GameDatabase.Instance.zones.Find(t => t != null && t.biome == biome) ?? GameDatabase.Instance.FindZone("forest");
            z = Object.Instantiate(template);
            uint h = Hash(rx * 7919 + (int)biome, ry * 104729);
            z.id = $"r_{rx}_{ry}_{biome}";
            z.seed = (int)(h & 0x7FFFFFFF);
            var names = placeB[biome];
            z.displayName = $"The {placeA[h % placeA.Length]} {names[(h >> 8) % names.Length]}";
            Vector2 center = new Vector2((rx + 0.5f) * RegionSize, (ry + 0.5f) * RegionSize);
            int lvl = LevelAt(center);
            z.minLevel = Mathf.Max(1, lvl - 3);
            z.maxLevel = lvl + 3;
            z.speciesCount = 4;
            z.safeZone = false;
            regions[key] = z;
            return z;
        }

        public static string RegionTitle(Vector2 p)
        {
            var z = RegionAt(p);
            return z != null ? z.displayName : "";
        }

        // ---------- Points of interest ----------

        public const int WaypointGrid = 7;   // chunks between waypoints
        public const int BossGrid = 11;      // chunks between possible boss gates

        public static bool HasWaypoint(int cx, int cy, out Vector2Int cell)
        {
            cell = default;
            if (Mod(cx, WaypointGrid) != WaypointGrid / 2 || Mod(cy, WaypointGrid) != WaypointGrid / 2) return false;
            var center = new Vector2Int(cx * ChunkSize + ChunkSize / 2, cy * ChunkSize + ChunkSize / 2);
            if (((Vector2)center).magnitude < HubRadius + 20f) return false;
            cell = center;
            return true;
        }

        public static bool HasBossGate(int cx, int cy, out Vector2Int cell)
        {
            cell = default;
            if (Mod(cx + 3, BossGrid) != 0 || Mod(cy + 5, BossGrid) != 0) return false;
            var center = new Vector2Int(cx * ChunkSize + ChunkSize / 2 + 4, cy * ChunkSize + ChunkSize / 2 - 3);
            if (((Vector2)center).magnitude < 220f) return false;
            cell = center;
            return true;
        }

        public static int Mod(int a, int m) => ((a % m) + m) % m;

        public static Vector2Int ChunkOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.y / ChunkSize));
    }
}
