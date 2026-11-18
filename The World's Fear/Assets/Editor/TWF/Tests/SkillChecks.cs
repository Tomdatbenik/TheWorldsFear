using System.Collections;
using UnityEngine;

namespace TWF.Tests
{
    // Skills cast in the debug yard (Scenes/TestRange).
    public static class SkillChecks
    {
        static readonly Vector2 Open = new Vector2(0f, -8f);   // open paving south of the stairs

        static IEnumerator Yard()
        {
            yield return Play.Scene("TestRange", 3f);
            foreach (var m in Object.FindObjectsByType<MonsterBrain>()) Object.Destroy(m.gameObject);
            HeroController.Instance.Combatant.Invulnerable = true;
            yield return null;
        }

        // A monster that stands still and does nothing, with plenty of life.
        static MonsterBrain Dummy(Vector2 at)
        {
            MonsterBrain m = null;
            foreach (var def in MobCatalog.All) { m = MonsterBrain.Spawn(MonsterGenerator.CreateMob(def), 40, MonsterRank.Normal, at); break; }
            Check.That(m != null, "No monster to test on");
            m.enabled = false;
            return m;
        }

        static void Vault(Vector2 dir) =>
            HeroController.Instance.CastSkill(WeaponSkills.Get(WeaponSkillId.Vault), 1, dir, Play.HeroAt + dir * 7f);

        [TwfCheck("Skills/Spear Vault crashes down where the hero lands, not where it started")]
        static IEnumerator VaultLandsHit()
        {
            yield return Yard();
            Play.MoveHero(Open);
            yield return Play.Wait(0.3f);
            // One beside the take-off, one beside the landing spot (off the line, so neither is in the way).
            var atStart = Dummy(Open + new Vector2(-1.4f, 0f));
            var atEnd = Dummy(Open + new Vector2(7f, 1.8f));
            yield return null;
            float startHp = atStart.Combatant.HP, endHp = atEnd.Combatant.HP;

            Vault(Vector2.right);
            yield return Play.Wait(1.2f);
            Check.AtLeast(Play.HeroAt.x - Open.x, 6f, "how far the vault carried the hero");
            Check.That(atEnd == null || atEnd.Combatant.HP < endHp, "The monster beside the landing spot was not hit by the crash");
            Check.That(atStart != null && atStart.Combatant.HP >= startHp, "The monster beside the take-off spot was hit: the crash happened where the vault started");
            foreach (var m in Object.FindObjectsByType<MonsterBrain>()) Object.Destroy(m.gameObject);
        }

        [TwfCheck("Skills/Spear Vault stops at a wall and crashes down there")]
        static IEnumerator VaultStopsAtWall()
        {
            yield return Yard();
            // The crenellated wall runs along y 22..24 (x -34..-8).
            var from = new Vector2(-20f, 18.5f);
            Play.MoveHero(from);
            yield return Play.Wait(0.3f);
            var byWall = Dummy(new Vector2(-18.2f, 21.2f));
            yield return null;
            float hp = byWall.Combatant.HP;

            Vault(Vector2.up);
            float until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until)
            {
                Check.AtMost(Play.HeroAt.y, 22f, "how far north the vault took the hero into the wall (y)");
                yield return null;
            }
            Check.AtLeast(Play.HeroAt.y - from.y, 1.5f, "how far the hero vaulted before the wall");
            Check.That(byWall == null || byWall.Combatant.HP < hp, "The monster by the wall was not hit by the crash where the hero stopped");
            foreach (var m in Object.FindObjectsByType<MonsterBrain>()) Object.Destroy(m.gameObject);
        }

        [TwfCheck("Skills/Spear Vault up the slope keeps the hero on the ground")]
        static IEnumerator VaultUpSlope()
        {
            yield return Yard();
            // The grass slope climbs north from about (20, 0) to the ledge at (20, 15).
            Play.MoveHero(new Vector2(20f, -1f));
            yield return Play.Wait(0.5f);
            Vault(Vector2.up);
            yield return Play.Wait(1.5f);
            Check.AtLeast(Play.HeroAt.y, 4f, "how far up the slope the vault went (y)");
            Check.Near(HeroController.Instance.transform.position.y, TerrainHeight.At(Play.HeroAt), 0.15f, "hero height after vaulting up the slope");
        }
    }
}
