using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace TWF
{
    // Builds a boss arena at runtime, different per boss:
    //   World's Fear: a ruined throne hall (cross-shaped stone floor, pillars, lava moat, ruin cliffs).
    //   Guardians: an irregular natural platform in their home biome, with cover to match
    //   (trees for the spider, broken pillars for the skeleton, dead trees for the zombie, rocks for the slime).
    // The fight takes place on a raised platform; everything around it lies far below (lava, or the
    // biome's terrain with decor), so the view never shows empty space and nothing blocks the camera.
    [DefaultExecutionOrder(-80)]
    public class BossArenaSetup : MonoBehaviour
    {
        public static BossArenaSetup Instance { get; private set; }

        const int Half = 44;              // grid covers -Half..Half-1
        const float Drop = 6f;            // how far below the platform the surroundings lie (+Z is down)
        const float Lip = 0.45f;          // low edge wall around the platform

        enum C : byte { Floor, Wall, Moat }
        C[,] cells;
        BossKind kind;
        Biome biome;
        readonly List<Vector2> edgePoints = new List<Vector2>();
        System.Random rng;
        ArtSet art;

        public Vector2 HeroSpawn { get; private set; } = new Vector2(0, -8);
        public Vector2 BossSpawn { get; private set; } = new Vector2(0, 6);

        void Awake()
        {
            Instance = this;
#if UNITY_EDITOR
            // Editor testing: a boss picked before entering play mode (statics reset on domain reload).
            int test = UnityEditor.SessionState.GetInt("TWF_TestBoss", -1);
            if (test >= 0) { WorldManager.PendingBossKind = (BossKind)test; UnityEditor.SessionState.EraseInt("TWF_TestBoss"); }
#endif
            kind = WorldManager.PendingBossKind;
            biome = BossTitan.ArenaBiome(kind, WorldManager.PendingBossBiome);
            WorldManager.PendingBossBiome = biome;
            rng = new System.Random(WorldManager.PendingBossLevel * 131 + (int)kind);
            art = ArtSet.Instance;

            cells = new C[Half * 2, Half * 2];
            for (int x = -Half; x < Half; x++)
            for (int y = -Half; y < Half; y++)
                cells[x + Half, y + Half] = kind == BossKind.WorldsFear ? HallShape(x, y) : BasinShape(x, y);

            BuildTiles();
            BuildCliffs();
            BuildCover();
            BuildDecor();
            CollectEdges();
            var boot = FindAnyObjectByType<GameBootstrap>();
            if (boot != null && boot.defaultSpawn != null) boot.defaultSpawn.position = HeroSpawn;
        }

        void Start()
        {
            BossTitan.Spawn(BossSpawn, Mathf.Max(5, WorldManager.PendingBossLevel), biome, kind);
        }

        // ---------- Shapes ----------

        C HallShape(int x, int y)
        {
            var c = HallFloor(x, y);
            if (c == C.Floor) return c;
            // A lava sea right below the hall, giving way to scorched ground further out.
            float n = Mathf.PerlinNoise(x * 0.12f + 3.1f, y * 0.12f + 7.7f) * 6f;
            return Mathf.Max(Mathf.Abs(x) - 13, Mathf.Abs(y) - 18) < 4 + n ? C.Moat : C.Wall;
        }

        C HallFloor(int x, int y)
        {
            // A cross: a long nave plus a wide transept, with a throne apse at the north.
            bool nave = Mathf.Abs(x) <= 6 && y >= -12 && y <= 12;
            bool transept = Mathf.Abs(y) <= 5 && Mathf.Abs(x) <= 13;
            bool apse = new Vector2(x, y - 12).magnitude < 6.5f;
            if (nave || transept || apse) return C.Floor;
            return C.Wall;
        }

        C BasinShape(int x, int y)
        {
            // An irregular bowl: the radius wobbles with noise around the circle.
            float a = Mathf.Atan2(y, x);
            float seed = WorldManager.PendingBossLevel * 0.37f;
            float r = 12.5f + Mathf.PerlinNoise(Mathf.Cos(a) * 1.4f + seed, Mathf.Sin(a) * 1.4f + seed) * 6f - 2f;
            float d = new Vector2(x * 0.85f, y * 1.1f).magnitude;
            return d < r ? C.Floor : C.Wall;
        }

        bool Is(int x, int y, C c) => x >= -Half && y >= -Half && x < Half && y < Half && cells[x + Half, y + Half] == c;

        public bool Inside(Vector2 p, float margin = 0f)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            if (!Is(x, y, C.Floor)) return false;
            if (margin <= 0f) return true;
            int m = Mathf.CeilToInt(margin);
            for (int dx = -m; dx <= m; dx++)
            for (int dy = -m; dy <= m; dy++)
                if (dx * dx + dy * dy <= margin * margin && !Is(x + dx, y + dy, C.Floor)) return false;
            return true;
        }

        public Vector2 ClampInside(Vector2 p, float margin = 1f)
        {
            if (Inside(p, margin)) return p;
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                var q = Vector2.Lerp(p, Vector2.zero, t);
                if (Inside(q, margin)) return q;
            }
            return Vector2.zero;
        }

        // A spot along the arena edge, preferring the side away from a point.
        public Vector2 EdgePoint(Vector2 awayFrom)
        {
            if (edgePoints.Count == 0) return Vector2.zero;
            Vector2 best = edgePoints[0];
            float bestScore = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = edgePoints[rng.Next(edgePoints.Count)];
                float score = Vector2.Distance(p, awayFrom) + (float)rng.NextDouble() * 4f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        void CollectEdges()
        {
            for (int x = -Half + 3; x < Half - 3; x++)
            for (int y = -Half + 3; y < Half - 3; y++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (Inside(p, 1.6f) && !Inside(p, 3.2f)) edgePoints.Add(p);
            }
        }

        // ---------- Tiles ----------

        TileBase[] FloorTiles() => art != null ? (kind == BossKind.WorldsFear ? art.ruinsGround : art.Ground(biome)) : WorldTiles.Instance.ruinsFloor;
        TileBase[] RimTiles() => art != null ? art.Ground(biome) : WorldTiles.Instance.grassPlain;

        void BuildTiles()
        {
            var grid = new GameObject("Grid").AddComponent<Grid>();
            grid.transform.SetParent(transform, false);
            var floor = MakeTilemap(grid.transform, "Floor", 0f, RenderAssets.Instance.litGround);
            var below = MakeTilemap(grid.transform, "Lowlands", Drop, RenderAssets.Instance.litGround);
            below.color = new Color(0.55f, 0.55f, 0.6f);   // darker far below, so the platform edge reads clearly
            var blockGo = new GameObject("Blockers");
            blockGo.transform.SetParent(grid.transform, false);
            blockGo.layer = LayerMask.NameToLayer("Walls");
            var blockers = blockGo.AddComponent<Tilemap>();
            blockGo.AddComponent<TilemapCollider2D>();
            blockGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;

            var floorTiles = FloorTiles();
            var rimTiles = RimTiles();
            var path = art != null ? art.Path(biome) : null;
            var moat = new List<Vector3>();
            for (int x = -Half; x < Half; x++)
            for (int y = -Half; y < Half; y++)
            {
                var cell = new Vector3Int(x, y, 0);
                uint h = (uint)((x * 73856093) ^ (y * 19349663)) & 0x7fffffff;
                switch (cells[x + Half, y + Half])
                {
                    case C.Floor:
                        bool aisle = kind == BossKind.WorldsFear && Mathf.Abs(x) <= 1 && path != null;
                        floor.SetTile(cell, aisle ? path[h % (uint)path.Length] : floorTiles[h % (uint)floorTiles.Length]);
                        break;
                    case C.Moat:
                        blockers.SetTile(cell, WorldTiles.Instance.blocker);
                        moat.Add(new Vector3(x, y));
                        break;
                    default:
                        below.SetTile(cell, rimTiles[h % (uint)rimTiles.Length]);
                        blockers.SetTile(cell, WorldTiles.Instance.blocker);
                        break;
                }
            }

            if (moat.Count > 0)
            {
                var mb = new Quads();
                foreach (var m in moat) mb.Add(m + new Vector3(0, 0, Drop - 0.4f), Vector3.right, Vector3.up, Vector3.back, 1f, 1f);
                MeshObject("Lava Moat", mb, RenderAssets.Instance.lava, false);
                for (int i = 0; i < moat.Count; i += 18)
                {
                    var holder = new GameObject("LavaGlow");
                    holder.transform.SetParent(transform, false);
                    holder.transform.position = moat[i] + new Vector3(0.5f, 0.5f, Drop - 2f);
                    PointLights.Attach(holder.transform, new Color(1f, 0.45f, 0.15f), 9f, 2.2f, Vector3.zero, true);
                }
            }
        }

        Tilemap MakeTilemap(Transform parent, string name, float z, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0, z);
            var tm = go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            RenderAssets.MakeLit(r, false, mat);
            r.sortingOrder = -100;
            r.mode = TilemapRenderer.Mode.Chunk;
            return tm;
        }

        // ---------- Cliffs ----------

        void BuildCliffs()
        {
            var cliffTex = art != null && art.Cliff(biome) != null ? art.Cliff(biome) : WorldTiles.CliffTexture(biome);
            var wallTex = kind == BossKind.WorldsFear && art != null && art.brick != null ? art.brick : cliffTex;
            var mat = WorldTiles.MeshMaterial("arena_cliff_" + kind + biome, wallTex);
            var cliffs = new Quads();
            var lip = new Quads();
            for (int x = -Half; x < Half; x++)
            for (int y = -Half; y < Half; y++)
            {
                if (!Is(x, y, C.Floor)) continue;
                // Wherever the platform ends: a cliff face dropping down, and a low lip on top.
                if (!Is(x, y - 1, C.Floor)) Edge(cliffs, lip, new Vector3(x, y), Vector3.right, Vector3.down);
                if (!Is(x, y + 1, C.Floor)) Edge(cliffs, lip, new Vector3(x + 1, y + 1), Vector3.left, Vector3.up);
                if (!Is(x - 1, y, C.Floor)) Edge(cliffs, lip, new Vector3(x, y + 1), Vector3.down, Vector3.left);
                if (!Is(x + 1, y, C.Floor)) Edge(cliffs, lip, new Vector3(x + 1, y), Vector3.up, Vector3.right);
            }
            MeshObject("Cliffs", cliffs, mat, true);
            MeshObject("Edge Lip", lip, mat, true);
        }

        // One unit of platform edge starting at o, running along u, facing outward along n.
        void Edge(Quads cliffs, Quads lip, Vector3 o, Vector3 u, Vector3 n)
        {
            cliffs.Add(o, u, Vector3.forward, n, 1f, Drop);
            // The lip: a short wall a little inside the edge, with crenels on the hall.
            bool crenel = kind == BossKind.WorldsFear && ((int)(o.x + o.y) & 1) == 0;
            float h = Lip + (crenel ? 0.3f : 0f);
            var inner = o - n * 0.25f;
            lip.Add(o, u, Vector3.back, n, 1f, h);
            lip.Add(inner + u, -u, Vector3.back, -n, 1f, h);
            lip.Add(inner + Vector3.back * h, u, n, Vector3.back, 1f, 0.25f);
        }

        // ---------- Cover (blocks sight, shockwaves and projectiles) ----------

        void BuildCover()
        {
            var brick = art != null && art.brick != null ? art.brick : WorldTiles.BrickTexture();
            if (kind == BossKind.WorldsFear)
            {
                var mat = WorldTiles.MeshMaterial("arena_pillar", brick);
                foreach (var p in new[] { new Vector2(-4f, 3f), new Vector2(4f, 3f), new Vector2(-4f, -6f), new Vector2(4f, -6f), new Vector2(-10f, 0f), new Vector2(10f, 0f) })
                    Pillar(p, 1.4f, 6.5f, mat);
                // The empty throne at the back of the apse.
                Pillar(new Vector2(0f, 16f), 2.4f, 2.2f, mat);
                return;
            }
            // Guardian platforms: cover that suits the boss.
            Sprite[] cover = art == null ? null
                : kind == BossKind.Spider ? art.oak
                : kind == BossKind.Skeleton ? art.brokenPillar
                : kind == BossKind.Zombie ? art.deadTree
                : art.rockLarge;
            int n = 4 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                for (int tries = 0; tries < 30; tries++)
                {
                    var p = new Vector2((float)(rng.NextDouble() * 2 - 1) * 9f, (float)(rng.NextDouble() * 2 - 1) * 9f);
                    if (!Inside(p, 3f) || p.magnitude < 4f || Vector2.Distance(p, HeroSpawn) < 4f) continue;
                    if (cover != null && cover.Length > 0) Decor(cover[rng.Next(cover.Length)], p, 0f, 1.6f, true);
                    else Pillar(p, 1.6f, 3f, WorldTiles.MeshMaterial("arena_pillar", brick));
                    break;
                }
            }
        }

        // ---------- Decor on the raised rim (fills the view up to the fog) ----------

        void BuildDecor()
        {
            for (int i = 0; i < 260; i++)
            {
                int x = rng.Next(-Half + 1, Half - 1), y = rng.Next(-Half + 1, Half - 1);
                if (!Is(x, y, C.Wall)) continue;
                if (Is(x + 1, y, C.Floor) || Is(x - 1, y, C.Floor) || Is(x, y + 1, C.Floor) || Is(x, y - 1, C.Floor)) continue;
                var s = RimSprite();
                if (s != null) Decor(s, new Vector2(x + 0.5f, y + 0.4f), Drop, 1.2f, false);
            }
        }

        Sprite RimSprite()
        {
            if (art == null) return null;
            Sprite Any(Sprite[] a) => a != null && a.Length > 0 ? a[rng.Next(a.Length)] : null;
            double r = rng.NextDouble();
            switch (kind == BossKind.WorldsFear ? Biome.Ruins : biome)
            {
                case Biome.Forest: return r < 0.4 ? Any(art.oak) : r < 0.7 ? Any(art.pine) : r < 0.85 ? Any(art.birch) : Any(art.bush);
                case Biome.Marsh: return r < 0.45 ? Any(art.willow) : r < 0.75 ? Any(art.deadTree) : Any(art.mushroomPurple);
                case Biome.Ruins: return r < 0.3 ? Any(art.brokenPillar) : r < 0.55 ? Any(art.deadTree) : r < 0.8 ? Any(art.rockLarge) : Any(art.pillar);
                default: return r < 0.5 ? Any(art.charredTree) : r < 0.8 ? Any(art.rockLarge) : Any(art.mushroomRed);
            }
        }

        void Decor(Sprite sprite, Vector2 pos, float z, float scale, bool solid)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, pos.y, z);
            go.layer = LayerMask.NameToLayer("Walls");
            var bb = Billboard.Create(go.transform, "Visual");
            bb.localScale = Vector3.one * scale * (0.9f + (float)rng.NextDouble() * 0.25f);
            var sr = bb.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            bool sways = sprite.name.StartsWith("tree") || sprite.name.StartsWith("bush");
            RenderAssets.MakeLit(sr, true, sways ? RenderAssets.Instance.litWind : null);
            if (solid) go.AddComponent<CircleCollider2D>().radius = Mathf.Max(0.6f, sprite.bounds.size.x * scale * 0.3f);
        }

        void Pillar(Vector2 c, float size, float height, Material mat)
        {
            var mb = new Quads();
            float h = size / 2f;
            Vector3[] corners = { new Vector3(c.x - h, c.y - h), new Vector3(c.x + h, c.y - h), new Vector3(c.x + h, c.y + h), new Vector3(c.x - h, c.y + h) };
            Vector3[] normals = { Vector3.down, Vector3.right, Vector3.up, Vector3.left };
            for (int i = 0; i < 4; i++)
                mb.Add(corners[i], (corners[(i + 1) % 4] - corners[i]).normalized, Vector3.back, normals[i], size, height);
            mb.Add(corners[0] + Vector3.back * height, Vector3.right, Vector3.up, Vector3.back, size, size);
            MeshObject("Pillar", mb, mat, true);
            var col = new GameObject("Pillar Collider");
            col.transform.SetParent(transform, false);
            col.transform.position = c;
            col.layer = LayerMask.NameToLayer("Walls");
            col.AddComponent<BoxCollider2D>().size = Vector2.one * size;
        }

        // ---------- Mesh helpers ----------

        class Quads
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            public void Add(Vector3 o, Vector3 u, Vector3 w, Vector3 normal, float a, float b)
            {
                int i = v.Count;
                v.Add(o); v.Add(o + u * a); v.Add(o + w * b); v.Add(o + u * a + w * b);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(a, 0)); uv.Add(new Vector2(0, b)); uv.Add(new Vector2(a, b));
                t.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 });
            }
        }

        void MeshObject(string name, Quads q, Material mat, bool shadows)
        {
            if (q.v.Count == 0 || mat == null) return;
            var m = new Mesh { name = name, indexFormat = q.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
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
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }
    }
}
