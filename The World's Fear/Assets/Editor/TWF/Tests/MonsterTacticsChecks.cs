using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TWF.Tests
{
    // Monsters that seem to think: grabs, sidesteps, hopping back, fleeing to a healer, healers, ledges and habits.
    public static class MonsterTacticsChecks
    {
        static readonly Vector2 Open = new Vector2(0f, -8f);   // open paving of the test yard

        static IEnumerator Yard()
        {
            yield return Play.Scene("TestRange", 3f);
            Clear();
            Play.MoveHero(Open);
            var hero = HeroController.Instance;
            hero.Release(false);
            hero.Combatant.ResetLife();
            HeroHabits.Force(false, false, false);
            yield return null;
        }

        static void Clear()
        {
            foreach (var m in Object.FindObjectsByType<MonsterBrain>()) Object.Destroy(m.gameObject);
            foreach (var p in Object.FindObjectsByType<Projectile>()) Object.Destroy(p.gameObject);
        }

        static MonsterBrain Spawn(MonsterArchetype a, Vector2 at, int level = 5) =>
            MonsterBrain.Spawn(MonsterGenerator.Create((int)a * 97 + 5, Biome.Forest, a), level, MonsterRank.Normal, at);

        static void Hurt(Combatant c, float share) =>
            c.TakeDamage(new DamageInfo { amount = c.MaxHP * share, trueDamage = true, silent = true });

        [TwfCheck("Tactics/A brute grabs the hero, and lets go when hurt; a hero can wriggle free")]
        static IEnumerator Grab()
        {
            yield return Yard();
            var hero = HeroController.Instance;
            // The yard's god mode keeps the hero invulnerable, and an invulnerable hero can't be grabbed.
            var range = Object.FindAnyObjectByType<TestRange>();
            var god = typeof(TestRange).GetField("god", BindingFlags.NonPublic | BindingFlags.Instance);
            Check.That(range != null && god != null, "The test yard's god mode switch is missing");
            god.SetValue(range, false);
            hero.Combatant.Invulnerable = false;
            try
            {
                var brute = Spawn(MonsterArchetype.Brute, Open + Vector2.right * 2.8f);
                brute.Aggro();
                int grabs = MonsterBrain.Grabs;
                yield return Play.Until(() => hero.Held || MonsterBrain.Grabs > grabs, 12f, "the brute's grab");
                Check.That(hero.Held && hero.HeldBy == brute.Combatant, "The brute's grab did not hold the hero");
                // Held: the hero cannot walk away.
                Vector2 at = Play.HeroAt;
                yield return Play.Wait(0.4f);
                Check.AtMost(Vector2.Distance(Play.HeroAt, at), 0.6f, "how far a held hero moved");
                // Hurt enough, the brute lets go and is left reeling.
                hero.Combatant.ResetLife();
                Hurt(brute.Combatant, MonsterBrain.GrabBreak + 0.05f);
                yield return Play.Wait(0.2f);
                Check.That(!hero.Held && hero.BrokeFree, "Hurting the brute did not free the hero");
                Check.That(brute.Combatant.IsStunned, "A brute that loses its hold is not left reeling");

                // Wriggling: every swing of the stick to a new side takes time off the hold.
                var update = typeof(HeroController).GetMethod("UpdateHold", BindingFlags.NonPublic | BindingFlags.Instance);
                Check.That(update != null, "HeroController.UpdateHold is missing");
                var holder = Spawn(MonsterArchetype.Chaser, Open + Vector2.left * 5f).Combatant;
                yield return null;
                Check.That(hero.Grab(holder, MonsterBrain.GrabHold), "A hero could not be grabbed");
                int swings = 0;
                while (hero.Held && swings < 20)
                {
                    update.Invoke(hero, new object[] { 0.01f, swings % 2 == 0 ? Vector2.right : Vector2.left });
                    swings++;
                }
                Check.That(!hero.Held && hero.BrokeFree, "Wriggling did not free the hero");
                Check.AtMost(swings, Mathf.CeilToInt(MonsterBrain.GrabHold / HeroController.WriggleShortens) + 2, "swings of the stick needed to break free");
            }
            finally
            {
                hero.Release(false);
                god.SetValue(range, true);
                hero.Combatant.Invulnerable = true;
                hero.Combatant.ResetLife();
                Clear();
            }
        }

        [TwfCheck("Tactics/A healer mends the most wounded, and enough damage breaks its spell")]
        static IEnumerator Healer()
        {
            yield return Yard();
            var healer = MonsterBrain.Spawn(MonsterGenerator.HealerFor(Biome.Forest), 5, MonsterRank.Normal, Open + new Vector2(6f, 0f));
            var ally = Spawn(MonsterArchetype.Brute, Open + new Vector2(6f, 3f));
            var other = Spawn(MonsterArchetype.Brute, Open + new Vector2(6f, -3f));
            yield return null;
            Check.That(healer.Species.archetype == MonsterArchetype.Healer, "The healer species is not a healer");
            ally.Combatant.SetHP(ally.Combatant.MaxHP * 0.3f);
            other.Combatant.SetHP(other.Combatant.MaxHP * 0.6f);
            healer.Aggro();
            int heals = MonsterBrain.Heals;
            yield return Play.Until(() => MonsterBrain.Heals > heals, 8f, "the healer's heal");
            Check.That(ally.Combatant.HPFraction > 0.45f, $"The most wounded ally was not mended (at {ally.Combatant.HPFraction:P0})");
            Check.Near(other.Combatant.HPFraction, 0.6f, 0.01f, "the life of the less wounded ally");

            // Hit hard while it channels: the spell breaks.
            other.Combatant.SetHP(other.Combatant.MaxHP * 0.2f);
            int interrupts = MonsterBrain.Interrupts;
            yield return Play.Until(() => healer.Healing, 8f, "the healer's next heal");
            Hurt(healer.Combatant, MonsterBrain.HealInterrupt + 0.02f);
            yield return Play.Wait(0.2f);
            Check.That(MonsterBrain.Interrupts > interrupts && !healer.Healing, "Damage did not break the healer's spell");
            Check.AtMost(other.Combatant.HPFraction, 0.21f, "life of the ally whose heal was broken");
            Clear();
        }

        [TwfCheck("Tactics/Every biome past the farms has a designed healer that mends")]
        static IEnumerator DesignedHealers()
        {
            yield return Yard();
            foreach (var biome in new[] { Biome.Forest, Biome.Marsh, Biome.Ruins, Biome.Ember })
            {
                var sp = MonsterGenerator.HealerFor(biome);
                Check.That(sp.archetype == MonsterArchetype.Healer, $"The {biome} healer is a {sp.archetype}");
                Check.That(sp.mob != null && sp.mob.sprites != null, $"The {biome} healer is not a designed mob (it is the generated '{sp.name}')");
                Check.That(sp.mob.warbandOnly, $"The {biome} healer '{sp.name}' could lead an ordinary pack");
                var healer = MonsterBrain.Spawn(sp, 5, MonsterRank.Normal, Open + new Vector2(5f, 0f));
                var ally = Spawn(MonsterArchetype.Brute, Open + new Vector2(5f, 3f));
                yield return null;
                ally.Combatant.SetHP(ally.Combatant.MaxHP * 0.3f);
                healer.Aggro();
                int heals = MonsterBrain.Heals;
                yield return Play.Until(() => MonsterBrain.Heals > heals, 8f, $"the {biome} healer '{sp.name}' to heal");
                Check.That(healer.GetComponentInChildren<SpriteRenderer>().sprite != null, $"The {biome} healer has nothing to draw");
                Clear();
                yield return null;
            }
        }

        [TwfCheck("Tactics/A wounded monster howls, runs to its healer and comes back angry")]
        static IEnumerator Flee()
        {
            yield return Yard();
            var runner = Spawn(MonsterArchetype.Chaser, Open + new Vector2(3f, 0f));
            var healer = MonsterBrain.Spawn(MonsterGenerator.HealerFor(Biome.Forest), 5, MonsterRank.Normal, Open + new Vector2(9f, 0f));
            var sleeper = Spawn(MonsterArchetype.Chaser, Open + new Vector2(12f, 4f));   // out of the hero's sight, in earshot
            yield return null;
            runner.FleeNature = true;
            runner.Aggro();
            yield return Play.Wait(0.3f);
            Check.That(!sleeper.InFight, "The far monster joined the fight before any howl");
            int howls = MonsterBrain.Howls;
            Hurt(runner.Combatant, 0.75f);
            yield return Play.Until(() => runner.IsFleeing, 2f, "the wounded monster to run");
            Check.That(MonsterBrain.Howls > howls, "The fleeing monster did not howl");
            Check.That(sleeper.InFight, "The howl did not bring the far monster into the fight");
            float before = Vector2.Distance(runner.transform.G(), healer.transform.G());
            yield return Play.Until(() => runner.Enraged, 8f, "the healer to mend the runner, and the runner to turn angry");
            Check.That(!runner.IsFleeing, "The mended monster kept running");
            Check.That(runner.Combatant.HPFraction > 0.4f, "The angry monster was not mended");
            Check.That(before > 0f, "nothing");
            Clear();
        }

        [TwfCheck("Tactics/Skirmishers sidestep a swing, casters hop back from the hero")]
        static IEnumerator React()
        {
            yield return Yard();
            var skirmisher = Spawn(MonsterArchetype.Skirmisher, Open + new Vector2(1.8f, 0f));
            skirmisher.Aggro();
            int dodges = MonsterBrain.Dodges;
            for (float t = 0f; t < 6f && MonsterBrain.Dodges == dodges; t += 0.25f)
            {
                Vector2 to = skirmisher.transform.G() - Play.HeroAt;
                HeroThreat.Swing(Play.HeroAt, to, to.magnitude + 0.5f);
                yield return Play.Wait(0.25f);
            }
            Check.That(MonsterBrain.Dodges > dodges, "The skirmisher never sidestepped a swing aimed at it");
            Clear();

            var caster = Spawn(MonsterArchetype.Caster, Open + new Vector2(1.4f, 0f));
            caster.Aggro();
            int backsteps = MonsterBrain.Backsteps;
            yield return Play.Until(() => MonsterBrain.Backsteps > backsteps, 3f, "the caster to hop back");
            yield return Play.Wait(0.4f);
            Check.AtLeast(Vector2.Distance(caster.transform.G(), Play.HeroAt), 2.8f, "distance of the caster from the hero after hopping back");
            Clear();
        }

        [TwfCheck("Tactics/A hero who lives on potions is made too sick to drink")]
        static IEnumerator Nausea()
        {
            yield return Yard();
            var hero = HeroController.Instance;
            var spitter = Spawn(MonsterArchetype.Spitter, Open + new Vector2(5f, 0f));
            yield return null;
            int keep = Profile.Data.potions;
            try
            {
                Profile.Data.potions = 5;
                HeroHabits.Force(false, false, true);
                hero.Combatant.SetHP(hero.Combatant.MaxHP * 0.3f);
                for (int i = 0; i < 40 && !hero.PotionBlocked; i++)
                    HeroHabits.HeroHit(hero, new DamageInfo { source = spitter.Combatant, amount = 1f });
                Check.That(hero.PotionBlocked, "Shooters' hits never made a potion-drinking hero sick");
                Check.That(!hero.UsePotion(), "A sick hero could drink a potion");
                HeroHabits.Force(false, false, false);
                hero.PotionBlockedUntil = 0f;
                hero.Combatant.SetHP(hero.Combatant.MaxHP * 0.3f);
                for (int i = 0; i < 40; i++)
                    HeroHabits.HeroHit(hero, new DamageInfo { source = spitter.Combatant, amount = 1f });
                Check.That(!hero.PotionBlocked, "A hero who seldom drinks was made sick");
            }
            finally
            {
                Profile.Data.potions = keep;
                hero.PotionBlockedUntil = 0f;
                HeroHabits.Force(false, false, false);
                hero.Combatant.ResetLife();
                Clear();
            }
        }

        [TwfCheck("Tactics/Healers join packs beyond the farms, never near the start")]
        static IEnumerator HealersKeepOut()
        {
            yield return Play.Scene("World", 3f);
            Check.That(MonsterSpawner.HealerMinDistance > WorldGen.FarmRingOuter, "Healers may spawn inside the farms");
            var near = new List<MonsterBrain>();
            var far = new List<MonsterBrain>();
            var rng = new System.Random(5);
            for (int i = 0; i < 60; i++)
            {
                float a = i * 0.37f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var inFarms = dir * Mathf.Lerp(WorldGen.FarmRingInner, WorldGen.FarmRingOuter - 1f, (i % 10) / 10f);
                var beyond = dir * (MonsterSpawner.HealerMinDistance + 40f + i * 3f);
                MonsterSpawner.SpawnPack(WorldGen.RegionAt(inFarms), inFarms, 0.5f, near, rng, WorldGen.LevelAt(inFarms));
                MonsterSpawner.SpawnPack(WorldGen.RegionAt(beyond), beyond, 0.5f, far, rng, WorldGen.LevelAt(beyond));
            }
            int healersNear = near.FindAll(m => m.Species.archetype == MonsterArchetype.Healer).Count;
            int healersFar = far.FindAll(m => m.Species.archetype == MonsterArchetype.Healer).Count;
            foreach (var m in near) if (m != null) Object.Destroy(m.gameObject);
            foreach (var m in far) if (m != null) Object.Destroy(m.gameObject);
            Check.That(healersNear == 0, $"{healersNear} healers spawned in the farmland");
            Check.AtLeast(healersFar, 3, "healers in 60 packs beyond the farms");
        }

        [TwfCheck("Tactics/A charge can knock the hero off a ledge, down a level")]
        static IEnumerator Ledge()
        {
            yield return Play.Scene("World", 3f);
            var hero = HeroController.Instance;
            // A ledge with a run-up, searched outward from the town.
            Vector2 spot = default, dir = default;
            bool found = false;
            for (int r = 130; r < 1500 && !found; r += 9)
                for (int k = 0; k < 64 && !found; k++)
                {
                    var p = new Vector2(Mathf.Cos(k * 0.0982f) * r, Mathf.Sin(k * 0.0982f) * r);
                    p = new Vector2(Mathf.Floor(p.x) + 0.5f, Mathf.Floor(p.y) + 0.5f);
                    if (MonsterBrain.FindLedge(p, out dir)) { spot = p; found = true; }
                }
            Check.That(found, "No ledge found in the world to test");
            int top = WorldGen.ElevAt(Mathf.FloorToInt(spot.x), Mathf.FloorToInt(spot.y));
            Play.MoveHero(spot);
            yield return Play.Wait(3f);
            Check.That(WorldGen.ElevAt(Mathf.FloorToInt(Play.HeroAt.x), Mathf.FloorToInt(Play.HeroAt.y)) == top, $"The hero set on the ledge at {spot} did not stay on its level");
            spot = Play.HeroAt;   // where it settled
            hero.Combatant.Invulnerable = false;
            try
            {
                // With the charge's leave to fall, the hero goes over the edge and lands below.
                Play.MoveHero(spot);
                yield return Play.Wait(1f);
                hero.AllowFall(0.7f);
                hero.Combatant.TakeDamage(new DamageInfo { amount = 0.01f, trueDamage = true, silent = true, knockback = dir * MonsterBrain.ShoveKnockback });
                yield return Play.Wait(1.2f);
                int below = WorldGen.ElevAt(Mathf.FloorToInt(Play.HeroAt.x), Mathf.FloorToInt(Play.HeroAt.y));
                Check.That(below < top, $"The charge's shove did not carry the hero off the ledge at {spot} (level {top}, now {below})");
            }
            finally
            {
                hero.Combatant.Invulnerable = true;
                hero.Combatant.ResetLife();
            }
        }
    }
}
