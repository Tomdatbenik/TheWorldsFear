using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace TWF
{
    // The environment proof of concept: a patch of the village, north of the plaza up the main road, in the new
    // art style. Remove this file (and its call-free bootstrap) and the old village comes back untouched.
    public class EnvTestSpot : MonoBehaviour
    {
        // The spot is the Rect below. Everything old in it is cleared at start.
        static readonly Rect Spot = new Rect(-16f, 34f, 38f, 22f);
        const float TerraceHeight = 1.5f;
        static readonly Rect Terrace = new Rect(6f, 46f, 10f, 8f);
        static readonly Rect Stairs = new Rect(10f, 43.5f, 2f, 2.5f);
        static readonly Rect Pool = new Rect(13f, 41f, 5f, 3.5f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded += (s, _) => Spawn();
            Spawn();
        }

        static void Spawn()
        {
            if (FindAnyObjectByType<Village>() == null || FindAnyObjectByType<EnvTestSpot>() != null) return;
            new GameObject("Env Test Spot").AddComponent<EnvTestSpot>();
        }

        Tilemap ground, detail;

        void Start()
        {
            TerrainHeight.Clear();
            ClearOldDecor();
            FindGround();
            PaintGround();
            BuildRoad();
            BuildTerrace();
            BuildPool();
            var t = transform;
            EnvArt.Place("timber_house", t, new Vector2(-6f, 40.5f));
            EnvArt.Place("thatch_cottage", t, new Vector2(10f, 38f));
            EnvArt.Place("well", t, new Vector2(-4.2f, 48f));
            EnvArt.PlaceTree("oak", t, new Vector2(-11f, 50f));
            EnvArt.PlaceTree("oak", t, new Vector2(-13.5f, 43f));
            EnvArt.PlaceTree("oak", t, new Vector2(-12.5f, 36.5f));
            EnvArt.PlaceTree("oak", t, new Vector2(-7f, 52.5f));
            EnvArt.PlaceTree("oak", t, new Vector2(17.5f, 38f));
            EnvArt.PlaceTree("oak", t, new Vector2(20f, 50f));
            EnvArt.PlaceTree("oak", t, new Vector2(8.5f, 51.5f), TerraceHeight);
            EnvArt.PlaceTree("oak", t, new Vector2(14f, 50f), TerraceHeight);
            var hero = HeroController.Instance;
            if (hero != null && hero.GetComponent<GroundFollow>() == null) hero.gameObject.AddComponent<GroundFollow>();
        }

        void ClearOldDecor()
        {
            var village = FindAnyObjectByType<Village>();
            var kill = new System.Collections.Generic.List<GameObject>();
            foreach (Transform c in village.transform)
                if (Spot.Contains(c.position) && c.GetComponentInChildren<Billboard>() != null) kill.Add(c.gameObject);
            foreach (var g in kill) Destroy(g);
        }

        void FindGround()
        {
            foreach (var tm in FindObjectsByType<Tilemap>())
                if (tm.name == "Ground" && tm.transform.parent != null && tm.transform.parent.name == "Village Ground") ground = tm;
            var go = new GameObject("Env Detail");
            go.transform.SetParent(ground.transform.parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            detail = go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            r.sharedMaterial = RenderAssets.Instance.litDetail;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.sortingOrder = -99;
            r.mode = TilemapRenderer.Mode.Chunk;
        }

        // ---------- Ground ----------

        static float RoadCentre(float y) => 2.5f + Mathf.Sin((y - 34f) * 0.16f) * 1.1f;
        static bool OnRoad(int x, int y) => Mathf.Abs(x + 0.5f - RoadCentre(y + 0.5f)) < 1.75f;

        Tile[,] Cut(Texture2D t, int cols, int rows)
        {
            var o = new Tile[cols, rows];
            for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = Sprite.Create(t, new Rect(x * 72, y * 72, 72, 72), new Vector2(0.5f, 0.5f), 72f, 0, SpriteMeshType.FullRect);
                o[x, y] = tile;
            }
            return o;
        }

        void PaintGround()
        {
            var grass = Cut(EnvArt.Tex("grass_tile"), 4, 4);
            int x0 = Mathf.FloorToInt(Spot.xMin), x1 = Mathf.CeilToInt(Spot.xMax), y0 = Mathf.FloorToInt(Spot.yMin), y1 = Mathf.CeilToInt(Spot.yMax);
            for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
            {
                int m(int v) => ((v % 4) + 4) % 4;
                var cell = new Vector3Int(x, y, 0);
                bool poolCell = Pool.Contains(new Vector2(x + 0.5f, y + 0.5f));
                if (poolCell) { ground.SetTile(cell, null); continue; }
                ground.SetTile(cell, grass[m(x), m(y)]);
            }
        }

        // The road is painted pixel by pixel from the curve, so its edge is ragged rather than one tile wide.
        void BuildRoad()
        {
            const int ppu = 72;
            var rect = new Rect(-2f, Spot.yMin, 10f, Spot.height);
            int W = Mathf.RoundToInt(rect.width * ppu), H = Mathf.RoundToInt(rect.height * ppu);
            var dirt = EnvArt.Tex("dirt_tile").GetPixels32();
            var px = new Color32[W * H];
            var rim = new Color32(96, 66, 40, 255);
            const int T = 288;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float wx = rect.xMin + (x + 0.5f) / ppu, wy = rect.yMin + (y + 0.5f) / ppu;
                float d = Mathf.Abs(wx - RoadCentre(wy));
                float edge = 1.65f + (Mathf.PerlinNoise(wx * 0.9f + 3.1f, wy * 0.9f) - 0.5f) * 0.9f + (Mathf.PerlinNoise(wx * 4f, wy * 4f + 9f) - 0.5f) * 0.35f;
                if (d > edge) continue;
                int u = ((Mathf.RoundToInt(wx * ppu) % T) + T) % T, v = ((Mathf.RoundToInt(wy * ppu) % T) + T) % T;
                px[y * W + x] = edge - d < 0.05f ? rim : dirt[v * T + u];
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "EnvRoad" };
            tex.SetPixels32(px);
            tex.Apply();
            var mat = new Material(RenderAssets.Instance.litDetail) { mainTexture = tex };
            var q = new Quads();
            float z = -0.02f;
            q.Add(new Vector3(rect.xMin, rect.yMin, z), new Vector3(rect.xMax, rect.yMin, z), new Vector3(rect.xMax, rect.yMax, z), new Vector3(rect.xMin, rect.yMax, z),
                Vector3.back, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            Mesh(q, "Road", mat, false);
        }

        // ---------- Terrace ----------

        void BuildTerrace()
        {
            var top = new Quads();
            var wall = new Quads();
            var treads = new Quads();
            float h = TerraceHeight;
            Rect t = Terrace;
            float z = -h;

            // Grass on top, tiled every 4 units. A real raised surface you walk on.
            top.Add(new Vector3(t.xMin, t.yMin, z), new Vector3(t.xMax, t.yMin, z), new Vector3(t.xMax, t.yMax, z), new Vector3(t.xMin, t.yMax, z),
                Vector3.back, new Vector2(t.xMin / 4f, t.yMin / 4f), new Vector2(t.xMax / 4f, t.yMin / 4f), new Vector2(t.xMax / 4f, t.yMax / 4f), new Vector2(t.xMin / 4f, t.yMax / 4f));
            // The four walls, u every 4 units, v over the full height. The south wall has the stair gap.
            WallRun(wall, new Vector2(t.xMin, t.yMin), new Vector2(Stairs.xMin, t.yMin), Vector3.down, h);
            WallRun(wall, new Vector2(Stairs.xMax, t.yMin), new Vector2(t.xMax, t.yMin), Vector3.down, h);
            WallRun(wall, new Vector2(t.xMax, t.yMin), new Vector2(t.xMax, t.yMax), Vector3.right, h);
            WallRun(wall, new Vector2(t.xMax, t.yMax), new Vector2(t.xMin, t.yMax), Vector3.up, h);
            WallRun(wall, new Vector2(t.xMin, t.yMax), new Vector2(t.xMin, t.yMin), Vector3.left, h);

            // Stairs: six stone steps up to the terrace, cheek walls on both sides.
            int steps = 6;
            float run = Stairs.height / steps, rise = h / steps;
            for (int i = 0; i < steps; i++)
            {
                float y = Stairs.yMin + i * run, zt = -(i + 1) * rise;
                treads.Add(new Vector3(Stairs.xMin, y, zt), new Vector3(Stairs.xMax, y, zt), new Vector3(Stairs.xMax, y + run, zt), new Vector3(Stairs.xMin, y + run, zt),
                    Vector3.back, new Vector2(0f, 0.05f), new Vector2(0.5f, 0.05f), new Vector2(0.5f, 0.3f), new Vector2(0f, 0.3f));
                float zb = -i * rise;
                wall.Add(new Vector3(Stairs.xMin, y, zb), new Vector3(Stairs.xMax, y, zb), new Vector3(Stairs.xMax, y, zt), new Vector3(Stairs.xMin, y, zt), Vector3.down,
                    new Vector2(0f, 0.4f), new Vector2(0.5f, 0.4f), new Vector2(0.5f, 0.6f), new Vector2(0f, 0.6f));
                // Cheeks.
                wall.Add(new Vector3(Stairs.xMin, y + run, zb), new Vector3(Stairs.xMin, y, zb), new Vector3(Stairs.xMin, y, zt), new Vector3(Stairs.xMin, y + run, zt), Vector3.left, Vector2.zero, new Vector2(0.15f, 0f), new Vector2(0.15f, 0.2f), new Vector2(0f, 0.2f));
                wall.Add(new Vector3(Stairs.xMax, y, zb), new Vector3(Stairs.xMax, y + run, zb), new Vector3(Stairs.xMax, y + run, zt), new Vector3(Stairs.xMax, y, zt), Vector3.right, Vector2.zero, new Vector2(0.15f, 0f), new Vector2(0.15f, 0.2f), new Vector2(0f, 0.2f));
            }
            Mesh(top, "Terrace Top", WorldTiles.MeshMaterial("env_top", EnvArt.Tex("grass_tile")), true);
            Mesh(wall, "Terrace Walls", EnvArt.WallMaterial(EnvArt.Tex("stone_wall_tile"), 0f), true);
            Mesh(treads, "Terrace Stairs", WorldTiles.MeshMaterial("env_tread", EnvArt.Tex("stone_wall_tile")), true);

            TerrainHeight.AddFlat(Terrace, h);
            TerrainHeight.AddRamp(Stairs, 0f, h);
            // Walls of the level: one line everywhere except where the stairs come up.
            Edge(new Vector2(t.xMin, t.yMin), new Vector2(Stairs.xMin, t.yMin));
            Edge(new Vector2(Stairs.xMax, t.yMin), new Vector2(t.xMax, t.yMin));
            Edge(new Vector2(t.xMax, t.yMin), new Vector2(t.xMax, t.yMax));
            Edge(new Vector2(t.xMax, t.yMax), new Vector2(t.xMin, t.yMax));
            Edge(new Vector2(t.xMin, t.yMax), new Vector2(t.xMin, t.yMin));
            Edge(new Vector2(Stairs.xMin, Stairs.yMin), new Vector2(Stairs.xMin, Stairs.yMax));
            Edge(new Vector2(Stairs.xMax, Stairs.yMin), new Vector2(Stairs.xMax, Stairs.yMax));
        }

        static void WallRun(Quads q, Vector2 a, Vector2 b, Vector3 n, float h)
        {
            float len = Vector2.Distance(a, b);
            float u = 0f;
            q.Add(new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f), new Vector3(b.x, b.y, -h), new Vector3(a.x, a.y, -h), n,
                new Vector2(u, 0f), new Vector2(u + len / 4f, 0f), new Vector2(u + len / 4f, 1f), new Vector2(u, 1f));
        }

        void Edge(Vector2 a, Vector2 b)
        {
            var go = new GameObject("Terrace Edge");
            go.transform.SetParent(transform, false);
            go.layer = LayerMask.NameToLayer("Walls");
            var e = go.AddComponent<EdgeCollider2D>();
            e.points = new[] { a, b };
            e.edgeRadius = 0.05f;
        }

        // ---------- Water ----------

        void BuildPool()
        {
            var q = new Quads();
            var p = Pool;
            q.Add(new Vector3(p.xMin, p.yMin, 0.12f), new Vector3(p.xMax, p.yMin, 0.12f), new Vector3(p.xMax, p.yMax, 0.12f), new Vector3(p.xMin, p.yMax, 0.12f),
                Vector3.back, new Vector2(p.xMin, p.yMin), new Vector2(p.xMax, p.yMin), new Vector2(p.xMax, p.yMax), new Vector2(p.xMin, p.yMax));
            var poolMat = new Material(RenderAssets.Instance.water);
            poolMat.SetColor("_Deep", new Color(0.14f, 0.58f, 0.82f));
            poolMat.SetColor("_Shallow", new Color(0.4f, 0.86f, 0.94f));
            Mesh(q, "Pool", poolMat, false);
            var rim = new Quads();
            float d = 0.14f;
            rim.Add(new Vector3(p.xMin, p.yMin, 0f), new Vector3(p.xMax, p.yMin, 0f), new Vector3(p.xMax, p.yMin, d), new Vector3(p.xMin, p.yMin, d), Vector3.up, Vector2.zero, new Vector2(1, 0), new Vector2(1, 0.1f), new Vector2(0, 0.1f));
            rim.Add(new Vector3(p.xMax, p.yMin, 0f), new Vector3(p.xMax, p.yMax, 0f), new Vector3(p.xMax, p.yMax, d), new Vector3(p.xMax, p.yMin, d), Vector3.left, Vector2.zero, new Vector2(1, 0), new Vector2(1, 0.1f), new Vector2(0, 0.1f));
            rim.Add(new Vector3(p.xMax, p.yMax, 0f), new Vector3(p.xMin, p.yMax, 0f), new Vector3(p.xMin, p.yMax, d), new Vector3(p.xMax, p.yMax, d), Vector3.down, Vector2.zero, new Vector2(1, 0), new Vector2(1, 0.1f), new Vector2(0, 0.1f));
            rim.Add(new Vector3(p.xMin, p.yMax, 0f), new Vector3(p.xMin, p.yMin, 0f), new Vector3(p.xMin, p.yMin, d), new Vector3(p.xMin, p.yMax, d), Vector3.right, Vector2.zero, new Vector2(1, 0), new Vector2(1, 0.1f), new Vector2(0, 0.1f));
            Mesh(rim, "Pool Rim", WorldTiles.MeshMaterial("env_rim", EnvArt.Tex("stone_wall_tile")), false);
            PoolShore(p);
            var go = new GameObject("Pool Block");
            go.transform.SetParent(transform, false);
            go.transform.position = p.center;
            go.layer = LayerMask.NameToLayer("Walls");
            go.AddComponent<BoxCollider2D>().size = p.size;
        }

        // Grass and a stone rim laid over the pool's edge, so the water has a natural outline.
        void PoolShore(Rect p)
        {
            const int ppu = 72;
            var rect = new Rect(p.xMin - 0.5f, p.yMin - 0.5f, p.width + 1f, p.height + 1f);
            int W = Mathf.RoundToInt(rect.width * ppu), H = Mathf.RoundToInt(rect.height * ppu);
            var grass = EnvArt.Tex("grass_tile").GetPixels32();
            var stone = EnvArt.Tex("stone_wall_tile").GetPixels32();
            var px = new Color32[W * H];
            const int T = 288, SW = 288, SH = 108;
            var c = p.center;
            float rx = p.width / 2f - 0.15f, ry = p.height / 2f - 0.15f;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float wx = rect.xMin + (x + 0.5f) / ppu, wy = rect.yMin + (y + 0.5f) / ppu;
                float nx = (wx - c.x) / rx, ny = (wy - c.y) / ry;
                float r = Mathf.Sqrt(nx * nx + ny * ny) + (Mathf.PerlinNoise(wx * 1.1f, wy * 1.1f + 5f) - 0.5f) * 0.35f + (Mathf.PerlinNoise(wx * 5f, wy * 5f) - 0.5f) * 0.08f;
                if (r < 0.9f) continue;     // open water
                int u = ((Mathf.RoundToInt(wx * ppu) % T) + T) % T, v = ((Mathf.RoundToInt(wy * ppu) % T) + T) % T;
                if (r < 1.0f)
                {
                    int su = ((Mathf.RoundToInt(wx * ppu) % SW) + SW) % SW, sv = Mathf.Abs(Mathf.RoundToInt(wy * ppu) % (SH - 30));
                    px[y * W + x] = stone[sv * SW + su];
                }
                else px[y * W + x] = grass[v * T + u];
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "EnvShore" };
            tex.SetPixels32(px);
            tex.Apply();
            var mat = new Material(RenderAssets.Instance.litDetail) { mainTexture = tex };
            var q = new Quads();
            float z = -0.02f;
            q.Add(new Vector3(rect.xMin, rect.yMin, z), new Vector3(rect.xMax, rect.yMin, z), new Vector3(rect.xMax, rect.yMax, z), new Vector3(rect.xMin, rect.yMax, z),
                Vector3.back, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            Mesh(q, "Pool Shore", mat, false);
        }

        // ---------- Mesh helper ----------

        class Quads
        {
            public readonly System.Collections.Generic.List<Vector3> v = new System.Collections.Generic.List<Vector3>();
            public readonly System.Collections.Generic.List<Vector3> n = new System.Collections.Generic.List<Vector3>();
            public readonly System.Collections.Generic.List<Vector2> uv = new System.Collections.Generic.List<Vector2>();
            public readonly System.Collections.Generic.List<int> t = new System.Collections.Generic.List<int>();

            public void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
        }

        void Mesh(Quads q, string name, Material mat, bool shadow)
        {
            var m = new Mesh { name = name };
            m.SetVertices(q.v);
            m.SetNormals(q.n);
            m.SetUVs(0, q.uv);
            var colors = new Color[q.v.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            m.colors = colors;
            m.SetTriangles(q.t, 0);
            m.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadow ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            r.receiveShadows = true;
        }
    }
}
