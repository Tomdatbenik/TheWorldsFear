using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // CastleFloor/CastleWall/Tower: the open castles on the overworld (appended, so saved numbers keep their meaning).
    public enum Cell : byte { Ground, Path, Water, Lava, Wall, Plateau, Hub, CastleFloor, CastleWall, Tower, Puddle, Stairs, Pillar }

    // Deterministic infinite world: everything about a position comes from the hero's world seed.
    // Difficulty grows with distance from the village at the origin.
    public static class WorldGen
    {
        public const int ChunkSize = 24;
        public const float HubRadius = 48f;     // the village; no monsters inside
        public const float SafeRadius = 62f;    // no camps this close
        public const float PlateauHeight = 2.4f; // cliffs rise toward -Z

        // Saves from before each hero had their own world all live in this one.
        public const int LegacySeed = 20261028;

        public static int Seed { get; private set; } = LegacySeed;

        // How far this world slides the grids its landmarks sit on.
        static Vector2Int waypointShift, chestShift;
        static int bossSalt;

        public static void SetSeed(int seed)
        {
            Seed = seed;
            regions.Clear();
            castles.Clear();
            stairBlocks.Clear();
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
            var c = BaseCell(x, y);
            return (c == Cell.Ground || c == Cell.Path) && StairAt(x, y, out _, out _) ? Cell.Stairs : c;
        }

        // The cell before stairs are laid against the plateaus.
        static Cell BaseCell(int x, int y)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = p.magnitude;
            if (IsHub(x, y)) return Cell.Hub;
            if (d > HubRadius && FortressCell(x, y, out var fortCell)) return fortCell;
            if (d > HubRadius && CastleCell(x, y, out var castleCell)) return castleCell;

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
            if (d > HubRadius + 12f && height > 0.72f) return NearLandmark(x, y) ? Cell.Ground : Cell.Plateau;
            // Ponds and small lakes everywhere but the ash lands (deep water: blocks), and shallow puddles you
            // splash through. The ponds keep away from the roads; puddles may lie on them.
            if (biome != Biome.Ember && d > HubRadius + 8f)
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
                    // Broken straight walls on a coarse grid.
                    if ((x % 13 == 0 || y % 11 == 0) && Noise(x, y, 0.2f, 33) > 0.55f) return Cell.Wall;
                    break;
            }
            return Cell.Ground;
        }

        public static bool Walkable(Cell c) => c == Cell.Ground || c == Cell.Path || c == Cell.Hub || c == Cell.CastleFloor || c == Cell.Puddle;

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

            public bool InArena(int x, int y)
            {
                var a = Arena;
                if (x < a.xMin || x >= a.xMax || y < a.yMin || y >= a.yMax) return false;
                int cx = Mathf.Min(x - a.xMin, a.xMax - 1 - x), cy = Mathf.Min(y - a.yMin, a.yMax - 1 - y);
                return cx + cy >= 5 && !IsPillar(x, y);  // the four corners cut off
            }

            // Six stone pillars in the arena: cover from the boss's waves, and something to keep the camera busy.
            public bool IsPillar(int x, int y)
            {
                int dx = x - gate.x, dy = y - (gate.y + 11);
                return (Mathf.Abs(dx) == 6 && (dy == -6 || dy == 5)) || (Mathf.Abs(dx) == 10 && dy == 0);
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
            else if (f.High(x, y)) cell = Cell.Plateau;       // everything else inside its bounds is a clearing
            return true;
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

        // Dungeons: one per patch of the map, at a random chunk and spot inside it, kept clear of other landmarks.
        public const int DungeonGrid = 6;
        const int DungeonEdge = 7;   // tiles from the chunk border, so the stairwell never crosses into the next chunk

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
        static bool NearLandmark(int x, int y)
        {
            var c = ChunkOf(new Vector2(x, y));
            return Near(HasWaypoint(c.x, c.y, out var a), a, x, y) || Near(HasBossGate(c.x, c.y, out var b), b, x, y)
                || Near(HasChestRaw(c.x, c.y, out var e, out _), e, x, y) || Near(HasDungeon(c.x, c.y, out var g), g, x, y);
        }

        static bool Near(bool has, Vector2Int p, int x, int y) => has && Mathf.Abs(p.x - x) <= 9 && Mathf.Abs(p.y - y) <= 9;

        // ---------- Heights: plateaus are walked on, stairs lead up to them ----------
        // A flight of stairs is 2 tiles wide and StairLength long, on low ground against a plateau's edge, rising toward it.
        // a = the footprint's lowest corner cell, d = the way up, r = across.
        public struct Stair { public Vector2Int a, d, r; }

        public const int StairLength = 3;
        const int StairBlock = 16;
        static readonly Dictionary<long, List<Stair>> stairBlocks = new Dictionary<long, List<Stair>>();
        static readonly Vector2Int[] StairDirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        static bool Low(Cell c) => c == Cell.Ground || c == Cell.Path;

        // Up to two flights per 16x16 block: every spot where one fits is found once, then picked by the world seed.
        static List<Stair> StairsIn(int bx, int by)
        {
            long key = ((long)bx << 32) ^ (uint)by;
            if (stairBlocks.TryGetValue(key, out var list)) return list;
            list = new List<Stair>();
            var found = new List<Stair>();
            int x0 = bx * StairBlock, y0 = by * StairBlock;
            for (int y = 3; y < StairBlock - 3; y++)
            for (int x = 3; x < StairBlock - 3; x++)
            foreach (var d in StairDirs)
            {
                var a = new Vector2Int(x0 + x, y0 + y);
                if (FortressAt(a.x, a.y, out _)) continue;          // a fortress has one way up, its own
                if (BaseCell(a.x + StairLength * d.x, a.y + StairLength * d.y) != Cell.Plateau) continue;
                var r = new Vector2Int(Mathf.Abs(d.y), Mathf.Abs(d.x));
                bool ok = true;
                for (int i = 0; i < 2 && ok; i++)
                {
                    var q = a + r * i;
                    ok = BaseCell(q.x + StairLength * d.x, q.y + StairLength * d.y) == Cell.Plateau;
                    for (int k = -1; k < StairLength && ok; k++) ok = Low(BaseCell(q.x + k * d.x, q.y + k * d.y));
                }
                if (ok) found.Add(new Stair { a = a, d = d, r = r });
            }
            if (found.Count > 0)
            {
                var first = found[(int)(Hash(bx * 131 + 7, by * 173 - 3) % (uint)found.Count)];
                list.Add(first);
                Stair far = first;
                int best = 0;
                foreach (var f in found)
                {
                    int dist = Mathf.Abs(f.a.x - first.a.x) + Mathf.Abs(f.a.y - first.a.y);
                    if (dist > best) { best = dist; far = f; }
                }
                if (best >= 7) list.Add(far);
            }
            stairBlocks[key] = list;
            return list;
        }

        // The flight a cell belongs to, and which of its rows: 0 = the bottom, StairLength - 1 = against the plateau.
        public static bool StairAt(int x, int y, out Stair stair, out int row)
        {
            if (FortressAt(x, y, out var fort))
            {
                var a = fort.StairA;
                stair = new Stair { a = a, d = Vector2Int.up, r = Vector2Int.right };
                row = y - a.y;
                return x >= a.x && x <= a.x + 1 && row >= 0 && row < StairLength;
            }
            foreach (var st in StairsIn(Mathf.FloorToInt(x / (float)StairBlock), Mathf.FloorToInt(y / (float)StairBlock)))
            {
                int rx = x - st.a.x, ry = y - st.a.y;
                int across = rx * st.r.x + ry * st.r.y, along = rx * st.d.x + ry * st.d.y;
                if (across < 0 || across > 1 || along < 0 || along >= StairLength) continue;
                stair = st; row = along;
                return true;
            }
            stair = default; row = 0;
            return false;
        }

        // How high the ground is at a point (0 on low ground, PlateauHeight on a plateau, in between on stairs).
        public static float HeightAt(Vector2 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            var c = CellAt(x, y);
            if (c == Cell.Plateau) return PlateauHeight;
            if (c != Cell.Stairs || !StairAt(x, y, out var st, out _)) return 0f;
            float along = (p.x - st.a.x) * st.d.x + (p.y - st.a.y) * st.d.y + (st.d.x + st.d.y < 0 ? 1f : 0f);
            return PlateauHeight * Mathf.Clamp01(along / StairLength);
        }

        // Can a walker go from one cell to the one next to it (not diagonal)? Low and high ground only meet on stairs,
        // which are entered at the bottom and left at the top.
        public static bool CanStep(int ax, int ay, int bx, int by)
        {
            var a = CellAt(ax, ay); var b = CellAt(bx, by);
            bool aS = a == Cell.Stairs, bS = b == Cell.Stairs;
            if (!aS && !bS) return (Walkable(a) && Walkable(b)) || (a == Cell.Plateau && b == Cell.Plateau);
            if (aS && bS) return true;
            int sx = aS ? ax : bx, sy = aS ? ay : by, ox = aS ? bx : ax, oy = aS ? by : ay;
            var other = aS ? b : a;
            if (!StairAt(sx, sy, out var st, out int row)) return false;
            if (other == Cell.Plateau) return row == StairLength - 1 && ox == sx + st.d.x && oy == sy + st.d.y;
            return Walkable(other) && row == 0 && ox == sx - st.d.x && oy == sy - st.d.y;
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
