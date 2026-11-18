using System.Collections;
using UnityEngine;

namespace TWF.Tests
{
    // Hard mode: the hero's stats are 40% lower, all but move speed.
    public static class DifficultyChecks
    {
        [TwfCheck("Difficulty/Hard mode lowers every stat by 40% except move speed")]
        static void HardScale()
        {
            Check.Near(Profile.HardStatScale, 0.6f, 0.0001f, "the share of its stats a hard-mode hero keeps");
            var normal = new CharacterStats();
            var hard = new CharacterStats();
            foreach (var s in new[] { normal, hard })
            {
                for (int i = 0; i < CharacterStats.Count; i++) s.SetBase((StatType)i, 0.25f);
                s.SetBase(StatType.MaxHP, 200f);
                s.SetBase(StatType.Damage, 50f);
                s.SetBase(StatType.Armor, 30f);
                s.SetBase(StatType.MoveSpeed, 5f);
                s.SetBase(StatType.AttackSpeed, 1f);
                s.SetBase(StatType.ExtraProjectiles, 0f);
                s.SetBase(StatType.ExtraSlashes, 0f);
                s.AddModifier(new StatModifier(StatType.Damage, ModKind.Percent, 0.5f));
            }
            hard.Scale = Profile.HardStatScale;
            for (int i = 0; i < CharacterStats.Count; i++)
            {
                var st = (StatType)i;
                float want = st == StatType.MoveSpeed ? normal.Get(st) : normal.Get(st) * 0.6f;
                Check.Near(hard.Get(st), want, 0.0005f, $"{StatInfo.Name(st)} in hard mode (normal: {normal.Get(st)})");
            }
            Check.Near(hard.Get(StatType.Damage), 45f, 0.001f, "hard-mode damage from 50 base +50%");
        }

        [TwfCheck("Difficulty/A hard-mode hero is weaker in play, a normal one is not")]
        static IEnumerator HardHero()
        {
            yield return Play.Scene("TestRange", 2f);
            var hero = HeroController.Instance;
            var d = Profile.Data;
            int keep = d.difficulty;
            try
            {
                d.difficulty = 0;
                hero.RebuildStats();
                float hp = hero.Combatant.MaxHP, dmg = hero.Combatant.Stats.Get(StatType.Damage), speed = hero.Combatant.Stats.Get(StatType.MoveSpeed);
                d.difficulty = 1;
                hero.RebuildStats();
                Check.Near(hero.Combatant.MaxHP, hp * 0.6f, 0.6f, "life of the hero in hard mode");
                Check.Near(hero.Combatant.Stats.Get(StatType.Damage), dmg * 0.6f, 0.6f, "damage of the hero in hard mode");
                Check.Near(hero.Combatant.Stats.Get(StatType.MoveSpeed), speed, 0.001f, "move speed of the hero in hard mode");
                d.difficulty = 0;
                hero.RebuildStats();
                Check.Near(hero.Combatant.MaxHP, hp, 0.6f, "life of the hero back in normal mode");
            }
            finally
            {
                d.difficulty = keep;
                hero.RebuildStats();
            }
        }
    }
}
