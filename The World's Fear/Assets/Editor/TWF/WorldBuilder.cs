using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace TWF.EditorTools
{
    // Builds Assets/Scenes/World.unity: the original village as a hub with four zones around it.
    // The layout comes from a fixed seed, so rebuilding gives the same map.
    public static class WorldBuilder
    {
        const int Seed = 20261028;
        const int ZoneSize = 110;
        const int GateWidth = 8;
        const string ForestTiles = "Assets/Plugins/Tiny RPG Forest/Artwork/Environment/";
        const string DecorDir = "Assets/Content/Decor";
        const string TileDir = "Assets/Content/Tiles";

        static readonly int[] grassPlain = { 222, 223, 226, 299, 351, 360, 396, 399 };
        static readonly int[] grassDetail = { 224, 225, 234, 237, 311, 331, 243, 289 };
        static readonly int[] dirt = { 317, 318, 336, 337 };

        class Zone
        {
            public ZoneDefinition def;
            public RectInt rect;
            public bool[,] blocked;
            public bool[,] path;
            public List<Vector2Int> gates = new List<Vector2Int>();
            public Vector2Int entry;
            public Vector2Int waypoint;
            public Transform root;
            public Tilemap ground, paths, obstacles, detail;
        }

        static System.Random rng;
        static Dictionary<string, Sprite> decor;
        static Dictionary<string, TileBase> genTiles;

        [MenuItem("TWF/Build World")]
        public static void Build()
        {
            rng = new System.Random(Seed);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.06f, 0.05f);
            camGo.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<CameraFollow>();
            var game = new GameObject("Game");
            game.AddComponent<WorldManager>();
            game.AddComponent<GameBootstrap>();

            // Ashvale, the village at the center of the infinite world, is built at runtime by Village.
            var hub = new RectInt(-45, -30, 90, 90);
            PrepareDecorSprites();
            PrepareGeneratedTiles();
            BuildWorldTilesAsset();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/Resources/TWF/GameDatabase.asset");

            var hubRoot = new GameObject("Hub").AddComponent<Grid>();
            var village = new GameObject("Village").AddComponent<Village>();
            village.transform.SetParent(hubRoot.transform, false);
            village.area = hub;
            AddWaypoint(hubRoot.transform, "village", "Ashvale Village", 0, new Vector2(2f, 3f));
            // The merchant stands at the market stall, the forge keeper at the smithy.
            var merchant = new GameObject("Merchant").AddComponent<Merchant>();
            merchant.transform.SetParent(hubRoot.transform, false);
            merchant.transform.position = new Vector2(-4.5f, 8.3f);
            var forge = new GameObject("Arcane Forge").AddComponent<Merchant>();
            forge.transform.SetParent(hubRoot.transform, false);
            forge.transform.position = new Vector2(8.2f, 8.1f);
            // The weaponmaster beside the smithy, the cartographer by the well.
            var weaponmaster = new GameObject("Weaponmaster").AddComponent<Merchant>();
            weaponmaster.transform.SetParent(hubRoot.transform, false);
            weaponmaster.transform.position = new Vector2(12.6f, 8.0f);
            weaponmaster.displayName = "Weaponmaster";
            weaponmaster.subtitle = "Weapon skills";
            weaponmaster.buttonLabel = "Train";
            weaponmaster.panel = PanelKind.Weaponmaster;
            weaponmaster.outfit = Outfit.Warrior;
            var cartographer = new GameObject("Cartographer").AddComponent<Merchant>();
            cartographer.transform.SetParent(hubRoot.transform, false);
            cartographer.transform.position = new Vector2(-4.4f, 0.4f);
            cartographer.displayName = "Cartographer";
            cartographer.subtitle = "Boss lair maps";
            cartographer.buttonLabel = "Maps";
            cartographer.panel = PanelKind.Cartographer;
            cartographer.outfit = Outfit.Mage;
            // The mystic reforges item properties, outside the inn.
            var mystic = new GameObject("Mystic").AddComponent<Merchant>();
            mystic.transform.SetParent(hubRoot.transform, false);
            mystic.transform.position = new Vector2(-8.4f, 7.4f);
            mystic.displayName = "Mystic";
            mystic.subtitle = "Reforge items";
            mystic.buttonLabel = "Reforge";
            mystic.panel = PanelKind.Reforge;
            mystic.outfit = Outfit.Ranger;
            forge.displayName = "Arcane Forge";
            forge.subtitle = "Craft skills";
            forge.buttonLabel = "Forge";
            forge.panel = PanelKind.Craft;
            forge.outfit = Outfit.Smith;

            var world = new GameObject("Infinite World").AddComponent<ChunkWorld>();
            world.hubRect = hub;

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/World.unity");
            AddToBuildSettings("Assets/Scenes/World.unity");
            Debug.Log($"World built. Hub {hub}");
        }

        static void BuildWorldTilesAsset()
        {
            const string path = "Assets/Resources/TWF/WorldTiles.asset";
            var wt = AssetDatabase.LoadAssetAtPath<WorldTiles>(path);
            if (wt == null)
            {
                wt = ScriptableObject.CreateInstance<WorldTiles>();
                AssetDatabase.CreateAsset(wt, path);
            }
            TileBase[] F(int[] ids) => System.Array.ConvertAll(ids, ForestTile);
            wt.grassPlain = F(grassPlain);
            wt.grassDetail = F(grassDetail);
            wt.dirt = F(dirt);
            wt.ruinsFloor = new[] { genTiles["ruins_floor0"], genTiles["ruins_floor1"], genTiles["ruins_floor2"], genTiles["ruins_floor3"] };
            wt.ruinsPath = genTiles["ruins_path"];
            wt.ash = new[] { genTiles["ash0"], genTiles["ash1"], genTiles["ash2"] };
            wt.emberPath = genTiles["ember_path"];
            wt.emberCrack = new[] { genTiles["ember_crack0"], genTiles["ember_crack1"] };
            wt.ruinsRubble = genTiles["ruins_rubble"];
            wt.blocker = genTiles["blocker"];
            wt.treeOrange = decor["tree-orange"];
            wt.treePink = decor["tree-pink"];
            wt.treeDried = decor["tree-dried"];
            wt.trunk = decor["trunk"];
            wt.rock = decor["rock"];
            wt.rockMonument = decor["rock-monument"];
            wt.bush = decor["bush"];
            wt.bushTall = decor["bush-tall"];
            EditorUtility.SetDirty(wt);
            AssetDatabase.SaveAssets();
        }

        static Vector2 Center(Vector2Int cell) => new Vector2(cell.x + 0.5f, cell.y + 0.5f);

        // ---------- Village hub ----------

        static RectInt CopyVillage()
        {
            var village = EditorSceneManager.OpenScene("Assets/Scenes/Village.unity", OpenSceneMode.Additive);
            RectInt bounds = new RectInt(-40, -30, 80, 90);
            foreach (var root in village.GetRootGameObjects())
            {
                // Keep the village's own wall colliders and the big rock.
                if (root.name == "BigRock")
                {
                    var walls = Object.Instantiate(root);
                    walls.name = "Village " + root.name;
                    SceneManager.MoveGameObjectToScene(walls, EditorSceneManager.GetActiveScene());
                    var wp = walls.transform.position;
                    walls.transform.position = new Vector3(wp.x, wp.y, 0f);
                    var litW = AssetDatabase.LoadAssetAtPath<RenderAssets>("Assets/Resources/TWF/RenderAssets.asset");
                    foreach (var sr in walls.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if (litW != null) sr.sharedMaterial = litW.litDetail;
                        sr.sortingLayerName = "Default";
                        sr.sortingOrder = SortOrder.Units;
                    }
                    continue;
                }
                if (root.name != "Map") continue;
                var copy = Object.Instantiate(root);
                copy.name = "Village";
                SceneManager.MoveGameObjectToScene(copy, EditorSceneManager.GetActiveScene());
                // Measure the village from its background tiles.
                foreach (var tm in copy.GetComponentsInChildren<Tilemap>())
                {
                    if (LayerMask.LayerToName(tm.gameObject.layer) != "Background") continue;
                    tm.CompressBounds();
                    var b = tm.cellBounds;
                    var offset = tm.transform.position;
                    bounds = new RectInt(Mathf.FloorToInt(b.xMin + offset.x), Mathf.FloorToInt(b.yMin + offset.y), b.size.x, b.size.y);
                }
                // The village used sorting layers that draw over everything; put it under the characters.
                var lit = AssetDatabase.LoadAssetAtPath<RenderAssets>("Assets/Resources/TWF/RenderAssets.asset");
                foreach (var r in copy.GetComponentsInChildren<TilemapRenderer>())
                {
                    string layer = r.sortingLayerName;
                    if (lit != null) r.sharedMaterial = layer == "Background" ? lit.litGround : lit.litDetail;
                    r.receiveShadows = true;
                    // Stack the layers a hair apart so opaque tiles don't z-fight (up is -Z).
                    var lp = r.transform.position;
                    r.transform.position = new Vector3(lp.x, lp.y, layer == "Background" ? 0f : layer == "Vegitation" ? -0.01f : -0.02f);
                    r.sortingLayerName = "Default";
                    r.sortingOrder = layer == "Background" ? -95 : layer == "Vegitation" ? -93 : -92;
                }
                var p = copy.transform.position;
                copy.transform.position = new Vector3(p.x, p.y, 0f);
            }
            EditorSceneManager.CloseScene(village, true);
            // Trim to a sensible hub size around the village center.
            int xMin = Mathf.Max(bounds.xMin, -45), xMax = Mathf.Min(bounds.xMax, 45);
            int yMin = Mathf.Max(bounds.yMin, -30), yMax = Mathf.Min(bounds.yMax, 60);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        static void BuildHubBorder(Zone hub, Transform parent)
        {
            var root = new GameObject("Hub Border").transform;
            root.SetParent(parent, false);
            hub.root = root;
            // A ground layer under the village so there are no holes.
            var ground = MakeTilemap(root, "Ground", -120, false);
            ground.transform.localPosition = new Vector3(0, 0, 0.02f); // just under the village tiles
            for (int x = hub.rect.xMin; x < hub.rect.xMax; x++)
            for (int y = hub.rect.yMin; y < hub.rect.yMax; y++)
                ground.SetTile(new Vector3Int(x, y, 0), ForestTile(Pick(grassPlain)));
        }

        // ---------- Zones ----------

        static void BuildZone(Zone z, Transform parent)
        {
            var r = z.rect;
            z.root = new GameObject(z.def.displayName).transform;
            z.root.SetParent(parent, false);
            z.blocked = new bool[r.width, r.height];
            z.path = new bool[r.width, r.height];
            var biome = z.def.biome;

            z.ground = MakeTilemap(z.root, "Ground", -110, false);
            z.paths = MakeTilemap(z.root, "Paths", -105, false);
            z.detail = MakeTilemap(z.root, "Detail", -104, false);
            z.obstacles = MakeTilemap(z.root, "Obstacles", -100, true);
            z.ground.color = GroundTint(biome);
            z.paths.color = GroundTint(biome);

            // Ground.
            for (int x = 0; x < r.width; x++)
            for (int y = 0; y < r.height; y++)
                z.ground.SetTile(new Vector3Int(r.xMin + x, r.yMin + y, 0), GroundTile(biome));

            // Waypoint near the middle, offset randomly.
            z.waypoint = new Vector2Int(r.xMin + r.width / 2 + rng.Next(-12, 12), r.yMin + r.height / 2 + rng.Next(-12, 12));

            // Paths: entry/exit gates to the waypoint, plus a couple of side trails.
            foreach (var g in z.gates) CarvePath(z, Inside(z, g), z.waypoint);
            for (int i = 0; i < 3; i++)
            {
                var a = new Vector2Int(r.xMin + rng.Next(12, r.width - 12), r.yMin + rng.Next(12, r.height - 12));
                CarvePath(z, z.waypoint, a);
            }

            BorderWall(z, z.root, biome, false);
            PlaceObstacles(z);
            ScatterDetail(z);
        }

        static Vector2Int Inside(Zone z, Vector2Int gate)
        {
            // Move a gate point one step inside the zone.
            var r = z.rect;
            return new Vector2Int(Mathf.Clamp(gate.x, r.xMin + 1, r.xMax - 2), Mathf.Clamp(gate.y, r.yMin + 1, r.yMax - 2));
        }

        static void CarvePath(Zone z, Vector2Int from, Vector2Int to)
        {
            Vector2 a = from, b = to;
            float len = Vector2.Distance(a, b);
            int steps = Mathf.CeilToInt(len * 2f);
            float phase = (float)rng.NextDouble() * 10f;
            Vector2 perp = new Vector2(-(b - a).y, (b - a).x).normalized;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float wobble = Mathf.Sin(t * Mathf.PI) * (Mathf.PerlinNoise(phase, t * 3f) - 0.5f) * len * 0.35f;
                Vector2 p = Vector2.Lerp(a, b, t) + perp * wobble;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    SetPath(z, Mathf.RoundToInt(p.x) + dx, Mathf.RoundToInt(p.y) + dy);
            }
        }

        static void SetPath(Zone z, int x, int y)
        {
            int lx = x - z.rect.xMin, ly = y - z.rect.yMin;
            if (lx < 0 || ly < 0 || lx >= z.rect.width || ly >= z.rect.height) return;
            if (z.path[lx, ly]) return;
            z.path[lx, ly] = true;
            z.paths.SetTile(new Vector3Int(x, y, 0), PathTile(z.def.biome));
        }

        static bool IsPath(Zone z, int lx, int ly, int margin)
        {
            for (int dx = -margin; dx <= margin; dx++)
            for (int dy = -margin; dy <= margin; dy++)
            {
                int x = lx + dx, y = ly + dy;
                if (x >= 0 && y >= 0 && x < z.rect.width && y < z.rect.height && z.path[x, y]) return true;
            }
            return false;
        }

        // Thick wall of biome obstacles around the zone, with openings at the gates.
        static void BorderWall(Zone z, Transform parent, Biome biome, bool hub)
        {
            var r = z.rect;
            var wallRoot = new GameObject("Border").transform;
            wallRoot.SetParent(parent, false);
            wallRoot.gameObject.layer = LayerMask.NameToLayer("Walls");

            bool NearGate(Vector2 p)
            {
                foreach (var g in z.gates)
                    if (Vector2.Distance(p, g) < GateWidth * 0.5f + 1f) return true;
                return false;
            }

            // Invisible colliders as segments so gaps stay exactly open.
            void Segment(Vector2 a, Vector2 b)
            {
                Vector2 dir = (b - a);
                float len = dir.magnitude;
                int steps = Mathf.CeilToInt(len);
                Vector2? start = null;
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = a + dir * (i / (float)steps);
                    bool open = NearGate(p);
                    if (!open && start == null) start = p;
                    if ((open || i == steps) && start != null)
                    {
                        Vector2 end = open ? p - dir.normalized : p;
                        var go = new GameObject("Wall");
                        go.layer = wallRoot.gameObject.layer;
                        go.transform.SetParent(wallRoot, false);
                        var box = go.AddComponent<BoxCollider2D>();
                        Vector2 c = (start.Value + end) / 2f;
                        go.transform.position = c;
                        box.size = new Vector2(Mathf.Abs(end.x - start.Value.x) + 1.5f, Mathf.Abs(end.y - start.Value.y) + 1.5f);
                        start = null;
                    }
                }
            }

            Segment(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin));
            Segment(new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax));
            Segment(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax));
            Segment(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax));

            // Visual border decor along the inside edge.
            if (hub) return;
            for (int side = 0; side < 4; side++)
            {
                int len = side < 2 ? r.width : r.height;
                for (float t = 0; t < len; t += 2.2f)
                {
                    Vector2 p;
                    switch (side)
                    {
                        case 0: p = new Vector2(r.xMin + t, r.yMin + 0.8f); break;
                        case 1: p = new Vector2(r.xMin + t, r.yMax - 0.5f); break;
                        case 2: p = new Vector2(r.xMin + 0.8f, r.yMin + t); break;
                        default: p = new Vector2(r.xMax - 0.8f, r.yMin + t); break;
                    }
                    if (NearGate(p)) continue;
                    PlaceDecor(z, wallRoot, BorderDecor(biome), p + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f), biome, false);
                    MarkBlocked(z, p, 2);
                }
            }
        }

        static void PlaceObstacles(Zone z)
        {
            var r = z.rect;
            var biome = z.def.biome;
            var root = new GameObject("Obstacles").transform;
            root.SetParent(z.root, false);
            root.gameObject.layer = LayerMask.NameToLayer("Walls");
            float noiseOffset = (float)rng.NextDouble() * 100f;

            // Tile obstacles (water, lava, ruined walls) from noise blobs.
            for (int x = 3; x < r.width - 3; x++)
            for (int y = 3; y < r.height - 3; y++)
            {
                if (IsPath(z, x, y, 2) || NearPoint(z, x, y, z.waypoint, 6)) continue;
                float n = Mathf.PerlinNoise(noiseOffset + x * 0.09f, noiseOffset + y * 0.09f);
                float threshold = biome == Biome.Marsh ? 0.62f : biome == Biome.Ember ? 0.68f : biome == Biome.Ruins ? 0.7f : 1.1f;
                if (n > threshold)
                {
                    z.obstacles.SetTile(new Vector3Int(r.xMin + x, r.yMin + y, 0), ObstacleTile(biome, x, y));
                    z.blocked[x, y] = true;
                }
            }

            // Ruins: straight broken wall segments.
            if (biome == Biome.Ruins)
            {
                for (int i = 0; i < 40; i++)
                {
                    int x = rng.Next(5, r.width - 5), y = rng.Next(5, r.height - 5);
                    bool horizontal = rng.NextDouble() < 0.5;
                    int len = rng.Next(4, 12);
                    for (int k = 0; k < len; k++)
                    {
                        int lx = horizontal ? x + k : x, ly = horizontal ? y : y + k;
                        if (lx >= r.width - 3 || ly >= r.height - 3 || IsPath(z, lx, ly, 2) || NearPoint(z, lx, ly, z.waypoint, 6)) continue;
                        if (rng.NextDouble() < 0.15) continue; // broken gaps
                        z.obstacles.SetTile(new Vector3Int(r.xMin + lx, r.yMin + ly, 0), genTiles["ruins_wall"]);
                        z.blocked[lx, ly] = true;
                    }
                }
            }

            // Decor clusters (trees, rocks) with colliders.
            int clusters = biome == Biome.Forest ? 70 : biome == Biome.Marsh ? 45 : 40;
            for (int i = 0; i < clusters; i++)
            {
                int cx = rng.Next(6, r.width - 6), cy = rng.Next(6, r.height - 6);
                int count = rng.Next(2, biome == Biome.Forest ? 7 : 4);
                for (int k = 0; k < count; k++)
                {
                    int x = cx + rng.Next(-4, 5), y = cy + rng.Next(-4, 5);
                    if (x < 3 || y < 3 || x >= r.width - 3 || y >= r.height - 3) continue;
                    if (IsPath(z, x, y, 2) || z.blocked[x, y] || NearPoint(z, x, y, z.waypoint, 6) || NearAnyGate(z, x, y, 7)) continue;
                    PlaceDecor(z, root, ClusterDecor(biome), new Vector2(r.xMin + x + 0.5f, r.yMin + y + 0.2f), biome, true);
                    MarkBlocked(z, new Vector2(r.xMin + x, r.yMin + y), 1);
                }
            }
        }

        static void ScatterDetail(Zone z)
        {
            var r = z.rect;
            var biome = z.def.biome;
            var root = new GameObject("Detail").transform;
            root.SetParent(z.root, false);
            for (int i = 0; i < 220; i++)
            {
                int x = rng.Next(3, r.width - 3), y = rng.Next(3, r.height - 3);
                if (z.blocked[x, y] || IsPath(z, x, y, 0)) continue;
                if (biome == Biome.Forest || biome == Biome.Marsh)
                {
                    if (rng.NextDouble() < 0.6) z.detail.SetTile(new Vector3Int(r.xMin + x, r.yMin + y, 0), ForestTile(Pick(grassDetail)));
                    else PlaceDecor(z, root, decor["bush"], new Vector2(r.xMin + x + 0.5f, r.yMin + y), biome, false, 0.8f);
                }
                else if (biome == Biome.Ember)
                {
                    z.detail.SetTile(new Vector3Int(r.xMin + x, r.yMin + y, 0), genTiles[rng.NextDouble() < 0.5 ? "ember_crack0" : "ember_crack1"]);
                }
                else
                {
                    z.detail.SetTile(new Vector3Int(r.xMin + x, r.yMin + y, 0), genTiles["ruins_rubble"]);
                }
            }
        }

        static void PlaceCamps(Zone z)
        {
            var r = z.rect;
            var root = new GameObject("Camps").transform;
            root.SetParent(z.root, false);
            var placed = new List<Vector2Int>();
            float maxDist = Mathf.Sqrt(r.width * r.width + r.height * r.height);
            int attempts = 0;
            while (placed.Count < 20 && attempts++ < 3000)
            {
                int x = rng.Next(6, r.width - 6), y = rng.Next(6, r.height - 6);
                var cell = new Vector2Int(r.xMin + x, r.yMin + y);
                if (!Open(z, x, y, 2)) continue;
                if (NearPoint(z, x, y, z.waypoint, 11) || NearAnyGate(z, x, y, 12)) continue;
                bool tooClose = false;
                foreach (var p in placed)
                    if (Vector2Int.Distance(p, cell) < 13) { tooClose = true; break; }
                if (tooClose) continue;
                placed.Add(cell);

                var camp = new GameObject("Camp").AddComponent<MonsterCamp>();
                camp.transform.SetParent(root, false);
                camp.transform.position = Center(cell);
                camp.zone = z.def;
                camp.depth = Mathf.Clamp01(Vector2Int.Distance(cell, z.entry) / (maxDist * 0.85f));
                camp.seed = rng.Next();
            }
        }

        static bool Open(Zone z, int x, int y, int radius)
        {
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int lx = x + dx, ly = y + dy;
                if (lx < 0 || ly < 0 || lx >= z.rect.width || ly >= z.rect.height || z.blocked[lx, ly]) return false;
            }
            return true;
        }

        static bool NearPoint(Zone z, int lx, int ly, Vector2Int worldPoint, float dist) =>
            Vector2.Distance(new Vector2(z.rect.xMin + lx, z.rect.yMin + ly), worldPoint) < dist;

        static bool NearAnyGate(Zone z, int lx, int ly, float dist)
        {
            foreach (var g in z.gates)
                if (NearPoint(z, lx, ly, g, dist)) return true;
            return false;
        }

        static void MarkBlocked(Zone z, Vector2 world, int radius)
        {
            int cx = Mathf.FloorToInt(world.x) - z.rect.xMin, cy = Mathf.FloorToInt(world.y) - z.rect.yMin;
            if (z.blocked == null) return;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && y >= 0 && x < z.rect.width && y < z.rect.height) z.blocked[x, y] = true;
            }
        }

        static Vector2Int FarthestOpenCell(Zone z, Vector2Int from)
        {
            Vector2Int best = z.waypoint;
            float bestD = 0f;
            for (int x = 8; x < z.rect.width - 8; x += 2)
            for (int y = 8; y < z.rect.height - 8; y += 2)
            {
                if (!Open(z, x, y, 2)) continue;
                var c = new Vector2Int(z.rect.xMin + x, z.rect.yMin + y);
                float d = Vector2Int.Distance(c, from);
                if (d > bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            // Clear a little arena around the gate.
            CarvePath(z, best, z.waypoint);
            return best;
        }

        static void AddZoneArea(Transform parent, Zone z)
        {
            var go = new GameObject("Area " + z.def.id);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<ZoneArea>();
            a.zone = z.def;
            a.bounds = new Rect(z.rect.xMin, z.rect.yMin, z.rect.width, z.rect.height);
        }

        static void AddWaypoint(Transform parent, string id, string name, int order, Vector2 pos)
        {
            var go = new GameObject("Waypoint " + id);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var wp = go.AddComponent<Waypoint>();
            wp.id = id;
            wp.displayName = name;
            wp.order = order;
        }

        // ---------- Tiles and decor ----------

        static Tilemap MakeTilemap(Transform parent, string name, int order, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<Tilemap>();
            var tr = go.AddComponent<TilemapRenderer>();
            tr.sortingOrder = order;
            tr.mode = TilemapRenderer.Mode.Chunk;
            var ra = AssetDatabase.LoadAssetAtPath<RenderAssets>("Assets/Resources/TWF/RenderAssets.asset");
            if (ra != null) tr.sharedMaterial = (name == "Ground" || name == "Paths") ? ra.litGround : ra.litDetail;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = true;
            if (collider)
            {
                go.layer = LayerMask.NameToLayer("Walls");
                var tc = go.AddComponent<TilemapCollider2D>();
                tc.compositeOperation = Collider2D.CompositeOperation.Merge;
                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Static;
                go.AddComponent<CompositeCollider2D>();
            }
            return tm;
        }

        static int Pick(int[] arr) => arr[rng.Next(arr.Length)];

        static TileBase ForestTile(int id) => AssetDatabase.LoadAssetAtPath<TileBase>($"{ForestTiles}tileset-sliced_{id}.asset");

        static Color GroundTint(Biome b)
        {
            switch (b)
            {
                case Biome.Marsh: return new Color(0.62f, 0.72f, 0.6f);
                case Biome.Ruins: return Color.white;
                case Biome.Ember: return Color.white;
                default: return Color.white;
            }
        }

        static TileBase GroundTile(Biome b)
        {
            switch (b)
            {
                case Biome.Ruins: return genTiles["ruins_floor" + rng.Next(4)];
                case Biome.Ember: return genTiles["ash" + rng.Next(3)];
                default: return ForestTile(rng.NextDouble() < 0.12 ? Pick(grassDetail) : Pick(grassPlain));
            }
        }

        static TileBase PathTile(Biome b)
        {
            switch (b)
            {
                case Biome.Ruins: return genTiles["ruins_path"];
                case Biome.Ember: return genTiles["ember_path"];
                default: return ForestTile(Pick(dirt));
            }
        }

        static TileBase ObstacleTile(Biome b, int x, int y)
        {
            switch (b)
            {
                case Biome.Marsh: return genTiles["water" + ((x + y) % 2)];
                case Biome.Ember: return genTiles["lava" + ((x * 3 + y) % 2)];
                default: return genTiles["ruins_wall"];
            }
        }

        static Sprite BorderDecor(Biome b)
        {
            switch (b)
            {
                case Biome.Forest: return decor[rng.NextDouble() < 0.5 ? "tree-orange" : "tree-pink"];
                case Biome.Marsh: return decor[rng.NextDouble() < 0.6 ? "tree-dried" : "tree-pink"];
                case Biome.Ruins: return decor[rng.NextDouble() < 0.3 ? "rock-monument" : "rock"];
                default: return decor[rng.NextDouble() < 0.5 ? "tree-dried" : "rock"];
            }
        }

        static Sprite ClusterDecor(Biome b)
        {
            double r = rng.NextDouble();
            switch (b)
            {
                case Biome.Forest: return decor[r < 0.4 ? "tree-orange" : r < 0.8 ? "tree-pink" : r < 0.9 ? "trunk" : "bush-tall"];
                case Biome.Marsh: return decor[r < 0.5 ? "tree-dried" : r < 0.75 ? "trunk" : "bush-tall"];
                case Biome.Ruins: return decor[r < 0.15 ? "rock-monument" : r < 0.8 ? "rock" : "trunk"];
                default: return decor[r < 0.5 ? "rock" : r < 0.85 ? "tree-dried" : "trunk"];
            }
        }

        static Color DecorTint(Biome b, Sprite s)
        {
            switch (b)
            {
                case Biome.Marsh: return new Color(0.7f, 0.75f, 0.85f);
                case Biome.Ruins: return s.name.StartsWith("rock") ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.6f, 0.6f, 0.6f);
                case Biome.Ember: return s.name.StartsWith("rock") ? new Color(0.55f, 0.4f, 0.38f) : new Color(0.35f, 0.25f, 0.22f);
                default: return Color.white;
            }
        }

        static void PlaceDecor(Zone z, Transform parent, Sprite sprite, Vector2 pos, Biome biome, bool collider, float scale = 1f)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.layer = LayerMask.NameToLayer("Walls");
            float s = scale * (0.9f + (float)rng.NextDouble() * 0.25f);
            // Collider on the root (2D physics), sprite on a billboarded child that stands up in 2.5D.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(rng.NextDouble() < 0.5 ? -s : s, s, 1f);
            visual.AddComponent<Billboard>();
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = DecorTint(biome, sprite);
            sr.sortingOrder = SortOrder.Units;
            var renderAssets = AssetDatabase.LoadAssetAtPath<RenderAssets>("Assets/Resources/TWF/RenderAssets.asset");
            if (renderAssets != null) sr.sharedMaterial = sprite.name.StartsWith("bush") || sprite.name.StartsWith("tree") ? renderAssets.litWind : renderAssets.litSprite;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            sr.receiveShadows = true;
            if (collider || sprite.name.StartsWith("tree") || sprite.name.StartsWith("rock"))
            {
                var c = go.AddComponent<CircleCollider2D>();
                float w = sprite.bounds.size.x * s;
                c.radius = Mathf.Clamp(w * 0.18f, 0.3f, 1.4f);
            }
        }

        // Copies the pack's decoration sprites at 16 pixels per unit with the pivot at the base,
        // so they match the hero's scale and sort by their feet.
        static void PrepareDecorSprites()
        {
            Dir(DecorDir);
            decor = new Dictionary<string, Sprite>();
            var names = new[] { "bush", "bush-tall", "rock", "rock-monument", "tree-dried", "tree-orange", "tree-pink", "trunk", "sign" };
            foreach (var n in names)
            {
                string src = $"{ForestTiles}sliced-objects/{n}.png";
                string dst = $"{DecorDir}/{n}.png";
                if (!File.Exists(dst)) AssetDatabase.CopyAsset(src, dst);
                var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = 16;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                imp.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                imp.SetTextureSettings(settings);
                imp.SaveAndReimport();
                decor[n] = AssetDatabase.LoadAssetAtPath<Sprite>(dst);
            }
        }

        // ---------- Generated tiles ----------

        static void PrepareGeneratedTiles()
        {
            Dir(TileDir);
            genTiles = new Dictionary<string, TileBase>();
            var trng = new System.Random(77);

            for (int i = 0; i < 4; i++) MakeTile($"ruins_floor{i}", FlagstoneTile(trng, new Color(0.42f, 0.41f, 0.4f), i), false);
            MakeTile("ruins_path", NoiseTile(trng, new Color(0.5f, 0.47f, 0.42f), 0.07f, null, 0f), false);
            MakeTile("ruins_wall", WallTile(trng), true);
            MakeTile("ruins_rubble", RubbleTile(trng), false);
            for (int i = 0; i < 2; i++) MakeTile($"water{i}", WaterTile(trng, i), true);
            for (int i = 0; i < 2; i++) MakeTile($"lava{i}", LavaTile(trng, i), true);
            for (int i = 0; i < 3; i++) MakeTile($"ash{i}", NoiseTile(trng, new Color(0.24f, 0.18f, 0.17f), 0.05f, new Color(0.45f, 0.2f, 0.1f), 0.02f), false);
            MakeTile("ember_path", NoiseTile(trng, new Color(0.33f, 0.26f, 0.22f), 0.05f, null, 0f), false);
            for (int i = 0; i < 2; i++) MakeTile($"ember_crack{i}", CrackTile(trng), false);
            // Invisible collider tile for blocked cells (cliffs, water, walls) in the streamed world.
            var clear = new Color[256];
            clear[0] = new Color(0, 0, 0, 0.01f);
            MakeTile("blocker", clear, true);
            AssetDatabase.SaveAssets();
        }

        static void MakeTile(string name, Color[] pixels, bool collider)
        {
            string png = $"{TileDir}/{name}.png";
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(png);
            var imp = (TextureImporter)AssetImporter.GetAtPath(png);
            imp.textureType = TextureImporterType.Sprite;
            imp.spritePixelsPerUnit = 16;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();

            string tilePath = $"{TileDir}/{name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(png);
            tile.colliderType = collider ? Tile.ColliderType.Grid : Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            genTiles[name] = tile;
        }

        static Color Vary(Color c, System.Random r, float amount)
        {
            float v = ((float)r.NextDouble() - 0.5f) * 2f * amount;
            return new Color(Mathf.Clamp01(c.r + v), Mathf.Clamp01(c.g + v), Mathf.Clamp01(c.b + v), 1f);
        }

        static Color[] NoiseTile(System.Random r, Color baseColor, float variation, Color? speck, float speckChance)
        {
            var px = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                px[i] = Vary(baseColor, r, variation);
                if (speck.HasValue && r.NextDouble() < speckChance) px[i] = speck.Value;
            }
            return px;
        }

        static Color[] FlagstoneTile(System.Random r, Color stone, int variant)
        {
            var px = new Color[256];
            int split = 5 + variant * 2;
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                bool mortar = y == 0 || y == 8 || (y < 8 ? x == split : x == (split + 7) % 16) || x == 0;
                var c = mortar ? stone * 0.6f : Vary(stone, r, 0.04f);
                if (!mortar && (y == 7 || y == 15)) c *= 0.85f;
                if (!mortar && r.NextDouble() < 0.03) c = new Color(0.35f, 0.42f, 0.3f); // moss
                c.a = 1f;
                px[y * 16 + x] = c;
            }
            return px;
        }

        static Color[] WallTile(System.Random r)
        {
            var px = new Color[256];
            var stone = new Color(0.55f, 0.53f, 0.5f);
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int row = y / 4;
                int offset = row % 2 == 0 ? 0 : 4;
                bool mortar = y % 4 == 0 || (x + offset) % 8 == 0;
                var c = mortar ? new Color(0.22f, 0.2f, 0.2f) : Vary(stone, r, 0.05f);
                if (!mortar && y % 4 == 3) c = c * 1.15f;
                if (y >= 14) c *= 0.7f;
                c.a = 1f;
                px[y * 16 + x] = c;
            }
            return px;
        }

        static Color[] RubbleTile(System.Random r)
        {
            var px = new Color[256];
            for (int i = 0; i < 256; i++) px[i] = Color.clear;
            for (int k = 0; k < 4; k++)
            {
                int cx = r.Next(2, 14), cy = r.Next(2, 14);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 0; dy++)
                    px[(cy + dy) * 16 + cx + dx] = dy == 0 ? new Color(0.6f, 0.58f, 0.55f) : new Color(0.3f, 0.29f, 0.28f);
            }
            return px;
        }

        static Color[] WaterTile(System.Random r, int variant)
        {
            var px = new Color[256];
            var water = new Color(0.16f, 0.25f, 0.22f);
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                var c = Vary(water, r, 0.02f);
                if ((y + variant * 3) % 6 == 0 && (x + y) % 5 < 3) c = new Color(0.3f, 0.42f, 0.36f);
                if (r.NextDouble() < 0.02) c = new Color(0.45f, 0.55f, 0.35f); // algae
                px[y * 16 + x] = c;
            }
            return px;
        }

        static Color[] LavaTile(System.Random r, int variant)
        {
            var px = new Color[256];
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.25f + variant * 7f, y * 0.25f + variant * 3f);
                var c = Color.Lerp(new Color(0.75f, 0.15f, 0.05f), new Color(1f, 0.75f, 0.2f), n);
                if (n > 0.72f) c = new Color(1f, 0.95f, 0.6f);
                c.a = 1f;
                px[y * 16 + x] = c;
            }
            return px;
        }

        static Color[] CrackTile(System.Random r)
        {
            var px = new Color[256];
            for (int i = 0; i < 256; i++) px[i] = Color.clear;
            int x = r.Next(2, 14), y = r.Next(2, 14);
            for (int k = 0; k < 9; k++)
            {
                px[y * 16 + x] = k % 3 == 0 ? new Color(1f, 0.6f, 0.2f) : new Color(0.85f, 0.3f, 0.1f);
                x = Mathf.Clamp(x + r.Next(-1, 2), 0, 15);
                y = Mathf.Clamp(y + r.Next(-1, 2), 0, 15);
            }
            return px;
        }

        // ---------- Util ----------

        static void Dir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Dir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
