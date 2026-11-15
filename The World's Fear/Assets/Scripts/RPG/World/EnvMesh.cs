using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    // Buildings and props as real 3D meshes, made by the Blender environment kit (pixelart-gen: env_kit.py --mesh,
    // env_install.py --mesh) into Resources/TWF/Env3D/<name>.bytes. Every piece keeps its own flat colour; a fine
    // pixel grain at the sprites' density (72 px per unit) and banded light make it read as pixel art.
    public static class EnvMesh
    {
        [System.Serializable]
        class Meta { public string name; public float[] footprint; public float height; }

        public class Asset { public Mesh mesh; public Vector2 footprint; public float height; }

        static readonly Dictionary<string, Asset> cache = new Dictionary<string, Asset>();
        static Material body, glass;

        public static bool Has(string name) => Load(name) != null;

        public static Asset Load(string name)
        {
            if (cache.TryGetValue(name, out var a)) return a;
            var bytes = Resources.Load<TextAsset>("TWF/Env3D/" + name);
            if (bytes == null) { cache[name] = null; return null; }
            a = new Asset { mesh = Read(bytes.bytes, name) };
            var metaText = Resources.Load<TextAsset>("TWF/Env3D/" + name + "_meta");
            var meta = metaText != null ? JsonUtility.FromJson<Meta>(metaText.text) : null;
            if (meta != null)
            {
                if (meta.footprint != null && meta.footprint.Length == 2) a.footprint = new Vector2(meta.footprint[0], meta.footprint[1]);
                a.height = meta.height;
            }
            cache[name] = a;
            return a;
        }

        static Mesh Read(byte[] data, string name)
        {
            using var r = new BinaryReader(new MemoryStream(data));
            if (new string(r.ReadChars(4)) != "TWFM") { Debug.LogError("EnvMesh: bad file " + name); return null; }
            int version = r.ReadInt32();
            int nv = r.ReadInt32(), nb = 0, ng = 0;
            if (version == 1) { nb = r.ReadInt32(); ng = r.ReadInt32(); }
            var pos = new Vector3[nv];
            var nrm = new Vector3[nv];
            var col = new Color32[nv];
            var uv = new Vector2[nv];
            for (int i = 0; i < nv; i++)
            {
                pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                col[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
            }
            // Grain coordinates: projected along the face's main axis, 4 units per texture repeat.
            for (int i = 0; i < nv; i++)
            {
                var n = nrm[i]; var p = pos[i];
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                uv[i] = (az >= ax && az >= ay ? new Vector2(p.x, p.y) : ax >= ay ? new Vector2(p.y, p.z) : new Vector2(p.x, p.z)) / 4f;
            }
            // Version 1: flat colours and glass. Version 2: a list per material (flat, glass, leaves, bark, rock).
            var lists = new List<int[]>();
            if (version == 1) { lists.Add(ReadTris(r, nb, pos, nrm)); lists.Add(ReadTris(r, ng, pos, nrm)); }
            else { int n = r.ReadInt32(); for (int k = 0; k < n; k++) lists.Add(ReadTris(r, r.ReadInt32(), pos, nrm)); }
            var m = new Mesh { name = "Env3D " + name, indexFormat = nv > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.vertices = pos; m.normals = nrm; m.colors32 = col; m.uv = uv;
            // Textured faces (leaves, bark, rock) carry rounded normals: their texture is laid out from each face's
            // true angle instead, so it doesn't stretch.
            for (int k = 2; k < lists.Count; k++)
            {
                var t = lists[k];
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 fa = pos[t[i]], fb = pos[t[i + 1]], fc = pos[t[i + 2]];
                    var fn = Vector3.Cross(fb - fa, fc - fa);
                    float ax = Mathf.Abs(fn.x), ay = Mathf.Abs(fn.y), az = Mathf.Abs(fn.z);
                    foreach (int vi in new[] { t[i], t[i + 1], t[i + 2] })
                    {
                        var p = pos[vi];
                        uv[vi] = (az >= ax && az >= ay ? new Vector2(p.x, p.y) : ax >= ay ? new Vector2(p.y, p.z) : new Vector2(p.x, p.z)) / 4f;
                    }
                }
            }
            m.uv = uv;
            m.subMeshCount = lists.Count;
            for (int k = 0; k < lists.Count; k++) m.SetTriangles(lists[k], k);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m;
        }

        // Each triangle is turned so it faces the way its normal points (the export doesn't promise a winding).
        static int[] ReadTris(BinaryReader r, int n, Vector3[] pos, Vector3[] nrm)
        {
            var t = new int[n];
            for (int i = 0; i < n; i++) t[i] = r.ReadInt32();
            for (int i = 0; i + 2 < n; i += 3)
            {
                Vector3 a = pos[t[i]], b = pos[t[i + 1]], c = pos[t[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm[t[i]]) < 0f) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
            }
            return t;
        }

        // A building or prop at p, turned by yaw degrees (counter-clockwise seen from above), with its footprint collider.
        public static GameObject Place(string name, Transform parent, Vector2 p, float height = 0f, float yaw = 0f)
        {
            var a = Load(name);
            if (a == null || a.mesh == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.x, p.y, -height);
            go.transform.rotation = Quaternion.Euler(0f, 0f, yaw);
            go.layer = LayerMask.NameToLayer("Walls");
            var visual = new GameObject("Mesh");
            visual.transform.SetParent(go.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = a.mesh;
            var mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterials = a.mesh.subMeshCount <= 2 ? new[] { BodyMaterial(), GlassMaterial() }
                : new[] { BodyMaterial(), GlassMaterial(), Textured("leaf", LeafTexture(), 1.55f), Textured("bark", BarkTexture(), 1.15f), Textured("rock", RockTexture(), 1.2f) };
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            if (a.footprint.x > 0f)
            {
                var bc = go.AddComponent<BoxCollider2D>();
                bc.size = a.footprint;
            }
            return go;
        }

        public static Material BodyMaterial()
        {
            if (body != null) return body;
            body = new Material(WorldTiles.MeshMaterial("env3d_body", GrainTexture())) { name = "Env3D Body" };
            body.SetFloat("_Cull", (float)CullMode.Back);
            body.SetColor("_Color", new Color(1.1f, 1.1f, 1.1f, 1f));
            return body;
        }

        static Material GlassMaterial()
        {
            if (glass != null) return glass;
            glass = new Material(BodyMaterial()) { name = "Env3D Glass" };
            glass.SetFloat("_Emission", 0.9f);
            return glass;
        }

        // ---------- Pixel textures for leaves, bark and rock (72 px per unit, like the sprites) ----------

        static readonly Dictionary<string, Material> textured = new Dictionary<string, Material>();

        static Material Textured(string key, Texture2D tex, float bright)
        {
            if (textured.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(WorldTiles.MeshMaterial("env3d_" + key, tex)) { name = "Env3D " + key };
            m.SetFloat("_Cull", (float)CullMode.Off);   // rounded normals: draw both sides rather than guess the winding
            m.SetColor("_Color", new Color(bright, bright, bright, 1f));
            textured[key] = m;
            return m;
        }

        static Texture2D NewTex(string name) =>
            new Texture2D(288, 288, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };

        // Little leaves, lit from the upper left, packed over a dark ground: the tree's own colour comes from the mesh.
        static Texture2D LeafTexture()
        {
            const int N = 288;
            var t = NewTex("Leaves");
            var v = new float[N * N];
            for (int i = 0; i < v.Length; i++) v[i] = 0.42f;
            var rng = new System.Random(5);
            for (int n = 0; n < 2600; n++)
            {
                int cx = rng.Next(N), cy = rng.Next(N);
                int w = 2 + rng.Next(3), h = 2 + rng.Next(2);
                float tone = 0.68f + (float)rng.NextDouble() * 0.22f;
                for (int y = -1; y <= h; y++)
                for (int x = -1; x <= w; x++)
                {
                    int px = (cx + x + N) % N, py = (cy + y + N) % N;
                    bool edge = x < 0 || y < 0 || x == w || y == h;
                    if (edge) { v[py * N + px] = Mathf.Min(v[py * N + px], 0.5f); continue; }
                    // The top-left pixels of a leaf catch the light.
                    v[py * N + px] = (x == 0 && y == h - 1) || (x == 1 && y == h - 1) ? 1f : tone;
                }
            }
            var px32 = new Color32[N * N];
            for (int i = 0; i < v.Length; i++) { byte b = (byte)(Mathf.Clamp01(v[i]) * 255); px32[i] = new Color32(b, b, b, 255); }
            t.SetPixels32(px32); t.Apply();
            return t;
        }

        static Texture2D BarkTexture()
        {
            const int N = 288;
            var t = NewTex("Bark");
            var px = new Color32[N * N];
            var rng = new System.Random(6);
            var col = new float[N];
            for (int x = 0; x < N; x++) col[x] = 0.7f + (float)rng.NextDouble() * 0.25f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float k = col[x / 3 * 3];
                if ((x / 3) % 4 == 0) k *= 0.6f;                         // furrows
                if (((y + x * 13) % 41) < 2) k *= 0.75f;                 // knots
                byte b = (byte)(Mathf.Clamp01(k) * 255);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D RockTexture()
        {
            const int N = 288;
            var t = NewTex("Rock");
            var px = new Color32[N * N];
            var rng = new System.Random(7);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float ax = x * Mathf.PI * 2f / N, ay = y * Mathf.PI * 2f / N;
                float n = Mathf.PerlinNoise(20f + Mathf.Cos(ax) * 3f + Mathf.Sin(ay), 20f + Mathf.Sin(ax) * 3f + Mathf.Cos(ay) * 3f);
                float k = 0.62f + Mathf.Floor(n * 4f) / 4f * 0.38f;    // banded, like painted stone
                if (rng.NextDouble() < 0.03) k *= 0.7f;
                byte b = (byte)(Mathf.Clamp01(k) * 255);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        // Soft blotches of light and dark at one texel per art pixel, so big surfaces aren't one flat colour.
        static Texture2D GrainTexture()
        {
            const int N = 288;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "Env3D Grain", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            var rng = new System.Random(3);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // Tileable blotches: noise on a torus.
                float ax = x * Mathf.PI * 2f / N, ay = y * Mathf.PI * 2f / N;
                float n = Mathf.PerlinNoise(10f + Mathf.Cos(ax) * 2.2f + Mathf.Sin(ay) * 0.7f, 10f + Mathf.Sin(ax) * 2.2f + Mathf.Cos(ay) * 1.9f);
                float v = 0.86f + n * 0.12f + (rng.NextDouble() < 0.06 ? -0.05f : 0f);
                byte b = (byte)(Mathf.Clamp01(v) * 255);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }
    }
}
