using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TWF.EditorTools
{
    // Generates the original 64x64 world art: seamless ground tiles, path fringes, cliff faces,
    // and trees / rocks / props. Output: Assets/Content/Art64 (64 pixels per unit, point filtered).
    public static class ArtGen
    {
        const string Dir = "Assets/Content/Art64";
        const int T = 64;

        static readonly int[,] bayer =
        {
            { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 },
        };

        // ---------- Noise ----------

        // Value noise that wraps every `period` cells across the 64px tile, so tiles are seamless.
        static float PNoise(float x, float y, int period, int seed)
        {
            float fx = x / T * period, fy = y / T * period;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3 - 2 * tx);
            ty = ty * ty * (3 - 2 * ty);
            float v00 = H(Mod(x0, period), Mod(y0, period), seed), v10 = H(Mod(x0 + 1, period), Mod(y0, period), seed);
            float v01 = H(Mod(x0, period), Mod(y0 + 1, period), seed), v11 = H(Mod(x0 + 1, period), Mod(y0 + 1, period), seed);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), ty);
        }

        static float Fbm(float x, float y, int seed) =>
            PNoise(x, y, 4, seed) * 0.5f + PNoise(x, y, 8, seed + 1) * 0.3f + PNoise(x, y, 16, seed + 2) * 0.2f;

        static int Mod(int a, int m) => ((a % m) + m) % m;

        static float H(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        // Pick a palette shade with ordered dithering for a pixel-art look.
        static Color Shade(Color[] pal, float v, int x, int y)
        {
            v = Mathf.Clamp01(v) * (pal.Length - 1);
            int i = Mathf.FloorToInt(v);
            float f = v - i;
            float threshold = (bayer[y & 3, x & 3] + 0.5f) / 16f;
            if (f > threshold && i < pal.Length - 1) i++;
            return pal[i];
        }

        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static Color[] Pal(params string[] hex) => Array.ConvertAll(hex, C);

        // ---------- Entry ----------

        [MenuItem("TWF/Generate 64px Art")]
        public static void Generate()
        {
            Directory.CreateDirectory(Dir);
            var result = ScriptableObject.CreateInstance<ArtSet>();

            // Ground per biome.
            result.forestGround = Tiles("forest_grass", 6, (x, y, v, s) => GrassPixel(x, y, v, s, Pal("#2f4a1f", "#3f6427", "#517d2e", "#669435", "#7fae42"), true));
            result.marshGround = Tiles("marsh_mud", 5, (x, y, v, s) => MudPixel(x, y, v, s));
            result.ruinsGround = Tiles("ruins_flag", 5, (x, y, v, s) => FlagPixel(x, y, v, s));
            result.emberGround = Tiles("ember_ash", 5, (x, y, v, s) => AshPixel(x, y, v, s));
            result.forestPath = Tiles("dirt_path", 5, (x, y, v, s) => DirtPixel(x, y, v, s, Pal("#5a3c24", "#6e4a2c", "#835a36", "#9a6c42", "#ad7f52")));
            result.marshPath = Tiles("marsh_path", 4, (x, y, v, s) => DirtPixel(x, y, v, s, Pal("#3d3626", "#4b432f", "#5b5139", "#6b6044", "#7a6f50")));
            result.ruinsPath = Tiles("ruins_path", 4, (x, y, v, s) => DirtPixel(x, y, v, s, Pal("#6a6560", "#7a746e", "#8a847c", "#9a938a", "#aaa398")));
            result.emberPath = Tiles("ember_path", 4, (x, y, v, s) => DirtPixel(x, y, v, s, Pal("#2e221e", "#3a2c26", "#47362e", "#544037", "#614a40")));

            // Grass fringe overlays that soften path edges (16 by neighbour mask).
            result.forestFringe = Fringes("fringe_forest", Pal("#2f4a1f", "#3f6427", "#517d2e", "#669435", "#7fae42"));
            result.marshFringe = Fringes("fringe_marsh", Pal("#2a3a22", "#35482a", "#425634", "#50663f", "#5f7549"));

            result.cliff = new[] { CliffTex("cliff_forest", Pal("#3b2c22", "#4f3b2c", "#654c38", "#7a5d45", "#8f6f53")),
                CliffTex("cliff_marsh", Pal("#2c3026", "#3a3f31", "#4a503e", "#5b624c", "#6c745a")),
                CliffTex("cliff_ruins", Pal("#4a4744", "#5e5a56", "#736e69", "#88827c", "#9c9690")),
                CliffTex("cliff_ember", Pal("#1e1715", "#2b211d", "#3a2c27", "#4a3831", "#5a453c")) };
            result.brick = BrickTex("brick");

            // Decor.
            result.oak = Sprites("tree_oak", 3, s => Oak(s, Pal("#1e3517", "#2b4a1f", "#3b6428", "#4f7f32", "#679c3f", "#86b84f"), false));
            result.birch = Sprites("tree_birch", 2, s => Oak(s, Pal("#3d4a1c", "#556426", "#6e7f30", "#8b9c3d", "#a8b84c", "#c8d468"), true));
            result.pine = Sprites("tree_pine", 3, s => Pine(s));
            result.willow = Sprites("tree_willow", 2, s => Willow(s));
            result.deadTree = Sprites("tree_dead", 3, s => DeadTree(s, false));
            result.charredTree = Sprites("tree_charred", 3, s => DeadTree(s, true));
            result.mushroomRed = Sprites("mushroom_red", 2, s => Mushroom(s, Pal("#5a1a14", "#8a2a1e", "#b83a28", "#d9553a", "#ef7a55")));
            result.mushroomPurple = Sprites("mushroom_purple", 2, s => Mushroom(s, Pal("#2e1a44", "#452866", "#5e3988", "#7a4ea8", "#9a6ac6")));
            result.rockSmall = Sprites("rock_small", 3, s => Rock(s, 26, 20, false));
            result.rockLarge = Sprites("rock_large", 3, s => Rock(s, 58, 44, false));
            result.rockMossy = Sprites("rock_mossy", 2, s => Rock(s, 44, 34, true));
            result.bush = Sprites("bush", 3, s => Bush(s));
            result.fern = Sprites("fern", 2, s => Fern(s));
            result.pillar = Sprites("ruin_pillar", 2, s => Pillar(s, false));
            result.brokenPillar = Sprites("ruin_pillar_broken", 2, s => Pillar(s, true));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            const string setPath = "Assets/Resources/TWF/ArtSet.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ArtSet>(setPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(result, existing);
                EditorUtility.SetDirty(existing);
            }
            else AssetDatabase.CreateAsset(result, setPath);
            AssetDatabase.SaveAssets();
            Debug.Log("TWF 64px art generated.");
        }

        // ---------- Saving ----------

        delegate Color PixelFn(int x, int y, float v, int seed);

        static TileBase[] Tiles(string name, int variants, PixelFn fn)
        {
            var tiles = new TileBase[variants];
            for (int i = 0; i < variants; i++)
            {
                int seed = name.GetHashCode() * 31 + i * 977;
                var px = new Color[T * T];
                for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                    px[y * T + x] = fn(x, y, Fbm(x, y, seed), seed);
                tiles[i] = SaveTile($"{name}_{i}", px, T, T);
            }
            return tiles;
        }

        static TileBase SaveTile(string name, Color[] px, int w, int h)
        {
            var sprite = SavePng(name, px, w, h, new Vector2(0.5f, 0.5f));
            string path = $"{Dir}/{name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        static Sprite SavePng(string name, Color[] px, int w, int h, Vector2 pivot)
        {
            string png = $"{Dir}/{name}.png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(png, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(png);
            var imp = (TextureImporter)AssetImporter.GetAtPath(png);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = T;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(png);
        }

        static Texture2D SaveTexture(string name, Color[] px)
        {
            string png = $"{Dir}/{name}.png";
            var tex = new Texture2D(T, T, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(png, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(png);
            var imp = (TextureImporter)AssetImporter.GetAtPath(png);
            imp.textureType = TextureImporterType.Default;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(png);
        }

        // ---------- Ground pixels ----------

        static Color GrassPixel(int x, int y, float v, int seed, Color[] pal, bool flowers)
        {
            float blade = H(x, y / 3, seed + 5);
            float n = v * 0.75f + (blade > 0.82f ? 0.35f : 0f) - (H(x, y, seed + 9) > 0.93f ? 0.25f : 0f);
            var c = Shade(pal, n, x, y);
            float f = H(x / 2, y / 2, seed + 13);
            if (flowers && f > 0.992f) c = H(x, y, seed) > 0.5f ? C("#f2efe0") : C("#f0d050");
            if (flowers && f < 0.004f) c = C("#d97aa8");
            return c;
        }

        static Color DirtPixel(int x, int y, float v, int seed, Color[] pal)
        {
            var c = Shade(pal, v * 0.9f + 0.05f, x, y);
            // Pebbles: a light pixel with a shadow pixel below-right.
            if (H(x / 2, y / 2, seed + 21) > 0.975f) c = Color.Lerp(pal[pal.Length - 1], Color.white, 0.25f);
            else if (H((x - 1) / 2, (y + 1) / 2, seed + 21) > 0.975f) c = pal[0];
            return c;
        }

        static Color MudPixel(int x, int y, float v, int seed)
        {
            var mud = Pal("#2b2a1c", "#363423", "#42402b", "#4e4c33", "#5c5a3e");
            var water = Pal("#1d2f2c", "#24403a", "#2e5148", "#3c6658");
            float puddle = PNoise(x, y, 4, seed + 31);
            if (puddle > 0.68f) return Shade(water, (puddle - 0.68f) * 3f + (H(x, y / 2, seed) > 0.95f ? 0.6f : 0f), x, y);
            var c = Shade(mud, v, x, y);
            if (H(x, y, seed + 7) > 0.985f) c = C("#5e6e3a"); // reeds / moss specks
            return c;
        }

        static Color FlagPixel(int x, int y, float v, int seed)
        {
            var stone = Pal("#4c4945", "#5d5955", "#6e6a65", "#7f7a74", "#8f8a83");
            // Stones on a 16px grid with offset rows; seams are dark, edges lit top-left.
            int row = y / 16;
            int ox = (row % 2 == 0) ? 0 : 8;
            int lx = Mod(x + ox, 16), ly = y % 16;
            int cellX = (x + ox) / 16, cellY = row;
            float cellTone = H(Mod(cellX, 4), Mod(cellY, 4), seed) * 0.35f;
            if (lx == 0 || ly == 0) return C("#2e2c2a");
            float edge = (ly == 15 || lx == 15) ? -0.25f : (ly == 1 || lx == 1) ? 0.2f : 0f;
            var c = Shade(stone, v * 0.5f + cellTone + 0.2f + edge, x, y);
            if (PNoise(x, y, 8, seed + 3) > 0.72f && H(x, y, seed + 4) > 0.5f) c = Color.Lerp(c, C("#4e6a3a"), 0.6f); // moss
            if (H(x / 2, y, seed + 6) > 0.993f) c = C("#2e2c2a"); // cracks
            return c;
        }

        static Color AshPixel(int x, int y, float v, int seed)
        {
            var ash = Pal("#1c1614", "#26201d", "#312925", "#3d332e", "#4a3e37");
            var c = Shade(ash, v, x, y);
            float crack = Mathf.Abs(PNoise(x, y, 8, seed + 41) - 0.5f);
            if (crack < 0.02f) c = Color.Lerp(C("#ff7a2a"), C("#ffd060"), H(x, y, seed) * 0.6f);
            else if (crack < 0.035f) c = C("#5a2a18");
            if (H(x, y, seed + 2) > 0.995f) c = C("#ffb040");
            return c;
        }

        // ---------- Fringes ----------

        // mask bits: 1 = grass to the north, 2 = east, 4 = south, 8 = west.
        static TileBase[] Fringes(string name, Color[] pal)
        {
            var tiles = new TileBase[16];
            for (int mask = 0; mask < 16; mask++)
            {
                int seed = name.GetHashCode() + mask * 13;
                var px = new Color[T * T];
                for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    float d = 99f;
                    if ((mask & 1) != 0) d = Mathf.Min(d, T - 1 - y);
                    if ((mask & 4) != 0) d = Mathf.Min(d, y);
                    if ((mask & 2) != 0) d = Mathf.Min(d, T - 1 - x);
                    if ((mask & 8) != 0) d = Mathf.Min(d, x);
                    // Ragged edge: grass reaches 4-14px into the path.
                    float reach = 5f + PNoise(x, y, 8, seed) * 10f;
                    px[y * T + x] = d < reach ? GrassPixel(x, y, Fbm(x, y, seed), seed, pal, false) : Color.clear;
                }
                tiles[mask] = mask == 0 ? null : SaveTile($"{name}_{mask}", px, T, T);
            }
            return tiles;
        }

        // ---------- Textures for meshes ----------

        static Texture2D CliffTex(string name, Color[] pal)
        {
            int seed = name.GetHashCode();
            var px = new Color[T * T];
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float strata = PNoise(x * 0.3f, y * 2f, 8, seed) * 0.6f + Fbm(x, y, seed + 1) * 0.4f;
                float crack = Mathf.Abs(PNoise(x, y * 0.5f, 8, seed + 2) - 0.5f);
                var c = Shade(pal, strata, x, y);
                if (crack < 0.025f) c = pal[0];
                if (y % 21 == 0) c = Color.Lerp(c, pal[0], 0.5f);
                px[y * T + x] = c;
            }
            return SaveTexture(name, px);
        }

        static Texture2D BrickTex(string name)
        {
            var stone = Pal("#4e4a46", "#605b56", "#736d67", "#867f78", "#968f87");
            var px = new Color[T * T];
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                int row = y / 12, ox = row % 2 == 0 ? 0 : 14;
                bool mortar = y % 12 == 0 || Mod(x + ox, 28) == 0;
                var c = mortar ? C("#2a2725") : Shade(stone, Fbm(x, y, 7) * 0.6f + H((x + ox) / 28, row, 3) * 0.4f, x, y);
                if (!mortar && y % 12 == 11) c = Color.Lerp(c, Color.black, 0.2f);
                if (!mortar && PNoise(x, y, 8, 9) > 0.75f) c = Color.Lerp(c, C("#4e6a3a"), 0.5f);
                px[y * T + x] = c;
            }
            return SaveTexture(name, px);
        }

        // ---------- Decor ----------

        class Canvas
        {
            public int w, h;
            public Color[] px;

            public Canvas(int w, int h)
            {
                this.w = w;
                this.h = h;
                px = new Color[w * h];
            }

            public void Set(int x, int y, Color c)
            {
                if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c;
            }

            public bool Filled(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && px[y * w + x].a > 0.5f;

            public void Outline(Color c)
            {
                var copy = (Color[])px.Clone();
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (Filled(x, y)) continue;
                    if (Filled(x + 1, y) || Filled(x - 1, y) || Filled(x, y + 1) || Filled(x, y - 1)) copy[y * w + x] = c;
                }
                px = copy;
            }
        }

        delegate Canvas DecorFn(int seed);

        static Sprite[] Sprites(string name, int variants, DecorFn fn)
        {
            var list = new Sprite[variants];
            for (int i = 0; i < variants; i++)
            {
                var c = fn(name.GetHashCode() + i * 7919);
                c.Outline(C("#120e0c"));
                list[i] = SavePng($"{name}_{i}", c.px, c.w, c.h, new Vector2(0.5f, 0.04f));
            }
            return list;
        }

        // Trunk with bark, then a canopy built from overlapping blobs, lit from the top-left.
        static Canvas Oak(int seed, Color[] leaves, bool birch)
        {
            var rng = new System.Random(seed);
            int w = 176, h = 240;
            var c = new Canvas(w, h);
            var bark = birch ? Pal("#9a948a", "#c9c2b5", "#e8e2d4") : Pal("#3a2618", "#4f3420", "#6a4529");
            int trunkW = birch ? 10 : 16 + rng.Next(6);
            int trunkTop = (birch ? 70 : 52) + rng.Next(16);
            for (int y = 4; y < trunkTop; y++)
            {
                int spread = y < 18 ? (18 - y) / 3 : 0; // root flare
                for (int x = w / 2 - trunkW / 2 - spread; x < w / 2 + trunkW / 2 + spread; x++)
                {
                    float shade = (x - (w / 2 - trunkW / 2f)) / trunkW; // light on the left
                    int idx = shade < 0.35f ? 2 : shade < 0.75f ? 1 : 0;
                    var col = bark[idx];
                    if (birch && H(x / 3, y / 2, seed) > 0.85f) col = C("#2a2622");
                    if (!birch && H(x, y / 4, seed) > 0.8f) col = bark[0];
                    c.Set(x, y, col);
                }
            }
            // Canopy blobs.
            var blobs = new List<Vector3>();
            int count = 7 + rng.Next(4);
            for (int i = 0; i < count; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI;
                float rad = (float)rng.NextDouble() * 40f;
                blobs.Add(new Vector3(w / 2 + Mathf.Cos(ang) * rad * 1.5f, trunkTop + 34 + Mathf.Sin(ang) * rad * 1.1f, 28 + rng.Next(20)));
            }
            blobs.Add(new Vector3(w / 2, trunkTop + 62, 46));
            Vector2 light = new Vector2(-0.6f, 0.8f).normalized;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float best = -1f;
                Vector2 normal = Vector2.zero;
                foreach (var b in blobs)
                {
                    float dx = x - b.x, dy = y - b.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = b.z + (H(x / 3, y / 3, seed + 3) - 0.5f) * 6f;
                    if (d < edge && edge - d > best)
                    {
                        best = edge - d;
                        normal = new Vector2(dx, dy) / Mathf.Max(1f, b.z);
                    }
                }
                if (best < 0f) continue;
                float lit = Vector2.Dot(normal, light) * 0.5f + 0.5f;
                float leafNoise = H(x / 2, y / 2, seed + 11) * 0.2f;
                // Leaf clumps: darker pockets between lit clusters.
                float clump = H(x / 7, y / 6, seed + 17);
                float v = lit * 0.75f + leafNoise + Mathf.Clamp01(best / 30f) * 0.1f - (clump > 0.72f ? 0.22f : 0f) - 0.05f;
                c.Set(x, y, Shade(leaves, v, x, y));
            }
            return c;
        }

        static Canvas Pine(int seed)
        {
            var rng = new System.Random(seed);
            var leaves = Pal("#10261c", "#183527", "#214633", "#2c5a40", "#3a704f", "#4c8760");
            int w = 128, h = 256;
            var c = new Canvas(w, h);
            for (int y = 4; y < 60; y++)
                for (int x = w / 2 - 6; x < w / 2 + 6; x++)
                    c.Set(x, y, x < w / 2 - 2 ? C("#5a3a22") : C("#3e2716"));
            int tiers = 5 + rng.Next(2);
            for (int t = 0; t < tiers; t++)
            {
                float baseY = 40 + t * 34f;
                float halfW = 58f - t * 9f;
                float height = 60f;
                for (int y = 0; y < (int)height; y++)
                {
                    float k = y / height;
                    float hw = halfW * (1f - k) + (H(t, y / 3, seed) - 0.5f) * 4f;
                    for (int x = (int)(w / 2 - hw); x < (int)(w / 2 + hw); x++)
                    {
                        float side = (x - (w / 2f - hw)) / (2 * hw);
                        float v = (1f - side) * 0.55f + (1f - k) * 0.1f + H(x / 2, y / 2 + t * 30, seed) * 0.2f + (y < 4 ? -0.2f : 0f);
                        c.Set(x, (int)baseY + y, Shade(leaves, v + 0.1f, x, y));
                    }
                }
            }
            return c;
        }

        static Canvas Willow(int seed)
        {
            var c = Oak(seed, Pal("#1e2e1a", "#2b4024", "#3a5530", "#4b6b3c", "#5f8249", "#78995a"), false);
            var rng = new System.Random(seed + 5);
            var strand = Pal("#2b4024", "#3a5530", "#4b6b3c");
            // Drooping strands hanging from the canopy.
            for (int i = 0; i < 40; i++)
            {
                int x = 24 + rng.Next(c.w - 48);
                int top = 0;
                for (int y = c.h - 1; y > 0; y--) if (c.Filled(x, y)) { top = y; break; }
                int startY = top - 30 - rng.Next(30);
                int len = 30 + rng.Next(50);
                for (int k = 0; k < len; k++)
                {
                    int y = startY - k;
                    int sx = x + Mathf.RoundToInt(Mathf.Sin(k * 0.15f + i) * 2f);
                    if (y > 10) c.Set(sx, y, strand[k % 3 == 0 ? 0 : 1]);
                }
            }
            return c;
        }

        static Canvas DeadTree(int seed, bool charred)
        {
            var rng = new System.Random(seed);
            int w = 200, h = 230;
            var c = new Canvas(w, h);
            var wood = charred ? Pal("#0e0b0a", "#1b1614", "#2a221e") : Pal("#3a3028", "#54473b", "#6e5e4f");
            void Branch(float x, float y, float ang, float len, float thick, int depth)
            {
                if (depth > 5 || thick < 1f) return;
                float ex = x + Mathf.Cos(ang) * len, ey = y + Mathf.Sin(ang) * len;
                int steps = Mathf.CeilToInt(len);
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    float px = Mathf.Lerp(x, ex, t), py = Mathf.Lerp(y, ey, t);
                    float th = Mathf.Lerp(thick, thick * 0.7f, t);
                    for (int dx = -(int)th; dx <= (int)th; dx++)
                    {
                        int idx = dx < -th * 0.3f ? 2 : dx > th * 0.3f ? 0 : 1;
                        var col = wood[idx];
                        if (charred && H((int)px + dx, (int)py / 3, seed) > 0.93f) col = C("#ff7a2a");
                        c.Set((int)px + dx, (int)py, col);
                    }
                }
                int kids = 2 + rng.Next(2);
                for (int k = 0; k < kids; k++)
                {
                    // Branches spread out but keep growing upward, inside the canvas.
                    float na = Mathf.Clamp(ang + ((float)rng.NextDouble() - 0.5f) * 1.3f, Mathf.PI * 0.18f, Mathf.PI * 0.82f);
                    Branch(ex, ey, na, len * (0.58f + (float)rng.NextDouble() * 0.18f), thick * 0.62f, depth + 1);
                }
            }
            Branch(w / 2, 4, Mathf.PI / 2 + ((float)rng.NextDouble() - 0.5f) * 0.15f, 58 + rng.Next(14), 8, 0);
            return c;
        }

        static Canvas Mushroom(int seed, Color[] cap)
        {
            var rng = new System.Random(seed);
            int w = 128, h = 120;
            var c = new Canvas(w, h);
            int stalkH = 42 + rng.Next(16);
            for (int y = 4; y < stalkH; y++)
                for (int x = w / 2 - 9; x < w / 2 + 9; x++)
                    c.Set(x, y, x < w / 2 - 3 ? C("#e8dfc9") : x < w / 2 + 4 ? C("#cfc4ab") : C("#a89c84"));
            float rx = 52 + rng.Next(8), ry = 34 + rng.Next(6);
            Vector2 light = new Vector2(-0.6f, 0.8f).normalized;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x - w / 2f) / rx, dy = (y - stalkH) / ry;
                if (dy < 0 || dx * dx + dy * dy > 1f) continue;
                float lit = Vector2.Dot(new Vector2(dx, dy), light) * 0.5f + 0.5f;
                var col = Shade(cap, lit * 0.8f + 0.15f, x, y);
                if (H(x / 6, y / 6, seed) > 0.8f && dy > 0.2f) col = C("#f4ecd8"); // spots
                c.Set(x, y, col);
            }
            return c;
        }

        static Canvas Rock(int seed, int rw, int rh, bool mossy)
        {
            var stone = Pal("#3e3b38", "#55514c", "#6c6762", "#857f78", "#a09991");
            int w = rw + 12, h = rh + 12;
            var c = new Canvas(w, h);
            Vector2 light = new Vector2(-0.6f, 0.8f).normalized;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x - w / 2f) / (rw / 2f), dy = (y - rh * 0.45f) / (rh / 2f);
                float wobble = (H(Mathf.RoundToInt(Mathf.Atan2(dy, dx) * 5f), 0, seed) - 0.5f) * 0.25f;
                if (y < 3 || dx * dx + dy * dy > 1f + wobble) continue;
                float lit = Vector2.Dot(new Vector2(dx, dy), light) * 0.5f + 0.5f;
                var col = Shade(stone, lit * 0.85f + H(x / 2, y / 2, seed) * 0.15f, x, y);
                if (mossy && dy > 0.2f && H(x / 2, y / 2, seed + 1) > 0.35f) col = Color.Lerp(col, C("#4f7f32"), 0.7f);
                c.Set(x, y, col);
            }
            return c;
        }

        static Canvas Bush(int seed)
        {
            var big = Oak(seed, Pal("#1e3517", "#2b4a1f", "#3b6428", "#4f7f32", "#679c3f", "#86b84f"), false);
            // Keep only the canopy, shrunk into a low bush shape.
            int w = 72, h = 52;
            var c = new Canvas(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = Mathf.RoundToInt(x * big.w / (float)w), sy = 100 + Mathf.RoundToInt(y * 1.6f);
                if (sx >= 0 && sy >= 0 && sx < big.w && sy < big.h && big.Filled(sx, sy) && big.px[sy * big.w + sx].g > 0.12f)
                    c.Set(x, y, big.px[sy * big.w + sx]);
            }
            return c;
        }

        static Canvas Fern(int seed)
        {
            var rng = new System.Random(seed);
            int w = 64, h = 48;
            var c = new Canvas(w, h);
            var green = Pal("#2b4a1f", "#3b6428", "#517d2e", "#669435");
            for (int f = 0; f < 6; f++)
            {
                float ang = Mathf.PI * (0.15f + 0.7f * f / 5f);
                for (int k = 0; k < 30; k++)
                {
                    float t = k / 30f;
                    float x = w / 2 + Mathf.Cos(ang) * k * 1.1f, y = 2 + Mathf.Sin(ang) * k * 1.2f - t * t * 10f;
                    c.Set((int)x, (int)y, green[2]);
                    if (k % 3 == 0) { c.Set((int)x + 1, (int)y + 1, green[3]); c.Set((int)x - 1, (int)y + 1, green[1]); }
                }
            }
            return c;
        }

        static Canvas Pillar(int seed, bool broken)
        {
            var rng = new System.Random(seed);
            var stone = Pal("#4e4a46", "#605b56", "#736d67", "#867f78", "#9a938a");
            int w = 64, h = broken ? 120 + rng.Next(40) : 220;
            var c = new Canvas(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 8; x < w - 8; x++)
            {
                bool cap = y < 14 || (!broken && y > h - 16);
                int half = cap ? 24 : 18;
                if (Mathf.Abs(x - w / 2) > half) continue;
                if (broken && y > h - 24 && H(x / 3, y, seed) > 0.5f + (h - y) / 48f) continue;
                float lit = 1f - (x - (w / 2f - half)) / (2f * half);
                var col = Shade(stone, lit * 0.7f + H(x / 2, y / 2, seed) * 0.2f, x, y);
                if (!cap && Mod(x - w / 2, 8) == 0) col = Color.Lerp(col, stone[0], 0.5f); // fluting
                if (y % 40 == 0) col = stone[0];
                if (PNoise(x, y, 4, seed + 5) > 0.82f) col = Color.Lerp(col, C("#4e6a3a"), 0.45f);
                c.Set(x, y, col);
            }
            return c;
        }
    }
}
