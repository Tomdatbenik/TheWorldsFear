using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // The world is Y-up: the ground is the XZ plane. Game logic thinks in 2D ground points (Vector2 x, y);
    // a ground point (x, y) at height h sits at world (x, h, y). These helpers are the one place that mapping lives.
    public static class Ground
    {
        // A sprite or quad lying on the ground, face up, its top pointing north (+Z).
        public static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f);

        // Flat decals (telegraphs, rings) sit a hair above the ground to avoid z-fighting,
        // above the village plaza and roads, which would otherwise hide decals there.
        public const float DecalH = 0.07f;
        public static Vector3 Decal(Vector2 p) => p.W(DecalH + TerrainHeight.At(p));

        // Colliders are tall upright extrusions of the old 2D shapes, so bodies at any height still meet the same walls.
        public const float TallH = 120f;

        // World (or unit-space) vector -> ground point.
        public static Vector2 G(this Vector3 v) => new Vector2(v.x, v.z);
        // A ground point standing on the terrain there (plus lift).
        public static Vector3 On(Vector2 g, float lift = 0f) => g.W(TerrainHeight.At(g) + lift);
        // Ground point -> world, at height h.
        public static Vector3 W(this Vector2 g, float h = 0f) => new Vector3(g.x, h, g.y);
        public static Vector3 W(this Vector2Int g, float h = 0f) => new Vector3(g.x, h, g.y);
        // A transform's ground position.
        public static Vector2 G(this Transform t) => new Vector2(t.position.x, t.position.z);
        // Moves a transform to a ground point, keeping its height.
        public static void SetG(this Transform t, Vector2 g) => t.position = new Vector3(g.x, t.position.y, g.y);
        // Rotation about the up axis that turns ground direction +X by `degrees` counter-clockwise (seen from above),
        // matching the old Quaternion.Euler(0, 0, degrees) on the ground plane.
        public static Quaternion Spin(float degrees) => Quaternion.Euler(0f, -degrees, 0f);
        // Rotation of an upright thing (a mesh, a body) facing ground direction dir.
        public static Quaternion Facing(Vector2 dir) => Spin(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

        // Old convention (ground XY, up -Z) -> Y-up, for a point or direction.
        public static Vector3 FromOld(Vector3 v) => new Vector3(v.x, -v.z, v.y);

        // Converts a mesh built in the old convention (ground XY, up -Z) to Y-up, normals included.
        public static Mesh ToY(Mesh m)
        {
            if (m == null) return m;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = FromOld(v[i]);
            m.vertices = v;
            var n = m.normals;
            if (n != null && n.Length == v.Length)
            {
                for (int i = 0; i < n.Length; i++) n[i] = FromOld(n[i]);
                m.normals = n;
            }
            m.RecalculateBounds();
            return m;
        }
    }

    // A body that moves over the ground: a 3D Rigidbody seen through the 2D ground API the game logic uses.
    // Its height (Y) belongs to the terrain (GroundFollow), never to the physics.
    [DisallowMultipleComponent]
    public class GroundBody : MonoBehaviour
    {
        Rigidbody rb;
        public Rigidbody Rigidbody => rb != null ? rb : (rb = GetComponent<Rigidbody>());

        // Gives a GameObject a ground body, replacing any 2D physics left on it by an old prefab.
        // circleRadius > 0 also gives it an upright round collider (at circleOffset on the ground).
        public static GroundBody Ensure(GameObject go, float circleRadius = 0f, Vector2 circleOffset = default, bool kinematic = false)
        {
            float r = circleRadius;
            Vector2 off = circleOffset;
            float mass = 1f;
            var rb2 = go.GetComponent<Rigidbody2D>();
            if (rb2 != null) { mass = rb2.mass; Object.DestroyImmediate(rb2); }
            foreach (var c2 in go.GetComponents<Collider2D>())
            {
                if (r <= 0f)
                {
                    if (c2 is CircleCollider2D cc) { r = cc.radius; off = cc.offset; }
                    else if (c2 is CapsuleCollider2D cap) { r = Mathf.Min(cap.size.x, cap.size.y) * 0.5f; off = cap.offset; }
                    else if (c2 is BoxCollider2D bx) { r = Mathf.Min(bx.size.x, bx.size.y) * 0.5f; off = bx.offset; }
                }
                Object.DestroyImmediate(c2);
            }
            var rb = Phys.Body(go, kinematic);
            rb.mass = mass;
            if (r > 0f && go.GetComponent<Collider>() == null) Phys.Circle(go, r, off);
            var b = go.GetComponent<GroundBody>();
            if (b == null) b = go.AddComponent<GroundBody>();
            b.rb = rb;
            return b;
        }

        public Vector2 position
        {
            get => Rigidbody.position.G();
            set
            {
                var p = new Vector3(value.x, transform.position.y, value.y);
                Rigidbody.position = p;
                transform.position = p;
            }
        }

        public Vector2 linearVelocity
        {
            get => Rigidbody.linearVelocity.G();
            set { if (!Rigidbody.isKinematic) Rigidbody.linearVelocity = value.W(); }
        }

        public float mass { get => Rigidbody.mass; set => Rigidbody.mass = value; }
        public bool isKinematic { get => Rigidbody.isKinematic; set => Rigidbody.isKinematic = value; }

        public void MovePosition(Vector2 p) => Rigidbody.MovePosition(new Vector3(p.x, transform.position.y, p.y));
    }

    // Upright box colliders for every filled cell of a tilemap (the old TilemapCollider2D). Rows of cells are merged
    // into long boxes, per 16x16 region, and a region is rebuilt whenever one of its tiles changes.
    [RequireComponent(typeof(UnityEngine.Tilemaps.Tilemap))]
    public class TilemapBoxes : MonoBehaviour
    {
        const int R = 16;
        UnityEngine.Tilemaps.Tilemap tm;
        readonly HashSet<Vector2Int> dirty = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, GameObject> regions = new Dictionary<Vector2Int, GameObject>();
        bool all = true;

        void OnEnable()
        {
            tm = GetComponent<UnityEngine.Tilemaps.Tilemap>();
            UnityEngine.Tilemaps.Tilemap.tilemapTileChanged += Changed;
            all = true;
        }

        void OnDisable() => UnityEngine.Tilemaps.Tilemap.tilemapTileChanged -= Changed;

        void Changed(UnityEngine.Tilemaps.Tilemap map, UnityEngine.Tilemaps.Tilemap.SyncTile[] tiles)
        {
            if (map != tm) return;
            foreach (var t in tiles) dirty.Add(new Vector2Int(Mathf.FloorToInt(t.position.x / (float)R), Mathf.FloorToInt(t.position.y / (float)R)));
        }

        void LateUpdate()
        {
            if (all)
            {
                all = false;
                tm.CompressBounds();
                var b = tm.cellBounds;
                for (int ry = Mathf.FloorToInt(b.yMin / (float)R); ry * R < b.yMax; ry++)
                for (int rx = Mathf.FloorToInt(b.xMin / (float)R); rx * R < b.xMax; rx++)
                    dirty.Add(new Vector2Int(rx, ry));
            }
            if (dirty.Count == 0) return;
            foreach (var r in dirty) Build(r);
            dirty.Clear();
        }

        // Builds the colliders now (for code that queries walls right after placing tiles).
        public void Rebuild()
        {
            all = true;
            LateUpdate();
        }

        void Build(Vector2Int r)
        {
            if (regions.TryGetValue(r, out var old) && old != null) Destroy(old);
            regions.Remove(r);
            var filled = new bool[R, R];
            bool any = false;
            for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
                if (tm.HasTile(new Vector3Int(r.x * R + x, r.y * R + y, 0))) { filled[x, y] = true; any = true; }
            if (!any) return;
            var go = new GameObject("Boxes " + r.x + "," + r.y);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            regions[r] = go;
            var used = new bool[R, R];
            for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                if (!filled[x, y] || used[x, y]) continue;
                int w = 1;
                while (x + w < R && filled[x + w, y] && !used[x + w, y]) w++;
                int h = 1;
                while (y + h < R)
                {
                    bool row = true;
                    for (int k = 0; k < w && row; k++) row = filled[x + k, y + h] && !used[x + k, y + h];
                    if (!row) break;
                    h++;
                }
                for (int yy = 0; yy < h; yy++) for (int xx = 0; xx < w; xx++) used[x + xx, y + yy] = true;
                var a = tm.CellToLocal(new Vector3Int(r.x * R + x, r.y * R + y, 0));
                var c = tm.CellToLocal(new Vector3Int(r.x * R + x + w, r.y * R + y + h, 0));
                var box = go.AddComponent<BoxCollider>();
                var size = c - a;
                box.center = (a + c) * 0.5f;
                box.size = new Vector3(Mathf.Abs(size.x), Ground.TallH, Mathf.Abs(size.z) > 1e-4f ? Mathf.Abs(size.z) : Mathf.Abs(size.y));
                if (Mathf.Abs(size.z) <= 1e-4f) box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Ground.TallH);
            }
        }
    }

    // 2D-style physics queries on the ground plane, run against the tall 3D colliders.
    public static class Phys
    {
        const float Half = Ground.TallH * 0.5f;
        static readonly Collider[] buffer = new Collider[256];

        public static Collider OverlapCircle(Vector2 p, float r, int mask = Physics.DefaultRaycastLayers)
        {
            int n = Physics.OverlapCapsuleNonAlloc(p.W(-Half), p.W(Half), r, buffer, mask, QueryTriggerInteraction.Collide);
            return n > 0 ? buffer[0] : null;
        }

        public static Collider[] OverlapCircleAll(Vector2 p, float r, int mask = Physics.DefaultRaycastLayers) =>
            Physics.OverlapCapsule(p.W(-Half), p.W(Half), r, mask, QueryTriggerInteraction.Collide);

        public static Collider OverlapPoint(Vector2 p, int mask = Physics.DefaultRaycastLayers) => OverlapCircle(p, 0.01f, mask);

        public struct Hit
        {
            public Collider collider;
            public Vector2 point, normal;
            public float distance;
            public static implicit operator bool(Hit h) => h.collider != null;
        }

        static Hit From(RaycastHit h) => new Hit { collider = h.collider, point = h.point.G(), normal = h.normal.G().normalized, distance = h.distance };

        public static Hit Raycast(Vector2 origin, Vector2 dir, float distance = Mathf.Infinity, int mask = Physics.DefaultRaycastLayers)
        {
            if (dir.sqrMagnitude < 1e-8f) return default;
            return Physics.Raycast(origin.W(), dir.normalized.W(), out var h, distance, mask, QueryTriggerInteraction.Ignore) ? From(h) : default;
        }

        public static Hit Linecast(Vector2 a, Vector2 b, int mask = Physics.DefaultRaycastLayers)
        {
            var d = b - a;
            return Raycast(a, d, d.magnitude, mask);
        }

        public static Hit CircleCast(Vector2 origin, float radius, Vector2 dir, float distance = Mathf.Infinity, int mask = Physics.DefaultRaycastLayers)
        {
            if (dir.sqrMagnitude < 1e-8f) return default;
            return Physics.SphereCast(origin.W(), radius, dir.normalized.W(), out var h, distance, mask, QueryTriggerInteraction.Ignore) ? From(h) : default;
        }

        // Bodies move on the ground plane: no gravity, no tipping over, height set by the terrain (GroundFollow).
        public static Rigidbody Body(GameObject go, bool kinematic = false)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = kinematic;
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            return rb;
        }

        // A circle on the ground: an upright capsule as tall as the world.
        public static CapsuleCollider Circle(GameObject go, float radius, Vector2 offset = default, bool trigger = false)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = radius;
            c.height = Ground.TallH + radius * 2f;
            c.center = offset.W();
            c.isTrigger = trigger;
            return c;
        }

        // A box on the ground: size and offset in ground units, extruded upright.
        public static BoxCollider Box(GameObject go, Vector2 size, Vector2 offset = default, bool trigger = false)
        {
            var c = go.AddComponent<BoxCollider>();
            c.size = new Vector3(size.x, Ground.TallH, size.y);
            c.center = offset.W();
            c.isTrigger = trigger;
            return c;
        }

        public static void SetBox(BoxCollider c, Vector2 size, Vector2 offset = default)
        {
            c.size = new Vector3(size.x, Ground.TallH, size.y);
            c.center = offset.W();
        }

        public static void SetCircle(CapsuleCollider c, float radius, Vector2 offset = default)
        {
            c.radius = radius;
            c.height = Ground.TallH + radius * 2f;
            c.center = offset.W();
        }

        // A chain of wall segments (the old EdgeCollider2D), each a thin upright box. Returns the boxes made.
        public static List<BoxCollider> Edge(GameObject go, IList<Vector2> points, float thickness = 0.1f)
        {
            var made = new List<BoxCollider>();
            for (int i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var d = b - a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                var seg = new GameObject("Edge");
                seg.layer = go.layer;
                seg.transform.SetParent(go.transform, false);
                seg.transform.localPosition = ((a + b) * 0.5f).W();
                seg.transform.localRotation = Ground.Facing(d);
                var c = seg.AddComponent<BoxCollider>();
                c.size = new Vector3(len + thickness, Ground.TallH, thickness);
                made.Add(c);
            }
            return made;
        }

        // Many small polygons (thin wall strips) as one upright extruded mesh collider.
        public static MeshCollider Prisms(GameObject go, IList<Vector2[]> polys)
        {
            var v = new List<Vector3>();
            var tris = new List<int>();
            const float H = Ground.TallH * 0.5f;
            foreach (var pts in polys)
            {
                int n = pts.Length, b = v.Count;
                for (int i = 0; i < n; i++) v.Add(pts[i].W(-H));
                for (int i = 0; i < n; i++) v.Add(pts[i].W(H));
                for (int i = 1; i + 1 < n; i++) { tris.Add(b); tris.Add(b + i); tris.Add(b + i + 1); tris.Add(b + n); tris.Add(b + n + i + 1); tris.Add(b + n + i); }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    tris.Add(b + i); tris.Add(b + n + j); tris.Add(b + j);
                    tris.Add(b + i); tris.Add(b + n + i); tris.Add(b + n + j);
                }
            }
            var m = new Mesh { name = "Prisms", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v);
            m.SetTriangles(tris, 0);
            var c = go.AddComponent<MeshCollider>();
            c.sharedMesh = m;
            return c;
        }

        // A filled polygon (the old PolygonCollider2D), as an upright extruded mesh collider (convex hull per triangle fan).
        public static MeshCollider Polygon(GameObject go, IList<Vector2> pts, bool trigger = false)
        {
            var m = new Mesh { name = "PolyCollider" };
            int n = pts.Count;
            var v = new Vector3[n * 2];
            for (int i = 0; i < n; i++) { v[i] = pts[i].W(-Ground.TallH * 0.5f); v[i + n] = pts[i].W(Ground.TallH * 0.5f); }
            var tris = new List<int>();
            for (int i = 1; i + 1 < n; i++) { tris.Add(0); tris.Add(i + 1); tris.Add(i); tris.Add(n); tris.Add(n + i); tris.Add(n + i + 1); }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                tris.Add(i); tris.Add(j); tris.Add(n + j);
                tris.Add(i); tris.Add(n + j); tris.Add(n + i);
            }
            m.vertices = v;
            m.triangles = tris.ToArray();
            var c = go.AddComponent<MeshCollider>();
            c.sharedMesh = m;
            if (trigger) { c.convex = true; c.isTrigger = true; }
            return c;
        }
    }
}
