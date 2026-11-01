using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // The mystic's reforge: pay to reroll one affix of an item and pick between the old roll and two new
    // ones. Like Diablo, once you reforge an affix on an item, that is the only one you can reforge again.
    // Every reforge of the same item costs more.
    public static class Reforge
    {
        static readonly float[] rarityCost = { 1f, 1.5f, 2.5f, 4f, 6f };

        public static int Cost(ItemInstance item) =>
            Mathf.RoundToInt((60 + item.itemLevel * 10) * rarityCost[Mathf.Clamp((int)item.rarity, 0, rarityCost.Length - 1)] * (1f + 0.5f * item.reforgeCount));

        public static bool CanReforge(ItemInstance item, int index) =>
            item != null && index >= 0 && index < item.affixes.Count && (item.reforgedAffix < 0 || item.reforgedAffix == index);

        // Pays and rolls two new options for the affix. Returns null when it can't.
        public static List<RolledAffix> Roll(ItemInstance item, int index)
        {
            if (!CanReforge(item, index) || Profile.Data.gold < Cost(item)) return null;
            var pool = new List<AffixDefinition>();
            foreach (var a in GameDatabase.Instance.affixes)
            {
                if (a == null || ((a.slots & RarityInfo.Mask(item.slot)) == 0 && !(item.slot == ItemSlot.OffHand && (a.slots & (SlotMask.Chest | SlotMask.Amulet)) != 0))) continue;
                // No duplicates of the item's other affixes.
                bool taken = false;
                for (int i = 0; i < item.affixes.Count; i++)
                    if (i != index && item.affixes[i].affixId == a.id) taken = true;
                if (!taken) pool.Add(a);
            }
            if (pool.Count == 0) return null;

            Profile.AddGold(-Cost(item));
            item.reforgedAffix = index;
            item.reforgeCount++;
            var rng = new System.Random(System.Environment.TickCount ^ item.uid.GetHashCode() ^ item.reforgeCount);
            var options = new List<RolledAffix>();
            for (int n = 0; n < 2 && pool.Count > 0; n++)
            {
                var def = Pick(pool, rng);
                pool.Remove(def);
                float value = def.Roll(item.itemLevel, rng);
                if (item.rarity == Rarity.Mythic) value = def.kind == ModKind.Flat ? Mathf.Round(value * 1.3f) : value * 1.3f;
                options.Add(new RolledAffix { affixId = def.id, stat = def.stat, kind = def.kind, value = value });
            }
            Profile.Save();
            return options;
        }

        // choice = null keeps the current affix.
        public static void Choose(ItemInstance item, int index, RolledAffix choice)
        {
            if (choice != null && index >= 0 && index < item.affixes.Count) item.affixes[index] = choice;
            Profile.NotifyItems();
            Profile.Save();
        }

        static AffixDefinition Pick(List<AffixDefinition> pool, System.Random rng)
        {
            int total = 0;
            foreach (var a in pool) total += Mathf.Max(1, a.weight);
            int r = rng.Next(total);
            foreach (var a in pool)
            {
                r -= Mathf.Max(1, a.weight);
                if (r < 0) return a;
            }
            return pool[pool.Count - 1];
        }
    }
}
