using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Turns a crafted recipe (Element -> Delivery -> Modifiers) into a runtime SkillDefinition.
    public static class SkillComposer
    {
        struct Element
        {
            public Color color;
            public StatusSpec status;
            public float damage;
            public float knockback;
            public int chain;
            public float heal;      // Holy: share of the caster's life healed per hit
            public float weaken;    // Shadow: enemies hit deal less damage
        }

        // The status an element's hits apply (also used by elemental weapons).
        public static StatusSpec ElementStatus(ElementPart e, int tier = 1) => ElementData(e, tier).status;

        static Element ElementData(ElementPart e, int tier)
        {
            float t = 1f + 0.15f * (tier - 1);
            var el = new Element { color = SkillParts.ElementColor(e), damage = t };
            switch (e)
            {
                case ElementPart.Fire: el.status = StatusSpec.Burn(0.35f * t, 3f); break;
                case ElementPart.Frost: el.status = StatusSpec.Slow(0.4f + 0.1f * (tier - 1), 1.5f + 0.5f * tier); el.damage *= 0.95f; break;
                case ElementPart.Lightning: el.status = StatusSpec.Stun(0.6f, 0.12f * tier); el.chain = 1; break;
                case ElementPart.Poison: el.status = StatusSpec.Poison(0.55f * t, 4f); el.damage *= 0.8f; break;
                case ElementPart.Arcane: el.damage *= 1.3f; break;
                case ElementPart.Holy: el.damage *= 0.85f; el.heal = 0.025f * t; break;
                case ElementPart.Shadow: el.damage *= 0.95f; el.weaken = 0.2f + 0.05f * (tier - 1); break;
                default: el.knockback = 1.5f; el.damage *= 1.1f; break;
            }
            return el;
        }

        public static SkillDefinition Build(CraftedSkill c)
        {
            var s = ScriptableObject.CreateInstance<SkillDefinition>();
            s.hideFlags = HideFlags.DontSave;
            s.id = "craft:" + c.uid;
            s.displayName = c.name;
            s.maxRank = 1;
            s.powerPerRank = 0f;
            s.cooldownPerRank = 0f;
            var el = ElementData(c.element, c.elementTier);
            // Extra elements (dual / triple spells) add their status and effects, and a bit of damage.
            var extras = new List<Element>();
            for (int i = 0; c.extraElements != null && i < c.extraElements.Count; i++)
                extras.Add(ElementData(c.extraElements[i], i < c.extraElementTiers.Count ? c.extraElementTiers[i] : 1));
            foreach (var x in extras)
            {
                el.heal = Mathf.Max(el.heal, x.heal);
                el.weaken = Mathf.Max(el.weaken, x.weaken);
                el.chain = Mathf.Max(el.chain, x.chain);
                el.knockback = Mathf.Max(el.knockback, x.knockback);
                el.damage *= 1.12f;
                el.color = Color.Lerp(el.color, x.color, 0.35f);
            }
            s.color = el.color;
            s.iconKey = SkillParts.Icon(PartKind.Element, (int)c.element);
            s.isUltimate = c.mods.Count >= 3;

            int Tier(ModPart m) => c.ModTier(m);
            bool Has(ModPart m) => c.Has(m);

            float dmg = el.damage * (1f + 0.2f * (c.deliveryTier - 1));
            float cdMult = 1f;
            float area = 1f, sizeMult = 1f, speedMult = 1f;
            float knock = el.knockback;
            if (Has(ModPart.Heavy)) { dmg *= 1.5f + 0.15f * Tier(ModPart.Heavy); cdMult *= 1.35f; speedMult *= 0.8f; knock += 1f; }
            if (Has(ModPart.Swift)) { cdMult *= 0.6f - 0.05f * Tier(ModPart.Swift); dmg *= 0.85f; }
            if (Has(ModPart.Speed)) { speedMult *= 1.5f + 0.15f * Tier(ModPart.Speed); cdMult *= 0.95f; }
            if (Has(ModPart.Wide)) { area *= 1.4f + 0.15f * Tier(ModPart.Wide); sizeMult *= 1.5f; }
            int split = Has(ModPart.Split) ? Tier(ModPart.Split) : 0;
            int chain = el.chain + (Has(ModPart.Chain) ? 2 + Tier(ModPart.Chain) : 0);
            bool lingering = Has(ModPart.Lingering);
            bool homing = Has(ModPart.Homing);
            s.castLock = Has(ModPart.Speed) ? 0.02f : 0.12f;

            var effects = new List<SkillEffect>();
            var hit = new HitExtras { chain = chain, chainMult = 0.6f, lingering = lingering, status = el.status, color = el.color, knockback = knock,
                heal = el.heal, weaken = el.weaken, elements = Elements.Mask(c.AllElements) };
            foreach (var x in extras) if (x.status.IsSet) hit.extraStatuses.Add(x.status);
            // Debuff modifiers ride along on every hit.
            if (Has(ModPart.Weaken)) hit.weaken = Mathf.Max(hit.weaken, 0.3f + 0.05f * (Tier(ModPart.Weaken) - 1));
            if (Has(ModPart.Expose)) hit.expose = 0.25f + 0.05f * (Tier(ModPart.Expose) - 1);
            // Buff modifiers trigger when you cast.
            var buff = new CraftedBuff
            {
                empower = Has(ModPart.Empower) ? 0.25f + 0.05f * (Tier(ModPart.Empower) - 1) : 0f,
                haste = Has(ModPart.Haste) ? 0.25f + 0.05f * (Tier(ModPart.Haste) - 1) : 0f,
                barrier = Has(ModPart.Barrier) ? 0.15f + 0.03f * (Tier(ModPart.Barrier) - 1) : 0f,
                mend = Has(ModPart.Mend) ? 0.08f + 0.02f * (Tier(ModPart.Mend) - 1) : 0f,
                color = el.color,
            };
            if (buff.Any) effects.Add(buff);
            switch (c.delivery)
            {
                case DeliveryPart.Forward:
                    s.targeting = Targeting.Skillshot; s.range = 9f; s.baseCooldown = 4.5f;
                    effects.Add(new CraftedProjectile { count = 1 + split * 2, spread = 25f + 10f * split, speed = 15f * speedMult, range = 9f, size = 0.55f * sizeMult,
                        hitRadius = 0.35f * sizeMult, damageMult = 1.8f * dmg, homing = homing, hit = hit });
                    break;
                case DeliveryPart.Pierce:
                    s.targeting = Targeting.Skillshot; s.range = 10f; s.baseCooldown = 6f;
                    effects.Add(new CraftedProjectile { count = 1 + split * 2, spread = 20f + 8f * split, speed = 32f * speedMult, range = 10f, size = 0.45f * sizeMult,
                        hitRadius = 0.45f * sizeMult, damageMult = 1.55f * dmg, pierce = -1, homing = homing, hit = hit });
                    break;
                case DeliveryPart.Single:
                    s.targeting = Targeting.Skillshot; s.range = 8f; s.baseCooldown = 4f;
                    effects.Add(new CraftedStrike { range = 8f, targets = 1 + split, damageMult = 2.4f * dmg, hit = hit });
                    break;
                case DeliveryPart.Explosion:
                    s.targeting = Targeting.GroundAoE; s.range = 8f; s.baseCooldown = 6.5f; s.indicatorRadius = 2.3f * area;
                    effects.Add(new CraftedArea { radius = 2.3f * area, delay = Has(ModPart.Speed) ? 0.15f : 0.45f, ticks = lingering ? 4 : 1, interval = 0.45f,
                        damageMult = (lingering ? 0.9f : 2.4f) * dmg, extraBlasts = split * 2, hit = hit });
                    break;
                case DeliveryPart.Nova:
                    s.targeting = Targeting.Self; s.range = 0f; s.baseCooldown = 8f; s.indicatorRadius = 3f * area;
                    effects.Add(new CraftedArea { atCaster = true, radius = 3f * area, delay = 0.05f, ticks = lingering ? 4 : 1, interval = 0.45f,
                        damageMult = (lingering ? 0.7f : 1.7f) * dmg, extraBlasts = split * 2, hit = hit, followCaster = lingering });
                    break;
                case DeliveryPart.Wave:
                    s.targeting = Targeting.Skillshot; s.range = 3f; s.baseCooldown = 5f;
                    effects.Add(new CraftedWave { radius = 3f * area, angle = 140f + 40f * split, damageMult = 2f * dmg, hit = hit });
                    break;
                case DeliveryPart.Blink:
                    s.targeting = Targeting.Dash; s.range = 6f; s.baseCooldown = 7f; s.castLock = 0.05f;
                    effects.Add(new DashEffect { distance = 6f, blink = true, invulnerable = true, color = el.color });
                    effects.Add(new CraftedArea { atCaster = true, radius = 2f * area, delay = 0.05f, ticks = lingering ? 4 : 1, interval = 0.45f,
                        damageMult = (lingering ? 0.6f : 1.6f) * dmg, extraBlasts = split * 2, hit = hit });
                    break;
                case DeliveryPart.Self:
                    s.targeting = Targeting.Self; s.range = 0f; s.baseCooldown = 14f; s.indicatorRadius = 1.2f; s.castLock = 0.05f;
                    effects.Add(new CraftedSelf { element = c.element, strength = (1f + 0.2f * (c.elementTier - 1)) * (1f + 0.15f * (c.deliveryTier - 1)), color = el.color });
                    break;
                case DeliveryPart.Aura:
                    s.targeting = Targeting.Self; s.range = 0f; s.baseCooldown = 12f; s.indicatorRadius = 2.6f * area; s.castLock = 0.05f;
                    effects.Add(new CraftedAura { radius = 2.6f * area, pulses = lingering ? 12 : 8, interval = Has(ModPart.Speed) ? 0.45f : 0.6f,
                        damageMult = 0.45f * dmg, extraTargets = split, hit = hit });
                    break;
                case DeliveryPart.Hex:
                    s.targeting = Targeting.Skillshot; s.range = 8f; s.baseCooldown = 7f;
                    hit.expose = Mathf.Max(hit.expose, 0.35f + 0.05f * (c.deliveryTier - 1));
                    effects.Add(new CraftedHex { range = 8f, targets = 1 + split, damageMult = 0.9f * dmg, duration = 6f, hit = hit });
                    break;
                case DeliveryPart.Whirlwind:
                    s.targeting = Targeting.Self; s.range = 0f; s.baseCooldown = 8f; s.indicatorRadius = 2.4f * area;
                    effects.Add(new CraftedArea { atCaster = true, followCaster = true, radius = 2.4f * area, delay = 0.05f, ticks = lingering ? 7 : 5, interval = 0.25f,
                        damageMult = 0.7f * dmg, extraBlasts = split * 2, hit = hit });
                    break;
                case DeliveryPart.AxeThrow:
                    s.targeting = Targeting.Skillshot; s.range = 9f; s.baseCooldown = 5f;
                    effects.Add(new CraftedProjectile { count = 1 + split * 2, spread = 20f, speed = 14f * speedMult, range = 9f, size = 0.9f * sizeMult, hitRadius = 0.6f * sizeMult,
                        damageMult = 2f * dmg, pierce = -1, homing = homing, hit = hit });
                    break;
                case DeliveryPart.Quake:
                    s.targeting = Targeting.Self; s.range = 0f; s.baseCooldown = 10f; s.indicatorRadius = 4.5f * area;
                    effects.Add(new RepeatEffect { times = 3, interval = 0.5f, inner = new List<SkillEffect> {
                        new CraftedArea { atCaster = true, radius = 4.5f * area, delay = 0.05f, ticks = lingering ? 2 : 1, interval = 0.4f, damageMult = 1.1f * dmg, extraBlasts = split, hit = hit } } });
                    break;
                case DeliveryPart.KnifeFan:
                    s.targeting = Targeting.Self; s.range = 0f; s.baseCooldown = 6f; s.indicatorRadius = 7f;
                    effects.Add(new CraftedProjectile { count = 10 + split * 4, spread = 360f, speed = 16f * speedMult, range = 7f, size = 0.45f * sizeMult, hitRadius = 0.35f * sizeMult,
                        damageMult = 0.8f * dmg, homing = homing, hit = hit });
                    break;
                case DeliveryPart.SpearThrow:
                    s.targeting = Targeting.Skillshot; s.range = 11f; s.baseCooldown = 6f;
                    effects.Add(new CraftedProjectile { count = 1 + split * 2, spread = 16f, speed = 30f * speedMult, range = 11f, size = 0.6f * sizeMult, hitRadius = 0.5f * sizeMult,
                        damageMult = 2.4f * dmg, pierce = -1, homing = homing, hit = hit });
                    break;
                case DeliveryPart.Barrage:
                    s.targeting = Targeting.Skillshot; s.range = 10f; s.baseCooldown = 7f;
                    effects.Add(new RepeatEffect { times = 5, interval = 0.12f, inner = new List<SkillEffect> {
                        new CraftedProjectile { count = 3 + split, spread = 18f, speed = 22f * speedMult, range = 10f, size = 0.45f * sizeMult, hitRadius = 0.35f * sizeMult,
                            damageMult = 0.6f * dmg, homing = homing, hit = hit } } });
                    break;
                case DeliveryPart.BoltVolley:
                    s.targeting = Targeting.Skillshot; s.range = 12f; s.baseCooldown = 8f;
                    effects.Add(new RepeatEffect { times = 3, interval = 0.2f, inner = new List<SkillEffect> {
                        new CraftedProjectile { count = 1 + split, spread = 12f, speed = 30f * speedMult, range = 12f, size = 0.5f * sizeMult, hitRadius = 0.45f * sizeMult,
                            damageMult = 1.2f * dmg, pierce = -1, homing = homing, hit = hit } } });
                    break;
                case DeliveryPart.Meteor:
                    s.targeting = Targeting.GroundAoE; s.range = 9f; s.baseCooldown = 10f; s.indicatorRadius = 3f * area;
                    effects.Add(new CraftedArea { radius = 3f * area, delay = Has(ModPart.Speed) ? 0.5f : 0.9f, ticks = lingering ? 4 : 1, interval = 0.45f,
                        damageMult = (lingering ? 1.5f : 4f) * dmg, extraBlasts = split * 2, hit = hit });
                    break;
                case DeliveryPart.Missiles:
                    s.targeting = Targeting.Skillshot; s.range = 10f; s.baseCooldown = 6f;
                    effects.Add(new RepeatEffect { times = 6 + split * 2, interval = 0.1f, inner = new List<SkillEffect> {
                        new CraftedProjectile { speed = 14f * speedMult, range = 10f, size = 0.45f * sizeMult, hitRadius = 0.4f * sizeMult, damageMult = 0.65f * dmg, homing = true, hit = hit } } });
                    break;
                case DeliveryPart.Flurry:
                    s.targeting = Targeting.Skillshot; s.range = 2.4f; s.baseCooldown = 7f;
                    effects.Add(new RepeatEffect { times = 8, interval = 0.08f, inner = new List<SkillEffect> {
                        new CraftedWave { radius = 2.4f * area, angle = 90f + 30f * split, damageMult = 0.45f * dmg, hit = hit } } });
                    break;
                default: // Dash
                    s.targeting = Targeting.Dash; s.range = 6f; s.baseCooldown = 6f; s.castLock = 0.28f;
                    effects.Add(new DashEffect { distance = 6f, duration = 0.28f / speedMult, damageMult = 1.4f * dmg, hitRadius = 1.3f * area,
                        status = el.status, color = el.color, invulnerable = true });
                    if (lingering || split > 0)
                        effects.Add(new CraftedArea { atCaster = true, radius = 1.8f * area, delay = 0.3f, ticks = lingering ? 4 : 1, interval = 0.45f,
                            damageMult = 0.8f * dmg, extraBlasts = split, hit = hit });
                    break;
            }

            s.baseCooldown *= cdMult;
            if (Has(ModPart.Echo))
            {
                int times = 2 + (Tier(ModPart.Echo) >= 3 ? 1 : 0);
                s.effects = new List<SkillEffect> { new RepeatEffect { times = times, interval = 0.5f, inner = effects } };
            }
            else s.effects = effects;

            s.glyph = c.name.Length > 4 ? c.name.Substring(0, 4).ToUpperInvariant() : c.name.ToUpperInvariant();
            s.description = $"{SkillParts.Describe(PartKind.Delivery, (int)c.delivery)} {SkillParts.Describe(PartKind.Element, (int)c.element)}";
            foreach (var m in c.mods) s.description += " " + SkillParts.Describe(PartKind.Modifier, (int)m);
            return s;
        }
    }

    // Shared "on hit" extras for crafted skills: chains and lingering zones.
    [Serializable]
    public class HitExtras
    {
        public int chain;
        public float chainMult = 0.6f;
        public bool lingering;
        public StatusSpec status;
        public Color color = Color.white;
        public float knockback;
        public float heal;            // share of the caster's life healed per hit (Holy)
        public float weaken;          // enemies hit deal this much less damage
        public float expose;          // enemies hit take this much more damage
        public float debuffDuration = 4f;
        public int elements;                                           // Elements.Mask
        public List<StatusSpec> extraStatuses = new List<StatusSpec>(); // from extra elements

        // Heals and debuffs, applied to every enemy hit.
        public void Afflict(SkillContext ctx, Combatant target)
        {
            if (target == null || ctx.caster == null) return;
            float dur = debuffDuration * ctx.Duration;
            if (weaken > 0f) target.Weaken(weaken, dur);
            if (expose > 0f) target.Expose(expose, dur);
            if (heal > 0f) ctx.caster.Heal(ctx.caster.MaxHP * heal);
            float hitDamage = ctx.caster.Stats.Get(StatType.Damage) * ctx.power;
            foreach (var st in extraStatuses) target.ApplyStatus(ctx.Scale(st), ctx.caster, hitDamage);
        }

        public void OnHit(SkillContext ctx, Combatant target)
        {
            if (target == null) return;
            Afflict(ctx, target);
            if (chain > 0) Chain(ctx, target, chain);
            if (lingering)
                AreaEffect.Spawn(ctx.caster, target.transform.position, new AreaSpec
                {
                    radius = 1.3f * ctx.Area, delay = 0.1f, ticks = 4, interval = 0.5f, damageMult = 0.25f * ctx.power,
                    status = ctx.Scale(status), color = color, elements = elements,
                });
        }

        // Jumps from target to target, drawing a quick arc.
        void Chain(SkillContext ctx, Combatant from, int jumps)
        {
            var hitSet = new HashSet<Combatant> { from };
            var current = from;
            for (int i = 0; i < jumps; i++)
            {
                Combatant next = null;
                float best = 4.5f;
                foreach (var c in Combat.HostilesInRadius(ctx.caster.team, current.transform.position, 4.5f))
                {
                    if (hitSet.Contains(c)) continue;
                    float d = Vector2.Distance(c.transform.position, current.transform.position);
                    if (d < best) { best = d; next = c; }
                }
                if (next == null) break;
                hitSet.Add(next);
                Vector2 a = current.transform.position, b = next.transform.position;
                var line = LineTelegraph.Show(a, (b - a).normalized, Vector2.Distance(a, b), 0.18f, color, 0.15f);
                line.transform.position += Vector3.back * 0.6f;
                Combat.Hit(ctx.caster, next, chainMult * ctx.power, ctx.Scale(status), false, 0f, -1f, elements);
                current = next;
            }
        }
    }

    [Serializable]
    public class CraftedProjectile : SkillEffect
    {
        public int count = 1;
        public float spread;
        public float speed = 15f, range = 9f, size = 0.5f, hitRadius = 0.35f, damageMult = 1.5f;
        public int pierce;
        public bool homing;
        public float explodeRadius;
        public HitExtras hit = new HitExtras();

        public override void Apply(SkillContext ctx)
        {
            int total = count + ctx.ExtraProjectiles;
            float fan = Mathf.Max(spread, total > 1 ? 12f * (total - 1) : 0f) * ctx.Spread;
            float baseAngle = Mathf.Atan2(ctx.direction.y, ctx.direction.x) * Mathf.Rad2Deg;
            var c = ctx;
            for (int i = 0; i < total; i++)
            {
                float a = total == 1 ? baseAngle : baseAngle - fan / 2f + fan * i / (total - 1);
                var d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                Combatant target = homing ? Combat.NearestHostile(ctx.caster.team, ctx.origin + d * range * 0.5f, range) : null;
                var spec = new ProjectileSpec
                {
                    speed = speed * ctx.ProjectileSpeed, range = range * ctx.Range, hitRadius = hitRadius, damageMult = damageMult * ctx.power,
                    pierce = pierce, status = ctx.Scale(hit.status), color = hit.color, size = size, knockback = hit.knockback,
                    homing = homing && target != null, rotateSprite = true, explodeRadius = explodeRadius * ctx.Area, elements = hit.elements,
                    onHit = t => hit.OnHit(c, t),
                };
                Projectile.Fire(ctx.caster, ctx.origin + d * 0.4f, d, spec, target);
            }
        }

        public override string Describe(float power) =>
            $"{(count > 1 ? count + " projectiles" : "A projectile")} for {Pct(damageMult * power)} damage{(pierce < 0 ? ", piercing" : "")}{(homing ? ", homing" : "")}{(hit.chain > 0 ? $", chains {hit.chain}x" : "")}";
    }

    [Serializable]
    public class CraftedStrike : SkillEffect
    {
        public float range = 8f;
        public int targets = 1;
        public float damageMult = 2.4f;
        public HitExtras hit = new HitExtras();

        public override void Apply(SkillContext ctx)
        {
            float r = range * ctx.Range;
            var list = Combat.HostilesInRadius(ctx.caster.team, ctx.origin, r);
            // Prefer enemies in the aimed direction, then closer ones.
            list.Sort((a, b) => Score(ctx, a).CompareTo(Score(ctx, b)));
            for (int i = 0; i < Mathf.Min(targets, list.Count); i++)
            {
                var t = list[i];
                Vector3 p = t.transform.position;
                VFX.Burst(p + Vector3.back * 0.8f, hit.color, 14, 0.8f);
                VFX.Ring(p, hit.color, 1f, 0.25f);
                Combat.Hit(ctx.caster, t, damageMult * ctx.power, ctx.Scale(hit.status), false, hit.knockback, -1f, hit.elements);
                hit.OnHit(ctx, t);
            }
        }

        static float Score(SkillContext ctx, Combatant c)
        {
            Vector2 to = (Vector2)c.transform.position - ctx.origin;
            return to.magnitude * 0.3f + Vector2.Angle(ctx.direction, to) * 0.05f;
        }

        public override string Describe(float power) =>
            $"Strikes {(targets > 1 ? targets + " enemies" : "an enemy")} for {Pct(damageMult * power)} damage";
    }

    [Serializable]
    public class CraftedArea : SkillEffect
    {
        public bool atCaster, followCaster;
        public float radius = 2.3f, delay = 0.4f, interval = 0.45f, damageMult = 2f;
        public int ticks = 1, extraBlasts;
        public HitExtras hit = new HitExtras();

        public override void Apply(SkillContext ctx)
        {
            Vector2 center = atCaster ? (Vector2)ctx.caster.transform.position : ctx.point;
            Spawn(ctx, center, radius * ctx.Area, damageMult);
            // Split: smaller blasts in a ring around the main one.
            for (int i = 0; i < extraBlasts; i++)
            {
                float a = i * Mathf.PI * 2f / extraBlasts;
                Spawn(ctx, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * ctx.Area * 1.4f, radius * ctx.Area * 0.6f, damageMult * 0.6f);
            }
        }

        void Spawn(SkillContext ctx, Vector2 at, float r, float mult)
        {
            var c = ctx;
            AreaEffect.Spawn(ctx.caster, at, new AreaSpec
            {
                radius = r, delay = delay, ticks = ticks, interval = interval, damageMult = mult * ctx.power,
                status = ctx.Scale(hit.status), color = hit.color, knockback = hit.knockback, followOwner = followCaster, elements = hit.elements,
                onHit = t => { hit.Afflict(c, t); if (hit.chain > 0) hit.OnHit(c, t); },
            });
        }

        public override string Describe(float power) =>
            $"Blast{(ticks > 1 ? $" {ticks} times" : "")} for {Pct(damageMult * power)} damage{(extraBlasts > 0 ? $" plus {extraBlasts} smaller blasts" : "")}";
    }

    [Serializable]
    public class CraftedWave : SkillEffect
    {
        public float radius = 3f, angle = 140f, damageMult = 2f;
        public HitExtras hit = new HitExtras();

        public override void Apply(SkillContext ctx)
        {
            float r = radius * ctx.Area;
            VFX.Slash(ctx.caster.transform.position, ctx.direction, r, angle, hit.color);
            foreach (var t in Combat.HostilesInArc(ctx.caster.team, ctx.caster.transform.position, ctx.direction, r, angle))
            {
                Combat.Hit(ctx.caster, t, damageMult * ctx.power, ctx.Scale(hit.status), false, hit.knockback, -1f, hit.elements);
                hit.OnHit(ctx, t);
            }
        }

        public override string Describe(float power) => $"A {angle:0}° wave for {Pct(damageMult * power)} damage";
    }

    // Buff modifiers: applied to the caster whenever the skill is cast.
    [Serializable]
    public class CraftedBuff : SkillEffect
    {
        public float empower, haste, barrier, mend;
        public float duration = 5f;
        public Color color = Color.white;

        public bool Any => empower > 0f || haste > 0f || barrier > 0f || mend > 0f;

        public override void Apply(SkillContext ctx)
        {
            var c = ctx.caster;
            if (c == null) return;
            float dur = duration * ctx.Duration;
            Vector3 head = c.transform.position + View.Up * (c.HeadHeight + 0.5f);
            if (empower > 0f)
            {
                c.AddTimedModifiers("empower", dur, (StatType.Damage, ModKind.Percent, empower));
                FloatingText.Spawn(head, "Empowered", new Color(1f, 0.6f, 0.3f), 0.8f);
            }
            if (haste > 0f)
            {
                c.AddTimedModifiers("haste", dur * 0.8f, (StatType.MoveSpeed, ModKind.Percent, haste), (StatType.AttackSpeed, ModKind.Percent, haste));
                FloatingText.Spawn(head + View.Up * 0.4f, "Haste", new Color(0.6f, 1f, 0.8f), 0.8f);
            }
            if (barrier > 0f) c.AddShield(c.MaxHP * barrier, dur);
            if (mend > 0f) c.Heal(c.MaxHP * mend);
            VFX.Ring(c.transform.position, color, 1.4f, 0.35f);
        }

        public override string Describe(float power)
        {
            var parts = new List<string>();
            if (empower > 0f) parts.Add($"+{Pct(empower)} damage");
            if (haste > 0f) parts.Add($"+{Pct(haste)} speed");
            if (barrier > 0f) parts.Add($"a {Pct(barrier)} life shield");
            if (mend > 0f) parts.Add($"heals {Pct(mend)} life");
            return "On cast: " + string.Join(", ", parts);
        }
    }

    // Self delivery: a blessing on the caster that depends on the element.
    [Serializable]
    public class CraftedSelf : SkillEffect
    {
        public ElementPart element;
        public float strength = 1f;
        public float duration = 6f;
        public Color color = Color.white;

        public override void Apply(SkillContext ctx)
        {
            var c = ctx.caster;
            if (c == null) return;
            float k = strength * ctx.power;
            float dur = duration * ctx.Duration;
            Vector3 head = c.transform.position + View.Up * (c.HeadHeight + 0.5f);
            string text;
            switch (element)
            {
                case ElementPart.Holy:
                    c.Heal(c.MaxHP * 0.3f * k);
                    text = null;
                    break;
                case ElementPart.Fire:
                    c.AddTimedModifiers("bless", dur, (StatType.Damage, ModKind.Percent, 0.3f * k));
                    text = "Burning fury";
                    break;
                case ElementPart.Frost:
                    c.AddShield(c.MaxHP * 0.3f * k, dur);
                    text = "Ice armor";
                    break;
                case ElementPart.Lightning:
                    c.AddTimedModifiers("bless", dur, (StatType.MoveSpeed, ModKind.Percent, 0.3f * k), (StatType.AttackSpeed, ModKind.Percent, 0.3f * k));
                    text = "Storm speed";
                    break;
                case ElementPart.Poison:
                    c.AddTimedModifiers("bless", dur, (StatType.CritChance, ModKind.Flat, 0.15f * k), (StatType.CritDamage, ModKind.Flat, 0.3f * k));
                    text = "Venom edge";
                    break;
                case ElementPart.Arcane:
                    c.AddTimedModifiers("bless", dur, (StatType.CooldownReduction, ModKind.Flat, 0.25f * k), (StatType.SkillPower, ModKind.Flat, 0.15f * k));
                    text = "Arcane focus";
                    break;
                case ElementPart.Shadow:
                    c.AddTimedModifiers("bless", dur, (StatType.LifeSteal, ModKind.Flat, 0.12f * k));
                    text = "Shadow thirst";
                    break;
                default:
                    c.AddTimedModifiers("bless", dur, (StatType.Armor, ModKind.Percent, 0.5f * k));
                    text = "Stoneskin";
                    break;
            }
            if (text != null) FloatingText.Spawn(head, text, color, 0.9f);
            VFX.Ring(c.transform.position, color, 1.8f, 0.4f);
            VFX.Burst(c.transform.position + View.Up * 0.8f, color, 18, 0.9f);
        }

        public override string Describe(float power)
        {
            float k = strength * power;
            switch (element)
            {
                case ElementPart.Holy: return $"Heals you for {Pct(0.3f * k)} of your life";
                case ElementPart.Fire: return $"+{Pct(0.3f * k)} damage for {duration:0} s";
                case ElementPart.Frost: return $"A shield of {Pct(0.3f * k)} of your life";
                case ElementPart.Lightning: return $"+{Pct(0.3f * k)} move and attack speed for {duration:0} s";
                case ElementPart.Poison: return $"+{Pct(0.15f * k)} crit chance for {duration:0} s";
                case ElementPart.Arcane: return $"+{Pct(0.25f * k)} cooldown reduction for {duration:0} s";
                case ElementPart.Shadow: return $"+{Pct(0.12f * k)} life steal for {duration:0} s";
                default: return $"+{Pct(0.5f * k)} armor for {duration:0} s";
            }
        }
    }

    // Aura delivery: pulses around the caster for a while.
    [Serializable]
    public class CraftedAura : SkillEffect
    {
        public float radius = 2.6f, interval = 0.6f, damageMult = 0.45f;
        public int pulses = 8, extraTargets;
        public HitExtras hit = new HitExtras();

        public override void Apply(SkillContext ctx)
        {
            if (ctx.runner != null) ctx.runner.StartCoroutine(Run(ctx));
        }

        System.Collections.IEnumerator Run(SkillContext ctx)
        {
            for (int i = 0; i < pulses && ctx.caster != null && !ctx.caster.IsDead; i++)
            {
                Vector2 at = ctx.caster.transform.position;
                float r = radius * ctx.Area;
                VFX.Ring(at, hit.color, r, 0.3f);
                foreach (var t in Combat.HostilesInRadius(ctx.caster.team, at, r))
                {
                    Combat.Hit(ctx.caster, t, damageMult * ctx.power, ctx.Scale(hit.status), false, 0f, -1f, hit.elements);
                    hit.Afflict(ctx, t);
                }
                yield return new WaitForSeconds(interval);
            }
        }

        public override string Describe(float power) =>
            $"Pulses {pulses} times around you for {Pct(damageMult * power)} damage each{(hit.heal > 0f ? ", healing you on every hit" : "")}";
    }

    // Hex delivery: curses enemies in front of you so they take more damage.
    [Serializable]
    public class CraftedHex : SkillEffect
    {
        public float range = 8f, damageMult = 0.9f, duration = 6f;
        public int targets = 1;
        public HitExtras hit = new HitExtras();

        public override void Apply(SkillContext ctx)
        {
            hit.debuffDuration = duration;
            float r = range * ctx.Range;
            var list = Combat.HostilesInRadius(ctx.caster.team, ctx.origin, r);
            list.Sort((a, b) => Score(ctx, a).CompareTo(Score(ctx, b)));
            for (int i = 0; i < Mathf.Min(targets, list.Count); i++)
            {
                var t = list[i];
                Vector3 p = t.transform.position;
                var mark = new Color(0.6f, 0.3f, 0.9f);
                VFX.Ring(p, mark, 1.2f, 0.5f);
                VFX.Burst(p + View.Up * (t.HeadHeight + 0.2f), Color.Lerp(mark, hit.color, 0.5f), 16, 0.7f);
                Combat.Hit(ctx.caster, t, damageMult * ctx.power, ctx.Scale(hit.status), false, 0f, -1f, hit.elements);
                hit.OnHit(ctx, t);
            }
        }

        static float Score(SkillContext ctx, Combatant c)
        {
            Vector2 to = (Vector2)c.transform.position - ctx.origin;
            return to.magnitude * 0.3f + Vector2.Angle(ctx.direction, to) * 0.05f;
        }

        public override string Describe(float power) =>
            $"Curses {(targets > 1 ? targets + " enemies" : "an enemy")}: +{Pct(hit.expose)} damage taken for {duration:0} s";
    }

    // Owned parts, crafted skills and their runtime definitions.
    public static class Crafting
    {
        static readonly Dictionary<string, SkillDefinition> built = new Dictionary<string, SkillDefinition>();

        public static void GiveStarterKit(SaveData d)
        {
            d.parts.Add(new PartRef { kind = PartKind.Element, id = (int)ElementPart.Physical, tier = 1 });
            d.parts.Add(new PartRef { kind = PartKind.Delivery, id = (int)DeliveryPart.Forward, tier = 1 });
            d.parts.Add(new PartRef { kind = PartKind.Delivery, id = (int)DeliveryPart.Single, tier = 1 });
            var first = new CraftedSkill { uid = NewUid(), element = ElementPart.Physical, delivery = DeliveryPart.Forward };
            first.name = SkillParts.NameFor(first.element, first.delivery, first.mods);
            d.craftedSkills.Add(first);
            d.equippedSkills[0] = "craft:" + first.uid;
        }

        static string NewUid() => Guid.NewGuid().ToString("N").Substring(0, 10);

        public static PartRef Owned(PartKind k, int id) => Profile.Data.parts.Find(p => p.kind == k && p.id == id);

        // Returns a message for the HUD.
        public static string AddPart(PartRef part)
        {
            var have = Owned(part.kind, part.id);
            if (have == null)
            {
                Profile.Data.parts.Add(part);
                return $"New skill part: {part.FullName}";
            }
            if (part.tier > have.tier)
            {
                have.tier = part.tier;
                return $"Upgraded: {have.FullName}";
            }
            int gold = 15 * part.tier;
            Profile.AddGold(gold);
            return $"Duplicate {part.FullName}: +{gold} gold";
        }

        public static SkillDefinition Get(string equippedId)
        {
            if (string.IsNullOrEmpty(equippedId) || !equippedId.StartsWith("craft:")) return null;
            string uid = equippedId.Substring(6);
            if (built.TryGetValue(uid, out var s) && s != null) return s;
            var c = Profile.Data.craftedSkills.Find(x => x.uid == uid);
            if (c == null) return null;
            s = SkillComposer.Build(c);
            built[uid] = s;
            return s;
        }

        public static CraftedSkill Find(string equippedId) =>
            equippedId != null && equippedId.StartsWith("craft:") ? Profile.Data.craftedSkills.Find(x => x.uid == equippedId.Substring(6)) : null;

        public static bool CanCraft(List<ElementPart> els, DeliveryPart d, List<ModPart> mods, out string why)
        {
            why = null;
            if (els == null || els.Count == 0) { why = "Pick an element."; return false; }
            if (els.Count > BossUnlocks.MaxElements) { why = "Defeat more bosses to combine that many elements."; return false; }
            var pd = Owned(PartKind.Delivery, (int)d);
            if (pd == null) { why = "You don't own those parts."; return false; }
            if (SkillParts.WeaponOf(d).HasValue && !BossUnlocks.WeaponArts) { why = "Weapon arts are not unlocked yet."; return false; }
            if (mods.Count > SkillParts.MaxMods(Profile.Data.level)) { why = "Too many modifiers: defeat more bosses for extra slots."; return false; }
            foreach (var e in els)
            {
                var pe = Owned(PartKind.Element, (int)e);
                if (pe == null) { why = "You don't own those parts."; return false; }
                if (!SkillParts.RequirementsMet(PartKind.Element, (int)e, pe.tier)) { why = $"{e} needs more attributes."; return false; }
            }
            if (!SkillParts.RequirementsMet(PartKind.Delivery, (int)d, pd.tier))
            {
                why = "Your attributes are too low.";
                return false;
            }
            foreach (var m in mods)
            {
                var pm = Owned(PartKind.Modifier, (int)m);
                if (pm == null) { why = "You don't own that modifier."; return false; }
                if (!SkillParts.RequirementsMet(PartKind.Modifier, (int)m, pm.tier)) { why = $"{m} needs more attributes."; return false; }
            }
            if (Profile.Data.craftedSkills.Count >= SkillParts.MaxCrafted) { why = "You can keep 6 crafted skills. Delete one first."; return false; }
            if (Profile.Data.gold < SkillParts.CraftCost(Profile.Data.level, mods.Count)) { why = "Not enough gold."; return false; }
            return true;
        }

        public static CraftedSkill Craft(ElementPart e, DeliveryPart d, List<ModPart> mods) => Craft(new List<ElementPart> { e }, d, mods);

        public static CraftedSkill Craft(List<ElementPart> els, DeliveryPart d, List<ModPart> mods)
        {
            if (!CanCraft(els, d, mods, out _)) return null;
            Profile.AddGold(-SkillParts.CraftCost(Profile.Data.level, mods.Count + els.Count - 1));
            var c = new CraftedSkill
            {
                uid = NewUid(), element = els[0], delivery = d, mods = new List<ModPart>(mods),
                elementTier = Owned(PartKind.Element, (int)els[0]).tier, deliveryTier = Owned(PartKind.Delivery, (int)d).tier,
            };
            for (int i = 1; i < els.Count; i++)
            {
                c.extraElements.Add(els[i]);
                c.extraElementTiers.Add(Owned(PartKind.Element, (int)els[i]).tier);
            }
            foreach (var m in mods) c.modTiers.Add(Owned(PartKind.Modifier, (int)m).tier);
            c.name = SkillParts.NameFor(els, d, c.mods);
            Profile.Data.craftedSkills.Add(c);
            // Fill an empty slot automatically.
            for (int i = 0; i < 4; i++)
            {
                if (!string.IsNullOrEmpty(Profile.Data.equippedSkills[i])) continue;
                if (i == 3 && c.mods.Count < 3) continue;
                Profile.Data.equippedSkills[i] = "craft:" + c.uid;
                break;
            }
            Profile.NotifySkills();
            Profile.Save();
            return c;
        }

        public static void Delete(CraftedSkill c)
        {
            Profile.Data.craftedSkills.Remove(c);
            built.Remove(c.uid);
            for (int i = 0; i < 4; i++)
                if (Profile.Data.equippedSkills[i] == "craft:" + c.uid) Profile.Data.equippedSkills[i] = null;
            Profile.NotifySkills();
        }

        public static void ClearCache() => built.Clear();
    }
}
