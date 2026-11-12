using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TWF
{
    // Tiles and decoration sprites used by the infinite world. Built by the WorldBuilder.
    public class WorldTiles : ScriptableObject
    {
        static WorldTiles instance;

        public static WorldTiles Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<WorldTiles>("TWF/WorldTiles");
                return instance;
            }
        }

        public TileBase[] grassPlain;
        public TileBase[] grassDetail;
        public TileBase[] dirt;
        public TileBase[] ruinsFloor;
        public TileBase ruinsPath;
        public TileBase[] ash;
        public TileBase emberPath;
        public TileBase[] emberCrack;
        public TileBase ruinsRubble;
        public TileBase blocker;

        public Sprite treeOrange, treePink, treeDried, trunk, rock, rockMonument, bush, bushTall;

        // ---------- Generated textures for meshes and grass ----------

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        public static Texture2D CliffTexture(Biome b)
        {
            string key = "cliff" + b;
            if (textures.TryGetValue(key, out var t) && t) return t;
            Color baseC = b == Biome.Ember ? new Color(0.32f, 0.2f, 0.18f)
                : b == Biome.Ruins ? new Color(0.52f, 0.5f, 0.48f)
                : b == Biome.Marsh ? new Color(0.36f, 0.38f, 0.3f)
                : new Color(0.55f, 0.4f, 0.26f);
            var rng = new System.Random(7 + (int)b);
            t = GameAssets.NewTexture(16, 16);
            t.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                // Horizontal rock strata with cracks.
                float strata = ((y + (x / 5) % 2) % 5 == 0) ? 0.7f : 1f;
                float v = 0.85f + (float)rng.NextDouble() * 0.2f;
                var c = baseC * strata * v;
                if (rng.NextDouble() < 0.04) c *= 0.6f;
                if (b == Biome.Ember && rng.NextDouble() < 0.025) c = new Color(1f, 0.45f, 0.1f);
                c.a = 1f;
                t.SetPixel(x, y, c);
            }
            t.Apply();
            textures[key] = t;
            return t;
        }

        public static Texture2D BrickTexture()
        {
            if (textures.TryGetValue("brick", out var t) && t) return t;
            var rng = new System.Random(3);
            t = GameAssets.NewTexture(16, 16);
            t.wrapMode = TextureWrapMode.Repeat;
            var stone = new Color(0.58f, 0.56f, 0.52f);
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int offset = (y / 4) % 2 == 0 ? 0 : 4;
                bool mortar = y % 4 == 0 || (x + offset) % 8 == 0;
                var c = mortar ? new Color(0.25f, 0.23f, 0.22f) : stone * (0.9f + (float)rng.NextDouble() * 0.15f);
                if (!mortar && rng.NextDouble() < 0.05) c = new Color(0.36f, 0.45f, 0.3f);
                c.a = 1f;
                t.SetPixel(x, y, c);
            }
            t.Apply();
            textures["brick"] = t;
            return t;
        }

        public static Texture2D GrassTexture(Biome b)
        {
            string key = "grass" + b;
            if (textures.TryGetValue(key, out var t) && t) return t;
            Color light = b == Biome.Marsh ? new Color(0.45f, 0.55f, 0.32f) : b == Biome.Ember ? new Color(0.45f, 0.3f, 0.2f) : new Color(0.5f, 0.72f, 0.28f);
            Color dark = light * 0.6f;
            var rng = new System.Random(11 + (int)b);
            t = GameAssets.NewTexture(16, 16);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[256];
            for (int blade = 0; blade < 7; blade++)
            {
                int x = 2 + rng.Next(12);
                int h = 6 + rng.Next(9);
                float lean = ((float)rng.NextDouble() - 0.5f) * 0.6f;
                for (int y = 0; y < h; y++)
                {
                    int bx = Mathf.Clamp(Mathf.RoundToInt(x + lean * y), 0, 15);
                    px[y * 16 + bx] = Color.Lerp(dark, light, y / (float)h);
                }
            }
            t.SetPixels(px);
            t.Apply();
            textures[key] = t;
            return t;
        }

        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        // Castle stone: large pale blocks, darker mortar, a little moss low down.
        public static Texture2D CastleTexture()
        {
            if (textures.TryGetValue("castle", out var t) && t) return t;
            var rng = new System.Random(19);
            t = GameAssets.NewTexture(16, 16);
            t.wrapMode = TextureWrapMode.Repeat;
            var stone = new Color(0.66f, 0.63f, 0.58f);
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int offset = (y / 5) % 2 == 0 ? 0 : 5;
                bool mortar = y % 5 == 0 || (x + offset) % 10 == 0;
                var c = mortar ? new Color(0.3f, 0.28f, 0.27f) : stone * (0.88f + (float)rng.NextDouble() * 0.14f);
                if (!mortar && y < 5 && rng.NextDouble() < 0.12) c = new Color(0.38f, 0.47f, 0.3f);
                c.a = 1f;
                t.SetPixel(x, y, c);
            }
            t.Apply();
            textures["castle"] = t;
            return t;
        }

        // A top-edge lip: the outer row dark ink, the rest a light, warm tone of the biome's stone.
        public static Texture2D RimTexture(Biome b)
        {
            string key = "rim" + b;
            if (textures.TryGetValue(key, out var t) && t) return t;
            Color lit = b == Biome.Ember ? new Color(0.72f, 0.5f, 0.42f)
                : b == Biome.Marsh ? new Color(0.7f, 0.76f, 0.6f)
                : b == Biome.Ruins ? new Color(0.88f, 0.86f, 0.8f)
                : new Color(0.96f, 0.86f, 0.62f);
            t = GameAssets.NewTexture(4, 8);
            t.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 4; x++)
                t.SetPixel(x, y, y < 2 ? new Color(0.12f, 0.09f, 0.1f, 1f) : y < 4 ? lit : lit * 0.9f + new Color(0, 0, 0, 0.1f));
            t.Apply();
            textures[key] = t;
            return t;
        }

        // The shadow at a wall's foot as an ordered dither (cut-out, no blending): solid at the wall, fading out.
        public static Texture2D FootShadowTexture()
        {
            if (textures.TryGetValue("footshadow", out var t) && t) return t;
            t = GameAssets.NewTexture(32, 12);
            t.wrapMode = TextureWrapMode.Repeat;
            int[,] bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
            for (int y = 0; y < 12; y++)
            for (int x = 0; x < 32; x++)
            {
                float density = 0.95f - y / 11f * 0.9f;                  // how much of the row is shadow
                bool on = bayer[y % 4, x % 4] / 16f < density;
                t.SetPixel(x, y, on ? new Color(0.06f, 0.05f, 0.08f, 1f) : new Color(0, 0, 0, 0));
            }
            t.Apply();
            textures["footshadow"] = t;
            return t;
        }

        // Like MeshMaterial but lit as flat ground (no mesh normals, so it never takes part in the see-through cut).
        public static Material FlatMaterial(string key, Texture2D tex)
        {
            if (materials.TryGetValue(key, out var m) && m) return m;
            m = new Material(RenderAssets.Instance.litSprite);
            m.mainTexture = tex;
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_MeshNormals", 0f);
            materials[key] = m;
            return m;
        }

        public static Material MeshMaterial(string key, Texture2D tex)
        {
            if (materials.TryGetValue(key, out var m) && m) return m;
            var ra = RenderAssets.Instance;
            m = new Material(ra.litSprite);
            m.mainTexture = tex;
            m.SetFloat("_Cutoff", 0f);
            m.SetFloat("_MeshNormals", 1f);
            materials[key] = m;
            return m;
        }

        public static Material GrassMaterial(Biome b)
        {
            string key = "grassmat" + b;
            if (materials.TryGetValue(key, out var m) && m) return m;
            m = new Material(RenderAssets.Instance.litWind);
            m.mainTexture = GrassTexture(b);
            m.enableInstancing = true;
            materials[key] = m;
            return m;
        }
    }
}
