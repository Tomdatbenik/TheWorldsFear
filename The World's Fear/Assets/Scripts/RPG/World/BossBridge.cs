using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TWF
{
    // Connects the original boss (BossAttack + legacy Health) to the RPG combat system.
    // The boss gets real stats; its old Health mirrors the new life so its rage phases still trigger.
    [RequireComponent(typeof(Combatant))]
    public class BossBridge : MonoBehaviour
    {
        public int level = 25;
        public float hpMultiplier = 22f;
        public string displayName = "The World's Fear";
        public string victoryScene = "Victory";

        Combatant combatant;
        global::Health legacy;
        int legacyMax;

        void Awake()
        {
            // The boss scales with how far out its gate was.
            level = Mathf.Max(5, WorldManager.PendingBossLevel);
            combatant = GetComponent<Combatant>();
            legacy = GetComponent<global::Health>();
            combatant.team = Team.Enemy;
            combatant.rank = MonsterRank.Boss;
            combatant.level = level;
            combatant.displayName = displayName;
            combatant.destroyOnDeath = false;
            combatant.radius = 3f;
            combatant.Stats.SetBase(StatType.MaxHP, MonsterGenerator.BaseHP(level) * hpMultiplier);
            combatant.Stats.SetBase(StatType.Damage, MonsterGenerator.BaseDamage(level) * 1.5f);
            combatant.Stats.SetBase(StatType.Armor, MonsterGenerator.BaseArmor(level) * 1.5f);
            combatant.Stats.SetBase(StatType.MoveSpeed, 0f);
            combatant.ResetLife();
        }

        void Start()
        {
            legacyMax = legacy != null ? Mathf.Max(1, legacy.HealthPoints) : 1;
            // The boss's attack triggers check against the hero's collider.
            var heroCollider = HeroController.Instance != null ? HeroController.Instance.GetComponent<Collider2D>() : null;
            var root = transform.root;
            foreach (var f in root.GetComponentsInChildren<FistAttack>(true)) f.playercollider = heroCollider;
            foreach (var f in root.GetComponentsInChildren<FistAttackClose>(true)) f.playercollider = heroCollider;
            foreach (var r in root.GetComponentsInChildren<RiskSpotAttack>(true)) r.playercollider = heroCollider;
            combatant.Died += OnDied;
            // The old fist and fire attacks deal Strength x scale; scale them to the boss level.
            if (HeroController.Instance != null) HeroController.Instance.Combatant.legacyDamageScale = 2f + level * 0.35f;
            GameUI.Instance?.ShowBossBar(combatant);
            ZoneLighting.Instance?.SetBoss();
            GameUI.Instance?.ShowZoneBanner(displayName, $"Level {level} boss", new Color(1f, 0.4f, 0.3f));
        }

        void Update()
        {
            if (legacy != null && !combatant.IsDead)
                legacy.HealthPoints = Mathf.Max(1, Mathf.CeilToInt(combatant.HPFraction * legacyMax));
        }

        void OnDied(Combatant killer)
        {
            var attack = GetComponentInParent<BossAttack>();
            if (attack != null) attack.enabled = false;
            StartCoroutine(Victory());
        }

        IEnumerator Victory()
        {
            // Rewards go straight into your bag, since the scene changes.
            var rng = new System.Random();
            Profile.AddXP(Profile.MonsterXP(level, MonsterRank.Boss));
            Profile.AddGold(level * 120);
            for (int i = 0; i < 3; i++)
            {
                var rarity = i == 0 ? Rarity.Legendary : ItemGenerator.RollRarity(rng, MonsterRank.Boss);
                Profile.AddItem(ItemGenerator.Generate(level + 2, rarity, ItemGenerator.RandomSlot(rng), rng));
            }
            Profile.Data.bossDefeated = true;
            Profile.Data.bossesKilled++;
            Profile.Save();

            GameUI.Instance?.Toast("The World's Fear is defeated!\nLegendary loot added to your bag", Palette.Gold, 4f);
            for (int i = 0; i < 12; i++)
            {
                VFX.Burst(transform.position + (Vector3)Random.insideUnitCircle * 3f, new Color(1f, 0.5f, 0.2f), 20, 2f);
                CameraFollow.Shake(0.3f);
                yield return new WaitForSeconds(0.25f);
            }
            if (legacy != null) legacy.HealthPoints = 0;
            yield return new WaitForSeconds(0.5f);
            SceneManager.LoadScene(victoryScene);
        }
    }

    public static class BossHelpers
    {
        // Minions summoned by the boss: generated monsters from the Ember Fields.
        public static void SpawnMinions(Vector2 near, int count)
        {
            var zone = GameDatabase.Instance.FindZone("ember");
            var species = MonsterGenerator.SpeciesFor(zone);
            if (species.Count == 0) return;
            for (int i = 0; i < count; i++)
            {
                var sp = species[Random.Range(0, species.Count)];
                var m = MonsterBrain.Spawn(sp, zone.maxLevel, MonsterRank.Normal, near + Random.insideUnitCircle * 3f, null, zone);
                m.leashRange = 100f;
                m.Aggro();
            }
        }

        // Standing in the boss's risk spot doubles your damage for a moment.
        public static void RiskSpotBuff()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            hero.StartCoroutine(Buff(hero.Combatant));
        }

        static IEnumerator Buff(Combatant c)
        {
            var source = new object();
            c.Stats.AddModifier(new StatModifier(StatType.Damage, ModKind.Percent, 1f, source));
            FloatingText.Spawn(c.transform.position + View.Up * 2f, "Risk taken: x2 damage!", Palette.Gold, 1.1f, 1.5f);
            yield return new WaitForSeconds(5f);
            if (c != null) c.Stats.RemoveModifiersFrom(source);
        }
    }
}
