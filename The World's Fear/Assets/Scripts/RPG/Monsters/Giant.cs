using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Giants roam the land past level 100. They are huge and very strong: they walk towards the
    // hero and smash the ground where the hero stands, after a short warning circle.
    public class Giant : MonoBehaviour
    {
        public const int LandLevel = 100;     // giants walk where the land is at least this level
        public const string MobId = "hill_giant";
        const float Height = 6f;              // world units, about four heroes tall
        const float StompRadius = 4.8f;       // the ground shakes this far round its feet
        const float StompWarning = 1.0f;      // seconds the circle shows before the foot lands
        const float StompEvery = 3.5f;
        const float StepEvery = 0.7f;         // a footfall shake while it walks near the hero

        Combatant self;
        float stompTimer = 2f, stepTimer;
        Vector2 lastPos;
        bool stomping;

        public static MonsterBrain Spawn(Vector2 at, int level)
        {
            var s = MakeSpecies(at);
            var m = MonsterBrain.Spawn(s, level, MonsterRank.Rare, at, null, WorldGen.RegionAt(at));
            m.name = "Giant";
            m.aggroRange = 18f;
            m.leashRange = 80f;
            m.GetComponent<GroundBody>().mass = 60f;   // the hero can't push it around
            m.gameObject.AddComponent<Giant>();
            return m;
        }

        // The Hill Giant from the art pipeline; until it is installed, the biggest humanoid mob there is.
        static MonsterSpecies MakeSpecies(Vector2 at)
        {
            var def = MobCatalog.Get(MobId);
            if (def == null)
                foreach (var d in MobCatalog.All)
                    if (d.body == "humanoid" && (def == null || d.heightUnits > def.heightUnits)) def = d;
            var s = MonsterGenerator.CreateMob(def);
            s.name = def.id == MobId ? def.name : "Giant";
            s.archetype = MonsterArchetype.Brute;
            s.scale = Mathf.Clamp(Height / Mathf.Max(0.5f, def.heightUnits), 1f, 4f);
            s.hpMult = 12f;
            s.damageMult = 2.5f;
            s.armorMult = 2f;
            s.moveSpeed = 2.4f;
            s.attackRange = 2.6f;
            s.attackCooldown = 2f;
            s.element = ElementPart.Physical;
            return s;
        }

        void Awake() => self = GetComponent<Combatant>();

        void Start()
        {
            self.Died += OnDied;
            lastPos = transform.G();
        }

        void Update()
        {
            if (self.IsDead) return;
            var hero = HeroController.Instance;
            if (hero == null || hero.Combatant.IsDead) return;
            Vector2 pos = transform.G();
            float dist = Vector2.Distance(hero.transform.G(), pos);

            // Heavy footfalls: the screen shakes a little with every step when it walks near you.
            float moved = (pos - lastPos).magnitude;
            lastPos = pos;
            if (!stomping && moved > 0.001f && dist < 18f)
            {
                stepTimer -= Time.deltaTime;
                if (stepTimer <= 0f)
                {
                    stepTimer = StepEvery;
                    CameraFollow.Shake(Mathf.Lerp(0.18f, 0.03f, dist / 18f));
                    VFX.Burst(pos, new Color(0.55f, 0.48f, 0.4f), 6, 0.8f);
                }
            }

            if (self.IsStunned || stomping) return;
            stompTimer -= Time.deltaTime;
            if (stompTimer > 0f || dist > StompRadius + 1.5f) return;
            stompTimer = StompEvery * Random.Range(0.8f, 1.2f);
            StartCoroutine(Stomp());
        }

        // It stops, lifts a foot and stamps it down: everything round its feet is hit, stunned and thrown back.
        IEnumerator Stomp()
        {
            stomping = true;
            var hold = new StatModifier(StatType.MoveSpeed, ModKind.Percent, -1f, this);
            self.Stats.AddModifier(hold);
            Vector2 feet = transform.G();
            AreaEffect.Spawn(self, feet, new AreaSpec
            {
                radius = StompRadius,
                delay = StompWarning,
                ticks = 1,
                damageMult = 2.2f,
                status = StatusSpec.Stun(0.6f),
                color = new Color(1f, 0.55f, 0.3f),
                knockback = 6f,
            });
            yield return new WaitForSeconds(StompWarning);
            CameraFollow.Shake(0.6f);
            VFX.Ring(feet, new Color(0.85f, 0.75f, 0.6f), StompRadius, 0.35f);
            VFX.Burst(feet, new Color(0.6f, 0.5f, 0.4f), 30, 2.5f);
            yield return new WaitForSeconds(0.4f);
            self.Stats.RemoveModifiersFrom(this);
            stomping = false;
        }

        // A giant is worth the fight: it drops a second load of loot.
        void OnDied(Combatant killer)
        {
            Loot.Drop(self, transform.G() + Vector2.right * 1.5f);
        }
    }

    // Keeps a few giants walking around the hero while the hero is in level 100+ land.
    public class GiantSpawner : MonoBehaviour
    {
        const int MaxGiants = 2;
        const float CheckEvery = 6f;
        const float SpawnMin = 24f, SpawnMax = 34f;   // just off screen
        const float Despawn = 70f;

        readonly List<MonsterBrain> giants = new List<MonsterBrain>();
        float timer = 3f;

        public static void Create() => new GameObject("GiantSpawner").AddComponent<GiantSpawner>();

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = CheckEvery;
            // Only in the open world, not in a dungeon or a boss arena.
            if (WorldManager.Instance == null || DungeonSetup.Instance != null || BossArenaSetup.Instance != null) return;
            var hero = HeroController.Instance;
            if (hero == null || hero.Combatant.IsDead) return;
            Vector2 p = hero.transform.G();

            giants.RemoveAll(g => g == null || g.Combatant.IsDead);
            foreach (var g in giants.ToArray())
                if (Vector2.Distance(g.transform.G(), p) > Despawn) { giants.Remove(g); Destroy(g.gameObject); }

            if (WorldGen.LevelAt(p) < Giant.LandLevel || giants.Count >= MaxGiants) return;
            for (int tries = 0; tries < 12; tries++)
            {
                Vector2 at = p + Random.insideUnitCircle.normalized * Random.Range(SpawnMin, SpawnMax);
                int lv = WorldGen.LevelAt(at);
                if (lv < Giant.LandLevel || !WorldGen.Walkable(WorldGen.CellAt(Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.y)))) continue;
                giants.Add(Giant.Spawn(at, lv + 5));
                return;
            }
        }
    }
}
