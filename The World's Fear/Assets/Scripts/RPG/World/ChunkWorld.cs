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
        Tilemap ground, detail, plateau, blockers;
        Vector2Int heroChunk = new Vector2Int(int.MinValue, int.MinValue);
        float checkTimer;
        WorldTiles tiles;
        Mesh grassQuad;
        ArtSet art;

        public IEnumerable<Vector2Int> LoadedChunks => chunks.Keys;

        void Awake()
        {
            Instance = this;
            WorldGen.HubRect = hubRect;
            _ = Profile.Data; // loading the hero sets the world seed; playing this scene directly skips the title screen
            tiles = WorldTiles.Instance;
            art = ArtSet.Instance;
            // Note: Unity's fake-null objects break '??', so check explicitly.
            if (GetComponent<Grid>() == null) gameObject.AddComponent<Grid>();

            ground = MakeTilemap("Ground", 0f, RenderAssets.Instance.litGround, false);
            detail = MakeTilemap("Detail", -0.01f, RenderAssets.Instance.litDetail, false);
            plateau = MakeTilemap("Plateau Tops", -WorldGen.PlateauHeight, RenderAssets.Instance.litGround, false);
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
                checkTimer = 0.25f;
                var c = WorldGen.ChunkOf(hero.transform.position);
                // Reveal the map around the hero.
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
            for (int i = 0; i < 2 && toLoad.Count > 0; i++)
            {
                Load(toLoad[0]);
                toLoad.RemoveAt(0);
            }
        }

        void LateUpdate() => DrawGrass();

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
            for (int y = -1; y <= S; y++)
            for (int x = -1; x <= S; x++)
                cells[x + 1, y + 1] = WorldGen.CellAt(x0 + x, y0 + y);

            var bounds = new BoundsInt(x0, y0, 0, S, S, 1);
            var groundTiles = new TileBase[S * S];
            var detailTiles = new TileBase[S * S];
            var plateauTiles = new TileBase[S * S];
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
                var biome = WorldGen.BiomeAt(new Vector2(wx + 0.5f, wy + 0.5f));
                uint h = WorldGen.Hash(wx, wy);

                switch (cell)
                {
                    case Cell.Path:
                        groundTiles[i] = EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h);
                        detailTiles[i] = FringeTile(cells, x, y, biome);
                        break;
                    case Cell.Water:
                    case Cell.Lava:
                        blockTiles[i] = tiles.blocker;
                        (cell == Cell.Water ? water : lava).Quad(new Vector3(wx, wy, 0.12f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1);
                        // Shore: the pool sits slightly below the ground.
                        AddDrop(cells, x, y, wx, wy, cell, cell == Cell.Water ? water : lava);
                        break;
                    case Cell.Plateau:
                        groundTiles[i] = EnvArt.GroundTile(biome, wx, wy) ?? GroundTile(biome, h);
                        if (WorldGen.FortressAt(wx, wy, out var fortHere))
                        {
                            // A fortress is floored in its boss's own biome: stone only for the Bone King's hall.
                            var fb = WorldGen.BossBiome(fortHere.kind);
                            plateauTiles[i] = fortHere.kind == BossKind.Skeleton ? CastleFloorTile(wx, wy, h)
                                : EnvArt.GroundTile(fb, wx, wy) ?? GroundTile(fb, h >> 3);
                            if (fortHere.IsPuddle(wx, wy))
                                puddles.Quad(new Vector3(wx, wy, -WorldGen.PlateauHeight - 0.006f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1);
                        }
                        else plateauTiles[i] = EnvArt.GroundTile(biome, wx, wy) ?? GroundTile(biome, h >> 3);
                        AddCliffs(cells, x, y, wx, wy, rockFaces);
                        AddPlateauEdges(cells, x, y, wx, wy, edges);
                        break;
                    case Cell.Stairs:
                        groundTiles[i] = EnvArt.PathTile(biome, wx, wy) ?? PathTile(biome, h);
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
                        groundTiles[i] = EnvArt.GroundTile(biome, wx, wy) ?? GroundTile(biome, h);
                        if ((h & 0xFF) < 14) detailTiles[i] = DetailTile(biome, h);
                        break;
                }
            }

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var cell = cells[x + 1, y + 1];
                if (!WorldGen.Solid(cell)) continue;
                AddRims(cells, x, y, x0 + x, y0 + y, rims);
                AddFootShadows(cells, x, y, x0 + x, y0 + y, footShadows);
            }

            ground.SetTilesBlock(bounds, groundTiles);
            detail.SetTilesBlock(bounds, detailTiles);
            plateau.SetTilesBlock(bounds, plateauTiles);
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
            }
            MeshObject(chunk.root, "Water", water, chunkBiome == Biome.Marsh ? RenderAssets.Instance.water
                : WaterTint("pond", new Color(0.14f, 0.5f, 0.74f), new Color(0.36f, 0.78f, 0.9f)), false);
            MeshObject(chunk.root, "Puddles", puddles, WaterTint("puddle", new Color(0.42f, 0.74f, 0.9f), new Color(0.7f, 0.92f, 1f)), false);
            MeshObject(chunk.root, "Lava", lava, RenderAssets.Instance.lava, false);
            if (lava.Count > 0) PointLights.Attach(chunk.root.transform, new Color(1f, 0.45f, 0.15f), 10f, 1.4f, (Vector3)LavaCenter(cells, x0, y0) + new Vector3(0, 0, -1.5f), true);

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
            plateau.SetTilesBlock(bounds, empty);
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

        // Vertical rock faces on every plateau edge that borders lower ground.
        void AddCliffs(Cell[,] cells, int x, int y, int wx, int wy, MeshBuilder mb)
        {
            float h = WorldGen.PlateauHeight;
            Vector3 up = Vector3.back * h;
            if (!At(cells, x, y - 1, Cell.Plateau)) mb.Quad(new Vector3(wx, wy, 0), Vector3.right, up / h, Vector3.down, 1, h);
            if (!At(cells, x, y + 1, Cell.Plateau)) mb.Quad(new Vector3(wx + 1, wy + 1, 0), Vector3.left, up / h, Vector3.up, 1, h);
            if (!At(cells, x - 1, y, Cell.Plateau)) mb.Quad(new Vector3(wx, wy + 1, 0), Vector3.down, up / h, Vector3.left, 1, h);
            if (!At(cells, x + 1, y, Cell.Plateau)) mb.Quad(new Vector3(wx + 1, wy, 0), Vector3.up, up / h, Vector3.right, 1, h);
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

        // A plateau's edge is closed everywhere except where a flight of stairs comes up to it.
        void AddPlateauEdges(Cell[,] cells, int x, int y, int wx, int wy, List<Vector2[]> edges)
        {
            foreach (var s in Sides)
            {
                var n = cells[x + s.dx + 1, y + s.dy + 1];
                if (n == Cell.Plateau) continue;
                if (n == Cell.Stairs && WorldGen.StairAt(wx + s.dx, wy + s.dy, out var st, out int row) && row == WorldGen.StairLength - 1
                    && st.d.x == -s.dx && st.d.y == -s.dy) continue;
                EdgeWall(new Vector2(wx, wy) + s.o, s.u, edges);
            }
        }

        // One cell of a flight: three stone steps rising toward the plateau, closed along its outer sides.
        void AddStairs(int wx, int wy, MeshBuilder mb, MeshBuilder rims, List<Vector2[]> edges)
        {
            if (!WorldGen.StairAt(wx, wy, out var st, out int row)) return;
            // Each tread sits at the slope's height at its lower edge, so whoever walks the flight stands on or just above
            // the stone and never sinks into the step ahead.
            const int steps = 4;
            float rise = WorldGen.PlateauHeight / (WorldGen.StairLength * steps);
            for (int k = 0; k < steps; k++)
            {
                float lo = k / (float)steps, size = 1f / steps;
                float top = (row * steps + k) * rise;
                if (top <= 0.001f) continue;
                float sx = st.d.x != 0 ? size : 1f, sy = st.d.y != 0 ? size : 1f;
                float ox = st.d.x > 0 ? lo : st.d.x < 0 ? 1f - lo - size : 0f;
                float oy = st.d.y > 0 ? lo : st.d.y < 0 ? 1f - lo - size : 0f;
                AddBlock(wx + ox, wy + oy, sx, sy, 0f, top, mb);
                // a light lip along the front of each tread, so the steps read from above
                foreach (var s in Sides)
                {
                    if (s.dx != -st.d.x || s.dy != -st.d.y) continue;
                    var o = new Vector3(wx + ox + s.o.x * sx, wy + oy + s.o.y * sy, -top - 0.012f);
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

        // A short drop from the ground down to the water/lava surface.
        void AddDrop(Cell[,] cells, int x, int y, int wx, int wy, Cell self, MeshBuilder mb)
        {
            const float d = 0.12f;
            Vector3 down = Vector3.forward;
            if (!At(cells, x, y - 1, self)) mb.Quad(new Vector3(wx, wy, 0), Vector3.right, down, Vector3.up, 1, d);
            if (!At(cells, x, y + 1, self)) mb.Quad(new Vector3(wx + 1, wy + 1, 0), Vector3.left, down, Vector3.down, 1, d);
            if (!At(cells, x - 1, y, self)) mb.Quad(new Vector3(wx, wy + 1, 0), Vector3.down, down, Vector3.right, 1, d);
            if (!At(cells, x + 1, y, self)) mb.Quad(new Vector3(wx + 1, wy, 0), Vector3.up, down, Vector3.left, 1, d);
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
                if (!At(cells, x, y, Cell.Ground)) continue;
                if (NearPath(cells, x, y)) continue;
                int wx = x0 + x, wy = y0 + y;
                var biome = WorldGen.BiomeAt(new Vector2(wx, wy));
                // Forest density comes in patches.
                float density = Mathf.PerlinNoise(wx * 0.06f + 300f, wy * 0.06f + 300f);
                float chance = biome == Biome.Forest ? Mathf.Max(0f, density - 0.45f) * 0.35f
                    : biome == Biome.Marsh ? 0.025f : biome == Biome.Ruins ? 0.02f : 0.018f;
                if (rng.NextDouble() > chance) continue;
                var sprite = DecorSprite(biome, rng);
                bool solid = !sprite.name.StartsWith("bush") && !sprite.name.StartsWith("fern");
                Decor(chunk.root.transform, sprite, new Vector2(wx + 0.5f, wy + 0.3f), biome, solid, rng);
            }
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

        void Decor(Transform parent, Sprite sprite, Vector2 pos, Biome biome, bool solid, System.Random rng)
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
            if (chunk.grassBiome == Biome.Ruins) return;
            int S = WorldGen.ChunkSize;
            int count = chunk.grassBiome == Biome.Ember ? 25 : 110;
            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(S), y = rng.Next(S);
                if (!At(cells, x, y, Cell.Ground)) continue;
                var p = new Vector3(x0 + x + (float)rng.NextDouble(), y0 + y + (float)rng.NextDouble(), 0f);
                float sc = 0.7f + (float)rng.NextDouble() * 0.6f;
                // Store position + scale; rotation is applied each frame from the camera.
                chunk.grass.Add(Matrix4x4.TRS(p, Quaternion.identity, new Vector3(sc, sc, 1f)));
            }
        }

        readonly Dictionary<Biome, List<Matrix4x4>> grassBatches = new Dictionary<Biome, List<Matrix4x4>>();

        void DrawGrass()
        {
            var hero = HeroController.Instance;
            if (hero == null || grassQuad == null) return;
            var hc = WorldGen.ChunkOf(hero.transform.position);
            foreach (var list in grassBatches.Values) list.Clear();
            var rot = View.Rotation;
            foreach (var chunk in chunks.Values)
            {
                if (chunk.grass.Count == 0 || Mathf.Abs(chunk.coord.x - hc.x) > 2 || Mathf.Abs(chunk.coord.y - hc.y) > 2) continue;
                if (!grassBatches.TryGetValue(chunk.grassBiome, out var batch)) grassBatches[chunk.grassBiome] = batch = new List<Matrix4x4>();
                foreach (var m in chunk.grass)
                    batch.Add(Matrix4x4.TRS(m.GetColumn(3), rot, new Vector3(m.m00, m.m11, 1f)));
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
                for (int dx = -2; dx <= 2 && open; dx++)
                for (int dy = -2; dy <= 2 && open; dy++)
                    if (!WorldGen.Walkable(cells[x + dx + 1, y + dy + 1]) || cells[x + dx + 1, y + dy + 1] == Cell.Hub
                        || cells[x + dx + 1, y + dy + 1] == Cell.CastleFloor) open = false;   // castles place their own
                if (!open) continue;
                var pos = new Vector2(x0 + x + 0.5f, y0 + y + 0.5f);
                if (pos.magnitude < WorldGen.SafeRadius) continue;
                var camp = new GameObject("Camp").AddComponent<MonsterCamp>();
                camp.transform.SetParent(chunk.root.transform, false);
                camp.transform.position = pos;
                camp.zone = WorldGen.RegionAt(pos);
                camp.fixedLevel = WorldGen.LevelAt(pos);
                camp.seed = (int)(h & 0x7FFFFFFF);
            }
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
            plateau.SetTilesBlock(b, empty);
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
