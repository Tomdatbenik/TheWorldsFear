using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace TWF
{
    // How high the ground is at a point: flat ground is 0, terraces and stairs raise it.
    // Movement stays 2D; heights only move what you see, and edge colliders keep you on the level you are on.
    public static class TerrainHeight
    {
        public struct Region { public Rect rect; public float h0, h1; public bool ramp; }
        static readonly List<Region> regions = new List<Region>();

        public static void Clear() => regions.Clear();
        public static void AddFlat(Rect r, float h) => regions.Add(new Region { rect = r, h0 = h, h1 = h });
        // Rises along +Y from h0 at the rect's bottom edge to h1 at its top edge.
        public static void AddRamp(Rect r, float h0, float h1) => regions.Add(new Region { rect = r, h0 = h0, h1 = h1, ramp = true });

        // The open world's own heights (plateaus and stairs), set by ChunkWorld while it is alive.
        public static Func<Vector2, float> World;

        public static float At(Vector2 p)
        {
            float best = World != null ? World(p) : 0f;
            foreach (var r in regions)
                if (r.rect.Contains(p))
                    best = Mathf.Max(best, r.ramp ? Mathf.Lerp(r.h0, r.h1, Mathf.InverseLerp(r.rect.yMin, r.rect.yMax, p.y)) : r.h0);
            return best;
        }
    }

    // Lifts the visible parts of a unit to the terrain height under it (the physics body stays on the 2D plane).
    public class GroundFollow : MonoBehaviour
    {
        const float RiseSpeed = 9f;
        readonly Dictionary<Transform, float> parts = new Dictionary<Transform, float>();   // each part's own height
        readonly Dictionary<Transform, float> written = new Dictionary<Transform, float>(); // what was last set on it
        readonly List<Transform> keys = new List<Transform>();
        readonly List<Transform> gone = new List<Transform>();
        float height;
        CameraFollow cam;
        bool hero;

        void Start()
        {
            hero = GetComponent<HeroController>() != null;
            if (hero && Camera.main != null) cam = Camera.main.GetComponent<CameraFollow>();
            height = TerrainHeight.At(transform.position);
        }

        // Low and high ground only meet on stairs. A knockback, a dash or a leap can carry a body through the thin
        // wall along a plateau's edge; when that happens it is put back where it last stood.
        public static bool KeepLevels = true;
        Vector2 lastGood;
        int lastLevel = -1;        // 0 low ground, 1 plateau, -1 anything else (stairs, pillars: no rule)
        Rigidbody2D rb;

        static int LevelOf(Vector2 p)
        {
            var c = WorldGen.CellAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
            return c == Cell.Plateau ? 1 : WorldGen.Walkable(c) ? 0 : -1;
        }

        void Guard()
        {
            if (!KeepLevels || TerrainHeight.World == null) return;
            Vector2 p = transform.position;
            int level = LevelOf(p);
            if (level >= 0 && lastLevel >= 0 && level != lastLevel)
            {
                if (rb == null) rb = GetComponent<Rigidbody2D>();
                if (rb != null) { rb.position = lastGood; rb.linearVelocity = Vector2.zero; }
                transform.position = new Vector3(lastGood.x, lastGood.y, transform.position.z);
                return;
            }
            // Stairs clear the memory: whoever comes off them may be on either level.
            lastLevel = level;
            if (level >= 0) lastGood = p;
        }

        void LateUpdate()
        {
            Guard();
            float target = TerrainHeight.At(transform.position);
            height = Mathf.MoveTowards(height, target, RiseSpeed * Time.deltaTime);
            // Children that turn up later (status flames, bars) join in at the height they were made for.
            foreach (Transform c in transform)
                if (!parts.ContainsKey(c)) parts[c] = c.localPosition.z;
            keys.Clear(); keys.AddRange(parts.Keys);
            foreach (var t in keys)
            {
                if (t == null) { gone.Add(t); continue; }
                var p = t.localPosition;
                // Moved by its own animation since last frame (a leap, a hop): that is its new height.
                if (written.TryGetValue(t, out float w) && !Mathf.Approximately(p.z, w)) parts[t] = p.z;
                p.z = parts[t] - height;
                t.localPosition = p;
                written[t] = p.z;
            }
            if (gone.Count > 0) { foreach (var g in gone) { parts.Remove(g); written.Remove(g); } gone.Clear(); }
            if (cam != null) cam.groundHeight = height;
            if (hero) View.GroundHeight = height;
        }
    }

    // Lifts a whole loose object (a projectile, a pickup, an effect, floating text) to the terrain height under it.
    public class HeightLift : MonoBehaviour
    {
        float baseZ;
        bool skip, ready;

        public static void On(GameObject go)
        {
            if (TerrainHeight.World != null && go.GetComponent<HeightLift>() == null) go.AddComponent<HeightLift>();
        }

        void LateUpdate()
        {
            if (!ready)
            {
                ready = true;
                // Anything riding on a unit is lifted with it already.
                skip = transform.parent != null && (transform.parent.GetComponentInParent<GroundFollow>() != null || transform.parent.GetComponentInParent<HeightLift>() != null);
                float h0 = TerrainHeight.At(transform.position), z = transform.position.z;
                baseZ = h0 > 0.5f && z < -h0 * 0.6f ? z + h0 : z;      // made at the lifted height already
            }
            if (skip) return;
            var p = transform.position;
            p.z = baseZ - TerrainHeight.At(p);
            transform.position = p;
        }
    }

    // A baked building or prop: 8 views, swapped as the camera turns. Draws with TWF/EnvSprite.
    public class EnvSprite : MonoBehaviour
    {
        public Sprite[] views;
        SpriteRenderer sr;
        int current = -1;

        public void Init(Sprite[] sprites, SpriteRenderer renderer)
        {
            views = sprites;
            sr = renderer;
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (views == null || views.Length == 0) return;
            int n = views.Length;
            int i = ((Mathf.RoundToInt(View.Yaw / (360f / n)) % n) + n) % n;
            if (i == current) return;
            current = i;
            sr.sprite = views[i];
        }
    }

    // Loading the pipeline's output from Resources/TWF/Env.
    public static class EnvArt
    {
        [Serializable]
        class Proxy { public string kind; public float w, d, wall, rise, overhang; public string ridge; }
        [Serializable]
        class Meta { public string name; public int views; public int ppu; public int cols; public int[] frame; public float[] pivot; public float depth_range; public float[] footprint; public float height; public Proxy proxy; }

        static Shader shader;
        static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
        static readonly Dictionary<string, Meta> metas = new Dictionary<string, Meta>();

        static Meta LoadMeta(string name)
        {
            if (metas.TryGetValue(name, out var m)) return m;
            var ta = Resources.Load<TextAsset>("TWF/Env/" + name);
            if (ta == null) { Debug.LogError("EnvArt: missing " + name); return null; }
            m = JsonUtility.FromJson<Meta>(ta.text);
            metas[name] = m;
            return m;
        }

        static Sprite[] LoadViews(string name)
        {
            if (cache.TryGetValue(name, out var s)) return s;
            var m = LoadMeta(name);
            var tex = Resources.Load<Texture2D>("TWF/Env/" + name + "_sheet");
            if (m == null || tex == null) { Debug.LogError("EnvArt: missing sheet " + name); return null; }
            s = new Sprite[m.views];
            var pivot = new Vector2(m.pivot[0] / m.frame[0], m.pivot[1] / m.frame[1]);
            int cols = Mathf.Max(1, m.cols > 0 ? m.cols : m.views);
            int rows = (m.views + cols - 1) / cols;
            for (int i = 0; i < m.views; i++)
            {
                int c = i % cols, r = i / cols;
                s[i] = Sprite.Create(tex, new Rect(c * m.frame[0], (rows - 1 - r) * m.frame[1], m.frame[0], m.frame[1]), pivot, m.ppu, 0, SpriteMeshType.FullRect);
            }
            cache[name] = s;
            return s;
        }

        public static Shader EnvShader => shader != null ? shader : shader = Resources.Load<Shader>("TWF/Env/TWF_EnvSprite");

        static readonly Dictionary<string, Material> spriteMats = new Dictionary<string, Material>();

        // Sprites stand on ground at a known height; the shader keeps them above that plane.
        static Material SpriteMaterial(string art, float height)
        {
            string key = art + "@" + height;
            if (spriteMats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(EnvShader) { name = "EnvSprite " + key };
            m.SetFloat("_BaseZ", -height);
            var glow = Tex(art + "_glow");
            if (glow != null) m.SetTexture("_GlowTex", glow);
            spriteMats[key] = m;
            return m;
        }

        public static Material WallMaterial(Texture2D tex, float baseZ)
        {
            var m = new Material(EnvShader) { name = "EnvWall", mainTexture = tex };
            m.SetFloat("_WallMode", 1f);
            m.SetFloat("_BaseZ", baseZ);
            return m;
        }

        public static Texture2D Tex(string name) => Resources.Load<Texture2D>("TWF/Env/" + name);

        // Paints a stretch of ground from a tile, with a ragged natural edge. inside(x, y) is how far (x, y) lies inside the area.
        public static void PaintGround(Transform parent, Rect rect, string tile, Func<float, float, float> inside, string name, float z = -0.02f)
        {
            const int ppu = 72, T = 288;
            int W = Mathf.RoundToInt(rect.width * ppu), H = Mathf.RoundToInt(rect.height * ppu);
            var src = Tex(tile).GetPixels32();
            var px = new Color32[W * H];
            var rim = new Color32(88, 62, 40, 255);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float wx = rect.xMin + (x + 0.5f) / ppu, wy = rect.yMin + (y + 0.5f) / ppu;
                float d = inside(wx, wy) + (Mathf.PerlinNoise(wx * 0.9f + 3.1f, wy * 0.9f) - 0.5f) * 0.9f + (Mathf.PerlinNoise(wx * 4f, wy * 4f + 9f) - 0.5f) * 0.35f;
                if (d < 0f) continue;
                int u = ((Mathf.RoundToInt(wx * ppu) % T) + T) % T, v = ((Mathf.RoundToInt(wy * ppu) % T) + T) % T;
                px[y * W + x] = d < 0.05f ? rim : src[v * T + u];
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            tex.SetPixels32(px);
            tex.Apply();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { new Vector3(rect.xMin, rect.yMin, z), new Vector3(rect.xMax, rect.yMin, z), new Vector3(rect.xMax, rect.yMax, z), new Vector3(rect.xMin, rect.yMax, z) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(RenderAssets.Instance.litDetail) { mainTexture = tex };
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        // ---------- Ground tiles for the world generator ----------

        static Tile[,] grassTiles, dirtTiles, fringeTiles;

        static Tile[,] CutTiles(Texture2D t)
        {
            if (t == null) return null;
            var o = new Tile[4, 4];
            for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = Sprite.Create(t, new Rect(x * 72, y * 72, 72, 72), new Vector2(0.5f, 0.5f), 72f, 0, SpriteMeshType.FullRect);
                o[x, y] = tile;
            }
            return o;
        }

        static void EnsureTiles()
        {
            if (grassTiles != null) return;
            grassTiles = CutTiles(Tex("grass_tile"));
            dirtTiles = CutTiles(Tex("dirt_tile"));
            fringeTiles = CutTiles(Tex("fringe_tile"));
        }

        // Only the zones the new tiles were painted for. Others keep their old tiles until they have their own.
        static bool Painted(Biome b) => b == Biome.Village || b == Biome.Forest;
        static int Wrap(int v) => ((v % 4) + 4) % 4;

        public static TileBase GroundTile(Biome b, int x, int y)
        {
            if (!Painted(b)) return null;
            EnsureTiles();
            return grassTiles == null ? null : grassTiles[Wrap(x), Wrap(y)];
        }

        public static TileBase PathTile(Biome b, int x, int y)
        {
            if (!Painted(b)) return null;
            EnsureTiles();
            return dirtTiles == null ? null : dirtTiles[Wrap(x), Wrap(y)];
        }

        // mask: which sides are grass, 1 N, 2 E, 4 S, 8 W.
        public static TileBase FringeTile(Biome b, int mask)
        {
            if (!Painted(b)) return null;
            EnsureTiles();
            return fringeTiles == null || mask <= 0 ? null : fringeTiles[mask % 4, 3 - mask / 4];
        }

        // A building or prop standing at p: 8-view sprite, footprint collider, and a hidden box that casts its shadow.
        public static GameObject Place(string name, Transform parent, Vector2 p, float height = 0f)
        {
            var views = LoadViews(name);
            var m = LoadMeta(name);
            if (views == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.x, p.y, -height);
            go.layer = LayerMask.NameToLayer("Walls");

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.AddComponent<Billboard>();
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = SpriteMaterial(name, height);
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            go.AddComponent<EnvSprite>().Init(views, sr);

            if (m.footprint != null && m.footprint.Length == 2 && m.footprint[0] > 0)
            {
                var size = new Vector2(m.footprint[0], m.footprint[1]);
                go.AddComponent<BoxCollider2D>().size = size;
                ContactShadow(go.transform, size);
            }
            ShadowShape(go.transform, m.proxy);
            return go;
        }

        // A tree or bush: a single upright view, swaying like the old ones.
        public static GameObject PlaceTree(string name, Transform parent, Vector2 p, float height = 0f)
        {
            var views = LoadViews(name);
            if (views == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.x, p.y, -height);
            go.layer = LayerMask.NameToLayer("Walls");
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.AddComponent<Billboard>();
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = views[0];
            // The card itself casts nothing: its flat shadow would swing round with the camera.
            RenderAssets.MakeLit(sr, false, RenderAssets.Instance.litWind);
            go.AddComponent<CircleCollider2D>().radius = 0.5f;
            ShadowShape(go.transform, LoadMeta(name).proxy);
            return go;
        }

        static Sprite contactSprite;

        // A soft dark rim on the ground around a footprint, so the building reads as standing on the ground.
        static void ContactShadow(Transform parent, Vector2 size)
        {
            if (contactSprite == null)
            {
                const int N = 64;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "EnvContact" };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Min(Mathf.Min(x + 0.5f, N - x - 0.5f), Mathf.Min(y + 0.5f, N - y - 0.5f)) / (N * 0.5f);
                    float a = Mathf.Clamp01(d * 3f);
                    px[y * N + x] = new Color32(20, 16, 12, (byte)(a * a * 150));
                }
                t.SetPixels32(px);
                t.Apply();
                contactSprite = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            }
            var go = new GameObject("ContactShadow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, Ground.DecalZ);
            float pad = Mathf.Min(1.1f, Mathf.Min(size.x, size.y) * 0.5f);
            go.transform.localScale = new Vector3(size.x + pad, size.y + pad, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = contactSprite;
            sr.sortingOrder = SortOrder.Units - 2;
            RenderAssets.MakeUnlit(sr);
        }

        // Hidden, world-fixed geometry that casts the asset's shadow, so shadows stay put when the camera turns.
        static void ShadowShape(Transform parent, Proxy p)
        {
            if (p == null || string.IsNullOrEmpty(p.kind)) return;
            if (p.kind == "tree")
            {
                Primitive(PrimitiveType.Sphere, parent, new Vector3(0f, 0f, -p.wall), new Vector3(p.w * 1.9f, p.w * 1.9f, p.w * 1.5f));
                Primitive(PrimitiveType.Cylinder, parent, new Vector3(0f, 0f, -p.wall * 0.5f), new Vector3(p.d * 2.2f, p.wall * 0.5f, p.d * 2.2f), Quaternion.Euler(90f, 0f, 0f));
                return;
            }
            if (p.kind == "well")
            {
                Primitive(PrimitiveType.Cylinder, parent, new Vector3(0f, 0f, -0.4f * p.d), new Vector3(p.w * 2f, 0.45f * p.d, p.w * 2f), Quaternion.Euler(90f, 0f, 0f));
                Primitive(PrimitiveType.Cube, parent, new Vector3(0f, 0f, -p.wall + 0.55f), new Vector3(p.w * 2.6f, p.d * 2f, 0.9f));
                return;
            }
            // A house: walls plus a two-sided roof with its overhang.
            Primitive(PrimitiveType.Cube, parent, new Vector3(0f, 0f, -p.wall * 0.5f), new Vector3(p.w, p.d, p.wall));
            bool ridgeX = p.ridge != "y";
            float x = (ridgeX ? p.w : p.d) / 2f + p.overhang, y = (ridgeX ? p.d : p.w) / 2f + p.overhang;
            float z0 = -p.wall, z1 = -p.wall - p.rise;
            var mesh = new Mesh { name = "RoofShadow" };
            mesh.vertices = new[] { new Vector3(-x, -y, z0), new Vector3(x, -y, z0), new Vector3(x, 0f, z1), new Vector3(-x, 0f, z1), new Vector3(-x, y, z0), new Vector3(x, y, z0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 1, 5, 2, 5, 4, 3, 5, 3, 2, 4, 0, 3, 0, 4, 5, 0, 5, 1 };
            mesh.RecalculateNormals();
            var go = new GameObject("RoofShadow");
            go.transform.SetParent(parent, false);
            if (!ridgeX) go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = RenderAssets.Instance.litSprite;
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            r.receiveShadows = false;
        }

        static void Primitive(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = "ShadowShape";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = RenderAssets.Instance.litSprite;
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            r.receiveShadows = false;
        }
    }
}
