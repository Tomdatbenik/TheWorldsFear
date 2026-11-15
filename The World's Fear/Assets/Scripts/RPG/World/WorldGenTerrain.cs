using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // How a creature gets about between levels of land.
    //   Walk: only on stairs.  Leap: also drops down a few levels.  Climb: up and down cliff faces (spiders).
    //   Fly: over everything it could stand on.
    public enum Mobility { Walk, Leap, Climb, Fly }

    // The shape of the land: up to 20 levels. The town and the farmland round it are flat; further out every biome
    // has its own relief (rolling forest hills, marsh islands, stepped ruin mesas, ember volcanoes with lava craters)
    // and long mountain ranges cross them all, with streams that fall from terrace to terrace as waterfalls.
    public static partial class WorldGen
    {
        public const float LevelHeight = 1.2f;   // one level of land
        public const int MaxLevel = 19;          // 20 levels: 0..19
        public const int PlateauLevel = 2;       // the old plateaus (PlateauHeight) are level 2
        public const int LeapDrop = 4;           // the most levels a leaper jumps down at once
        public const int RockLevel = 11;         // bare rock above this, no trees
        const float FlatRadius = 125f;           // town and farmland: flat
        const float RiseRadius = 190f;           // relief reaches its full height here

        // ---------- How biomes look at their borders ----------
        // Gameplay keeps the true biome (BiomeAt). What you see - ground, plants, the shape of the land - uses this one:
        // the biome map pushed about by noise, so a border wanders and frays over 10-15 tiles instead of running
        // straight; with dither, single tiles of each side scatter into the other as well.
        public static Biome LookBiomeAt(Vector2 p, bool dither = true)
        {
            float ox = (Noise(p.x, p.y, 0.06f, 91) - 0.5f) * 18f, oy = (Noise(p.x, p.y, 0.06f, 92) - 0.5f) * 18f;
            if (dither)
            {
                int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y);
                ox += (Hash01(ix * 3 + 1, iy * 5 + 2) - 0.5f) * 6f;
                oy += (Hash01(ix * 7 + 3, iy * 3 + 1) - 0.5f) * 6f;
            }
            var b = BiomeAt(p + new Vector2(ox, oy));
            return b == Biome.Village && BiomeAt(p) != Biome.Village ? BiomeAt(p) : b;
        }

        // ---------- Levels ----------

        // ---------- Per-chunk cache: cells and levels are asked for again and again (stairs, routes, rendering) ----------

        class TerrainCache { public readonly byte[] cell = new byte[ChunkSize * ChunkSize]; public readonly sbyte[] elev = new sbyte[ChunkSize * ChunkSize]; }
        static readonly Dictionary<long, TerrainCache> terrainCache = new Dictionary<long, TerrainCache>();

        static TerrainCache CacheFor(int x, int y, out int index)
        {
            int cx = x >= 0 ? x / ChunkSize : (x + 1) / ChunkSize - 1, cy = y >= 0 ? y / ChunkSize : (y + 1) / ChunkSize - 1;
            index = (y - cy * ChunkSize) * ChunkSize + (x - cx * ChunkSize);
            long key = ((long)cx << 32) ^ (uint)cy;
            if (terrainCache.TryGetValue(key, out var c)) return c;
            if (terrainCache.Count > 6000) terrainCache.Clear();
            c = new TerrainCache();
            for (int i = 0; i < c.cell.Length; i++) { c.cell[i] = 255; c.elev[i] = -1; }
            terrainCache[key] = c;
            return c;
        }

        static void ClearTerrainCache()
        {
            terrainCache.Clear();
            landmarks.Clear();
            farms = null;
        }

        // The level of the land a cell stands on.
        public static int ElevAt(int x, int y)
        {
            var c = CacheFor(x, y, out int i);
            if (c.elev[i] < 0) c.elev[i] = (sbyte)ElevAtRaw(x, y);
            return c.elev[i];
        }

        static int ElevAtRaw(int x, int y)
        {
            if (IsHub(x, y)) return 0;
            var p = new Vector2(x + 0.5f, y + 0.5f);
            if (p.magnitude > HubRadius)
            {
                if (FortressAt(x, y, out var f)) return f.High(x, y) && !f.IsPillar(x, y) ? PlateauLevel : 0;
                if (CastleAt(x, y, out _)) return 0;
            }
            if (InFarmland(x, y)) return 0;
            return ElevRaw(x, y);
        }

        // The level from the noise alone (callers handle the hub, fortresses and castles).
        static int ElevRaw(int x, int y)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = p.magnitude;
            if (d < FlatRadius) return 0;
            float ramp = Smooth(Mathf.InverseLerp(FlatRadius, RiseRadius, d));
            float clear = LandmarkClearance(x, y);
            if (clear <= 0f) return 0;

            // Mostly broad flat land in a few tiers (room for camps and fights); now and then a peak.
            float e = 0f;
            var biome = LookBiomeAt(p, false);
            switch (biome)
            {
                case Biome.Forest:
                    // Low rolling hills, one to three levels.
                    e = Mathf.Max(0f, Noise(x, y, 0.012f, 71) - 0.56f) * 9f;
                    break;
                case Biome.Marsh:
                    // Low mossy islands rising out of the bog.
                    { float n = Noise(x, y, 0.03f, 72); e = n > 0.64f ? 1.2f + (n - 0.64f) * 6f : 0f; }
                    break;
                case Biome.Ruins:
                    // Mesas with two flat tiers.
                    { float n = Noise(x, y, 0.011f, 73); e = n > 0.67f ? 6.5f : n > 0.56f ? 3.5f : 0f; }
                    break;
                case Biome.Ember:
                    // Ash plateaus, and now and then a volcano.
                    { float n = Noise(x, y, 0.0075f, 74); e = n > 0.5f ? 2.5f : 0f;
                      float v = Mathf.Max(0f, n - 0.66f) / 0.34f; if (v > 0f) e = Mathf.Max(e, 3f + Mathf.Pow(v, 0.9f) * 17f); }
                    break;
            }
            // The old lone plateaus keep their place: level 2.
            if (Noise(x, y, 0.03f, 41) > 0.72f) e = Mathf.Max(e, PlateauLevel + 0.5f);

            // Mountain ranges are tablelands: broad flat tiers two to six levels up along the ridges...
            float ridge = 1f - Mathf.Abs(2f * Noise(x, y, 0.0017f, 75) - 1f);
            float where = Mathf.InverseLerp(0.4f, 0.6f, Noise(x, y, 0.0011f, 76));
            float m = Smooth(Mathf.InverseLerp(0.42f, 1f, ridge)) * where;
            if (m > 0.08f) e = Mathf.Max(e, m < 0.3f ? 2.5f : m < 0.55f ? 4.5f : 6.5f);
            // ...and here and there a peak climbs out of them, up to the highest level.
            float pk = Smooth(Mathf.InverseLerp(0.68f, 0.93f, Noise(x, y, 0.0055f, 78)));
            if (m > 0.35f && pk > 0f) e = Mathf.Max(e, 6.5f + pk * Mathf.Min(1f, m * 1.6f) * 13.5f + (Noise(x, y, 0.05f, 77) - 0.5f) * 1.2f * pk);

            e *= ramp * clear;
            return Mathf.Clamp(Mathf.FloorToInt(e), 0, MaxLevel);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        // What a raised cell holds: lava in a volcano's crater, a stream or a mountain lake, a path, or open high ground.
        static Cell RaisedCell(int x, int y, int elev, bool path)
        {
            var biome = BiomeAt(new Vector2(x + 0.5f, y + 0.5f));
            if (biome == Biome.Ember)
            {
                if (elev >= 12 && Noise(x, y, 0.09f, 81) > 0.7f) return Cell.Lava;
                return path ? Cell.Path : Cell.Plateau;
            }
            if (Stream(x, y)) return Cell.Water;
            if (elev >= 3 && Noise(x, y, 0.055f, 82) > 0.77f) return Cell.Water;
            return path ? Cell.Path : Cell.Plateau;
        }

        // Streams: thin meandering lines that run over the high ground, broken here and there.
        static float StreamLine(int x, int y) => Mathf.Abs(Noise(x, y, 0.011f, 83) - 0.5f);
        static bool Stream(int x, int y) => StreamLine(x, y) < 0.013f && Noise(x, y, 0.004f, 84) > 0.35f;

        // A pool on low ground where a stream falls off the high ground next to it.
        static bool PlungePool(int x, int y)
        {
            if (StreamLine(x, y) > 0.03f) return false;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (Stream(nx, ny) && ElevRaw(nx, ny) > 0 && BiomeAt(new Vector2(nx + 0.5f, ny + 0.5f)) != Biome.Ember) return true;
            }
            return false;
        }

        // 0 right by a landmark (waypoint, boss gate, chest, dungeon, castle, fortress), 1 well away from all of them:
        // the land falls away round them, so none ends up walled in by cliffs.
        static float LandmarkClearance(int x, int y)
        {
            float best = float.MaxValue;
            var c = ChunkOf(new Vector2(x, y));
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                foreach (var l in LandmarksOf(c.x + dx, c.y + dy))
                    best = Mathf.Min(best, Mathf.Max(Mathf.Abs(l.x - x), Mathf.Abs(l.y - y)));
            float near = best < float.MaxValue ? Smooth(Mathf.InverseLerp(9f, 24f, best)) : 1f;
            return Mathf.Min(near, StructureClearance(x, y));
        }

        static readonly Dictionary<long, Vector2Int[]> landmarks = new Dictionary<long, Vector2Int[]>();

        static Vector2Int[] LandmarksOf(int cx, int cy)
        {
            long key = ((long)cx << 32) ^ (uint)cy;
            if (landmarks.TryGetValue(key, out var arr)) return arr;
            var list = new List<Vector2Int>();
            if (HasWaypoint(cx, cy, out var a)) list.Add(a);
            if (HasBossGate(cx, cy, out var b)) list.Add(b);
            if (HasChestRaw(cx, cy, out var e, out _)) list.Add(e);
            if (HasDungeon(cx, cy, out var g)) list.Add(g);
            arr = list.ToArray();
            landmarks[key] = arr;
            return arr;
        }

        // Castles and fortresses: flat round their walls, rising again 8..24 tiles out.
        static float StructureClearance(int x, int y)
        {
            float best = float.MaxValue;
            int span = CastleGrid * ChunkSize;
            if (CastleInPatch(Mathf.FloorToInt(x / (float)span), Mathf.FloorToInt(y / (float)span), out var castle))
                best = Mathf.Min(best, RectGap(castle.rect, x, y));
            int bspan = BossGrid * ChunkSize;
            if (FortressInPatch(Mathf.FloorToInt(x / (float)bspan), Mathf.FloorToInt(y / (float)bspan), out var fort))
                best = Mathf.Min(best, RectGap(fort.Bounds, x, y));
            return best < float.MaxValue ? Smooth(Mathf.InverseLerp(8f, 24f, best)) : 1f;
        }

        static float RectGap(RectInt r, int x, int y)
        {
            int gx = Mathf.Max(0, Mathf.Max(r.xMin - x, x - (r.xMax - 1)));
            int gy = Mathf.Max(0, Mathf.Max(r.yMin - y, y - (r.yMax - 1)));
            return Mathf.Max(gx, gy);
        }

        // ---------- Farmland ----------
        // Farmsteads round the town walls: fields, fences, a barn and a farmhouse. The ground there is kept flat and
        // clear, and the monsters are rats and spiders, so a new hero learns the game here.
        public struct Farm { public RectInt rect; public int seed; public Vector2 dirToTown; }

        public const float FarmRingInner = 60f, FarmRingOuter = 118f;
        static List<Farm> farms;
        static int farmSeed = int.MinValue;

        public static List<Farm> Farms
        {
            get
            {
                if (farms != null && farmSeed == Seed) return farms;
                farmSeed = Seed;
                farms = new List<Farm>();
                var rng = new System.Random(Seed * 7 + 11);
                const int count = 9;
                for (int i = 0; i < count; i++)
                {
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        float ang = (i + 0.5f) / count * Mathf.PI * 2f + (float)(rng.NextDouble() - 0.5) * 0.45f;
                        float dist = 76f + (float)rng.NextDouble() * 30f;
                        var c = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                        int w = 32 + rng.Next(3) * 4, h = 28 + rng.Next(3) * 2;
                        var rect = new RectInt(Mathf.RoundToInt(c.x - w / 2f), Mathf.RoundToInt(c.y - h / 2f), w, h);
                        if (TouchesRoad(rect) || Overlaps(rect, 5)) continue;
                        farms.Add(new Farm { rect = rect, seed = rng.Next(), dirToTown = -c.normalized });
                        break;
                    }
                }
                return farms;
            }
        }

        // The four great roads (|x| < 2 or |y| < 2) and room either side of them.
        static bool TouchesRoad(RectInt r) => (r.xMin < 6 && r.xMax > -6) || (r.yMin < 6 && r.yMax > -6);

        static bool Overlaps(RectInt r, int pad)
        {
            var g = new RectInt(r.xMin - pad, r.yMin - pad, r.width + pad * 2, r.height + pad * 2);
            if (g.Overlaps(HubRect)) return true;
            foreach (var f in farms) if (g.Overlaps(f.rect)) return true;
            return false;
        }

        // Inside a farm, or its margin: flat ground, no trees, ponds or camps.
        public static bool InFarmland(int x, int y)
        {
            float d = Mathf.Sqrt((float)x * x + (float)y * y);
            if (d < FarmRingInner - 20f || d > FarmRingOuter + 20f) return false;
            foreach (var f in Farms)
                if (x >= f.rect.xMin - 3 && x < f.rect.xMax + 3 && y >= f.rect.yMin - 3 && y < f.rect.yMax + 3) return true;
            return false;
        }

        // The farmland ring, where rats and spiders roam.
        public static bool InFarmRing(Vector2 p) => p.magnitude > HubRadius && p.magnitude < FarmRingOuter;
    }
}
