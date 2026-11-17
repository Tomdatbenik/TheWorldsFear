using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF.Tests
{
    // The debug scene (Scenes/TestRange): walls, stairs, a slope and every monster in one small yard, at a fixed noon.
    // Combat, effects and rendering are checked here, where nothing depends on where the world put things.
    public static class TestRangeChecks
    {
        static readonly Vector2 Open = new Vector2(0f, -8f);   // open paving south of the stairs

        static IEnumerator Yard()
        {
            yield return Play.Scene("TestRange", 3f);
            foreach (var m in Object.FindObjectsByType<MonsterBrain>()) Object.Destroy(m.gameObject);
            foreach (var p in Object.FindObjectsByType<Projectile>()) Object.Destroy(p.gameObject);
            Play.MoveHero(new Vector2(0f, -2f));
            yield return null;
        }

        static MonsterBrain SpawnAny(Vector2 at, int level = 5)
        {
            foreach (var def in MobCatalog.All) return MonsterBrain.Spawn(MonsterGenerator.CreateMob(def), level, MonsterRank.Normal, at);
            Check.Fail("The mob catalogue is empty");
            return null;
        }

        [TwfCheck("TestRange/The yard is drawn: walls, stairs and slope (no missing shaders)")]
        static IEnumerator Picture()
        {
            yield return Yard();
            yield return new WaitForEndOfFrame();
            Play.LooksRight(Play.Shoot("testrange_yard"), 0.08f, "The yard from the start");
            // Up on the platform, by the wall and on the slope: each view has its own geometry.
            foreach (var (name, at) in new[] { ("platform", new Vector2(0f, 14f)), ("wall", new Vector2(-20f, 20f)), ("slope", new Vector2(20f, 5f)), ("bridge", new Vector2(31f, 4f)) })
            {
                Play.MoveHero(at);
                yield return Play.Wait(1.5f);
                yield return new WaitForEndOfFrame();
                Play.LooksRight(Play.Shoot("testrange_" + name), 0.08f, $"The yard at the {name}");
            }
        }

        [TwfCheck("TestRange/Heights: platform, stairs and slope carry the hero")]
        static IEnumerator Heights()
        {
            yield return Yard();
            Check.Near(TerrainHeight.At(new Vector2(0f, 15f)), 2.4f, 0.01f, "platform height");
            Check.Near(TerrainHeight.At(new Vector2(0f, 8f)), 1.2f, 0.05f, "half way up the stone stairs");
            Check.Near(TerrainHeight.At(new Vector2(0f, -8f)), 0f, 0.01f, "the yard floor");
            foreach (var (at, what) in new[] { (new Vector2(0f, 15f), "platform"), (new Vector2(0f, 8f), "stairs"), (new Vector2(20f, 5f), "slope"), (new Vector2(20f, 15f), "ledge") })
            {
                Play.MoveHero(at);
                yield return Play.Wait(1.5f);
                Check.Near(HeroController.Instance.transform.position.y, TerrainHeight.At(Play.HeroAt), 0.1f, $"hero height on the {what}");
            }
        }

        [TwfCheck("TestRange/Every monster spawns with a body and health")]
        static IEnumerator EveryMonster()
        {
            yield return Yard();
            int n = 0;
            foreach (var def in MobCatalog.All)
            {
                var m = MonsterBrain.Spawn(MonsterGenerator.CreateMob(def), 5, MonsterRank.Normal, Open + new Vector2(n % 8 * 2f - 7f, -(n / 8) * 2f));
                Check.That(m != null, $"Monster '{def.name}' did not spawn");
                Check.That(m.Combatant != null && m.Combatant.MaxHP > 0f && !m.Combatant.IsDead, $"Monster '{def.name}' spawned without health");
                Check.That(m.GetComponentInChildren<Renderer>() != null, $"Monster '{def.name}' has nothing to draw");
                n++;
            }
            foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
                foreach (var sp in HandmadeMonsters.For(b))
                {
                    var m = MonsterBrain.Spawn(sp, 5, MonsterRank.Normal, Open + new Vector2(n % 8 * 2f - 7f, -(n / 8) * 2f));
                    Check.That(m != null && m.Combatant != null && m.Combatant.MaxHP > 0f, $"Handmade monster '{sp.name}' ({b}) did not spawn properly");
                    n++;
                }
            Check.AtLeast(n, 5, "monster kinds");
            // A second of life with all of them awake: thinking, animating and drawing must not throw.
            yield return Play.Wait(1.5f);
            yield return new WaitForEndOfFrame();
            Play.LooksRight(Play.Shoot("testrange_monsters"), 0.08f, "The yard full of monsters");
            foreach (var m in Object.FindObjectsByType<MonsterBrain>()) Object.Destroy(m.gameObject);
        }

        [TwfCheck("TestRange/Ranks: elites and rares spawn with affixes")]
        static IEnumerator Ranks()
        {
            yield return Yard();
            MobDef first = null;
            foreach (var def in MobCatalog.All) { first = def; break; }
            var normal = MonsterBrain.Spawn(MonsterGenerator.CreateMob(first), 10, MonsterRank.Normal, Open);
            var elite = MonsterBrain.Spawn(MonsterGenerator.CreateMob(first), 10, MonsterRank.Elite, Open + Vector2.right * 3f);
            var rare = MonsterBrain.Spawn(MonsterGenerator.CreateMob(first), 10, MonsterRank.Rare, Open + Vector2.left * 3f, MonsterSpawner.RollAffixes(new System.Random(3), 2));
            yield return null;
            Check.That(elite.Combatant.MaxHP > normal.Combatant.MaxHP, "An elite should have more health than a normal monster");
            Check.That(rare.Combatant.MaxHP > normal.Combatant.MaxHP, "A rare should have more health than a normal monster");
            yield return Play.Wait(1f);
        }

        [TwfCheck("TestRange/Attacks: damage hurts, kills and is refused by the invulnerable")]
        static IEnumerator Damage()
        {
            yield return Yard();
            var hero = HeroController.Instance.Combatant;
            var m = SpawnAny(Open);
            yield return null;
            float before = m.Combatant.HP;
            float dealt = m.Combatant.TakeDamage(new DamageInfo { source = hero, amount = before * 0.25f, trueDamage = true });
            Check.That(dealt > 0f && m.Combatant.HP < before, $"A hit did no damage (dealt {dealt}, health {before} -> {m.Combatant.HP})");
            Check.That(!m.Combatant.IsDead, "A quarter of its health killed the monster");

            float armoured = m.Combatant.HP;
            m.Combatant.TakeDamage(new DamageInfo { source = hero, amount = armoured * 0.1f, isBasicAttack = true });
            Check.That(m.Combatant.HP <= armoured, "A basic attack healed the monster");

            m.Combatant.Invulnerable = true;
            float held = m.Combatant.HP;
            m.Combatant.TakeDamage(new DamageInfo { source = hero, amount = 99999f, trueDamage = true });
            Check.Near(m.Combatant.HP, held, 0.001f, "health of an invulnerable monster after a hit");
            m.Combatant.Invulnerable = false;

            bool died = false;
            m.Combatant.Died += _ => died = true;
            m.Combatant.TakeDamage(new DamageInfo { source = hero, amount = 99999f, trueDamage = true });
            Check.That(m.Combatant.IsDead && died, "A lethal hit did not kill the monster");
            yield return Play.Wait(1.5f);   // death animation, loot, sounds
        }

        [TwfCheck("TestRange/Attacks: statuses, shields and healing")]
        static IEnumerator Statuses()
        {
            yield return Yard();
            var hero = HeroController.Instance.Combatant;
            var m = SpawnAny(Open);
            yield return null;
            m.Combatant.Stun(1f);
            Check.That(m.Combatant.IsStunned && m.Combatant.MoveSpeed == 0f, "A stunned monster can still move");
            yield return Play.Until(() => !m.Combatant.IsStunned, 3f, "the stun to wear off");

            float full = m.Combatant.HP;
            m.Combatant.TakeDamage(new DamageInfo { source = hero, amount = full * 0.5f, trueDamage = true });
            float hurt = m.Combatant.HP;
            m.Combatant.Heal(full * 0.2f, false);
            Check.That(m.Combatant.HP > hurt, "Healing did not restore health");
            Check.AtMost(m.Combatant.HP, m.Combatant.MaxHP + 0.01f, "health after healing");

            float beforeShield = m.Combatant.HP;
            m.Combatant.AddShield(1000f, 5f);
            m.Combatant.TakeDamage(new DamageInfo { source = hero, amount = 10f, trueDamage = true });
            Check.Near(m.Combatant.HP, beforeShield, 0.001f, "health behind a shield after a small hit");
        }

        [TwfCheck("TestRange/Attacks: projectiles fly, hit and stop at walls")]
        static IEnumerator Projectiles()
        {
            yield return Yard();
            var hero = HeroController.Instance.Combatant;
            // Sight lines: open paving is clear, the crenellated wall (y 22..24, x -34..-8) is not.
            Check.That(!Walls.Blocked(new Vector2(0f, -8f), new Vector2(6f, -8f)), "Open paving counts as blocked");
            Check.That(Walls.Blocked(new Vector2(-20f, 20f), new Vector2(-20f, 27f)), "The wall does not block a line through it");

            Play.MoveHero(Open);
            yield return null;
            var m = SpawnAny(Open + Vector2.right * 5f);
            yield return null;
            float before = m.Combatant.HP;
            var spec = ProjectileSpec.Default(Color.cyan);
            var shot = Projectile.Fire(hero, Open + Vector2.right * 0.8f, Vector2.right, spec);
            Check.That(shot != null, "Projectile.Fire returned nothing");
            yield return Play.Until(() => m == null || m.Combatant.HP < before || m.Combatant.IsDead, 4f, "a projectile fired straight at a monster 5 tiles away to hit it");

            // Fired into the wall from the south: it must be gone before it comes out the other side.
            var intoWall = Projectile.Fire(hero, new Vector2(-20f, 19f), Vector2.up, spec);
            float until = Time.realtimeSinceStartup + 4f;
            while (intoWall != null && Time.realtimeSinceStartup < until)
            {
                Check.AtMost(intoWall.transform.G().y, 24.5f, "how far north a projectile got through the wall (y)");
                yield return null;
            }
        }

        [TwfCheck("TestRange/Effects: areas, telegraphs and particles play without errors")]
        static IEnumerator Effects()
        {
            yield return Yard();
            var hero = HeroController.Instance.Combatant;
            Play.MoveHero(Open);
            yield return null;
            var m = SpawnAny(Open + Vector2.right * 2f);
            yield return null;
            float before = m.Combatant.HP;
            var area = AreaEffect.Spawn(hero, Open + Vector2.right * 2f, new AreaSpec { radius = 3f, delay = 0.2f, ticks = 2, interval = 0.3f, damageMult = 1f, color = Color.red, snapshotDamage = before * 0.2f });
            Check.That(area != null, "AreaEffect.Spawn returned nothing");
            VFX.Burst(Open, Color.yellow, 12, 1.5f);
            VFX.Ring(Open, Color.white, 2f, 0.4f);
            VFX.Slash(Ground.On(Open), Vector2.right, 1.5f, 90f, Color.white);
            LineTelegraph.Show(Open, Vector2.up, 6f, 1f, Color.red, 0.5f);
            yield return Play.Until(() => m == null || m.Combatant.HP < before, 4f, "an area effect on top of a monster to damage it");
            yield return new WaitForEndOfFrame();
            Play.LooksRight(Play.Shoot("testrange_effects"), 0.08f, "The yard with effects playing");
            yield return Play.Wait(1.5f);
        }

        [TwfCheck("TestRange/Hero: every weapon swings and every slotted skill casts")]
        static IEnumerator HeroAttacks()
        {
            yield return Yard();
            var hero = HeroController.Instance;
            Play.MoveHero(Open);
            yield return null;
            var dummy = SpawnAny(Open + Vector2.right * 1.5f, 50);
            dummy.Combatant.Invulnerable = true;
            yield return null;
            int cast = 0;
            for (int slot = 0; slot < 6; slot++)
            {
                SkillDefinition skill = null;
                try { skill = hero.SkillIn(slot); } catch (System.IndexOutOfRangeException) { break; }
                if (skill == null) continue;
                hero.ResetCooldowns();
                hero.CastSkill(skill, 1, Vector2.right, Open + Vector2.right * 1.5f);
                cast++;
                yield return Play.Wait(0.8f);
            }
            Check.AtLeast(cast, 1, "skills the test hero could cast");
            yield return Play.Wait(1f);
        }

        [TwfCheck("TestRange/Loot: gold, potions and items drop and can be picked up")]
        static IEnumerator LootDrops()
        {
            yield return Yard();
            Play.MoveHero(Open);
            yield return null;
            int goldBefore = Profile.Data.gold;
            Pickup.SpawnGold(Open + Vector2.right * 6f, 25);
            Pickup.SpawnPotion(Open + Vector2.right * 7f);
            yield return null;
            Check.AtLeast(Object.FindObjectsByType<Pickup>().Length, 2, "pickups lying in the yard");
            // Walk over the gold.
            Play.MoveHero(Open + Vector2.right * 6f);
            yield return Play.Until(() => Profile.Data.gold > goldBefore, 4f, "the hero to pick up gold it stands on");
        }
    }
}
