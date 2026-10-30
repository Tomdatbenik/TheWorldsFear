using System;
using System.Collections.Generic;

namespace TWF
{
    [Serializable]
    public class CodexMonster
    {
        public string name;
        public int element, biome, archetype, seed;
        public bool spider;
        public int kills, highestLevel;
    }

    // The bestiary and loot journal: what you've slain, which bosses you've faced, what you've found.
    public static class Codex
    {
        public static void RecordKill(MonsterSpecies sp, int level)
        {
            if (sp == null || Profile.Data == null) return;
            var list = Profile.Data.codexMonsters;
            var e = list.Find(m => m.name == sp.name);
            if (e == null)
            {
                e = new CodexMonster { name = sp.name, element = (int)sp.element, biome = (int)sp.biome, archetype = (int)sp.archetype, seed = sp.seed, spider = sp.isSpider };
                list.Add(e);
                GameUI.Instance?.Toast($"New codex entry: {sp.name}", Palette.Gold, 1.6f);
            }
            e.kills++;
            if (level > e.highestLevel) e.highestLevel = level;
        }

        public static void SeeBoss(BossKind k)
        {
            if (Profile.Data != null && !Profile.Data.bossesSeen.Contains(k.ToString())) Profile.Data.bossesSeen.Add(k.ToString());
        }

        public static bool BossSeen(BossKind k) => Profile.Data.bossesSeen.Contains(k.ToString());

        public static void RecordItem(ItemInstance item)
        {
            if (item == null || Profile.Data == null) return;
            var d = Profile.Data;
            if (d.lootByRarity == null || d.lootByRarity.Length < 5) d.lootByRarity = new int[5];
            d.lootByRarity[(int)item.rarity]++;
            if (!string.IsNullOrEmpty(item.legendaryPower) && !d.codexPowers.Contains(item.legendaryPower)) d.codexPowers.Add(item.legendaryPower);
            if (item.slot == ItemSlot.Weapon && !d.codexWeapons.Contains(item.weaponType)) d.codexWeapons.Add(item.weaponType);
        }

        // Rebuild a monster's look for its codex page.
        public static MonsterLook LookOf(CodexMonster m) =>
            m.spider ? SpiderSpriteGenerator.Generate(m.seed, (Biome)m.biome) : MonsterSpriteGenerator.Generate(m.seed, (Biome)m.biome, (MonsterArchetype)m.archetype);
    }
}
