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
            go.transform.localPosition = new Vector3(0, 0, z);
            var tm = go.AddComponent<Tilemap>();
            if (collider)
            {
                go.layer = LayerMask.NameToLayer("Walls");
                go.AddComponent<TilemapCollider2D>();
                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Static;
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
            for (int dx = -loadRadius; dx <= loadRadius; dx++)
            for (int dy = -loadRadius; dy <= loadRadius; dy++)
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
                var c = WorldGen.ChunkOf(CameraFollow.CinematicActive ? (Vector2)CameraFollow.CinematicFocus : (Vector2)hero.transform.position);
                // Reveal the map around the hero.
                if (!CameraFollow.CinematicActive)
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        Profile.MarkExplored(new Vector2Int(c.x + dx, c.y + dy));
                if (c != heroChunk)
                {
                    heroChunk = c;
                    toLoad.Clear();
                    for (int dx = -loadRadius; dx <= loadRadius; dx++)
                    for (int dy = -loadRadius; dy <= loadRadius; dy++)
                    {
                        var k = new Vector2Int(c.x + dx, c.y + dy);
                        if (!chunks.ContainsKey(k)) toLoad.Add(k);
                    }
                    toLoad.Sort((a, b) => (a - c).sqrMagnitude.CompareTo((b - c).sqrMagnitude));
                    var unload = new List<Vector2Int>();
                    foreach (var k in chunks.Keys)
                        if (Mathf.Abs(k.x - c.x) > loadRadius + 1 || Mathf.Abs(k.y - c.y) > loadRadius + 1) unload.Add(k);
                    foreach (var k in unload) Unload(k);
                }
            }
            // A couple of chunks per frame keeps it smooth.
            for (int i = 0; i < (CameraFollow.CinematicActive ? 4 : 2) && toLoad.Count > 0; i++)
            {
                Load(toLoad[0]);
                toLoad.RemoveAt(0);
            }
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
            var water = new MeshBuilder();
            var puddles = new MeshBuilder();
            var lava = new MeshBuilder();
            var decorRng = new System.Random((int)WorldGen.Hash(c.x, c.y));
            Biome chunkBiome = WorldGen.BiomeAt(new Vector2(x0 + S / 2f, y0 + S / 2f));
            chunk.grassBiome = chunkBiome;

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int wx = x0 + x, wy = y0 + y, i = y * S + x;
                var cell = cells[x + 1, y + 1];
                if (cell == Cell.Hub) continue;
                var biome = WorldGen.LookBiomeAt(new Vector2(wx + 0.5f, wy + 0.5f));
                uint h = WorldGen.Hash(wx, wy);
                int lv = elev[x + 1, y + 1];
                float z = -lv * WorldGen.LevelHeight;
                // Bare rock high up a mountain.
                var groundBiome = lv >= WorldGen.RockLevel && biome != Biome.Ember ? Biome.Ruins : biome;
                if (lv > 0) AddRaisedSides(cells, elev, x, y, wx, wy, rockFaces, falls, lavaFalls, foam, rims, footShadows, edges);

                switch (cell)
                {
                    case Cell.Path:
                        if (lv > 0) { LevelTiles(lv)[i] = EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h); break; }
                        groundTiles[i] = EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h);
                        detailTiles[i] = FringeTile(cells, x, y, biome);
                        break;
                    case Cell.Water:
                    case Cell.Lava:
                        blockTiles[i] = tiles.blocker;
                        (cell == Cell.Water ? water : lava).Quad(new Vector3(wx, wy, z + 0.12f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1);
                        // Shore: the pool sits slightly below the ground.
                        AddDrop(cells, elev, x, y, wx, wy, cell, cell == Cell.Water ? water : lava);
                        break;
                    case Cell.Plateau:
                        if (WorldGen.FortressAt(wx, wy, out var fortHere))
                        {
                            // A fortress is floored in its boss's own biome: stone only for the Bone King's hall.
                            var fb = WorldGen.BossBiome(fortHere.kind);
                            LevelTiles(lv)[i] = fortHere.kind == BossKind.Skeleton ? CastleFloorTile(wx, wy, h)
                                : EnvArt.GroundTile(fb, wx, wy) ?? GroundTile(fb, h >> 3);
                            if (fortHere.IsPuddle(wx, wy))
                                puddles.Quad(new Vector3(wx, wy, -WorldGen.PlateauHeight - 0.006f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1);
                        }
                        else LevelTiles(lv)[i] = Shore(cells, elev, x, y) ? EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h)
                            : EnvArt.GroundTile(groundBiome, wx, wy) ?? GroundTile(groundBiome, h >> 3);
                        break;
                    case Cell.Stairs:
                        if (WorldGen.StairAt(wx, wy, out var flight, out _) && flight.natural)
                        {
                            // A grassy slope: the cell's own ground, tilted from one level up to the next.
                            bool worn = WorldGen.OnPath(wx, wy);
                            AddSlope(wx, wy, flight, worn ? EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h) : EnvArt.GroundTile(groundBiome, wx, wy) ?? GroundTile(groundBiome, h), slopes, slopeSides, edges);
                            break;
                        }
                        if (lv > 0) LevelTiles(lv)[i] = EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h);
                        else groundTiles[i] = EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h);
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
                        groundTiles[i] = EnvArt.GroundTile(biome, wx, wy) ?? GroundTile(biome, h);
                        puddles.Quad(new Vector3(wx, wy, -0.006f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1);
                        break;
                    default:
                        groundTiles[i] = Shore(cells, elev, x, y) ? EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h)
                            : EnvArt.GroundTile(biome, wx, wy) ?? GroundTile(biome, h);
                        if ((h & 0xFF) < 14) detailTiles[i] = DetailTile(biome, h);
                        break;
                }
            }

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var cell = cells[x + 1, y + 1];
                if (!WorldGen.Solid(cell) || cell == Cell.Plateau) continue;
                AddRims(cells, x, y, x0 + x, y0 + y, rims);
                AddFootShadows(cells, x, y, x0 + x, y0 + y, footShadows);
            }

            ground.SetTilesBlock(bounds, groundTiles);
            detail.SetTilesBlock(bounds, detailTiles);
            foreach (var kv in levelTiles) LevelMap(kv.Key).SetTilesBlock(bounds, kv.Value);
            blockers.SetTilesBlock(bounds, blockTiles);

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
            if (edges.Count > 0)
            {
                var eg = new GameObject("LevelEdges");
                eg.transform.SetParent(chunk.root.transform, false);
                eg.layer = LayerMask.NameToLayer("Walls");
                var poly = eg.AddComponent<PolygonCollider2D>();
                poly.pathCount = edges.Count;
                for (int k = 0; k < edges.Count; k++) poly.SetPath(k, edges[k]);
                LevelPass.AddEdges(poly);
            }
            MeshObject(chunk.root, "Water", water, chunkBiome == Biome.Marsh ? RenderAssets.Instance.water
                : WaterTint("pond", new Color(0.14f, 0.5f, 0.74f), new Color(0.36f, 0.78f, 0.9f)), false);
            MeshObject(chunk.root, "Puddles", puddles, WaterTint("puddle", new Color(0.42f, 0.74f, 0.9f), new Color(0.7f, 0.92f, 1f)), false);
            MeshObject(chunk.root, "Lava", lava, RenderAssets.Instance.lava, false);
            foreach (var kv in slopes) kv.Value.Build(chunk.root.transform, "Slopes", SlopeMaterial(kv.Key), false);
            slopeSides.Build(chunk.root.transform, "Slope Sides", WorldTiles.MeshMaterial("cliff" + chunkBiome, cliffTex), true);
            MeshObject(chunk.root, "Waterfalls", falls, WaterfallMaterial(), false);
            MeshObject(chunk.root, "Lavafalls", lavaFalls, LavafallMaterial(), false);
            MeshObject(chunk.root, "Foam", foam, WaterTint("foam", new Color(0.75f, 0.9f, 1f), new Color(0.97f, 1f, 1f)), false);
            if (falls.Count > 0) chunk.root.AddComponent<FallSpray>().Init(cells, elev, x0, y0);
            if (lava.Count > 0) PointLights.Attach(chunk.root.transform, new Color(1f, 0.45f, 0.15f), 10f, 1.4f, (Vector3)LavaCenter(cells, x0, y0) + new Vector3(0, 0, -1.5f), true);

            chunkElev = elev;
            PlaceDecor(chunk, cells, x0, y0, decorRng);
            PlaceGrass(chunk, cells, x0, y0, decorRng);
            AmbientLife.Build(chunk.root.transform, cells, x0, y0, c);
            PlaceCamps(chunk, cells, x0, y0);
            PlacePointsOfInterest(chunk, c);
        }

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
                if (nl >= lv) continue;
                var n = cells[x + s.dx + 1, y + s.dy + 1];
                float zLow = -nl * H, zTop = -lv * H, height = (lv - nl) * H;
                // A slope or stair arriving here: open ground, no cliff.
                WorldGen.Stair st = default; int row = 0;
                bool arrival = n == Cell.Stairs && WorldGen.StairAt(wx + s.dx, wy + s.dy, out st, out row) && row == st.len - 1
                    && st.d.x == -s.dx && st.d.y == -s.dy && st.hi == lv;
                if (arrival && st.natural) continue;
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

        // A grassy slope over one tile of a natural flight, tilted from its low edge up to its high edge, in the
        // tile's own ground art; the flight's outer sides get a rock face under the slope.
        void AddSlope(int wx, int wy, WorldGen.Stair st, TileBase tile, Dictionary<Texture2D, Town.Solid> slopes, Town.Solid sides, List<Vector2[]> edges)
        {
            float Z(float px, float py)
            {
                float along = (px - st.a.x) * st.d.x + (py - st.a.y) * st.d.y + (st.d.x + st.d.y < 0 ? 1f : 0f);
                return -(st.lo + (st.hi - st.lo) * Mathf.Clamp01(along / st.len)) * WorldGen.LevelHeight;
            }
            var sprite = (tile as Tile)?.sprite;
            if (sprite != null)
            {
                var tex = sprite.texture;
                if (!slopes.TryGetValue(tex, out var mb)) slopes[tex] = mb = new Town.Solid();
                var r = sprite.textureRect;
                Vector2 Uv(float fx, float fy) => new Vector2((r.x + fx * r.width) / tex.width, (r.y + fy * r.height) / tex.height);
                var a = new Vector3(wx, wy, Z(wx, wy)); var b = new Vector3(wx + 1, wy, Z(wx + 1, wy));
                var c = new Vector3(wx + 1, wy + 1, Z(wx + 1, wy + 1)); var d = new Vector3(wx, wy + 1, Z(wx, wy + 1));
                var nrm = Vector3.Cross(b - a, d - a).normalized;
                if (nrm.z > 0f) nrm = -nrm;
                mb.Quad(a, b, c, d, nrm, Uv(0, 0), Uv(1, 0), Uv(1, 1), Uv(0, 1));
            }
            // Rock under the slope along the flight's outer sides, and the thin walls that keep walkers on it.
            float baseZ = -st.lo * WorldGen.LevelHeight;
            foreach (var s in Sides)
            {
                if (s.dx * st.d.x + s.dy * st.d.y != 0) continue;
                if (WorldGen.CellAt(wx + s.dx, wy + s.dy) == Cell.Stairs) continue;
                var p0 = new Vector2(wx, wy) + s.o; var p1 = p0 + s.u;
                float z0 = Z(p0.x, p0.y), z1 = Z(p1.x, p1.y);
                var n3 = new Vector3(-s.inward.x, -s.inward.y, 0f);
                if (z0 < baseZ - 0.01f || z1 < baseZ - 0.01f)
                    sides.Quad(new Vector3(p0.x, p0.y, baseZ), new Vector3(p1.x, p1.y, baseZ), new Vector3(p1.x, p1.y, z1), new Vector3(p0.x, p0.y, z0), n3,
                        new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, (baseZ - z1)), new Vector2(0f, (baseZ - z0)));
                EdgeWall(p0, s.u, edges);
            }
        }

        static readonly Dictionary<Texture2D, Material> slopeMats = new Dictionary<Texture2D, Material>();

        static Material SlopeMaterial(Texture2D tex)
        {
            if (slopeMats.TryGetValue(tex, out var m) && m != null) return m;
            m = new Material(RenderAssets.Instance.litGround) { name = "Slope " + tex.name, mainTexture = tex };
            slopeMats[tex] = m;
            return m;
        }

        // Ground at the edge of water on its own level: sand and pebbles.
        static bool Shore(Cell[,] cells, int[,] elev, int x, int y)
        {
            int lv = Lv(elev, x, y);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (cells[x + dx + 1, y + dy + 1] == Cell.Water && Lv(elev, x + dx, y + dy) == lv) return true;
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

        static Material WaterTint(string key, Color deep, Color shallow)
        {
            if (waterTints.TryGetValue(key, out var m) && m) return m;
            m = new Material(RenderAssets.Instance.water) { name = "Water_" + key };
            m.SetColor("_Deep", deep);
            m.SetColor("_Shallow", shallow);
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
                Decor(chunk.root.transform, sprite, new Vector3(wx + 0.5f, wy + 0.3f, -lv * WorldGen.LevelHeight), biome, solid, rng);
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
            if (!EnvMesh.Has("tree_green")) return false;
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
            RenderAssets.MakeLit(sr, true, sways ? RenderAssets.Instance.litWind : null);
            if (solid)
            {
                var col = go.AddComponent<CircleCollider2D>();
                col.radius = Mathf.Clamp(sprite.bounds.size.x * s * 0.18f, 0.3f, 1.4f);
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
                var p = new Vector3(x0 + x + (float)rng.NextDouble(), y0 + y + (float)rng.NextDouble(), -glv * WorldGen.LevelHeight);
                float sc = 0.7f + (float)rng.NextDouble() * 0.6f;
                // Store position + scale; rotation is applied each frame from the camera.
                // Each tuft takes the look of the ground it stands on (the z scale carries the biome).
                var gb = WorldGen.LookBiomeAt(p);
                if (gb == Biome.Ruins || (gb == Biome.Ember && rng.NextDouble() > 0.1)) continue;
                chunk.grass.Add(Matrix4x4.TRS(p, Quaternion.identity, new Vector3(sc, sc, 1f + (int)gb)));
            }
        }

        readonly Dictionary<Biome, List<Matrix4x4>> grassBatches = new Dictionary<Biome, List<Matrix4x4>>();

        void DrawGrass()
        {
            var hero = HeroController.Instance;
            if (hero == null || grassQuad == null) return;
            var hc = WorldGen.ChunkOf(CameraFollow.CinematicActive ? (Vector2)CameraFollow.CinematicFocus : (Vector2)hero.transform.position);
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
            for (int k = 0; k < 2; k++)
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
                camp.transform.position = new Vector3(pos.x, pos.y, -campLv * WorldGen.LevelHeight);
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
                if (((Vector2)t.position - pos).sqrMagnitude < Clear * Clear) Destroy(t.gameObject);
            }
            chunk.grass.RemoveAll(m => ((Vector2)(Vector3)m.GetColumn(3) - pos).sqrMagnitude < 3.5f * 3.5f);
            var go = new GameObject("Lair");
            go.transform.SetParent(chunk.root.transform, false);
            go.transform.position = pos;
            go.AddComponent<MonsterLair>().Init(MonsterLair.KindFor(WorldGen.BiomeAt(pos), h >> 7), WorldGen.RegionAt(pos), WorldGen.LevelAt(pos), (int)(h & 0x7FFFFFFF));
        }

        void PlacePointsOfInterest(Chunk chunk, Vector2Int c)
        {
            PlaceCastle(chunk, c);
            if (WorldGen.HasWaypoint(c.x, c.y, out var wpCell))
            {
                ClearAround(wpCell, 3);
                var go = new GameObject("Waypoint");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = (Vector2)wpCell + Vector2.one * 0.5f;
                var wp = go.AddComponent<Waypoint>();
                wp.id = $"wp_{c.x}_{c.y}";
                wp.displayName = WorldGen.RegionTitle(wpCell);
                wp.order = Mathf.RoundToInt(((Vector2)wpCell).magnitude);
                WorldManager.Instance?.RegisterWaypoint(wp);
            }
            if (WorldGen.HasChest(c.x, c.y, out var chestCell, out int waves))
            {
                ClearAround(chestCell, 4);
                var go = new GameObject("ChallengeChest");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = (Vector2)chestCell + Vector2.one * 0.5f;
                var chest = go.AddComponent<ChallengeChest>();
                chest.waves = waves;
                chest.key = $"{chestCell.x}_{chestCell.y}";
            }
            if (WorldGen.HasBossGate(c.x, c.y, out var bossCell) && WorldGen.FortressAt(bossCell.x, bossCell.y, out var fort))
            {
                var go = new GameObject("BossFortress");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = (Vector2)bossCell;
                go.AddComponent<BossFortress>().Init(fort);
            }
            if (WorldGen.HasDungeon(c.x, c.y, out var dungeonCell))
            {
                ClearAround(dungeonCell, 6);
                CarveStairwell(chunk, dungeonCell);
                var go = new GameObject("DungeonGate");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = (Vector2)dungeonCell + Vector2.one * 0.5f;
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
            go.transform.position = new Vector2(keepCentre.x, k.yMax - 2f);
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
            camp.transform.position = pos;
            camp.zone = zone;
            camp.fixedLevel = level;
            camp.seed = seed;
            camp.respawnTime = respawn;
        }

        // Opens the ground for a stairwell and clears anything that was placed on the spot.
        void CarveStairwell(Chunk chunk, Vector2Int cell)
        {
            int halfW = DungeonGate.HalfWidth, halfL = DungeonGate.HalfLength;
            var hole = new BoundsInt(cell.x - halfW, cell.y - halfL, 0, 2 * halfW + 1, 2 * halfL + 1, 1);
            var none = new TileBase[hole.size.x * hole.size.y];
            ground.SetTilesBlock(hole, none);
            detail.SetTilesBlock(hole, none);

            const float ClearX = 5f, ClearY = 7f;
            bool OnSpot(Vector3 p) => Mathf.Abs(p.x - cell.x) < ClearX && Mathf.Abs(p.y - cell.y) < ClearY;
            chunk.grass.RemoveAll(m => OnSpot(m.GetColumn(3)));
            foreach (var t in chunk.root.GetComponentsInChildren<Transform>(true))
                if (t != chunk.root.transform && OnSpot(t.position)) Destroy(t.gameObject);
        }

        // Make sure points of interest aren't buried in walls or water.
        void ClearAround(Vector2Int center, int r)
        {
            var empty = new TileBase[(2 * r + 1) * (2 * r + 1)];
            var b = new BoundsInt(center.x - r, center.y - r, 0, 2 * r + 1, 2 * r + 1, 1);
            blockers.SetTilesBlock(b, empty);
            foreach (var tm in levelMaps.Values) tm.SetTilesBlock(b, empty);
            var fill = new TileBase[empty.Length];
            var biome = WorldGen.BiomeAt(center);
            for (int i = 0; i < fill.Length; i++)
            {
                int x = center.x - r + i % (2 * r + 1), y = center.y - r + i / (2 * r + 1);
                fill[i] = EnvArt.PathTile(biome, x, y) ?? tiles.dirt[i % tiles.dirt.Length];
            }
            ground.SetTilesBlock(b, fill);
        }

        // ---------- Mesh helper ----------

        class MeshBuilder
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> tris = new List<int>();

            public int Count => verts.Count;

            // A rectangle from origin along u (width w) and v (height h), facing normal.
            public void Quad(Vector3 origin, Vector3 u, Vector3 v, Vector3 normal, float w, float h, float uvH = -1f)
            {
                int i = verts.Count;
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
                var colors = new Color[verts.Count];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                m.colors = colors;
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
