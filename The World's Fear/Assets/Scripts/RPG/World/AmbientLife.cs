using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    // Fireflies, butterflies, embers, ash and marsh mist. Each chunk gets one static mesh of
    // quads (one per critter) drawn with TWF/Ambient, which animates them on the GPU.
    // The mesh lives under the chunk root, so it streams in and out with the chunk.
    public static class AmbientLife
    {
        const int Firefly = 0, Butterfly = 1, Ember = 2, Ash = 3, Mist = 4, Beam = 5, Leaf = 6, Mote = 7;

        static Material material;
        static Texture2D atlas;

        static readonly List<Vector3> verts = new List<Vector3>();
        static readonly List<Vector2> corners = new List<Vector2>();
        static readonly List<Vector4> data = new List<Vector4>();
        static readonly List<Color> colors = new List<Color>();
        static readonly List<int> tris = new List<int>();

        static readonly Color FireflyForest = new Color(0.8f, 1f, 0.35f);
        static readonly Color FireflyMarsh = new Color(0.45f, 1f, 0.8f);
        static readonly Color EmberColor = new Color(1f, 0.5f, 0.12f);
        static readonly Color AshColor = new Color(0.55f, 0.52f, 0.5f);
        static readonly Color MistColor = new Color(0.75f, 0.85f, 0.8f, 0.55f);
        static readonly Color SunColor = new Color(1f, 0.92f, 0.7f); // tinted further by the biome's sun
        static readonly Color[] LeafColors =
        {
            new Color(0.95f, 0.55f, 0.18f), new Color(0.85f, 0.32f, 0.15f), new Color(0.98f, 0.8f, 0.3f), new Color(0.5f, 0.72f, 0.25f),
        };

        // cells has a 1-cell border, as built by ChunkWorld.
        public static void Build(Transform parent, Cell[,] cells, int x0, int y0, Vector2Int c)
        {
            var mat = Material;
            if (mat == null) return;
            int S = WorldGen.ChunkSize;
            var rng = new System.Random((int)WorldGen.Hash(c.x * 7 + 101, c.y * 13 - 37));
            Clear();

            Biome main = WorldGen.BiomeAt(new Vector2(x0 + S / 2f, y0 + S / 2f));

            // Fireflies: thickest over marsh water and in the forest's tree patches.
            int fireflies = main == Biome.Marsh ? 22 : main == Biome.Forest ? 14 : main == Biome.Village ? 5 : main == Biome.Ruins ? 3 : 0;
            for (int i = 0; i < fireflies; i++)
            {
                int x = rng.Next(S), y = rng.Next(S);
                var cell = cells[x + 1, y + 1];
                if (cell == Cell.Plateau || cell == Cell.Wall || cell == Cell.Lava) continue;
                var p = new Vector2(x0 + x + (float)rng.NextDouble(), y0 + y + (float)rng.NextDouble());
                var b = WorldGen.BiomeAt(p);
                if (b == Biome.Forest && Mathf.PerlinNoise(p.x * 0.06f + 300f, p.y * 0.06f + 300f) < 0.42f && rng.NextDouble() < 0.6) continue;
                Add(new Vector3(p.x, p.y, 0f), Firefly, (float)rng.NextDouble(), 0.5f, b == Biome.Marsh ? FireflyMarsh : FireflyForest);
            }

            // Butterflies over the grass of the forest and the village.
            int butterflies = main == Biome.Village ? rng.Next(3, 7) : main == Biome.Forest ? rng.Next(1, 5) : 0;
            for (int i = 0; i < butterflies; i++)
            {
                int x = rng.Next(S), y = rng.Next(S);
                var cell = cells[x + 1, y + 1];
                if (cell != Cell.Ground && cell != Cell.Hub && cell != Cell.Path) continue;
                Add(new Vector3(x0 + x + 0.5f, y0 + y + 0.5f, 0f), Butterfly, (float)rng.NextDouble(), 0.6f, Color.white);
            }

            if (main == Biome.Ember)
            {
                // Embers rise from the lava, a few drift up from the ash everywhere; ash snows down.
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    if (cells[x + 1, y + 1] != Cell.Lava || rng.NextDouble() > 0.4) continue;
                    Add(new Vector3(x0 + x + (float)rng.NextDouble(), y0 + y + (float)rng.NextDouble(), 0.1f), Ember, (float)rng.NextDouble(), 0.38f, EmberColor);
                }
                for (int i = 0; i < 20; i++)
                    Add(new Vector3(x0 + (float)rng.NextDouble() * S, y0 + (float)rng.NextDouble() * S, 0f), Ember, (float)rng.NextDouble(), 0.32f, EmberColor);
                for (int i = 0; i < 26; i++)
                    Add(new Vector3(x0 + (float)rng.NextDouble() * S, y0 + (float)rng.NextDouble() * S, 0f), Ash, (float)rng.NextDouble(), 0.12f, AshColor);
            }

            // Shafts of sunlight with dust glinting in them, mostly through the forest's tree patches.
            int beams = main == Biome.Forest ? 7 : main == Biome.Village ? 5 : main == Biome.Ruins ? 4 : main == Biome.Marsh ? 2 : 0;
            for (int i = 0; i < beams; i++)
            {
                int x = rng.Next(S), y = rng.Next(S);
                var cell = cells[x + 1, y + 1];
                if (cell == Cell.Plateau || cell == Cell.Wall || cell == Cell.Water || cell == Cell.Lava) continue;
                var p = new Vector2(x0 + x + 0.5f, y0 + y + 0.5f);
                if (main == Biome.Forest && Mathf.PerlinNoise(p.x * 0.06f + 300f, p.y * 0.06f + 300f) < 0.45f) continue;
                float seed = (float)rng.NextDouble();
                Add(new Vector3(p.x, p.y, 0f), Beam, seed, 1.8f + (float)rng.NextDouble() * 1.6f, SunColor, 6f);
                for (int k = 0; k < 6; k++)
                    Add(new Vector3(p.x, p.y, 0f), Mote, (float)rng.NextDouble(), 0.16f, SunColor, (float)rng.NextDouble());
            }

            // Leaves drifting down from the forest canopy.
            if (main == Biome.Forest)
                for (int i = 0; i < 10; i++)
                {
                    var p = new Vector2(x0 + (float)rng.NextDouble() * S, y0 + (float)rng.NextDouble() * S);
                    if (Mathf.PerlinNoise(p.x * 0.06f + 300f, p.y * 0.06f + 300f) < 0.42f) continue;
                    Add(new Vector3(p.x, p.y, 0f), Leaf, (float)rng.NextDouble(), 0.2f, LeafColors[rng.Next(LeafColors.Length)]);
                }

            // Low mist curling over marsh water.
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                if (cells[x + 1, y + 1] != Cell.Water || rng.NextDouble() > 0.12) continue;
                Add(new Vector3(x0 + x + 0.5f, y0 + y + 0.5f, 0f), Mist, (float)rng.NextDouble(), 2.6f + (float)rng.NextDouble() * 1.4f, MistColor);
            }

            if (verts.Count == 0) return;
            var mesh = new Mesh { name = "Ambient" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, data);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0, false);
            // Motion is done on the GPU, so the bounds cover everywhere a critter can wander.
            // Sunbeams lean several units toward the sun, hence the wide margin.
            mesh.bounds = new Bounds(new Vector3(x0 + S / 2f, y0 + S / 2f, -2.5f), new Vector3(S + 20f, S + 20f, 8f));

            var go = new GameObject("Ambient");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.AddComponent<AmbientMesh>().mesh = mesh;
        }

        static void Clear()
        {
            verts.Clear(); corners.Clear(); data.Clear(); colors.Clear(); tris.Clear();
        }

        static void Add(Vector3 anchor, int kind, float seed, float size, Color color, float extra = 0f)
        {
            int i = verts.Count;
            for (int k = 0; k < 4; k++)
            {
                verts.Add(anchor);
                data.Add(new Vector4(kind, seed, size, extra));
                colors.Add(color);
            }
            corners.Add(new Vector2(-0.5f, -0.5f));
            corners.Add(new Vector2(0.5f, -0.5f));
            corners.Add(new Vector2(-0.5f, 0.5f));
            corners.Add(new Vector2(0.5f, 0.5f));
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i + 2); tris.Add(i + 3); tris.Add(i + 1);
        }

        static Material Material
        {
            get
            {
                if (material != null) return material;
                var src = Resources.Load<Material>("TWF/Ambient");
                var shader = src != null ? src.shader : Shader.Find("TWF/Ambient");
                if (shader == null) return null;
                material = src != null ? new Material(src) : new Material(shader);
                material.name = "Ambient (runtime)";
                material.mainTexture = Atlas;
                material.SetVector("_Atlas", new Vector4(Variants.Length, 3, 0, 0));
                return material;
            }
        }

        // ---------- Butterfly pixel art ----------

        const int Frame = 12;

        // wing, wing shadow/outline, spot
        static readonly Color[][] Variants =
        {
            new[] { new Color(1f, 0.56f, 0.12f), new Color(0.2f, 0.1f, 0.06f), new Color(1f, 0.92f, 0.75f) },  // monarch
            new[] { new Color(0.96f, 0.96f, 0.9f), new Color(0.45f, 0.45f, 0.5f), new Color(0.25f, 0.25f, 0.3f) }, // cabbage white
            new[] { new Color(0.38f, 0.66f, 1f), new Color(0.1f, 0.16f, 0.4f), new Color(0.85f, 0.95f, 1f) },    // blue
            new[] { new Color(1f, 0.88f, 0.3f), new Color(0.42f, 0.28f, 0.08f), new Color(1f, 0.6f, 0.2f) },     // yellow
        };

        static Texture2D Atlas
        {
            get
            {
                if (atlas != null) return atlas;
                int w = Frame * Variants.Length, h = Frame * 3;
                var px = new Color[w * h];
                for (int v = 0; v < Variants.Length; v++)
                for (int f = 0; f < 3; f++)
                    DrawButterfly(px, w, v * Frame, f * Frame, Variants[v], f);
                atlas = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "ButterflyAtlas",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                atlas.SetPixels(px);
                atlas.Apply(false, true);
                return atlas;
            }
        }

        // Seen from above at a slant: two upper and two lower wings around a dark body.
        // Frame 0 = wings open, 1 = half, 2 = nearly closed.
        static void DrawButterfly(Color[] px, int w, int ox, int oy, Color[] pal, int frame)
        {
            float open = frame == 0 ? 1f : frame == 1 ? 0.55f : 0.2f;
            float cx = 5.5f;
            for (int y = 0; y < Frame; y++)
            for (int x = 0; x < Frame; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Abs(fx - cx);
                // Upper wing: a tilted ellipse, lower wing: a smaller one.
                float uw = Ellipse(dx, fy, 2.6f * open, 7.3f, 2.8f * open + 0.4f, 2.4f);
                float lw = Ellipse(dx, fy, 1.9f * open, 3.9f, 2f * open + 0.3f, 1.7f);
                float wing = Mathf.Max(uw, lw);
                Color c = Color.clear;
                if (wing > 0f)
                {
                    c = wing < 0.28f ? pal[1] : pal[0];
                    // A spot near each upper wing tip, and darker veins toward the body.
                    if (open > 0.5f && uw > 0.28f && Mathf.Abs(dx - 3.6f * open) < 0.8f && Mathf.Abs(fy - 8.2f) < 0.8f) c = pal[2];
                    if (dx < 1.4f && wing > 0.28f) c = Color.Lerp(pal[0], pal[1], 0.45f);
                }
                // Body and antennae.
                if (dx < 0.6f && fy > 2.5f && fy < 9.5f) c = new Color(0.12f, 0.09f, 0.08f);
                if ((Mathf.Abs(fx - (cx - 1.5f)) < 0.6f || Mathf.Abs(fx - (cx + 1.5f)) < 0.6f) && fy > 9.5f && fy < 11f) c = new Color(0.12f, 0.09f, 0.08f);
                // Pixel art: only fully opaque or empty.
                if (c.a > 0f) c.a = 1f;
                px[(oy + y) * w + ox + x] = c;
            }
        }

        // 1 at the centre, 0 at the rim, negative outside. Centre sits out from the body at (cx + ox, oy).
        static float Ellipse(float dx, float y, float ox, float oy, float rx, float ry)
        {
            float ex = (dx - ox) / Mathf.Max(0.01f, rx), ey = (y - oy) / ry;
            return 1f - Mathf.Sqrt(ex * ex + ey * ey);
        }
    }
}
