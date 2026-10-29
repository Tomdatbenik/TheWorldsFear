using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public static class ItemGenerator
    {
        static readonly Dictionary<ItemSlot, string[]> baseNames = new Dictionary<ItemSlot, string[]>
        {
            { ItemSlot.Helm, new[] { "Leather Cap", "Iron Helm", "Steel Visage", "Dread Crown" } },
            { ItemSlot.Chest, new[] { "Padded Vest", "Chainmail", "Plate Cuirass", "Ancient Aegis" } },
            { ItemSlot.Gloves, new[] { "Cloth Wraps", "Leather Gloves", "Iron Gauntlets", "Talon Grips" } },
            { ItemSlot.Boots, new[] { "Sandals", "Leather Boots", "Iron Greaves", "Wanderer's Striders" } },
            { ItemSlot.Ring, new[] { "Copper Ring", "Silver Band", "Gold Signet", "Obsidian Loop" } },
            { ItemSlot.Amulet, new[] { "Bone Charm", "Silver Pendant", "Jade Amulet", "Star Talisman" } },
        };

        static readonly string[] rareFirst = { "Grim", "Doom", "Storm", "Blood", "Shadow", "Ash", "Soul", "Bone", "Dread", "Wolf", "Raven", "Night", "Ember", "Frost" };
        static readonly string[] rareSecond = { "Song", "Grasp", "Fang", "Ward", "Bite", "Veil", "Call", "Mark", "Shroud", "Edge", "Heart", "Spire", "Gaze", "Hold" };
        static readonly string[] legendaryAdj = { "Ancient", "Fabled", "Forgotten", "Eternal", "Cursed", "Storied" };
        static readonly string[] mythicAdj = { "Godforged", "Worldbreaker's", "Starfallen", "Primordial", "Apocalyptic", "Divine" };

        public static ItemSlot RandomSlot(System.Random rng)
        {
            // Weapons and jewelry a little rarer than armor.
            int[] weights = { 14, 15, 15, 15, 15, 13, 13 };
            int total = 0;
            foreach (var w in weights) total += w;
            int r = rng.Next(total);
            for (int i = 0; i < weights.Length; i++)
            {
                if (r < weights[i]) return (ItemSlot)i;
                r -= weights[i];
            }
            return ItemSlot.Chest;
        }

        public static Rarity RollRarity(System.Random rng, MonsterRank source, float magicFind = 0f)
        {
            float legendary = 0.01f, rare = 0.09f, magic = 0.32f;
            switch (source)
            {
                case MonsterRank.Elite: legendary = 0.03f; rare = 0.2f; magic = 0.45f; break;
                case MonsterRank.Rare: legendary = 0.07f; rare = 0.38f; magic = 0.45f; break;
                case MonsterRank.Boss: legendary = 0.6f; rare = 0.4f; magic = 0f; break;
            }
            // Magic find (from distance) improves the odds of better items.
            legendary *= 1f + magicFind * 2f;
            rare *= 1f + magicFind;
            magic *= 1f + magicFind * 0.5f;
            double r = rng.NextDouble();
            if (r < legendary) return Rarity.Legendary;
            if (r < legendary + rare) return Rarity.Rare;
            if (r < legendary + rare + magic) return Rarity.Magic;
            return Rarity.Common;
        }

        public static ItemInstance Generate(int itemLevel, Rarity rarity, ItemSlot slot, System.Random rng, WeaponType? forcedWeapon = null)
        {
            itemLevel = Mathf.Max(1, itemLevel);
            int tier = Mathf.Clamp((itemLevel - 1) / 8, 0, 3);
            var item = new ItemInstance
            {
                uid = Guid.NewGuid().ToString("N").Substring(0, 12),
                slot = slot,
                rarity = rarity,
                itemLevel = itemLevel,
                seed = rng.Next(),
            };

            if (slot == ItemSlot.Weapon)
            {
                // Any weapon type can drop; each has its own basic attack.
                var wt = forcedWeapon ?? (WeaponType)rng.Next(9);
                item.weaponType = (int)wt;
                item.baseName = Weapons.Get(wt).tierNames[tier];
            }
            else item.baseName = baseNames[slot][tier];
            // Beyond the base tiers, gear gets grander titles.
            if (itemLevel > 32) item.baseName = (itemLevel > 80 ? "Mythic " : itemLevel > 55 ? "Exalted " : "Ancient ") + item.baseName;

            float jitter = 0.9f + (float)rng.NextDouble() * 0.2f;
            switch (slot)
            {
                case ItemSlot.Weapon:
                    item.implicitStat = StatType.Damage;
                    item.implicitValue = Mathf.Round((3f + itemLevel * 1.8f) * jitter * Weapons.Get((WeaponType)item.weaponType).implicitDamage);
                    break;
                case ItemSlot.Chest: item.implicitStat = StatType.Armor; item.implicitValue = Mathf.Round((3f + itemLevel * 1.9f) * jitter); break;
                case ItemSlot.Helm: item.implicitStat = StatType.Armor; item.implicitValue = Mathf.Round((2f + itemLevel * 1.2f) * jitter); break;
                case ItemSlot.Gloves: item.implicitStat = StatType.Armor; item.implicitValue = Mathf.Round((1f + itemLevel * 0.8f) * jitter); break;
                case ItemSlot.Boots: item.implicitStat = StatType.Armor; item.implicitValue = Mathf.Round((1f + itemLevel * 0.9f) * jitter); break;
                case ItemSlot.Ring: item.implicitStat = StatType.MaxHP; item.implicitValue = Mathf.Round((5f + itemLevel * 3f) * jitter); break;
                case ItemSlot.Amulet: item.implicitStat = StatType.Damage; item.implicitValue = Mathf.Round((1f + itemLevel * 0.8f) * jitter); break;
            }

            // Roll distinct affixes allowed on this slot.
            var pool = new List<AffixDefinition>();
            foreach (var a in GameDatabase.Instance.affixes)
                if (a != null && (a.slots & RarityInfo.Mask(slot)) != 0) pool.Add(a);
            int count = RarityInfo.AffixCount(rarity, rng);
            AffixDefinition firstPrefix = null, firstSuffix = null;
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                var def = PickWeighted(pool, rng);
                pool.Remove(def);
                item.affixes.Add(new RolledAffix { affixId = def.id, stat = def.stat, kind = def.kind, value = def.Roll(itemLevel, rng) });
                if (def.isPrefix && firstPrefix == null) firstPrefix = def;
                if (!def.isPrefix && firstSuffix == null) firstSuffix = def;
            }

            switch (rarity)
            {
                case Rarity.Common:
                    item.name = item.baseName;
                    break;
                case Rarity.Magic:
                    item.name = $"{(firstPrefix != null ? firstPrefix.nameText + " " : "")}{item.baseName}{(firstSuffix != null ? " " + firstSuffix.nameText : "")}";
                    break;
                case Rarity.Rare:
                    item.name = $"{rareFirst[rng.Next(rareFirst.Length)]} {rareSecond[rng.Next(rareSecond.Length)]}";
                    break;
                case Rarity.Legendary:
                    var powers = ItemPowers.All.FindAll(p => (p.slots & RarityInfo.Mask(slot)) != 0);
                    var power = powers.Count > 0 ? powers[rng.Next(powers.Count)] : ItemPowers.All[0];
                    item.legendaryPower = power.id;
                    item.name = $"{legendaryAdj[rng.Next(legendaryAdj.Length)]} {item.baseName} {power.name}";
                    item.implicitValue = Mathf.Round(item.implicitValue * 1.25f);
                    break;
                case Rarity.Mythic:
                    // Mythic: a legendary power, five affixes rolled higher and a much stronger base.
                    var mythPowers = ItemPowers.All.FindAll(p => (p.slots & RarityInfo.Mask(slot)) != 0);
                    var mythPower = mythPowers.Count > 0 ? mythPowers[rng.Next(mythPowers.Count)] : ItemPowers.All[0];
                    item.legendaryPower = mythPower.id;
                    item.name = $"{mythicAdj[rng.Next(mythicAdj.Length)]} {item.baseName} {mythPower.name}";
                    item.implicitValue = Mathf.Round(item.implicitValue * 1.5f);
                    foreach (var a in item.affixes) a.value = a.kind == ModKind.Flat ? Mathf.Round(a.value * 1.3f) : a.value * 1.3f;
                    break;
            }
            return item;
        }

        static AffixDefinition PickWeighted(List<AffixDefinition> pool, System.Random rng)
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

        public static string Tooltip(ItemInstance item, ItemInstance compareTo = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<color={Palette.Hex(RarityInfo.Color(item.rarity))}><b>{item.name}</b></color>\n");
            string kind = item.slot == ItemSlot.Weapon ? Weapons.Name((WeaponType)item.weaponType) : item.slot.ToString();
            sb.Append($"<size=80%><color=#999>{item.rarity} {kind} · Item level {item.itemLevel}</color></size>\n");
            sb.Append($"<color=#fff>{StatInfo.Format(item.implicitStat, ModKind.Flat, item.implicitValue)}</color>\n");
            foreach (var a in item.affixes) sb.Append($"<color=#8cf>{a.Describe()}</color>\n");
            if (!string.IsNullOrEmpty(item.legendaryPower))
                sb.Append($"<color=#ff9a30>{(item.rarity == Rarity.Mythic ? "Mythic power" : "Legendary")}: {ItemPowers.Describe(item.legendaryPower)}</color>\n");
            if (compareTo != null && compareTo != item)
            {
                float diff = item.Score() - compareTo.Score();
                string arrow = diff > 0.5f ? "<color=#5f5>+ Upgrade</color>" : diff < -0.5f ? "<color=#f55>- Weaker than equipped</color>" : "<color=#ccc>~ Similar to equipped</color>";
                sb.Append(arrow).Append('\n');
            }
            sb.Append($"<size=80%><color=#ffd040>Sells for {item.SellValue} gold</color></size>");
            return sb.ToString();
        }
    }
}
