using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    public static class MonsterSpawner
    {
        static readonly MonsterAffix[] affixPool = (MonsterAffix[])System.Enum.GetValues(typeof(MonsterAffix));

        public static List<MonsterAffix> RollAffixes(System.Random rng, int count)
        {
            var list = new List<MonsterAffix>();
            while (list.Count < count)
            {
                var a = affixPool[rng.Next(affixPool.Length)];
                if (!list.Contains(a)) list.Add(a);
            }
            return list;
        }

        public static void SpawnPack(ZoneDefinition zone, Vector2 center, float depth, List<MonsterBrain> into, System.Random rng)
        {
            var species = MonsterGenerator.SpeciesFor(zone);
            if (species.Count == 0) return;
            int level = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(zone.minLevel, zone.maxLevel, depth)) + rng.Next(-1, 2), zone.minLevel, zone.maxLevel);
            var main = species[rng.Next(species.Count)];
            double roll = rng.NextDouble();
            var pack = new List<MonsterBrain>();

            Vector2 Around(float r) => center + new Vector2((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * r;

            if (roll < zone.rareChance)
            {
                int affixCount = level >= 12 ? 3 : level >= 6 ? 2 : 1;
                pack.Add(MonsterBrain.Spawn(main, level, MonsterRank.Rare, center, RollAffixes(rng, affixCount), zone));
                int minions = Mathf.RoundToInt(3 * main.packMultiplier) + 1;
                for (int i = 0; i < minions; i++) pack.Add(MonsterBrain.Spawn(main, level, MonsterRank.Normal, Around(2f), null, zone));
            }
            else if (roll < zone.rareChance + zone.eliteChance)
            {
                int count = Mathf.Max(2, Mathf.RoundToInt(rng.Next(2, 4) * Mathf.Min(1.5f, main.packMultiplier)));
                for (int i = 0; i < count; i++) pack.Add(MonsterBrain.Spawn(main, level, MonsterRank.Elite, Around(1.5f), null, zone));
            }
            else
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(rng.Next(zone.packSize.x, zone.packSize.y + 1) * main.packMultiplier));
                var second = rng.NextDouble() < 0.4 ? species[rng.Next(species.Count)] : main;
                for (int i = 0; i < count; i++)
                    pack.Add(MonsterBrain.Spawn(i % 3 == 2 ? second : main, level, MonsterRank.Normal, Around(2.2f), null, zone));
            }

            foreach (var m in pack)
            {
                m.Pack = pack;
                m.Home = m.transform.position;
            }
            into.AddRange(pack);
        }
    }

}
