using System.Collections.Generic;
using UnityEngine;

namespace TWF.Tests
{
    // The rules of the generated world, checked without running the game.
    public static class WorldGenChecks
    {
        [TwfCheck("WorldGen/Same seed gives the same world")]
        static void Deterministic()
        {
            Check.EachSeed(seed =>
            {
                long First() { long h = 17; for (int y = 200; y < 264; y++) for (int x = -300; x < -236; x++) h = h * 31 + (int)WorldGen.CellAt(x, y) * 7 + WorldGen.ElevAt(x, y); return h; }
                long a = First();
                WorldGen.SetSeed(seed);   // drops every cache
                long b = First();
                Check.That(a == b, $"seed {seed}: the same 64x64 patch came out differently the second time");
            });
        }

        [TwfCheck("WorldGen/Chests stay scarce")]
        static void ChestDensity()
        {
            Check.EachSeed(seed =>
            {
                int n = 0, caches = 0, summits = 0, challenge = 0;
                for (int cx = -10; cx <= 10; cx++)
                for (int cy = -10; cy <= 10; cy++)
                {
                    n++;
                    if (WorldGen.HasCache(cx, cy, out _)) caches++;
                    if (WorldGen.HasSummit(cx, cy, out _)) summits++;
                    if (WorldGen.HasChest(cx, cy, out _, out _)) challenge++;
                }
                // Hidden caches are a bonus on top of the challenge chests, never the main thing on the map.
                Check.AtMost(caches, challenge + 4, $"seed {seed}: hidden caches against {challenge} challenge chests in {n} chunks");
                Check.AtMost((caches + summits + challenge) / (float)n, 0.12f, $"seed {seed}: chests per chunk");
                Check.AtLeast(challenge, 8, $"seed {seed}: challenge chests in {n} chunks");
            });
        }

        [TwfCheck("WorldGen/More monster camps further out")]
        static void CampSlotsGrow()
        {
            int last = 0;
            for (int r = 0; r < 3000; r += 50)
            {
                int slots = WorldGen.CampSlots(r, 0);
                Check.That(slots >= last, $"camp slots drop from {last} to {slots} at distance {r}");
                last = slots;
            }
            Check.AtLeast(WorldGen.CampSlots(60, 0), 2, "camp slots near the town");
            Check.AtLeast(last, 4, "camp slots in the far country");
        }

        // A character's height is read from WorldGen.HeightAt; the ground is drawn from WorldGen.SurfaceAt as two
        // triangles split along the corners (1,0)-(0,1). If the two disagree, characters sink into slopes or float.
        [TwfCheck("WorldGen/Characters stand on the drawn slope surface")]
        static void HeightMatchesSurface()
        {
            Check.EachSeed(seed =>
            {
                int tiles = 0; float worst = 0f; string at = "";
                for (int x = -140; x < 140; x++)
                for (int y = -140; y < 140; y++)
                {
                    if (!WorldGen.SurfaceAt(x, y, out float h00, out float h10, out float h11, out float h01)) continue;
                    tiles++;
                    foreach (var (fx, fy) in new[] { (0.02f, 0.02f), (0.98f, 0.02f), (0.98f, 0.98f), (0.02f, 0.98f), (0.5f, 0.5f), (0.25f, 0.25f), (0.75f, 0.75f), (0.3f, 0.6f) })
                    {
                        float drawn = fx + fy <= 1f ? h00 + (h10 - h00) * fx + (h01 - h00) * fy : h11 + (h01 - h11) * (1f - fx) + (h10 - h11) * (1f - fy);
                        float d = Mathf.Abs(WorldGen.HeightAt(new Vector2(x + fx, y + fy)) - drawn);
                        if (d > worst) { worst = d; at = $"{x + fx:0.00},{y + fy:0.00}"; }
                    }
                }
                Check.AtMost(worst, 0.01f, $"seed {seed}: height against the drawn surface over {tiles} slope tiles (worst at {at})");
            });
        }

        [TwfCheck("WorldGen/Slopes join without steps")]
        static void SlopesContinuous()
        {
            Check.EachSeed(seed =>
            {
                float worst = 0f; string at = "";
                for (int x = -140; x < 140; x++)
                for (int y = -140; y < 140; y++)
                {
                    if (!WorldGen.SurfaceAt(x, y, out _, out _, out _, out _)) continue;
                    // Across the edge to the tile east and the tile north, where that is bent ground too and both ends
                    // of the edge are corners of the slope surface (elsewhere the edge may be a real cliff).
                    bool Shared(int cx, int cy) => !float.IsNaN(WorldGen.SlopeVertexHeight(cx, cy));
                    if (WorldGen.SurfaceAt(x + 1, y, out _, out _, out _, out _) && Shared(x + 1, y) && Shared(x + 1, y + 1))
                        for (float f = 0.1f; f < 1f; f += 0.2f)
                        {
                            float d = Mathf.Abs(WorldGen.HeightAt(new Vector2(x + 0.999f, y + f)) - WorldGen.HeightAt(new Vector2(x + 1.001f, y + f)));
                            if (d > worst) { worst = d; at = $"{x + 1},{y + f:0.0}"; }
                        }
                    if (WorldGen.SurfaceAt(x, y + 1, out _, out _, out _, out _) && Shared(x, y + 1) && Shared(x + 1, y + 1))
                        for (float f = 0.1f; f < 1f; f += 0.2f)
                        {
                            float d = Mathf.Abs(WorldGen.HeightAt(new Vector2(x + f, y + 0.999f)) - WorldGen.HeightAt(new Vector2(x + f, y + 1.001f)));
                            if (d > worst) { worst = d; at = $"{x + f:0.0},{y + 1}"; }
                        }
                }
                Check.AtMost(worst, 0.02f, $"seed {seed}: step between neighbouring slope tiles (worst at {at})");
            });
        }

        // Road bridges over rivers are straight and two tiles wide. A regression made them thick slabs covering the
        // whole crossing; this counts how many bridge tiles sit in the middle of a slab.
        [TwfCheck("WorldGen/River bridges stay thin")]
        static void BridgesThin()
        {
            Check.EachSeed(seed =>
            {
                int bridge = 0, slab = 0;
                for (int x = -300; x < 300; x++)
                for (int y = -300; y < 300; y++)
                {
                    if (WorldGen.CellAt(x, y) != Cell.Bridge || WorldGen.ElevAt(x, y) != WorldGen.BridgeLevel || !WorldGen.RiverAt(x, y)) continue;
                    bridge++;
                    int Run(int dx, int dy)
                    {
                        int n = 1;
                        for (int k = 1; k < 12 && WorldGen.CellAt(x + dx * k, y + dy * k) == Cell.Bridge; k++) n++;
                        for (int k = 1; k < 12 && WorldGen.CellAt(x - dx * k, y - dy * k) == Cell.Bridge; k++) n++;
                        return n;
                    }
                    if (Mathf.Min(Run(1, 0), Run(0, 1)) > 3) slab++;
                }
                if (bridge == 0) return;
                Check.AtMost(slab / (float)bridge, 0.1f, $"seed {seed}: share of {bridge} river bridge tiles lying inside a slab wider than three tiles");
            });
        }

        // Walking between two points 40 tiles apart must not turn into a maze: the route on foot against the
        // straight line.
        [TwfCheck("WorldGen/The country can be crossed on foot")]
        static void Traversal()
        {
            var rng = new System.Random(7);
            int pairs = 0, blocked = 0; double sum = 0;
            for (int tries = 0; tries < 3000 && pairs < 24; tries++)
            {
                double a = rng.NextDouble() * 6.2832, r = 220 + rng.NextDouble() * 380;
                int sx = (int)(System.Math.Cos(a) * r), sy = (int)(System.Math.Sin(a) * r);
                double b = rng.NextDouble() * 6.2832;
                int gx = sx + (int)(System.Math.Cos(b) * 40), gy = sy + (int)(System.Math.Sin(b) * 40);
                bool Ok(int x, int y) => WorldGen.Standable(WorldGen.CellAt(x, y)) && WorldGen.ElevAt(x, y) < WorldGen.MountainLevel;
                if (!Ok(sx, sy) || !Ok(gx, gy) || !Ok((sx + gx) / 2, (sy + gy) / 2)) continue;
                pairs++;
                int steps = Route(sx, sy, gx, gy, 90);
                if (steps < 0) { blocked++; continue; }
                sum += steps / (float)(Mathf.Abs(gx - sx) + Mathf.Abs(gy - sy));
            }
            Check.AtLeast(pairs, 12, "pairs of points found to test");
            Check.AtMost(blocked / (float)pairs, 0.25f, $"share of {pairs} routes with no way through within 90 tiles");
            if (pairs > blocked) Check.AtMost((float)(sum / (pairs - blocked)), 1.4f, "mean detour (route on foot / straight line)");
        }

        // Breadth-first steps from one cell to another by WorldGen.CanStep, or -1.
        public static int Route(int sx, int sy, int gx, int gy, int reach)
        {
            int w = 2 * reach + 1;
            var dist = new int[w * w];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var q = new Queue<int>();
            dist[reach * w + reach] = 0; q.Enqueue(reach * w + reach);
            while (q.Count > 0)
            {
                int cur = q.Dequeue(); int cx = cur % w - reach + sx, cy = cur / w - reach + sy;
                if (cx == gx && cy == gy) return dist[cur];
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    int ix = nx - sx + reach, iy = ny - sy + reach;
                    if (ix < 0 || iy < 0 || ix >= w || iy >= w) continue;
                    int ni = iy * w + ix;
                    if (dist[ni] >= 0 || !WorldGen.CanStep(cx, cy, nx, ny)) continue;
                    dist[ni] = dist[cur] + 1; q.Enqueue(ni);
                }
            }
            return -1;
        }

        // Waypoints, dungeon gates and challenge chests are put down wherever the seed falls. Half the dungeons once
        // stood in a river, a pond or lava, a third with it in the stairwell itself.
        [TwfCheck("WorldGen/Dungeons, waypoints and chests stand on dry ground")]
        static void LandmarksDry()
        {
            Check.EachSeed(seed =>
            {
                int landmarks = 0;
                void Dry(Vector2Int at, int r, string what)
                {
                    landmarks++;
                    for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        var cell = WorldGen.CellAt(at.x + dx, at.y + dy);
                        Check.That(cell != Cell.Water && cell != Cell.Lava, $"seed {seed}: {cell} at {at.x + dx},{at.y + dy}, inside the yard of the {what} at {at}");
                    }
                }
                for (int cx = -14; cx <= 14; cx++)
                for (int cy = -14; cy <= 14; cy++)
                {
                    if (WorldGen.HasDungeon(cx, cy, out var d)) Dry(d, 6, "dungeon");
                    if (WorldGen.HasWaypoint(cx, cy, out var w)) Dry(w, 3, "waypoint");
                    if (WorldGen.HasChest(cx, cy, out var c, out _)) Dry(c, 4, "challenge chest");
                }
                Check.AtLeast(landmarks, 20, $"seed {seed}: landmarks found to test");
            });
        }

        [TwfCheck("WorldGen/Chests and caches can be walked to")]
        static void ChestsReachable()
        {
            int tested = 0, sealedOff = 0; string where = "";
            for (int cx = -8; cx <= 8; cx++)
            for (int cy = -8; cy <= 8; cy++)
            {
                if (!WorldGen.HasCache(cx, cy, out var cell) && !WorldGen.HasChest(cx, cy, out cell, out _)) continue;
                tested++;
                // Somewhere 25 tiles off must be reachable from the chest.
                bool free = false;
                foreach (var d in new[] { new Vector2Int(25, 0), new Vector2Int(-25, 0), new Vector2Int(0, 25), new Vector2Int(0, -25), new Vector2Int(18, 18), new Vector2Int(-18, -18) })
                {
                    int gx = cell.x + d.x, gy = cell.y + d.y;
                    if (!WorldGen.Standable(WorldGen.CellAt(gx, gy))) continue;
                    if (Route(cell.x, cell.y, gx, gy, 60) >= 0) { free = true; break; }
                }
                if (!free) { sealedOff++; where += $" {cell.x},{cell.y}"; }
            }
            Check.AtLeast(tested, 5, "chests found to test");
            Check.AtMost(sealedOff / (float)tested, 0.1f, $"share of {tested} chests that cannot be walked away from (at{where})");
        }
    }
}
