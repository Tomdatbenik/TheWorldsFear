using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    public enum LairKind { GoblinCamp, SpiderNest, MireDen, BoneCairn, CinderMound }

    // A monster lair in the open world: a goblin camp round a fire, a spider nest, a den. Its huts or nests are
    // targets with health of their own, and while any of them stands, monsters keep coming out of them.
    // Farm the lair instead of breaking it and it answers: after enough kills a champion comes out, two levels up.
    // Twenty seconds later, if the lair still stands and that champion is dead, the next one comes, a level higher
    // again, and so on until the lair is destroyed.
    public class MonsterLair : MonoBehaviour
    {
        public const float ActivateRange = 22f, ResetRange = 42f;
        public const int MaxSize = 5;                  // lair levels: 1 = one or two huts, 5 = five
        const float GrowEvery = 240f;                  // a lair left standing grows a level every four minutes
        const int AlivePerSpawner = 2;
        const float ChampionEvery = 20f;
        static float Clock => Profile.Data.playTime;   // play time of this run: saved, so lairs keep growing across restarts

        public LairKind kind;
        public ZoneDefinition zone;
        public int level;                              // the land's level, where the monsters start
        public int seed;
        public int size;                               // the lair's own level, 1 to 5

        LairRecord state;                              // saved with the run: a destroyed lair stays destroyed
        float SpawnEvery => 6.5f - 0.6f * (size - 1);
        int ChampionAfter => 12 - 2 * (size - 1);      // kills of lair monsters before the first champion
        int MinionLevel => level + (size - 1) / 2;
        int SpawnerCount(int s) => s == 1 ? 1 + (seed & 1) : s;

        readonly List<Combatant> spawners = new List<Combatant>();
        readonly List<float> nextSpawn = new List<float>();
        readonly List<MonsterBrain> alive = new List<MonsterBrain>();
        MonsterBrain champion;
        int kills, championLevel;
        float nextChampionAt = -1f, farSince = -1f;
        bool active, cleared;
        string key;
        System.Random rng;
        List<MonsterSpecies> roster;
        MonsterSpecies championSpecies;

        public static string Title(LairKind k)
        {
            switch (k)
            {
                case LairKind.GoblinCamp: return "Goblin Camp";
                case LairKind.SpiderNest: return "Spider Nest";
                case LairKind.MireDen: return "Mire Den";
                case LairKind.BoneCairn: return "Bone Cairn";
                default: return "Cinder Mound";
            }
        }

        static string SpawnerName(LairKind k)
        {
            switch (k)
            {
                case LairKind.GoblinCamp: return "Goblin Hut";
                case LairKind.SpiderNest: return "Spider Nest";
                case LairKind.MireDen: return "Mire Den";
                case LairKind.BoneCairn: return "Bone Cairn";
                default: return "Cinder Mound";
            }
        }

        public static LairKind KindFor(Biome b, uint hash)
        {
            switch (b)
            {
                case Biome.Marsh: return LairKind.MireDen;
                case Biome.Ruins: return LairKind.BoneCairn;
                case Biome.Ember: return LairKind.CinderMound;
                default: return (hash & 1) == 0 ? LairKind.GoblinCamp : LairKind.SpiderNest;
            }
        }

        static string[] Minions(LairKind k)
        {
            switch (k)
            {
                case LairKind.GoblinCamp: return new[] { "goblin_cutthroat", "goblin_cutthroat", "goblin_archer", "goblin_hexer", "goblin_shieldbearer" };
                case LairKind.SpiderNest: return new[] { "forest_spider", "thicket_skitter", "thicket_skitter" };
                case LairKind.MireDen: return new[] { "bog_toad", "mire_stalker", "bloat_slime", "gnat_swarmling" };
                case LairKind.BoneCairn: return new[] { "shambling_dead", "shambling_dead", "bone_archer", "crypt_blade", "skeleton_guard" };
                default: return new[] { "cinder_mite", "cinder_mite", "fire_slime", "ember_imp", "scorch_stalker" };
            }
        }

        static string ChampionId(LairKind k)
        {
            switch (k)
            {
                case LairKind.GoblinCamp: return "goblin_warchief";
                case LairKind.SpiderNest: return "forest_spider";
                case LairKind.MireDen: return "mudshell_turtle";
                case LairKind.BoneCairn: return "death_knight";
                default: return "magma_colossus";
            }
        }

        public void Init(LairKind k, ZoneDefinition z, int lvl, int s)
        {
            kind = k; zone = z; level = Mathf.Max(1, lvl); seed = s;
            rng = new System.Random(s);
            key = Mathf.RoundToInt(transform.position.x) + "_" + Mathf.RoundToInt(transform.position.y);
            roster = new List<MonsterSpecies>();
            foreach (var id in Minions(k))
            {
                var d = MobCatalog.Get(id);
                if (d != null) roster.Add(MonsterGenerator.CreateMob(d));
            }
            if (roster.Count == 0 && z != null) roster.AddRange(MonsterGenerator.SpeciesFor(z));
            var cd = MobCatalog.Get(ChampionId(k));
            championSpecies = cd != null ? MonsterGenerator.CreateMob(cd) : roster.Count > 0 ? roster[0] : null;
            if (championSpecies != null && k == LairKind.SpiderNest)
            {
                championSpecies.name = "Nest Matron";
                championSpecies.scale = 1.45f;
            }
            state = Profile.Lair(key);
            if (state == null) Profile.AddLair(state = NewState());
            bool broken = state.destroyed;
            size = broken ? Mathf.Max(1, state.destroyedSize) : SizeNow();
            championLevel = level + 1 + size;
            BuildBase();
            for (int i = 0; i < SpawnerCount(size); i++) AddSlot(i);
            if (broken)
            {
                cleared = true;
                foreach (var sp in spawners) { sp.Die(null); }
            }
        }

        // Further from the village a lair starts bigger: level 1 near home, up to 3 at the edge.
        LairRecord NewState()
        {
            float d = ((Vector2)transform.position).magnitude;
            int start = 1 + Mathf.Clamp(Mathf.FloorToInt((d - 60f) / 220f), 0, 2) + ((seed >> 3) % 3 == 0 ? 1 : 0);
            return new LairRecord { key = key, born = Clock, startSize = Mathf.Clamp(start, 1, 3) };
        }

        int SizeNow() => Mathf.Clamp(state.startSize + Mathf.FloorToInt((Clock - state.born) / GrowEvery), 1, MaxSize);

        // Left standing, the lair grows: a new hut or nest goes up, and what comes out gets stronger.
        void Grow()
        {
            int target = SizeNow();
            if (target <= size) return;
            int before = SpawnerCount(size);
            size = target;
            for (int i = before; i < SpawnerCount(size); i++)
            {
                AddSlot(i);
                var s = spawners[spawners.Count - 1];
                VFX.Burst(s.transform.position + View.Up * 0.6f, new Color(0.6f, 0.5f, 0.35f), 16, 1f);
            }
            foreach (var s in spawners) if (s != null && !s.IsDead) SetLabel(s);
            var hero = HeroController.Instance;
            if (hero != null && Vector2.Distance(hero.transform.position, transform.position) < ActivateRange)
                FloatingText.Spawn(transform.position + View.Up * 2.8f, $"The {Title(kind).ToLower()} grows!  Lv {size}", new Color(1f, 0.7f, 0.3f), 1.2f, 2f);
            if (nextChampionAt < 0f) championLevel = level + 1 + size;
        }

        // ---------- Life ----------

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null || rng == null) return;
            alive.RemoveAll(m => m == null || m.Combatant.IsDead);
            float d = Vector2.Distance(hero.transform.position, transform.position);
            if (!cleared && Time.frameCount % 30 == 0) Grow();

            if (d > ResetRange)
            {
                if (farSince < 0f) farSince = Time.time;
                if (Time.time - farSince > 5f && active) ResetLair();
                return;
            }
            farSince = -1f;
            if (cleared) return;

            int standing = 0;
            foreach (var s in spawners) if (s != null && !s.IsDead) standing++;
            if (standing == 0) { OnCleared(); return; }

            if (!active)
            {
                if (d > ActivateRange) return;
                active = true;
                // The camp is awake: a first group comes out at once.
                for (int i = 0; i < spawners.Count; i++)
                {
                    if (spawners[i].IsDead) continue;
                    SpawnFrom(spawners[i]);
                    SpawnFrom(spawners[i]);
                    nextSpawn[i] = Time.time + SpawnEvery * (0.6f + 0.25f * i);
                }
            }

            int cap = 1 + standing * AlivePerSpawner;
            for (int i = 0; i < spawners.Count; i++)
            {
                if (spawners[i].IsDead || Time.time < nextSpawn[i]) continue;
                nextSpawn[i] = Time.time + SpawnEvery;
                if (CountMinions() < cap) SpawnFrom(spawners[i]);
            }

            // Farmed without being broken: champions, each one a level above the last.
            bool championDead = champion == null || champion.Combatant.IsDead;
            if (nextChampionAt < 0f)
            {
                if (kills >= ChampionAfter) SummonChampion();
            }
            else if (Time.time >= nextChampionAt && championDead) SummonChampion();
        }

        int CountMinions()
        {
            int n = 0;
            foreach (var m in alive) if (m != champion) n++;
            return n;
        }

        Combatant AnyStanding()
        {
            var list = spawners.FindAll(s => s != null && !s.IsDead);
            return list.Count > 0 ? list[rng.Next(list.Count)] : null;
        }

        Vector2 DoorOf(Combatant s)
        {
            Vector2 away = (Vector2)s.transform.position - (Vector2)transform.position;
            if (away.sqrMagnitude < 0.01f) away = Vector2.down;
            Vector2 side = new Vector2(-away.y, away.x).normalized * (float)(rng.NextDouble() * 2 - 1) * 0.8f;
            return (Vector2)s.transform.position + away.normalized * (s.radius + 0.7f) + side;
        }

        void SpawnFrom(Combatant s)
        {
            if (roster.Count == 0) return;
            var sp = roster[rng.Next(roster.Count)];
            var m = MonsterBrain.Spawn(sp, Mathf.Max(1, MinionLevel + rng.Next(-1, 1)), MonsterRank.Normal, DoorOf(s), null, zone);
            Enlist(m);
            m.Combatant.Died += killer => { if (killer != null && killer.team == Team.Player) kills++; };
            VFX.Burst(m.transform.position + View.Up * 0.4f, new Color(0.35f, 0.3f, 0.25f), 6, 0.4f);
        }

        void Enlist(MonsterBrain m)
        {
            m.transform.SetParent(transform.parent, true);
            m.Home = transform.position;
            m.Pack = alive;
            alive.Add(m);
            // Whoever comes out of a lair that is under attack joins the fight.
            if (HeroController.Instance != null && Vector2.Distance(HeroController.Instance.transform.position, transform.position) < 12f) m.Aggro();
        }

        void SummonChampion()
        {
            var s = AnyStanding();
            if (s == null || championSpecies == null) return;
            int affixes = championLevel >= 12 ? 3 : championLevel >= 6 ? 2 : 1;
            champion = MonsterBrain.Spawn(championSpecies, championLevel, MonsterRank.Rare, DoorOf(s), MonsterSpawner.RollAffixes(rng, affixes), zone);
            Enlist(champion);
            champion.Aggro();
            var at = champion.transform.position + View.Up * 2.4f;
            FloatingText.Spawn(at, championSpecies.name + " comes out!  Lv " + championLevel, new Color(1f, 0.55f, 0.2f), 1.3f, 2.2f);
            VFX.Burst(champion.transform.position + View.Up * 0.6f, new Color(1f, 0.5f, 0.15f), 22, 1.1f);
            Sound.Play(Sfx.MonsterDeath, champion.transform.position, 0.5f);
            championLevel++;
            nextChampionAt = Time.time + ChampionEvery;
        }

        void OnCleared()
        {
            cleared = true;
            state.destroyed = true;
            state.destroyedSize = size;
            Profile.Save();
            Legacy.OnLairDestroyed();
            int bonus = championLevel - (level + 1 + size) + (size - 1);     // champions faced, and the lair's size
            Pickup.SpawnGold((Vector2)transform.position + Random.insideUnitCircle, Mathf.Max(5, level * 12 * (1 + bonus)));
            Profile.AddXP(Profile.MonsterXP(level, MonsterRank.Rare) * (1 + bonus));
            FloatingText.Spawn(transform.position + View.Up * 2.6f, Title(kind) + " destroyed", Palette.Gold, 1.3f, 2.2f);
        }

        // The hero walked away: monsters that came out go back in, broken huts stay broken, the anger cools.
        void ResetLair()
        {
            active = false;
            foreach (var m in alive) if (m != null) Destroy(m.gameObject);
            alive.Clear();
            champion = null;
            kills = 0;
            championLevel = level + 1 + size;
            nextChampionAt = -1f;
            foreach (var s in spawners) if (s != null && !s.IsDead) s.ResetLife();
        }

        void OnDestroy()
        {
            foreach (var m in alive)
                if (m != null) Destroy(m.gameObject);
        }

        // ---------- Building ----------

        // What every lair has whatever its size: the campfire, egg sacs, bones, a glow.
        void BuildBase()
        {
            slotAngle = (float)rng.NextDouble() * 360f;
            switch (kind)
            {
                case LairKind.GoblinCamp: Campfire(Vector2.zero); break;
                case LairKind.SpiderNest:
                    for (int i = 0; i < 4; i++) Prop(Random.insideUnitCircle * 1.1f, 0.35f, 0.4f, "eggsac", EggTex());
                    break;
                case LairKind.MireDen: PointLights.Attach(transform, new Color(0.55f, 0.9f, 0.4f), 7f, 0.9f, new Vector3(0, 0, -1.4f), true); break;
                case LairKind.BoneCairn:
                    for (int i = 0; i < 3; i++) Prop(Random.insideUnitCircle * 1.1f, 0.3f, 0.25f, "bones", BoneTex());
                    break;
                default: PointLights.Attach(transform, new Color(1f, 0.45f, 0.15f), 7f, 1.2f, new Vector3(0, 0, -1.2f), true); break;
            }
        }

        float slotAngle;
        static readonly int[] SlotOrder = { 0, 2, 4, 1, 3 };   // spread round the ring as the lair grows

        // Hut or nest number i, on a ring round the lair's centre.
        void AddSlot(int i)
        {
            float ring = kind == LairKind.GoblinCamp ? 3f : 2.5f;
            float a = (slotAngle + SlotOrder[i % SlotOrder.Length] * 72f) * Mathf.Deg2Rad;
            var at = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
            float k = 0.85f + (float)rng.NextDouble() * 0.25f;
            switch (kind)
            {
                case LairKind.GoblinCamp: AddSpawner(at, 1.05f, 2.3f * k, Hut); break;
                case LairKind.SpiderNest: AddSpawner(at, 1.15f * k, 1.4f * k, (go, r, h) => Dome(go, r, h, "web", WebTex(), true)); break;
                case LairKind.MireDen: AddSpawner(at, 1.2f * k, 1.2f * k, (go, r, h) => { Dome(go, r, h, "mud", MudTex(), true); Hole(go, r, h); }); break;
                case LairKind.BoneCairn: AddSpawner(at, 1.1f * k, 1.5f * k, (go, r, h) => Dome(go, r, h, "cairn", WorldTiles.CliffTexture(Biome.Ruins), true)); break;
                default: AddSpawner(at, 1.1f * k, 1.4f * k, (go, r, h) => { Dome(go, r, h, "cinder", CinderTex(), true); Vent(go, r, h); }); break;
            }
        }

        void SetLabel(Combatant c)
        {
            foreach (var t in c.GetComponentsInChildren<TMPro.TMP_Text>(true))
                t.text = $"{SpawnerName(kind)}  Lv {size}";
        }

        delegate void Shape(GameObject go, float radius, float height);

        void AddSpawner(Vector2 offset, float radius, float height, Shape shape)
        {
            var go = new GameObject(SpawnerName(kind));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = offset;
            go.layer = LayerMask.NameToLayer("Monsters");
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            shape(visual, radius, height);
            go.AddComponent<CircleCollider2D>().radius = radius * 0.9f;

            var c = go.AddComponent<Combatant>();
            c.team = Team.Enemy;
            c.level = level + size - 1;
            c.rank = MonsterRank.Elite;
            c.radius = radius;
            c.height = height;
            c.destroyOnDeath = false;
            c.displayName = SpawnerName(kind);
            c.Stats.SetBase(StatType.MaxHP, MonsterGenerator.BaseHP(c.level) * 7f);
            c.Stats.SetBase(StatType.Armor, MonsterGenerator.BaseArmor(c.level) * 1.2f);
            c.Stats.SetBase(StatType.Damage, 0f);
            c.Stats.SetBase(StatType.MoveSpeed, 0f);
            c.ResetLife();
            WorldBar.Attach(c, height + 0.45f, 1.7f, $"{SpawnerName(kind)}  Lv {size}", new Color(0.95f, 0.75f, 0.5f), new Color(0.85f, 0.45f, 0.15f), false);
            c.Damaged += (info, dealt) => StartCoroutine(Shake(visual.transform));
            c.Died += killer => OnSpawnerDied(c, visual.transform);
            spawners.Add(c);
            nextSpawn.Add(0f);
        }

        void OnSpawnerDied(Combatant c, Transform visual)
        {
            foreach (var col in c.GetComponents<Collider2D>()) col.enabled = false;
            if (rng != null && !cleared)
            {
                Loot.Drop(c, c.transform.position);
                VFX.Burst(c.transform.position + View.Up * 0.8f, new Color(0.45f, 0.35f, 0.25f), 20, 1.2f);
                Sound.Play(Sfx.MonsterDeath, c.transform.position, 0.5f);
            }
            var bar = c.GetComponentInChildren<WorldBar>();
            if (bar != null) bar.gameObject.SetActive(false);
            if (isActiveAndEnabled) StartCoroutine(Collapse(visual, cleared ? 0f : 0.6f));
            else visual.localScale = new Vector3(1.1f, 1.1f, 0.18f);
        }

        // A broken hut or nest slumps into a low heap of rubble.
        static IEnumerator Collapse(Transform t, float duration)
        {
            float time = 0f;
            var from = t.localScale;
            var to = new Vector3(1.15f, 1.15f, 0.18f);
            while (time < duration && t != null)
            {
                time += Time.deltaTime;
                t.localScale = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, time / duration));
                yield return null;
            }
            if (t != null) t.localScale = to;
        }

        static IEnumerator Shake(Transform t)
        {
            if (t == null || t.localScale.z < 0.5f) yield break;
            float time = 0f;
            while (time < 0.15f && t != null)
            {
                time += Time.deltaTime;
                float k = 1f - time / 0.15f;
                t.localPosition = new Vector3(Mathf.Sin(time * 90f) * 0.06f * k, 0f, 0f);
                yield return null;
            }
            if (t != null) t.localPosition = Vector3.zero;
        }

        void Campfire(Vector2 at)
        {
            var go = new GameObject("Campfire");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            var mb = new Mesh3();
            // Stones in a ring, logs crossed in the middle.
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                mb.Dome(new Vector3(Mathf.Cos(a) * 0.62f, Mathf.Sin(a) * 0.62f, 0f), 0.17f, 0.16f, 6, 2);
            }
            Render(go, "campstones", mb, WorldTiles.CliffTexture(Biome.Ruins), true);
            var logs = new Mesh3();
            logs.Box(new Vector3(0, 0, -0.08f), new Vector3(0.9f, 0.16f, 0.16f), 30f);
            logs.Box(new Vector3(0, 0, -0.18f), new Vector3(0.9f, 0.16f, 0.16f), -40f);
            Render(go, "logs", logs, WoodTex(), true);
            var fire = Fx.Play("flame_fire", (Vector2)go.transform.position, 0.75f, null, 0f, 1e7f);
            if (fire != null) fire.transform.SetParent(go.transform, true);
            PointLights.Attach(go.transform, new Color(1f, 0.55f, 0.2f), 7f, 1.6f, new Vector3(0, 0, -0.8f), true);
            go.AddComponent<CircleCollider2D>().radius = 0.65f;
            go.layer = LayerMask.NameToLayer("Monsters");
            go.AddComponent<HeightProp>();
        }

        void Hut(GameObject go, float r, float h)
        {
            float wall = h * 0.45f;
            var walls = new Mesh3();
            walls.Ring(Vector3.zero, r, r, 0f, wall, 9, 1, 0);
            Render(go, "hutwall", walls, WoodTex(), true);
            var roof = new Mesh3();
            roof.Cone(new Vector3(0, 0, -wall), r * 1.3f, h - wall, 9);
            Render(go, "hutroof", roof, ThatchTex(), true);
            // The door faces the fire.
            Vector2 toFire = -(Vector2)go.transform.parent.localPosition;
            var door = new Mesh3();
            Vector2 n = toFire.normalized;
            Vector2 u = new Vector2(-n.y, n.x);
            Vector3 o = (Vector3)(n * (r * 0.97f)) - (Vector3)(u * 0.28f);
            door.Quad(o + Vector3.back * 0.01f, (Vector3)u, Vector3.back, (Vector3)n, 0.56f, wall * 0.9f);
            Render(go, "hutdoor", door, DarkTex(), false);
        }

        static void Dome(GameObject go, float r, float h, string key, Texture2D tex, bool shadow)
        {
            var m = new Mesh3();
            m.Dome(Vector3.zero, r, h, 12, 5);
            Render(go, key, m, tex, shadow);
        }

        // A glowing lava vent on top of a cinder mound.
        static void Vent(GameObject go, float r, float h)
        {
            var m = new Mesh3();
            m.Dome(new Vector3(0, 0, -h * 0.82f), r * 0.42f, h * 0.22f, 10, 2);
            var child = new GameObject("vent");
            child.transform.SetParent(go.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = m.Build("vent");
            var mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RenderAssets.Instance.lava;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // The dark mouth of a den, on the side facing the camera.
        static void Hole(GameObject go, float r, float h)
        {
            var m = new Mesh3();
            m.Quad(new Vector3(-0.35f, -r * 0.93f, -0.02f), Vector3.right, Vector3.back, Vector3.down, 0.7f, h * 0.45f);
            Render(go, "denhole", m, DarkTex(), false);
        }

        void Prop(Vector2 at, float r, float h, string key, Texture2D tex)
        {
            var go = new GameObject(key);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            var m = new Mesh3();
            m.Dome(Vector3.zero, r, h, 8, 3);
            Render(go, key, m, tex, false);
            go.AddComponent<HeightProp>();
        }

        static void Render(GameObject go, string key, Mesh3 mb, Texture2D tex, bool shadows)
        {
            var child = new GameObject(key);
            child.transform.SetParent(go.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mb.Build(key);
            var r = child.AddComponent<MeshRenderer>();
            r.sharedMaterial = WorldTiles.MeshMaterial("lair_" + key, tex);
            r.shadowCastingMode = shadows ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        // ---------- Textures ----------

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        static Texture2D Tex(string key, System.Func<int, int, System.Random, Color> paint)
        {
            if (textures.TryGetValue(key, out var t) && t) return t;
            var rng = new System.Random(key.GetHashCode() & 0xffff);
            t = GameAssets.NewTexture(16, 16);
            t.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                t.SetPixel(x, y, paint(x, y, rng));
            t.Apply();
            textures[key] = t;
            return t;
        }

        static float N(System.Random r, float a) => 1f - a + (float)r.NextDouble() * a * 2f;
        static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        static Texture2D WoodTex() => Tex("lair_wood", (x, y, r) =>
            Mul(new Color(0.42f, 0.27f, 0.15f), (x % 4 == 0 ? 0.6f : 1f) * N(r, 0.1f)));
        static Texture2D ThatchTex() => Tex("lair_thatch", (x, y, r) =>
            Mul(new Color(0.66f, 0.52f, 0.26f), ((x + y / 3) % 3 == 0 ? 0.72f : 1f) * (y % 5 == 0 ? 0.8f : 1f) * N(r, 0.12f)));
        static Texture2D DarkTex() => Tex("lair_dark", (x, y, r) => Mul(new Color(0.09f, 0.06f, 0.05f), N(r, 0.2f)));
        static Texture2D WebTex() => Tex("lair_web", (x, y, r) =>
            Mul(new Color(0.78f, 0.78f, 0.74f), ((x + y) % 5 == 0 || (x - y + 16) % 6 == 0 ? 1.15f : 0.82f) * N(r, 0.08f)));
        static Texture2D EggTex() => Tex("lair_egg", (x, y, r) => Mul(new Color(0.86f, 0.84f, 0.7f), N(r, 0.08f)));
        static Texture2D MudTex() => Tex("lair_mud2", (x, y, r) =>
            Mul(r.NextDouble() < 0.3 ? new Color(0.5f, 0.66f, 0.3f) : new Color(0.55f, 0.44f, 0.3f), N(r, 0.12f)));
        static Texture2D BoneTex() => Tex("lair_bone", (x, y, r) => Mul(new Color(0.86f, 0.82f, 0.7f), (x % 3 == 0 ? 0.75f : 1f) * N(r, 0.1f)));
        static Texture2D CinderTex() => Tex("lair_cinder2", (x, y, r) =>
            (x + y * 2) % 9 == 0 || (x * 3 - y + 32) % 11 == 0 ? new Color(1f, 0.55f, 0.15f) : Mul(new Color(0.36f, 0.33f, 0.32f), N(r, 0.15f)));

        // ---------- Mesh helper (up is -Z) ----------

        class Mesh3
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();

            int Vert(Vector3 p, Vector3 normal, Vector2 tc) { v.Add(p); n.Add(normal); uv.Add(tc); return v.Count - 1; }
            void Tri(int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(b); }   // both faces

            public void Quad(Vector3 o, Vector3 u, Vector3 up, Vector3 normal, float w, float h)
            {
                int a = Vert(o, normal, Vector2.zero), b = Vert(o + u * w, normal, new Vector2(w, 0));
                int c = Vert(o + up * h, normal, new Vector2(0, h)), d = Vert(o + u * w + up * h, normal, new Vector2(w, h));
                Tri(a, c, b); Tri(c, d, b);
            }

            // A band between two rings: radius r0 at height z0 to radius r1 at z1 (heights measured upward).
            public void Ring(Vector3 c, float r0, float r1, float z0, float z1, int seg, float uvScale, float flare)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f); var d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                    float arc = r0 * (a1 - a0);
                    int p = Vert(c + d0 * r0 + Vector3.back * z0, d0, new Vector2(i * arc, 0));
                    int q = Vert(c + d1 * r0 + Vector3.back * z0, d1, new Vector2((i + 1) * arc, 0));
                    int s = Vert(c + d0 * r1 + Vector3.back * z1, (d0 + Vector3.back * flare).normalized, new Vector2(i * arc, z1 - z0));
                    int w = Vert(c + d1 * r1 + Vector3.back * z1, (d1 + Vector3.back * flare).normalized, new Vector2((i + 1) * arc, z1 - z0));
                    Tri(p, s, q); Tri(s, w, q);
                }
            }

            public void Cone(Vector3 baseCentre, float r, float h, int seg)
            {
                var apex = baseCentre + Vector3.back * h;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f); var d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                    var nm = ((d0 + d1).normalized * h + Vector3.back * r).normalized;
                    float slant = Mathf.Sqrt(r * r + h * h), arc = r * (a1 - a0);
                    int p = Vert(baseCentre + d0 * r, nm, new Vector2(i * arc, 0));
                    int q = Vert(baseCentre + d1 * r, nm, new Vector2((i + 1) * arc, 0));
                    int s = Vert(apex, nm, new Vector2((i + 0.5f) * arc, slant));
                    Tri(p, s, q);
                }
            }

            public void Dome(Vector3 c, float r, float h, int seg, int rings)
            {
                for (int k = 0; k < rings; k++)
                {
                    float p0 = k * Mathf.PI * 0.5f / rings, p1 = (k + 1) * Mathf.PI * 0.5f / rings;
                    Ring(c, r * Mathf.Cos(p0), r * Mathf.Cos(p1), h * Mathf.Sin(p0), h * Mathf.Sin(p1), seg, 1f, (k + 0.5f) / rings * 1.5f);
                }
            }

            public void Box(Vector3 centre, Vector3 size, float angleDeg)
            {
                var rot = Quaternion.Euler(0, 0, angleDeg);
                Vector3 ex = rot * Vector3.right * size.x * 0.5f, ey = rot * Vector3.up * size.y * 0.5f, ez = Vector3.back * size.z * 0.5f;
                void Face(Vector3 nrm, Vector3 a, Vector3 b)
                {
                    var o = centre + nrm - a - b;
                    Quad(o, a.normalized, b.normalized, nrm.normalized, a.magnitude * 2f, b.magnitude * 2f);
                }
                Face(ez, ex, ey); Face(-ez, ex, ey); Face(ey, ex, ez); Face(-ey, ex, ez); Face(ex, ey, ez); Face(-ex, ey, ez);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
                var colors = new Color[v.Count];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                m.colors = colors;
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }

    // A small loose prop of a lair (an egg sac, a heap of bones): stands at the terrain height under it.
    public class HeightProp : MonoBehaviour
    {
        void Start()
        {
            if (TerrainHeight.World == null) return;
            var p = transform.position;
            transform.position = new Vector3(p.x, p.y, -TerrainHeight.At(p));
        }
    }
}
