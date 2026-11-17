using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    // Ashvale as a walled town, built at runtime when the World scene loads:
    //   - the citadel in the middle on a raised stone platform, stairs up on all four sides; you start in the plaza
    //     at the foot of its south stairs
    //   - a square town wall with a gatehouse on each of the four roads, towers on the corners, and stairs up onto
    //     the wall walk along every run of wall
    //   - four districts between the roads: the market (north-east), the arcane quarter with its tower (north-west),
    //     the inn yard (south-east) and a quiet green with a pond (south-west); houses face the roads
    // Walls, citadel and tower are textured meshes (72 px per unit like the sprites); houses, stalls, lamps and the
    // well are meshes from the Blender environment kit (EnvMesh). The farms outside the walls are Farmland's.
    public class Town : MonoBehaviour
    {
        public static bool On = true;
        public static readonly RectInt HubRect = new RectInt(-48, -48, 96, 96);
        public static readonly Vector2 Spawn = new Vector2(0f, -19.5f);   // where a new hero starts: the plaza at the citadel's feet
        static readonly Vector2 WaypointSpot = new Vector2(-12f, -16.5f);  // the town's portal, west of the citadel's stairs

        // Walls (given for the south side; the other three are the same turned round the centre).
        const float Outer = 40f, Thick = 2f, WallH = 5f;
        const float GateHalf = 2.5f, TowerH = 7.5f;
        const float RunEnd = 35.5f, GateTowerEnd = 7f;

        // Citadel.
        const float PlinthHalf = 13f, PlinthH = 2.4f;
        static readonly Rect Keep = new Rect(-8f, -2f, 16f, 12f);
        const float KeepTop = 13.4f, TurretTop = 16.6f, Turret = 3.6f;

        Solid stone, keepStone, top, roof, purpleRoof, wood, dark, cloth, glass;
        readonly List<Rect> blocked = new List<Rect>();     // where no tree or lamp may go

        public static void Build(Transform parent)
        {
            var go = new GameObject("Town of Ashvale");
            go.transform.SetParent(parent, false);
            go.AddComponent<Town>().Make();
        }

        // A spot a hero must not be put down on (inside a building or wall).
        public static bool Blocked(Vector2 p)
        {
            if (!On) return false;
            var k = Grow(Keep, Turret / 2f + 0.5f);
            if (k.Contains(p)) return true;
            float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y);
            return (ax > Outer - Thick - 0.5f && ax < Outer + 0.5f && ay > GateHalf) || (ay > Outer - Thick - 0.5f && ay < Outer + 0.5f && ax > GateHalf);
        }

        void Make()
        {
            TerrainHeight.Clear();
            stone = new Solid { topAnchoredHeight = CappedWallHeight }; keepStone = new Solid(); top = new Solid(); roof = new Solid(); purpleRoof = new Solid();
            wood = new Solid(); dark = new Solid(); cloth = new Solid(); glass = new Solid();

            PaintGround();
            for (int k = 0; k < 4; k++) WallSide(k);
            Citadel();
            ArcaneTower(new Vector2(-29f, 29f));
            Pond(new Rect(-27f, -29f, 6f, 4f));

            stone.Build(transform, "Stone", Mat("stone", CappedWall(EnvArt.Tex("stone_wall_tile"))), true);
            keepStone.Build(transform, "Keep Stone", Mat("keep", EnvArt.Tex("dgn_keep_wall")), true);
            top.Build(transform, "Paving", Mat("paving", EnvArt.Tex("cobble_tile")), true);
            roof.Build(transform, "Roofs", Mat("roof", SlateTexture(new Color32(78, 92, 122, 255))), true);
            purpleRoof.Build(transform, "Tower Roof", Mat("roof_purple", SlateTexture(new Color32(104, 70, 140, 255))), true);
            wood.Build(transform, "Wood", Mat("wood", PlankTexture()), true);
            dark.Build(transform, "Openings", Mat("dark", Flat(new Color32(22, 16, 20, 255))), false);
            cloth.Build(transform, "Banners", Mat("cloth", BannerTexture()), true);
            glass.Build(transform, "Lit Windows", Mat("glass", Flat(new Color32(255, 196, 92, 255))), false);

            Houses();
            Districts();
            Lamps();
            Greenery();
            PlaceShops();
            PlaceWaypoint();
            Guards();
            Townsfolk();
            Farmland.Build(transform.parent);
        }

        // ---------- Ground ----------

        void PaintGround()
        {
            var t = transform;
            // The two great roads: cobbles inside the walls, dirt beyond the gates.
            EnvArt.PaintGround(t, new Rect(-3.5f, -Outer + 0.5f, 7f, Outer * 2f - 1f), "cobble_tile", (x, y) => 2.4f - Mathf.Abs(x), "Road NS", -0.02f);
            EnvArt.PaintGround(t, new Rect(-Outer + 0.5f, -3.5f, Outer * 2f - 1f, 7f), "cobble_tile", (x, y) => 2.4f - Mathf.Abs(y), "Road EW", -0.025f);
            foreach (var r in new[] { new Rect(-3.5f, -48f, 7f, 8.5f), new Rect(-3.5f, 39.5f, 7f, 8.5f) })
                EnvArt.PaintGround(t, r, "dirt_tile", (x, y) => 2f - Mathf.Abs(x), "Gate Road", -0.02f);
            foreach (var r in new[] { new Rect(-48f, -3.5f, 8.5f, 7f), new Rect(39.5f, -3.5f, 8.5f, 7f) })
                EnvArt.PaintGround(t, r, "dirt_tile", (x, y) => 2f - Mathf.Abs(y), "Gate Road", -0.025f);
            // The plaza round the citadel, and the district squares.
            EnvArt.PaintGround(t, new Rect(-21f, -24f, 42f, 45f), "cobble_tile", (x, y) => 18.5f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(y + 1.5f)) + (y < -16f && Mathf.Abs(x) < 6f ? 3f : 0f), "Plaza", -0.04f);
            Square(new Vector2(25f, 25f), 8.5f, "cobble_tile", "Market");
            Square(new Vector2(-24f, 24f), 7f, "cobble_tile", "Arcane Square");
            Square(new Vector2(24f, -24f), 7f, "dirt_tile", "Inn Yard");
            blocked.Add(new Rect(-3.5f, -48f, 7f, 96f));
            blocked.Add(new Rect(-48f, -3.5f, 96f, 7f));
            blocked.Add(new Rect(-19f, -21f, 38f, 40f));
        }

        void Square(Vector2 c, float r, string tile, string name)
        {
            EnvArt.PaintGround(transform, new Rect(c.x - r - 1f, c.y - r - 1f, r * 2f + 2f, r * 2f + 2f), tile, (x, y) => r - Vector2.Distance(new Vector2(x, y), c), name, -0.035f);
            blocked.Add(new Rect(c.x - r, c.y - r, r * 2f, r * 2f));
        }

        // ---------- Walls ----------

        // One side of the wall, built as the south side turned k quarter turns round the centre.
        void WallSide(int k)
        {
            float y0 = -Outer, y1 = -Outer + Thick;
            // Two runs of wall from the corner towers to the gatehouse, each with stairs up onto its walk.
            foreach (int sign in new[] { -1, 1 })
            {
                float a = sign > 0 ? GateTowerEnd : -RunEnd, b = sign > 0 ? RunEnd : -GateTowerEnd;
                var run = R(new Rect(a, y0, b - a, Thick), k);
                Box(stone, top, run, 0f, WallH);
                for (float x = a + 0.3f; x < b - 0.6f; x += 1.3f) Box(stone, top, R(new Rect(x, y0, 0.65f, 0.5f), k), WallH, WallH + 0.75f);
                float c = (a + b) / 2f;
                var stairs = new Rect(c - 1f, y1, 2f, 7f);          // up toward the wall
                Steps(R(stairs, k), WallH, 9, RotDir(Vector2Int.down, k));
                TerrainHeight.AddFlat(run, WallH);
                TerrainHeight.AddRamp(R(stairs, k), 0f, WallH, RotDir(Vector2Int.down, k));
                Edge(k, new Vector2(c - 1f, y1 + 7f), new Vector2(c - 1f, y1), new Vector2(a, y1), new Vector2(a, y0),
                     new Vector2(b, y0), new Vector2(b, y1), new Vector2(c + 1f, y1), new Vector2(c + 1f, y1 + 7f));
                blocked.Add(R(new Rect(a, y0 - 1f, b - a, Thick + 9f), k));
            }
            // The gatehouse: a tower each side, an arch over the road, the gate leaves open against the inside.
            foreach (int sign in new[] { -1, 1 })
            {
                var t = R(new Rect(sign > 0 ? GateHalf : -GateTowerEnd, y0 - 1.5f, GateTowerEnd - GateHalf, Thick + 3f), k);
                Tower(t, TowerH, k);
            }
            Box(stone, top, R(new Rect(-GateHalf, y0, GateHalf * 2f, Thick), k), 3.8f, WallH + 0.3f);
            for (float x = -GateHalf + 0.2f; x < GateHalf - 0.5f; x += 1.3f) Box(stone, top, R(new Rect(x, y0, 0.65f, 0.5f), k), WallH + 0.3f, WallH + 1.05f);
            Box(wood, wood, R(new Rect(-GateHalf, y1, 0.2f, 2.1f), k), 0f, 3.6f);
            Box(wood, wood, R(new Rect(GateHalf - 0.2f, y1, 0.2f, 2.1f), k), 0f, 3.6f);
            // The corner tower at this side's right-hand end.
            Tower(R(new Rect(RunEnd, -Outer - 2.5f, 7f, 7f), k), TowerH + 1f, k, true);
            blocked.Add(R(new Rect(-GateTowerEnd - 1f, y0 - 3f, GateTowerEnd * 2f + 2f, Thick + 6f), k));
        }

        void Tower(Rect t, float h, int k, bool roofed = false)
        {
            Box(stone, top, t, 0f, h);
            Box(stone, top, Grow(t, 0.2f), h - 0.45f, h);
            if (roofed) Pyramid(roof, Grow(t, 0.4f), h, 3.4f);
            else Crenels(stone, top, Grow(t, 0.2f), h, 0.75f, 0.45f);
            // Arrow slits on the outer face.
            var outward = RotDir(Vector2Int.down, k);
            var c = t.center;
            Vector2 face = c + new Vector2(outward.x * (t.width / 2f + 0.02f), outward.y * (t.height / 2f + 0.02f));
            Vector2 along = new Vector2(-outward.y, outward.x) * 0.18f;
            foreach (float z in new[] { h * 0.35f, h * 0.65f }) FaceQuad(dark, face - along, face + along, z, z + 1.1f, new Vector3(outward.x, outward.y, 0f));
            Block(t);
        }

        // ---------- Citadel ----------

        void Citadel()
        {
            var plinth = new Rect(-PlinthHalf, -PlinthHalf, PlinthHalf * 2f, PlinthHalf * 2f);
            Box(stone, top, plinth, 0f, PlinthH);
            TerrainHeight.AddFlat(plinth, PlinthH);
            for (int k = 0; k < 4; k++)
            {
                // Stairs up the middle of each side, and a low parapet either side of them.
                var stairs = new Rect(-2f, -PlinthHalf - 3.5f, 4f, 3.5f);
                Steps(R(stairs, k), PlinthH, 6, RotDir(Vector2Int.up, k));
                TerrainHeight.AddRamp(R(stairs, k), 0f, PlinthH, RotDir(Vector2Int.up, k));
                for (float x = -PlinthHalf + 0.4f; x < PlinthHalf - 0.6f; x += 1.5f)
                    if (x + 0.7f < -2.8f || x > 2.8f) Box(stone, top, R(new Rect(x, -PlinthHalf, 0.7f, 0.4f), k), PlinthH, PlinthH + 0.45f);
                Edge(k, new Vector2(-2f, -PlinthHalf - 3.5f), new Vector2(-2f, -PlinthHalf), new Vector2(-PlinthHalf, -PlinthHalf));
                Edge(k, new Vector2(2f, -PlinthHalf - 3.5f), new Vector2(2f, -PlinthHalf), new Vector2(PlinthHalf, -PlinthHalf));
            }

            // The keep, its corner turrets with pointed slate roofs, and a tall roof behind the battlements.
            Box(keepStone, top, Keep, PlinthH, KeepTop);
            Crenels(keepStone, top, Keep, KeepTop, 0.8f, 0.5f);
            Box(stone, top, Grow(Keep, 0.25f), PlinthH, PlinthH + 0.6f);
            Box(keepStone, top, Grow(Keep, 0.12f), 7.6f, 8.0f);
            foreach (var c in new[] { new Vector2(Keep.xMin, Keep.yMin), new Vector2(Keep.xMax, Keep.yMin), new Vector2(Keep.xMin, Keep.yMax), new Vector2(Keep.xMax, Keep.yMax) })
            {
                var t = new Rect(c.x - Turret / 2f, c.y - Turret / 2f, Turret, Turret);
                Box(keepStone, top, t, PlinthH, TurretTop);
                Box(stone, top, Grow(t, 0.2f), TurretTop - 0.5f, TurretTop);
                Pyramid(roof, Grow(t, 0.35f), TurretTop, 3.6f);
                FaceQuad(dark, new Vector2(c.x - 0.18f, t.yMin - 0.02f), new Vector2(c.x + 0.18f, t.yMin - 0.02f), 6f, 7.1f, Vector3.down);
                FaceQuad(dark, new Vector2(c.x - 0.18f, t.yMin - 0.02f), new Vector2(c.x + 0.18f, t.yMin - 0.02f), 11f, 12.1f, Vector3.down);
            }
            Pyramid(roof, new Rect(Keep.xMin + 2.4f, Keep.yMin + 2.4f, Keep.width - 4.8f, Keep.height - 4.8f), KeepTop, 4.2f);

            // The great door, banners either side, lit windows above.
            float y = Keep.yMin - 0.02f;
            FaceQuad(dark, new Vector2(-1.7f, y), new Vector2(1.7f, y), PlinthH, PlinthH + 4.2f, Vector3.down);
            FaceQuad(wood, new Vector2(-1.4f, y - 0.01f), new Vector2(1.4f, y - 0.01f), PlinthH, PlinthH + 3.8f, Vector3.down);
            Box(stone, top, new Rect(-2.4f, Keep.yMin - 1f, 4.8f, 1f), PlinthH, PlinthH + 0.25f);
            foreach (float wx in new[] { -4.6f, 4.6f })
            {
                FaceQuad(dark, new Vector2(wx - 0.4f, y), new Vector2(wx + 0.4f, y), 9.4f, 10.9f, Vector3.down);
                FaceQuad(glass, new Vector2(wx - 0.3f, y - 0.01f), new Vector2(wx + 0.3f, y - 0.01f), 9.5f, 10.8f, Vector3.down);
                FaceQuad(dark, new Vector2(wx - 0.4f, y), new Vector2(wx + 0.4f, y), 4.4f, 5.8f, Vector3.down);
            }
            FaceQuad(cloth, new Vector2(-3.2f, y - 0.03f), new Vector2(-2f, y - 0.03f), 5.4f, 10.4f, Vector3.down);
            FaceQuad(cloth, new Vector2(2f, y - 0.03f), new Vector2(3.2f, y - 0.03f), 5.4f, 10.4f, Vector3.down);
            Block(Grow(Keep, Turret / 2f));

            // The courtyard in front of the keep.
            EnvMesh.Place("well", transform, new Vector2(-7f, -8f), PlinthH);
            foreach (var p in new[] { new Vector2(-4f, -12f), new Vector2(4f, -12f), new Vector2(-11.5f, -3f), new Vector2(11.5f, -3f) })
                Lamp(p, PlinthH);
            blocked.Add(Grow(plinth, 4f));
        }

        // The arcane quarter's tower: a slim stone tower with a tall violet roof.
        void ArcaneTower(Vector2 c)
        {
            var t = new Rect(c.x - 2.8f, c.y - 2.8f, 5.6f, 5.6f);
            Box(keepStone, top, t, 0f, 12f);
            Box(stone, top, Grow(t, 0.25f), 0f, 0.7f);
            Box(stone, top, Grow(t, 0.3f), 11.4f, 12f);
            Pyramid(purpleRoof, Grow(t, 0.55f), 12f, 6.5f);
            float y = t.yMin - 0.02f;
            FaceQuad(dark, new Vector2(c.x - 0.9f, y), new Vector2(c.x + 0.9f, y), 0f, 2.6f, Vector3.down);
            FaceQuad(wood, new Vector2(c.x - 0.75f, y - 0.01f), new Vector2(c.x + 0.75f, y - 0.01f), 0f, 2.4f, Vector3.down);
            foreach (float z in new[] { 5f, 8.5f })
            {
                FaceQuad(dark, new Vector2(c.x - 0.4f, y), new Vector2(c.x + 0.4f, y), z, z + 1.4f, Vector3.down);
                FaceQuad(glass, new Vector2(c.x - 0.3f, y - 0.01f), new Vector2(c.x + 0.3f, y - 0.01f), z + 0.1f, z + 1.3f, Vector3.down);
            }
            Block(t);
            blocked.Add(Grow(t, 2f));
            var light = new GameObject("Tower Glow");
            light.transform.SetParent(transform, false);
            light.transform.position = new Vector3(c.x, 6f, c.y - 3.2f);
            var pl = PointLights.Attach(light.transform, new Color(0.7f, 0.5f, 1f), 9f, 1.2f, Vector3.zero, true);
            if (pl != null) light.AddComponent<NightLight>().target = pl;
        }

        void Pond(Rect p)
        {
            var q = new Solid();
            q.Quad(new Vector3(p.xMin, p.yMin, 0.1f), new Vector3(p.xMax, p.yMin, 0.1f), new Vector3(p.xMax, p.yMax, 0.1f), new Vector3(p.xMin, p.yMax, 0.1f), Vector3.back,
                new Vector2(p.xMin, p.yMin), new Vector2(p.xMax, p.yMin), new Vector2(p.xMax, p.yMax), new Vector2(p.xMin, p.yMax));
            var m = new Material(RenderAssets.Instance.water);
            m.SetColor("_Deep", new Color(0.14f, 0.5f, 0.74f));
            m.SetColor("_Shallow", new Color(0.36f, 0.78f, 0.9f));
            q.Build(transform, "Pond", m, false);
            foreach (var s in new[] { new Rect(p.xMin - 0.4f, p.yMin - 0.4f, p.width + 0.8f, 0.4f), new Rect(p.xMin - 0.4f, p.yMax, p.width + 0.8f, 0.4f),
                                      new Rect(p.xMin - 0.4f, p.yMin, 0.4f, p.height), new Rect(p.xMax, p.yMin, 0.4f, p.height) })
                Box(stone, top, s, 0f, 0.25f);
            Block(p);
            blocked.Add(Grow(p, 1.5f));
        }

        // ---------- Houses, shops, lamps, trees ----------

        void House(string art, Vector2 p, float yaw)
        {
            var go = EnvMesh.Place(art, transform, p, 0f, yaw) ?? EnvArt.Place(art, transform, p);
            blocked.Add(new Rect(p.x - 5.5f, p.y - 5.5f, 11f, 11f));
        }

        void Houses()
        {
            // Along the four roads, doors to the road.
            House("timber_house", new Vector2(8.6f, 29f), -90f);
            House("slate_house", new Vector2(-8.6f, 29f), 90f);
            House("red_cottage", new Vector2(8.6f, -29f), -90f);
            House("moss_cottage", new Vector2(-8.6f, -29f), 90f);
            House("thatch_cottage", new Vector2(29f, 8.6f), 0f);
            House("slate_house", new Vector2(29f, -8.6f), 180f);
            House("red_cottage", new Vector2(-29f, 8.6f), 0f);
            House("timber_house", new Vector2(-29f, -8.6f), 180f);
            // In the districts.
            House("inn", new Vector2(27f, -28.5f), 0f);
            House("moss_cottage", new Vector2(-30f, -20.5f), 90f);
            House("thatch_cottage", new Vector2(31.5f, 31f), 180f);
        }

        void Districts()
        {
            // The market: stalls round the square, a well in the middle.
            Prop("stall_produce", new Vector2(20.5f, 29.5f), 0f);
            Prop("stall_weapons", new Vector2(29.5f, 20f), 90f);
            Prop("stall_potions", new Vector2(24f, 31f), 180f);
            EnvMesh.Place("well", transform, new Vector2(25f, 24f));
            // The arcane quarter.
            Prop("stall_potions", new Vector2(-21f, 20.5f), 0f);
            // The inn yard and the mapmaker.
            Prop("stall_maps", new Vector2(21f, -20.5f), 0f);
            Prop("haystack", new Vector2(31f, -22f), 0f);
            // The green.
            Prop("scarecrow", new Vector2(-33f, -33f), 30f);
        }

        void Prop(string art, Vector2 p, float yaw)
        {
            EnvMesh.Place(art, transform, p, 0f, yaw);
            blocked.Add(new Rect(p.x - 2.5f, p.y - 2.5f, 5f, 5f));
        }

        void Lamps()
        {
            for (int k = 0; k < 4; k++)
                foreach (float t in new[] { 24f, 31f, 37f })
                    Lamp(Rot(new Vector2((t % 2f < 1f ? 3.2f : -3.2f), -t), k), 0f);
            foreach (var c in new[] { new Vector2(21f, 21f), new Vector2(-21f, 21f), new Vector2(21f, -21f), new Vector2(-21f, -21f) }) Lamp(c, 0f);
            foreach (var c in new[] { new Vector2(25f, 33.5f), new Vector2(17f, 25f), new Vector2(-24f, 17f), new Vector2(17f, -24f) }) Lamp(c, 0f);
        }

        void Lamp(Vector2 p, float height)
        {
            EnvArt.Place("lamp", transform, p, height);
            var holder = new GameObject("Lamp Light");
            holder.transform.SetParent(transform, false);
            holder.transform.position = p.W(3.6f + height);
            var light = PointLights.Attach(holder.transform, new Color(1f, 0.78f, 0.45f), 7f, 1.3f, Vector3.zero, true);
            if (light != null) holder.AddComponent<NightLight>().target = light;
        }

        void Greenery()
        {
            var rng = new System.Random(1717);
            int trees = 0, bushes = 0;
            for (int t = 0; t < 4000 && (trees < 70 || bushes < 90); t++)
            {
                var p = new Vector2(-47f + (float)rng.NextDouble() * 94f, -47f + (float)rng.NextDouble() * 94f);
                bool inside = Mathf.Abs(p.x) < Outer - Thick - 1f && Mathf.Abs(p.y) < Outer - Thick - 1f;
                bool outside = Mathf.Abs(p.x) > Outer + 2f || Mathf.Abs(p.y) > Outer + 2f;
                if (!inside && !outside) continue;
                if (Inside(p, 0.8f)) continue;
                if (trees < 70 && rng.NextDouble() < 0.45)
                {
                    EnvArt.PlaceTree(rng.NextDouble() < 0.5 ? "oak" : "oak2", transform, p);
                    blocked.Add(new Rect(p.x - 2f, p.y - 2f, 4f, 4f));
                    trees++;
                }
                else if (bushes < 90)
                {
                    var bush = EnvArt.PlaceTree("bush", transform, p);
                    if (bush != null) Destroy(bush.GetComponent<Collider>());
                    blocked.Add(new Rect(p.x - 0.7f, p.y - 0.7f, 1.4f, 1.4f));
                    bushes++;
                }
            }
        }

        bool Inside(Vector2 p, float pad)
        {
            foreach (var r in blocked)
                if (p.x > r.xMin - pad && p.x < r.xMax + pad && p.y > r.yMin - pad && p.y < r.yMax + pad) return true;
            return false;
        }

        // The shopkeepers move to their districts.
        void PlaceShops()
        {
            var spots = new Dictionary<string, Vector2>
            {
                { "Merchant", new Vector2(20.5f, 27.6f) },
                { "Weaponmaster", new Vector2(27.6f, 20f) },
                { "Arcane Forge", new Vector2(-21f, 18.6f) },
                { "Mystic", new Vector2(-29f, 24.6f) },
                { "Cartographer", new Vector2(21f, -22.4f) },
            };
            foreach (var m in FindObjectsByType<Merchant>())
                if (spots.TryGetValue(m.displayName, out var p) || spots.TryGetValue(m.name, out p))
                    m.transform.position = p.W(m.transform.position.y);
        }

        void PlaceWaypoint()
        {
            // Off to the side of the plaza, clear of the citadel's stairs: the portal arch is tall and wide.
            foreach (var w in FindObjectsByType<Waypoint>())
                if (w.id == "village") w.transform.position = WaypointSpot.W(0f);
            var wm = FindAnyObjectByType<WorldManager>();
            if (wm != null) wm.villageWaypoint = WaypointSpot;
        }

        // Two guards at each gate, looking out.
        void Guards()
        {
            string[] looks = { "guard", "m_guard" };
            for (int k = 0; k < 4; k++)
                for (int i = 0; i < 2; i++)
                {
                    var p = Rot(new Vector2(i == 0 ? -3.9f : 3.9f, -Outer + Thick + 1.2f), k);
                    var go = new GameObject("Gate Guard");
                    go.transform.SetParent(transform, false);
                    go.transform.position = p.W();
                    var visual = Billboard.Create(go.transform, "Visual");
                    var sr = visual.gameObject.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = SortOrder.Units;
                    RenderAssets.MakeLit(sr);
                    var npc = visual.gameObject.AddComponent<EnvNpcSprite>();
                    npc.Init(looks[(i + k) % 2], sr);
                    var o = RotDir(Vector2Int.down, k);
                    npc.Facing = new Vector2(o.x, o.y);
                }
        }

        // Townsfolk walk the roads, the plaza and the district squares.
        void Townsfolk()
        {
            var nodes = new List<Vector2>();
            var links = new List<List<int>>();
            int Add(Vector2 p) { nodes.Add(p); links.Add(new List<int>()); return nodes.Count - 1; }
            void Link(int a, int b) { links[a].Add(b); links[b].Add(a); }
            var ring = new int[8];
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f - Mathf.PI / 2f;
                ring[i] = Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? 18f : 25f));
            }
            for (int i = 0; i < 8; i++) Link(ring[i], ring[(i + 1) % 8]);
            for (int k = 0; k < 4; k++)
            {
                int a = Add(Rot(new Vector2(0f, -27f), k)), b = Add(Rot(new Vector2(0f, -35f), k));
                Link(ring[k * 2], a); Link(a, b);
            }
            EnvVillageLife.OverrideNodes = nodes;
            EnvVillageLife.OverrideLinks = links;
            new GameObject("Town Life").AddComponent<EnvVillageLife>();
        }

        // ---------- Geometry ----------

        static Vector2 Rot(Vector2 p, int k)
        {
            for (int i = 0; i < (k & 3); i++) p = new Vector2(-p.y, p.x);
            return p;
        }

        static Vector2Int RotDir(Vector2Int d, int k)
        {
            for (int i = 0; i < (k & 3); i++) d = new Vector2Int(-d.y, d.x);
            return d;
        }

        static Rect R(Rect r, int k)
        {
            var a = Rot(r.min, k); var b = Rot(r.max, k);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static Rect Grow(Rect r, float by) => new Rect(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);

        public static void Box(Solid sides, Solid lid, Rect r, float h0, float h1)
        {
            float x0 = r.xMin, x1 = r.xMax, y0 = r.yMin, y1 = r.yMax, z0 = -h0, z1 = -h1;
            float th = sides.topAnchoredHeight;
            Vector2 Side(float along, float h) => th > 0f ? new Vector2(along / 4f, 1f - (h1 - h) / th) : Town.Side(along, h);
            sides.Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down,
                Side(x0, h0), Side(x1, h0), Side(x1, h1), Side(x0, h1));
            sides.Quad(new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), Vector3.up,
                Side(-x1, h0), Side(-x0, h0), Side(-x0, h1), Side(-x1, h1));
            sides.Quad(new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), Vector3.left,
                Side(-y1, h0), Side(-y0, h0), Side(-y0, h1), Side(-y1, h1));
            sides.Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right,
                Side(y0, h0), Side(y1, h0), Side(y1, h1), Side(y0, h1));
            lid.Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.back,
                new Vector2(x0, y0) / 4f, new Vector2(x1, y0) / 4f, new Vector2(x1, y1) / 4f, new Vector2(x0, y1) / 4f);
        }

        static Vector2 Side(float along, float h) => new Vector2(along / 4f, h / 1.5f);

        public static void Crenels(Solid s, Solid lid, Rect r, float h, float tall, float deep)
        {
            const float step = 1.3f, w = 0.65f;
            for (float x = r.xMin; x < r.xMax - w * 0.5f; x += step)
            {
                float xe = Mathf.Min(x + w, r.xMax);
                Box(s, lid, new Rect(x, r.yMin, xe - x, deep), h, h + tall);
                Box(s, lid, new Rect(x, r.yMax - deep, xe - x, deep), h, h + tall);
            }
            for (float y = r.yMin + step; y < r.yMax - step * 0.5f; y += step)
            {
                Box(s, lid, new Rect(r.xMin, y, deep, w), h, h + tall);
                Box(s, lid, new Rect(r.xMax - deep, y, deep, w), h, h + tall);
            }
        }

        static void Pyramid(Solid s, Rect r, float h, float rise)
        {
            var c = new Vector3(r.center.x, r.center.y, -h - rise);
            float z = -h;
            var sw = new Vector3(r.xMin, r.yMin, z); var se = new Vector3(r.xMax, r.yMin, z);
            var ne = new Vector3(r.xMax, r.yMax, z); var nw = new Vector3(r.xMin, r.yMax, z);
            float slopeY = Mathf.Sqrt(rise * rise + r.height * r.height / 4f), slopeX = Mathf.Sqrt(rise * rise + r.width * r.width / 4f);
            void Face(Vector3 a, Vector3 b, Vector3 n, float width, float slope) =>
                s.Tri(a, b, c, n.normalized, new Vector2(0f, 0f), new Vector2(width / 4f, 0f), new Vector2(width / 8f, slope / 4f));
            Face(sw, se, new Vector3(0f, -rise, -r.height / 2f), r.width, slopeY);
            Face(se, ne, new Vector3(rise, 0f, -r.width / 2f), r.height, slopeX);
            Face(ne, nw, new Vector3(0f, rise, -r.height / 2f), r.width, slopeY);
            Face(nw, sw, new Vector3(-rise, 0f, -r.width / 2f), r.height, slopeX);
        }

        // Stone steps over r rising to h toward dir.
        void Steps(Rect r, float h, int n, Vector2Int dir)
        {
            float rise = h / n;
            for (int i = 0; i < n; i++)
            {
                float f = (float)i / n;
                Rect step;
                if (dir.y > 0) step = new Rect(r.xMin, r.yMin + r.height * f, r.width, r.height * (1f - f));
                else if (dir.y < 0) step = new Rect(r.xMin, r.yMin, r.width, r.height * (1f - f));
                else if (dir.x > 0) step = new Rect(r.xMin + r.width * f, r.yMin, r.width * (1f - f), r.height);
                else step = new Rect(r.xMin, r.yMin, r.width * (1f - f), r.height);
                Box(keepStone, top, step, 0f, (i + 1) * rise);
            }
        }

        // A flat quad standing on the ground line a-b, facing normal.
        static void FaceQuad(Solid s, Vector2 a, Vector2 b, float h0, float h1, Vector3 normal)
        {
            float w = Vector2.Distance(a, b);
            s.Quad(new Vector3(a.x, a.y, -h0), new Vector3(b.x, b.y, -h0), new Vector3(b.x, b.y, -h1), new Vector3(a.x, a.y, -h1), normal,
                new Vector2(0f, 0f), new Vector2(w / 4f, 0f), new Vector2(w / 4f, (h1 - h0) / 4f), new Vector2(0f, (h1 - h0) / 4f));
        }

        void Block(Rect r)
        {
            var go = new GameObject("Block");
            go.transform.SetParent(transform, false);
            go.layer = LayerMask.NameToLayer("Walls");
            Phys.Box(go, r.size, r.center);
        }

        // The rim of a raised level, given for the south side and turned k quarter turns.
        void Edge(int k, params Vector2[] points)
        {
            var go = new GameObject("Level Edge");
            go.transform.SetParent(transform, false);
            go.layer = LayerMask.NameToLayer("Walls");
            for (int i = 0; i < points.Length; i++) points[i] = Rot(points[i], k);
            Phys.Edge(go, points);
        }

        // ---------- Materials and textures (72 px per unit, like the sprites) ----------

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static Material Mat(string key, Texture2D tex)
        {
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            m = new Material(WorldTiles.MeshMaterial("town_" + key, tex)) { name = "Town " + key };
            m.SetFloat("_Cull", (float)CullMode.Back);
            if (key != "dark" && key != "glass" && key != "cloth") m.SetFloat("_Relief", 1f);
            mats[key] = m;
            return m;
        }

        const float CappedWallHeight = 8f;   // world units the capped wall texture covers, top to bottom

        // The stone wall tile (4 x 1.5 units, moss along its top) extended downward with plain brick courses to
        // CappedWallHeight: one texture covers a whole wall, the moss only at its crown. Every other course is shifted
        // half a brick so the bond keeps running.
        public static Texture2D CappedWall(Texture2D src)
        {
            if (src == null) return null;
            const int CourseTop = 82, Course = 26;               // the moss-free course at the foot of the tile (rows from the top)
            int w = src.width, sh = src.height, h = Mathf.RoundToInt(CappedWallHeight * sh / 1.5f);
            var s = src.GetPixels32();
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)                          // y counts rows from the top, like the source rows
            {
                int below = y - sh;
                for (int x = 0; x < w; x++)
                {
                    Color32 c;
                    if (below < 0) c = s[(sh - 1 - y) * w + x];
                    else
                    {
                        int k = below / Course, ry = CourseTop + below % Course;
                        int sx = (x + (k % 2 == 0 ? w / 12 : 0)) % w;
                        c = s[(sh - 1 - ry) * w + sx];
                    }
                    px[(h - 1 - y) * w + x] = c;
                }
            }
            var t = NewTex("TownCappedWall", w, h);
            t.SetPixels32(px); t.Apply();
            return t;
        }

        public static Texture2D NewTex(string name, int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };

        static Texture2D Flat(Color32 c)
        {
            var t = NewTex("TownFlat", 4, 4);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = c;
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Color32 Jit(System.Random rng, Color32 c, float amount)
        {
            float k = 1f + ((float)rng.NextDouble() * 2f - 1f) * amount;
            return new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), 255);
        }

        static Texture2D SlateTexture(Color32 baseC)
        {
            const int N = 288, rowH = 18, shW = 24;
            var t = NewTex("TownSlate", N, N);
            var px = new Color32[N * N];
            var rng = new System.Random(7);
            int cols = N / shW + 1;
            var shade = new Color32[(N / rowH) * cols];
            for (int i = 0; i < shade.Length; i++) shade[i] = Jit(rng, baseC, 0.12f);
            var gap = new Color32((byte)(baseC.r * 0.42f), (byte)(baseC.g * 0.42f), (byte)(baseC.b * 0.45f), 255);
            var seam = new Color32((byte)(baseC.r * 0.62f), (byte)(baseC.g * 0.62f), (byte)(baseC.b * 0.64f), 255);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int row = y / rowH, off = row % 2 == 0 ? 0 : shW / 2;
                int col = ((x + off) / shW) % cols;
                int ry = y % rowH, rx = (x + off) % shW;
                Color32 c = shade[row * cols + col];
                if (ry < 2) c = gap;
                else if (rx == 0) c = seam;
                else if (ry > rowH - 4) c = new Color32((byte)Mathf.Min(255, c.r + 22), (byte)Mathf.Min(255, c.g + 24), (byte)Mathf.Min(255, c.b + 26), 255);
                px[y * N + x] = c;
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        public static Texture2D PlankTexture()
        {
            const int N = 288, pw = 20;
            var t = NewTex("TownPlanks", N, N);
            var px = new Color32[N * N];
            var rng = new System.Random(11);
            var cols = new Color32[N / pw + 1];
            for (int i = 0; i < cols.Length; i++) cols[i] = Jit(rng, new Color32(122, 74, 40, 255), 0.12f);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var c = cols[x / pw];
                if (x % pw == 0) c = new Color32(58, 32, 20, 255);
                int band = y % 144;
                if (band > 40 && band < 50) c = band == 41 || band == 49 ? new Color32(32, 30, 36, 255) : new Color32(70, 70, 80, 255);
                px[y * N + x] = c;
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D BannerTexture()
        {
            const int W = 72, H = 288;
            var t = NewTex("TownBanner", W, H);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[W * H];
            var red = new Color32(150, 34, 40, 255); var redD = new Color32(110, 22, 32, 255); var gold = new Color32(232, 180, 64, 255);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color32 c = (x / 6) % 3 == 0 ? redD : red;
                if (x < 5 || x >= W - 5) c = gold;
                int cy = 150 - Mathf.Abs(x - W / 2);
                if (y > cy && y < cy + 12) c = gold;
                if (y > H - 10) c = new Color32(70, 46, 30, 255);
                px[y * W + x] = c;
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        // ---------- Mesh builder: one-sided faces, wound from the side their normal points to ----------

        public class Solid
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();
            // When set: side faces map the texture from each block's top down, this many units tall (a crown that
            // belongs on top of the wall, like moss, stays at the top instead of repeating every tile).
            public float topAnchoredHeight;

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), normal) > 0f) t.AddRange(new[] { i, i + 1, i + 3, i + 1, i + 2, i + 3 });
                else t.AddRange(new[] { i, i + 3, i + 1, i + 1, i + 3, i + 2 });
            }

            // The same, with a normal at each corner (smooth shading across a surface built of many quads).
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                n.Add(na); n.Add(nb); n.Add(nc); n.Add(nd);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), na + nb + nc + nd) > 0f) t.AddRange(new[] { i, i + 1, i + 3, i + 1, i + 2, i + 3 });
                else t.AddRange(new[] { i, i + 3, i + 1, i + 1, i + 3, i + 2 });
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                for (int k = 0; k < 3; k++) n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f) t.AddRange(new[] { i, i + 1, i + 2 });
                else t.AddRange(new[] { i, i + 2, i + 1 });
            }

            public GameObject Build(Transform parent, string name, Material mat, bool shadows)
            {
                if (v.Count == 0) return null;
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
                var colors = new Color[v.Count];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                m.colors = colors;
                m.SetTriangles(t, 0);
                Ground.ToY(m);   // built with the ground on XY and up -Z; the world is Y-up
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
                return go;
            }
        }
    }
}
