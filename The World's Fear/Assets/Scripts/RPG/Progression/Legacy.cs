using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TWF
{
    [Serializable]
    public class LegacyData
    {
        public int runsFinished;
        public int lairsDestroyed;
        public List<string> achievements = new List<string>();
    }

    // What carries over from one hero to the next: the save of a run is wiped when the hero falls, this is not.
    // Finished runs unlock weapons, two at a time; achievements give every later run a bonus.
    public static class Legacy
    {
        public class Achievement
        {
            public string id, title, how, bonus;
        }

        // Weapons open up in pairs: the first three from the start, then two more after each finished run.
        public static readonly WeaponType[][] WeaponTiers =
        {
            new[] { WeaponType.Sword, WeaponType.Bow, WeaponType.Staff },
            new[] { WeaponType.Axe, WeaponType.Daggers },
            new[] { WeaponType.Spear, WeaponType.Crossbow },
            new[] { WeaponType.Hammer, WeaponType.Wand },
        };

        public const int SeasonedLevel = 10, VeteranLevel = 25, LairGoal = 10, ExplorerDistance = 1500;
        public const float LegendDamage = 0.10f;

        static LegacyData data;
        static List<Achievement> all;
        static string FilePath => Path.Combine(Application.persistentDataPath, "legacy.json");

        public static LegacyData Data
        {
            get
            {
                if (data != null) return data;
                try { if (File.Exists(FilePath)) data = JsonUtility.FromJson<LegacyData>(File.ReadAllText(FilePath)); }
                catch (Exception e) { Debug.LogWarning("Could not read legacy: " + e.Message); }
                if (data == null) data = new LegacyData();
                if (data.achievements == null) data.achievements = new List<string>();
                return data;
            }
        }

        static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Data)); }
            catch (Exception e) { Debug.LogWarning("Could not save legacy: " + e.Message); }
        }

        // ---------- The list ----------

        public static List<Achievement> All
        {
            get
            {
                if (all != null) return all;
                all = new List<Achievement>
                {
                    new Achievement { id = "seasoned", title = "Seasoned", how = $"Reach level {SeasonedLevel}", bonus = "Start every run one level higher" },
                    new Achievement { id = "veteran", title = "Veteran", how = $"Reach level {VeteranLevel}", bonus = "Start every run one more level higher" },
                    new Achievement { id = "lairbreaker", title = "Lair Breaker", how = $"Destroy {LairGoal} monster lairs (over all runs)", bonus = "Start every run with an extra potion" },
                    new Achievement { id = "explorer", title = "Far Wanderer", how = $"Travel {ExplorerDistance}m from the village", bonus = "Start every run with 2 extra attribute points" },
                    new Achievement { id = "guardianbane", title = "Guardian's Bane", how = "Defeat all four guardians in one run", bonus = "Start every run with an extra skill point" },
                    new Achievement { id = "worldsaver", title = "World Saver", how = "Defeat the World's Fear", bonus = "Start every run with 300 gold" },
                };
                for (int i = 0; i < 9; i++)
                {
                    var w = (WeaponType)i;
                    all.Add(new Achievement { id = "adept_" + w, title = Weapons.Name(w) + " Adept", how = $"Defeat a guardian with {With(w)}",
                        bonus = $"The Weaponmaster can teach {WeaponMasteries.Name(w)}" });
                    all.Add(new Achievement { id = "legend_" + w, title = Weapons.Name(w) + " Legend", how = $"Defeat the World's Fear with {With(w)}",
                        bonus = $"+{Mathf.RoundToInt(LegendDamage * 100)}% damage with {With(w)}" });
                }
                return all;
            }
        }

        // "a Sword", "an Axe", "Daggers".
        public static string With(WeaponType w)
        {
            string n = Weapons.Name(w);
            if (n.EndsWith("s")) return n;
            return ("AEIOU".IndexOf(n[0]) >= 0 ? "an " : "a ") + n;
        }

        public static Achievement Get(string id) => All.Find(a => a.id == id);
        public static bool Has(string id) => Data.achievements.Contains(id);

        static void Grant(string id)
        {
            if (Has(id)) return;
            var a = Get(id);
            if (a == null) return;
            Data.achievements.Add(id);
            Save();
            Sound.Play(Sfx.LevelUp, Vector3.zero, 0.5f);
            GameUI.Instance?.Toast($"Achievement: {a.title}\n<size=75%>{a.bonus}</size>", Palette.Gold, 5f);
        }

        // ---------- What it unlocks ----------

        public static int TiersOpen => Mathf.Min(WeaponTiers.Length, 1 + Data.runsFinished);

        public static bool WeaponUnlocked(WeaponType w)
        {
            if (w == WeaponType.Fists) return true;
            for (int t = 0; t < TiersOpen; t++)
                if (Array.IndexOf(WeaponTiers[t], w) >= 0) return true;
            return false;
        }

        // How many more finished runs open this weapon (0 = open).
        public static int RunsUntil(WeaponType w)
        {
            for (int t = 0; t < WeaponTiers.Length; t++)
                if (Array.IndexOf(WeaponTiers[t], w) >= 0) return Mathf.Max(0, t - Data.runsFinished);
            return 0;
        }

        public static List<WeaponType> UnlockedWeapons()
        {
            var list = new List<WeaponType>();
            for (int t = 0; t < TiersOpen; t++) list.AddRange(WeaponTiers[t]);
            return list;
        }

        public static bool MasteryUnlocked(WeaponType w) => Has("adept_" + w);
        public static float WeaponDamageBonus(WeaponType w) => Has("legend_" + w) ? LegendDamage : 0f;
        public static int StartLevel => 1 + (Has("seasoned") ? 1 : 0) + (Has("veteran") ? 1 : 0);

        // A new hero gets every bonus earned so far.
        public static void ApplyStartBonuses(SaveData d)
        {
            for (int lv = 1; lv < StartLevel; lv++)
            {
                d.level++;
                d.skillPoints++;
                d.attributePoints += 3;
            }
            if (Has("lairbreaker")) d.potions += 1;
            if (Has("explorer")) d.attributePoints += 2;
            if (Has("guardianbane")) d.skillPoints += 1;
            if (Has("worldsaver")) d.gold += 300;
        }

        public static string StartBonusText()
        {
            var parts = new List<string>();
            if (StartLevel > 1) parts.Add($"level {StartLevel}");
            if (Has("lairbreaker")) parts.Add("+1 potion");
            if (Has("explorer")) parts.Add("+2 attribute points");
            if (Has("guardianbane")) parts.Add("+1 skill point");
            if (Has("worldsaver")) parts.Add("+300 gold");
            return parts.Count == 0 ? "" : "You start with: " + string.Join(", ", parts);
        }

        // ---------- Events from the game ----------

        public static void OnLevel(int level)
        {
            if (level >= SeasonedLevel) Grant("seasoned");
            if (level >= VeteranLevel) Grant("veteran");
        }

        public static void OnTravel(float distance)
        {
            if (distance >= ExplorerDistance) Grant("explorer");
        }

        public static void OnLairDestroyed()
        {
            Data.lairsDestroyed++;
            Save();
            if (Data.lairsDestroyed >= LairGoal) Grant("lairbreaker");
        }

        public static void OnBossKilled(BossKind kind)
        {
            var w = Weapons.Equipped;
            if (kind == BossKind.WorldsFear)
            {
                Grant("worldsaver");
                if (w != WeaponType.Fists) Grant("legend_" + w);
            }
            else if (kind != BossKind.Titan)
            {
                if (w != WeaponType.Fists) Grant("adept_" + w);
                var slain = Profile.Data.guardiansSlain;
                bool allFour = true;
                foreach (var g in BossTitan.Guardians) if (!slain.Contains(g.ToString())) allFour = false;
                if (allFour) Grant("guardianbane");
            }
        }

        // The run is over: by a fall, or by beating the World's Fear. Counted once per hero. Returns what it opened.
        public static string FinishRun()
        {
            var d = Profile.Data;
            if (d == null || d.runCounted) return "";
            d.runCounted = true;
            int before = TiersOpen;
            Data.runsFinished++;
            Save();
            if (TiersOpen == before) return "";
            var names = new List<string>();
            foreach (var w in WeaponTiers[TiersOpen - 1]) names.Add(Weapons.Name(w));
            return "New weapons unlocked: " + string.Join(" and ", names);
        }
    }
}
