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
            if (!StatInfo.IsFraction(stat) && kind == ModKind.Flat && stat != StatType.AttackSpeed && stat != StatType.MoveSpeed && stat != StatType.HPRegen)
                v = Mathf.Round(v);
            return v;
        }
    }
}
