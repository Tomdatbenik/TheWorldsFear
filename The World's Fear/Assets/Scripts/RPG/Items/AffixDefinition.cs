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
            float t = Mathf.InverseLerp(1, 30, itemLevel);
            float min = Mathf.Lerp(rangeAtLevel1.x, rangeAtLevel30.x, t);
            float max = Mathf.Lerp(rangeAtLevel1.y, rangeAtLevel30.y, t);
            float v = Mathf.Lerp(min, max, (float)rng.NextDouble());
            if (!StatInfo.IsFraction(stat) && kind == ModKind.Flat && stat != StatType.AttackSpeed && stat != StatType.MoveSpeed && stat != StatType.HPRegen)
                v = Mathf.Round(v);
            return v;
        }
    }
}
