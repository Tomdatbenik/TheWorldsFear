using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace TWF
{
    // Builds Ashvale, the village at the center of the world, at runtime: a cobbled plaza with a well
    // and the waypoint, roads out to the four directions, timber-framed houses with glowing windows,
    // a market stall, a smithy, lamp posts, fences and trees. Ground is the XY plane, up is -Z.
    [DefaultExecutionOrder(-90)]
    public class Village : MonoBehaviour
    {
        public RectInt area = new RectInt(-45, -30, 90, 90);
        public Vector2 plaza = new Vector2(2f, 4f);
        public float plazaRadius = 7f;
        public int seed = 1717;

        System.Random rng;
        ArtSet art;
        readonly List<Rect> blocked = new List<Rect>();   // footprints of buildings, roads and the plaza

        // Shared meshes, one per material.
        MB plaster, planks, timber, stone, door, window, glow, iron, cloth;
        readonly Dictionary<Color, MB> roofs = new Dictionary<Color, MB>();

        static readonly Color RoofRed = new Color(0.62f, 0.26f, 0.2f);
        static readonly Color RoofSlate = new Color(0.32f, 0.38f, 0.48f);
        static readonly Color RoofThatch = new Color(0.72f, 0.58f, 0.3f);
        static readonly Color RoofMoss = new Color(0.35f, 0.45f, 0.28f);

        void Awake()
        {
            rng = new System.Random(seed);
            art = ArtSet.Instance;
            plaster = new MB(); planks = new MB(); timber = new MB(); stone = new MB();
            door = new MB(); window = new MB(); glow = new MB(); iron = new MB(); cloth = new MB();

            BuildGround();
            BuildBuildings();
            BuildProps();
            BuildTrees();
            Flush();
        }

        // ---------- Ground ----------

        bool OnRoad(float x, float y) =>
            (x >= 1f && x < 4f && (y > plaza.y || y < plaza.y)) || (y >= 3f && y < 6f);

        void BuildGround()
        {
            var grid = new GameObject("Village Ground").AddComponent<Grid>();
            grid.transform.SetParent(transform, false);
            var go = new GameObject("Ground");
            go.transform.SetParent(grid.transform, false);
            var tm = go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            RenderAssets.MakeLit(r, false, RenderAssets.Instance.litGround);
            r.sortingOrder = -100;
            r.mode = TilemapRenderer.Mode.Chunk;

            var grass = art != null ? art.forestGround : WorldTiles.Instance.grassPlain;
            var path = art != null ? art.forestPath : WorldTiles.Instance.dirt;
            var cobble = art != null ? art.ruinsGround : WorldTiles.Instance.ruinsFloor;
            for (int x = area.xMin; x < area.xMax; x++)
            for (int y = area.yMin; y < area.yMax; y++)
            {
                uint h = (uint)((x * 73856093) ^ (y * 19349663)) & 0x7fffffff;
                float cx = x + 0.5f, cy = y + 0.5f;
                float dPlaza = Vector2.Distance(new Vector2(cx, cy), plaza);
                TileBase[] set = grass;
                if (dPlaza < plazaRadius) set = cobble;
                else if (OnRoad(cx, cy)) set = dPlaza < plazaRadius + 9f ? cobble : path;
                tm.SetTile(new Vector3Int(x, y, 0), set[h % (uint)set.Length]);
            }
            blocked.Add(new Rect(plaza.x - plazaRadius, plaza.y - plazaRadius, plazaRadius * 2f, plazaRadius * 2f));
            blocked.Add(new Rect(0.5f, area.yMin, 4f, area.height));
            blocked.Add(new Rect(area.xMin, 2.5f, area.width, 4f));
        }

        // ---------- Buildings ----------

        struct HouseSpec { public float x, y, w, d, h; public Color roof; public bool planked, tall, chimney; }

        void BuildBuildings()
        {
            var houses = new[]
            {
                new HouseSpec { x = -13f, y = 12.5f, w = 7f, d = 5f, h = 4.2f, roof = RoofRed, tall = true, chimney = true },   // the inn
                new HouseSpec { x = -12f, y = -4.5f, w = 5f, d = 4f, h = 2.6f, roof = RoofSlate, chimney = true },
                new HouseSpec { x = 15f, y = 13f, w = 5f, d = 4f, h = 2.8f, roof = RoofThatch, planked = true },
                new HouseSpec { x = 15f, y = -4.5f, w = 5f, d = 4f, h = 2.6f, roof = RoofRed, chimney = true },
                new HouseSpec { x = -5f, y = 18.5f, w = 5f, d = 4f, h = 2.6f, roof = RoofMoss, planked = true },
                new HouseSpec { x = 10.5f, y = 20f, w = 4f, d = 4f, h = 2.6f, roof = RoofSlate, chimney = true },
                new HouseSpec { x = -25f, y = 11f, w = 5f, d = 4f, h = 2.6f, roof = RoofThatch, planked = true },
                new HouseSpec { x = 26f, y = 12f, w = 5f, d = 4f, h = 2.8f, roof = RoofRed },
                new HouseSpec { x = -8f, y = -14f, w = 4f, d = 4f, h = 2.5f, roof = RoofMoss, chimney = true },
                new HouseSpec { x = 12f, y = -15f, w = 5f, d = 4f, h = 2.6f, roof = RoofSlate },
                new HouseSpec { x = -26f, y = -6f, w = 4f, d = 4f, h = 2.5f, roof = RoofRed, planked = true },
                new HouseSpec { x = 26f, y = -8f, w = 4f, d = 4f, h = 2.5f, roof = RoofThatch },
                new HouseSpec { x = -16f, y = 27f, w = 5f, d = 4f, h = 2.6f, roof = RoofSlate, chimney = true },
                new HouseSpec { x = 18f, y = 28f, w = 5f, d = 4f, h = 2.6f, roof = RoofRed, planked = true },
            };
            foreach (var h in houses) House(h);
            Sign(new Vector2(-9.2f, 9.3f), "The Weary Wolf");

            MarketStall(new Vector2(-4.5f, 10.2f));
            Smithy(new Vector2(9.5f, 10.5f));
            Well(new Vector2(-2.2f, 0.2f));
        }

        void House(HouseSpec s)
        {
            float x0 = s.x - s.w / 2f, x1 = s.x + s.w / 2f, y0 = s.y - s.d / 2f, y1 = s.y + s.d / 2f;
            float H = s.h, R = s.d * 0.42f, o = 0.4f;
            bool faceNorth = s.y < plaza.y;   // doors face the plaza side
            var walls = s.planked ? planks : plaster;

            // Stone foundation, then walls above it.
            const float found = 0.5f;
            Box(stone, x0 - 0.05f, y0 - 0.05f, s.w + 0.1f, s.d + 0.1f, 0f, found);
            walls.Wall(new Vector3(x0, y0, -found), Vector3.right, s.w, H - found, Vector3.down);
            walls.Wall(new Vector3(x1, y1, -found), Vector3.left, s.w, H - found, Vector3.up);
            walls.Wall(new Vector3(x0, y1, -found), Vector3.down, s.d, H - found, Vector3.left);
            walls.Wall(new Vector3(x1, y0, -found), Vector3.up, s.d, H - found, Vector3.right);
            // Gables.
            walls.Tri(new Vector3(x0, y0, -H), new Vector3(x0, y1, -H), new Vector3(x0, s.y, -H - R), Vector3.left);
            walls.Tri(new Vector3(x1, y0, -H), new Vector3(x1, y1, -H), new Vector3(x1, s.y, -H - R), Vector3.right);

            // Timber frame on plaster houses: corner posts and beams.
            if (!s.planked)
            {
                foreach (var c in new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x0, y1), new Vector2(x1, y1) })
                    Box(timber, c.x - 0.12f, c.y - 0.12f, 0.24f, 0.24f, found, H - found);
                Band(timber, x0, y0, x1, y1, H - 0.18f, 0.18f);
                if (s.tall) Band(timber, x0, y0, x1, y1, H * 0.5f, 0.16f);
                // Diagonal braces on the side walls.
                foreach (float x in new[] { x0 - 0.02f, x1 + 0.02f })
                {
                    var n = x < s.x ? Vector3.left : Vector3.right;
                    timber.Strip(new Vector3(x, y0 + 0.2f, -found), new Vector3(x, s.y, -H + 0.2f), 0.14f, n);
                    timber.Strip(new Vector3(x, y1 - 0.2f, -found), new Vector3(x, s.y, -H + 0.2f), 0.14f, n);
                }
            }

            // Roof: two slopes meeting at a ridge along X, with overhang.
            var roof = Roof(s.roof);
            float run = s.d / 2f + o;
            var nS = new Vector3(0f, -R, -run).normalized;
            var nN = new Vector3(0f, R, -run).normalized;
            float slope = Mathf.Sqrt(run * run + R * R);
            roof.Quad(new Vector3(x0 - o, y0 - o, -H + 0.12f), new Vector3(x1 + o, y0 - o, -H + 0.12f), new Vector3(x1 + o, s.y, -H - R), new Vector3(x0 - o, s.y, -H - R),
                nS, new Vector2(0, 0), new Vector2(s.w + 2 * o, 0), new Vector2(s.w + 2 * o, slope), new Vector2(0, slope));
            roof.Quad(new Vector3(x1 + o, y1 + o, -H + 0.12f), new Vector3(x0 - o, y1 + o, -H + 0.12f), new Vector3(x0 - o, s.y, -H - R), new Vector3(x1 + o, s.y, -H - R),
                nN, new Vector2(0, 0), new Vector2(s.w + 2 * o, 0), new Vector2(s.w + 2 * o, slope), new Vector2(0, slope));
            // Ridge beam.
            Box(timber, x0 - o, s.y - 0.1f, s.w + 2 * o, 0.2f, H + R - 0.05f, 0.15f);

            if (s.chimney)
                Box(stone, x1 - 1.2f, s.y + 0.2f, 0.6f, 0.6f, H, R + 0.7f);

            // Door on the plaza side, windows on every wall.
            float doorX = s.x + (s.w > 4.5f ? -s.w * 0.18f : 0f);
            float yFace = faceNorth ? y1 + 0.03f : y0 - 0.03f;
            var faceN = faceNorth ? Vector3.up : Vector3.down;
            door.Panel(new Vector3(doorX, yFace, 0f), faceNorth ? Vector3.left : Vector3.right, 1f, 1.7f, faceN);
            Box(stone, doorX - 0.7f, faceNorth ? y1 : y0 - 0.5f, 1.4f, 0.5f, 0f, 0.12f);   // doorstep

            int perSide = Mathf.Max(1, Mathf.FloorToInt(s.w / 2f));
            float[] rows = s.tall ? new[] { 1.5f, H * 0.5f + 1.05f } : new[] { 1.55f };
            foreach (float z in rows)
            for (int i = 0; i < perSide; i++)
            {
                float wx = x0 + (i + 0.5f) * s.w / perSide;
                foreach (bool north in new[] { false, true })
                {
                    if (north == faceNorth && z < 2f && Mathf.Abs(wx - doorX) < 1.1f) continue;
                    float wy = north ? y1 + 0.03f : y0 - 0.03f;
                    window.Panel(new Vector3(wx, wy, -z + 0.4f), north ? Vector3.left : Vector3.right, 0.8f, 0.8f, north ? Vector3.up : Vector3.down);
                }
            }
            foreach (bool east in new[] { false, true })
                window.Panel(new Vector3(east ? x1 + 0.03f : x0 - 0.03f, s.y, -1.15f), east ? Vector3.up : Vector3.down, 0.8f, 0.8f, east ? Vector3.right : Vector3.left);

            Solid(new Vector2(s.x, s.y), new Vector2(s.w, s.d));
            blocked.Add(new Rect(x0 - 1.5f, y0 - 1.5f, s.w + 3f, s.d + 3f));

            // A little fenced garden beside some houses.
            if (rng.NextDouble() < 0.6)
            {
                float gx = rng.NextDouble() < 0.5 ? x0 - 3.2f : x1 + 0.4f;
                Fence(new Rect(gx, y0, 2.8f, s.d), faceNorth);
            }
            if (rng.NextDouble() < 0.7) Barrel(new Vector2(x1 + 0.45f, faceNorth ? y1 + 0.6f : y0 - 0.6f));
        }

        void MarketStall(Vector2 c)
        {
            const float w = 4f, d = 2f, h = 2.3f;
            float x0 = c.x - w / 2f, x1 = c.x + w / 2f, y0 = c.y - d / 2f, y1 = c.y + d / 2f;
            foreach (var p in new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x0, y1), new Vector2(x1, y1) })
                Box(timber, p.x - 0.1f, p.y - 0.1f, 0.2f, 0.2f, 0f, p.y < c.y ? h : h + 0.5f);
            Box(planks, x0 + 0.1f, y0 + 0.1f, w - 0.2f, 0.8f, 0f, 0.95f);   // counter
            // Striped awning sloping toward the front.
            cloth.Quad(new Vector3(x0 - 0.3f, y0 - 0.5f, -h + 0.1f), new Vector3(x1 + 0.3f, y0 - 0.5f, -h + 0.1f), new Vector3(x1 + 0.3f, y1, -h - 0.5f), new Vector3(x0 - 0.3f, y1, -h - 0.5f),
                new Vector3(0, -0.5f, -1f).normalized, new Vector2(0, 0), new Vector2(w + 0.6f, 0), new Vector2(w + 0.6f, d + 0.5f), new Vector2(0, d + 0.5f));
            // Wares on the counter.
            for (int i = 0; i < 4; i++) Box(planks, x0 + 0.35f + i * 0.9f, y0 + 0.3f, 0.45f, 0.4f, 0.95f, 0.35f);
            Crate(new Vector2(x1 + 0.7f, y0 + 0.2f));
            Crate(new Vector2(x1 + 0.75f, y0 + 1.0f));
            Barrel(new Vector2(x0 - 0.6f, y0 + 0.3f));
            Solid(new Vector2(c.x, c.y + 0.1f), new Vector2(w, d - 0.2f));
            blocked.Add(new Rect(x0 - 1f, y0 - 1f, w + 2f, d + 2f));
        }

        void Smithy(Vector2 c)
        {
            const float w = 5f, d = 3.4f, h = 2.8f;
            float x0 = c.x - w / 2f, x1 = c.x + w / 2f, y0 = c.y - d / 2f, y1 = c.y + d / 2f;
            // Open front: back and side walls of stone, posts in front.
            Box(stone, x0, y1 - 0.4f, w, 0.4f, 0f, h);
            Box(stone, x0, y0 + 0.6f, 0.4f, d - 0.6f, 0f, h);
            Box(stone, x1 - 0.4f, y0 + 0.6f, 0.4f, d - 0.6f, 0f, h);
            Box(timber, x0, y0, 0.25f, 0.25f, 0f, h);
            Box(timber, x1 - 0.25f, y0, 0.25f, 0.25f, 0f, h);
            Band(timber, x0, y0, x1, y1, h - 0.2f, 0.2f);
            // Lean-to roof, high at the back.
            var roof = Roof(RoofSlate);
            float o = 0.4f;
            roof.Quad(new Vector3(x0 - o, y0 - o, -h), new Vector3(x1 + o, y0 - o, -h), new Vector3(x1 + o, y1 + o, -h - 1.1f), new Vector3(x0 - o, y1 + o, -h - 1.1f),
                new Vector3(0, -1.1f, -(d + 2 * o)).normalized, Vector2.zero, new Vector2(w + 2 * o, 0), new Vector2(w + 2 * o, d + 1f), new Vector2(0, d + 1f));
            stone.Tri(new Vector3(x0, y0, -h), new Vector3(x0, y1, -h), new Vector3(x0, y1, -h - 1.1f), Vector3.left);
            stone.Tri(new Vector3(x1, y0, -h), new Vector3(x1, y1, -h), new Vector3(x1, y1, -h - 1.1f), Vector3.right);
            // Furnace with a glowing mouth, and a chimney through the roof.
            Box(stone, c.x - 2.1f, y1 - 1.6f, 1.6f, 1.2f, 0f, 1.6f);
            glow.Panel(new Vector3(c.x - 1.3f, y1 - 1.62f, -0.55f), Vector3.right, 0.8f, 0.6f, Vector3.down);
            Box(stone, c.x - 1.75f, y1 - 1.3f, 0.8f, 0.7f, 1.6f, h + 1.9f);
            var fire = new GameObject("Furnace Light");
            fire.transform.SetParent(transform, false);
            fire.transform.position = new Vector3(c.x - 1.3f, y1 - 2.3f, -0.8f);
            var light = PointLights.Attach(fire.transform, new Color(1f, 0.5f, 0.2f), 6f, 2.2f, Vector3.zero, true);
            if (light != null) fire.AddComponent<Flicker>().target = light;
            // Anvil out front.
            Box(iron, c.x + 0.6f, y0 + 0.6f, 0.35f, 0.35f, 0f, 0.55f);
            Box(iron, c.x + 0.35f, y0 + 0.5f, 0.9f, 0.55f, 0.55f, 0.3f);
            Barrel(new Vector2(x1 + 0.6f, y0 + 0.4f));
            Solid(new Vector2(c.x, y1 - 0.2f), new Vector2(w, 0.4f));
            Solid(new Vector2(x0 + 0.2f, c.y + 0.3f), new Vector2(0.4f, d - 0.6f));
            Solid(new Vector2(x1 - 0.2f, c.y + 0.3f), new Vector2(0.4f, d - 0.6f));
            Solid(new Vector2(c.x - 1.3f, y1 - 1f), new Vector2(1.6f, 1.2f));
            blocked.Add(new Rect(x0 - 1f, y0 - 1f, w + 2f, d + 2f));
        }

        void Well(Vector2 c)
        {
            Prism(stone, c, 0.95f, 8, 0f, 0.8f);
            var water = new MB();
            water.Quad(new Vector3(c.x - 0.7f, c.y - 0.7f, -0.6f), new Vector3(c.x + 0.7f, c.y - 0.7f, -0.6f), new Vector3(c.x + 0.7f, c.y + 0.7f, -0.6f), new Vector3(c.x - 0.7f, c.y + 0.7f, -0.6f),
                Vector3.back, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            Mesh("Well Water", water, RenderAssets.Instance.water, false);
            Box(timber, c.x - 1.0f, c.y - 0.1f, 0.2f, 0.2f, 0f, 2.2f);
            Box(timber, c.x + 0.8f, c.y - 0.1f, 0.2f, 0.2f, 0f, 2.2f);
            Box(timber, c.x - 1.0f, c.y - 0.05f, 2f, 0.1f, 1.7f, 0.1f);
            var roof = Roof(RoofRed);
            roof.Quad(new Vector3(c.x - 1.3f, c.y - 0.9f, -2.1f), new Vector3(c.x + 1.3f, c.y - 0.9f, -2.1f), new Vector3(c.x + 1.3f, c.y, -2.7f), new Vector3(c.x - 1.3f, c.y, -2.7f),
                new Vector3(0, -0.6f, -0.9f).normalized, Vector2.zero, new Vector2(2.6f, 0), new Vector2(2.6f, 1.1f), new Vector2(0, 1.1f));
            roof.Quad(new Vector3(c.x + 1.3f, c.y + 0.9f, -2.1f), new Vector3(c.x - 1.3f, c.y + 0.9f, -2.1f), new Vector3(c.x - 1.3f, c.y, -2.7f), new Vector3(c.x + 1.3f, c.y, -2.7f),
                new Vector3(0, 0.6f, -0.9f).normalized, Vector2.zero, new Vector2(2.6f, 0), new Vector2(2.6f, 1.1f), new Vector2(0, 1.1f));
            Solid(c, Vector2.one * 1.7f);
            blocked.Add(new Rect(c.x - 1.5f, c.y - 1.5f, 3f, 3f));
        }

        void Sign(Vector2 at, string text)
        {
            Box(timber, at.x - 0.08f, at.y - 0.08f, 0.16f, 0.16f, 0f, 2.6f);
            Box(timber, at.x - 0.05f, at.y - 0.05f, 1.1f, 0.1f, 2.4f, 0.1f);
            Box(planks, at.x + 0.3f, at.y - 0.06f, 0.75f, 0.12f, 1.75f, 0.6f);
        }

        // ---------- Props ----------

        void BuildProps()
        {
            // Lamp posts along the roads and around the plaza.
            var lamps = new List<Vector2>();
            for (float y = area.yMin + 6f; y < area.yMax - 4f; y += 9f)
                if (Mathf.Abs(y - plaza.y) > plazaRadius + 1f) lamps.Add(new Vector2(y % 18f < 9f ? 0.2f : 4.8f, y));
            for (float x = area.xMin + 6f; x < area.xMax - 4f; x += 9f)
                if (Mathf.Abs(x - plaza.x) > plazaRadius + 1f) lamps.Add(new Vector2(x, x % 18f < 9f ? 2.2f : 6.8f));
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                lamps.Add(plaza + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (plazaRadius - 0.6f));
            }
            foreach (var p in lamps) Lamp(p);

            // Benches and crates around the plaza.
            Box(planks, plaza.x + 3.2f, plaza.y - 4.8f, 1.6f, 0.5f, 0.35f, 0.12f);
            Box(timber, plaza.x + 3.3f, plaza.y - 4.7f, 0.15f, 0.3f, 0f, 0.35f);
            Box(timber, plaza.x + 4.55f, plaza.y - 4.7f, 0.15f, 0.3f, 0f, 0.35f);
            Crate(new Vector2(-7.5f, 7.2f));
            Barrel(new Vector2(7.6f, 7.4f));
            Barrel(new Vector2(8.3f, 7.0f));

            // A fenced crop field to the south-east.
            var field = new Rect(18f, -25f, 9f, 7f);
            Fence(field, true);
            for (float x = field.xMin + 1f; x < field.xMax - 0.5f; x += 1.4f)
            for (float y = field.yMin + 0.8f; y < field.yMax - 0.5f; y += 1.2f)
                if (art != null && art.fern != null && art.fern.Length > 0) Decor(art.fern[rng.Next(art.fern.Length)], new Vector2(x, y), 0.8f, false);
            blocked.Add(field);
        }

        void Lamp(Vector2 p)
        {
            Box(iron, p.x - 0.07f, p.y - 0.07f, 0.14f, 0.14f, 0f, 2.6f);
            Box(iron, p.x - 0.22f, p.y - 0.22f, 0.44f, 0.44f, 2.6f, 0.08f);
            Box(glow, p.x - 0.16f, p.y - 0.16f, 0.32f, 0.32f, 2.68f, 0.4f);
            Box(iron, p.x - 0.24f, p.y - 0.24f, 0.48f, 0.48f, 3.08f, 0.1f);
            var holder = new GameObject("Lamp");
            holder.transform.SetParent(transform, false);
            holder.transform.position = new Vector3(p.x, p.y, -2.9f);
            PointLights.Attach(holder.transform, new Color(1f, 0.78f, 0.45f), 7f, 1.3f, Vector3.zero, true);
            Solid(p, Vector2.one * 0.3f);
        }

        void Fence(Rect r, bool gapSouth)
        {
            void Run(Vector2 a, Vector2 b, bool gap)
            {
                float len = Vector2.Distance(a, b);
                int posts = Mathf.Max(1, Mathf.RoundToInt(len / 1.2f));
                var dir = (b - a) / len;
                for (int i = 0; i <= posts; i++)
                {
                    var p = a + dir * (len * i / posts);
                    if (gap && Mathf.Abs(i - posts / 2f) < 0.6f) continue;
                    Box(timber, p.x - 0.08f, p.y - 0.08f, 0.16f, 0.16f, 0f, 0.9f);
                }
                // Two rails; a gate gap in the middle when asked.
                float mid = len / 2f;
                foreach (float z in new[] { 0.35f, 0.75f })
                {
                    if (gap)
                    {
                        RailSeg(a, dir, 0f, mid - 0.7f, z);
                        RailSeg(a, dir, mid + 0.7f, len, z);
                    }
                    else RailSeg(a, dir, 0f, len, z);
                }
                var col = new GameObject("Fence");
                col.transform.SetParent(transform, false);
                col.layer = LayerMask.NameToLayer("Walls");
                var center = (a + b) / 2f;
                col.transform.position = center;
                var bc = col.AddComponent<BoxCollider2D>();
                bc.size = Mathf.Abs(dir.x) > 0.5f ? new Vector2(len, 0.2f) : new Vector2(0.2f, len);
                if (gap) { bc.size = Mathf.Abs(dir.x) > 0.5f ? new Vector2(mid - 0.7f, 0.2f) : new Vector2(0.2f, mid - 0.7f); col.transform.position = a + dir * (mid - 0.7f) / 2f; }
            }
            var c00 = new Vector2(r.xMin, r.yMin); var c10 = new Vector2(r.xMax, r.yMin);
            var c01 = new Vector2(r.xMin, r.yMax); var c11 = new Vector2(r.xMax, r.yMax);
            Run(c00, c10, gapSouth);
            Run(c01, c11, !gapSouth);
            Run(c00, c01, false);
            Run(c10, c11, false);
        }

        void RailSeg(Vector2 a, Vector2 dir, float from, float to, float z)
        {
            if (to - from < 0.1f) return;
            var p = a + dir * from;
            float len = to - from;
            if (Mathf.Abs(dir.x) > 0.5f) Box(timber, Mathf.Min(p.x, p.x + dir.x * len), p.y - 0.04f, len, 0.08f, z, 0.1f);
            else Box(timber, p.x - 0.04f, Mathf.Min(p.y, p.y + dir.y * len), 0.08f, len, z, 0.1f);
        }

        void Barrel(Vector2 c)
        {
            Prism(planks, c, 0.32f, 8, 0f, 0.8f);
            Prism(iron, c, 0.34f, 8, 0.15f, 0.07f);
            Prism(iron, c, 0.34f, 8, 0.6f, 0.07f);
            Solid(c, Vector2.one * 0.6f);
        }

        void Crate(Vector2 c)
        {
            Box(planks, c.x - 0.35f, c.y - 0.35f, 0.7f, 0.7f, 0f, 0.7f);
            Band(timber, c.x - 0.36f, c.y - 0.36f, c.x + 0.36f, c.y + 0.36f, 0.6f, 0.1f);
            Solid(c, Vector2.one * 0.7f);
        }

        // ---------- Trees around the village ----------

        void BuildTrees()
        {
            if (art == null) return;
            int placed = 0;
            for (int tries = 0; tries < 1500 && placed < 140; tries++)
            {
                var p = new Vector2(area.xMin + (float)rng.NextDouble() * area.width, area.yMin + (float)rng.NextDouble() * area.height);
                float d = Vector2.Distance(p, plaza);
                // Sparse inside the village, thicker toward the edge.
                if (d < 14f) continue;
                if (rng.NextDouble() > Mathf.InverseLerp(14f, 40f, d) * 0.9f + 0.1f) continue;
                if (IsBlocked(p, 1.2f)) continue;
                double r = rng.NextDouble();
                var set = r < 0.45 ? art.oak : r < 0.7 ? art.pine : r < 0.85 ? art.birch : art.bush;
                if (set == null || set.Length == 0) continue;
                bool isTree = set != art.bush;
                Decor(set[rng.Next(set.Length)], p, isTree ? 1f : 0.9f, isTree);
                blocked.Add(new Rect(p.x - 1f, p.y - 1f, 2f, 2f));
                placed++;
            }
            // Bushes and ferns hugging the houses.
            for (int i = 0; i < 60; i++)
            {
                var p = new Vector2(area.xMin + (float)rng.NextDouble() * area.width, area.yMin + (float)rng.NextDouble() * area.height);
                if (Vector2.Distance(p, plaza) < plazaRadius + 1f || OnRoad(p.x, p.y)) continue;
                var set = rng.NextDouble() < 0.5 ? art.bush : art.fern;
                if (set != null && set.Length > 0) Decor(set[rng.Next(set.Length)], p, 0.8f, false);
            }
        }

        bool IsBlocked(Vector2 p, float pad)
        {
            foreach (var r in blocked)
                if (p.x > r.xMin - pad && p.x < r.xMax + pad && p.y > r.yMin - pad && p.y < r.yMax + pad) return true;
            return false;
        }

        void Decor(Sprite sprite, Vector2 pos, float scale, bool solid)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("Walls");
            var bb = Billboard.Create(go.transform, "Visual");
            bb.localScale = Vector3.one * scale * (0.9f + (float)rng.NextDouble() * 0.25f);
            var sr = bb.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            bool sways = sprite.name.StartsWith("tree") || sprite.name.StartsWith("bush") || sprite.name.StartsWith("fern") || sprite.name.StartsWith("oak") || sprite.name.StartsWith("pine") || sprite.name.StartsWith("birch");
            RenderAssets.MakeLit(sr, true, sways ? RenderAssets.Instance.litWind : null);
            if (solid) go.AddComponent<CircleCollider2D>().radius = 0.45f;
        }

        // ---------- Geometry helpers ----------

        BoxCollider2D Solid(Vector2 center, Vector2 size)
        {
            var go = new GameObject("Solid");
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            go.layer = LayerMask.NameToLayer("Walls");
            var bc = go.AddComponent<BoxCollider2D>();
            bc.size = size;
            return bc;
        }

        // An axis-aligned box standing on the ground from height z0 (up) with the given height.
        static void Box(MB mb, float x, float y, float w, float d, float z0, float h)
        {
            float za = -z0, zb = -(z0 + h);
            mb.Wall(new Vector3(x, y, za), Vector3.right, w, h, Vector3.down);
            mb.Wall(new Vector3(x + w, y + d, za), Vector3.left, w, h, Vector3.up);
            mb.Wall(new Vector3(x, y + d, za), Vector3.down, d, h, Vector3.left);
            mb.Wall(new Vector3(x + w, y, za), Vector3.up, d, h, Vector3.right);
            mb.Quad(new Vector3(x, y, zb), new Vector3(x + w, y, zb), new Vector3(x + w, y + d, zb), new Vector3(x, y + d, zb), Vector3.back,
                Vector2.zero, new Vector2(w, 0), new Vector2(w, d), new Vector2(0, d));
        }

        // A horizontal beam running around a rectangle at height z.
        static void Band(MB mb, float x0, float y0, float x1, float y1, float z, float h)
        {
            const float t = 0.06f;
            mb.Wall(new Vector3(x0 - t, y0 - t, -z), Vector3.right, x1 - x0 + 2 * t, h, Vector3.down);
            mb.Wall(new Vector3(x1 + t, y1 + t, -z), Vector3.left, x1 - x0 + 2 * t, h, Vector3.up);
            mb.Wall(new Vector3(x0 - t, y1 + t, -z), Vector3.down, y1 - y0 + 2 * t, h, Vector3.left);
            mb.Wall(new Vector3(x1 + t, y0 - t, -z), Vector3.up, y1 - y0 + 2 * t, h, Vector3.right);
        }

        static void Prism(MB mb, Vector2 c, float r, int sides, float z0, float h)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r;
                var p1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r;
                float am = (a0 + a1) / 2f;
                var n = new Vector3(Mathf.Cos(am), Mathf.Sin(am), 0f);
                float len = Vector2.Distance(p0, p1);
                mb.Wall(new Vector3(p0.x, p0.y, -z0), (Vector3)(p1 - p0) / len, len, h, n);
                mb.Tri(new Vector3(c.x, c.y, -z0 - h), new Vector3(p0.x, p0.y, -z0 - h), new Vector3(p1.x, p1.y, -z0 - h), Vector3.back);
            }
        }

        MB Roof(Color c)
        {
            if (!roofs.TryGetValue(c, out var mb)) roofs[c] = mb = new MB();
            return mb;
        }

        void Flush()
        {
            Mesh("Plaster", plaster, Mat("plaster", PlasterTex()), true);
            Mesh("Planks", planks, Mat("planks", PlankTex()), true);
            Mesh("Timber", timber, Mat("timber", TimberTex()), true);
            Mesh("Stone", stone, Mat("stone", art != null && art.brick != null ? art.brick : WorldTiles.BrickTexture()), true);
            Mesh("Iron", iron, Mat("iron", Solid(new Color(0.22f, 0.22f, 0.25f))), true);
            Mesh("Doors", door, Mat("door", DoorTex()), false);
            Mesh("Awning", cloth, Mat("awning", StripeTex()), true);
            var winMat = Mat("window", WindowTex());
            winMat.SetFloat("_Emission", 0.9f);
            Mesh("Windows", window, winMat, false);
            var glowMat = Mat("glow", Solid(new Color(1f, 0.62f, 0.28f)));
            glowMat.SetFloat("_Emission", 1.1f);
            Mesh("Glow", glow, glowMat, false);
            foreach (var kv in roofs) Mesh("Roof", kv.Value, Mat("roof" + kv.Key, RoofTex(kv.Key)), true);
        }

        static Material Mat(string key, Texture2D tex) => WorldTiles.MeshMaterial("village_" + key, tex);

        void Mesh(string name, MB q, Material mat, bool shadows)
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

        // Mesh data with world-unit UVs (textures repeat once per unit).
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            // A vertical wall from origin along a direction, rising h units (up is -Z).
            public void Wall(Vector3 o, Vector3 along, float len, float h, Vector3 normal)
            {
                Quad(o, o + along * len, o + along * len + Vector3.back * h, o + Vector3.back * h, normal,
                    new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, h), new Vector2(0, h));
            }

            // A flat panel (door, window) centered at the bottom-middle point, with 0..1 UVs.
            public void Panel(Vector3 bottomCenter, Vector3 along, float w, float h, Vector3 normal)
            {
                var o = bottomCenter - along * (w / 2f);
                Quad(o, o + along * w, o + along * w + Vector3.back * h, o + Vector3.back * h, normal,
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                for (int k = 0; k < 3; k++) n.Add(normal);
                // Planar UVs from the two largest axes.
                foreach (var p in new[] { a, b, c })
                    uv.Add(Mathf.Abs(normal.x) > 0.5f ? new Vector2(p.y, -p.z) : Mathf.Abs(normal.y) > 0.5f ? new Vector2(p.x, -p.z) : new Vector2(p.x, p.y));
                t.AddRange(new[] { i, i + 1, i + 2 });
            }

            // A thin diagonal strip on a wall (timber braces).
            public void Strip(Vector3 a, Vector3 b, float width, Vector3 normal)
            {
                var dir = (b - a).normalized;
                var side = Vector3.Cross(dir, normal).normalized * (width / 2f);
                float len = Vector3.Distance(a, b);
                Quad(a - side, a + side, b + side, b - side, normal, new Vector2(0, 0), new Vector2(width, 0), new Vector2(width, len), new Vector2(0, len));
            }
        }

        // ---------- Textures (16 px per unit, generated) ----------

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        static Texture2D Tex(string key, int w, int h, System.Func<int, int, System.Random, Color> f, bool repeat = true)
        {
            if (texCache.TryGetValue(key, out var t) && t) return t;
            t = GameAssets.NewTexture(w, h);
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            var r = new System.Random(key.GetHashCode());
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = f(x, y, r);
                c.a = 1f;
                t.SetPixel(x, y, c);
            }
            t.Apply();
            texCache[key] = t;
            return t;
        }

        static float N(System.Random r, float a) => 1f - a + (float)r.NextDouble() * a * 2f;

        static Texture2D Solid(Color c) => Tex("solid" + c, 4, 4, (x, y, r) => c * N(r, 0.05f));

        static Texture2D PlasterTex() => Tex("plaster", 16, 16, (x, y, r) =>
        {
            var c = new Color(0.86f, 0.8f, 0.68f) * N(r, 0.05f);
            if (r.NextDouble() < 0.04) c *= 0.85f;   // small cracks and stains
            return c;
        });

        static Texture2D PlankTex() => Tex("planks", 16, 16, (x, y, r) =>
        {
            int plank = x / 4;
            var c = new Color(0.55f, 0.38f, 0.24f) * (plank % 2 == 0 ? 1f : 0.88f) * N(r, 0.05f);
            if (x % 4 == 0) c *= 0.55f;                         // seams
            if ((y + plank * 5) % 16 == 0) c *= 0.7f;           // board ends
            if (r.NextDouble() < 0.03) c *= 0.75f;              // knots
            return c;
        });

        static Texture2D TimberTex() => Tex("timber", 8, 8, (x, y, r) => new Color(0.3f, 0.2f, 0.13f) * N(r, 0.08f) * (y % 4 == 0 ? 0.85f : 1f));

        static Texture2D RoofTex(Color baseC) => Tex("roof" + baseC, 16, 16, (x, y, r) =>
        {
            int row = y / 4;
            int off = row % 2 == 0 ? 0 : 2;
            var c = baseC * N(r, 0.06f);
            if (y % 4 == 0) c *= 0.6f;                          // shadow under each row
            else if ((x + off) % 4 == 0) c *= 0.75f;            // gaps between shingles
            else if (y % 4 == 3) c *= 1.12f;                    // lit top edge
            return c;
        });

        static Texture2D DoorTex() => Tex("door", 16, 24, (x, y, r) =>
        {
            var c = new Color(0.42f, 0.27f, 0.16f) * N(r, 0.05f);
            if (x < 2 || x > 13 || y > 21) return new Color(0.22f, 0.15f, 0.1f);   // frame
            if (x % 4 == 2) c *= 0.7f;
            if (y == 6 || y == 17) c = new Color(0.25f, 0.25f, 0.28f);             // iron straps
            if (x == 11 && (y == 11 || y == 12)) c = new Color(0.85f, 0.7f, 0.3f);  // handle
            return c;
        }, false);

        static Texture2D WindowTex() => Tex("window", 16, 16, (x, y, r) =>
        {
            if (x < 2 || x > 13 || y < 2 || y > 13) return new Color(0.3f, 0.2f, 0.13f);   // frame
            if (x == 7 || x == 8 || y == 7 || y == 8) return new Color(0.26f, 0.17f, 0.11f); // cross bars
            var c = Color.Lerp(new Color(1f, 0.82f, 0.45f), new Color(0.95f, 0.6f, 0.3f), y / 16f);
            if (x - y == 4 || x - y == 5) c = Color.Lerp(c, Color.white, 0.4f);              // glint
            return c;
        }, false);

        static Texture2D StripeTex() => Tex("awning", 8, 8, (x, y, r) =>
            (x / 4) % 2 == 0 ? new Color(0.75f, 0.2f, 0.18f) * N(r, 0.04f) : new Color(0.92f, 0.88f, 0.78f) * N(r, 0.04f));
    }
}
