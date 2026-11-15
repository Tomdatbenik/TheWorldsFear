using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    // The farmsteads round the town walls (WorldGen.Farms): each is fenced in with its gate toward the town, a lane
    // up the middle to the yard, fields of wheat, cabbages and corn either side, and at the back a barn, a farmhouse,
    // haystacks and sometimes a windmill. The land there is flat and clear (WorldGen keeps it so) and the monsters
    // round about are rats and spiders, so a new hero learns to fight here.
    public class Farmland : MonoBehaviour
    {
        enum Crop { Wheat, Cabbage, Corn }

        Town.Solid wood, soil;
        // The crops are pixel-art sprites standing in the fields, facing the camera like the grass (drawn instanced).
        readonly Dictionary<Crop, List<Matrix4x4>> plants = new Dictionary<Crop, List<Matrix4x4>>();
        readonly List<Matrix4x4> batch = new List<Matrix4x4>();
        static Mesh card;
        static readonly Dictionary<Crop, Material> cropMats = new Dictionary<Crop, Material>();

        public static void Build(Transform parent)
        {
            if (FindAnyObjectByType<Farmland>() != null) return;
            var go = new GameObject("Farmland");
            go.transform.SetParent(parent, false);
            go.AddComponent<Farmland>().Make();
        }

        void Make()
        {
            wood = new Town.Solid(); soil = new Town.Solid();
            foreach (Crop c in System.Enum.GetValues(typeof(Crop))) plants[c] = new List<Matrix4x4>();
            int i = 0;
            foreach (var f in WorldGen.Farms) Farm(f, i++);
            wood.Build(transform, "Fences", Town.Mat("wood", Town.PlankTexture()), true);
            soil.Build(transform, "Tilled Soil", Town.Mat("soil", SoilTexture()), false);

        }

        // ---------- One farm, laid out in its own frame: u along the gate side, v inward from it ----------

        struct Frame
        {
            public RectInt r; public int side;   // 0 gate on the south, 1 east, 2 north, 3 west
            public float U => side % 2 == 0 ? r.width : r.height;
            public float V => side % 2 == 0 ? r.height : r.width;

            public Vector2 P(float u, float v)
            {
                switch (side)
                {
                    case 0: return new Vector2(r.xMin + u, r.yMin + v);
                    case 2: return new Vector2(r.xMax - u, r.yMax - v);
                    case 1: return new Vector2(r.xMax - v, r.yMin + u);
                    default: return new Vector2(r.xMin + v, r.yMax - u);
                }
            }

            public Rect R(float u0, float v0, float u1, float v1)
            {
                var a = P(u0, v0); var b = P(u1, v1);
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }

            // A building whose door faces the gate.
            public float Yaw => side == 0 ? 0f : side == 1 ? 90f : side == 2 ? 180f : -90f;
            // Rows run along v (from the gate inward): true when that is world x.
            public bool RowsAlongX => side % 2 == 1;
        }

        void Farm(WorldGen.Farm farm, int index)
        {
            var d = farm.dirToTown;
            int side = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (d.x > 0 ? 1 : 3) : (d.y > 0 ? 2 : 0);
            var f = new Frame { r = farm.rect, side = side };
            var rng = new System.Random(farm.seed);
            float U = f.U, V = f.V, mid = U / 2f;

            // Fence all round, the gate gap in the middle of the town side.
            Fence(f.P(0, 0), f.P(mid - 2f, 0));
            Fence(f.P(mid + 2f, 0), f.P(U, 0));
            Fence(f.P(U, 0), f.P(U, V));
            Fence(f.P(U, V), f.P(0, V));
            Fence(f.P(0, V), f.P(0, 0));
            var post = f.P(mid - 2f, 0); var post2 = f.P(mid + 2f, 0);
            Post(post, 1.6f); Post(post2, 1.6f);

            // The yard at the back: a barn, a farmhouse, haystacks; every other farm has a windmill.
            float back = V - 6.2f;
            EnvArt.PaintGround(transform, Grow(f.R(1f, V - 12.5f, U - 1f, V - 0.8f), 0.5f), "dirt_tile",
                (x, y) => 1.2f, "Farmyard", -0.03f);
            EnvArt.PaintGround(transform, f.R(mid - 1.6f, -0.5f, mid + 1.6f, V - 12f), "dirt_tile",
                (x, y) => 1.2f, "Farm Lane", -0.03f);
            EnvMesh.Place("barn", transform, f.P(U * 0.24f, back), 0f, f.Yaw);
            EnvMesh.Place(index % 3 == 0 ? "red_cottage" : "thatch_cottage", transform, f.P(U * 0.72f, V - 4.5f), 0f, f.Yaw);
            EnvMesh.Place("haystack", transform, f.P(U * 0.47f, V - 3f), 0f, (float)rng.NextDouble() * 360f);
            EnvMesh.Place("haystack", transform, f.P(U * 0.42f, V - 6.5f), 0f, (float)rng.NextDouble() * 360f);
            if (index % 2 == 0 && U >= 26f) Windmill(transform, f.P(U - 3.2f, V - 10.5f));

            // Fields either side of the lane, a crop each, a scarecrow in one.
            var crops = new[] { Crop.Wheat, Crop.Cabbage, Crop.Corn };
            float v0 = 2.5f, v1 = V - 13.5f;
            if (v1 - v0 >= 3f)
            {
                // Two fields each side of the lane, split across the middle, a crop each.
                float vm = (v0 + v1) / 2f;
                var plots = new[] { f.R(2f, v0, mid - 2.5f, vm - 0.6f), f.R(2f, vm + 0.6f, mid - 2.5f, v1),
                                    f.R(mid + 2.5f, v0, U - 2f, vm - 0.6f), f.R(mid + 2.5f, vm + 0.6f, U - 2f, v1) };
                int first = rng.Next(3);
                for (int p = 0; p < plots.Length; p++) Field(plots[p], crops[(first + p) % 3], f.RowsAlongX);
                EnvMesh.Place("scarecrow", transform, plots[rng.Next(plots.Length)].center, 0f, 0f);   // facing south, toward the camera
            }
        }

        static Rect Grow(Rect r, float by) => new Rect(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);

        public static void Windmill(Transform parent, Vector2 p)
        {
            var mill = EnvMesh.Place("windmill", parent, p, 0f, 0f);
            if (mill == null) return;
            // The sails turn on the front of the cap, facing south.
            var sails = EnvMesh.Place("sails", mill.transform, p + new Vector2(0f, -2.15f), 7.0f * 1.4f + 0.35f, 0f);
            if (sails == null) return;
            Destroy(sails.GetComponent<Collider>());
            sails.AddComponent<Spin>();
        }

        // ---------- Fences ----------

        void Fence(Vector2 a, Vector2 b)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.1f) return;
            Vector2 dir = (b - a) / len;
            int posts = Mathf.Max(1, Mathf.RoundToInt(len / 2f));
            for (int i = 0; i <= posts; i++) Post(a + dir * (len * i / posts), 1.15f);
            // Two rails.
            foreach (float h in new[] { 0.45f, 0.9f })
            {
                var r = dir.x != 0f ? Rect.MinMaxRect(Mathf.Min(a.x, b.x), a.y - 0.05f, Mathf.Max(a.x, b.x), a.y + 0.05f)
                                    : Rect.MinMaxRect(a.x - 0.05f, Mathf.Min(a.y, b.y), a.x + 0.05f, Mathf.Max(a.y, b.y));
                Town.Box(wood, wood, r, h, h + 0.14f);
            }
            var go = new GameObject("Fence");
            go.transform.SetParent(transform, false);
            go.layer = LayerMask.NameToLayer("Walls");
            Phys.Box(go, dir.x != 0f ? new Vector2(len, 0.2f) : new Vector2(0.2f, len), (a + b) / 2f);
        }

        void Post(Vector2 p, float h) => Town.Box(wood, wood, new Rect(p.x - 0.09f, p.y - 0.09f, 0.18f, 0.18f), 0f, h);

        // ---------- Fields ----------

        // Tilled soil with rows of a crop on it, the rows running away from the gate.
        void Field(Rect r, Crop crop, bool rowsAlongX)
        {
            soil.Quad(new Vector3(r.xMin, r.yMin, -0.03f), new Vector3(r.xMax, r.yMin, -0.03f), new Vector3(r.xMax, r.yMax, -0.03f), new Vector3(r.xMin, r.yMax, -0.03f),
                Vector3.back, new Vector2(r.xMin, r.yMin) / 4f, new Vector2(r.xMax, r.yMin) / 4f, new Vector2(r.xMax, r.yMax) / 4f, new Vector2(r.xMin, r.yMax) / 4f);
            // Rows of plants, a little uneven, each one a sprite.
            var size = CropSize(crop);
            const float spacing = 1.1f;
            float step = crop == Crop.Cabbage ? 0.7f : crop == Crop.Corn ? 0.55f : 0.32f;
            var rng = new System.Random(Mathf.RoundToInt(r.xMin * 31f + r.yMin * 17f));
            var list = plants[crop];
            for (float a = (rowsAlongX ? r.yMin : r.xMin) + 0.6f; a < (rowsAlongX ? r.yMax : r.xMax) - 0.3f; a += spacing)
                for (float b = (rowsAlongX ? r.xMin : r.yMin) + 0.4f; b < (rowsAlongX ? r.xMax : r.yMax) - 0.3f; b += step)
                {
                    float jit = ((float)rng.NextDouble() - 0.5f) * 0.12f, sc = 0.85f + (float)rng.NextDouble() * 0.3f;
                    var pos = rowsAlongX ? new Vector3(b + jit, 0f, a + jit) : new Vector3(a + jit, 0f, b + jit);
                    list.Add(Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size.x * sc * (rng.NextDouble() < 0.5 ? -1f : 1f), size.y * sc, 1f)));
                }
        }

        static Vector2 CropSize(Crop c) => c == Crop.Wheat ? new Vector2(0.5f, 0.75f) : c == Crop.Cabbage ? new Vector2(0.56f, 0.42f) : new Vector2(0.56f, 1.5f);

        void LateUpdate()
        {
            if (card == null) card = Card();
            var hero = HeroController.Instance;
            Vector2 near = CameraFollow.CinematicActive ? CameraFollow.CinematicFocus.G() : hero != null ? hero.transform.G() : Vector2.zero;
            if (near.magnitude > WorldGen.FarmRingOuter + 60f) return;
            var rot = View.Rotation;
            foreach (var kv in plants)
            {
                if (kv.Value.Count == 0) continue;
                batch.Clear();
                foreach (var m in kv.Value)
                    batch.Add(Matrix4x4.TRS(m.GetColumn(3), rot, new Vector3(m.m00, m.m11, 1f)));
                var rp = new RenderParams(CropMaterial(kv.Key)) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, layer = 0 };
                for (int start = 0; start < batch.Count; start += 1023)
                    Graphics.RenderMeshInstanced(rp, card, 0, batch, Mathf.Min(1023, batch.Count - start), start);
            }
        }

        static Mesh Card()
        {
            var m = new Mesh { name = "CropCard" };
            m.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }

        static Material CropMaterial(Crop c)
        {
            if (cropMats.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(RenderAssets.Instance.litWind) { name = "Crop " + c, mainTexture = CropSprite(c), enableInstancing = true };
            cropMats[c] = m;
            return m;
        }

        // ---------- Pixel-art crop sprites (72 px per unit), outlined like the rest of the art ----------

        static Texture2D CropSprite(Crop crop)
        {
            var size = CropSize(crop);
            int W = Mathf.RoundToInt(size.x * 72f), H = Mathf.RoundToInt(size.y * 72f);
            var px = new Color32[W * H];
            var rng = new System.Random((int)crop * 13 + 5);
            void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = c; }
            void Disc(float cx, float cy, float rx, float ry, Color32 lit, Color32 mid, Color32 dark)
            {
                for (int y = Mathf.FloorToInt(cy - ry); y <= cy + ry; y++)
                for (int x = Mathf.FloorToInt(cx - rx); x <= cx + rx; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry, d = dx * dx + dy * dy;
                    if (d > 1f) continue;
                    // Lit from the upper left.
                    float l = -dx * 0.6f + dy * 0.8f;
                    Set(x, y, l > 0.35f ? lit : l < -0.35f ? dark : mid);
                }
            }
            switch (crop)
            {
                case Crop.Wheat:
                {
                    var stalk = new Color32(196, 156, 64, 255); var ear = new Color32(236, 198, 98, 255);
                    var earLit = new Color32(252, 228, 150, 255); var earDark = new Color32(178, 128, 48, 255);
                    for (int k = 0; k < 6; k++)
                    {
                        int x = 4 + k * (W - 8) / 5 + rng.Next(-1, 2), top = H - 4 - rng.Next(10);
                        for (int y = 0; y < top - 10; y++) Set(x + (y > top - 24 ? (k % 2 == 0 ? 1 : 0) : 0), y, stalk);
                        Disc(x + 0.5f, top - 6, 2.4f, 6.5f, earLit, ear, earDark);
                        if (rng.NextDouble() < 0.6) for (int y = top - 2; y < top + 3; y++) Set(x + 1, y, earLit);   // whiskers
                    }
                    break;
                }
                case Crop.Corn:
                {
                    var stalk = new Color32(86, 150, 58, 255); var leaf = new Color32(112, 178, 72, 255); var leafDark = new Color32(56, 110, 48, 255);
                    var cob = new Color32(240, 204, 80, 255); var cobLit = new Color32(252, 232, 140, 255); var husk = new Color32(150, 190, 90, 255);
                    int cx = W / 2;
                    for (int y = 0; y < H - 6; y++) { Set(cx, y, stalk); Set(cx + 1, y, leafDark); }
                    for (int k = 0; k < 6; k++)
                    {
                        int y0 = 12 + k * (H - 30) / 6, dir = k % 2 == 0 ? 1 : -1, len = 12 + rng.Next(8);
                        for (int i = 0; i < len; i++)
                        {
                            int x = cx + dir * (i + 1), y = y0 + i / 2 - (i > len - 5 ? (i - len + 5) : 0);
                            Set(x, y, i < 3 ? leafDark : leaf); Set(x, y + 1, leaf);
                        }
                    }
                    Disc(cx + 4, H * 0.5f, 3.2f, 8f, cobLit, cob, husk);
                    for (int i = 0; i < 6; i++) Set(cx - 2 + i, H - 6 + (i % 2), new Color32(214, 180, 100, 255));   // tassel
                    break;
                }
                default:
                {
                    var lit = new Color32(176, 222, 120, 255); var mid = new Color32(112, 176, 76, 255); var dark = new Color32(56, 118, 56, 255);
                    Disc(W / 2f, H * 0.42f, W * 0.46f, H * 0.4f, lit, mid, dark);
                    Disc(W / 2f + 1, H * 0.5f, W * 0.28f, H * 0.26f, new Color32(206, 236, 156, 255), lit, mid);
                    for (int i = 0; i < 4; i++) Set(W / 2 - 1 + i, (int)(H * 0.5f), dark);   // the heart's fold
                    break;
                }
            }
            // A dark outline round every plant, like the sprites.
            var ink = new Color32(40, 30, 28, 255);
            var outPx = (Color32[])px.Clone();
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (px[y * W + x].a != 0) continue;
                bool edge = (x > 0 && px[y * W + x - 1].a != 0) || (x < W - 1 && px[y * W + x + 1].a != 0) || (y > 0 && px[(y - 1) * W + x].a != 0) || (y < H - 1 && px[(y + 1) * W + x].a != 0);
                if (edge && y > 0) outPx[y * W + x] = ink;
            }
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "Crop " + crop, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(outPx); t.Apply();
            return t;
        }

        // ---------- Textures (72 px per unit) ----------

        static Texture2D SoilTexture()
        {
            const int N = 288;
            var t = Town.NewTex("FarmSoil", N, N);
            var px = new Color32[N * N];
            var rng = new System.Random(21);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // Furrows every 0.55 units both ways (the texture is laid in world space), with clods.
                bool furrow = (x % 40) < 6 || (y % 40) < 6;
                float n = (float)rng.NextDouble();
                byte r = (byte)(furrow ? 74 : 112 + n * 18), g = (byte)(furrow ? 48 : 74 + n * 12), b = (byte)(furrow ? 32 : 46 + n * 8);
                px[y * N + x] = new Color32(r, g, b, 255);
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }
    }

    // Turns a windmill's sails.
    public class Spin : MonoBehaviour
    {
        public float speed = 40f;
        void Update() => transform.Rotate(Vector3.forward, speed * Time.deltaTime, Space.World);
    }
}
