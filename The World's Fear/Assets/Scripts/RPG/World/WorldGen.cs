using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // CastleFloor/CastleWall/Tower: the open castles on the overworld (appended, so saved numbers keep their meaning).
    public enum Cell : byte { Ground, Path, Water, Lava, Wall, Plateau, Hub, CastleFloor, CastleWall, Tower, Puddle, Stairs, Pillar, Bridge }

    // Deterministic infinite world: everything about a position comes from the hero's world seed.
    // Difficulty grows with distance from the village at the origin.
    public static partial class WorldGen
    {
        public const int ChunkSize = 24;
        public const float HubRadius = 48f;     // the village; no monsters inside
        public const float SafeRadius = 62f;    // no camps this close
        public const float PlateauHeight = 2.4f; // the old single plateau: level 2 (cliffs rise toward -Z)

        // Saves from before each hero had their own world all live in this one.
        public const int LegacySeed = 20261028;

        public static int Seed { get; private set; } = LegacySeed;

        // How far this world slides the grids its landmarks sit on.
        static Vector2Int waypointShift, chestShift;
        static int bossSalt;

        public static void SetSeed(int seed)
        {
            Seed = seed;
            ClearTerrainCache();
            regions.Clear();
            castles.Clear();
            stairBlocks.Clear();
            vertexHeights.Clear();
            fortresses.Clear();
            bool legacy = seed == LegacySeed;
            waypointShift = legacy ? Vector2Int.zero : GridShift(51, WaypointGrid);
            chestShift = legacy ? Vector2Int.zero : GridShift(53, ChestGrid);
            if (legacy) return;
            // Every other lair stays sealed until the first boss is slain, so a layout has to have a gate at its distance.
            bossSalt = 55;
            while (!HasFirstBossGate()) bossSalt += 2;
        }

        static Vector2Int GridShift(int salt, int grid) =>
            new Vector2Int((int)(Hash(salt, grid) % (uint)grid), (int)(Hash(salt + 1, grid) % (uint)grid));

        static bool HasFirstBossGate()
        {
            const int reach = 24; // chunks; well past the first boss's distance band
            for (int cx = -reach; cx <= reach; cx++)
            for (int cy = -reach; cy <= reach; cy++)
                if (HasBossGate(cx, cy, out var cell) && WorldManager.KindForGate(cell) == WorldManager.BossOrder[0]) return true;
            return false;
        }

        // The copied village's area; generated terrain stays out of it.
        public static RectInt HubRect = new RectInt(-48, -48, 96, 96);

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
            // A boss's fortress stands in that boss's own land: its biome reaches about 40 tiles round it.
            int span = BossGrid * ChunkSize;
            if (d > 150f && FortressInPatch(Mathf.FloorToInt(p.x / span), Mathf.FloorToInt(p.y / span), out var fort)
                && (p - new Vector2(fort.gate.x, fort.gate.y + 4f)).sqrMagnitude < 40f * 40f) return BossBiome(fort.kind);
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
            var c = BaseCell(x, y);
            // The ground round a grassy slope bends up to meet it, and a puddle can't: there it is plain ground (high
            // ground up on a hill, like the rest of it).
            if (c == Cell.Puddle && NextToSlope(x, y)) return ElevAt(x, y) > 0 ? Cell.Plateau : Cell.Ground;
            return (c == Cell.Ground || c == Cell.Path || c == Cell.Plateau) && StairAt(x, y, out _, out _) ? Cell.Stairs : c;
        }

        static bool NextToSlope(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var b = BaseCell(x + dx, y + dy);
                if ((b == Cell.Ground || b == Cell.Path || b == Cell.Plateau) && StairAt(x + dx, y + dy, out var st, out _) && st.natural) return true;
            }
            return false;
        }

        // The cell before stairs are laid against the plateaus (cached per chunk, see WorldGenTerrain).
        static Cell BaseCell(int x, int y)
        {
            var c = CacheFor(x, y, out int i);
            if (c.cell[i] == 255) c.cell[i] = (byte)BaseCellRaw(x, y);
            return (Cell)c.cell[i];
        }

        static Cell BaseCellRaw(int x, int y) => SpanLevel(x, y) > 0 ? Cell.Bridge : LandCell(x, y);

        // What the land itself holds (under a high bridge, what it crosses).
        public static Cell LandCell(int x, int y)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = p.magnitude;
            if (IsHub(x, y)) return Cell.Hub;
            if (d > HubRadius && FortressCell(x, y, out var fortCell)) return fortCell;
            if (d > HubRadius && CastleCell(x, y, out var castleCell)) return castleCell;
            if (InFarmland(x, y)) return Cell.Ground;   // farms: flat and clear (WorldGenTerrain)

            // Roads: ridges of low-frequency noise make long meandering trails.
            float r = Noise(x, y, 0.018f, 21);
            float r2 = Noise(x, y, 0.011f, 22);
            bool path = Mathf.Abs(r - 0.5f) < 0.022f || Mathf.Abs(r2 - 0.5f) < 0.016f;
            // Four roads leave the village so you never start lost.
            if (d < 140f && (Mathf.Abs(x) < 2 || Mathf.Abs(y) < 2)) path = true;

            if (BridgeApproach(x, y)) path = true;   // the path led along the bank to a bridge's end
            // Rivers on the low ground, swelling into lakes; a path crosses on a bridge.
            if (RiverAt(x, y)) return BridgeCrosses(x, y) ? Cell.Bridge : Cell.Water;

            // Canyons through the high ground: water on the floor, lava in the ash wastes.
            if (CanyonAt(x, y)) return BiomeAt(p) == Biome.Ember ? Cell.Lava : Cell.Water;

            // Raised land: hills, mesas, volcanoes and mountain ranges (see WorldGenTerrain).
            int elev = ElevRaw(x, y);
            if (elev > 0) return RaisedCell(x, y, elev, path);
            if (path) return Cell.Path;
            if (PlungePool(x, y)) return Cell.Water;
            var biome = BiomeAt(p);
            float blob = Noise(x, y, 0.07f, 31);
            // Ponds and small lakes everywhere but the ash lands (deep water: blocks), and shallow puddles you
            // splash through. The ponds keep away from the roads; puddles may lie on them.
            if (biome != Biome.Ember && d > HubRadius + 8f && !InFarmland(x, y))
            {
                float pond = Noise(x, y, 0.06f, 51) * 0.75f + Noise(x, y, 0.19f, 52) * 0.25f;
                if (pond > (biome == Biome.Marsh ? 0.70f : 0.745f)) return Cell.Water;
                if (pond > (biome == Biome.Marsh ? 0.66f : 0.715f)) return Cell.Puddle;          // a shallow rim round a pond
                if (Noise(x, y, 0.33f, 53) > 0.83f && Noise(x, y, 0.05f, 54) > 0.5f) return Cell.Puddle;   // rain puddles
            }
            switch (biome)
            {
                case Biome.Marsh: if (blob > 0.64f) return Cell.Water; break;
                case Biome.Ember: if (blob > 0.68f) return Cell.Lava; break;
                case Biome.Ruins:
                    // Broken straight walls on a coarse grid: stretches of at least three tiles (a lone tile of wall
                    // is a square block standing about, not a ruin).
                    if ((x % 13 == 0 && WallRun(x, y, 0, 1)) || (y % 11 == 0 && WallRun(x, y, 1, 0))) return Cell.Wall;
                    break;
            }
            return Cell.Ground;
        }

        // Is this tile part of three or more wall tiles in a row along (dx, dy)?
        static bool WallRun(int x, int y, int dx, int dy)
        {
            bool W(int k) => Noise(x + dx * k, y + dy * k, 0.2f, 33) > 0.55f;
            if (!W(0)) return false;
            return (W(-1) && W(1)) || (W(-1) && W(-2)) || (W(1) && W(2));
        }

        public static bool Walkable(Cell c) => c == Cell.Ground || c == Cell.Path || c == Cell.Hub || c == Cell.CastleFloor || c == Cell.Puddle || c == Cell.Bridge;

        // Raised and blocking: drawn as blocks with a lit rim on top and a shadow at the foot.
        public static bool Solid(Cell c) => c == Cell.Plateau || c == Cell.Wall || c == Cell.CastleWall || c == Cell.Tower || c == Cell.Pillar;

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
            z.id = $"r_{Seed}_{rx}_{ry}_{biome}"; // species are cached by id, so it has to differ between worlds
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
            if (Mod(cx + waypointShift.x, WaypointGrid) != WaypointGrid / 2 || Mod(cy + waypointShift.y, WaypointGrid) != WaypointGrid / 2) return false;
            var center = new Vector2Int(cx * ChunkSize + ChunkSize / 2, cy * ChunkSize + ChunkSize / 2);
            if (((Vector2)center).magnitude < HubRadius + 20f) return false;
            cell = center;
            return true;
        }

        // A boss gate is now the gate cell of the patch's fortress.
        public static bool HasBossGate(int cx, int cy, out Vector2Int cell)
        {
            cell = default;
            int px = Mathf.FloorToInt(cx / (float)BossGrid), py = Mathf.FloorToInt(cy / (float)BossGrid);
            if (!FortressInPatch(px, py, out var f) || ChunkOf(f.gate) != new Vector2Int(cx, cy)) return false;
            cell = f.gate;
            return true;
        }

        // ---------- Boss fortresses ----------
        // One per patch of the map, on high ground in the boss's own biome. Which boss holds it follows the distance
        // from the village (WorldManager.KindForGate), so the order you meet them in stays the same.
        //   arena (26 x 22, corners cut)   the boss's ground: it cannot leave it
        //   neck  (4 x 2)                  the gate between them, opened by killing the generals
        //   terrace (16 x 8)               where the generals stand
        //   stairs (2 x 3)                 the only way up, sealed until the boss before has been slain
        public struct Fortress
        {
            public Vector2Int gate;      // the first arena row, in the middle
            public BossKind kind;
            public RectInt Arena => new RectInt(gate.x - 13, gate.y, 26, 22);
            public RectInt Neck => new RectInt(gate.x - 2, gate.y - 2, 4, 2);
            public RectInt Terrace => new RectInt(gate.x - 8, gate.y - 10, 16, 8);
            public Vector2Int StairA => new Vector2Int(gate.x - 1, gate.y - 10 - StairLength);
            public RectInt Bounds => new RectInt(gate.x - 17, gate.y - 18, 34, 44);
            public Vector2 ArenaCentre => new Vector2(gate.x, gate.y + 11f);
            public Vector2 TerraceCentre => new Vector2(gate.x, gate.y - 6f);

            // Every boss has its own arena, cut from the same 26 x 22 block of high ground:
            //   Slime     a round basin with a ring of shallow puddles
            //   Spider    an irregular forest clearing
            //   Zombie    a wide bog with two sunken pools
            //   Skeleton  a stone hall with cut corners and six pillars
            //   Fear      a cross-shaped throne floor, lava in the four corners, eight tall pillars
            public bool InArena(int x, int y) => Floor(x, y) && !IsPillar(x, y) && Pool(x, y) == Cell.Ground;

            bool InRect(int x, int y) { var a = Arena; return x >= a.xMin && x < a.xMax && y >= a.yMin && y < a.yMax; }

            // -1..1 across the block
            float U(int x) => (x + 0.5f - gate.x) / 13f;
            float V(int y) => (y + 0.5f - (gate.y + 11f)) / 11f;

            // The way in from the gate is always floor.
            bool Entry(int x, int y) => x >= gate.x - 2 && x < gate.x + 2 && y >= gate.y && y < gate.y + 5;

            bool Floor(int x, int y)
            {
                if (!InRect(x, y)) return false;
                if (Entry(x, y)) return true;
                float u = U(x), v = V(y);
                switch (kind)
                {
                    case BossKind.Slime: return u * u + v * v < 0.93f * 0.93f;
                    case BossKind.Spider:
                    {
                        float ang = Mathf.Atan2(v, u);
                        float r = 0.84f + 0.11f * Mathf.Sin(3f * ang + 1.3f) + 0.06f * Mathf.Sin(5f * ang + 0.4f);
                        return u * u + v * v < r * r;
                    }
                    case BossKind.Zombie: return Mathf.Pow(Mathf.Abs(u), 3f) + Mathf.Pow(Mathf.Abs(v), 3f) < 0.9f;
                    case BossKind.WorldsFear: return Mathf.Abs(u) < 0.4f || Mathf.Abs(v) < 0.42f;
                    default:
                    {
                        var a = Arena;
                        int cx = Mathf.Min(x - a.xMin, a.xMax - 1 - x), cy = Mathf.Min(y - a.yMin, a.yMax - 1 - y);
                        return cx + cy >= 5;
                    }
                }
            }

            // Sunken pools inside the block (low ground again, below the arena's edge): water for the bog, lava
            // round the throne floor. Cell.Ground = no pool here.
            public Cell Pool(int x, int y)
            {
                if (!InRect(x, y) || Entry(x, y)) return Cell.Ground;
                float u = U(x), v = V(y);
                if (kind == BossKind.WorldsFear) return Mathf.Abs(u) < 0.4f || Mathf.Abs(v) < 0.42f ? Cell.Ground : Cell.Lava;
                if (kind == BossKind.Zombie)
                {
                    float a = (Mathf.Abs(u) - 0.5f) / 0.17f, b = (v - 0.1f) / 0.36f;
                    return a * a + b * b < 1f ? Cell.Water : Cell.Ground;
                }
                return Cell.Ground;
            }

            // Shallow water on the arena floor (walked through).
            public bool IsPuddle(int x, int y)
            {
                if (!InArena(x, y)) return false;
                float u = U(x), v = V(y), r = Mathf.Sqrt(u * u + v * v);
                if (kind == BossKind.Slime) return r > 0.5f && r < 0.66f && (Hash(x * 3 + 1, y * 7 + 5) % 5) < 3;
                if (kind == BossKind.Zombie) return (Hash(x / 2 * 5 + 3, y / 2 * 11 + 1) % 9) == 0;
                return false;
            }

            public bool IsPillar(int x, int y)
            {
                int dx = x - gate.x, dy = y - (gate.y + 11);
                if (kind == BossKind.Skeleton) return (Mathf.Abs(dx) == 6 && (dy == -6 || dy == 5)) || (Mathf.Abs(dx) == 10 && dy == 0);
                if (kind == BossKind.WorldsFear) return (dx == -4 || dx == 3) && (dy == -8 || dy == -3 || dy == 2 || dy == 7);
                return false;
            }

            public bool High(int x, int y) => InArena(x, y) || Neck.Contains(new Vector2Int(x, y)) || Terrace.Contains(new Vector2Int(x, y));
        }

        // Where each boss lives.
        public static Biome BossBiome(BossKind k)
        {
            switch (k)
            {
                case BossKind.Spider: return Biome.Forest;
                case BossKind.Zombie: return Biome.Marsh;
                case BossKind.Skeleton: return Biome.Ruins;
                case BossKind.WorldsFear: return Biome.Ember;
                default: return Biome.Forest;            // the Slime, and anything else
            }
        }

        static readonly Dictionary<long, Fortress?> fortresses = new Dictionary<long, Fortress?>();

        public static bool FortressInPatch(int px, int py, out Fortress fortress)
        {
            long key = ((long)px << 32) ^ (uint)py;
            if (!fortresses.TryGetValue(key, out var found))
            {
                fortresses[key] = null;                  // while searching, this patch has none (HasDungeon asks)
                found = FindFortress(px, py, true) ?? FindFortress(px, py, false);
                fortresses[key] = found;
            }
            fortress = found ?? default;
            return found.HasValue;
        }

        // Tries spots across the patch in the seed's order: far enough out, in the right biome for the boss that the
        // distance gives it, and with no waypoint, dungeon or challenge chest where it would stand.
        static Fortress? FindFortress(int px, int py, bool matchBiome)
        {
            int span = BossGrid * ChunkSize;
            const int inset = 30;
            for (int k = 0; k < (matchBiome ? 60 : 12); k++)
            {
                uint h = Hash(px * 101 + bossSalt + k * 977, py * 211 - bossSalt - k * 131);
                var gate = new Vector2Int(px * span + inset + (int)((h & 0xFFFF) % (uint)(span - 2 * inset)) & ~1,
                                          py * span + inset + (int)((h >> 16) % (uint)(span - 2 * inset)) & ~1);
                if (((Vector2)gate).magnitude < 220f) continue;
                var f = new Fortress { gate = gate, kind = WorldManager.KindForGate(gate) };
                if (matchBiome && (BiomeAt(f.ArenaCentre) != BossBiome(f.kind) || BiomeAt(f.TerraceCentre) != BossBiome(f.kind))) continue;
                var b = f.Bounds;
                var c0 = ChunkOf(new Vector2(b.xMin - 6, b.yMin - 6)); var c1 = ChunkOf(new Vector2(b.xMax + 6, b.yMax + 6));
                bool clear = true;
                for (int cx = c0.x; cx <= c1.x && clear; cx++)
                for (int cy = c0.y; cy <= c1.y && clear; cy++)
                    if (HasWaypoint(cx, cy, out _) || HasDungeon(cx, cy, out _)) clear = false;
                if (clear) return f;
            }
            return null;
        }

        public static bool FortressAt(int x, int y, out Fortress f)
        {
            int span = BossGrid * ChunkSize;
            return FortressInPatch(Mathf.FloorToInt(x / (float)span), Mathf.FloorToInt(y / (float)span), out f) && f.Bounds.Contains(new Vector2Int(x, y));
        }

        static bool FortressCell(int x, int y, out Cell cell)
        {
            cell = Cell.Ground;
            if (!FortressAt(x, y, out var f)) return false;
            if (f.IsPillar(x, y)) cell = Cell.Pillar;
            else if (f.High(x, y)) cell = Cell.Plateau;
            else if (f.Pool(x, y) != Cell.Ground) cell = f.Pool(x, y);       // everything else inside its bounds is a clearing
            return true;
        }

        // Camp slots per chunk (about three in four are used): two near the town, more the further out you go, so the
        // long walks of the far country are fights and not just walks.
        public static int CampSlots(int x, int y)
        {
            int level = LevelAt(new Vector2(x, y));
            return level < 8 ? 2 : level < 20 ? 3 : level < 35 ? 4 : 5;
        }

        // Challenge chests: scattered about one per 4x4 chunks, never near the village.
        // Waves 3-10, with long challenges rarer; further out they tend to be longer.
        public const int ChestGrid = 4;

        // A challenge chest that would stand inside a castle (or its clearing) is left out: the castle wins.
        public static bool HasChest(int cx, int cy, out Vector2Int cell, out int waves) =>
            HasChestRaw(cx, cy, out cell, out waves) && !CastleAt(cell.x, cell.y, out _) && !FortressAt(cell.x, cell.y, out _);

        static bool HasChestRaw(int cx, int cy, out Vector2Int cell, out int waves)
        {
            cell = default;
            waves = 0;
            if (Mod(cx + 1 + chestShift.x, ChestGrid) != 0 || Mod(cy + 2 + chestShift.y, ChestGrid) != 0) return false;
            uint h = Hash(cx * 7 + 3, cy * 13 + 5);
            if ((h & 3) == 0) return false;   // some grid spots stay empty
            var p = new Vector2Int(cx * ChunkSize + 4 + (int)(h >> 4) % (ChunkSize - 8), cy * ChunkSize + 4 + (int)(h >> 12) % (ChunkSize - 8));
            float d = ((Vector2)p).magnitude;
            if (d < HubRadius + 30f) return false;
            float r = Hash01(cx * 31 + 11, cy * 17 + 2);
            float far = Mathf.Clamp01(d / 900f);
            waves = Mathf.Clamp(3 + Mathf.FloorToInt(Mathf.Pow(r, 1.6f - far) * 8f), 3, 10);
            cell = p;
            return true;
        }

        // Hidden caches: a small chest off the beaten track, so that leaving the road pays. About one chunk in twelve is
        // searched for its most out-of-the-way spot: up on high ground, or tucked into a nook shut in by cliffs and
        // water, well away from any path. Open flat country has no such spot and gets none. The spot can always be
        // walked to from the land round it.
        static readonly Dictionary<long, Vector2Int?> caches = new Dictionary<long, Vector2Int?>();

        public static bool HasCache(int cx, int cy, out Vector2Int cell)
        {
            long key = ((long)cx << 32) ^ (uint)cy;
            if (!caches.TryGetValue(key, out var found))
            {
                found = FindCache(cx, cy);
                if (caches.Count > 4000) caches.Clear();
                caches[key] = found;
            }
            cell = found ?? default;
            return found.HasValue;
        }

        static Vector2Int? FindCache(int cx, int cy)
        {
            if (Hash(cx * 19 + 7, cy * 23 + 1) % 12 != 0) return null;
            if (HasWaypoint(cx, cy, out _) || HasBossGate(cx, cy, out _) || HasChestRaw(cx, cy, out _, out _) || HasDungeon(cx, cy, out _)) return null;
            int x0 = cx * ChunkSize, y0 = cy * ChunkSize;
            if (new Vector2(x0 + ChunkSize / 2, y0 + ChunkSize / 2).magnitude < HubRadius + 60f) return null;
            int low = int.MaxValue;
            for (int y = 0; y < ChunkSize; y += 2)
            for (int x = 0; x < ChunkSize; x += 2)
                low = Mathf.Min(low, ElevAt(x0 + x, y0 + y));
            var best = new List<(int score, Vector2Int at)>();
            for (int y = 3; y < ChunkSize - 3; y++)
            for (int x = 3; x < ChunkSize - 3; x++)
            {
                int wx = x0 + x, wy = y0 + y, lv = ElevAt(wx, wy);
                if (lv >= MountainLevel || InFarmland(wx, wy) || CastleAt(wx, wy, out _) || FortressAt(wx, wy, out _)) continue;
                // How shut in the spot is: the tiles within three of it that are not open ground on its own level.
                int shut = 0; bool room = true, road = false;
                for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    var c = CellAt(wx + dx, wy + dy);
                    bool open = (c == Cell.Ground || c == Cell.Plateau || c == Cell.Path) && ElevAt(wx + dx, wy + dy) == lv;
                    if (!open) { shut++; if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) room = false; }
                    if (c == Cell.Path || c == Cell.Bridge) road = true;
                }
                if (!room || road) continue;
                int up = lv - low;
                if (shut < 20 && up < 3) continue;
                best.Add((shut + up * 8 + (int)(Hash(wx, wy) & 3), new Vector2Int(wx, wy)));
            }
            best.Sort((p, q) => q.score.CompareTo(p.score));
            for (int i = 0; i < best.Count && i < 4; i++)
                if (WalksOut(best[i].at)) return best[i].at;
            return null;
        }

        // ---------- Summit chests ----------
        // The top of every peak that can be climbed holds a chest: the highest ground of its chunk, higher than
        // anything in the chunks round it, with a way down on foot (the flights that climb a mountain terrace by terrace).
        public const int SummitLevel = MountainLevel + 2;
        public const int SnowLevel = 10;         // snow lies from here up, outside the ash wastes
        static readonly Dictionary<long, Vector2Int?> summits = new Dictionary<long, Vector2Int?>();
        static readonly Dictionary<long, int> chunkTops = new Dictionary<long, int>();

        static int ChunkTop(int cx, int cy)
        {
            long key = ((long)cx << 32) ^ (uint)cy;
            if (chunkTops.TryGetValue(key, out int top)) return top;
            for (int y = 0; y < ChunkSize; y++)
            for (int x = 0; x < ChunkSize; x++)
                top = Mathf.Max(top, ElevAt(cx * ChunkSize + x, cy * ChunkSize + y));
            if (chunkTops.Count > 8000) chunkTops.Clear();
            return chunkTops[key] = top;
        }

        public static bool HasSummit(int cx, int cy, out Vector2Int cell)
        {
            long key = ((long)cx << 32) ^ (uint)cy;
            if (!summits.TryGetValue(key, out var found))
            {
                found = FindSummit(cx, cy);
                if (summits.Count > 4000) summits.Clear();
                summits[key] = found;
            }
            cell = found ?? default;
            return found.HasValue;
        }

        static Vector2Int? FindSummit(int cx, int cy)
        {
            int top = ChunkTop(cx, cy);
            if (top < SummitLevel) return null;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int other = ChunkTop(cx + dx, cy + dy);
                if (other > top || (other == top && (dy < 0 || (dy == 0 && dx < 0)))) return null;
            }
            // The middle of the top terrace, on open ground with room round it.
            var tops = new List<Vector2Int>(); Vector2 mid = Vector2.zero;
            for (int y = 0; y < ChunkSize; y++)
            for (int x = 0; x < ChunkSize; x++)
            {
                int wx = cx * ChunkSize + x, wy = cy * ChunkSize + y;
                if (ElevAt(wx, wy) != top || !Floor(CellAt(wx, wy)) || CellAt(wx, wy) == Cell.Path) continue;
                tops.Add(new Vector2Int(wx, wy)); mid += new Vector2(wx, wy);
            }
            if (tops.Count == 0) return null;
            mid /= tops.Count;
            tops.Sort((p, q) => ((Vector2)p - mid).sqrMagnitude.CompareTo(((Vector2)q - mid).sqrMagnitude));
            for (int i = 0; i < tops.Count && i < 3; i++)
                if (WalksDown(tops[i])) return tops[i];
            return null;
        }

        // Can a walker get from here down off the mountain?
        static bool WalksDown(Vector2Int from)
        {
            const int R = 72, W = 2 * R + 1;
            var seen = new bool[W * W];
            var open = new Queue<Vector2Int>();
            open.Enqueue(from); seen[R * W + R] = true;
            while (open.Count > 0)
            {
                var p = open.Dequeue();
                if (ElevAt(p.x, p.y) < MountainLevel && CellAt(p.x, p.y) != Cell.Stairs) return true;
                if (Mathf.Abs(p.x - from.x) >= R - 1 || Mathf.Abs(p.y - from.y) >= R - 1) continue;
                foreach (var d in StairDirs)
                {
                    var q = p + d;
                    int i = (q.y - from.y + R) * W + (q.x - from.x + R);
                    if (seen[i] || !CanStep(p.x, p.y, q.x, q.y)) continue;
                    seen[i] = true; open.Enqueue(q);
                }
            }
            return false;
        }

        // Can a walker get from here out into the land round it (not an island, not a ledge with no way up)?
        static bool WalksOut(Vector2Int from)
        {
            const int R = 28, W = 2 * R + 1;
            var seen = new bool[W * W];
            var open = new Queue<Vector2Int>();
            open.Enqueue(from); seen[R * W + R] = true;
            while (open.Count > 0)
            {
                var p = open.Dequeue();
                if (Mathf.Abs(p.x - from.x) >= R - 1 || Mathf.Abs(p.y - from.y) >= R - 1) return true;
                foreach (var d in StairDirs)
                {
                    var q = p + d;
                    int i = (q.y - from.y + R) * W + (q.x - from.x + R);
                    if (seen[i] || !CanStep(p.x, p.y, q.x, q.y)) continue;
                    seen[i] = true; open.Enqueue(q);
                }
            }
            return false;
        }

        // Dungeons: one per patch of the map, at a random chunk and spot inside it, kept clear of other landmarks.
        public const int DungeonGrid = 6;
        const int DungeonEdge = 7;   // tiles from the chunk border, so the stairwell never crosses into the next chunk

        // The hole a dungeon's stairs go down through, round the gate's cell: the ground is cut away there.
        public static BoundsInt Stairwell(Vector2Int gate) =>
            new BoundsInt(gate.x - DungeonGate.HalfWidth, gate.y - DungeonGate.HalfLength, 0, 2 * DungeonGate.HalfWidth + 1, 2 * DungeonGate.HalfLength + 1, 1);

        public static bool InStairwell(int x, int y)
        {
            int cx = Mathf.FloorToInt(x / (float)ChunkSize), cy = Mathf.FloorToInt(y / (float)ChunkSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (HasDungeon(cx + dx, cy + dy, out var gate) && Stairwell(gate).Contains(new Vector3Int(x, y, 0))) return true;
            return false;
        }

        public static bool HasDungeon(int cx, int cy, out Vector2Int cell)
        {
            cell = default;
            int px = Mathf.FloorToInt(cx / (float)DungeonGrid), py = Mathf.FloorToInt(cy / (float)DungeonGrid);
            uint h = Hash(px * 53 + 9, py * 97 - 4);
            if (Mod(cx, DungeonGrid) != (h & 0xFF) % DungeonGrid || Mod(cy, DungeonGrid) != ((h >> 8) & 0xFF) % DungeonGrid) return false;
            uint spot = (uint)(ChunkSize - 2 * DungeonEdge);
            var c = new Vector2Int(cx * ChunkSize + DungeonEdge + (int)(((h >> 16) & 0xFF) % spot), cy * ChunkSize + DungeonEdge + (int)((h >> 24) % spot));
            if (((Vector2)c).magnitude < HubRadius + 40f) return false;
            if (HasWaypoint(cx, cy, out _) || HasBossGate(cx, cy, out _) || HasChestRaw(cx, cy, out _, out _)) return false;
            cell = c;
            return true;
        }

        // High ground stays clear of waypoints, gates and chests, so none of them ends up on a plateau with no way down.
        internal static bool NearLandmark(int x, int y)
        {
            var c = ChunkOf(new Vector2(x, y));
            return Near(HasWaypoint(c.x, c.y, out var a), a, x, y) || Near(HasBossGate(c.x, c.y, out var b), b, x, y)
                || Near(HasChestRaw(c.x, c.y, out var e, out _), e, x, y) || Near(HasDungeon(c.x, c.y, out var g), g, x, y);
        }

        static bool Near(bool has, Vector2Int p, int x, int y) => has && Mathf.Abs(p.x - x) <= 9 && Mathf.Abs(p.y - y) <= 9;

        // ---------- Heights: up to 20 levels of land, stairs lead from one to the next ----------
        // A flight of stairs is 2 tiles wide and StairLength long, on ground at level lo against higher ground at level
        // hi (one or two levels up), rising toward it. a = the footprint's lowest corner cell, d = the way up, r = across.
        public struct Stair { public Vector2Int a, d, r; public int lo, hi, w, len; public bool natural, path; }

        public const int StairLength = 3;
        const int StairApron = 1;
        const int StairBlock = 16;
        const int MaxFlights = 12;
        const int MaxWidth = 8;
        static readonly Dictionary<long, List<Stair>> stairBlocks = new Dictionary<long, List<Stair>>();
        static readonly Vector2Int[] StairDirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        // Flat open ground a flight may stand on (not water, not a wall).
        static bool Floor(Cell c) => c == Cell.Ground || c == Cell.Path || c == Cell.Plateau;

        // Every spot where a flight fits is found once, then the world seed picks: one flight for each level that
        // has a way up in this 16x16 block (so a mountain is climbed terrace by terrace), plus one far from the first.
        static List<Stair> StairsIn(int bx, int by)
        {
            long key = ((long)bx << 32) ^ (uint)by;
            if (stairBlocks.TryGetValue(key, out var list)) return list;
            list = new List<Stair>();
            var found = new List<Stair>();
            int x0 = bx * StairBlock, y0 = by * StairBlock;
            for (int y = 2; y < StairBlock - 2; y++)
            for (int x = 2; x < StairBlock - 2; x++)
            foreach (var d in StairDirs)
            {
                var a = new Vector2Int(x0 + x, y0 + y);
                if (FortressAt(a.x, a.y, out _)) continue;          // a fortress has one way up, its own
                var r = new Vector2Int(Mathf.Abs(d.y), Mathf.Abs(d.x));
                foreach (int len in new[] { StairLength, 2 })
                {
                var top = a + d * len;
                if (!Floor(BaseCell(top.x, top.y))) continue;
                int lo = ElevAt(a.x, a.y), hi = ElevAt(top.x, top.y);
                if (hi - lo < 1 || hi - lo > 2) continue;
                bool onPath = CrossesPath(a, d, r, 1, len);
                // In the ruins a flight is a stone stair, and it stands where it is needed: on a road, or up a cliff.
                // Cut into an edge that is a walkable bank anyway it is a stair to nowhere, boxed in by its own walls.
                if (!onPath && BiomeAt(new Vector2(a.x, a.y)) == Biome.Ruins && BankAllowed(a.x, a.y, hi)) continue;
                // Short flights: for paths that would otherwise run into a cliff, and for a single level up.
                if (len < StairLength && !onPath && hi - lo > 1) continue;
                // As wide as the ground allows, up to MaxWidth: a broad grassy slope rather than a narrow stair.
                int w = 0;
                while (w < MaxWidth && InBlock(a + r * w + d * (len - 1), x0, y0) && InBlock(a + r * w, x0, y0) && Column(a + r * w, d, lo, hi, len)) w++;
                onPath = w > 0 && CrossesPath(a, d, r, w, len);
                bool ok = w >= (onPath ? 1 : 2);
                // Room to walk past the foot: open ground at the same level behind it and a tile wider on each side,
                // so a flight never fills a narrow gap between two rises and blocks the way through.
                // (A path's own flight needs no run-up: the path leads to it.)
                for (int k = -StairApron; k <= -1 && ok && !onPath; k++)
                for (int i = -1; i <= w && ok; i++)
                {
                    int qx = a.x + r.x * i + k * d.x, qy = a.y + r.y * i + k * d.y;
                    ok = Floor(BaseCell(qx, qy)) && ElevAt(qx, qy) == lo;
                }
                if (ok) { found.Add(new Stair { a = a, d = d, r = r, lo = lo, hi = hi, w = w, len = len, natural = BiomeAt(new Vector2(a.x, a.y)) != Biome.Ruins, path = onPath }); break; }
                }
            }
            if (found.Count > 0)
            {
                // The widest first (ties broken by the seed).
                found.Sort((p, q) => p.path != q.path ? q.path.CompareTo(p.path) : q.w != p.w ? q.w.CompareTo(p.w) : (Hash(p.a.x, p.a.y) & 0xFFFF).CompareTo(Hash(q.a.x, q.a.y) & 0xFFFF));
                // Every flight that fits, widest first, as long as they keep a little apart: most rises have a way up
                // close by, so the high ground is somewhere to go rather than a wall to walk round.
                foreach (var f in found)
                {
                    if (list.Count >= MaxFlights) break;
                    if (!Overlaps(list, f)) list.Add(f);
                }
            }
            stairBlocks[key] = list;
            return list;
        }

        static bool Overlaps(List<Stair> list, Stair f)
        {
            var rf = Footprint(f, 3);
            foreach (var o in list)
                if (Footprint(o, 0).Overlaps(rf)) return true;
            return false;
        }

        static RectInt Footprint(Stair f, int pad)
        {
            var b = f.a + f.r * (f.w - 1) + f.d * (f.len - 1);
            return new RectInt(Mathf.Min(f.a.x, b.x) - pad, Mathf.Min(f.a.y, b.y) - pad, Mathf.Abs(b.x - f.a.x) + 1 + pad * 2, Mathf.Abs(b.y - f.a.y) + 1 + pad * 2);
        }

        // A road or trail runs up through this flight: the path crosses its foot and its top.
        static bool CrossesPath(Vector2Int a, Vector2Int d, Vector2Int r, int w, int len)
        {
            bool foot = false, top = false;
            for (int i = 0; i < w; i++)
            {
                var q = a + r * i;
                foot |= OnPath(q.x - d.x, q.y - d.y) || OnPath(q.x, q.y);
                top |= OnPath(q.x + d.x * len, q.y + d.y * len);
            }
            return foot && top;
        }

        // A road or trail runs over this tile (whatever level it is on).
        public static bool OnPath(int x, int y)
        {
            float r = Noise(x, y, 0.018f, 21), r2 = Noise(x, y, 0.011f, 22);
            return Mathf.Abs(r - 0.5f) < 0.022f || Mathf.Abs(r2 - 0.5f) < 0.016f;
        }

        // Flights stay inside their own 16x16 block, so StairAt finds every tile of them there.
        static bool InBlock(Vector2Int q, int x0, int y0) => q.x - x0 >= 0 && q.x - x0 < StairBlock && q.y - y0 >= 0 && q.y - y0 < StairBlock;

        // One column of a flight: StairLength tiles of open ground at level lo (and one before it), high ground at hi after.
        static bool Column(Vector2Int q, Vector2Int d, int lo, int hi, int len)
        {
            var t = q + d * len;
            if (!Floor(BaseCell(t.x, t.y)) || ElevAt(t.x, t.y) != hi) return false;
            for (int k = -1; k < len; k++)
                if (!Floor(BaseCell(q.x + k * d.x, q.y + k * d.y)) || ElevAt(q.x + k * d.x, q.y + k * d.y) != lo) return false;
            return true;
        }

        // The flight a cell belongs to, and which of its rows: 0 = the bottom, StairLength - 1 = against the high ground.
        public static bool StairAt(int x, int y, out Stair stair, out int row)
        {
            if (FortressAt(x, y, out var fort))
            {
                var a = fort.StairA;
                stair = new Stair { a = a, d = Vector2Int.up, r = Vector2Int.right, lo = 0, hi = PlateauLevel, w = 2, len = StairLength };
                row = y - a.y;
                return x >= a.x && x <= a.x + 1 && row >= 0 && row < StairLength;
            }
            if (BlockStairAt(x, y, out stair, out row)) return true;
            if (PathRampAt(x, y, out stair, out row)) return true;
            return BankAt(x, y, out stair, out row);
        }

        // ---------- Banks: rises you can walk up anywhere ----------
        // In the gentle lands (forest, marsh, round the town) every rise of one or two levels is a grassy bank, one
        // tile per level, so the hills roll instead of walling you in. In the ruins most of the edges are banks and in
        // the ember wastes about half, in long stretches, and the rest stay cliffs. Mountains above BankTopRugged keep their
        // cliffs: the high ranges are for climbers and flyers.
        const int BankTopRugged = 6;

        static bool BankAllowed(int x, int y, int hi)
        {
            if (hi >= MountainLevel) return false;   // the high ranges are for flyers and climbers
            var b = BiomeAt(new Vector2(x + 0.5f, y + 0.5f));
            if (b == Biome.Forest || b == Biome.Marsh || b == Biome.Village) return true;
            // The ruins' mesas are broad and lie across every way through: most of their edges are banks, so you
            // cross them where you meet them. The ash wastes keep more of their cliffs.
            return hi <= BankTopRugged && Noise(x, y, 0.06f, 91) > (b == Biome.Ruins ? 0.3f : 0.48f);
        }

        static bool BankAt(int x, int y, out Stair stair, out int row)
        {
            stair = default; row = 0;
            if (!Floor(BaseCell(x, y))) return false;
            // One level: this tile alone. Two levels: this tile and the next, as rows 0 and 1.
            for (int len = 1; len <= 2; len++)
            for (int r = 0; r < len; r++)
            foreach (var d in StairDirs)
            {
                var a = new Vector2Int(x - d.x * r, y - d.y * r);
                if (TryBank(a, d, len, out stair)) { row = r; return true; }
            }
            return false;
        }

        static bool TryBank(Vector2Int a, Vector2Int d, int len, out Stair stair)
        {
            stair = default;
            var top = a + d * len;
            int lo = ElevAt(a.x, a.y), hi = ElevAt(top.x, top.y);
            if (hi - lo != len || !Floor(BaseCell(top.x, top.y))) return false;
            if (!BankAllowed(a.x, a.y, hi)) return false;
            for (int k = 0; k < len; k++)
            {
                var q = a + d * k;
                if (!Floor(BaseCell(q.x, q.y)) || ElevAt(q.x, q.y) != lo) return false;
                if (BlockStairAt(q.x, q.y, out _, out _)) return false;
            }
            if (FortressAt(a.x, a.y, out _) || FortressAt(top.x, top.y, out _)) return false;
            stair = new Stair { a = a, d = d, r = new Vector2Int(Mathf.Abs(d.y), Mathf.Abs(d.x)), lo = lo, hi = hi, w = 1, len = len, natural = true, path = OnPath(a.x, a.y) };
            return true;
        }

        static bool BlockStairAt(int x, int y, out Stair stair, out int row)
        {
            foreach (var st in StairsIn(Mathf.FloorToInt(x / (float)StairBlock), Mathf.FloorToInt(y / (float)StairBlock)))
            {
                int rx = x - st.a.x, ry = y - st.a.y;
                int across = rx * st.r.x + ry * st.r.y, along = rx * st.d.x + ry * st.d.y;
                if (across < 0 || across >= st.w || along < 0 || along >= st.len) continue;
                stair = st; row = along;
                return true;
            }
            stair = default; row = 0;
            return false;
        }

        // Paths go up what they meet: where a road or trail runs into a rise of one or two levels, its last two
        // tiles become a grassy slope up (one tile wide; the path's other tiles get their own beside it), so you can
        // always follow a path onto the high ground.
        const int RampLength = 2;
        static bool PathRampAt(int x, int y, out Stair stair, out int row)
        {
            stair = default; row = 0;
            if (!OnPath(x, y)) return false;
            // Two tiles where the path meets the rise square on; one, steeper, where it comes at it slantwise.
            for (int len = RampLength; len >= 1; len--)
            for (int r = 0; r < len; r++)
            foreach (var d in StairDirs)
            {
                var a = new Vector2Int(x - d.x * r, y - d.y * r);
                if (TryPathRamp(a, d, len, out stair)) { row = r; return true; }
            }
            return false;
        }

        static bool TryPathRamp(Vector2Int a, Vector2Int d, int len, out Stair stair)
        {
            stair = default;
            var top = a + d * len;
            int lo = ElevAt(a.x, a.y), hi = ElevAt(top.x, top.y);
            if (hi - lo < 1 || hi - lo > 2) return false;
            if (!OnPath(top.x, top.y) || !Floor(BaseCell(top.x, top.y))) return false;
            // The ramp's own tiles: path at the low level, not already part of a flight; the tile before it too.
            for (int k = len > 1 ? -1 : 0; k < len; k++)
            {
                var q = a + d * k;
                if (!OnPath(q.x, q.y) || !Floor(BaseCell(q.x, q.y)) || ElevAt(q.x, q.y) != lo) return false;
                if (k >= 0 && BlockStairAt(q.x, q.y, out _, out _)) return false;
            }
            if (FortressAt(a.x, a.y, out _)) return false;
            stair = new Stair { a = a, d = d, r = new Vector2Int(Mathf.Abs(d.y), Mathf.Abs(d.x)), lo = lo, hi = hi, w = 1, len = len, natural = true, path = true };
            return true;
        }

        // Something can stand here (at the cell's level).
        public static bool Standable(Cell c) => Walkable(c) || c == Cell.Plateau;

        // How high the ground is at a point: its level times LevelHeight, in between on stairs.
        public static float HeightAt(Vector2 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            // Grassy slopes and the ground they bend (SurfaceAt): the surface you see, corner to corner.
            if (SurfaceAt(x, y, out float h00, out float h10, out float h11, out float h01))
            {
                float fx = p.x - x, fy = p.y - y;
                // The tile is drawn as two triangles split along the corner (1,0)-(0,1) (Town.Solid.Quad): the height
                // is read off the same two triangles, so nobody sinks into a twisted tile or floats over it.
                return fx + fy <= 1f ? h00 + (h10 - h00) * fx + (h01 - h00) * fy
                                     : h11 + (h01 - h11) * (1f - fx) + (h10 - h11) * (1f - fy);
            }
            var c = CellAt(x, y);
            if (c != Cell.Stairs || !StairAt(x, y, out var st, out _)) return ElevAt(x, y) * LevelHeight;
            float along = (p.x - st.a.x) * st.d.x + (p.y - st.a.y) * st.d.y + (st.d.x + st.d.y < 0 ? 1f : 0f);
            return (st.lo + (st.hi - st.lo) * Mathf.Clamp01(along / st.len)) * LevelHeight;
        }

        // ---------- The bent ground round grassy slopes ----------
        // A grid corner touched by a grassy slope (a natural flight, a bank, a path ramp) gets one height, the same for
        // every tile that shares it. Slope tiles take their four corners from it; so does flat ground touching a slope,
        // which bends to meet it, so no step or notch is left anywhere round a slope.
        // The height is the mean of what the tiles round the corner say: a slope its own height there, flat ground its
        // level. Where an edge is a staircase of slopes (a rise running at a slant), the slopes disagree at the corners
        // between their rows; the mean lays one smooth slanting bank over them instead of a row of teeth. Where a cliff
        // ends at the corner, its top has to stay put, so the corner keeps the highest height round it.
        // NaN where no slope touches the corner.
        static readonly Dictionary<long, float> vertexHeights = new Dictionary<long, float>();

        public static float SlopeVertexHeight(int cx, int cy)
        {
            long key = ((long)cx << 32) ^ (uint)cy;
            if (vertexHeights.TryGetValue(key, out float h)) return h;
            h = float.NaN;
            int top = int.MinValue;
            // The four tiles round the corner: 0 = (-1,-1), 1 = (0,-1), 2 = (-1,0), 3 = (0,0).
            var said = new float[4]; var has = new bool[4]; var lvl = new int[4]; var slope = new Stair?[4];
            float max = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                int qx = cx - 1 + (k & 1), qy = cy - 1 + (k >> 1);
                lvl[k] = ElevAt(qx, qy);
                var c = CellAt(qx, qy);
                if (c == Cell.Stairs && StairAt(qx, qy, out var st, out _) && st.natural)
                {
                    float along = (cx - st.a.x) * st.d.x + (cy - st.a.y) * st.d.y + (st.d.x + st.d.y < 0 ? 1f : 0f);
                    said[k] = (st.lo + (st.hi - st.lo) * Mathf.Clamp01(along / st.len)) * LevelHeight;
                    has[k] = true; slope[k] = st;
                    top = Mathf.Max(top, st.hi);
                    max = Mathf.Max(max, said[k]);
                }
            }
            if (top == int.MinValue) { Remember(key, h); return h; }
            for (int k = 0; k < 4; k++)
            {
                if (has[k]) continue;
                int qx = cx - 1 + (k & 1), qy = cy - 1 + (k >> 1);
                if (!Bends(CellAt(qx, qy)) || lvl[k] > top) continue;
                said[k] = lvl[k] * LevelHeight; has[k] = true;
                max = Mathf.Max(max, said[k]);
            }
            // A cliff along one of the four edges meeting here (between two of the tiles round it): the higher tile
            // stands over the lower one, unless the lower one is a slope reaching up to it.
            bool Cliff(int a, int b)
            {
                if (lvl[a] == lvl[b]) return false;
                int hi = lvl[a] > lvl[b] ? a : b, lo = hi == a ? b : a;
                return !(slope[lo] is Stair ls && lvl[hi] <= ls.hi);
            }
            bool cliff = Cliff(0, 1) || Cliff(2, 3) || Cliff(0, 2) || Cliff(1, 3);
            if (cliff) h = max;
            else
            {
                float sum = 0f; int n = 0;
                for (int k = 0; k < 4; k++) if (has[k]) { sum += said[k]; n++; }
                h = sum / n;
            }
            Remember(key, h);
            return h;
        }

        static void Remember(long key, float h)
        {
            if (vertexHeights.Count > 200000) vertexHeights.Clear();
            vertexHeights[key] = h;
        }

        // Flat ground that bends to meet a slope (puddles and anything built stay flat).
        public static bool Bends(Cell c) => c == Cell.Ground || c == Cell.Path || c == Cell.Plateau;

        // The four corner heights of a grassy slope tile, or of flat ground bent by one (up or down); false for
        // anything else.
        public static bool SurfaceAt(int x, int y, out float h00, out float h10, out float h11, out float h01)
        {
            h00 = h10 = h11 = h01 = 0f;
            var c = CellAt(x, y);
            bool slope = c == Cell.Stairs && StairAt(x, y, out var st, out _) && st.natural;
            if (!slope && !Bends(c)) return false;
            float own = ElevAt(x, y) * LevelHeight;
            float V(int cx, int cy) { float v = SlopeVertexHeight(cx, cy); return float.IsNaN(v) ? own : v; }
            h00 = V(x, y); h10 = V(x + 1, y); h11 = V(x + 1, y + 1); h01 = V(x, y + 1);
            if (slope) return true;
            return Mathf.Abs(h00 - own) > 0.01f || Mathf.Abs(h10 - own) > 0.01f || Mathf.Abs(h11 - own) > 0.01f || Mathf.Abs(h01 - own) > 0.01f;
        }

        // Can a walker go from one cell to the one next to it (not diagonal)? Different levels only meet on stairs,
        // which are entered at the bottom and left at the top. Climbers and flyers go anywhere they can stand;
        // leapers also drop down a few levels.
        public static bool CanStep(int ax, int ay, int bx, int by) => CanStep(ax, ay, bx, by, Mobility.Walk);

        public static bool CanStep(int ax, int ay, int bx, int by, Mobility how)
        {
            var a = CellAt(ax, ay); var b = CellAt(bx, by);
            bool aS = a == Cell.Stairs, bS = b == Cell.Stairs;
            if (how == Mobility.Climb || how == Mobility.Fly)
                return (Standable(a) || aS) && (Standable(b) || bS);
            if (!aS && !bS)
            {
                if (!Standable(a) || !Standable(b)) return false;
                int ea = ElevAt(ax, ay), eb = ElevAt(bx, by);
                if (ea == eb) return true;
                return how == Mobility.Leap && eb < ea && ea - eb <= LeapDrop;
            }
            if (aS && bS) return true;
            int sx = aS ? ax : bx, sy = aS ? ay : by, ox = aS ? bx : ax, oy = aS ? by : ay;
            var other = aS ? b : a;
            if (!StairAt(sx, sy, out var st, out int row)) return false;
            if (!Standable(other)) return false;
            if (row == st.len - 1 && ox == sx + st.d.x && oy == sy + st.d.y) return ElevAt(ox, oy) == st.hi;
            if (row == 0 && ox == sx - st.d.x && oy == sy - st.d.y) return ElevAt(ox, oy) == st.lo;
            return false;
        }

        // ---------- Castles: open dungeons on the overworld ----------
        // About three patches in four hold a ruined castle: an outer wall with a main gate, two side gates and maybe a
        // breach, a tower on each corner, broken walls in the courtyard and a keep against the north wall. Monster
        // packs hold the courtyard and the keep; the keep's chest opens once the keep is clear. No loading screen:
        // it is part of the world, walked into through its gates.
        public const int CastleGrid = 9;     // chunks per patch
        const int CastleMargin = 3;          // open ground kept round the walls so they are always reachable

        public struct Castle
        {
            public RectInt rect, keep;
            public uint seed;
            public int breach;               // x of a 2-wide gap in the north wall, -1 for none
            public Vector2Int Center => new Vector2Int(rect.x + rect.width / 2, rect.y + rect.height / 2);
        }

        static readonly Dictionary<long, Castle?> castles = new Dictionary<long, Castle?>();

        public static bool CastleInPatch(int px, int py, out Castle castle)
        {
            long key = ((long)px << 32) ^ (uint)py;
            if (!castles.TryGetValue(key, out var found))
            {
                found = null;
                uint h = Hash(px * 389 + 17, py * 743 - 29);
                if ((h & 3) != 0)
                {
                    int span = CastleGrid * ChunkSize;
                    int w = 24 + (int)((h >> 4) % 8u) * 2, ht = 20 + (int)((h >> 8) % 4u) * 2;
                    int pad = CastleMargin + 6;
                    var rect = new RectInt(px * span + pad + (int)((h >> 12) % (uint)(span - w - 2 * pad)),
                                           py * span + pad + (int)((h >> 20) % (uint)(span - ht - 2 * pad)), w, ht);
                    if (rect.center.magnitude > HubRadius + 90f && !CastleTouchesLandmark(rect) && !CastleTouchesFortress(rect))
                    {
                        int kw = Mathf.Max(8, w / 3) & ~1, kh = Mathf.Max(6, ht / 3);
                        var keep = new RectInt(rect.x + (w - kw) / 2, rect.yMax - 3 - kh, kw, kh);
                        uint h2 = Hash(px * 61 + 3, py * 83 + 5);
                        int breach = (h2 & 1) == 0 ? -1 : rect.x + 4 + (int)((h2 >> 4) % (uint)(w / 2 - 6));
                        found = new Castle { rect = rect, keep = keep, seed = h2, breach = breach };
                    }
                }
                castles[key] = found;
            }
            castle = found ?? default;
            return found.HasValue;
        }

        public static bool CastleAt(int x, int y, out Castle castle)
        {
            int span = CastleGrid * ChunkSize;
            if (!CastleInPatch(Mathf.FloorToInt(x / (float)span), Mathf.FloorToInt(y / (float)span), out castle)) return false;
            var r = castle.rect;
            return x >= r.x - CastleMargin && x < r.xMax + CastleMargin && y >= r.y - CastleMargin && y < r.yMax + CastleMargin;
        }

        // A castle never covers a waypoint, boss gate, challenge chest or dungeon: it gives way to them.
        static bool CastleTouchesLandmark(RectInt r)
        {
            var a = ChunkOf(new Vector2(r.x - CastleMargin - 7, r.y - CastleMargin - 7));
            var b = ChunkOf(new Vector2(r.xMax + CastleMargin + 7, r.yMax + CastleMargin + 7));
            for (int cx = a.x; cx <= b.x; cx++)
            for (int cy = a.y; cy <= b.y; cy++)
                if (HasWaypoint(cx, cy, out _) || HasBossGate(cx, cy, out _) || HasDungeon(cx, cy, out _))
                    return true;
            return false;
        }

        static bool CastleTouchesFortress(RectInt r)
        {
            int span = BossGrid * ChunkSize, m = CastleMargin + 4;
            for (int ix = 0; ix < 2; ix++)
            for (int iy = 0; iy < 2; iy++)
            {
                int x = ix == 0 ? r.xMin : r.xMax, y = iy == 0 ? r.yMin : r.yMax;
                if (!FortressInPatch(Mathf.FloorToInt(x / (float)span), Mathf.FloorToInt(y / (float)span), out var f)) continue;
                var b = f.Bounds;
                if (r.xMin - m < b.xMax && r.xMax + m > b.xMin && r.yMin - m < b.yMax && r.yMax + m > b.yMin) return true;
            }
            return false;
        }

        static bool CastleCell(int x, int y, out Cell cell)
        {
            cell = Cell.Ground;
            if (!CastleAt(x, y, out var c)) return false;
            var r = c.rect;
            if (!r.Contains(new Vector2Int(x, y))) return true;                    // the clearing round the walls
            int lx = x - r.x, ly = y - r.y, w = r.width, h = r.height;
            bool cornerX = lx < 3 || lx >= w - 3, cornerY = ly < 3 || ly >= h - 3;
            if (cornerX && cornerY) { cell = Cell.Tower; return true; }
            if (lx == 0 || lx == w - 1 || ly == 0 || ly == h - 1)
            {
                bool mainGate = ly == 0 && Mathf.Abs(lx - w / 2) <= 1;
                bool sideGate = (lx == 0 || lx == w - 1) && (ly == h / 2 || ly == h / 2 - 1);
                bool breach = ly == h - 1 && c.breach >= 0 && (x == c.breach || x == c.breach + 1);
                cell = mainGate || sideGate || breach ? Cell.CastleFloor : Cell.CastleWall;
                return true;
            }
            var k = c.keep;
            if (k.Contains(new Vector2Int(x, y)))
            {
                int kx = x - k.x, ky = y - k.y;
                bool edge = kx == 0 || kx == k.width - 1 || ky == 0 || ky == k.height - 1;
                bool door = ky == 0 && (kx == k.width / 2 || kx == k.width / 2 - 1);
                bool corner = (kx < 2 || kx >= k.width - 2) && (ky < 2 || ky >= k.height - 2);
                cell = corner ? Cell.Tower : edge && !door ? Cell.CastleWall : Cell.CastleFloor;
                return true;
            }
            // Broken walls in the courtyard: short runs, never in front of a gate or the keep's door.
            bool lane = Mathf.Abs(lx - w / 2) <= 3 || Mathf.Abs(ly - h / 2) <= 2;
            bool nearKeep = x >= k.x - 2 && x < k.xMax + 2 && y >= k.y - 3;
            if (!lane && !nearKeep && lx > 3 && lx < w - 4 && ly > 3 && ly < h - 4)
            {
                uint hh = Hash(x / 3 * 7 + 1, y * 5 + 2);
                if ((hh % 23) == 0) { cell = Cell.Wall; return true; }
            }
            cell = Cell.CastleFloor;
            return true;
        }

        public static int Mod(int a, int m) => ((a % m) + m) % m;

        public static Vector2Int ChunkOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.y / ChunkSize));
    }
}
