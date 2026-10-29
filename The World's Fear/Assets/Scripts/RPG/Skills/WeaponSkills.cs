using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Signature skills tied to a weapon type (Barrage needs a bow, Whirlwind a sword...). Bought from the
    // Weaponmaster and attached per weapon type; the attached skill of the weapon in your hands sits on
    // the fifth skill button. Ids are stable: new skills are appended.
    public enum WeaponSkillId
    {
        Whirlwind, BladeRush,          // Sword
        CleavingStorm, AxeThrow,       // Axe
        Earthquake, Shockwave,         // Hammer
        FanOfKnives, ShadowStep,       // Daggers
        Impale, Vault,                 // Spear
        Barrage, RainOfArrows,         // Bow
        ExplosiveBolt, PiercingVolley, // Crossbow
        Meteor, ArcaneOrb,             // Staff
        MagicMissiles, ChainSpark,     // Wand
        HundredFists, Uppercut,        // Fists
    }

    public static class WeaponSkills
    {
        public const int Count = 20;
        static readonly Dictionary<WeaponSkillId, SkillDefinition> built = new Dictionary<WeaponSkillId, SkillDefinition>();

        public static WeaponType WeaponOf(WeaponSkillId id) => (WeaponType)((int)id / 2);

        public static WeaponSkillId[] For(WeaponType w) => new[] { (WeaponSkillId)((int)w * 2), (WeaponSkillId)((int)w * 2 + 1) };

        public static string Name(WeaponSkillId id)
        {
            switch (id)
            {
                case WeaponSkillId.BladeRush: return "Blade Rush";
                case WeaponSkillId.CleavingStorm: return "Cleaving Storm";
                case WeaponSkillId.AxeThrow: return "Axe Throw";
                case WeaponSkillId.FanOfKnives: return "Fan of Knives";
                case WeaponSkillId.ShadowStep: return "Shadow Step";
                case WeaponSkillId.RainOfArrows: return "Rain of Arrows";
                case WeaponSkillId.ExplosiveBolt: return "Explosive Bolt";
                case WeaponSkillId.PiercingVolley: return "Piercing Volley";
                case WeaponSkillId.ArcaneOrb: return "Arcane Orb";
                case WeaponSkillId.MagicMissiles: return "Magic Missiles";
                case WeaponSkillId.ChainSpark: return "Chain Spark";
                case WeaponSkillId.HundredFists: return "Hundred Fists";
                default: return id.ToString();
            }
        }

        public static string Describe(WeaponSkillId id)
        {
            switch (id)
            {
                case WeaponSkillId.Whirlwind: return "Spin with your sword, hitting everything around you five times.";
                case WeaponSkillId.BladeRush: return "Dash forward, slashing through every enemy in the way.";
                case WeaponSkillId.CleavingStorm: return "A huge cleave all around you that knocks enemies back.";
                case WeaponSkillId.AxeThrow: return "Hurl a spinning axe that passes through every enemy.";
                case WeaponSkillId.Earthquake: return "Pound the ground: three quakes around you that stun.";
                case WeaponSkillId.Shockwave: return "Slam a narrow shockwave forward that stuns.";
                case WeaponSkillId.FanOfKnives: return "Throw poisoned knives in every direction.";
                case WeaponSkillId.ShadowStep: return "Vanish and reappear ahead, backstabbing the nearest enemy.";
                case WeaponSkillId.Impale: return "A lightning-fast thrust that pierces a long line.";
                case WeaponSkillId.Vault: return "Vault forward on your spear and crash down on landing.";
                case WeaponSkillId.Barrage: return "Loose five rapid volleys of three arrows.";
                case WeaponSkillId.RainOfArrows: return "Arrows rain on the target area again and again.";
                case WeaponSkillId.ExplosiveBolt: return "A heavy bolt that explodes on impact.";
                case WeaponSkillId.PiercingVolley: return "Three bolts in a row that pierce everything.";
                case WeaponSkillId.Meteor: return "Call a burning meteor down on the target spot.";
                case WeaponSkillId.ArcaneOrb: return "A slow, huge orb that grinds through every enemy.";
                case WeaponSkillId.MagicMissiles: return "Six homing missiles in quick succession.";
                case WeaponSkillId.ChainSpark: return "A spark that jumps between up to six enemies.";
                case WeaponSkillId.HundredFists: return "A flurry of eight rapid punches in front of you.";
                default: return "A rising punch that stuns and launches an enemy.";
            }
        }

        public static string Icon(WeaponSkillId id)
        {
            switch (id)
            {
                case WeaponSkillId.Whirlwind: return "spiral";
                case WeaponSkillId.BladeRush: return "dash";
                case WeaponSkillId.CleavingStorm: return "slash";
                case WeaponSkillId.AxeThrow: return "spiral";
                case WeaponSkillId.Earthquake: return "nova";
                case WeaponSkillId.Shockwave: return "slash";
                case WeaponSkillId.FanOfKnives: return "poison";
                case WeaponSkillId.ShadowStep: return "moon";
                case WeaponSkillId.Impale: return "sword";
                case WeaponSkillId.Vault: return "leap";
                case WeaponSkillId.Barrage: return "bow";
                case WeaponSkillId.RainOfArrows: return "rain";
                case WeaponSkillId.ExplosiveBolt: return "meteor";
                case WeaponSkillId.PiercingVolley: return "bow";
                case WeaponSkillId.Meteor: return "meteor";
                case WeaponSkillId.ArcaneOrb: return "arcane";
                case WeaponSkillId.MagicMissiles: return "staff";
                case WeaponSkillId.ChainSpark: return "bolt";
                case WeaponSkillId.HundredFists: return "fist";
                default: return "up";
            }
        }

        public static Color ColorOf(WeaponSkillId id)
        {
            switch (WeaponOf(id))
            {
                case WeaponType.Staff: return new Color(1f, 0.5f, 0.25f);
                case WeaponType.Wand: return new Color(0.7f, 0.55f, 1f);
                case WeaponType.Daggers: return new Color(0.55f, 0.95f, 0.35f);
                case WeaponType.Bow: case WeaponType.Crossbow: return new Color(0.75f, 0.9f, 0.55f);
                default: return new Color(0.92f, 0.88f, 0.8f);
            }
        }

        public static int Price(WeaponSkillId id) => 250 + Profile.Data.level * 20 + ((int)id % 2) * 100;

        // ---------- Ownership and attaching ----------

        public static bool Owned(WeaponSkillId id) => Profile.Data.weaponSkillsOwned.Contains((int)id);

        public static WeaponSkillId? Attached(WeaponType w)
        {
            var a = Profile.Data.weaponSkillAttached;
            int i = (int)w;
            return a != null && i < a.Length && a[i] > 0 ? (WeaponSkillId)(a[i] - 1) : (WeaponSkillId?)null;
        }

        public static bool Buy(WeaponSkillId id)
        {
            if (Owned(id) || Profile.Data.gold < Price(id)) return false;
            Profile.AddGold(-Price(id));
            Profile.Data.weaponSkillsOwned.Add((int)id);
            if (Attached(WeaponOf(id)) == null) Attach(id);
            Profile.Save();
            return true;
        }

        public static void Attach(WeaponSkillId id)
        {
            if (!Owned(id)) return;
            Profile.Data.weaponSkillAttached[(int)WeaponOf(id)] = (int)id + 1;
            Profile.NotifySkills();
            Profile.Save();
        }

        // The skill for the fifth button: whatever is attached to the weapon in your hands.
        public static SkillDefinition Current()
        {
            var id = Attached(Weapons.Equipped);
            return id.HasValue ? Get(id.Value) : null;
        }

        // ---------- Building ----------

        public static SkillDefinition Get(WeaponSkillId id)
        {
            if (built.TryGetValue(id, out var s) && s != null) return s;
            s = Build(id);
            built[id] = s;
            return s;
        }

        static SkillDefinition Build(WeaponSkillId id)
        {
            var s = ScriptableObject.CreateInstance<SkillDefinition>();
            s.hideFlags = HideFlags.DontSave;
            s.id = "weapon:" + id;
            s.displayName = Name(id);
            s.description = Describe(id);
            s.maxRank = 1;
            s.powerPerRank = 0f;
            s.cooldownPerRank = 0f;
            s.color = ColorOf(id);
            s.iconKey = Icon(id);
            s.glyph = Name(id).Substring(0, Mathf.Min(4, Name(id).Length)).ToUpperInvariant();
            s.castLock = 0.1f;
            var col = s.color;
            var hit = new HitExtras { color = col };
            var fx = new List<SkillEffect>();
            switch (id)
            {
                case WeaponSkillId.Whirlwind:
                    Self(s, 8f, 2.4f);
                    fx.Add(new CraftedArea { atCaster = true, followCaster = true, radius = 2.4f, delay = 0.05f, ticks = 5, interval = 0.25f, damageMult = 0.75f, hit = hit });
                    break;
                case WeaponSkillId.BladeRush:
                    Dash(s, 6f, 6f);
                    fx.Add(new DashEffect { distance = 6f, duration = 0.22f, damageMult = 2f, hitRadius = 1.4f, color = col, invulnerable = true });
                    break;
                case WeaponSkillId.CleavingStorm:
                    Self(s, 7f, 3.4f);
                    fx.Add(new CraftedWave { radius = 3.4f, angle = 360f, damageMult = 2.2f, hit = new HitExtras { color = col, knockback = 3f } });
                    break;
                case WeaponSkillId.AxeThrow:
                    Shot(s, 9f, 5f);
                    fx.Add(new CraftedProjectile { speed = 14f, range = 9f, size = 0.9f, hitRadius = 0.6f, damageMult = 2f, pierce = -1, hit = hit });
                    break;
                case WeaponSkillId.Earthquake:
                    Self(s, 10f, 4.5f);
                    fx.Add(new RepeatEffect { times = 3, interval = 0.5f, inner = new List<SkillEffect> {
                        new CraftedArea { atCaster = true, radius = 4.5f, delay = 0.05f, damageMult = 1.1f, hit = new HitExtras { color = new Color(0.8f, 0.65f, 0.45f), status = StatusSpec.Stun(0.5f) } } } });
                    break;
                case WeaponSkillId.Shockwave:
                    Shot(s, 7f, 7f);
                    fx.Add(new CraftedWave { radius = 7f, angle = 50f, damageMult = 2.5f, hit = new HitExtras { color = col, status = StatusSpec.Stun(1f), knockback = 2f } });
                    break;
                case WeaponSkillId.FanOfKnives:
                    Self(s, 6f, 7f);
                    fx.Add(new CraftedProjectile { count = 12, spread = 360f, speed = 16f, range = 7f, size = 0.45f, hitRadius = 0.35f, damageMult = 0.9f,
                        hit = new HitExtras { color = col, status = StatusSpec.Poison(0.4f, 3f) } });
                    break;
                case WeaponSkillId.ShadowStep:
                    Dash(s, 6f, 7f);
                    fx.Add(new DashEffect { distance = 6f, blink = true, invulnerable = true, color = new Color(0.55f, 0.35f, 0.75f) });
                    fx.Add(new CraftedStrike { range = 8f, targets = 1, damageMult = 3.2f, hit = new HitExtras { color = new Color(0.55f, 0.35f, 0.75f), expose = 0.2f } });
                    break;
                case WeaponSkillId.Impale:
                    Shot(s, 11f, 6f);
                    fx.Add(new CraftedProjectile { speed = 34f, range = 11f, size = 0.5f, hitRadius = 0.5f, damageMult = 2.5f, pierce = -1, hit = new HitExtras { color = col, knockback = 2f } });
                    break;
                case WeaponSkillId.Vault:
                    Dash(s, 7f, 7f);
                    fx.Add(new DashEffect { distance = 7f, duration = 0.35f, invulnerable = true, color = col });
                    fx.Add(new CraftedArea { atCaster = true, radius = 2.4f, delay = 0.35f, damageMult = 2f, hit = new HitExtras { color = col, knockback = 2.5f } });
                    break;
                case WeaponSkillId.Barrage:
                    Shot(s, 10f, 7f);
                    fx.Add(new RepeatEffect { times = 5, interval = 0.12f, inner = new List<SkillEffect> {
                        new CraftedProjectile { count = 3, spread = 18f, speed = 22f, range = 10f, size = 0.45f, hitRadius = 0.35f, damageMult = 0.6f, hit = hit } } });
                    break;
                case WeaponSkillId.RainOfArrows:
                    Ground(s, 9f, 9f, 3.2f);
                    fx.Add(new CraftedArea { radius = 3.2f, delay = 0.4f, ticks = 6, interval = 0.3f, damageMult = 0.6f, hit = hit });
                    break;
                case WeaponSkillId.ExplosiveBolt:
                    Shot(s, 10f, 7f);
                    fx.Add(new CraftedProjectile { speed = 20f, range = 10f, size = 0.7f, hitRadius = 0.45f, damageMult = 2.6f, explodeRadius = 2.4f,
                        hit = new HitExtras { color = new Color(1f, 0.55f, 0.2f), status = StatusSpec.Burn(0.3f, 2f) } });
                    break;
                case WeaponSkillId.PiercingVolley:
                    Shot(s, 12f, 8f);
                    fx.Add(new RepeatEffect { times = 3, interval = 0.2f, inner = new List<SkillEffect> {
                        new CraftedProjectile { speed = 30f, range = 12f, size = 0.5f, hitRadius = 0.45f, damageMult = 1.2f, pierce = -1, hit = hit } } });
                    break;
                case WeaponSkillId.Meteor:
                    Ground(s, 9f, 10f, 3f);
                    fx.Add(new CraftedArea { radius = 3f, delay = 0.9f, damageMult = 4f, hit = new HitExtras { color = col, status = StatusSpec.Burn(0.4f, 3f), knockback = 2f } });
                    break;
                case WeaponSkillId.ArcaneOrb:
                    Shot(s, 12f, 7f);
                    fx.Add(new CraftedProjectile { speed = 6f, range = 12f, size = 1.4f, hitRadius = 1f, damageMult = 1.7f, pierce = -1, hit = hit });
                    break;
                case WeaponSkillId.MagicMissiles:
                    Shot(s, 10f, 6f);
                    fx.Add(new RepeatEffect { times = 6, interval = 0.1f, inner = new List<SkillEffect> {
                        new CraftedProjectile { speed = 14f, range = 10f, size = 0.45f, hitRadius = 0.4f, damageMult = 0.65f, homing = true, hit = hit } } });
                    break;
                case WeaponSkillId.ChainSpark:
                    Shot(s, 9f, 6f);
                    fx.Add(new CraftedStrike { range = 9f, targets = 1, damageMult = 1.7f,
                        hit = new HitExtras { color = new Color(1f, 0.9f, 0.35f), chain = 5, chainMult = 0.8f, status = StatusSpec.Stun(0.4f, 0.3f) } });
                    break;
                case WeaponSkillId.HundredFists:
                    Shot(s, 2.4f, 7f);
                    fx.Add(new RepeatEffect { times = 8, interval = 0.08f, inner = new List<SkillEffect> {
                        new CraftedWave { radius = 2.4f, angle = 90f, damageMult = 0.45f, hit = hit } } });
                    break;
                default: // Uppercut
                    Shot(s, 2.5f, 6f);
                    fx.Add(new CraftedStrike { range = 2.5f, targets = 1, damageMult = 3.3f, hit = new HitExtras { color = col, status = StatusSpec.Stun(1f), knockback = 3f } });
                    break;
            }
            s.effects = fx;
            return s;
        }

        static void Self(SkillDefinition s, float cd, float radius) { s.targeting = Targeting.Self; s.range = 0f; s.baseCooldown = cd; s.indicatorRadius = radius; }
        static void Shot(SkillDefinition s, float range, float cd) { s.targeting = Targeting.Skillshot; s.range = range; s.baseCooldown = cd; }
        static void Dash(SkillDefinition s, float range, float cd) { s.targeting = Targeting.Dash; s.range = range; s.baseCooldown = cd; s.castLock = 0.2f; }
        static void Ground(SkillDefinition s, float range, float cd, float radius) { s.targeting = Targeting.GroundAoE; s.range = range; s.baseCooldown = cd; s.indicatorRadius = radius; }
    }
}
