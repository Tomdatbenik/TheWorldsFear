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

        static bool rollMix(System.Random rng) => rng.NextDouble() < 0.7;

        static MonsterSpecies Pick(List<MonsterSpecies> species, System.Predicate<MonsterSpecies> match, System.Random rng)
        {
            var found = species.FindAll(match);
            return found.Count > 0 ? found[rng.Next(found.Count)] : null;
        }

        public static void SpawnPack(ZoneDefinition zone, Vector2 center, float depth, List<MonsterBrain> into, System.Random rng, int fixedLevel = 0, float rareOverride = -1f, float eliteOverride = -1f, List<MonsterSpecies> roster = null, float warbandOverride = -1f)
        {
            // The farmland round the town: rats and spiders, rarely anything special, never a warband.
            bool farm = roster == null && WorldGen.InFarmRing(center);
            var species = roster ?? (farm ? MonsterGenerator.FarmRoster() : MonsterGenerator.SpeciesFor(zone));
            // High in the mountain ranges, where cliffs keep walkers out, only flyers live.
            bool mountain = roster == null && !farm && WorldGen.ElevAt(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y)) >= WorldGen.MountainLevel;
            if (mountain && MonsterGenerator.FlyerRoster(zone.biome).Count > 0) species = MonsterGenerator.FlyerRoster(zone.biome);
            // Above the snow line (the ash wastes have none): the frost kinds, now that the peaks can be climbed.
            if (mountain && zone.biome != Biome.Ember && WorldGen.ElevAt(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y)) >= WorldGen.SnowLevel
                && MonsterGenerator.FrostRoster(zone).Count > 0) species = MonsterGenerator.FrostRoster(zone);
            if (species.Count == 0) return;
            int level = fixedLevel > 0
                ? Mathf.Max(1, fixedLevel + rng.Next(-1, 2))
                : Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(zone.minLevel, zone.maxLevel, depth)) + rng.Next(-1, 2), zone.minLevel, zone.maxLevel);
            // Further from the village: more elites and rares.
            float rareChance = rareOverride >= 0f ? rareOverride : fixedLevel > 0 ? WorldGen.RareChance(center) : zone.rareChance;
            float eliteChance = eliteOverride >= 0f ? eliteOverride : fixedLevel > 0 ? WorldGen.EliteChance(center) : zone.eliteChance;
            // Sometimes an organised warband instead of an ordinary pack: a separate, harder tier.
            if (farm) { rareChance *= 0.3f; eliteChance *= 0.4f; }
            else if (mountain) { }   // no warbands on the peaks
            else if (rng.NextDouble() < (warbandOverride >= 0f ? warbandOverride : Warband.SpawnChance) && Warband.Spawn(zone, zone.biome, center, level, into, rng)) return;

            var main = species[rng.Next(species.Count)];
            double roll = rng.NextDouble();
            var pack = new List<MonsterBrain>();

            Vector2 Around(float r) => center + new Vector2((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * r;

            if (roll < rareChance)
            {
                int affixCount = level >= 12 ? 3 : level >= 6 ? 2 : 1;
                pack.Add(MonsterBrain.Spawn(main, level, MonsterRank.Rare, center, RollAffixes(rng, affixCount), zone));
                int minions = Mathf.RoundToInt(3 * main.packMultiplier) + 1;
                for (int i = 0; i < minions; i++) pack.Add(MonsterBrain.Spawn(main, level, MonsterRank.Normal, Around(2f), null, zone));
            }
            else if (roll < rareChance + eliteChance)
            {
                int count = Mathf.Max(2, Mathf.RoundToInt(rng.Next(2, 4) * Mathf.Min(1.5f, main.packMultiplier)));
                for (int i = 0; i < count; i++) pack.Add(MonsterBrain.Spawn(main, level, MonsterRank.Elite, Around(1.5f), null, zone));
            }
            else
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(rng.Next(zone.packSize.x, zone.packSize.y + 1) * main.packMultiplier));
                var second = rng.NextDouble() < 0.4 ? species[rng.Next(species.Count)] : main;
                // A guardian and shooters pair up inside the same head count: a shooter pack's second kind is often a guardian, and the other way round.
                bool Ranged(MonsterSpecies s) => s.preferredDistance > 0f;
                if (rollMix(rng))
                {
                    var partner = main.archetype == MonsterArchetype.Guardian ? Pick(species, Ranged, rng)
                                : Ranged(main) ? Pick(species, s => s.archetype == MonsterArchetype.Guardian, rng) : null;
                    if (partner != null) second = partner;
                }
                for (int i = 0; i < count; i++)
                    pack.Add(MonsterBrain.Spawn(i % 3 == 2 ? second : main, level, MonsterRank.Normal, Around(2.2f), null, zone));
            }

            foreach (var m in pack)
            {
                m.Pack = pack;
                m.Home = m.transform.G();
            }
            into.AddRange(pack);
        }
    }

}
