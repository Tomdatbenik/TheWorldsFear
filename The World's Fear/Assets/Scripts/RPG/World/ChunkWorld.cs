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

        public IEnumerable<Vector2Int> LoadedChunks => chunks.Keys;

        void Awake()
        {
            Instance = this;
            WorldGen.HubRect = hubRect;
            tiles = WorldTiles.Instance;
            // Note: Unity's fake-null objects break '??', so check explicitly.
            if (GetComponent<Grid>() == null) gameObject.AddComponent<Grid>();

            ground = MakeTilemap("Ground", 0f, RenderAssets.Instance.litGround, false);
            detail = MakeTilemap("Detail", -0.01f, RenderAssets.Instance.litDetail, false);
            plateau = MakeTilemap("Plateau Tops", -WorldGen.PlateauHeight, RenderAssets.Instance.litGround, false);
            blockers = MakeTilemap("Blockers", 0f, null, true);
            grassQuad = BuildQuad();
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
            var water = new MeshBuilder();
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
                        groundTiles[i] = PathTile(biome, h);
                        break;
                    case Cell.Water:
                    case Cell.Lava:
                        blockTiles[i] = tiles.blocker;
                        (cell == Cell.Water ? water : lava).Quad(new Vector3(wx, wy, 0.12f), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Vector3.back, 1, 1);
                        // Shore: the pool sits slightly below the ground.
                        AddDrop(cells, x, y, wx, wy, cell, cell == Cell.Water ? water : lava);
                        break;
                    case Cell.Plateau:
                        groundTiles[i] = GroundTile(biome, h);
                        plateauTiles[i] = GroundTile(biome, h >> 3);
                        blockTiles[i] = tiles.blocker;
                        AddCliffs(cells, x, y, wx, wy, rockFaces);
                        break;
                    case Cell.Wall:
                        groundTiles[i] = GroundTile(biome, h);
                        blockTiles[i] = tiles.blocker;
                        AddBox(wx, wy, 1.6f, brickFaces);
                        break;
                    default:
                        groundTiles[i] = GroundTile(biome, h);
                        if ((h & 0xFF) < 14) detailTiles[i] = DetailTile(biome, h);
                        break;
                }
            }

            ground.SetTilesBlock(bounds, groundTiles);
            detail.SetTilesBlock(bounds, detailTiles);
            plateau.SetTilesBlock(bounds, plateauTiles);
            blockers.SetTilesBlock(bounds, blockTiles);

            MeshObject(chunk.root, "Cliffs", rockFaces, WorldTiles.MeshMaterial("cliff" + chunkBiome, WorldTiles.CliffTexture(chunkBiome)), true);
            MeshObject(chunk.root, "Walls", brickFaces, WorldTiles.MeshMaterial("brick", WorldTiles.BrickTexture()), true);
            MeshObject(chunk.root, "Water", water, RenderAssets.Instance.water, false);
            MeshObject(chunk.root, "Lava", lava, RenderAssets.Instance.lava, false);
            if (lava.Count > 0) PointLights.Attach(chunk.root.transform, new Color(1f, 0.45f, 0.15f), 10f, 1.4f, (Vector3)LavaCenter(cells, x0, y0) + new Vector3(0, 0, -1.5f), true);

            PlaceDecor(chunk, cells, x0, y0, decorRng);
            PlaceGrass(chunk, cells, x0, y0, decorRng);
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
            switch (b)
            {
                case Biome.Ruins: return Pick(tiles.ruinsFloor, h);
                case Biome.Ember: return Pick(tiles.ash, h);
                default: return (h % 9 == 0) ? Pick(tiles.grassDetail, h >> 4) : Pick(tiles.grassPlain, h >> 4);
            }
        }

        TileBase PathTile(Biome b, uint h)
        {
            switch (b)
            {
                case Biome.Ruins: return tiles.ruinsPath;
                case Biome.Ember: return tiles.emberPath;
                default: return Pick(tiles.dirt, h);
            }
        }

        TileBase DetailTile(Biome b, uint h)
        {
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
                bool solid = !sprite.name.StartsWith("bush");
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

        Sprite DecorSprite(Biome b, System.Random rng)
        {
            double r = rng.NextDouble();
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
            sr.color = DecorTint(biome, sprite);
            bool sways = sprite.name.StartsWith("tree") || sprite.name.StartsWith("bush");
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
                    if (!WorldGen.Walkable(cells[x + dx + 1, y + dy + 1]) || cells[x + dx + 1, y + dy + 1] == Cell.Hub) open = false;
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
            if (WorldGen.HasBossGate(c.x, c.y, out var bossCell))
            {
                ClearAround(bossCell, 3);
                var go = new GameObject("BossGate");
                go.transform.SetParent(chunk.root.transform, false);
                go.transform.position = (Vector2)bossCell + Vector2.one * 0.5f;
                var gate = go.AddComponent<BossGate>();
                gate.recommendedLevel = WorldGen.LevelAt(bossCell) + 3;
            }
        }

        // Make sure points of interest aren't buried in walls or water.
        void ClearAround(Vector2Int center, int r)
        {
            var empty = new TileBase[(2 * r + 1) * (2 * r + 1)];
            var b = new BoundsInt(center.x - r, center.y - r, 0, 2 * r + 1, 2 * r + 1, 1);
            blockers.SetTilesBlock(b, empty);
            plateau.SetTilesBlock(b, empty);
            var fill = new TileBase[empty.Length];
            for (int i = 0; i < fill.Length; i++) fill[i] = tiles.dirt[i % tiles.dirt.Length];
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
            public void Quad(Vector3 origin, Vector3 u, Vector3 v, Vector3 normal, float w, float h)
            {
                int i = verts.Count;
                verts.Add(origin);
                verts.Add(origin + u * w);
                verts.Add(origin + v * h);
                verts.Add(origin + u * w + v * h);
                for (int k = 0; k < 4; k++) normals.Add(normal);
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(w, 0));
                uvs.Add(new Vector2(0, h));
                uvs.Add(new Vector2(w, h));
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
