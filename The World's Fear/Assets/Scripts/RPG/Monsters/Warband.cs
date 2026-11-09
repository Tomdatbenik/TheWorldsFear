using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // An organised group of one faction (goblins, undead), a harder tier than the ordinary small packs: a commander, shields in front of
    // the shooters, melee that surround the hero instead of queueing, shooters that fire together, and a loss of nerve when the commander falls.
    public class Warband
    {
        public const float SpawnChance = 0.12f;       // of the packs in a region that has a faction
        const float VolleyEvery = 4.5f;
        const float HpBonus = 0.35f, DamageBonus = 0.15f;  // only for warband members; ordinary mobs keep the old balance
        const float BrokenCooldown = 1.35f;           // longer between attacks once the commander is dead

        public readonly List<MonsterBrain> Members = new List<MonsterBrain>();
        public MonsterBrain Leader { get; private set; }
        public bool Broken { get; private set; }
        float volleyTimer = VolleyEvery * 0.5f;
        int tickedFrame = -1;

        public float CooldownMult => Broken ? BrokenCooldown : 1f;

        public static bool Spawn(ZoneDefinition zone, Biome biome, Vector2 center, int level, List<MonsterBrain> into, System.Random rng)
        {
            var factions = MonsterGenerator.WarbandFactions(biome);
            if (factions.Count == 0) return false;
            var faction = factions[rng.Next(factions.Count)];
            var roster = MonsterGenerator.WarbandRoster(biome, faction);
            var leaders = roster.FindAll(s => s.mob.leader);
            var ranged = roster.FindAll(s => s.preferredDistance > 0f);
            var guards = roster.FindAll(s => s.archetype == MonsterArchetype.Guardian);
            var melee = roster.FindAll(s => !s.mob.leader && s.preferredDistance <= 0f && s.archetype != MonsterArchetype.Guardian);
            if (leaders.Count == 0 || melee.Count == 0) return false;

            var band = new Warband();
            Vector2 Around(float r) => center + new Vector2((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * r;
            void Add(MonsterSpecies s, MonsterRank rank, float spread)
            {
                var m = MonsterBrain.Spawn(s, level, rank, Around(spread), null, zone);
                band.Enlist(m);
                into.Add(m);
            }
            var chief = leaders[rng.Next(leaders.Count)];
            Add(chief, MonsterRank.Elite, 0.8f);
            band.Leader = band.Members[0];
            int guardCount = guards.Count > 0 ? 1 + rng.Next(2) : 0;
            int shooters = ranged.Count > 0 ? 2 + rng.Next(2) : 0;
            int fighters = 3 + rng.Next(3);
            for (int i = 0; i < guardCount; i++) Add(guards[rng.Next(guards.Count)], MonsterRank.Normal, 2.5f);
            for (int i = 0; i < shooters; i++) Add(ranged[rng.Next(ranged.Count)], MonsterRank.Normal, 3.2f);
            for (int i = 0; i < fighters; i++) Add(melee[rng.Next(melee.Count)], MonsterRank.Normal, 2.8f);
            foreach (var m in band.Members)
            {
                m.Pack = band.Members;
                m.Home = m.transform.position;
            }
            return true;
        }

        void Enlist(MonsterBrain m)
        {
            Members.Add(m);
            m.Band = this;
            var c = m.Combatant;
            c.Stats.AddModifier(new StatModifier(StatType.MaxHP, ModKind.Percent, HpBonus, this));
            c.Stats.AddModifier(new StatModifier(StatType.Damage, ModKind.Percent, DamageBonus, this));
            c.ResetLife();
        }

        // Called from every member's fight logic; runs once a frame.
        public void Tick()
        {
            if (tickedFrame == Time.frameCount) return;
            tickedFrame = Time.frameCount;
            if (Broken) return;
            if (Leader == null || Leader.Combatant.IsDead)
            {
                Broken = true;
                foreach (var m in Members)
                    if (m != null && !m.Combatant.IsDead) VFX.Burst(m.transform.position + View.Up * 0.5f, new Color(0.8f, 0.8f, 0.9f), 6, 0.4f);
                return;
            }
            volleyTimer -= Time.deltaTime;
            if (volleyTimer > 0f) return;
            volleyTimer = VolleyEvery;
            VFX.Ring(Leader.transform.position + View.Up * 0.3f, new Color(1f, 0.8f, 0.3f), 1.6f, 0.35f);
            foreach (var m in Members)
                if (m != null && !m.Combatant.IsDead && m.Species.preferredDistance > 0f) m.OrderAttack();
        }

        // A place on a ring round the hero, spaced so the band's melee come from all sides. Each member keeps its own slot.
        public Vector2 FlankPoint(MonsterBrain m, Vector2 hero, float ring)
        {
            int count = 0, mine = 0;
            foreach (var o in Members)
            {
                if (o == null || o.Combatant.IsDead || o.Species.preferredDistance > 0f || o.Species.archetype == MonsterArchetype.Guardian) continue;
                if (o == m) mine = count;
                count++;
            }
            float angle = Mathf.PI * 2f * mine / Mathf.Max(1, count);
            return hero + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring;
        }

        // Somewhere a wounded member can fall back to: behind a guardian if one still stands.
        public Vector2 SafeSpot(MonsterBrain m, Vector2 hero, Vector2 pos)
        {
            MonsterBrain best = null;
            float bestD = float.MaxValue;
            foreach (var o in Members)
            {
                if (o == null || o == m || o.Combatant.IsDead || o.Species.archetype != MonsterArchetype.Guardian) continue;
                float d = ((Vector2)o.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = o; }
            }
            Vector2 away = (pos - hero).normalized;
            return best != null ? (Vector2)best.transform.position + away * 1.6f : pos + away * 3f;
        }
    }
}
