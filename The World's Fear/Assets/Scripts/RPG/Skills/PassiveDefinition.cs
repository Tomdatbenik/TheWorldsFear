using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TWF
{
    [CreateAssetMenu(menuName = "TWF/Passive")]
    public class PassiveDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public string glyph = "+";
        public Color color = new Color(0.8f, 0.7f, 0.4f);
        public int maxRank = 3;

        [Tooltip("Values are per rank: rank 3 gives 3x the value.")]
        public List<StatModifier> modifiersPerRank = new List<StatModifier>();

        [SerializeReference] public List<PassiveProc> procs = new List<PassiveProc>();

        // Applies to a combatant; returns an action that removes it again.
        public Action Apply(Combatant owner, int rank)
        {
            var source = new object();
            foreach (var m in modifiersPerRank)
                owner.Stats.AddModifier(new StatModifier(m.stat, m.kind, m.value * rank, source));

            var cleanups = new List<Action>();
            foreach (var p in procs)
                if (p != null) cleanups.Add(p.Attach(owner, rank));

            return () =>
            {
                owner.Stats.RemoveModifiersFrom(source);
                foreach (var c in cleanups) c?.Invoke();
            };
        }

        public string Describe(int rank)
        {
            int r = Mathf.Max(1, rank);
            var sb = new StringBuilder(description);
            foreach (var m in modifiersPerRank)
                sb.Append("\n<color=#bbb>• ").Append(StatInfo.Format(m.stat, m.kind, m.value * r)).Append("</color>");
            foreach (var p in procs)
                if (p != null) sb.Append("\n<color=#bbb>• ").Append(p.Describe(r)).Append("</color>");
            return sb.ToString();
        }
    }

    [Serializable]
    public abstract class PassiveProc
    {
        public abstract Action Attach(Combatant owner, int rank);
        public abstract string Describe(int rank);
    }

    [Serializable]
    public class StatusOnHitProc : PassiveProc
    {
        public float chancePerRank = 0.1f;
        public StatusSpec status;

        public override Action Attach(Combatant owner, int rank)
        {
            Action<Combatant, DamageInfo, float> h = (target, info, dealt) =>
            {
                if (info.silent || target.IsDead) return;
                if (UnityEngine.Random.value < chancePerRank * rank)
                    target.ApplyStatus(new StatusSpec { type = status.type, magnitude = status.magnitude, duration = status.duration, chance = 1f }, owner, info.amount);
            };
            owner.DealtDamage += h;
            return () => owner.DealtDamage -= h;
        }

        public override string Describe(int rank) =>
            $"{Mathf.RoundToInt(chancePerRank * rank * 100)}% chance on hit to {StatusVerb(status.type)}";

        static string StatusVerb(StatusType t)
        {
            switch (t)
            {
                case StatusType.Burn: return "ignite the enemy";
                case StatusType.Poison: return "poison the enemy";
                case StatusType.Slow: return "slow the enemy";
                case StatusType.Stun: return "stun the enemy";
                case StatusType.Chill: return "chill the enemy";
            }
            return "apply " + t;
        }
    }

    // A flat stat bonus (used by legendary item powers).
    [Serializable]
    public class StatBonusProc : PassiveProc
    {
        public StatType stat;
        public ModKind kind = ModKind.Flat;
        public float valuePerRank;

        public override Action Attach(Combatant owner, int rank)
        {
            var source = new object();
            owner.Stats.AddModifier(new StatModifier(stat, kind, valuePerRank * rank, source));
            return () => owner.Stats.RemoveModifiersFrom(source);
        }

        public override string Describe(int rank) => StatInfo.Format(stat, kind, valuePerRank * rank);
    }

    [Serializable]
    public class HealOnKillProc : PassiveProc
    {
        public float percentMaxHPPerRank = 0.02f;

        public override Action Attach(Combatant owner, int rank)
        {
            Action<Combatant> h = victim => owner.Heal(owner.MaxHP * percentMaxHPPerRank * rank);
            owner.Killed += h;
            return () => owner.Killed -= h;
        }

        public override string Describe(int rank) => $"Kills heal {percentMaxHPPerRank * rank * 100f:0.#}% of max life";
    }

    [Serializable]
    public class ExecuteProc : PassiveProc
    {
        [Tooltip("Bonus damage (fraction of the hit) against enemies below the threshold.")]
        public float bonusPerRank = 0.1f;
        public float threshold = 0.35f;

        public override Action Attach(Combatant owner, int rank)
        {
            Action<Combatant, DamageInfo, float> h = (target, info, dealt) =>
            {
                if (info.silent || target.IsDead || target.HPFraction > threshold) return;
                target.TakeDamage(new DamageInfo { source = null, amount = dealt * bonusPerRank * rank, trueDamage = true, silent = true });
            };
            owner.DealtDamage += h;
            return () => owner.DealtDamage -= h;
        }

        public override string Describe(int rank) =>
            $"+{Mathf.RoundToInt(bonusPerRank * rank * 100)}% damage to enemies below {Mathf.RoundToInt(threshold * 100)}% life";
    }

    [Serializable]
    public class LowLifeProc : PassiveProc
    {
        public StatType stat = StatType.Damage;
        public float valuePerRank = 0.1f;
        public float threshold = 0.4f;

        public override Action Attach(Combatant owner, int rank)
        {
            var source = new object();
            bool active = false;
            Action check = () =>
            {
                bool should = !owner.IsDead && owner.HPFraction < threshold;
                if (should == active) return;
                active = should;
                if (active) owner.Stats.AddModifier(new StatModifier(stat, ModKind.Percent, valuePerRank * rank, source));
                else owner.Stats.RemoveModifiersFrom(source);
            };
            Action<DamageInfo, float> onDamaged = (_, __) => check();
            Action<float> onHealed = _ => check();
            owner.Damaged += onDamaged;
            owner.Healed += onHealed;
            return () =>
            {
                owner.Damaged -= onDamaged;
                owner.Healed -= onHealed;
                owner.Stats.RemoveModifiersFrom(source);
            };
        }

        public override string Describe(int rank) =>
            $"{StatInfo.Format(stat, ModKind.Percent, valuePerRank * rank)} while below {Mathf.RoundToInt(threshold * 100)}% life";
    }

    [Serializable]
    public class ShieldOnHitTakenProc : PassiveProc
    {
        public float percentPerRank = 0.05f;
        public float cooldown = 8f;

        public override Action Attach(Combatant owner, int rank)
        {
            float next = 0f;
            Action<DamageInfo, float> h = (info, dealt) =>
            {
                if (info.silent || Time.time < next || owner.IsDead) return;
                next = Time.time + cooldown;
                owner.AddShield(owner.MaxHP * percentPerRank * rank, 4f);
            };
            owner.Damaged += h;
            return () => owner.Damaged -= h;
        }

        public override string Describe(int rank) =>
            $"Taking damage grants a {Mathf.RoundToInt(percentPerRank * rank * 100)}% life shield (every {cooldown:0}s)";
    }

    [Serializable]
    public class ExplodeOnKillProc : PassiveProc
    {
        public float chancePerRank = 0.15f;
        public float radius = 2f;
        public float damageMult = 0.8f;
        public Color color = new Color(1f, 0.5f, 0.2f);

        public override Action Attach(Combatant owner, int rank)
        {
            Action<Combatant> h = victim =>
            {
                if (UnityEngine.Random.value > chancePerRank * rank) return;
                Vector2 p = victim.transform.position;
                VFX.Ring(p, color, radius, 0.3f);
                VFX.Burst(p, color, 14, radius);
                foreach (var c in Combat.HostilesInRadius(owner.team, p, radius))
                    Combat.Hit(owner, c, damageMult);
            };
            owner.Killed += h;
            return () => owner.Killed -= h;
        }

        public override string Describe(int rank) =>
            $"{Mathf.RoundToInt(chancePerRank * rank * 100)}% chance that enemies explode on death for {Mathf.RoundToInt(damageMult * 100)}% damage";
    }
}
