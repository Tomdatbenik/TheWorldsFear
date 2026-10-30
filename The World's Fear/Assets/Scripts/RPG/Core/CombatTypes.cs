using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum Team { Player, Enemy }

    public enum MonsterRank { Normal, Elite, Rare, Boss }

    public enum StatusType { None, Slow, Stun, Burn, Poison, Chill }

    [Serializable]
    public struct StatusSpec
    {
        public StatusType type;
        [Tooltip("Slow: fraction of speed removed. Burn/Poison: fraction of the hit's damage dealt per second.")]
        public float magnitude;
        public float duration;
        [Range(0, 1)] public float chance;

        public bool IsSet => type != StatusType.None && duration > 0f;

        public static StatusSpec Slow(float amount, float duration, float chance = 1f) =>
            new StatusSpec { type = StatusType.Slow, magnitude = amount, duration = duration, chance = chance };

        public static StatusSpec Stun(float duration, float chance = 1f) =>
            new StatusSpec { type = StatusType.Stun, magnitude = 1f, duration = duration, chance = chance };

        public static StatusSpec Burn(float dpsFraction, float duration, float chance = 1f) =>
            new StatusSpec { type = StatusType.Burn, magnitude = dpsFraction, duration = duration, chance = chance };

        public static StatusSpec Poison(float dpsFraction, float duration, float chance = 1f) =>
            new StatusSpec { type = StatusType.Poison, magnitude = dpsFraction, duration = duration, chance = chance };
    }

    public struct DamageInfo
    {
        public Combatant source;
        public float amount;
        public bool isCrit;
        public bool trueDamage;   // ignores armor (damage over time, legacy hits)
        public bool isBasicAttack;
        public bool silent;       // no floating text
        public Vector2 knockback;
        public StatusSpec status;
        public int elements;      // Elements.Mask of the attack (0 = physical)
    }

    public static class Combat
    {
        public static bool IsHostile(Team a, Team b) => a != b;

        // Rolls attacker damage times a multiplier, including crits.
        // baseDamage >= 0 is used when the attacker is gone (e.g. a monster died before its attack landed).
        public static DamageInfo Roll(Combatant attacker, float multiplier, bool basicAttack = false, float baseDamage = -1f)
        {
            var info = new DamageInfo { source = attacker, isBasicAttack = basicAttack };
            if (attacker == null)
            {
                info.source = null;
                info.amount = (baseDamage >= 0f ? baseDamage : 1f) * multiplier;
                return info;
            }

            float dmg = attacker.Stats.Get(StatType.Damage) * multiplier;
            dmg *= UnityEngine.Random.Range(0.92f, 1.08f);
            if (UnityEngine.Random.value < attacker.Stats.Get(StatType.CritChance))
            {
                info.isCrit = true;
                dmg *= 1.5f + attacker.Stats.Get(StatType.CritDamage);
            }
            info.amount = dmg;
            return info;
        }

        // Armor reduces damage by armor / (armor + 100).
        // Fighting above your level is punishing: monsters that outlevel you take less damage from you
        // and hit harder. Monsters far below you hit softer.
        public static float LevelGapFactor(Combatant attacker, Combatant target)
        {
            int gap = target.level - attacker.level;
            if (attacker.team == Team.Player)
                return gap > 0 ? Mathf.Max(0.3f, 1f - 0.04f * gap) : 1f;
            return gap < 0 ? Mathf.Min(2.5f, 1f + 0.06f * -gap) : Mathf.Max(0.5f, 1f - 0.03f * gap);
        }

        public static float Mitigate(float amount, float armor)
        {
            return amount * 100f / (100f + Mathf.Max(0f, armor));
        }

        public static List<Combatant> HostilesInRadius(Team team, Vector2 center, float radius)
        {
            var buffer = new List<Combatant>();
            foreach (var c in Combatant.All)
            {
                if (c.IsDead || !IsHostile(team, c.team)) continue;
                if (((Vector2)c.transform.position - center).sqrMagnitude <= (radius + c.radius) * (radius + c.radius))
                    buffer.Add(c);
            }
            return buffer;
        }

        public static List<Combatant> HostilesInArc(Team team, Vector2 center, Vector2 dir, float radius, float angleDeg)
        {
            var list = HostilesInRadius(team, center, radius);
            if (angleDeg >= 360f) return list;
            list.RemoveAll(c =>
            {
                Vector2 to = (Vector2)c.transform.position - center;
                return to.sqrMagnitude > 0.04f && Vector2.Angle(dir, to) > angleDeg * 0.5f;
            });
            return list;
        }

        public static Combatant NearestHostile(Team team, Vector2 from, float range)
        {
            Combatant best = null;
            float bestScore = float.MaxValue;
            foreach (var c in Combatant.All)
            {
                if (c.IsDead || !c.Targetable || !IsHostile(team, c.team)) continue;
                float d = Vector2.Distance(from, c.transform.position) - c.radius;
                if (d > range) continue;
                // Prefer closer targets, and low-health targets among those nearly as close.
                float score = d + c.HPFraction * 0.75f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        public static void Hit(Combatant attacker, Combatant target, float multiplier, StatusSpec status = default, bool basicAttack = false, float knockback = 0f, float baseDamage = -1f, int elements = 0)
        {
            if (target == null || target.IsDead) return;
            var info = Roll(attacker, multiplier, basicAttack, baseDamage);
            info.status = status;
            info.elements = elements;
            if (knockback > 0f && attacker != null && target != null)
                info.knockback = ((Vector2)(target.transform.position - attacker.transform.position)).normalized * knockback;
            target.TakeDamage(info);
        }
    }
}
