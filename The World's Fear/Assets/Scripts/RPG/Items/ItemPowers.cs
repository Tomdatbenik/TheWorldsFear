using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Unique effects on legendary items. Each power is a passive proc attached while the item is worn.
    public static class ItemPowers
    {
        public class Power
        {
            public string id;
            public string name;
            public SlotMask slots;
            public PassiveProc proc;
            public int rank = 1;
        }

        public static readonly List<Power> All = new List<Power>
        {
            new Power { id = "inferno", name = "of the Inferno", slots = SlotMask.Weapon | SlotMask.Gloves | SlotMask.Ring,
                proc = new StatusOnHitProc { chancePerRank = 0.25f, status = StatusSpec.Burn(0.4f, 3f) } },
            new Power { id = "venom", name = "of the Viper", slots = SlotMask.Weapon | SlotMask.Gloves | SlotMask.Amulet,
                proc = new StatusOnHitProc { chancePerRank = 0.3f, status = StatusSpec.Poison(0.35f, 4f) } },
            new Power { id = "frost", name = "of Winter", slots = SlotMask.Weapon | SlotMask.Boots | SlotMask.Ring,
                proc = new StatusOnHitProc { chancePerRank = 0.3f, status = StatusSpec.Slow(0.4f, 2f) } },
            new Power { id = "thunder", name = "of Thunder", slots = SlotMask.Weapon | SlotMask.Helm,
                proc = new StatusOnHitProc { chancePerRank = 0.08f, status = StatusSpec.Stun(0.8f) } },
            new Power { id = "vampire", name = "of the Vampire", slots = SlotMask.Chest | SlotMask.Amulet | SlotMask.Ring,
                proc = new HealOnKillProc { percentMaxHPPerRank = 0.05f } },
            new Power { id = "executioner", name = "of the Executioner", slots = SlotMask.Weapon | SlotMask.Helm,
                proc = new ExecuteProc { bonusPerRank = 0.4f, threshold = 0.3f } },
            new Power { id = "bulwark", name = "of the Bulwark", slots = SlotMask.Chest | SlotMask.Helm | SlotMask.Boots,
                proc = new ShieldOnHitTakenProc { percentPerRank = 0.15f, cooldown = 10f } },
            new Power { id = "volatile", name = "of Calamity", slots = SlotMask.Gloves | SlotMask.Amulet | SlotMask.Chest,
                proc = new ExplodeOnKillProc { chancePerRank = 0.35f, radius = 2.5f, damageMult = 1.2f } },
            new Power { id = "berserk", name = "of the Berserker", slots = SlotMask.Chest | SlotMask.Gloves | SlotMask.Weapon,
                proc = new LowLifeProc { stat = StatType.Damage, valuePerRank = 0.35f, threshold = 0.5f } },
        };

        public static Power Find(string id) => All.Find(p => p.id == id);

        static readonly List<Action> active = new List<Action>();

        public static void Refresh(HeroController hero)
        {
            foreach (var a in active) a();
            active.Clear();
            foreach (var item in Profile.Data.equipment)
            {
                var p = Find(item.legendaryPower);
                if (p != null) active.Add(p.proc.Attach(hero.Combatant, p.rank));
            }
        }

        public static string Describe(string id)
        {
            var p = Find(id);
            return p != null ? p.proc.Describe(p.rank) : "";
        }
    }
}
