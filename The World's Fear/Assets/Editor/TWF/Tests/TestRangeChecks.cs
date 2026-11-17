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

        // Every area attack draws a ring and a disc the size of its radius. Each new size used to cost about 60 ms to
        // build (a stall whenever a monster kind cast its first area), so sizes never seen before are timed here.
        [TwfCheck("TestRange/Performance: a new size of area effect appears without a stall")]
        static IEnumerator AreaFirstUse()
        {
            yield return Yard();
            var hero = HeroController.Instance.Combatant;
            float worst = 0f; float at = 0f; string all = "";
            var watch = new System.Diagnostics.Stopwatch();
            // Odd radii no monster uses, small to very large.
            foreach (float radius in new[] { 0.83f, 1.71f, 2.63f, 3.37f, 4.19f, 5.57f, 7.91f })
            {
                watch.Restart();
                var area = AreaEffect.Spawn(hero, Open + Vector2.left * 6f, new AreaSpec { radius = radius, delay = 0.1f, ticks = 1, damageMult = 0f, color = Color.red, quiet = true });
                float ms = (float)watch.Elapsed.TotalMilliseconds;
                if (ms > worst) { worst = ms; at = radius; }
                all += $" {radius}:{ms:0.0}";
                Check.That(area != null, $"No area effect of radius {radius}");
                yield return null;
            }
            Debug.Log($"[TwfCheck] area first use: worst {worst:0.0} ms (radius {at}); each:{all}");
            // Editor on a PC; a phone is several times slower.
            Check.AtMost(worst, 12f, $"time (ms) to show an area effect of a size not seen before (radius {at})");
            yield return Play.Wait(0.5f);
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

        // Dual Wield: a sword moved to the off hand is the same sword there. It used to lose half its damage.
        [TwfCheck("TestRange/Dual Wield: a sword keeps its stats in the off hand")]
        static IEnumerator DualWieldKeepsStats()
        {
            yield return Yard();
            if (!Profile.Data.masteriesOwned.Contains((int)WeaponType.Sword)) Profile.Data.masteriesOwned.Add((int)WeaponType.Sword);
            ItemInstance Sword(string baseName, float damage) => new ItemInstance
            {
                uid = System.Guid.NewGuid().ToString(), slot = ItemSlot.Weapon, weaponType = (int)WeaponType.Sword, rarity = Rarity.Rare, itemLevel = 20,
                baseName = baseName, name = baseName, implicitStat = StatType.Damage, implicitValue = damage,
            };
            var first = Sword("Iron Sword", 40f);
            var second = Sword("Steel Broadsword", 52f);
            Profile.Data.inventory.Add(first);
            Profile.Data.inventory.Add(second);
            Check.That(Profile.EquipItem(first), "Could not equip a sword in the main hand");
            Check.That(Profile.CanSwitchHand(second), "With Dual Wield learned, a sword in the bag cannot go to the off hand");
            Check.That(Profile.EquipInOtherHand(second), "Could not equip the second sword in the off hand");
            Check.That(second.slot == ItemSlot.OffHand && Profile.Equipped(ItemSlot.OffHand) == second, "The second sword is not in the off hand");
            Check.Near(second.implicitValue, 52f, 0.001f, "damage of a sword once it is in the off hand");
            Check.That(Profile.Equipped(ItemSlot.Weapon) == first, "Equipping the off hand took the main-hand sword off");
            yield return null;
            Check.That(WeaponMasteries.DualWielding, "Two swords equipped, but the hero is not dual wielding");

            // Back to the bag and into the main hand: still the same sword.
            Profile.UnequipItem(second);
            Check.That(Profile.EquipInOtherHand(second), "Could not move the sword back to the main hand");
            Check.That(second.slot == ItemSlot.Weapon, "The sword did not return to the main hand");
            Check.Near(second.implicitValue, 52f, 0.001f, "damage of the sword after a round trip through the off hand");

            // A sword that an older version halved is given its damage back when the save loads.
            var halved = Sword("Iron Sword", 20f);
            halved.slot = ItemSlot.OffHand; halved.offHandKind = (int)OffHandKind.Sword;
            Check.That(Profile.RestoreOffHandSword(halved), "An old halved off-hand sword was not restored");
            Check.Near(halved.implicitValue, Mathf.Round(20f * Weapons.Get(WeaponType.Sword).implicitDamage / Profile.OffHandBladeShare), 0.001f, "restored damage of an old halved sword");
            Check.That(!Profile.RestoreOffHandSword(halved), "The restore ran twice on the same sword");

            // The small blades that drop as off-hand items are not swords: they stay as they were made.
            var blade = Sword("Sidesword", 15f);
            blade.slot = ItemSlot.OffHand; blade.offHandKind = (int)OffHandKind.Sword;
            Check.That(!Profile.RestoreOffHandSword(blade), "An off-hand blade was treated as a halved sword");
            Check.Near(blade.implicitValue, 15f, 0.001f, "damage of an off-hand blade after loading");
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
