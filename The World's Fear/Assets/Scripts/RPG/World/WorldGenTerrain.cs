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
        public const int MountainLevel = 8;      // the high ranges: cliffs kept, flyers live up here
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

        class TerrainCache
        {
            public readonly byte[] cell = new byte[ChunkSize * ChunkSize];
            public readonly sbyte[] elev = new sbyte[ChunkSize * ChunkSize];
            public readonly sbyte[] land = new sbyte[ChunkSize * ChunkSize];   // the land's own level, under any high bridge
            public readonly byte[] span = new byte[ChunkSize * ChunkSize];     // 255 not worked out, 0 no high bridge, else its level
        }
        static readonly Dictionary<long, TerrainCache> terrainCache = new Dictionary<long, TerrainCache>();

        static TerrainCache CacheFor(int x, int y, out int index)
        {
            int cx = x >= 0 ? x / ChunkSize : (x + 1) / ChunkSize - 1, cy = y >= 0 ? y / ChunkSize : (y + 1) / ChunkSize - 1;
            index = (y - cy * ChunkSize) * ChunkSize + (x - cx * ChunkSize);
            long key = ((long)cx << 32) ^ (uint)cy;
            if (terrainCache.TryGetValue(key, out var c)) return c;
            if (terrainCache.Count > 6000) terrainCache.Clear();
            c = new TerrainCache();
            for (int i = 0; i < c.cell.Length; i++) { c.cell[i] = 255; c.elev[i] = -1; c.land[i] = -1; c.span[i] = 255; }
            terrainCache[key] = c;
            return c;
        }

        static void ClearTerrainCache()
        {
            terrainCache.Clear();
            caches.Clear(); summits.Clear(); chunkTops.Clear(); bridgeRuns.Clear();
            landmarks.Clear();
            farms = null;
        }

        // The level a cell is walked on: the land's, or the deck of a high bridge over it.
        public static int ElevAt(int x, int y)
        {
            var c = CacheFor(x, y, out int i);
            if (c.elev[i] < 0) { int span = SpanLevel(x, y); c.elev[i] = (sbyte)(span > 0 ? span : BaseElevAt(x, y)); }
            return c.elev[i];
        }

        // The level of the land itself (under a high bridge, the ground it crosses).
        public static int BaseElevAt(int x, int y)
        {
            var c = CacheFor(x, y, out int i);
            if (c.land[i] < 0) c.land[i] = (sbyte)ElevAtRaw(x, y);
            return c.land[i];
        }

        // ---------- High bridges ----------
        // Where two stretches of high ground on one level face each other across a gap of a few tiles, at least two
        // levels deep, a plank bridge spans it from edge to edge: the high ground runs on instead of ending at every
        // cleft. Bridges lie on lanes two tiles wide (every 17th pair of rows, every 19th pair of columns), so a long
        // canyon is crossed here and there, not roofed over. The ground under a bridge cannot be walked through; a
        // road down there is never bridged over, so no road is cut.
        const int MaxSkySpan = 10;

        // The level of the high bridge over this cell, or 0.
        public static int SpanLevel(int x, int y)
        {
            var c = CacheFor(x, y, out int i);
            if (c.span[i] == 255) c.span[i] = (byte)SpanLevelRaw(x, y);
            return c.span[i];
        }

        static int SpanLevelRaw(int x, int y)
        {
            if (new Vector2(x + 0.5f, y + 0.5f).magnitude < FlatRadius) return 0;
            if (OnPath(x, y) && CanyonAt(x, y)) return ElevUncut(x, y);   // a road over a canyon: its own bridge
            if (Mod(y, 17) < 2) { int l = SpanAlong(x, y, 1, 0); if (l > 0) return l; }
            if (Mod(x, 19) < 2) { int l = SpanAlong(x, y, 0, 1); if (l > 0) return l; }
            return 0;
        }

        static int SpanAlong(int x, int y, int dx, int dy)
        {
            int own = BaseElevAt(x, y);
            // Back to the edge the bridge starts from: the first tile at least two levels over this one.
            int back = 0, hi = 0;
            for (int k = 1; k <= MaxSkySpan && back == 0; k++)
            {
                int e = BaseElevAt(x - dx * k, y - dy * k);
                if (e >= own + 2) { back = k; hi = e; }
            }
            if (back == 0 || hi >= MountainLevel) return 0;
            // On to the far edge: the first tile that is not two levels under the deck has to be the same high ground.
            int on = 0;
            for (int k = 1; k <= MaxSkySpan + 1 - back && on == 0; k++)
            {
                int e = BaseElevAt(x + dx * k, y + dy * k);
                if (e > hi - 2) { if (e != hi) return 0; on = k; }
            }
            if (on == 0 || back + on - 1 < 3) return 0;
            int ax = x - dx * back, ay = y - dy * back, bx = x + dx * on, by = y + dy * on;
            // The gap's floor is about level (one bridge for the whole gap, the same from every tile of it)...
            int lo = int.MaxValue, top = int.MinValue;
            for (int k = 1; k < back + on; k++)
            {
                int e = BaseElevAt(ax + dx * k, ay + dy * k);
                lo = Mathf.Min(lo, e); top = Mathf.Max(top, e);
            }
            if (top - lo > 1 || hi - top < 2) return 0;
            // ...solid high ground at both ends, two tiles of it, so the bridge leads somewhere...
            for (int k = 0; k <= 1; k++)
            {
                if (!Floor(LandCell(ax - dx * k, ay - dy * k)) || BaseElevAt(ax - dx * k, ay - dy * k) != hi) return 0;
                if (!Floor(LandCell(bx + dx * k, by + dy * k)) || BaseElevAt(bx + dx * k, by + dy * k) != hi) return 0;
            }
            // ...a cleft that runs on a good way to both sides, so the bridge saves a real walk round (over a notch in
            // the edge, or by the end of a gap, it would be a bridge beside the way you would walk anyway)...
            for (int side = -1; side <= 1; side += 2)
                for (int j = 1; j <= 3; j++)
                {
                    bool gap = false;
                    for (int k = 1; k < back + on && !gap; k++)
                        gap = BaseElevAt(ax + dx * k + dy * j * side, ay + dy * k + dx * j * side) <= hi - 2;
                    if (!gap) return 0;
                }
            // ...and under it open country, water or lava: nothing built, and no road to cut.
            for (int k = 1; k < back + on; k++)
            {
                var g = LandCell(ax + dx * k, ay + dy * k);
                if (g != Cell.Ground && g != Cell.Plateau && g != Cell.Water && g != Cell.Puddle && g != Cell.Lava) return 0;
            }
            return hi;
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
            if (BridgeZone(x, y)) return BridgeLevel;   // a bridge and the path out from its ends: level with the river
            // The median of the 3x3 round the cell: lone bumps, one-tile notches and thin slivers of a level are
            // smoothed away, so each tier is a solid stretch of ground you can walk about on.
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                medianBuf[n++] = ElevRaw(x + dx, y + dy);
            System.Array.Sort(medianBuf);
            return medianBuf[4];
        }

        [System.ThreadStatic] static int[] medianBufTS;
        static int[] medianBuf => medianBufTS ??= new int[9];

        // The level from the noise alone (callers handle the hub, fortresses and castles): the land as it rose, with
        // the canyons cut down through it.
        static int ElevRaw(int x, int y) => CanyonCut(x, y) ? 0 : ElevUncut(x, y);

        // ---------- Canyons ----------
        // Winding gorges a few tiles wide cut the high ground (mesas, ash plateaus, tablelands) right down to the
        // lowest level, with water on the floor, lava in the ash wastes. They split the high ground into parts joined
        // by the high bridges (SpanLevel). A road that meets a canyon crosses it on a bridge of its own, as wide as
        // the road. The peaks are left alone.
        static float CanyonLine(int x, int y)
        {
            float wx = (Noise(x, y, 0.009f, 93) - 0.5f) * 60f, wy = (Noise(x, y, 0.009f, 94) - 0.5f) * 60f;
            return Mathf.Abs(Noise(x + wx, y + wy, 0.0052f, 91) - 0.5f);
        }

        static bool CanyonCut(int x, int y)
        {
            if (CanyonLine(x, y) > 0.0105f) return false;
            int lv = ElevUncut(x, y);
            return lv >= 2 && lv < MountainLevel;
        }

        // The floor of a canyon: the cut itself, and the odd tile beside it that the smoothing lowered with it.
        public static bool CanyonAt(int x, int y)
        {
            if (new Vector2(x + 0.5f, y + 0.5f).magnitude < FlatRadius) return false;
            if (CanyonCut(x, y)) return true;
            if (CanyonLine(x, y) > 0.03f || BaseElevAt(x, y) != 0 || ElevUncut(x, y) < 2) return false;
            return CanyonCut(x + 1, y) || CanyonCut(x - 1, y) || CanyonCut(x, y + 1) || CanyonCut(x, y - 1);
        }

        static int ElevUncut(int x, int y)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d = p.magnitude;
            if (d < FlatRadius) return 0;
            if (BridgeZone(x, y)) return BridgeLevel;
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
                    // Mesas with two flat tiers, each two levels over the last: a step a bank can climb, so the mesas
                    // can be walked over from side to side, not only up onto and back down again.
                    { float n = Noise(x, y, 0.011f, 73); e = n > 0.67f ? 4.5f : n > 0.56f ? 2.5f : 0f; }
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
            float pk = Smooth(Mathf.InverseLerp(0.60f, 0.9f, Noise(x, y, 0.0055f, 78)));
            if (m > 0.3f && pk > 0f) e = Mathf.Max(e, 6.5f + pk * Mathf.Min(1f, m * 1.6f) * 13.5f + (Noise(x, y, 0.05f, 77) - 0.5f) * 1.2f * pk);

            e *= ramp * clear * RiverValley(x, y);
            int lv = Mathf.Clamp(Mathf.FloorToInt(e), 0, MaxLevel);
            // Above the first two levels the land rises two at a time: half as many tiers, each twice as wide, so
            // a mountain is a few broad terraces rather than rings of narrow ledges.
            if (lv > 2) lv -= lv % 2;
            return lv;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        // ---------- Rivers ----------
        // A river is a long winding line of noise (pushed about by more noise so it never runs straight), a few tiles
        // wide, swelling now and then into a lake. The land falls away to the lowest level along it, so it runs in a
        // shallow valley. Where a path meets it, it crosses on a low plank bridge level with the banks: no raised ends,
        // so a path meeting the river at a slant never climbs a stair of one-tile ledges with cliffs round them.
        public const int BridgeLevel = 0;
        const float RiverStart = 150f;           // beyond the farms and the town's four roads

        static float RiverNoise(int x, int y)
        {
            float wx = (Noise(x, y, 0.006f, 63) - 0.5f) * 70f, wy = (Noise(x, y, 0.006f, 64) - 0.5f) * 70f;
            return Mathf.Abs(Noise(x + wx, y + wy, 0.0026f, 61) - 0.5f);
        }

        // Half the river's width, in noise units: a few tiles, much more where it swells into a lake.
        static float RiverHalf(int x, int y) => 0.0045f + Mathf.Max(0f, Noise(x, y, 0.012f, 62) - 0.6f) * 0.12f;

        static bool RiverLand(int x, int y)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            return p.magnitude > RiverStart && BiomeAt(p) != Biome.Ember;
        }

        public static bool RiverAt(int x, int y) => RiverLand(x, y) && RiverNoise(x, y) < RiverHalf(x, y);

        // 0 in and right by the river, rising to 1 over the valley's sides.
        static float RiverValley(int x, int y)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            if (p.magnitude <= RiverStart) return 1f;
            float half = RiverHalf(x, y);
            float v = Smooth(Mathf.InverseLerp(half + 0.002f, half + 0.02f, RiverNoise(x, y)));
            if (v >= 1f || BiomeAt(p) != Biome.Ember) return v;
            // The ash wastes hold no river, but where one runs up to their edge its valley carries on into them a
            // little way, fading out: the ash comes down to the water, not standing over it as a wall of cliffs.
            for (int r = 3; r <= 12; r += 3)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    if (BiomeAt(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r) != Biome.Ember)
                        return Mathf.Lerp(v, 1f, Smooth((r - 3) / 12f));
                }
            return 1f;
        }


        const int Abutment = 2;       // how far the raised path runs out from each end of a bridge

        // A path tile in the river is a bridge where the path crosses it, at whatever slant: the stretch of path lying
        // in the water (all its tiles, taken together) reaches the path on both banks. The river is the line where its
        // noise crosses the middle, so the two banks are the two signs of it. A path that only dips into the water and
        // comes out on the same bank, or runs along in the river for a long way, just stops at it.
        const int MaxBridgeTiles = 800;
        static readonly Dictionary<long, bool> bridgeRuns = new Dictionary<long, bool>();
        static readonly Queue<Vector2Int> bridgeQueue = new Queue<Vector2Int>();
        static readonly List<long> bridgeRun = new List<long>();

        static float RiverSide(int x, int y)
        {
            float wx = (Noise(x, y, 0.006f, 63) - 0.5f) * 70f, wy = (Noise(x, y, 0.006f, 64) - 0.5f) * 70f;
            return Noise(x + wx, y + wy, 0.0026f, 61) - 0.5f;
        }

        public static bool BridgeCrosses(int x, int y)
        {
            if (!OnPath(x, y) || !RiverAt(x, y)) return false;
            long Key(int qx, int qy) => ((long)qx << 32) ^ (uint)qy;
            if (bridgeRuns.TryGetValue(Key(x, y), out bool known)) return known;
            bridgeQueue.Clear(); bridgeRun.Clear();
            var seen = new HashSet<long> { Key(x, y) };
            bridgeQueue.Enqueue(new Vector2Int(x, y));
            bool plus = false, minus = false, built = false, tooLong = false;
            while (bridgeQueue.Count > 0)
            {
                var q = bridgeQueue.Dequeue();
                bridgeRun.Add(Key(q.x, q.y));
                if (bridgeRun.Count > MaxBridgeTiles) { tooLong = true; break; }
                for (int k = 0; k < 4; k++)
                {
                    int nx = q.x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = q.y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (FortressCell(nx, ny, out _) || CastleCell(nx, ny, out _)) { built = true; continue; }   // a fort in a lake is a bank of its own
                    if (!OnPath(nx, ny)) continue;
                    if (!RiverAt(nx, ny)) { if (RiverSide(nx, ny) > 0f) plus = true; else minus = true; continue; }
                    if (seen.Add(Key(nx, ny))) bridgeQueue.Enqueue(new Vector2Int(nx, ny));
                }
            }
            bool crosses = (plus ? 1 : 0) + (minus ? 1 : 0) + (built ? 1 : 0) >= 2 && !tooLong;
            if (tooLong) { bridgeRuns[Key(x, y)] = false; return false; }
            foreach (long key in bridgeRun) bridgeRuns[key] = crosses;
            return crosses;
        }

        // A river tile just off the path's ragged edge, with the bridge's deck or the path on the bank on both sides of
        // it along one axis: decked over too, or it is a square of open water in the end of the bridge.
        public static bool BridgeGap(int x, int y)
        {
            bool Deck(int qx, int qy) => OnPath(qx, qy) && (!RiverAt(qx, qy) || BridgeCrosses(qx, qy));
            return (Deck(x - 1, y) && Deck(x + 1, y)) || (Deck(x, y - 1) && Deck(x, y + 1));
        }

        // A bridge, and the path on the banks for Abutment tiles out from it: kept at BridgeLevel, flat to the water.
        static bool BridgeZone(int x, int y)
        {
            // (The banks may lie outside river land, at the edge of the ash wastes: an end is flattened wherever a
            // bridge needs one.)
            if (!OnPath(x, y)) return false;
            if (RiverAt(x, y)) return BridgeCrosses(x, y);
            if (RiverNoise(x, y) > RiverHalf(x, y) + 0.03f) return false;
            foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                for (int k = 1; k <= Abutment; k++)
                {
                    int qx = x + d.x * k, qy = y + d.y * k;
                    if (!RiverAt(qx, qy)) { if (!OnPath(qx, qy)) break; continue; }
                    if (OnPath(qx, qy) && BridgeCrosses(qx, qy)) return true;
                    break;
                }
            return false;
        }

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
