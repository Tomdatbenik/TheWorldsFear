using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum ItemSlot { Weapon, Helm, Chest, Gloves, Boots, Ring, Amulet }

    [Flags]
    public enum SlotMask
    {
        None = 0,
        Weapon = 1 << 0, Helm = 1 << 1, Chest = 1 << 2, Gloves = 1 << 3, Boots = 1 << 4, Ring = 1 << 5, Amulet = 1 << 6,
        Armor = Helm | Chest | Gloves | Boots,
        Jewelry = Ring | Amulet,
        All = Weapon | Armor | Jewelry,
    }

    public enum Rarity { Common, Magic, Rare, Legendary }

    public static class RarityInfo
    {
        public static Color Color(Rarity r)
        {
            switch (r)
            {
                case Rarity.Magic: return new Color(0.4f, 0.55f, 1f);
                case Rarity.Rare: return new Color(1f, 0.9f, 0.3f);
                case Rarity.Legendary: return new Color(1f, 0.55f, 0.1f);
                default: return new Color(0.9f, 0.9f, 0.9f);
            }
        }

        public static int AffixCount(Rarity r, System.Random rng)
        {
            switch (r)
            {
                case Rarity.Magic: return rng.Next(1, 3);
                case Rarity.Rare: return rng.Next(3, 5);
                case Rarity.Legendary: return 4;
                default: return 0;
            }
        }

        public static SlotMask Mask(ItemSlot s) => (SlotMask)(1 << (int)s);
    }

    [Serializable]
    public class RolledAffix
    {
        public string affixId;
        public StatType stat;
        public ModKind kind;
        public float value;

        public string Describe() => StatInfo.Format(stat, kind, value);
    }

    [Serializable]
    public class ItemInstance
    {
        public string uid;
        public ItemSlot slot;
        public Rarity rarity;
        public int itemLevel;
        public int seed;
        public string baseName;
        public string name;
        public StatType implicitStat;
        public float implicitValue;
        public List<RolledAffix> affixes = new List<RolledAffix>();
        public string legendaryPower;
        public int weaponType; // WeaponType, for weapons

        public IEnumerable<StatModifier> Modifiers(object source)
        {
            if (implicitValue != 0f) yield return new StatModifier(implicitStat, ModKind.Flat, implicitValue, source);
            foreach (var a in affixes) yield return new StatModifier(a.stat, a.kind, a.value, source);
        }

        public int SellValue => Mathf.RoundToInt((5 + itemLevel * 2) * (1 + (int)rarity * (int)rarity * 1.5f));

        // Rough item score for "better/worse" arrows.
        public float Score()
        {
            float s = 0f;
            foreach (var m in Modifiers(null)) s += StatWeight(m.stat, m.kind) * m.value;
            if (!string.IsNullOrEmpty(legendaryPower)) s += 25f;
            return s;
        }

        static float StatWeight(StatType stat, ModKind kind)
        {
            if (kind == ModKind.Percent) return 120f;
            switch (stat)
            {
                case StatType.MaxHP: return 0.35f;
                case StatType.Damage: return 2.2f;
                case StatType.Armor: return 0.8f;
                case StatType.MoveSpeed: return 12f;
                case StatType.AttackSpeed: return 60f;
                case StatType.HPRegen: return 3f;
                default: return 150f; // fractions
            }
        }
    }
}
