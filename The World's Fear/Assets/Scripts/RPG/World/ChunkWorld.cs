using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace TWF
{
    // Streams the infinite world in chunks around the hero.
    // Ground is the XY plane; plateaus and walls rise toward -Z as real 3D meshes.
    [DefaultExecutionOrder(-90)]
    public class ChunkWorld : MonoBehaviour
    {
        public static ChunkWorld Instance { get; private set; }

        public int loadRadius = 3;
        // Chunks kept loaded round the hero, each way. Two is plenty: a chunk is 24 tiles, so the nearest unloaded
        // ground is at least 48 tiles off, and the screen with its shadows shows about 30. Three (49 chunks, up to 81
        // before the far ones were let go) kept three times the camps, lights and scenery alive that could ever be
        // seen, and built seven new chunks at every border crossed instead of five.
        const int MaxLoadRadius = 2;
        int Radius => Mathf.Min(loadRadius, MaxLoadRadius);
        public RectInt hubRect = new RectInt(-45, -30, 90, 90);

        class Chunk
        {
            public Vector2Int coord;
            public GameObject root;
            public List<Matrix4x4> grass = new List<Matrix4x4>();
            public Biome grassBiome;
        }

        readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        readonly List<Vector2Int> toLoad = new List<Vector2Int>();
        Tilemap ground, detail, blockers;
        readonly Dictionary<int, Tilemap> levelMaps = new Dictionary<int, Tilemap>();   // raised ground, one per level
        Vector2Int heroChunk = new Vector2Int(int.MinValue, int.MinValue);
        float checkTimer;
        WorldTiles tiles;
        Mesh grassQuad;
        ArtSet art;

        public IEnumerable<Vector2Int> LoadedChunks => chunks.Keys;

        void Awake()
        {
            Instance = this;
            if (Town.On) hubRect = Town.HubRect;
            WorldGen.HubRect = hubRect;
            _ = Profile.Data; // loading the hero sets the world seed; playing this scene directly skips the title screen
            tiles = WorldTiles.Instance;
            art = ArtSet.Instance;
            // Note: Unity's fake-null objects break '??', so check explicitly.
            if (GetComponent<Grid>() == null) gameObject.AddComponent<Grid>();

            ground = MakeTilemap("Ground", 0f, RenderAssets.Instance.litGround, false);
            detail = MakeTilemap("Detail", -0.01f, RenderAssets.Instance.litDetail, false);
            blockers = MakeTilemap("Blockers", 0f, null, true);
            grassQuad = BuildQuad();
            TerrainHeight.World = WorldGen.HeightAt;
        }

        void OnDestroy()
        {
            if (Instance == this) TerrainHeight.World = null;
        }

        Tilemap MakeTilemap(string name, float z, Material material, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, -z, 0);   // z: the old height (negative is up)
            go.transform.localRotation = Ground.Flat;              // the tiles lie on the ground, cell (x, y) at world (x, z)
            var tm = go.AddComponent<Tilemap>();
            if (collider)
            {
                go.layer = LayerMask.NameToLayer("Walls");
                go.AddComponent<TilemapBoxes>();
                return tm;
            }
            var r = go.AddComponent<TilemapRenderer>();
            r.sharedMaterial = material;
            r.mode = TilemapRenderer.Mode.Chunk;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.sortingOrder = -100;
            return tm;
        }

        // The ground tilemap of a level of raised land, made the first time that level is seen.
        Tilemap LevelMap(int level)
        {
            if (levelMaps.TryGetValue(level, out var tm)) return tm;
            tm = MakeTilemap("Level " + level, -level * WorldGen.LevelHeight, RenderAssets.Instance.litGround, false);
            levelMaps[level] = tm;
            return tm;
        }

        void Update2Falls()
        {
            // Falling water and lava slide down their faces.
            float t = Time.time;
            if (waterfallMat != null) waterfallMat.mainTextureOffset = new Vector2(0f, t * 1.6f);
            if (lavafallMat != null) lavafallMat.mainTextureOffset = new Vector2(0f, t * 0.5f);
        }

        // Load everything around a point right away (spawn / teleport).
        public void LoadAround(Vector2 p)
        {
            var c = WorldGen.ChunkOf(p);
            for (int dx = -Radius; dx <= Radius; dx++)
            for (int dy = -Radius; dy <= Radius; dy++)
                Load(new Vector2Int(c.x + dx, c.y + dy));
            heroChunk = c;
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            checkTimer -= Time.deltaTime;
            if (checkTimer <= 0f)
            {
                checkTimer = CameraFollow.CinematicActive ? 0.1f : 0.25f;
                // A cinematic flight streams the land round the camera's focus instead of the hero.
                var c = WorldGen.ChunkOf(CameraFollow.CinematicActive ? CameraFollow.CinematicFocus.G() : hero.transform.G());
                // Reveal the map around the hero.
                if (!CameraFollow.CinematicActive)
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        Profile.MarkExplored(new Vector2Int(c.x + dx, c.y + dy));
                if (c != heroChunk)
                {
                    heroChunk = c;
                    toLoad.Clear();
                    for (int dx = -Radius; dx <= Radius; dx++)
                    for (int dy = -Radius; dy <= Radius; dy++)
                    {
                        var k = new Vector2Int(c.x + dx, c.y + dy);
                        if (!chunks.ContainsKey(k)) toLoad.Add(k);
                    }
                    toLoad.Sort((a, b) => (a - c).sqrMagnitude.CompareTo((b - c).sqrMagnitude));
                    var unload = new List<Vector2Int>();
                    foreach (var k in chunks.Keys)
                        if (Mathf.Abs(k.x - c.x) > Radius + 1 || Mathf.Abs(k.y - c.y) > Radius + 1) unload.Add(k);
                    var unloadClock = System.Diagnostics.Stopwatch.StartNew();
                    foreach (var k in unload) Unload(k);
                    MaxUnloadMs = Mathf.Max(MaxUnloadMs, (float)unloadClock.Elapsed.TotalMilliseconds);
                }
            }
            Stream();
        }

        // ---------- Streaming ----------
        // New country is worked out a little each frame. A chunk took 40 to 150 ms to make in one go (most of it the
        // world generator: cells, levels, river bridges), and two were made per frame: a freeze at every chunk border.
        // Now the generator's answers for the next chunk are fetched ahead within a few milliseconds a frame (they
        // are cached, so building then finds them ready), and at most one chunk is built per frame.
        const float StreamBudgetMs = 4f;
        static readonly System.Diagnostics.Stopwatch streamClock = new System.Diagnostics.Stopwatch();
        Vector2Int warming = new Vector2Int(int.MinValue, 0);
        int warmStep, warmIndex;
        public static float LastStreamMs { get; private set; }
        // The longest each kind of streaming work has taken, in ms, since ResetStreamStats: for the performance checks.
        public static float MaxWarmMs, MaxBuildMs, MaxFurnishMs, MaxUnloadMs;
        public static void ResetStreamStats() { MaxWarmMs = MaxBuildMs = MaxFurnishMs = MaxUnloadMs = 0f; laps.Clear(); }
        static readonly Dictionary<string, float> laps = new Dictionary<string, float>();
        static readonly System.Diagnostics.Stopwatch lapClock = new System.Diagnostics.Stopwatch();
        static double lapLast;
        static void Lap(string name)
        {
            double now = lapClock.Elapsed.TotalMilliseconds; float ms = (float)(now - lapLast); lapLast = now;
            if (!laps.TryGetValue(name, out float max) || ms > max) laps[name] = ms;
        }
        public static string LapStats { get { var sb = new System.Text.StringBuilder(); foreach (var kv in laps) sb.Append(kv.Key + " " + kv.Value.ToString("0.0") + "; "); return sb.ToString(); } }
        public static string StreamStats => $"warm {MaxWarmMs:0.0} ms, build {MaxBuildMs:0.0} ms, scenery {MaxFurnishMs:0.0} ms, unload {MaxUnloadMs:0.0} ms";

        void Stream()
        {
            LastStreamMs = 0f;
            if (toLoad.Count == 0 && furnishing.Count == 0) return;
            streamClock.Restart();
            float budget = CameraFollow.CinematicActive ? 30f : StreamBudgetMs;
            if (furnishing.Count > 0)
            {
                furnishing.Dequeue()();   // the chunk built last frame gets its scenery
                MaxFurnishMs = Mathf.Max(MaxFurnishMs, (float)streamClock.Elapsed.TotalMilliseconds);
            }
            else
            {
                var k = toLoad[0];
                bool ready = Warm(k, budget);
                float warmed = (float)streamClock.Elapsed.TotalMilliseconds;
                MaxWarmMs = Mathf.Max(MaxWarmMs, warmed);
                if (ready)
                {
                    deferFurnish = !CameraFollow.CinematicActive;
                    Load(k);
                    deferFurnish = false;
                    toLoad.RemoveAt(0);
                    MaxBuildMs = Mathf.Max(MaxBuildMs, (float)streamClock.Elapsed.TotalMilliseconds - warmed);
                }
            }
            LastStreamMs = (float)streamClock.Elapsed.TotalMilliseconds;
        }

        bool OverBudget(float budget) => streamClock.Elapsed.TotalMilliseconds >= budget;

        // Fetches what building chunk c will ask the world generator, a slice at a time. True once it is all cached.
        bool Warm(Vector2Int c, float budget)
        {
            if (c != warming) { warming = c; warmStep = 0; warmIndex = 0; }
            lapClock.Restart(); lapLast = 0; int stepAtStart = warmStep;
            try {
            int S = WorldGen.ChunkSize, x0 = c.x * S, y0 = c.y * S;
            const int Margin = 20;   // how far round the chunk the land is fetched (stairs and slopes look that far)
            // River bridges, a few rows of a region at a time. Asked at the four corners of the margin: a question
            // about any tile works out the regions round that tile, so every tile fetched below must find them done.
            while (warmStep < 4)
            {
                if (!WorldGen.WarmBridges(x0 - Margin + (warmStep % 2) * (S + 2 * Margin), y0 - Margin + (warmStep / 2) * (S + 2 * Margin), () => OverBudget(budget))) return false;
                warmStep++;
            }
            // The land itself for the chunk and a wide margin round it: stairs and slopes are worked out a block at a
            // time from the land about them, and without this one question about a cell cost a whole block.
            if (warmStep == 4)
            {
                int m = S + 2 * Margin;
                for (; warmIndex < m * m; warmIndex++)
                {
                    if ((warmIndex & 7) == 0 && OverBudget(budget)) return false;
                    WorldGen.WarmLand(x0 - Margin + warmIndex % m, y0 - Margin + warmIndex / m);
                }
                warmStep = 30; warmIndex = 0;
            }
            // Staircases, a 16-tile block at a time, for every block the chunk and its border touch.
            if (warmStep == 30)
            {
                const int B = 16;
                int bx0 = Mathf.FloorToInt((x0 - 2) / (float)B), by0 = Mathf.FloorToInt((y0 - 2) / (float)B);
                int nb = Mathf.FloorToInt((x0 + S + 1) / (float)B) - bx0 + 1, nby = Mathf.FloorToInt((y0 + S + 1) / (float)B) - by0 + 1;
                for (; warmIndex < nb * nby; warmIndex++)
                    if (!WorldGen.WarmStairs((bx0 + warmIndex % nb) * B, (by0 + warmIndex / nb) * B, () => OverBudget(budget))) return false;
                warmStep = 40; warmIndex = 0;
            }
            // Cells and levels, with the border Load reads.
            int w = S + 2;
            if (warmStep == 40)
            {
                for (; warmIndex < w * w; warmIndex++)
                {
                    if (OverBudget(budget)) return false;
                    int x = x0 - 1 + warmIndex % w, y = y0 - 1 + warmIndex / w;
                    WorldGen.CellAt(x, y);
                    WorldGen.ElevAt(x, y);
                }
                warmStep = 5; warmIndex = 0;
            }
            // The slope surface at every corner (bent ground, slopes, paths over them).
            if (warmStep == 5)
            {
                int n = S + 3;
                for (; warmIndex < n * n; warmIndex++)
                {
                    if (OverBudget(budget)) return false;
                    WorldGen.SlopeVertexHeight(x0 - 1 + warmIndex % n, y0 - 1 + warmIndex / n);
                }
                warmStep = 6; warmIndex = 0;
            }
            // What stands in the chunk: each of these searches it.
            if (warmStep == 6) { if (OverBudget(budget)) return false; WorldGen.HasCache(c.x, c.y, out _); warmStep = 7; }
            if (warmStep == 7) { if (OverBudget(budget)) return false; WorldGen.HasSummit(c.x, c.y, out _); warmStep = 8; }
            if (warmStep == 8) { if (OverBudget(budget)) return false; WorldGen.HasDungeon(c.x, c.y, out _); WorldGen.HasChest(c.x, c.y, out _, out _); WorldGen.HasWaypoint(c.x, c.y, out _); warmStep = 9; }
            // Building is one piece of work: it gets a frame of its own.
            return !OverBudget(budget * 0.5f);
            } finally { Lap("warm step " + stepAtStart); }
        }

        void LateUpdate() { DrawGrass(); Update2Falls(); }

        // ---------- Building a chunk ----------

        void Load(Vector2Int c)
        {
            if (chunks.ContainsKey(c)) return;
            int S = WorldGen.ChunkSize;
            int x0 = c.x * S, y0 = c.y * S;
            var chunk = new Chunk { coord = c, root = new GameObject($"Chunk {c.x},{c.y}") };
            chunk.root.transform.SetParent(transform, false);
            chunks[c] = chunk;

            var cells = new Cell[S + 2, S + 2]; // with a 1-cell border for edge checks
            var elev = new int[S + 2, S + 2];
            for (int y = -1; y <= S; y++)
            for (int x = -1; x <= S; x++)
            {
                cells[x + 1, y + 1] = WorldGen.CellAt(x0 + x, y0 + y);
                elev[x + 1, y + 1] = WorldGen.ElevAt(x0 + x, y0 + y);
            }

            lapClock.Restart(); lapLast = 0;
            var bounds = new BoundsInt(x0, y0, 0, S, S, 1);
            var groundTiles = new TileBase[S * S];
            var detailTiles = new TileBase[S * S];
            var levelTiles = new Dictionary<int, TileBase[]>();
            TileBase[] LevelTiles(int lv) { if (!levelTiles.TryGetValue(lv, out var a)) levelTiles[lv] = a = new TileBase[S * S]; return a; }
            var slopes = new Dictionary<Texture2D, Town.Solid>();   // grassy slopes between levels, by ground texture
            var slopeSides = new Town.Solid();
            var falls = new MeshBuilder();         // water falling down a cliff
            var lavaFalls = new MeshBuilder();
            var foam = new MeshBuilder();          // white water where a fall lands
            var blockTiles = new TileBase[S * S];
            var rockFaces = new MeshBuilder();
            var brickFaces = new MeshBuilder();
            var castleFaces = new MeshBuilder();
            var stairFaces = new MeshBuilder();
            var rims = new MeshBuilder();          // a lit lip along every top edge: what is raised reads at a glance
            var footShadows = new MeshBuilder();   // a dithered shadow on the ground at the foot of every wall
            var edges = new List<Vector2[]>();     // thin walls along plateau edges and stair sides: you stay on your level
            var water = new MeshBuilder();       // clear ponds
            var marshWater = new MeshBuilder();  // the marsh's own dark water
            var nowhere = new MeshBuilder();     // never drawn: water that would lie over a dungeon's stairwell
            var puddles = new MeshBuilder();
            var lava = new MeshBuilder();
            var planks = new Town.Solid();         // bridge decks and railings
            var paths = new Dictionary<Texture2D, PathMesh>();   // paths over the grass, rounded at the edges
            var decorRng = new System.Random((int)WorldGen.Hash(c.x, c.y));
            Biome chunkBiome = WorldGen.BiomeAt(new Vector2(x0 + S / 2f, y0 + S / 2f));
            chunk.grassBiome = chunkBiome;
            var blends = new Dictionary<Texture2D, PathMesh>();  // another biome's ground laid over the seam, blended
            pathBlends = new Dictionary<Texture2D, PathMesh>();
            PrepareGround(cells, elev, x0, y0);

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int wx = x0 + x, wy = y0 + y, i = y * S + x;
                var cell = cells[x + 1, y + 1];
                if (cell == Cell.Hub) continue;
                var biome = WorldGen.LookBiomeAt(new Vector2(wx + 0.5f, wy + 0.5f));
                // Dirt (paths, shores, river beds) follows the biomes' smooth outline, not their ragged per-tile one:
                // otherwise each tile of a shore could be another biome's dirt, a patchwork of squares.
                var pathBiome = WorldGen.LookBiomeAt(new Vector2(wx + 0.5f, wy + 0.5f), false);
                uint h = WorldGen.Hash(wx, wy);
                int lv = elev[x + 1, y + 1];
                float z = -lv * WorldGen.LevelHeight;
                // Bare rock high up a mountain.
                var groundBiome = lv >= WorldGen.RockLevel && biome != Biome.Ember ? Biome.Ruins : biome;
                // Where biomes meet, the cell is floored in the lowest of them round it and the others are blended
                // over it (AddGroundBlend), so the seam is a soft, ragged edge instead of a line between tiles.
                var baseBiome = groundLike[x + 1, y + 1] ? BaseBiome(x, y) : groundBiome;
                if (lv > 0 && cell != Cell.Bridge) AddRaisedSides(cells, elev, x, y, wx, wy, rockFaces, falls, lavaFalls, foam, rims, footShadows, edges);

                switch (cell)
                {
                    case Cell.Path:
                    {
                        // Grass under the path, and the path laid over it with its corners rounded (AddPathOverlay).
                        var grass = EnvArt.GroundTile(baseBiome, wx, wy) ?? GroundTile(baseBiome, h);
                        if (lv > 0) LevelTiles(lv)[i] = grass; else groundTiles[i] = grass;
                        break;
                    }
                    case Cell.Water:
                    case Cell.Lava:
                    {
                        blockTiles[i] = tiles.blocker;
                        // The pool lies on its own shore ground, its outer corners rounded off and a shore band along
                        // its open edges (TWF_Water), so ponds and lava pools read as one round body, not tiles.
                        var shore = EnvArt.PathTile(pathBiome, wx, wy) ?? PathTile(pathBiome, h);
                        if (lv > 0) LevelTiles(lv)[i] = shore; else groundTiles[i] = shore;
                        // A puddle running into a pond is the same water: no rounded corner or shore between them.
                        bool Same(int dx, int dy)
                        {
                            var n = cells[x + dx + 1, y + dy + 1];
                            if (cell == Cell.Water && n == Cell.Bridge) return true;   // the river runs on under a bridge
                            return elev[x + dx + 1, y + dy + 1] == lv && (n == cell || (cell == Cell.Water && n == Cell.Puddle));
                        }
                        var pool = WorldGen.InStairwell(wx, wy) ? nowhere : cell == Cell.Lava ? lava : WorldGen.BiomeAt(new Vector2(wx + 0.5f, wy + 0.5f)) == Biome.Marsh ? marshWater : water;
                        pool.Quad(new Vector3(wx, wy, z - 0.012f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1,
                            -1f, RoundCorners(Same), OpenSides(Same));
                        break;
                    }
                    case Cell.Bridge:
                    {
                        // What it crosses lies underneath on its own level: a river on its bed, or the floor of a gap
                        // between two stretches of high ground (WorldGen.SpanLevel)...
                        int under = WorldGen.BaseElevAt(wx, wy);
                        var below = WorldGen.LandCell(wx, wy);
                        float uh = under * WorldGen.LevelHeight;
                        bool wet = below == Cell.Water || below == Cell.Bridge || below == Cell.Puddle;
                        var floor = wet || below == Cell.Lava ? EnvArt.PathTile(pathBiome, wx, wy) ?? PathTile(pathBiome, h)
                            : EnvArt.GroundTile(groundBiome, wx, wy) ?? GroundTile(groundBiome, h);
                        if (under > 0) LevelTiles(under)[i] = floor; else groundTiles[i] = floor;
                        bool Wet(int dx, int dy)
                        {
                            var n = cells[x + dx + 1, y + dy + 1];
                            if (n == Cell.Bridge) { var nb = WorldGen.LandCell(wx + dx, wy + dy); return nb == Cell.Water || nb == Cell.Bridge || nb == Cell.Puddle || nb == Cell.Lava; }
                            return n == Cell.Water || n == Cell.Lava;
                        }
                        if (wet || below == Cell.Lava)
                        {
                            var river = WorldGen.InStairwell(wx, wy) ? nowhere : below == Cell.Lava ? lava : WorldGen.BiomeAt(new Vector2(wx + 0.5f, wy + 0.5f)) == Biome.Marsh ? marshWater : water;
                            river.Quad(new Vector3(wx, wy, -uh - 0.004f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1, -1f, RoundCorners(Wet), OpenSides(Wet));
                        }
                        // ...and the way crosses over it on a plank deck with a railing along each open side: the kit's
                        // bridge pieces (BridgeArt), or plain plank boxes without them.
                        float deck = lv * WorldGen.LevelHeight + 0.03f;
                        bool Open(int dx, int dy)
                        {
                            var n = cells[x + dx + 1, y + dy + 1];
                            return n != Cell.Bridge && (n == Cell.Water || n == Cell.Lava || elev[x + dx + 1, y + dy + 1] != lv);
                        }
                        bool kit = BridgeArt.PlaceTile(chunk.root.transform, new Vector2(wx, wy), deck, Open(0, 1), Open(1, 0), Open(0, -1), Open(-1, 0));
                        if (!kit) Town.Box(planks, planks, new Rect(wx, wy, 1, 1), deck - 0.22f, deck);
                        foreach (var s in Sides)
                        {
                            if (!Open(s.dx, s.dy)) continue;
                            var o = new Vector2(wx, wy) + s.o;
                            Rect Along(float from, float to, float thick)
                            {
                                Vector2 p = o + s.u * from, q = o + s.u * to + s.inward * thick;
                                return Rect.MinMaxRect(Mathf.Min(p.x, q.x), Mathf.Min(p.y, q.y), Mathf.Max(p.x, q.x), Mathf.Max(p.y, q.y));
                            }
                            if (!kit) Town.Box(planks, planks, Along(0f, 1f, 0.12f), deck, deck + 0.55f);
                            // A high bridge stands on a trestle down to the floor of the gap: a post at each end of the
                            // tile and rails between them, so it shows that there is no way through underneath.
                            if (lv > under)
                            {
                                float trestleTop = deck - 0.2f;
                                Town.Box(planks, planks, Along(0f, 0.14f, 0.14f), uh, trestleTop);
                                Town.Box(planks, planks, Along(0.86f, 1f, 0.14f), uh, trestleTop);
                                for (float t = uh + 0.75f; t < trestleTop - 0.3f; t += 0.9f)
                                    Town.Box(planks, planks, Along(0.14f, 0.86f, 0.08f), t, t + 0.14f);
                            }
                            EdgeWall(o, s.u, edges);
                        }
                        break;
                    }
                    case Cell.Plateau:
                        if (WorldGen.FortressAt(wx, wy, out var fortHere))
                        {
                            // A fortress is floored in its boss's own biome: stone only for the Bone King's hall.
                            var fb = WorldGen.BossBiome(fortHere.kind);
                            LevelTiles(lv)[i] = fortHere.kind == BossKind.Skeleton ? CastleFloorTile(wx, wy, h)
                                : EnvArt.GroundTile(fb, wx, wy) ?? GroundTile(fb, h >> 3);
                            if (fortHere.IsPuddle(wx, wy))
                                puddles.Quad(new Vector3(wx, wy, -WorldGen.PlateauHeight - 0.006f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1,
                                    -1f, RoundCorners((dx, dy) => fortHere.IsPuddle(wx + dx, wy + dy)), OpenSides((dx, dy) => fortHere.IsPuddle(wx + dx, wy + dy)));
                        }
                        else LevelTiles(lv)[i] = EnvArt.GroundTile(baseBiome, wx, wy) ?? GroundTile(baseBiome, h >> 3);
                        break;
                    case Cell.Stairs:
                        if (WorldGen.StairAt(wx, wy, out var flight, out _) && flight.natural)
                        {
                            // A grassy slope: the cell's own ground, tilted from one level up to the next. A path up it
                            // is laid over it like any other (AddPathOverlay), with the same soft edge.
                            var under = EnvArt.GroundTile(baseBiome, wx, wy) ?? GroundTile(baseBiome, h);
                            // Flat ground under the slope too, at its foot's level: nothing shows through anywhere.
                            if (lv > 0) LevelTiles(lv)[i] = under; else groundTiles[i] = under;
                            AddSlope(wx, wy, flight, under, slopes, slopeSides, edges);
                            break;
                        }
                        if (lv > 0) LevelTiles(lv)[i] = EnvArt.PathTile(pathBiome, wx, wy) ?? PathTile(pathBiome, h);
                        else groundTiles[i] = EnvArt.PathTile(pathBiome, wx, wy) ?? PathTile(pathBiome, h);
                        AddStairs(wx, wy, stairFaces, rims, edges);
                        break;
                    case Cell.Wall:
                        groundTiles[i] = EnvArt.GroundTile(biome, wx, wy) ?? GroundTile(biome, h);
                        blockTiles[i] = tiles.blocker;
                        AddBox(wx, wy, WallHeight, brickFaces);
                        break;
                    case Cell.CastleWall:
                    case Cell.Tower:
                    case Cell.Pillar:
                        groundTiles[i] = CastleFloorTile(wx, wy, h);
                        blockTiles[i] = tiles.blocker;
                        float top = HeightOf(cell);
                        AddBox(wx, wy, top, castleFaces);
                        AddMerlons(cells, x, y, wx, wy, cell, top, castleFaces);
                        break;
                    case Cell.CastleFloor:
                        groundTiles[i] = CastleFloorTile(wx, wy, h);
                        break;
                    case Cell.Puddle:
                        // Shallow water just over the ground: walked through, not round.
                    {
                        var under = EnvArt.GroundTile(baseBiome, wx, wy) ?? GroundTile(baseBiome, h);
                        if (lv > 0) LevelTiles(lv)[i] = under; else groundTiles[i] = under;
                    }
                    {
                        bool Wet(int dx, int dy)
                        {
                            var n = cells[x + dx + 1, y + dy + 1];
                            return n == Cell.Puddle || (n == Cell.Water && elev[x + dx + 1, y + dy + 1] == lv);
                        }
                        // A puddle running into a pond is drawn as the pond's own water: one colour, no squares in it.
                        var into = WorldGen.InStairwell(wx, wy) ? nowhere : !pondPuddle[x + 1, y + 1] ? puddles
                            : WorldGen.BiomeAt(new Vector2(wx + 0.5f, wy + 0.5f)) == Biome.Marsh ? marshWater : water;
                        into.Quad(new Vector3(wx, wy, z - (into == puddles ? 0.012f : 0.013f)), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1,
                            -1f, RoundCorners(Wet), OpenSides(Wet));
                    }
                        break;
                    default:
                    {
                        // On its own level's map: open ground up on a hill would otherwise be a hole in it.
                        var tile = EnvArt.GroundTile(baseBiome, wx, wy) ?? GroundTile(baseBiome, h);
                        if (lv > 0) LevelTiles(lv)[i] = tile;
                        else
                        {
                            groundTiles[i] = tile;
                            if ((h & 0xFF) < 14) detailTiles[i] = DetailTile(biome, h);
                        }
                    }
                        break;
                }
                // Ground next to a grassy slope bends to meet it. Where it bends down, its flat tile would cover the
                // dip, so the bent ground is all that is drawn there.
                if (WorldGen.Bends(cell) && AddBentGround(wx, wy, lv > 0 ? LevelTiles(lv)[i] : groundTiles[i], slopes))
                {
                    if (lv > 0) LevelTiles(lv)[i] = null; else groundTiles[i] = null;
                    detailTiles[i] = null;
                }
                // Snow lies on the high ground of the mountains (not in the ash wastes).
                bool snowy = lv >= WorldGen.SnowLevel && pathBiome != Biome.Ember && cell != Cell.Bridge && LevelTiles(lv)[i] != null;
                if (snowy) { LevelTiles(lv)[i] = SnowTile(h); detailTiles[i] = null; }
                // Other biomes' ground blended over this one's, where they meet.
                // Nothing is laid over a dungeon's stairwell: the ground there is cut away.
                if (WorldGen.InStairwell(wx, wy)) continue;
                if (groundLike[x + 1, y + 1] && !snowy) AddGroundBlend(x, y, wx, wy, lv, h, baseBiome, blends);
                // Paths: rounded at their outer corners, swept round the grass at their bends, and up the slopes.
                if (cell == Cell.Path || cell == Cell.Puddle || WorldGen.Bends(cell) || slopeCell[x + 1, y + 1])
                    AddPathOverlay(cells, elev, x, y, wx, wy, lv, cell, h, paths);
            }

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var cell = cells[x + 1, y + 1];
                if (!WorldGen.Solid(cell) || cell == Cell.Plateau) continue;
                AddRims(cells, x, y, x0 + x, y0 + y, rims);
                AddFootShadows(cells, x, y, x0 + x, y0 + y, footShadows);
            }

            Lap("cells");
            ground.SetTilesBlock(bounds, groundTiles);
            detail.SetTilesBlock(bounds, detailTiles);
            foreach (var kv in levelTiles) LevelMap(kv.Key).SetTilesBlock(bounds, kv.Value);
            blockers.SetTilesBlock(bounds, blockTiles);

            Lap("tilemaps");
            var cliffTex = art != null && art.Cliff(chunkBiome) != null ? art.Cliff(chunkBiome) : WorldTiles.CliffTexture(chunkBiome);
            var brickTex = art != null && art.brick != null ? art.brick : WorldTiles.BrickTexture();
            MeshObject(chunk.root, "Cliffs", rockFaces, WorldTiles.MeshMaterial("cliff" + chunkBiome, cliffTex), true);
            MeshObject(chunk.root, "Walls", brickFaces, WorldTiles.MeshMaterial("brick", brickTex), true);
            MeshObject(chunk.root, "Castle", castleFaces, WorldTiles.MeshMaterial("castle", WorldTiles.CastleTexture()), true);
            if (stairFaces.Count > 0)
            {
                var stairMat = WorldTiles.MeshMaterial("stairs", WorldTiles.CastleTexture());
                stairMat.SetFloat("_NoReveal", 1f);        // steps are walked on: they never fade in front of the hero
                MeshObject(chunk.root, "Stairs", stairFaces, stairMat, true);
            }
            MeshObject(chunk.root, "Rims", rims, WorldTiles.MeshMaterial("rim" + chunkBiome, WorldTiles.RimTexture(chunkBiome)), false);
            MeshObject(chunk.root, "FootShadows", footShadows, WorldTiles.FlatMaterial("footshadow", WorldTiles.FootShadowTexture()), false);
            Lap("walls");
            if (edges.Count > 0)
            {
                var eg = new GameObject("LevelEdges");
                eg.transform.SetParent(chunk.root.transform, false);
                eg.layer = LayerMask.NameToLayer("Walls");
                LevelPass.AddEdges(Phys.Prisms(eg, edges));
            }
            Lap("colliders");
            // Picked per cell, not per chunk: a pond crossing a chunk border keeps one colour.
            MeshObject(chunk.root, "Water", water, WaterTint("pond", new Color(0.14f, 0.5f, 0.74f), new Color(0.36f, 0.78f, 0.9f)), false);
            MeshObject(chunk.root, "Marsh Water", marshWater, WaterTint("marsh", RenderAssets.Instance.water.GetColor("_Deep"), RenderAssets.Instance.water.GetColor("_Shallow")), false);
            MeshObject(chunk.root, "Puddles", puddles, WaterTint("puddle", new Color(0.42f, 0.74f, 0.9f), new Color(0.7f, 0.92f, 1f)), false);
            MeshObject(chunk.root, "Lava", lava, LavaPool(), false);
            planks.Build(chunk.root.transform, "Bridges", Town.Mat("bridge", Town.PlankTexture()), true);
            foreach (var kv in blends) kv.Value.Build(chunk.root.transform, PathMaterial(kv.Key, 2f));
            foreach (var kv in paths) kv.Value.Build(chunk.root.transform, PathMaterial(kv.Key, 1f));
            foreach (var kv in pathBlends) kv.Value.Build(chunk.root.transform, PathMaterial(kv.Key, 2f));
            foreach (var kv in slopes) kv.Value.Build(chunk.root.transform, "Slopes", SlopeMaterial(kv.Key), false);
            slopeSides.Build(chunk.root.transform, "Slope Sides", WorldTiles.MeshMaterial("cliff" + chunkBiome, cliffTex), true);
            MeshObject(chunk.root, "Waterfalls", falls, WaterfallMaterial(), false);
            MeshObject(chunk.root, "Lavafalls", lavaFalls, LavafallMaterial(), false);
            MeshObject(chunk.root, "Foam", foam, WaterTint("foam", new Color(0.75f, 0.9f, 1f), new Color(0.97f, 1f, 1f)), false);
            if (falls.Count > 0) chunk.root.AddComponent<FallSpray>().Init(cells, elev, x0, y0);
            if (lava.Count > 0) PointLights.Attach(chunk.root.transform, new Color(1f, 0.45f, 0.15f), 10f, 1.4f, LavaCenter(cells, x0, y0).W(1.5f), true);

            // What stands on the ground (scenery, grass, wildlife, camps, landmarks) is a second piece of work: when the
            // chunk is streamed in ahead of the hero it gets a frame of its own, after the ground.
            Lap("meshes");
            System.Action furnish = () =>
            {
                if (!chunks.TryGetValue(c, out var live) || live != chunk || chunk.root == null) return;   // let go of meanwhile
                chunkElev = elev;
                PlaceDecor(chunk, cells, x0, y0, decorRng);
                PlaceGrass(chunk, cells, x0, y0, decorRng);
                AmbientLife.Build(chunk.root.transform, cells, x0, y0, c);
                PlaceCamps(chunk, cells, x0, y0);
                PlacePointsOfInterest(chunk, c);
            };
            if (deferFurnish) furnishing.Enqueue(furnish); else furnish();
        }

        bool deferFurnish;
        readonly Queue<System.Action> furnishing = new Queue<System.Action>();

        void Unload(Vector2Int c)
        {
            if (!chunks.TryGetValue(c, out var chunk)) return;
            int S = WorldGen.ChunkSize;
            var bounds = new BoundsInt(c.x * S, c.y * S, 0, S, S, 1);
            var empty = new TileBase[S * S];
            ground.SetTilesBlock(bounds, empty);
            detail.SetTilesBlock(bounds, empty);
            foreach (var tm in levelMaps.Values) tm.SetTilesBlock(bounds, empty);
            blockers.SetTilesBlock(bounds, empty);
            Destroy(chunk.root);
            chunks.Remove(c);
        }

        static bool At(Cell[,] cells, int x, int y, Cell c) => cells[x + 1, y + 1] == c;

        Vector2 LavaCenter(Cell[,] cells, int x0, int y0)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            int S = WorldGen.ChunkSize;
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                if (At(cells, x, y, Cell.Lava)) { sum += new Vector2(x0 + x + 0.5f, y0 + y + 0.5f); n++; }
            return n > 0 ? sum / n : new Vector2(x0, y0);
        }

        int[,] chunkElev;   // the level of every cell of the chunk being built (with its 1-cell border)

        static int Lv(int[,] elev, int x, int y) => elev[x + 1, y + 1];

        // Everything round the sides of a raised cell, toward each lower neighbour: a rock face (or falling water or
        // lava) from the neighbour's level up to this one, a lit rim on top, a shadow at the foot, and a thin wall
        // that keeps walkers on their level, open only where a flight of stairs arrives.
        void AddRaisedSides(Cell[,] cells, int[,] elev, int x, int y, int wx, int wy, MeshBuilder rock, MeshBuilder falls, MeshBuilder lavaFalls,
            MeshBuilder foam, MeshBuilder rims, MeshBuilder shadows, List<Vector2[]> edges)
        {
            float H = WorldGen.LevelHeight;
            int lv = Lv(elev, x, y);
            var self = cells[x + 1, y + 1];
            foreach (var s in Sides)
            {
                int nl = Lv(elev, x + s.dx, y + s.dy);
                var n = cells[x + s.dx + 1, y + s.dy + 1];
                // A high bridge leaving from this edge: the rock face under its end still stands, down to the floor of
                // the gap; the edge itself is open, the bridge's own deck.
                if (n == Cell.Bridge && nl == lv)
                {
                    int gapFloor = WorldGen.BaseElevAt(wx + s.dx, wy + s.dy);
                    if (gapFloor < lv && self != Cell.Water && self != Cell.Lava)
                        RockColumns(new Vector3(wx + s.so.x, wy + s.so.y, -gapFloor * H), s.su, s.outward, wx, wy, s.dx, s.dy, (lv - gapFloor) * H, -lv * H, rock, rims);
                    continue;
                }
                if (nl >= lv) continue;
                float zLow = -nl * H, zTop = -lv * H, height = (lv - nl) * H;
                // A slope or stair arriving here: open ground, no cliff.
                WorldGen.Stair st = default; int row = 0;
                bool arrival = n == Cell.Stairs && WorldGen.StairAt(wx + s.dx, wy + s.dy, out st, out row) && row == st.len - 1
                    && st.d.x == -s.dx && st.d.y == -s.dy && st.hi == lv;
                if (arrival && st.natural) continue;
                // A grassy slope running alongside, up to this level: the slope bends up to meet this edge (AddSlope),
                // so no rock face, rim or wall here: it is open ground.
                if (n == Cell.Stairs && st.natural && lv <= st.hi) continue;
                // The face: the side of this cell, from the neighbour's ground up to this one's.
                var o = new Vector3(wx + s.so.x, wy + s.so.y, zLow);
                if (self == Cell.Water || self == Cell.Lava)
                    (self == Cell.Water ? falls : lavaFalls).Quad(o, s.su, Vector3.back, new Vector3(s.outward.x, s.outward.y, 0f), 1, height);
                else RockColumns(o, s.su, s.outward, wx, wy, s.dx, s.dy, height, zTop, rock, rims);
                if (self == Cell.Water) continue;   // spray rises where it lands (FallSpray)
                if (self == Cell.Lava) continue;
                rims.Quad(new Vector3(wx + s.o.x, wy + s.o.y, zTop - 0.012f), s.u, s.inward, Vector3.back, 1, RimWidth, 1f);
                if (WorldGen.Standable(n)) shadows.Quad(new Vector3(wx + s.so.x, wy + s.so.y, zLow - 0.015f), s.su, s.outward, Vector3.back, 1, ShadowWidth, 1f);
                if (arrival) continue;
                EdgeWall(new Vector2(wx, wy) + s.o, s.u, edges);
            }
        }

        // A cliff face as three slabs of rock standing a little proud of each other (like stone columns), each
        // with a lit cap: the edge reads as rock, not as a wall. o = the face's start corner at the foot, u = along it.
        static void RockColumns(Vector3 o, Vector2 u2, Vector2 out2, int wx, int wy, int dx, int dy, float height, float zTop, MeshBuilder rock, MeshBuilder caps)
        {
            var u = new Vector3(u2.x, u2.y, 0f);
            var n = new Vector3(out2.x, out2.y, 0f);
            uint h = WorldGen.Hash(wx * 7 + dx * 3 + 1, wy * 13 + dy * 5 + 2);
            float w0 = 0.26f + (h & 15) / 15f * 0.14f, w1 = 0.28f + ((h >> 4) & 15) / 15f * 0.14f;
            float[] widths = { w0, w1, 1f - w0 - w1 };
            float[] depth = { 0.03f + ((h >> 8) & 15) / 15f * 0.16f, 0.03f + ((h >> 12) & 15) / 15f * 0.16f, 0.03f + ((h >> 16) & 15) / 15f * 0.16f };
            float t = 0f;
            for (int k = 0; k < 3; k++)
            {
                float wk = widths[k], dk = depth[k];
                // A slightly shorter slab now and then: a notch in the skyline of the cliff.
                float top = height - ((h >> (20 + k)) & 1) * 0.12f;
                rock.Quad(o + u * t + n * dk, u, Vector3.back, n, wk, top);
                caps.Quad(o + u * t + new Vector3(0f, 0f, -top) - n * 0.0f, u, n, Vector3.back, wk, dk, 1f);
                // The side of this slab where it stands proud of the next one (or of the cliff's ends).
                float prev = k == 0 ? 0f : depth[k - 1], next = k == 2 ? 0f : depth[k + 1];
                if (dk > prev) rock.Quad(o + u * t + n * prev, n, Vector3.back, -u, dk - prev, top);
                if (dk > next) rock.Quad(o + u * (t + wk) + n * next, n, Vector3.back, u, dk - next, top);
                t += wk;
            }
        }

        // A grassy slope over one tile of a natural flight, in the tile's own ground art. Its corners come from the
        // shared surface round slopes (WorldGen.SurfaceAt), so it meets the slopes and the bent ground beside it with no
        // gap. Toward water or anything built a skirt of the same ground falls away to it, with a thin wall to keep
        // walkers on the slope; toward ground that bends to meet it, nothing: you may step on and off from the side.
        void AddSlope(int wx, int wy, WorldGen.Stair st, TileBase tile, Dictionary<Texture2D, Town.Solid> slopes, Town.Solid sides, List<Vector2[]> edges)
        {
            WorldGen.SurfaceAt(wx, wy, out float h00, out float h10, out float h11, out float h01);
            float z00 = -h00, z10 = -h10, z11 = -h11, z01 = -h01;   // the meshes are built with up = -Z
            float CornerZ(Vector2 p) => p.x <= wx ? (p.y <= wy ? z00 : z01) : (p.y <= wy ? z10 : z11);
            var mb = SurfaceQuad(wx, wy, tile, z00, z10, z11, z01, slopes, out var Uv);
            foreach (var s in Sides)
            {
                if (s.dx * st.d.x + s.dy * st.d.y != 0) continue;
                int nx = wx + s.dx, ny = wy + s.dy;
                var nc = WorldGen.CellAt(nx, ny);
                if (nc == Cell.Stairs) continue;
                if (WorldGen.Bends(nc) && WorldGen.ElevAt(nx, ny) <= st.hi) continue;   // the ground there bends to meet this edge
                var p0 = new Vector2(wx, wy) + s.o; var p1 = p0 + s.u;
                float z0 = CornerZ(p0), z1 = CornerZ(p1);
                float nz = -WorldGen.ElevAt(nx, ny) * WorldGen.LevelHeight;
                if (mb != null && (z0 < nz - 0.01f || z1 < nz - 0.01f))
                {
                    // Down at the slope's own edge, leaning out only a little: a full tile out, a small drop would lie
                    // almost flat, a flap of ground over the water beside the slope.
                    float drop = nz - Mathf.Min(z0, z1);
                    var o = -s.inward * Mathf.Min(0.2f, drop * 0.25f);
                    var a0 = new Vector3(p0.x, p0.y, Mathf.Min(z0, nz)); var a1 = new Vector3(p1.x, p1.y, Mathf.Min(z1, nz));
                    var b1 = new Vector3(p1.x + o.x, p1.y + o.y, nz); var b0 = new Vector3(p0.x + o.x, p0.y + o.y, nz);
                    var sn = Vector3.Cross(a1 - a0, b0 - a0).normalized;
                    if (sn.z > 0f) sn = -sn;
                    Vector2 f0 = p0 - new Vector2(wx, wy), f1 = p1 - new Vector2(wx, wy), fin = s.inward * 0.35f;
                    mb.Quad(a0, a1, b1, b0, sn, Uv(f0.x, f0.y), Uv(f1.x, f1.y), Uv(f1.x + fin.x, f1.y + fin.y), Uv(f0.x + fin.x, f0.y + fin.y));
                }
                EdgeWall(p0, s.u, edges);
            }
        }

        // One tile of ground with its four corners at the given heights (old convention: up is -Z), in the tile's art.
        static Town.Solid SurfaceQuad(int wx, int wy, TileBase tile, float z00, float z10, float z11, float z01, Dictionary<Texture2D, Town.Solid> slopes, out System.Func<float, float, Vector2> uv)
        {
            uv = null;
            var sprite = (tile as Tile)?.sprite;
            if (sprite == null) return null;
            var tex = sprite.texture;
            if (!slopes.TryGetValue(tex, out var mb)) slopes[tex] = mb = new Town.Solid();
            var r = sprite.textureRect;
            System.Func<float, float, Vector2> Uv = (fx, fy) => new Vector2((r.x + Mathf.Clamp01(fx) * r.width) / tex.width, (r.y + Mathf.Clamp01(fy) * r.height) / tex.height);
            uv = Uv;
            var a = new Vector3(wx, wy, z00); var b = new Vector3(wx + 1, wy, z10);
            var c = new Vector3(wx + 1, wy + 1, z11); var d = new Vector3(wx, wy + 1, z01);
            // Each corner's normal from the lie of the land round it, so the shading runs smoothly over the tiles.
            float own = WorldGen.ElevAt(wx, wy) * WorldGen.LevelHeight;
            mb.Quad(a, b, c, d, CornerNormal(wx, wy, own), CornerNormal(wx + 1, wy, own), CornerNormal(wx + 1, wy + 1, own), CornerNormal(wx, wy + 1, own),
                Uv(0, 0), Uv(1, 0), Uv(1, 1), Uv(0, 1));
            return mb;
        }

        // The ground's normal at a grid corner (old convention: up = -Z), from the heights of the corners either side
        // of it: the surface round slopes where a slope touches them, else the level of the ground there - this tile's
        // own where it reaches that corner (the far side of a cliff is not this ground), else the highest round it.
        // The same for every tile sharing the corner, so the shading runs on over the seams between tiles.
        static Vector3 CornerNormal(int cx, int cy, float own)
        {
            float H(int x, int y)
            {
                float v = WorldGen.SlopeVertexHeight(x, y);
                if (!float.IsNaN(v)) return v;
                int ownLv = Mathf.RoundToInt(own / WorldGen.LevelHeight), top = int.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    int l = WorldGen.ElevAt(x - 1 + (k & 1), y - 1 + (k >> 1));
                    if (l == ownLv) return own;
                    top = Mathf.Max(top, l);
                }
                return top * WorldGen.LevelHeight;
            }
            float gx = (H(cx + 1, cy) - H(cx - 1, cy)) * 0.5f, gy = (H(cx, cy + 1) - H(cx, cy - 1)) * 0.5f;
            return new Vector3(-gx, -gy, -1f).normalized;
        }

        // Flat ground a slope touches bends to meet it (WorldGen.SurfaceAt): a triangle at an outside corner, a gentle
        // side along a slope. Drawn over the flat tile, which stays underneath where the ground only rises. True when
        // it dips below its level somewhere: then the flat tile would hide the dip and has to go.
        static bool AddBentGround(int wx, int wy, TileBase tile, Dictionary<Texture2D, Town.Solid> slopes)
        {
            if (tile == null || !WorldGen.SurfaceAt(wx, wy, out float h00, out float h10, out float h11, out float h01)) return false;
            if (SurfaceQuad(wx, wy, tile, -h00 - 0.004f, -h10 - 0.004f, -h11 - 0.004f, -h01 - 0.004f, slopes, out _) == null) return false;
            float own = WorldGen.ElevAt(wx, wy) * WorldGen.LevelHeight - 0.01f;
            return h00 < own || h10 < own || h11 < own || h01 < own;
        }

        // ---------- Rounded paths ----------
        // A path is drawn over grass as its own tiles, rounded like the ponds: a corner with grass on both sides is cut
        // to a quarter circle, and where a path bends, the corner of the grass tile inside the bend gets a curve of
        // path laid over it. On ground bent up beside a slope the path follows the bend.
        // Worn ground drawn as rounded path: paths, and the sand round a pool (its shore). Water, bridges and puddles
        // count as joined to it, so a shore runs right up to the water without rounding off there.
        // Per chunk being built (with its 1-cell border): the biome each cell's ground is drawn in, whether it is open
        // ground that blends with its neighbours, and whether it is a grassy slope.
        Biome[,] groundBiomes, pathBiomes;
        bool[,] groundLike, slopeCell, pondPuddle;
        int[,] blendElev;

        void PrepareGround(Cell[,] cells, int[,] elev, int x0, int y0)
        {
            int n = cells.GetLength(0);
            groundBiomes = new Biome[n, n]; pathBiomes = new Biome[n, n]; groundLike = new bool[n, n]; slopeCell = new bool[n, n]; blendElev = elev;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int wx = x0 + x - 1, wy = y0 + y - 1;
                var c = cells[x, y];
                var b = WorldGen.LookBiomeAt(new Vector2(wx + 0.5f, wy + 0.5f));
                bool slope = c == Cell.Stairs && WorldGen.StairAt(wx, wy, out var st, out _) && st.natural;
                slopeCell[x, y] = slope;
                bool fort = c == Cell.Plateau && WorldGen.FortressAt(wx, wy, out _);
                groundLike[x, y] = !fort && (c == Cell.Ground || c == Cell.Path || c == Cell.Plateau || c == Cell.Puddle || slope);
                // as drawn: open ground and puddles in their biome's grass, the rest bare rock high on a mountain
                bool rocky = c != Cell.Ground && c != Cell.Puddle && elev[x, y] >= WorldGen.RockLevel && b != Biome.Ember;
                groundBiomes[x, y] = rocky ? Biome.Ruins : b;
                pathBiomes[x, y] = WorldGen.LookBiomeAt(new Vector2(wx + 0.5f, wy + 0.5f), false);
            }
            // Puddles joined to a pond on their level (through other puddles too): they are drawn as the pond's water.
            pondPuddle = new bool[n, n];
            var open = new Queue<Vector2Int>();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (cells[x, y] == Cell.Water) open.Enqueue(new Vector2Int(x, y));
            while (open.Count > 0)
            {
                var p = open.Dequeue();
                foreach (var s in Sides)
                {
                    int qx = p.x + s.dx, qy = p.y + s.dy;
                    if (qx < 0 || qy < 0 || qx >= n || qy >= n || pondPuddle[qx, qy] || cells[qx, qy] != Cell.Puddle || elev[qx, qy] != elev[p.x, p.y]) continue;
                    pondPuddle[qx, qy] = true;
                    open.Enqueue(new Vector2Int(qx, qy));
                }
            }
        }

        // Do two cells' grounds run into each other? On one level, or by a slope; not across a cliff.
        bool Connected(int ax, int ay, int bx, int by) =>
            blendElev[ax + 1, ay + 1] == blendElev[bx + 1, by + 1] || slopeCell[ax + 1, ay + 1] || slopeCell[bx + 1, by + 1];

        // The biome a neighbour's ground counts as from this cell: its own where the two run together, else this one's.
        Biome BlendBiome(int x, int y, int qx, int qy)
        {
            if (groundLike[qx + 1, qy + 1] && Connected(x, y, qx, qy)) return groundBiomes[qx + 1, qy + 1];
            return groundBiomes[x + 1, y + 1];
        }

        Biome BaseBiome(int x, int y)
        {
            var b = groundBiomes[x + 1, y + 1];
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var q = BlendBiome(x, y, x + dx, y + dy);
                if (q < b) b = q;
            }
            return b;
        }

        // Each biome round this cell above its floor, laid over it with the soft edge of the paths: at each corner, how
        // much of the ground round it is that biome or one above it. The seam sits on the tile edge, ragged and dithered.
        void AddGroundBlend(int x, int y, int wx, int wy, int lv, uint h, Biome floor, Dictionary<Texture2D, PathMesh> blends)
        {
            for (var b = floor + 1; b <= Biome.Ember; b++)
            {
                bool any = false;
                for (int dx = -1; dx <= 1 && !any; dx++)
                for (int dy = -1; dy <= 1 && !any; dy++)
                    if (BlendBiome(x, y, x + dx, y + dy) == b) any = true;
                if (!any) continue;
                float Corner(int cx, int cy)
                {
                    int n = 0;
                    if (BlendBiome(x, y, x, y) >= b) n++;
                    if (BlendBiome(x, y, x + cx, y) >= b) n++;
                    if (BlendBiome(x, y, x, y + cy) >= b) n++;
                    if (BlendBiome(x, y, x + cx, y + cy) >= b) n++;
                    return n / 4f;
                }
                var tile = EnvArt.GroundTile(b, wx, wy) ?? GroundTile(b, h);
                OverlayQuad(wx, wy, lv, tile, new Vector4(Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1)), 0.005f + 0.0004f * (int)b, blends);
            }
        }

        // One tile of a blended overlay, over the ground's own surface (bent beside slopes, tilted on them).
        static void OverlayQuad(int wx, int wy, int lv, TileBase tile, Vector4 wear, float lift, Dictionary<Texture2D, PathMesh> meshes)
        {
            if (wear.x + wear.y + wear.z + wear.w < 0.01f) return;
            var sprite = (tile as Tile)?.sprite;
            if (sprite == null) return;
            float flat = lv * WorldGen.LevelHeight;
            if (!WorldGen.SurfaceAt(wx, wy, out float h00, out float h10, out float h11, out float h01)) h00 = h10 = h11 = h01 = flat;
            if (!meshes.TryGetValue(sprite.texture, out var pm)) meshes[sprite.texture] = pm = new PathMesh();
            float own = WorldGen.ElevAt(wx, wy) * WorldGen.LevelHeight;
            pm.Quad(new Vector3(wx, wy, -h00 - lift), new Vector3(wx + 1, wy, -h10 - lift), new Vector3(wx + 1, wy + 1, -h11 - lift), new Vector3(wx, wy + 1, -h01 - lift),
                CornerNormal(wx, wy, own), CornerNormal(wx + 1, wy, own), CornerNormal(wx + 1, wy + 1, own), CornerNormal(wx, wy + 1, own), sprite, wear);
        }

        // x, y: the cell asked about; ox, oy: the cell whose overlay is being built.
        bool Pathish(Cell[,] cells, int[,] elev, int x, int y, int ox, int oy, int wx, int wy)
        {
            var c = cells[x + 1, y + 1];
            if (c == Cell.Bridge || c == Cell.Water || c == Cell.Puddle) return true;
            if (c == Cell.Stairs) return WorldGen.OnPath(wx, wy);
            if (!Connected(ox, oy, x, y)) return false;
            return c == Cell.Path || (WorldGen.Bends(c) && Shore(cells, elev, x, y));
        }

        void AddPathOverlay(Cell[,] cells, int[,] elev, int x, int y, int wx, int wy, int lv, Cell cell, uint h, Dictionary<Texture2D, PathMesh> paths)
        {
            // How much of the ground round each corner is worn (0, 1/4 ... 1): the shader draws the path where the
            // blend of the four is over a half, with a wobble - an organic patch of dirt, round at its ends and corners.
            bool worn = cell == Cell.Path || cell == Cell.Puddle || (WorldGen.Bends(cell) && Shore(cells, elev, x, y)) || (slopeCell[x + 1, y + 1] && WorldGen.OnPath(wx, wy));
            // Where a path runs from one biome into another its dirt changes too: the lowest biome's dirt round here is
            // laid first, and each other biome's over it where the path round a corner is that biome or one above it,
            // so the seam between the two dirts is as soft as the path's own edge, not a staircase along the tiles.
            Biome floor = pathBiomes[x + 1, y + 1], top = floor;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var q = pathBiomes[x + dx + 1, y + dy + 1];
                if (q < floor) floor = q;
                if (q > top) top = q;
            }
            float CornerWear(int cx, int cy, Biome b)   // cx, cy: -1 or +1, the corner's direction
            {
                int n = 0;
                for (int ox = 0; ox <= 1; ox++)
                for (int oy = 0; oy <= 1; oy++)
                {
                    int qx = ox == 0 ? 0 : cx, qy = oy == 0 ? 0 : cy;
                    if (pathBiomes[x + qx + 1, y + qy + 1] < b) continue;
                    if (qx == 0 && qy == 0 ? worn : Pathish(cells, elev, x + qx, y + qy, x, y, wx + qx, wy + qy)) n++;
                }
                return n / 4f;
            }
            for (var b = floor; b <= top; b++)
            {
                var tile = EnvArt.PathTile(b, wx, wy) ?? PathTile(b, h);
                var wear = new Vector4(CornerWear(-1, -1, b), CornerWear(1, -1, b), CornerWear(1, 1, b), CornerWear(-1, 1, b));
                // The lowest dirt edged with trodden grass (0.009, above the bent ground and the biome blends); the
                // others plain, just over it.
                if (b == floor) OverlayQuad(wx, wy, lv, tile, wear, 0.009f, paths);
                else OverlayQuad(wx, wy, lv, tile, wear, 0.0095f + 0.0003f * (int)b, pathBlends);
            }
        }

        // Another biome's dirt laid over a path's (AddPathOverlay), per chunk being built.
        Dictionary<Texture2D, PathMesh> pathBlends;

        static readonly Dictionary<(Texture2D, float), Material> pathMats = new Dictionary<(Texture2D, float), Material>();

        // kind 1: a path (its edge mixed with trodden grass); 2: another biome's ground, blended in plain.
        static Material PathMaterial(Texture2D tex, float kind)
        {
            if (pathMats.TryGetValue((tex, kind), out var m) && m != null) return m;
            m = new Material(RenderAssets.Instance.litGround) { name = (kind > 1.5f ? "Blend " : "Path ") + tex.name, mainTexture = tex };
            m.SetFloat("_PathMask", kind);
            m.SetFloat("_Terrain", 1f);         // on a slope it leans with it, and is shaded like it
            pathMats[(tex, kind)] = m;
            return m;
        }

        class PathMesh
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<Vector4> masks = new List<Vector4>();
            readonly List<Vector2> locals = new List<Vector2>();
            readonly List<int> tris = new List<int>();
            readonly List<Vector3> normals = new List<Vector3>();

            // a, b, c, d: the corners (x, y), (x+1, y), (x+1, y+1), (x, y+1), built with up = -Z.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Sprite sprite, Vector4 wear)
            {
                var r = sprite.textureRect; var tex = sprite.texture;
                Vector2 Uv(float fx, float fy) => new Vector2((r.x + fx * r.width) / tex.width, (r.y + fy * r.height) / tex.height);
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                uvs.Add(Uv(0, 0)); uvs.Add(Uv(1, 0)); uvs.Add(Uv(1, 1)); uvs.Add(Uv(0, 1));
                for (int k = 0; k < 4; k++) masks.Add(wear);
                // The ground's own normals (up = -Z here), so a patch on a slope is shaded like the slope under it.
                normals.Add(na); normals.Add(nb); normals.Add(nc); normals.Add(nd);
                locals.Add(new Vector2(0, 0)); locals.Add(new Vector2(1, 0)); locals.Add(new Vector2(1, 1)); locals.Add(new Vector2(0, 1));
                tris.AddRange(new[] { i, i + 3, i + 1, i + 1, i + 3, i + 2 });
            }

            public void Build(Transform parent, Material mat)
            {
                if (verts.Count == 0) return;
                var m = new Mesh { name = "Paths", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(verts);
                m.SetUVs(0, uvs);
                m.SetUVs(1, masks);
                m.SetUVs(2, locals);
                var colors = new Color[verts.Count];
                for (int k = 0; k < colors.Length; k++) colors[k] = Color.white;
                m.SetNormals(normals);
                m.colors = colors;
                m.SetTriangles(tris, 0);
                Ground.ToY(m);
                var go = new GameObject("Paths");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
        }

        static readonly Dictionary<Texture2D, Material> slopeMats = new Dictionary<Texture2D, Material>();

        static Material SlopeMaterial(Texture2D tex)
        {
            if (slopeMats.TryGetValue(tex, out var m) && m != null) return m;
            m = new Material(RenderAssets.Instance.litGround) { name = "Slope " + tex.name, mainTexture = tex };
            m.SetFloat("_Terrain", 1f);         // each face shaded by the way it leans: the height reads at a glance
            slopeMats[tex] = m;
            return m;
        }

        // Ground at the edge of water on its own level: sand and pebbles.
        static bool Shore(Cell[,] cells, int[,] elev, int x, int y)
        {
            int lv = Lv(elev, x, y);
            int n = cells.GetLength(0);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int ix = x + dx + 1, iy = y + dy + 1;
                if (ix < 0 || iy < 0 || ix >= n || iy >= n) continue;   // the chunk's border ring has no ring of its own
                if (cells[ix, iy] == Cell.Water && Lv(elev, x + dx, y + dy) == lv) return true;
            }
            return false;
        }

        // ---------- Raised blocks: heights, rims, foot shadows, battlements ----------

        const float WallHeight = 1.6f, CastleWallHeight = 2.4f, TowerHeight = 3.6f;
        const float RimWidth = 0.2f, ShadowWidth = 0.55f;

        static float HeightOf(Cell c) => c == Cell.Plateau ? WorldGen.PlateauHeight : c == Cell.Wall ? WallHeight
            : c == Cell.CastleWall ? CastleWallHeight : c == Cell.Tower ? TowerHeight : c == Cell.Pillar ? WorldGen.PlateauHeight + 3.2f : 0f;

        // The four sides of a cell: offset to the neighbour, then (origin, along, inward) of the top-face strip at
        // that edge, and (origin, along, outward) of the ground strip just outside it. Each pair keeps the
        // winding of a face that looks up at the camera.
        static readonly (int dx, int dy, Vector2 o, Vector2 u, Vector2 inward, Vector2 so, Vector2 su, Vector2 outward)[] Sides =
        {
            (0, -1, new Vector2(0, 0), Vector2.right, Vector2.up,    new Vector2(1, 0), Vector2.left,  Vector2.down),
            (0, 1,  new Vector2(1, 1), Vector2.left,  Vector2.down,  new Vector2(0, 1), Vector2.right, Vector2.up),
            (-1, 0, new Vector2(0, 1), Vector2.down,  Vector2.right, new Vector2(0, 0), Vector2.up,    Vector2.left),
            (1, 0,  new Vector2(1, 0), Vector2.up,    Vector2.left,  new Vector2(1, 1), Vector2.down,  Vector2.right),
        };

        // A thin wall along the side of a cell (o = the side's start corner, u = along it).
        static void EdgeWall(Vector2 o, Vector2 u, List<Vector2[]> edges)
        {
            const float t = 0.07f;
            var n = new Vector2(-u.y, u.x) * t;
            edges.Add(new[] { o - n, o + u - n, o + u + n, o + n });
        }

        // One cell of a flight: three stone steps rising toward the plateau, closed along its outer sides.
        void AddStairs(int wx, int wy, MeshBuilder mb, MeshBuilder rims, List<Vector2[]> edges)
        {
            if (!WorldGen.StairAt(wx, wy, out var st, out int row)) return;
            // Each tread sits at the slope's height at its lower edge, so whoever walks the flight stands on or just above
            // the stone and never sinks into the step ahead.
            const int steps = 4;
            float baseZ = st.lo * WorldGen.LevelHeight;
            float rise = (st.hi - st.lo) * WorldGen.LevelHeight / (st.len * steps);
            for (int k = 0; k < steps; k++)
            {
                float lo = k / (float)steps, size = 1f / steps;
                float top = (row * steps + k) * rise;
                if (top <= 0.001f) continue;
                float sx = st.d.x != 0 ? size : 1f, sy = st.d.y != 0 ? size : 1f;
                float ox = st.d.x > 0 ? lo : st.d.x < 0 ? 1f - lo - size : 0f;
                float oy = st.d.y > 0 ? lo : st.d.y < 0 ? 1f - lo - size : 0f;
                AddBlock(wx + ox, wy + oy, sx, sy, baseZ, top, mb);
                // a light lip along the front of each tread, so the steps read from above
                foreach (var s in Sides)
                {
                    if (s.dx != -st.d.x || s.dy != -st.d.y) continue;
                    var o = new Vector3(wx + ox + s.o.x * sx, wy + oy + s.o.y * sy, -baseZ - top - 0.012f);
                    rims.Quad(o, s.u, s.inward, Vector3.back, s.u.x != 0 ? sx : sy, 0.11f, 1f);
                }
            }
            foreach (var s in Sides)
            {
                if (s.dx * st.d.x + s.dy * st.d.y != 0) continue;                 // only the two sides across the flight
                if (WorldGen.CellAt(wx + s.dx, wy + s.dy) == Cell.Stairs) continue; // the flight's other column
                EdgeWall(new Vector2(wx, wy) + s.o, s.u, edges);
            }
        }

        // A light lip with a dark outer line along each top edge that drops to something lower: from any camera
        // angle a raised block's outline shows, even where its side face points away.
        void AddRims(Cell[,] cells, int x, int y, int wx, int wy, MeshBuilder mb)
        {
            float top = HeightOf(cells[x + 1, y + 1]);
            foreach (var s in Sides)
            {
                if (HeightOf(cells[x + s.dx + 1, y + s.dy + 1]) >= top - 0.01f) continue;
                var o = new Vector3(wx + s.o.x, wy + s.o.y, -top - 0.012f);
                mb.Quad(o, s.u, s.inward, Vector3.back, 1, RimWidth, 1f);
            }
        }

        // A dithered shadow on walkable ground along each side of a block, darkest at the foot.
        void AddFootShadows(Cell[,] cells, int x, int y, int wx, int wy, MeshBuilder mb)
        {
            foreach (var s in Sides)
            {
                var n = cells[x + s.dx + 1, y + s.dy + 1];
                if (!WorldGen.Walkable(n) || n == Cell.Hub) continue;
                // No shadow lies on a dungeon's stairwell: the ground there is cut away.
                if (WorldGen.InStairwell(wx + s.dx, wy + s.dy)) continue;
                var o = new Vector3(wx + s.so.x, wy + s.so.y, -0.015f);
                mb.Quad(o, s.su, s.outward, Vector3.back, 1, ShadowWidth, 1f);
            }
        }

        // Battlements: small blocks on every other tile along a castle wall's or tower's outer edges.
        void AddMerlons(Cell[,] cells, int x, int y, int wx, int wy, Cell self, float top, MeshBuilder mb)
        {
            const float size = 0.42f, rise = 0.42f;
            foreach (var s in Sides)
            {
                if (HeightOf(cells[x + s.dx + 1, y + s.dy + 1]) >= top - 0.01f) continue;
                if (((wx + wy) & 1) != 0) continue;
                var corner = new Vector2(wx, wy) + s.o + s.u * ((1 - size) / 2);
                var min = Vector2.Min(corner, corner + s.u * size + s.inward * size);
                AddBlock(min.x, min.y, size, size, top, rise, mb);
            }
        }

        // A box from z = -z0 up to -(z0 + height), its footprint (x, y, sx, sy).
        static void AddBlock(float x, float y, float sx, float sy, float z0, float height, MeshBuilder mb)
        {
            Vector3 up = Vector3.back;
            mb.Quad(new Vector3(x, y, -z0), Vector3.right, up, Vector3.down, sx, height);
            mb.Quad(new Vector3(x + sx, y + sy, -z0), Vector3.left, up, Vector3.up, sx, height);
            mb.Quad(new Vector3(x, y + sy, -z0), Vector3.down, up, Vector3.left, sy, height);
            mb.Quad(new Vector3(x + sx, y, -z0), Vector3.up, up, Vector3.right, sy, height);
            mb.Quad(new Vector3(x, y, -z0 - height), Vector3.right, Vector3.up, Vector3.back, sx, sy);
        }

        TileBase CastleFloorTile(int wx, int wy, uint h) => EnvArt.PathTile(Biome.Ruins, wx, wy) ?? PathTile(Biome.Ruins, h);

        // A short drop from the ground down to the water/lava surface, at the pool's own level.
        void AddDrop(Cell[,] cells, int[,] elev, int x, int y, int wx, int wy, Cell self, MeshBuilder mb)
        {
            const float d = 0.12f;
            int lv = Lv(elev, x, y);
            float z = -lv * WorldGen.LevelHeight;
            Vector3 down = Vector3.forward;
            bool Shore(int dx, int dy) => !At(cells, x + dx, y + dy, self) && Lv(elev, x + dx, y + dy) >= lv;
            if (Shore(0, -1)) mb.Quad(new Vector3(wx, wy, z), Vector3.right, down, Vector3.up, 1, d);
            if (Shore(0, 1)) mb.Quad(new Vector3(wx + 1, wy + 1, z), Vector3.left, down, Vector3.down, 1, d);
            if (Shore(-1, 0)) mb.Quad(new Vector3(wx, wy + 1, z), Vector3.down, down, Vector3.right, 1, d);
            if (Shore(1, 0)) mb.Quad(new Vector3(wx + 1, wy, z), Vector3.up, down, Vector3.left, 1, d);
        }

        void AddBox(int wx, int wy, float height, MeshBuilder mb)
        {
            Vector3 up = Vector3.back;
            mb.Quad(new Vector3(wx, wy, 0), Vector3.right, up, Vector3.down, 1, height);
            mb.Quad(new Vector3(wx + 1, wy + 1, 0), Vector3.left, up, Vector3.up, 1, height);
            mb.Quad(new Vector3(wx, wy + 1, 0), Vector3.down, up, Vector3.left, 1, height);
            mb.Quad(new Vector3(wx + 1, wy, 0), Vector3.up, up, Vector3.right, 1, height);
            mb.Quad(new Vector3(wx, wy, -height), Vector3.right, Vector3.up, Vector3.back, 1, 1);
        }

        static Material waterfallMat, lavafallMat;

        // Falling water: bright streaks that slide down the face (the offset moves every frame).
        static Material WaterfallMaterial()
        {
            if (waterfallMat != null) return waterfallMat;
            waterfallMat = new Material(WorldTiles.MeshMaterial("waterfall", FallTexture(new Color(0.3f, 0.62f, 0.86f), new Color(0.62f, 0.86f, 0.98f), new Color(0.95f, 1f, 1f), 5)));
            waterfallMat.name = "Waterfall";
            waterfallMat.SetFloat("_Emission", 0.35f);
            waterfallMat.SetFloat("_NoReveal", 1f);
            return waterfallMat;
        }

        static Material LavafallMaterial()
        {
            if (lavafallMat != null) return lavafallMat;
            lavafallMat = new Material(WorldTiles.MeshMaterial("lavafall", FallTexture(new Color(0.75f, 0.2f, 0.05f), new Color(1f, 0.5f, 0.1f), new Color(1f, 0.85f, 0.35f), 9)));
            lavafallMat.name = "Lavafall";
            lavafallMat.SetFloat("_Emission", 1.2f);
            lavafallMat.SetFloat("_NoReveal", 1f);
            return lavafallMat;
        }

        // Vertical streaks in three tones at the art's 72 px per unit, tiling every unit.
        static Texture2D FallTexture(Color deep, Color mid, Color light, int seed)
        {
            const int N = 72;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "Fall", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var px = new Color[N * N];
            var rng = new System.Random(seed);
            var cols = new float[N];
            for (int x = 0; x < N; x++) cols[x] = (float)rng.NextDouble();
            for (int x = 0; x < N; x++)
            {
                int len = 6 + rng.Next(20), off = rng.Next(N);
                for (int y = 0; y < N; y++)
                {
                    float v = cols[x / 3 * 3];
                    bool streak = ((y + off) % N) < len;
                    px[y * N + x] = streak ? (v > 0.55f ? light : mid) : (v > 0.8f ? mid : deep);
                }
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        // The marsh's water material in other colours: clear ponds, and light shallow puddles.
        static readonly Dictionary<string, Material> waterTints = new Dictionary<string, Material>();

        static Material lavaPool;
        static Material LavaPool()
        {
            if (lavaPool) return lavaPool;
            lavaPool = new Material(RenderAssets.Instance.lava) { name = "Lava_pool" };
            lavaPool.SetFloat("_Round", 1f);
            return lavaPool;
        }

        static Material WaterTint(string key, Color deep, Color shallow)
        {
            if (waterTints.TryGetValue(key, out var m) && m) return m;
            m = new Material(RenderAssets.Instance.water) { name = "Water_" + key };
            m.SetColor("_Deep", deep);
            m.SetColor("_Shallow", shallow);
            if (key != "foam") m.SetFloat("_Round", 1f);   // round pools and puddles, not square tiles of water
            waterTints[key] = m;
            return m;
        }

        void MeshObject(GameObject parent, string name, MeshBuilder mb, Material material, bool castShadows)
        {
            if (mb.Count == 0 || material == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        // ---------- Tile choice ----------

        TileBase Pick(TileBase[] arr, uint h) => arr[h % (uint)arr.Length];

        // Snow: drawn here, a few tiles of soft white with blue hollows and a glint or two, on the art's pixel grid.
        static Tile[] snowTiles;

        static TileBase SnowTile(uint h)
        {
            if (snowTiles == null || snowTiles[0] == null)
            {
                snowTiles = new Tile[6];
                for (int v = 0; v < snowTiles.Length; v++)
                {
                    const int N = 18, P = 4;
                    var tex = new Texture2D(N * P, N * P, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    var rng = new System.Random(911 + v * 37);
                    var px = new Color32[N * P * N * P];
                    for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        double r = rng.NextDouble();
                        float drift = Mathf.PerlinNoise((x + v * 31) * 0.22f, (y + v * 17) * 0.22f);
                        Color c = drift < 0.3f ? new Color(0.72f, 0.82f, 0.97f) : drift < 0.5f ? new Color(0.84f, 0.91f, 1f) : new Color(0.93f, 0.97f, 1f);
                        if (r < 0.03) c = Color.white; else if (r < 0.07) c = new Color(0.64f, 0.76f, 0.95f);
                        for (int j = 0; j < P; j++) for (int k = 0; k < P; k++) px[(y * P + j) * N * P + x * P + k] = c;
                    }
                    tex.SetPixels32(px); tex.Apply();
                    var tile = ScriptableObject.CreateInstance<Tile>();
                    tile.sprite = Sprite.Create(tex, new Rect(0, 0, N * P, N * P), new Vector2(0.5f, 0.5f), N * P, 0, SpriteMeshType.FullRect);
                    snowTiles[v] = tile;
                }
            }
            return snowTiles[h % (uint)snowTiles.Length];
        }

        TileBase GroundTile(Biome b, uint h)
        {
            if (art != null && art.Ground(b) != null && art.Ground(b).Length > 0) return Pick(art.Ground(b), h);
            switch (b)
            {
                case Biome.Ruins: return Pick(tiles.ruinsFloor, h);
                case Biome.Ember: return Pick(tiles.ash, h);
                default: return (h % 9 == 0) ? Pick(tiles.grassDetail, h >> 4) : Pick(tiles.grassPlain, h >> 4);
            }
        }

        TileBase PathTile(Biome b, uint h)
        {
            if (art != null && art.Path(b) != null && art.Path(b).Length > 0) return Pick(art.Path(b), h);
            switch (b)
            {
                case Biome.Ruins: return tiles.ruinsPath;
                case Biome.Ember: return tiles.emberPath;
                default: return Pick(tiles.dirt, h);
            }
        }

        // Grass creeping onto the edges of paths, picked by which neighbours are grass.
        TileBase FringeTile(Cell[,] cells, int x, int y, Biome b)
        {
            var set = art != null ? art.Fringe(b) : null;
            if (set == null && EnvArt.FringeTile(b, 1) == null) return null;
            int mask = 0;
            if (At(cells, x, y + 1, Cell.Ground)) mask |= 1;
            if (At(cells, x + 1, y, Cell.Ground)) mask |= 2;
            if (At(cells, x, y - 1, Cell.Ground)) mask |= 4;
            if (At(cells, x - 1, y, Cell.Ground)) mask |= 8;
            var painted = EnvArt.FringeTile(b, mask);
            if (painted != null) return painted;
            return mask > 0 && set != null ? set[mask] : null;
        }

        TileBase DetailTile(Biome b, uint h)
        {
            if (art != null) return null; // the 64px ground already has its detail
            switch (b)
            {
                case Biome.Ruins: return tiles.ruinsRubble;
                case Biome.Ember: return Pick(tiles.emberCrack, h);
                default: return null;
            }
        }

        // ---------- Decor ----------

        void PlaceDecor(Chunk chunk, Cell[,] cells, int x0, int y0, System.Random rng)
        {
            int S = WorldGen.ChunkSize;
            for (int y = 1; y < S - 1; y++)
            for (int x = 1; x < S - 1; x++)
            {
                if (!OpenGround(cells, x, y, x0, y0, 1)) continue;
                if (NearPath(cells, x, y)) continue;
                int wx = x0 + x, wy = y0 + y;
                if (WorldGen.InFarmland(wx, wy)) continue;          // the farms are kept clear
                var biome = WorldGen.LookBiomeAt(new Vector2(wx + 0.5f, wy + 0.5f));
                int lv = Lv(chunkElev, x, y);
                // Forest density comes in patches.
                float density = Mathf.PerlinNoise(wx * 0.06f + 300f, wy * 0.06f + 300f);
                float chance = biome == Biome.Forest ? Mathf.Max(0f, density - 0.45f) * 0.35f
                    : biome == Biome.Marsh ? 0.025f : biome == Biome.Ruins ? 0.02f : 0.018f;
                if (rng.NextDouble() > chance) continue;
                // 3D trees, bushes and rocks from the environment kit; the old sprites where none are installed.
                if (Nature(chunk.root.transform, biome, lv, new Vector2(wx + 0.5f, wy + 0.5f), rng)) continue;
                // Above the tree line only rocks; the mountains are bare.
                var sprite = lv >= WorldGen.RockLevel ? (art != null ? (rng.NextDouble() < 0.5 ? Any(art.rockLarge, rng) : Any(art.rockSmall, rng)) : null) : DecorSprite(biome, rng);
                if (sprite == null) continue;
                bool solid = !sprite.name.StartsWith("bush") && !sprite.name.StartsWith("fern");
                Decor(chunk.root.transform, sprite, new Vector3(wx + 0.5f, lv * WorldGen.LevelHeight, wy + 0.3f), biome, solid, rng);
            }
        }

        static readonly Dictionary<Biome, (string name, float w)[]> NatureMix = new Dictionary<Biome, (string, float)[]>
        {
            { Biome.Forest, new[] { ("tree_green", 0.3f), ("tree_green2", 0.16f), ("tree_red", 0.1f), ("tree_orange", 0.1f), ("tree_pine", 0.14f), ("bush_green", 0.08f), ("bush_flower", 0.05f), ("bush_red", 0.03f), ("rock_grey", 0.04f) } },
            { Biome.Marsh, new[] { ("tree_marsh", 0.4f), ("tree_dead", 0.22f), ("bush_green", 0.2f), ("rock_grey", 0.18f) } },
            { Biome.Ruins, new[] { ("rock_grey", 0.4f), ("tree_dead", 0.25f), ("bush_green", 0.2f), ("tree_pine", 0.15f) } },
            { Biome.Ember, new[] { ("rock_ash", 0.5f), ("tree_ember", 0.35f), ("tree_dead", 0.15f) } },
        };

        // A tree, bush or rock mesh at p (turned and sized at random). False when none are installed for this biome.
        bool Nature(Transform parent, Biome biome, int lv, Vector2 p, System.Random rng)
        {
            // Off: the pixel-art trees, bushes and rocks read better than the kit's meshes (set true to bring them back).
            if (!EnvArt.MeshNature || !EnvMesh.Has("tree_green")) return false;
            string name;
            if (lv >= WorldGen.RockLevel) name = biome == Biome.Ember ? "rock_ash" : rng.NextDouble() < 0.25 ? "tree_pine" : "rock_grey";
            else
            {
                if (!NatureMix.TryGetValue(biome, out var mix)) mix = NatureMix[Biome.Forest];
                double r = rng.NextDouble(), acc = 0;
                name = mix[mix.Length - 1].name;
                foreach (var m in mix) { acc += m.w; if (r < acc) { name = m.name; break; } }
            }
            var go = EnvMesh.Place(name, parent, p + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.6f,
                lv * WorldGen.LevelHeight, (float)rng.NextDouble() * 360f);
            if (go == null) return false;
            float sc = 0.85f + (float)rng.NextDouble() * 0.4f;
            go.transform.localScale = new Vector3(sc, sc, sc);
            return true;
        }

        // Open ground with room round it: ground cells all on one level, r tiles from any edge, outside the boss
        // fortresses (whose high ground is kept for the fight).
        bool OpenGround(Cell[,] cells, int x, int y, int x0, int y0, int r)
        {
            int n = cells.GetLength(0) - 2;
            if (x - r < -1 || y - r < -1 || x + r > n || y + r > n) return false;
            int lv = Lv(chunkElev, x, y);
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                var c = cells[x + dx + 1, y + dy + 1];
                if ((c != Cell.Ground && c != Cell.Plateau) || Lv(chunkElev, x + dx, y + dy) != lv) return false;
            }
            return lv == 0 || !WorldGen.FortressAt(x0 + x, y0 + y, out _);
        }

        static bool NearPath(Cell[,] cells, int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (At(cells, x + dx, y + dy, Cell.Path)) return true;
            return false;
        }

        static Sprite Any(Sprite[] arr, System.Random rng) => arr != null && arr.Length > 0 ? arr[rng.Next(arr.Length)] : null;

        Sprite DecorSprite(Biome b, System.Random rng)
        {
            double r = rng.NextDouble();
            if (art != null)
            {
                Sprite s;
                switch (b)
                {
                    case Biome.Forest: s = r < 0.35 ? Any(art.oak, rng) : r < 0.6 ? Any(art.pine, rng) : r < 0.75 ? Any(art.birch, rng) : r < 0.87 ? Any(art.bush, rng) : r < 0.95 ? Any(art.fern, rng) : Any(art.rockSmall, rng); break;
                    case Biome.Marsh: s = r < 0.35 ? Any(art.willow, rng) : r < 0.6 ? Any(art.deadTree, rng) : r < 0.75 ? Any(art.mushroomPurple, rng) : r < 0.85 ? Any(art.rockMossy, rng) : Any(art.fern, rng); break;
                    case Biome.Ruins: s = r < 0.25 ? Any(art.pillar, rng) : r < 0.55 ? Any(art.brokenPillar, rng) : r < 0.75 ? Any(art.rockLarge, rng) : r < 0.9 ? Any(art.deadTree, rng) : Any(art.rockSmall, rng); break;
                    default: s = r < 0.45 ? Any(art.charredTree, rng) : r < 0.7 ? Any(art.rockLarge, rng) : r < 0.85 ? Any(art.rockSmall, rng) : Any(art.mushroomRed, rng); break;
                }
                if (s != null) return s;
            }
            switch (b)
            {
                case Biome.Forest: return r < 0.42 ? tiles.treeOrange : r < 0.84 ? tiles.treePink : r < 0.92 ? tiles.trunk : tiles.bushTall;
                case Biome.Marsh: return r < 0.55 ? tiles.treeDried : r < 0.75 ? tiles.trunk : tiles.bushTall;
                case Biome.Ruins: return r < 0.15 ? tiles.rockMonument : r < 0.75 ? tiles.rock : tiles.trunk;
                default: return r < 0.5 ? tiles.rock : r < 0.85 ? tiles.treeDried : tiles.trunk;
            }
        }

        static Color DecorTint(Biome b, Sprite s)
        {
            switch (b)
            {
                case Biome.Marsh: return new Color(0.72f, 0.78f, 0.86f);
                case Biome.Ruins: return s.name.StartsWith("rock") ? new Color(0.88f, 0.88f, 0.92f) : new Color(0.6f, 0.6f, 0.6f);
                case Biome.Ember: return s.name.StartsWith("rock") ? new Color(0.58f, 0.42f, 0.4f) : new Color(0.32f, 0.24f, 0.22f);
                default: return Color.white;
            }
        }

        void Decor(Transform parent, Sprite sprite, Vector3 pos, Biome biome, bool solid, System.Random rng)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("Walls");
            float s = 0.9f + (float)rng.NextDouble() * 0.25f;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(rng.NextDouble() < 0.5 ? -s : s, s, 1f);
            visual.AddComponent<Billboard>();
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = art != null ? Color.white : DecorTint(biome, sprite);
            bool sways = sprite.name.StartsWith("tree") || sprite.name.StartsWith("bush") || sprite.name.StartsWith("fern");
            // The card casts nothing itself: its shadow comes from a low shape at its foot (EnvArt.FootShadow).
            RenderAssets.MakeLit(sr, false, sways ? RenderAssets.Instance.litWind : null);
            EnvArt.FootShadow(go.transform, Mathf.Clamp(sprite.bounds.size.x * s * 0.7f, 0.5f, 3.2f));
            if (solid)
            {
                Phys.Circle(go, Mathf.Clamp(sprite.bounds.size.x * s * 0.18f, 0.3f, 1.4f));
            }
        }

        // ---------- Grass ----------

        void PlaceGrass(Chunk chunk, Cell[,] cells, int x0, int y0, System.Random rng)
        {
            int S = WorldGen.ChunkSize;
            const int count = 300;
            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(S), y = rng.Next(S);
                if (!OpenGround(cells, x, y, x0, y0, 0) || WorldGen.InFarmland(x0 + x, y0 + y)) continue;
                int glv = Lv(chunkElev, x, y);
                if (glv >= WorldGen.RockLevel) continue;
                var p = new Vector3(x0 + x + (float)rng.NextDouble(), glv * WorldGen.LevelHeight, y0 + y + (float)rng.NextDouble());
                float sc = 0.7f + (float)rng.NextDouble() * 0.6f;
                // Store position + scale; rotation is applied each frame from the camera.
                // Each tuft takes the look of the ground it stands on (the z scale carries the biome).
                var gb = WorldGen.LookBiomeAt(p.G());
                if (gb == Biome.Ruins || (gb == Biome.Ember && rng.NextDouble() > 0.1)) continue;
                chunk.grass.Add(Matrix4x4.TRS(p, Quaternion.identity, new Vector3(sc, sc, 1f + (int)gb)));
            }
        }

        readonly Dictionary<Biome, List<Matrix4x4>> grassBatches = new Dictionary<Biome, List<Matrix4x4>>();

        void DrawGrass()
        {
            var hero = HeroController.Instance;
            if (hero == null || grassQuad == null) return;
            var hc = WorldGen.ChunkOf(CameraFollow.CinematicActive ? CameraFollow.CinematicFocus.G() : hero.transform.G());
            foreach (var list in grassBatches.Values) list.Clear();
            var rot = View.Rotation;
            foreach (var chunk in chunks.Values)
            {
                if (chunk.grass.Count == 0 || Mathf.Abs(chunk.coord.x - hc.x) > 2 || Mathf.Abs(chunk.coord.y - hc.y) > 2) continue;
                foreach (var m in chunk.grass)
                {
                    var gb = (Biome)(Mathf.RoundToInt(m.m22) - 1);
                    if (!grassBatches.TryGetValue(gb, out var batch)) grassBatches[gb] = batch = new List<Matrix4x4>();
                    batch.Add(Matrix4x4.TRS(m.GetColumn(3), rot, new Vector3(m.m00, m.m11, 1f)));
                }
            }
            foreach (var kv in grassBatches)
            {
                if (kv.Value.Count == 0) continue;
                var rp = new RenderParams(WorldTiles.GrassMaterial(kv.Key))
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = 0,
                };
                for (int start = 0; start < kv.Value.Count; start += 1023)
                {
                    int n = Mathf.Min(1023, kv.Value.Count - start);
                    Graphics.RenderMeshInstanced(rp, grassQuad, 0, kv.Value, n, start);
                }
            }
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "GrassQuad" };
            m.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }

        // ---------- Monsters and points of interest ----------

        void PlaceCamps(Chunk chunk, Cell[,] cells, int x0, int y0)
        {
            int S = WorldGen.ChunkSize;
            for (int k = 0; k < WorldGen.CampSlots(x0 + S / 2, y0 + S / 2); k++)
            {
                uint h = WorldGen.Hash(x0 * 31 + k, y0 * 17 - k);
                if ((h & 3) == 0) continue; // ~75% of slots used
                int x = 3 + (int)((h >> 4) % (uint)(S - 6)), y = 3 + (int)((h >> 12) % (uint)(S - 6));
                bool open = true;
                int campLv = Lv(chunkElev, x, y);
                for (int dx = -2; dx <= 2 && open; dx++)
                for (int dy = -2; dy <= 2 && open; dy++)
                    if (!WorldGen.Walkable(cells[x + dx + 1, y + dy + 1]) || cells[x + dx + 1, y + dy + 1] == Cell.Hub
                        || cells[x + dx + 1, y + dy + 1] == Cell.CastleFloor || Lv(chunkElev, x + dx, y + dy) != campLv) open = false;   // castles place their own
                // Packs live up on the high ground too, with room so none of them spawns over the edge.
                if (!open) open = OpenGround(cells, x, y, x0, y0, 3);
                // Broken country (terraces, canyons, mountainsides) has few such spots: the camp looks round its chunk
                // for a level patch instead of being dropped, or the hills would stand empty.
                for (int tries = 1; !open && tries <= 16; tries++)
                {
                    uint h2 = WorldGen.Hash(x0 * 31 + k + tries * 977, y0 * 17 - k + tries * 131);
                    int ax = 3 + (int)((h2 >> 4) % (uint)(S - 6)), ay = 3 + (int)((h2 >> 12) % (uint)(S - 6));
                    if (!OpenGround(cells, ax, ay, x0, y0, 2)) continue;
                    x = ax; y = ay; campLv = Lv(chunkElev, x, y); open = true;
                }
                if (!open) continue;
                var pos = new Vector2(x0 + x + 0.5f, y0 + y + 0.5f);
                if (pos.magnitude < WorldGen.SafeRadius) continue;
                // Lairs (a goblin camp, a spider nest, a den) stand in every other chunk each way, at most one per chunk,
                // so two of them are never side by side.
                int cx = Mathf.FloorToInt(x0 / (float)S), cy = Mathf.FloorToInt(y0 / (float)S);
                if (k == 0 && (cx & 1) == 0 && (cy & 1) == 0 && ((h >> 24) & 3) != 0 && pos.magnitude > WorldGen.FarmRingOuter && LairFits(cells, x, y, x0, y0))
                {
                    PlaceLair(chunk, pos, h);
                    continue;
                }
                var camp = new GameObject("Camp").AddComponent<MonsterCamp>();
                camp.transform.SetParent(chunk.root.transform, false);
                camp.transform.position = pos.W(campLv * WorldGen.LevelHeight);
                camp.zone = WorldGen.RegionAt(pos);
                camp.fixedLevel = WorldGen.LevelAt(pos);
                camp.seed = (int)(h & 0x7FFFFFFF);
            }
        }

        // A lair needs open low ground 4 tiles round its centre.
        bool LairFits(Cell[,] cells, int x, int y, int x0, int y0)
        {
            for (int dx = -4; dx <= 4; dx++)
            for (int dy = -4; dy <= 4; dy++)
            {
                var cl = cells[x + dx + 1, y + dy + 1];
                if (!WorldGen.Walkable(cl) || cl == Cell.Hub || cl == Cell.CastleFloor || Lv(chunkElev, x + dx, y + dy) != 0) return false;
            }
            return true;
        }

        void PlaceLair(Chunk chunk, Vector2 pos, uint h)
        {
            // Trees, rocks and grass make way for it.
            const float Clear = 5f;
            for (int i = chunk.root.transform.childCount - 1; i >= 0; i--)
            {
                var t = chunk.root.transform.GetChild(i);
                if (t.gameObject.layer != LayerMask.NameToLayer("Walls") || t.Find("Visual") == null || t.GetComponent<EnvSprite>() != null) continue;
                if ((t.G() - pos).sqrMagnitude < Clear * Clear) Destroy(t.gameObject);
            }
            chunk.grass.RemoveAll(m => (((Vector3)m.GetColumn(3)).G() - pos).sqrMagnitude < 3.5f * 3.5f);
            var go = new GameObject("Lair");
            go.transform.SetParent(chunk.root.transform, false);
            go.transform.position = pos.W();
            go.AddComponent<MonsterLair>().Init(MonsterLair.KindFor(WorldGen.BiomeAt(pos), h >> 7), WorldGen.RegionAt(pos), WorldGen.LevelAt(pos), (int)(h & 0x7FFFFFFF));
        }

        void PlacePointsOfInterest(Chunk chunk, Vector2Int c)
        {
            PlaceCastle(chunk, c);
            if (WorldGen.HasWaypoint(c.x, c.y, out var wpCell))
            {
                ClearAround(chunk, wpCell, 3);
                var go = new GameObject("Waypoint");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = ((Vector2)wpCell + Vector2.one * 0.5f).W();
                var wp = go.AddComponent<Waypoint>();
                wp.id = $"wp_{c.x}_{c.y}";
                wp.displayName = WorldGen.RegionTitle(wpCell);
                wp.order = Mathf.RoundToInt(((Vector2)wpCell).magnitude);
                WorldManager.Instance?.RegisterWaypoint(wp);
            }
            if (WorldGen.HasChest(c.x, c.y, out var chestCell, out int waves))
            {
                ClearAround(chunk, chestCell, 4);
                var go = new GameObject("ChallengeChest");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = ((Vector2)chestCell + Vector2.one * 0.5f).W();
                var chest = go.AddComponent<ChallengeChest>();
                chest.waves = waves;
                chest.key = $"{chestCell.x}_{chestCell.y}";
            }
            // A hidden cache in the chunk's most out-of-the-way spot (WorldGen.HasCache): its glow shows from afar.
            if (WorldGen.HasCache(c.x, c.y, out var cacheCell))
            {
                var go = new GameObject("HiddenCache");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = Ground.On((Vector2)cacheCell + Vector2.one * 0.5f);
                var cache = go.AddComponent<DungeonChest>();
                cache.key = $"cache_{cacheCell.x}_{cacheCell.y}";
                cache.level = WorldGen.LevelAt(cacheCell) + 1;
                cache.title = "Hidden cache";
                cache.roomClear = () => true;
            }
            // A chest on the top of every peak that can be climbed (WorldGen.HasSummit): a better one than a cache.
            if (WorldGen.HasSummit(c.x, c.y, out var summitCell))
            {
                var go = new GameObject("SummitChest");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = Ground.On((Vector2)summitCell + Vector2.one * 0.5f);
                var summit = go.AddComponent<DungeonChest>();
                summit.key = $"summit_{summitCell.x}_{summitCell.y}";
                summit.level = WorldGen.LevelAt(summitCell) + 3;
                summit.title = "Summit chest";
                summit.roomClear = () => true;
            }
            if (WorldGen.HasBossGate(c.x, c.y, out var bossCell) && WorldGen.FortressAt(bossCell.x, bossCell.y, out var fort))
            {
                var go = new GameObject("BossFortress");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = ((Vector2)bossCell).W();
                go.AddComponent<BossFortress>().Init(fort);
            }
            if (WorldGen.HasDungeon(c.x, c.y, out var dungeonCell))
            {
                ClearAround(chunk, dungeonCell, 6, WorldGen.Stairwell(dungeonCell));
                CarveStairwell(chunk, dungeonCell);
                var go = new GameObject("DungeonGate");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = ((Vector2)dungeonCell + Vector2.one * 0.5f).W();
                var gate = go.AddComponent<DungeonGate>();
                gate.areaLevel = WorldGen.LevelAt(dungeonCell);
                gate.key = WorldManager.GateKey(dungeonCell);
            }
        }

        // The chunk holding a castle's centre fills it: packs across the courtyard, a stronger one in the keep, and the
        // keep's chest, which opens once nothing hostile is left inside the keep.
        void PlaceCastle(Chunk chunk, Vector2Int c)
        {
            int px = Mathf.FloorToInt(c.x / (float)WorldGen.CastleGrid), py = Mathf.FloorToInt(c.y / (float)WorldGen.CastleGrid);
            if (!WorldGen.CastleInPatch(px, py, out var castle) || WorldGen.ChunkOf(castle.Center) != c) return;
            var r = castle.rect;
            int level = WorldGen.LevelAt(castle.Center) + 1;
            var zone = WorldGen.RegionAt(castle.Center);
            var spots = new[]
            {
                new Vector2(r.x + r.width * 0.25f, r.y + r.height * 0.22f), new Vector2(r.x + r.width * 0.75f, r.y + r.height * 0.22f),
                new Vector2(r.x + r.width * 0.2f, r.y + r.height * 0.55f), new Vector2(r.x + r.width * 0.8f, r.y + r.height * 0.55f),
                new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.38f),
            };
            for (int i = 0; i < spots.Length; i++)
                Camp(chunk, spots[i], zone, level, (int)(castle.seed & 0x3FFFFFFF) + i * 977, 120f);
            var k = castle.keep;
            var keepCentre = new Vector2(k.x + k.width / 2f, k.y + k.height / 2f);
            Camp(chunk, keepCentre + Vector2.down * 1f, zone, level + 2, (int)(castle.seed >> 2) + 31, 240f);

            var go = new GameObject("CastleChest");
            go.transform.SetParent(chunk.root.transform, false);
            go.transform.position = (new Vector2(keepCentre.x, k.yMax - 2f)).W();
            var chest = go.AddComponent<DungeonChest>();
            chest.key = $"castle_{r.x}_{r.y}";
            chest.level = level + 2;
            var keepRect = new Rect(k.x, k.y, k.width, k.height);
            chest.roomClear = () =>
            {
                foreach (var m in Combatant.All)
                    if (m != null && !m.IsDead && m.team == Team.Enemy && keepRect.Contains(m.transform.position)) return false;
                return true;
            };
        }

        static void Camp(Chunk chunk, Vector2 pos, ZoneDefinition zone, int level, int seed, float respawn)
        {
            var camp = new GameObject("CastleCamp").AddComponent<MonsterCamp>();
            camp.transform.SetParent(chunk.root.transform, false);
            camp.transform.position = pos.W();
            camp.zone = zone;
            camp.fixedLevel = level;
            camp.seed = seed;
            camp.respawnTime = respawn;
        }

        // Opens the ground for a stairwell and clears anything that was placed on the spot.
        void CarveStairwell(Chunk chunk, Vector2Int cell)
        {
            var hole = WorldGen.Stairwell(cell);
            var none = new TileBase[hole.size.x * hole.size.y];
            ground.SetTilesBlock(hole, none);
            detail.SetTilesBlock(hole, none);

            const float ClearX = 5f, ClearY = 7f;
            bool OnSpot(Vector3 p) => Mathf.Abs(p.x - cell.x) < ClearX && Mathf.Abs(p.z - cell.y) < ClearY;
            chunk.grass.RemoveAll(m => OnSpot(m.GetColumn(3)));
            foreach (var t in chunk.root.GetComponentsInChildren<Transform>(true))
                if (t != chunk.root.transform && OnSpot(t.position)) Destroy(t.gameObject);
        }

        // Make sure points of interest aren't buried in walls or water.
        void ClearAround(Chunk chunk, Vector2Int center, int r, BoundsInt? hole = null)
        {
            var empty = new TileBase[(2 * r + 1) * (2 * r + 1)];
            var b = new BoundsInt(center.x - r, center.y - r, 0, 2 * r + 1, 2 * r + 1, 1);
            blockers.SetTilesBlock(b, empty);
            foreach (var tm in levelMaps.Values) tm.SetTilesBlock(b, empty);
            var fill = new TileBase[empty.Length];
            var biome = WorldGen.BiomeAt(center);
            // Grass over the square, and on it a round patch of the biome's own path (as the roads round it, never the
            // old tileset's dirt, which put a red square under every waypoint in the ruins), with the paths' soft edge.
            var patch = new Dictionary<Texture2D, PathMesh>();
            float R = r + 0.3f;
            float Wear(float px, float py) => Mathf.Clamp01(R - Vector2.Distance(new Vector2(px, py), center + new Vector2(0.5f, 0.5f)) + 0.5f);
            for (int i = 0; i < fill.Length; i++)
            {
                int x = center.x - r + i % (2 * r + 1), y = center.y - r + i / (2 * r + 1);
                uint h = WorldGen.Hash(x, y);
                fill[i] = EnvArt.GroundTile(biome, x, y) ?? GroundTile(biome, h);
                // No path over a hole in the ground (a dungeon's stairwell): it would lie over the stairs like a lid.
                if (hole.HasValue && hole.Value.Contains(new Vector3Int(x, y, 0))) continue;
                var sprite = ((EnvArt.PathTile(biome, x, y) ?? PathTile(biome, h)) as Tile)?.sprite;
                if (sprite == null) continue;
                var wear = new Vector4(Wear(x, y), Wear(x + 1, y), Wear(x + 1, y + 1), Wear(x, y + 1));
                if (wear.x + wear.y + wear.z + wear.w < 0.01f) continue;
                if (!patch.TryGetValue(sprite.texture, out var pm)) patch[sprite.texture] = pm = new PathMesh();
                const float z = -0.009f;
                pm.Quad(new Vector3(x, y, z), new Vector3(x + 1, y, z), new Vector3(x + 1, y + 1, z), new Vector3(x, y + 1, z), Vector3.back, Vector3.back, Vector3.back, Vector3.back, sprite, wear);
            }
            ground.SetTilesBlock(b, fill);
            foreach (var kv in patch) kv.Value.Build(chunk.root.transform, PathMaterial(kv.Key, 1f));
        }

        // ---------- Mesh helper ----------

        // Which corners of a puddle cell are rounded off (r: lower left, g: lower right, b: upper left, a: upper right):
        // those where neither neighbour along the corner's two sides is puddle too. A lone cell is a disc, a run a capsule.
        static Color RoundCorners(System.Func<int, int, bool> wet)
        {
            bool l = wet(-1, 0), r = wet(1, 0), d = wet(0, -1), u = wet(0, 1);
            return new Color(!l && !d ? 1 : 0, !r && !d ? 1 : 0, !l && !u ? 1 : 0, !r && !u ? 1 : 0);
        }

        // Which sides of a pool cell are its shore (x left, y right, z bottom, w top).
        static Vector4 OpenSides(System.Func<int, int, bool> wet) =>
            new Vector4(wet(-1, 0) ? 0 : 1, wet(1, 0) ? 0 : 1, wet(0, -1) ? 0 : 1, wet(0, 1) ? 0 : 1);

        class MeshBuilder
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> tris = new List<int>();
            readonly List<Color> colors = new List<Color>();
            readonly List<Vector4> extra = new List<Vector4>();

            public int Count => verts.Count;

            // A rectangle from origin along u (width w) and v (height h), facing normal.
            public void Quad(Vector3 origin, Vector3 u, Vector3 v, Vector3 normal, float w, float h, float uvH = -1f, Color? color = null, Vector4 data = default)
            {
                int i = verts.Count;
                for (int k = 0; k < 4; k++) { colors.Add(color ?? Color.white); extra.Add(data); }
                verts.Add(origin);
                verts.Add(origin + u * w);
                verts.Add(origin + v * h);
                verts.Add(origin + u * w + v * h);
                for (int k = 0; k < 4; k++) normals.Add(normal);
                float vh = uvH < 0 ? h : uvH;
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(w, 0));
                uvs.Add(new Vector2(0, vh));
                uvs.Add(new Vector2(w, vh));
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i + 2); tris.Add(i + 3); tris.Add(i + 1);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(verts);
                m.SetNormals(normals);
                m.SetUVs(0, uvs);
                while (colors.Count < verts.Count) colors.Add(Color.white);
                while (extra.Count < verts.Count) extra.Add(Vector4.zero);
                m.SetColors(colors);
                m.SetUVs(1, extra);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                return Ground.ToY(m);   // built with the ground on XY and up -Z; the world is Y-up
            }
        }
    }
}
