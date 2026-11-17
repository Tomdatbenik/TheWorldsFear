using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF.Tests
{
    // Mythic powers: what each gear slot can roll, and what the powers do once worn.
    public static class MythicChecks
    {
        static readonly (ItemSlot slot, OffHandKind kind, string name)[] Slots =
        {
            (ItemSlot.Weapon, 0, "weapon"), (ItemSlot.Helm, 0, "helm"), (ItemSlot.Chest, 0, "chest"), (ItemSlot.Gloves, 0, "gloves"),
            (ItemSlot.Boots, 0, "boots"), (ItemSlot.Ring, 0, "ring"), (ItemSlot.Amulet, 0, "amulet"),
            (ItemSlot.OffHand, OffHandKind.Shield, "shield"), (ItemSlot.OffHand, OffHandKind.Orb, "orb"), (ItemSlot.OffHand, OffHandKind.Tome, "tome"),
            (ItemSlot.OffHand, OffHandKind.Quiver, "quiver"), (ItemSlot.OffHand, OffHandKind.ParryDagger, "parry dagger"), (ItemSlot.OffHand, OffHandKind.Sword, "off-hand sword"),
        };

        [TwfCheck("Mythics/Every slot rolls three different powers")]
        static void ThreePerSlot()
        {
            var inSomePool = new HashSet<string>();
            foreach (var (slot, kind, name) in Slots)
            {
                var item = new ItemInstance { slot = slot, offHandKind = (int)kind };
                var pool = MythicPowers.PowersFor(item);
                var distinct = new HashSet<string>(pool);
                Check.That(distinct.Count == pool.Length, $"The {name} pool lists a power twice");
                Check.AtLeast(distinct.Count, 3, $"mythic powers a {name} can roll");
                foreach (string id in pool)
                {
                    inSomePool.Add(id);
                    var p = MythicPowers.Find(id);
                    Check.That(p != null, $"The {name} pool names '{id}', which is not a mythic power");
                    Check.That(!string.IsNullOrEmpty(p.name) && !string.IsNullOrEmpty(p.text), $"Mythic power '{id}' has no name or description");
                }
                // The roll reaches every power of the pool, and nothing else.
                var rng = new System.Random(99);
                var rolled = new HashSet<string>();
                for (int i = 0; i < 200; i++)
                {
                    var it = new ItemInstance { slot = slot, offHandKind = (int)kind, element = (int)ElementPart.Physical };
                    MythicPowers.Roll(it, rng);
                    Check.That(distinct.Contains(it.mythicPower), $"A {name} rolled '{it.mythicPower}', which is not in its pool");
                    Check.That(!string.IsNullOrEmpty(it.bonusPassive) && it.bonusRanks >= 1 && it.bonusRanks <= 3, $"A {name} rolled a passive bonus of {it.bonusRanks} on '{it.bonusPassive}'");
                    rolled.Add(it.mythicPower);
                }
                Check.That(rolled.Count == distinct.Count, $"200 {name} rolls reached {rolled.Count} of its {distinct.Count} powers");
            }
            foreach (var p in MythicPowers.All)
                Check.That(inSomePool.Contains(p.id), $"Mythic power '{p.name}' ({p.id}) cannot roll on anything");
            var names = new HashSet<string>();
            foreach (var p in MythicPowers.All) Check.That(names.Add(p.name), $"Two mythic powers are called '{p.name}'");
        }

        // ---------- In play ----------

        static readonly Vector2 Spot = new Vector2(0f, -8f);

        static HeroController Hero => HeroController.Instance;

        static IEnumerator Yard()
        {
            yield return Play.Scene("TestRange", 3f);
            foreach (var m in Object.FindObjectsByType<MonsterBrain>()) Object.Destroy(m.gameObject);
            Profile.Data.equipment.RemoveAll(e => e != null && e.name == "Test Mythic");
            MythicPowers.Refresh(Hero);
            Hero.Combatant.Invulnerable = true;
            Hero.Combatant.SetHP(Hero.Combatant.MaxHP);
            Play.MoveHero(Spot);
            yield return null;
        }

        static ItemInstance Wear(string power, ItemSlot slot, OffHandKind kind = 0, string passive = "p_thickskin", int ranks = 1, bool refresh = true)
        {
            var it = new ItemInstance
            {
                uid = System.Guid.NewGuid().ToString(), slot = slot, offHandKind = (int)kind, rarity = Rarity.Mythic, itemLevel = 20, baseName = "Test Mythic", name = "Test Mythic",
                implicitStat = StatType.Armor, implicitValue = 1f, mythicPower = power, bonusPassive = passive, bonusRanks = ranks,
            };
            Profile.Data.equipment.RemoveAll(e => e != null && e.slot == slot);
            Profile.Data.equipment.Add(it);
            if (refresh) MythicPowers.Refresh(Hero);
            Check.That(!refresh || MythicPowers.Has(power), $"Wearing a mythic with '{power}' did not switch the power on");
            return it;
        }

        // A monster that stands still and does nothing, with plenty of life.
        static MonsterBrain Dummy(Vector2 at, int level = 40)
        {
            MonsterBrain m = null;
            foreach (var def in MobCatalog.All) { m = MonsterBrain.Spawn(MonsterGenerator.CreateMob(def), level, MonsterRank.Normal, at); break; }
            Check.That(m != null, "No monster to test on");
            m.enabled = false;
            return m;
        }

        static float Strike(Combatant target, float amount, bool basic = true) =>
            target.TakeDamage(new DamageInfo { source = Hero.Combatant, amount = amount, trueDamage = true, isBasicAttack = basic });

        static IEnumerator Done()
        {
            Profile.Data.equipment.RemoveAll(e => e != null && e.name == "Test Mythic");
            MythicPowers.Refresh(Hero);
            Hero.Combatant.Invulnerable = true;
            foreach (var m in Object.FindObjectsByType<MonsterBrain>()) Object.Destroy(m.gameObject);
            yield return null;
        }

        [TwfCheck("Mythics/Helm and chest: Thought Storm, Bloodward")]
        static IEnumerator HelmChest()
        {
            yield return Yard();
            var hero = Hero.Combatant;

            Wear("m_thoughtstorm", ItemSlot.Helm);
            // Every skill on a 5-second cooldown, then one spell is cast: the others lose a second, the cast one none.
            var cooldowns = (float[])typeof(HeroController).GetField("cooldowns", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(Hero);
            for (int i = 0; i < cooldowns.Length; i++) cooldowns[i] = 5f;
            int cast = HeroController.WeaponSlot == 0 ? 1 : 0, other = -1;
            for (int i = 0; i < cooldowns.Length; i++) if (i != cast) { other = i; break; }
            MythicPowers.OnSpellCast(Hero, cast, Vector2.right, Spot + Vector2.right * 2f);
            Check.Near(Hero.CooldownLeft(other), 4f, 0.05f, "another skill's cooldown after a spell cast with Thought Storm");
            Check.Near(Hero.CooldownLeft(cast), 5f, 0.05f, "the cast spell's own cooldown with Thought Storm");
            Hero.ResetCooldowns();

            Wear("m_bloodward", ItemSlot.Chest);
            hero.SetHP(hero.MaxHP);
            hero.Heal(hero.MaxHP, false);
            Check.That(hero.ShieldHP > 0f, "Healing at full life gave no shield with Bloodward");
            Check.AtMost(hero.ShieldHP, hero.MaxHP * 0.2f + 0.01f, "Bloodward shield");
            hero.SetHP(hero.MaxHP * 0.5f);
            float shield = hero.ShieldHP;
            hero.Heal(1f, false);
            Check.Near(hero.ShieldHP, shield, 0.01f, "Bloodward shield after a heal that did not overflow");
            yield return Done();
        }

        [TwfCheck("Mythics/Gloves: Tempo, Cruel Hands")]
        static IEnumerator Gloves()
        {
            yield return Yard();
            var hero = Hero.Combatant;
            var a = Dummy(Spot + Vector2.right * 2f);
            var b = Dummy(Spot + Vector2.right * 4f);
            yield return null;

            Wear("m_tempo", ItemSlot.Gloves);
            float speed = hero.Stats.Get(StatType.AttackSpeed);
            for (int i = 0; i < 4; i++) Strike(a.Combatant, 1f);
            Check.That(hero.Stats.Get(StatType.AttackSpeed) > speed * 1.1f, $"Four basic hits with Tempo: attack speed {speed:0.00} -> {hero.Stats.Get(StatType.AttackSpeed):0.00}");
            for (int i = 0; i < 20; i++) Strike(a.Combatant, 1f);
            Check.AtMost(hero.Stats.Get(StatType.AttackSpeed), speed * 1.31f, "attack speed at full Tempo");

            Wear("m_cruelhands", ItemSlot.Gloves);
            float clean = Strike(a.Combatant, 100f, false);
            a.Combatant.ApplyStatus(StatusSpec.Burn(0.1f, 6f), hero, 10f);
            float cruel = Strike(a.Combatant, 100f, false);
            Check.Near(cruel / clean, 1.25f, 0.02f, "damage to a burning enemy against a clean one with Cruel Hands");
            Check.That(!b.Combatant.HasAnyStatus, "The second dummy already has an ailment");
            Strike(a.Combatant, 999999f, false);
            yield return null;
            Check.That(a == null || a.Combatant.IsDead, "The burning dummy did not die");
            Check.That(b.Combatant.HasStatus(StatusType.Burn), "The burn did not jump to the nearby enemy when its bearer died");
            yield return Done();
        }

        [TwfCheck("Mythics/Boots: Earthshaker, Ambush Stride")]
        static IEnumerator Boots()
        {
            yield return Yard();
            var hero = Hero.Combatant;
            var a = Dummy(Spot + Vector2.right * 1.5f);
            yield return null;

            Wear("m_earthshaker", ItemSlot.Boots);
            MythicPowers.OnDashEnd(hero, Spot);
            Check.That(a.Combatant.IsStunned, "The end of a dash beside an enemy did not stun it with Earthshaker");
            float hp = a.Combatant.HP;
            MythicPowers.OnDashEnd(hero, Spot);
            Check.Near(a.Combatant.HP, hp, 0.01f, "enemy life after a second dash inside Earthshaker's 6 seconds");

            Wear("m_ambush", ItemSlot.Boots);
            float walk = hero.Stats.Get(StatType.MoveSpeed);
            Check.That(!MythicPowers.AmbushReady, "Ambush Stride is ready the moment it is put on");
            yield return Play.Until(() => MythicPowers.AmbushReady, 5f, "Ambush Stride to be ready after 3 seconds without attacking");
            yield return Play.Wait(0.2f);
            Check.Near(hero.Stats.Get(StatType.MoveSpeed) / walk, 1.25f, 0.02f, "move speed with Ambush Stride ready");
            float ambush = Strike(a.Combatant, 100f, false);
            float normal = Strike(a.Combatant, 100f, false);
            Check.Near(ambush / normal, 2f, 0.05f, "the ambush hit against the hit after it");
            yield return Play.Wait(1f);
            Check.Near(hero.Stats.Get(StatType.MoveSpeed), walk, 0.01f, "move speed after the ambush is spent");
            yield return Done();
        }

        [TwfCheck("Mythics/Amulet: Resonance, Overcharge")]
        static IEnumerator Amulet()
        {
            yield return Yard();
            var hero = Hero.Combatant;
            var a = Dummy(Spot + Vector2.right * 2f);
            var b = Dummy(Spot + Vector2.right * 3.2f);
            yield return null;

            Wear("m_resonance", ItemSlot.Amulet);
            a.Combatant.ApplyStatus(StatusSpec.Burn(0.01f, 6f), hero, 1f);
            float beside = b.Combatant.HP;
            yield return null;
            Check.Near(b.Combatant.HP, beside, 0.01f, "life of the enemy beside one that only burns");
            a.Combatant.ApplyStatus(StatusSpec.Poison(0.01f, 6f), hero, 1f);
            Check.That(b.Combatant.HP < beside, "A second ailment on an enemy did not blast the one beside it with Resonance");

            Wear("m_overcharge", ItemSlot.Amulet);
            float area = hero.Stats.Get(StatType.AreaSize), power = hero.Stats.Get(StatType.SkillPower);
            Check.That(!MythicPowers.Overcharged, "Overcharge is charged the moment it is put on");
            yield return Play.Until(() => MythicPowers.Overcharged, 12f, "Overcharge to charge after 10 seconds");
            Check.Near(hero.Stats.Get(StatType.AreaSize) - area, 0.6f, 0.01f, "area size while overcharged");
            Check.Near(hero.Stats.Get(StatType.SkillPower) - power, 0.6f, 0.01f, "skill power while overcharged");
            MythicPowers.OnSpellCast(Hero, HeroController.WeaponSlot == 0 ? 1 : 0, Vector2.right, Spot + Vector2.right * 2f);
            Check.That(!MythicPowers.Overcharged, "Casting a spell did not use the Overcharge");
            yield return Play.Wait(1f);
            Check.Near(hero.Stats.Get(StatType.AreaSize), area, 0.01f, "area size once the overcharged spell is done");
            yield return Done();
        }

        [TwfCheck("Mythics/Ring: Hunter's Mark, Vein of Fortune")]
        static IEnumerator Ring()
        {
            yield return Yard();
            var a = Dummy(Spot + Vector2.right * 2f);
            var b = Dummy(Spot + Vector2.right * 5f);
            yield return null;

            Wear("m_huntersmark", ItemSlot.Ring);
            Strike(a.Combatant, 10f);
            Check.That(a.Combatant.IsExposed, "The first enemy hit was not marked");
            Strike(b.Combatant, 10f);
            Check.That(!b.Combatant.IsExposed, "A second enemy was marked while the first mark still stood");
            float marked = Strike(a.Combatant, 100f, false), plain = Strike(b.Combatant, 100f, false);
            Check.Near(marked / plain, 1.2f, 0.02f, "damage to the marked enemy against an unmarked one");

            Check.That(MythicPowers.ExtraLoot == 0 && MythicPowers.GoldMult == 1f, "Loot is boosted without Vein of Fortune");
            Wear("m_fortune", ItemSlot.Ring);
            Check.That(MythicPowers.ExtraLoot == 1, "Vein of Fortune adds no item");
            Check.Near(MythicPowers.GoldMult, 1.25f, 0.001f, "gold with Vein of Fortune");
            yield return Done();
        }

        [TwfCheck("Mythics/Off hand: Orbiting Shards, Forbidden Page, Riposte Wall, Counterstrike, Keystone")]
        static IEnumerator OffHands()
        {
            yield return Yard();
            var hero = Hero.Combatant;

            // Orbiting Shards: an enemy standing on the shards' circle is struck without the hero doing anything.
            var a = Dummy(Spot + Vector2.right * 2.1f);
            yield return null;
            Wear("m_shards", ItemSlot.OffHand, OffHandKind.Orb);
            float hp = a.Combatant.HP;
            yield return Play.Until(() => a == null || a.Combatant.HP < hp, 6f, "an orbiting shard to strike an enemy standing on its circle");

            // Forbidden Page.
            float power = hero.Stats.Get(StatType.SkillPower);
            MythicPowers.Refresh(Hero);
            Wear("m_forbiddenpage", ItemSlot.OffHand, OffHandKind.Tome);
            Check.Near(hero.Stats.Get(StatType.SkillPower) - (power - 0f), 0.35f, 0.36f, "skill power with Forbidden Page");
            hero.SetHP(hero.MaxHP);
            float life = hero.HP;
            MythicPowers.OnSpellCast(Hero, HeroController.WeaponSlot == 0 ? 1 : 0, Vector2.right, Spot);
            Check.Near(hero.HP / life, 0.95f, 0.005f, "life left after one spell with Forbidden Page");
            hero.SetHP(1f);
            MythicPowers.OnSpellCast(Hero, HeroController.WeaponSlot == 0 ? 1 : 0, Vector2.right, Spot);
            Check.That(hero.HP >= 1f && !hero.IsDead, "Forbidden Page took the hero's last life");
            hero.SetHP(hero.MaxHP);

            // Riposte Wall and Counterstrike answer a hit taken, so the hero has to be able to take one.
            hero.Invulnerable = false;
            var foe = Dummy(Spot + Vector2.left * 1.5f);
            yield return null;
            Wear("m_ripostewall", ItemSlot.OffHand, OffHandKind.Shield);
            hero.Invulnerable = false; hero.TakeDamage(new DamageInfo { source = foe.Combatant, amount = 1f });
            Check.That(foe.Combatant.IsStunned, "An enemy that struck the hero was not stunned by Riposte Wall");
            yield return Play.Until(() => !foe.Combatant.IsStunned, 3f, "the riposte stun to end");
            hero.Invulnerable = false; hero.TakeDamage(new DamageInfo { source = foe.Combatant, amount = 1f });
            Check.That(!foe.Combatant.IsStunned, "Riposte Wall stunned the same enemy twice within 5 seconds");

            Wear("m_counterstrike", ItemSlot.OffHand, OffHandKind.ParryDagger);
            float crit = hero.Stats.Get(StatType.CritChance);
            hero.Invulnerable = false; hero.TakeDamage(new DamageInfo { source = foe.Combatant, amount = 1f });
            Check.AtLeast(hero.Stats.Get(StatType.CritChance), 0.9f, "crit chance right after being hit with Counterstrike (the stat is capped below 100%)");
            float foeLife = foe.Combatant.HP;
            float dealt = Strike(foe.Combatant, 50f);
            yield return Play.Wait(0.4f);
            Check.Near(foeLife - foe.Combatant.HP, dealt * 2f, dealt * 0.05f, "life taken by a counterstrike (the hit and its second strike)");
            Check.Near(hero.Stats.Get(StatType.CritChance), crit, 0.01f, "crit chance once the counterstrike is spent");
            hero.Invulnerable = true;
            hero.SetHP(hero.MaxHP);

            // Keystone: one more rank on every other mythic's passive bonus, never on Hydra or Flurry.
            Wear("m_hunger", ItemSlot.Gloves, 0, "p_mastery", 2);
            Wear("m_frostbound", ItemSlot.Boots, 0, "p_hydra", 1);
            Check.That(MythicPowers.BonusRanks("p_mastery") == 2, $"Without Keystone a +2 mythic gives {MythicPowers.BonusRanks("p_mastery")} ranks");
            Wear("m_keystone", ItemSlot.OffHand, OffHandKind.Orb, "p_expanse", 1);
            Check.That(MythicPowers.BonusRanks("p_mastery") == 3, $"With Keystone a +2 mythic gives {MythicPowers.BonusRanks("p_mastery")} ranks, expected 3");
            Check.That(MythicPowers.BonusRanks("p_hydra") == 1, $"Keystone raised Hydra to {MythicPowers.BonusRanks("p_hydra")}");
            Check.That(MythicPowers.BonusRanks("p_expanse") == 1, $"Keystone raised its own passive bonus to {MythicPowers.BonusRanks("p_expanse")}");
            yield return Done();
        }

        [TwfCheck("Mythics/Quiver: Ricochet carries a killing shot on")]
        static IEnumerator Ricochet()
        {
            yield return Yard();
            // A bow in the main hand: Ricochet only follows shots.
            Profile.Data.equipment.RemoveAll(e => e != null && e.slot == ItemSlot.Weapon);
            Profile.Data.equipment.Add(new ItemInstance
            {
                uid = System.Guid.NewGuid().ToString(), slot = ItemSlot.Weapon, weaponType = (int)WeaponType.Bow, rarity = Rarity.Rare, itemLevel = 20,
                baseName = "Test Mythic", name = "Test Mythic", implicitStat = StatType.Damage, implicitValue = 30f,
            });
            Hero.RebuildStats();
            Wear("m_ricochet", ItemSlot.OffHand, OffHandKind.Quiver);
            yield return null;
            Check.That(Hero.Weapon.ranged, "The test hero's bow does not count as a ranged weapon");
            var a = Dummy(Spot + Vector2.right * 3f, 3);
            var b = Dummy(Spot + Vector2.right * 6f, 3);
            yield return null;
            float hp = b.Combatant.HP;
            Strike(a.Combatant, 999999f);
            yield return Play.Wait(0.5f);
            Check.That(a == null || a.Combatant.IsDead, "The first target survived the shot");
            Check.That(b.Combatant.HP < hp, "The shot that killed did not bounce on to the next enemy");
            Profile.Data.equipment.RemoveAll(e => e != null && e.name == "Test Mythic");
            Hero.RebuildStats();
            yield return Done();
        }
    }
}
