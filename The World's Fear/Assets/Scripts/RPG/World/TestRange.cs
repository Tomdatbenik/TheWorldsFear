using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // The debug scene (Scenes/TestRange): a small yard with every kind of ground the world has (stone stairs up to a
    // platform, a castle-brick flight, a grass slope to a high ledge, a crenellated wall) and every monster, plus a
    // panel to switch weapons, spawn and pacify monsters, slow time and jump between the spots.
    // Runs on a throwaway hero: nothing here is saved, and dying just restarts the scene.
    [DefaultExecutionOrder(-90)]
    public class TestRange : MonoBehaviour
    {
        public static TestRange Instance { get; private set; }

        const float Half = 40f;
        const float PlatformH = 2.4f, LedgeH = 4.8f, CastleH = 1.2f;
        static readonly Rect Platform = new Rect(-10f, 10f, 20f, 10f);
        static readonly Rect StoneStairs = new Rect(-2f, 6f, 4f, 4f);
        static readonly Rect CastleStairs = new Rect(-16f, 6f, 3f, 3f);
        static readonly Rect CastleTop = new Rect(-19f, 9f, 9f, 6f);
        static readonly Rect Ledge = new Rect(14f, 10f, 12f, 10f);
        static readonly Rect Slope = new Rect(16f, 0f, 8f, 10f);
        static readonly Rect WallRun = new Rect(-34f, 22f, 26f, 2f);
        static readonly Vector2 PortalSpot = new Vector2(10f, -3f), MillSpot = new Vector2(-28f, 0f);

        readonly List<MonsterBrain> spawned = new List<MonsterBrain>();
        readonly List<(string name, System.Func<Vector2, MonsterBrain> spawn)> kinds = new List<(string, System.Func<Vector2, MonsterBrain>)>();

        bool god = true, passive = true, frozen, panelOpen = true, blocking;
        int level = 5;
        MonsterRank rank = MonsterRank.Normal;
        float timeScale = 1f;
        bool lockTime = true;
        float timeOfDay = 0.33f;     // DayNight.Time01: 0 sunrise, ~0.33 noon, 0.65 sunset
        float relief;                // _TWF_ReliefBoost: 0 as authored, -1 off
        Vector2 scroll;
        Rect panel = new Rect(10, 10, 300, 640);

        void Awake()
        {
            Instance = this;
            // A throwaway hero: never written to disk, never touches the real save.
            Profile.Sandbox = true;
            Profile.NewGame(WeaponType.Sword);
            Profile.Data.inventory.Clear();
            TerrainHeight.Clear();
            TerrainHeight.World = null;
            Build();
            var boot = FindAnyObjectByType<GameBootstrap>();
            if (boot != null && boot.defaultSpawn != null) boot.defaultSpawn.position = new Vector2(0f, -2f).W();
        }

        void Start()
        {
            CollectKinds();
            SpawnGallery();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat("_TWF_ReliefBoost", 0f);
        }

        // ---------- The yard ----------

        void Build()
        {
            var stone = new Town.Solid { topAnchoredHeight = 8f };
            var paving = new Town.Solid();
            var keep = new Town.Solid();
            var castle = new Town.Solid();
            var grass = new Town.Solid();
            var edges = new GameObject("Edges") { layer = LayerMask.NameToLayer("Walls") };
            edges.transform.SetParent(transform, false);

            // The yard floor and a low wall round it.
            Town.Box(paving, paving, new Rect(-Half, -Half, Half * 2f, Half * 2f), -0.5f, 0f);
            Phys.Edge(edges, new[] { new Vector2(-Half, -Half), new Vector2(Half, -Half), new Vector2(Half, Half), new Vector2(-Half, Half), new Vector2(-Half, -Half) });

            // Stone stairs up to a platform (like the citadel).
            Town.Box(stone, paving, Platform, 0f, PlatformH);
            TerrainHeight.AddFlat(Platform, PlatformH);
            Steps(keep, paving, StoneStairs, 0f, PlatformH, 6);
            TerrainHeight.AddRamp(StoneStairs, 0f, PlatformH, Vector2Int.up);
            Phys.Edge(edges, new[] { new Vector2(StoneStairs.xMin, StoneStairs.yMin), new Vector2(StoneStairs.xMin, Platform.yMin), new Vector2(Platform.xMin, Platform.yMin),
                new Vector2(Platform.xMin, Platform.yMax), new Vector2(Platform.xMax, Platform.yMax), new Vector2(Platform.xMax, Platform.yMin),
                new Vector2(StoneStairs.xMax, Platform.yMin), new Vector2(StoneStairs.xMax, StoneStairs.yMin) });

            // A castle-brick flight one level up (like the open world's stairs), to a small terrace.
            Town.Box(castle, castle, CastleTop, 0f, CastleH);
            TerrainHeight.AddFlat(CastleTop, CastleH);
            Steps(castle, castle, CastleStairs, 0f, CastleH, 12);
            TerrainHeight.AddRamp(CastleStairs, 0f, CastleH, Vector2Int.up);
            Phys.Edge(edges, new[] { new Vector2(CastleStairs.xMin, CastleStairs.yMin), new Vector2(CastleStairs.xMin, CastleTop.yMin), new Vector2(CastleTop.xMin, CastleTop.yMin),
                new Vector2(CastleTop.xMin, CastleTop.yMax), new Vector2(CastleTop.xMax, CastleTop.yMax), new Vector2(CastleTop.xMax, CastleTop.yMin),
                new Vector2(CastleStairs.xMax, CastleTop.yMin), new Vector2(CastleStairs.xMax, CastleStairs.yMin) });

            // A grass slope up to a high ledge.
            Town.Box(stone, grass, Ledge, 0f, LedgeH);
            TerrainHeight.AddFlat(Ledge, LedgeH);
            float x0 = Slope.xMin, x1 = Slope.xMax, y0 = Slope.yMin, y1 = Slope.yMax;
            grass.Quad(new Vector3(x0, y0, 0f), new Vector3(x1, y0, 0f), new Vector3(x1, y1, -LedgeH), new Vector3(x0, y1, -LedgeH),
                new Vector3(0f, -LedgeH, -Slope.height).normalized, new Vector2(x0, y0) / 4f, new Vector2(x1, y0) / 4f, new Vector2(x1, y1) / 4f, new Vector2(x0, y1) / 4f);
            foreach (float x in new[] { x0, x1 })   // rock under the slope's sides
                stone.Tri(new Vector3(x, y0, 0f), new Vector3(x, y1, 0f), new Vector3(x, y1, -LedgeH),
                    x == x0 ? Vector3.left : Vector3.right, new Vector2(y0 / 4f, 1f - LedgeH / 8f), new Vector2(y1 / 4f, 1f - LedgeH / 8f), new Vector2(y1 / 4f, 1f));
            TerrainHeight.AddRamp(Slope, 0f, LedgeH, Vector2Int.up);
            Phys.Edge(edges, new[] { new Vector2(x0, y0), new Vector2(x0, Ledge.yMin), new Vector2(Ledge.xMin, Ledge.yMin), new Vector2(Ledge.xMin, Ledge.yMax),
                new Vector2(Ledge.xMax, Ledge.yMax), new Vector2(Ledge.xMax, Ledge.yMin), new Vector2(x1, Ledge.yMin), new Vector2(x1, y0) });

            // A run of town wall with crenels and a tower.
            Town.Box(stone, paving, WallRun, 0f, 5f);
            Town.Crenels(stone, paving, WallRun, 5f, 0.75f, 0.45f);
            var tower = new Rect(WallRun.xMax, WallRun.yMin - 1f, 4f, 4f);
            Town.Box(stone, paving, tower, 0f, 7.5f);
            Town.Crenels(stone, paving, new Rect(tower.xMin - 0.2f, tower.yMin - 0.2f, tower.width + 0.4f, tower.height + 0.4f), 7.5f, 0.75f, 0.45f);
            Phys.Box(Holder("Wall Block"), new Vector2(WallRun.width, WallRun.height), WallRun.center);
            Phys.Box(Holder("Tower Block"), tower.size, tower.center);

            stone.Build(transform, "Stone", Town.Mat("stone", Town.CappedWall(EnvArt.Tex("stone_wall_tile"))), true);
            keep.Build(transform, "Steps", Town.Mat("keep", EnvArt.Tex("dgn_keep_wall")), true);
            paving.Build(transform, "Paving", Town.Mat("paving", EnvArt.Tex("cobble_tile")), true);
            castle.Build(transform, "Castle Stairs", WorldTiles.MeshMaterial("stairs", WorldTiles.CastleTexture()), true);
            grass.Build(transform, "Grass", Town.Mat("test_grass", EnvArt.Tex("grass_tile")), true);

            // Some of the world's props for scale and sorting.
            EnvArt.PlaceTree("oak", transform, new Vector2(30f, -20f));
            EnvArt.Place("well", transform, new Vector2(-6f, 15f), PlatformH);

            // The kit's textured 3D props: pixel-art textures on real shapes.
            Farmland.Windmill(transform, MillSpot);
            EnvMesh.Place("scarecrow", transform, new Vector2(-8f, -3f));
            EnvMesh.Place("haystack", transform, MillSpot + new Vector2(6f, -4f));
            EnvMesh.Place("stall_produce", transform, MillSpot + new Vector2(7f, 3f));
            EnvMesh.Place("portal", transform, PortalSpot);
            EnvMesh.Place("timber_house", transform, new Vector2(32f, -6f));

        }

        GameObject Holder(string name)
        {
            var go = new GameObject(name) { layer = LayerMask.NameToLayer("Walls") };
            go.transform.SetParent(transform, false);
            return go;
        }

        // Stone steps over r rising from h0 to h1 toward +y.
        static void Steps(Town.Solid sides, Town.Solid tops, Rect r, float h0, float h1, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float f = i / (float)n;
                var step = new Rect(r.xMin, r.yMin + r.height * f, r.width, r.height * (1f - f));
                Town.Box(sides, tops, step, h0, h0 + (h1 - h0) * (i + 1) / n);
            }
        }

        // ---------- Monsters ----------

        void CollectKinds()
        {
            kinds.Clear();
            var all = new List<MobDef>(MobCatalog.All);
            all.Sort((a, b) => string.CompareOrdinal(a.biome + a.id, b.biome + b.id));
            foreach (var d in all)
            {
                var def = d;
                kinds.Add((def.name + " (" + def.biome + ")", at => MonsterBrain.Spawn(MonsterGenerator.CreateMob(def), level, rank, at)));
            }
            foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
                foreach (var sp in HandmadeMonsters.For(b))
                {
                    var s = sp;
                    kinds.Add((s.name + " (handmade " + b + ")", at => MonsterBrain.Spawn(s, level, rank, at)));
                }
            foreach (MonsterArchetype a in System.Enum.GetValues(typeof(MonsterArchetype)))
            {
                var arch = a;
                kinds.Add(("Generated " + arch, at => MonsterBrain.Spawn(MonsterGenerator.Create((int)arch * 97 + 5, Biome.Forest, arch), level, rank, at)));
            }
            kinds.Add(("Generated spider", at => MonsterBrain.Spawn(MonsterGenerator.Create(31, Biome.Forest, MonsterArchetype.Chaser, true), level, rank, at)));
            kinds.Add(("Giant", at => Giant.Spawn(at, Mathf.Max(level, 100))));
        }

        // Everything in rows across the south of the yard, plus a few on the stairs and the platform.
        void SpawnGallery()
        {
            ClearMonsters();
            int perRow = 10;
            for (int i = 0; i < kinds.Count; i++)
            {
                var at = new Vector2(-27f + (i % perRow) * 6f, -10f - (i / perRow) * 6f);
                Spawn(i, at);
            }
            // On the stairs and up top, to check how bodies draw on raised ground.
            for (int i = 0; i < Mathf.Min(4, kinds.Count); i++)
            {
                Spawn(i, new Vector2(-1f + (i % 2) * 2f, StoneStairs.yMin + 1f + (i / 2) * 2f));
                Spawn(i, new Vector2(-6f + i * 4f, Platform.center.y));
            }
            if (kinds.Count > 0) Spawn(0, CastleStairs.center);
            if (kinds.Count > 1) Spawn(1, Ledge.center);
        }

        void Spawn(int kind, Vector2 at)
        {
            MonsterBrain m = null;
            try { m = kinds[kind].spawn(at); }
            catch (System.Exception e) { Debug.LogWarning("TestRange: could not spawn " + kinds[kind].name + ": " + e.Message); }
            if (m == null) return;
            spawned.Add(m);
            Apply(m);
        }

        void Apply(MonsterBrain m)
        {
            if (m == null) return;
            if (passive) m.aggroRange = 0f;
            else m.aggroRange = 12f;
            m.enabled = !frozen;
        }

        void ApplyAll()
        {
            spawned.RemoveAll(m => m == null);
            foreach (var m in spawned) Apply(m);
        }

        void ClearMonsters()
        {
            foreach (var m in FindObjectsByType<MonsterBrain>()) if (m != null) Destroy(m.gameObject);
            spawned.Clear();
        }

        // ---------- Hero ----------

        void Equip(WeaponType t)
        {
            var item = ItemGenerator.Generate(Mathf.Max(1, level), Rarity.Rare, ItemSlot.Weapon, new System.Random(), t);
            Profile.Data.inventory.Clear();
            Profile.Data.inventory.Add(item);
            if (Profile.IsTwoHanded(item)) Profile.Data.equipment.RemoveAll(i => i.slot == ItemSlot.OffHand);
            Profile.EquipItem(item);
            Profile.Data.inventory.Clear();
        }

        void Teleport(Vector2 g)
        {
            var h = HeroController.Instance;
            if (h == null) return;
            var body = h.GetComponent<GroundBody>();
            if (body != null) { body.position = g; body.linearVelocity = Vector2.zero; }
            h.transform.position = g.W(TerrainHeight.At(g));
        }

        void Update()
        {
            var h = HeroController.Instance;
            if (h != null)
            {
                h.Combatant.Invulnerable = god;
                if (god && h.Combatant.HP < h.Combatant.MaxHP * 0.5f) h.Combatant.Heal(h.Combatant.MaxHP, false);
            }
            if (Input.GetKeyDown(KeyCode.F1)) panelOpen = !panelOpen;
            if (lockTime) DayNight.SetTime(timeOfDay);
            Shader.SetGlobalFloat("_TWF_ReliefBoost", relief);
            // Clicks on the panel must not walk the hero or swing at the ground.
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool over = panelOpen && panel.Contains(mouse);
            if (over != blocking) { MobaInput.Blocked = over; blocking = over; }
        }

        void OnGUI()
        {
            if (!panelOpen) { GUI.Label(new Rect(10, 10, 300, 22), "F1: test panel"); return; }
            panel = GUILayout.Window(91731, panel, Panel, "Test range (F1 hides)");
        }

        void Panel(int id)
        {
            GUILayout.Label("Weapon");
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (WeaponType t in System.Enum.GetValues(typeof(WeaponType)))
            {
                if (GUILayout.Button(t.ToString(), GUILayout.Width(66))) Equip(t);
                if (++col % 4 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("Go to");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start")) Teleport(new Vector2(0f, -2f));
            if (GUILayout.Button("Stairs")) Teleport(new Vector2(0f, StoneStairs.yMin - 1f));
            if (GUILayout.Button("Platform")) Teleport(Platform.center + Vector2.down * 2f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Castle flight")) Teleport(new Vector2(CastleStairs.center.x, CastleStairs.yMin - 1f));
            if (GUILayout.Button("Slope")) Teleport(new Vector2(Slope.center.x, Slope.yMin - 1f));
            if (GUILayout.Button("Ledge")) Teleport(Ledge.center);
            if (GUILayout.Button("Wall")) Teleport(new Vector2(WallRun.center.x, WallRun.yMin - 3f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Portal")) Teleport(PortalSpot + Vector2.down * 2.5f);
            if (GUILayout.Button("Scarecrow")) Teleport(new Vector2(-8f, -5.5f));
            if (GUILayout.Button("Windmill")) Teleport(MillSpot + Vector2.down * 7f);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("Hero");
            GUILayout.BeginHorizontal();
            god = GUILayout.Toggle(god, "God mode");
            GUILayout.Label("Time");
            foreach (float s in new[] { 1f, 0.5f, 0.2f })
            {
                bool on = Mathf.Approximately(timeScale, s);
                if (GUILayout.Toggle(on, s + "x") && !on) { timeScale = s; Time.timeScale = s; }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Camera pitch " + Mathf.RoundToInt(cf.pitch) + "°", GUILayout.Width(120));
                cf.pitch = Mathf.Round(GUILayout.HorizontalSlider(cf.pitch, 25f, 85f));
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            lockTime = GUILayout.Toggle(lockTime, "Lock time", GUILayout.Width(120));
            timeOfDay = GUILayout.HorizontalSlider(timeOfDay, 0f, 1f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Floor relief " + (relief + 1f).ToString("0.0") + "x", GUILayout.Width(120));
            relief = GUILayout.HorizontalSlider(relief, -1f, 3f);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("Monsters  (level " + level + ", " + rank + ")");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-5")) level = Mathf.Max(1, level - 5);
            if (GUILayout.Button("+5")) level += 5;
            foreach (MonsterRank r in new[] { MonsterRank.Normal, MonsterRank.Elite, MonsterRank.Rare })
                if (GUILayout.Toggle(rank == r, r.ToString()) && rank != r) rank = r;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool p = GUILayout.Toggle(passive, "Passive");
            bool f = GUILayout.Toggle(frozen, "Frozen");
            if (p != passive || f != frozen) { passive = p; frozen = f; ApplyAll(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Gallery")) SpawnGallery();
            if (GUILayout.Button("Clear")) ClearMonsters();
            GUILayout.EndHorizontal();

            GUILayout.Label("Spawn next to the hero:");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(260));
            var h = HeroController.Instance;
            for (int i = 0; i < kinds.Count; i++)
                if (GUILayout.Button(kinds[i].name) && h != null)
                    Spawn(i, h.transform.G() + Random.insideUnitCircle.normalized * 3f);
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }
    }
}
