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

        public static float At(Vector2 p)
        {
            float best = 0f;
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
        readonly List<(Transform t, float z)> parts = new List<(Transform, float)>();
        float height;
        CameraFollow cam;

        void Start()
        {
            foreach (Transform c in transform) parts.Add((c, c.localPosition.z));
            if (GetComponent<HeroController>() != null && Camera.main != null) cam = Camera.main.GetComponent<CameraFollow>();
        }

        void LateUpdate()
        {
            float target = TerrainHeight.At(transform.position);
            height = Mathf.MoveTowards(height, target, RiseSpeed * Time.deltaTime);
            foreach (var (t, z) in parts)
            {
                if (t == null) continue;
                var p = t.localPosition;
                p.z = z - height;
                t.localPosition = p;
            }
            if (cam != null) cam.focusHeight = height;
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

        static readonly Dictionary<float, Material> spriteMats = new Dictionary<float, Material>();

        // Sprites stand on ground at a known height; the shader keeps them above that plane.
        static Material SpriteMaterial(float height)
        {
            if (spriteMats.TryGetValue(height, out var m) && m != null) return m;
            m = new Material(EnvShader) { name = "EnvSprite " + height };
            m.SetFloat("_BaseZ", -height);
            spriteMats[height] = m;
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
            sr.sharedMaterial = SpriteMaterial(height);
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            go.AddComponent<EnvSprite>().Init(views, sr);

            if (m.footprint != null && m.footprint.Length == 2 && m.footprint[0] > 0)
            {
                var size = new Vector2(m.footprint[0], m.footprint[1]);
                go.AddComponent<BoxCollider2D>().size = size;
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
