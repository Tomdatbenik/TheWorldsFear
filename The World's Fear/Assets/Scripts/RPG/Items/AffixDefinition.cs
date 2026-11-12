using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    [CreateAssetMenu(menuName = "TWF/Affix")]
    public class AffixDefinition : ScriptableObject
    {
        public string id;
        [Tooltip("Prefix e.g. 'Brutal', or suffix e.g. 'of the Bear'.")]
        public string nameText;
        public bool isPrefix = true;
        public StatType stat;
        public ModKind kind;
        [Tooltip("Value range at item level 1.")]
        public Vector2 rangeAtLevel1 = new Vector2(1, 3);
        [Tooltip("Value range at item level 30.")]
        public Vector2 rangeAtLevel30 = new Vector2(10, 20);
        public SlotMask slots = SlotMask.All;
        public int weight = 10;

        public float Roll(int itemLevel, System.Random rng)
        {
            // Keeps growing past item level 30 for the infinite world (fractions are capped by the stat clamps).
            float t = (itemLevel - 1) / 29f;
            float min = Mathf.LerpUnclamped(rangeAtLevel1.x, rangeAtLevel30.x, t);
            float max = Mathf.LerpUnclamped(rangeAtLevel1.y, rangeAtLevel30.y, t);
            if (StatInfo.IsFraction(stat) || kind == ModKind.Percent)
            {
                // Percentages grow slower past 30 so they don't explode.
                if (t > 1f)
                {
                    float extra = Mathf.Log(t) * 0.5f + 1f;
                    min = rangeAtLevel30.x * extra;
                    max = rangeAtLevel30.y * extra;
                }
            }
            float v = Mathf.Lerp(min, max, (float)rng.NextDouble());
            return Whole(stat, kind) ? Mathf.Round(v) : v;
        }

        // Flat stats counted in whole numbers (damage, armor, life). Fractions such as crit chance must never be
        // rounded, or 5% crit becomes 0.
        public static bool Whole(StatType stat, ModKind kind) =>
            kind == ModKind.Flat && !StatInfo.IsFraction(stat) && stat != StatType.AttackSpeed && stat != StatType.MoveSpeed && stat != StatType.HPRegen;

        // A rolled value made stronger (mythic items), kept whole only where the stat is whole.
        public static float Scale(StatType stat, ModKind kind, float value, float mult) =>
            Whole(stat, kind) ? Mathf.Round(value * mult) : value * mult;

        public const float MythicMult = 1.3f;

        // A mythic stat: the roll x1.3, then 5-10 more on top. Whole stats get +5-10, percentages +5-10%
        // (crit chance, cooldown...), and small decimal stats (attack speed, move speed, regen) 5-10% of their value.
        public static float Mythic(StatType stat, ModKind kind, float value, System.Random rng, float mult = MythicMult)
        {
            float extra = 5f + (float)rng.NextDouble() * 5f;
            float v = Scale(stat, kind, value, mult);
            if (Whole(stat, kind)) return v + Mathf.Round(extra);
            if (kind == ModKind.Percent || StatInfo.IsFraction(stat)) return v + extra / 100f;
            return v * (1f + extra / 100f);
        }
    }
}
