using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum StatType
    {
        MaxHP,
        Damage,
        Armor,
        MoveSpeed,
        AttackSpeed,
        CritChance,
        CritDamage,
        CooldownReduction,
        LifeSteal,
        HPRegen,
        // Skill modifiers (from gear): appended so saved data keeps its numbers.
        SkillRange,
        AreaSize,
        ExtraProjectiles,
        ProjectileSpeed,
        EffectDuration,
        ProjectileSpread,
    }

    public enum ModKind { Flat, Percent }

    [Serializable]
    public struct StatModifier
    {
        public StatType stat;
        public ModKind kind;
        public float value;
        [NonSerialized] public object source;

        public StatModifier(StatType stat, ModKind kind, float value, object source = null)
        {
            this.stat = stat;
            this.kind = kind;
            this.value = value;
            this.source = source;
        }

        public string Describe()
        {
            return StatInfo.Format(stat, kind, value);
        }
    }

    [Serializable]
    public struct StatEntry
    {
        public StatType stat;
        public float value;

        public StatEntry(StatType stat, float value)
        {
            this.stat = stat;
            this.value = value;
        }
    }

    // Final value = (base + sum of flat) * (1 + sum of percent), clamped per stat.
    public class CharacterStats
    {
        public static readonly int Count = Enum.GetValues(typeof(StatType)).Length;

        readonly float[] baseValues = new float[Count];
        readonly float[] cache = new float[Count];
        readonly List<StatModifier> modifiers = new List<StatModifier>();
        bool dirty = true;

        public event Action Changed;

        public float this[StatType stat] => Get(stat);

        public void SetBase(StatType stat, float value)
        {
            baseValues[(int)stat] = value;
            MarkDirty();
        }

        public float GetBase(StatType stat) => baseValues[(int)stat];

        public void SetBases(IEnumerable<StatEntry> entries)
        {
            foreach (var e in entries) baseValues[(int)e.stat] = e.value;
            MarkDirty();
        }

        public void AddModifier(StatModifier mod)
        {
            modifiers.Add(mod);
            MarkDirty();
        }

        public void RemoveModifiersFrom(object source)
        {
            if (modifiers.RemoveAll(m => ReferenceEquals(m.source, source)) > 0) MarkDirty();
        }

        public float Get(StatType stat)
        {
            if (dirty) Recalculate();
            return cache[(int)stat];
        }

        void MarkDirty()
        {
            dirty = true;
            Changed?.Invoke();
        }

        void Recalculate()
        {
            var flat = new float[Count];
            var pct = new float[Count];
            foreach (var m in modifiers)
            {
                if (m.kind == ModKind.Flat) flat[(int)m.stat] += m.value;
                else pct[(int)m.stat] += m.value;
            }

            for (int i = 0; i < Count; i++)
            {
                cache[i] = StatInfo.Clamp((StatType)i, (baseValues[i] + flat[i]) * (1f + pct[i]));
            }
            dirty = false;
        }
    }

    public static class StatInfo
    {
        public static string Name(StatType stat)
        {
            switch (stat)
            {
                case StatType.MaxHP: return "Life";
                case StatType.Damage: return "Damage";
                case StatType.Armor: return "Armor";
                case StatType.MoveSpeed: return "Move Speed";
                case StatType.AttackSpeed: return "Attack Speed";
                case StatType.CritChance: return "Crit Chance";
                case StatType.CritDamage: return "Crit Damage";
                case StatType.CooldownReduction: return "Cooldown Reduction";
                case StatType.LifeSteal: return "Life Steal";
                case StatType.HPRegen: return "Life Regen";
                case StatType.SkillRange: return "Skill Range";
                case StatType.AreaSize: return "Area Size";
                case StatType.ExtraProjectiles: return "Projectiles";
                case StatType.ProjectileSpeed: return "Projectile Speed";
                case StatType.EffectDuration: return "Effect Duration";
                case StatType.ProjectileSpread: return "Projectile Spread";
            }
            return stat.ToString();
        }

        // These stats are fractions (0.1 = 10%) even as flat values.
        public static bool IsFraction(StatType stat)
        {
            return stat == StatType.CritChance || stat == StatType.CritDamage ||
                   stat == StatType.CooldownReduction || stat == StatType.LifeSteal ||
                   stat == StatType.SkillRange || stat == StatType.AreaSize || stat == StatType.ProjectileSpeed ||
                   stat == StatType.EffectDuration || stat == StatType.ProjectileSpread;
        }

        public static float Clamp(StatType stat, float v)
        {
            switch (stat)
            {
                case StatType.CritChance: return Mathf.Clamp(v, 0f, 0.95f);
                case StatType.CooldownReduction: return Mathf.Clamp(v, 0f, 0.5f);
                case StatType.LifeSteal: return Mathf.Clamp(v, 0f, 0.5f);
                case StatType.AttackSpeed: return Mathf.Max(0.2f, v);
                case StatType.MoveSpeed: return Mathf.Max(0.5f, v);
                case StatType.SkillRange: return Mathf.Clamp(v, 0f, 1.5f);
                case StatType.AreaSize: return Mathf.Clamp(v, 0f, 1.5f);
                case StatType.ExtraProjectiles: return Mathf.Clamp(Mathf.Round(v), 0f, 6f);
                case StatType.ProjectileSpeed: return Mathf.Clamp(v, 0f, 1.5f);
                case StatType.EffectDuration: return Mathf.Clamp(v, 0f, 2f);
                case StatType.ProjectileSpread: return Mathf.Clamp(v, -0.6f, 2f);
                default: return Mathf.Max(0f, v);
            }
        }

        public static string FormatValue(StatType stat, float v)
        {
            if (IsFraction(stat)) return Mathf.RoundToInt(v * 100f) + "%";
            if (stat == StatType.AttackSpeed || stat == StatType.MoveSpeed || stat == StatType.HPRegen) return v.ToString("0.0#");
            return Mathf.RoundToInt(v).ToString();
        }

        public static string Format(StatType stat, ModKind kind, float v)
        {
            string sign = v >= 0 ? "+" : "-";
            float a = Mathf.Abs(v);
            if (kind == ModKind.Percent) return $"{sign}{Mathf.RoundToInt(a * 100f)}% {Name(stat)}";
            if (IsFraction(stat)) return $"{sign}{(a * 100f):0.#}% {Name(stat)}";
            if (stat == StatType.AttackSpeed || stat == StatType.MoveSpeed || stat == StatType.HPRegen) return $"{sign}{a:0.##} {Name(stat)}";
            return $"{sign}{Mathf.RoundToInt(a)} {Name(stat)}";
        }
    }
}
