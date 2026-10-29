using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TWF
{
    public enum Targeting
    {
        Skillshot, // aimed in a direction (line indicator)
        GroundAoE, // aimed at a point (circle indicator)
        Self,      // centered on the hero, no aiming
        Dash,      // moves the hero in a direction
    }

    [CreateAssetMenu(menuName = "TWF/Skill")]
    public class SkillDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        [Tooltip("Short text drawn on the skill button when there's no icon.")]
        public string glyph = "?";
        public Color color = Color.white;
        public Sprite icon;
        [Tooltip("IconFactory key; empty = picked from the name.")]
        public string iconKey;

        public bool isUltimate;
        public Targeting targeting;
        public float range = 6f;
        [Tooltip("Radius of the circle indicator for GroundAoE / Self skills.")]
        public float indicatorRadius = 2f;
        public float baseCooldown = 6f;
        [Tooltip("Cooldown fraction removed per extra rank (0.05 = 5%).")]
        public float cooldownPerRank = 0.05f;
        [Tooltip("Damage/effect fraction added per extra rank (0.15 = 15%).")]
        public float powerPerRank = 0.15f;
        public int maxRank = 5;
        [Tooltip("Seconds the hero can't move after casting.")]
        public float castLock = 0.12f;

        [SerializeReference] public List<SkillEffect> effects = new List<SkillEffect>();

        public float Cooldown(int rank, float cdr) =>
            baseCooldown * (1f - cooldownPerRank * Mathf.Max(0, rank - 1)) * (1f - cdr);

        public float Power(int rank) => 1f + powerPerRank * Mathf.Max(0, rank - 1);

        public void Cast(SkillContext ctx)
        {
            foreach (var e in effects) e?.Apply(ctx);
        }

        public string Describe(int rank)
        {
            var sb = new StringBuilder();
            sb.Append(description);
            float power = Power(Mathf.Max(1, rank));
            foreach (var e in effects)
            {
                var d = e?.Describe(power);
                if (!string.IsNullOrEmpty(d)) sb.Append("\n<color=#bbb>• ").Append(d).Append("</color>");
            }
            sb.Append($"\n<color=#8cf>Cooldown {Cooldown(Mathf.Max(1, rank), 0f):0.#}s</color>");
            return sb.ToString();
        }
    }

    public class SkillContext
    {
        public Combatant caster;
        public MonoBehaviour runner;   // for coroutines
        public Rigidbody2D body;
        public Vector2 origin;
        public Vector2 direction;      // normalized
        public Vector2 point;          // ground target
        public int rank;
        public float power;            // multiplies damage and effect values
        public Combatant autoTarget;

        // Gear modifiers.
        float Stat(StatType t) => caster != null ? caster.Stats.Get(t) : 0f;
        public float Range => 1f + Stat(StatType.SkillRange);
        public float Area => 1f + Stat(StatType.AreaSize);
        public int ExtraProjectiles => Mathf.RoundToInt(Stat(StatType.ExtraProjectiles));
        public float ProjectileSpeed => 1f + Stat(StatType.ProjectileSpeed);
        public float Duration => 1f + Stat(StatType.EffectDuration);
        public float Spread => 1f + Stat(StatType.ProjectileSpread);

        public StatusSpec Scale(StatusSpec s)
        {
            if (s.IsSet) s.duration *= Duration;
            return s;
        }
    }

    [Serializable]
    public abstract class SkillEffect
    {
        public abstract void Apply(SkillContext ctx);
        public virtual string Describe(float power) => null;

        protected static string Pct(float mult) => Mathf.RoundToInt(mult * 100f) + "%";
    }

    [Serializable]
    public class MeleeArcEffect : SkillEffect
    {
        public float radius = 2f;
        public float angle = 120f;
        public float damageMult = 1.5f;
        public StatusSpec status;
        public float knockback;
        public Color color = Color.white;

        public override void Apply(SkillContext ctx)
        {
            float r = radius * ctx.Area;
            VFX.Slash(ctx.caster.transform.position, ctx.direction, r, angle, color);
            foreach (var t in Combat.HostilesInArc(ctx.caster.team, ctx.caster.transform.position, ctx.direction, r, angle))
                Combat.Hit(ctx.caster, t, damageMult * ctx.power, ctx.Scale(status), false, knockback);
        }

        public override string Describe(float power) => $"Hits in a {angle:0}° arc for {Pct(damageMult * power)} damage";
    }

    [Serializable]
    public class ProjectileEffect : SkillEffect
    {
        public int count = 1;
        public float spread = 0f;
        public float speed = 14f;
        public float range = 8f;
        public float hitRadius = 0.35f;
        public float damageMult = 1.2f;
        public int pierce = 0;
        public float explodeRadius = 0f;
        public StatusSpec status;
        public float size = 0.5f;
        public Color color = Color.white;
        public float knockback;
        public bool useArrowSprite;

        public override void Apply(SkillContext ctx)
        {
            var spec = new ProjectileSpec
            {
                speed = speed * ctx.ProjectileSpeed, range = range * ctx.Range, hitRadius = hitRadius, damageMult = damageMult * ctx.power,
                pierce = pierce, explodeRadius = explodeRadius * ctx.Area, status = ctx.Scale(status), color = color, size = size,
                knockback = knockback, sprite = useArrowSprite ? HeroVisuals.ArrowSprite : null, rotateSprite = true,
            };
            // Extra projectiles from gear fan out; spread gear widens or tightens the fan.
            int total = count + ctx.ExtraProjectiles;
            float fan = Mathf.Max(spread, total > 1 ? 12f * (total - 1) : 0f) * ctx.Spread;
            float baseAngle = Mathf.Atan2(ctx.direction.y, ctx.direction.x) * Mathf.Rad2Deg;
            for (int i = 0; i < total; i++)
            {
                float a = total == 1 ? baseAngle : baseAngle - fan / 2f + fan * i / (total - 1);
                var d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                Projectile.Fire(ctx.caster, ctx.origin + d * 0.4f, d, spec);
            }
        }

        public override string Describe(float power)
        {
            string what = count > 1 ? $"Fires {count} projectiles" : "Fires a projectile";
            string extra = pierce < 0 ? ", piercing all enemies" : pierce > 0 ? $", piercing {pierce}" : "";
            if (explodeRadius > 0) extra += ", exploding on impact";
            return $"{what} for {Pct(damageMult * power)} damage{extra}";
        }
    }

    [Serializable]
    public class GroundAreaEffect : SkillEffect
    {
        public bool atCaster;
        public bool followCaster;
        public float radius = 2.5f;
        public float delay = 0.4f;
        public int ticks = 1;
        public float interval = 0.4f;
        public float damageMult = 2f;
        public StatusSpec status;
        public Color color = Color.white;
        public float knockback;

        public override void Apply(SkillContext ctx)
        {
            Vector2 center = atCaster ? (Vector2)ctx.caster.transform.position : ctx.point;
            AreaEffect.Spawn(ctx.caster, center, new AreaSpec
            {
                radius = radius * ctx.Area, delay = delay, ticks = ticks, interval = interval,
                damageMult = damageMult * ctx.power, status = ctx.Scale(status), color = color,
                knockback = knockback, followOwner = followCaster,
            });
        }

        public override string Describe(float power)
        {
            string hits = ticks > 1 ? $"{ticks} times for {Pct(damageMult * power)} damage each" : $"for {Pct(damageMult * power)} damage";
            string extra = status.type == StatusType.Stun ? $", stunning for {status.duration:0.#}s"
                : status.type == StatusType.Slow ? $", slowing by {Mathf.RoundToInt(status.magnitude * 100)}%"
                : status.type == StatusType.Burn ? ", setting enemies on fire" : "";
            return $"Hits an area {hits}{extra}";
        }
    }

    [Serializable]
    public class DashEffect : SkillEffect
    {
        public float distance = 4f;
        public float duration = 0.18f;
        public bool blink;          // teleport instead of moving
        public bool invulnerable = true;
        public float damageMult;    // > 0 = damages enemies touched on the way or at landing
        public float hitRadius = 1.2f;
        public bool damageAtEnd;
        public StatusSpec status;
        public Color color = Color.white;

        public override void Apply(SkillContext ctx) => ctx.runner.StartCoroutine(Run(ctx));

        IEnumerator Run(SkillContext ctx)
        {
            var caster = ctx.caster;
            var body = ctx.body;
            Vector2 start = caster.transform.position;
            float dist = distance * ctx.Range;
            float hitR = hitRadius * ctx.Area;

            // Don't dash through walls.
            var hit = Physics2D.CircleCast(start, 0.3f, ctx.direction, dist, LayerMask.GetMask("Walls", "Default"));
            if (hit.collider != null && !hit.collider.isTrigger && hit.collider.attachedRigidbody != body)
                dist = Mathf.Max(0f, hit.distance - 0.1f);

            Vector2 end = start + ctx.direction * dist;
            if (invulnerable) caster.Invulnerable = true;
            var hitSet = new HashSet<Combatant>();

            if (blink)
            {
                VFX.Burst(start, color, 12, 0.6f);
                if (body) body.position = end; else caster.transform.position = end;
                VFX.Burst(end, color, 12, 0.6f);
                yield return null;
            }
            else
            {
                float t = 0f;
                while (t < duration)
                {
                    t += Time.fixedDeltaTime;
                    Vector2 p = Vector2.Lerp(start, end, Mathf.SmoothStep(0, 1, t / duration));
                    if (body) body.MovePosition(p); else caster.transform.position = p;
                    if (damageMult > 0f && !damageAtEnd)
                    {
                        foreach (var c in Combat.HostilesInRadius(caster.team, p, hitR))
                            if (hitSet.Add(c)) Combat.Hit(caster, c, damageMult * ctx.power, ctx.Scale(status));
                    }
                    if (UnityEngine.Random.value < 0.6f) VFX.Burst(p, color, 1, 0.2f);
                    yield return new WaitForFixedUpdate();
                }
            }

            if (damageMult > 0f && damageAtEnd)
            {
                VFX.Ring(end, color, hitR, 0.3f);
                foreach (var c in Combat.HostilesInRadius(caster.team, end, hitR))
                    Combat.Hit(caster, c, damageMult * ctx.power, ctx.Scale(status), false, 1.5f);
            }
            if (invulnerable) caster.Invulnerable = false;
        }

        public override string Describe(float power)
        {
            string move = blink ? $"Teleports {distance:0.#}m" : $"Dashes {distance:0.#}m";
            if (damageMult > 0f) move += $", dealing {Pct(damageMult * power)} damage" + (damageAtEnd ? " on landing" : " to enemies passed through");
            return move;
        }
    }

    [Serializable]
    public class BuffEffect : SkillEffect
    {
        public StatType stat = StatType.Damage;
        public ModKind kind = ModKind.Percent;
        public float value = 0.25f;
        public float duration = 5f;
        public bool scaleWithRank = true;
        public Color color = Color.yellow;

        public override void Apply(SkillContext ctx)
        {
            float v = scaleWithRank ? value * ctx.power : value;
            ctx.runner.StartCoroutine(Run(ctx.caster, v, duration * ctx.Duration));
            VFX.Ring(ctx.caster.transform.position, color, 1.5f, 0.4f);
        }

        IEnumerator Run(Combatant c, float v, float time)
        {
            var source = new object();
            c.Stats.AddModifier(new StatModifier(stat, kind, v, source));
            yield return new WaitForSeconds(time);
            if (c != null) c.Stats.RemoveModifiersFrom(source);
        }

        public override string Describe(float power) =>
            $"{StatInfo.Format(stat, kind, scaleWithRank ? value * power : value)} for {duration:0.#}s";
    }

    [Serializable]
    public class HealEffect : SkillEffect
    {
        public float percentMaxHP = 0.15f;

        public override void Apply(SkillContext ctx) => ctx.caster.Heal(ctx.caster.MaxHP * percentMaxHP * ctx.power);

        public override string Describe(float power) => $"Heals {Pct(percentMaxHP * power)} of max life";
    }

    [Serializable]
    public class ShieldEffect : SkillEffect
    {
        public float percentMaxHP = 0.2f;
        public float duration = 4f;

        public override void Apply(SkillContext ctx)
        {
            ctx.caster.AddShield(ctx.caster.MaxHP * percentMaxHP * ctx.power, duration * ctx.Duration);
            VFX.Ring(ctx.caster.transform.position, Palette.Shield, 1.2f, 0.4f);
        }

        public override string Describe(float power) => $"Shields for {Pct(percentMaxHP * power)} of max life for {duration:0.#}s";
    }

    [Serializable]
    public class RepeatEffect : SkillEffect
    {
        [Tooltip("Repeats the inner effects several times (Whirlwind spins, Arrow Rain volleys).")]
        public int times = 3;
        public float interval = 0.3f;
        [SerializeReference] public List<SkillEffect> inner = new List<SkillEffect>();

        public override void Apply(SkillContext ctx) => ctx.runner.StartCoroutine(Run(ctx));

        IEnumerator Run(SkillContext ctx)
        {
            for (int i = 0; i < times; i++)
            {
                if (ctx.caster == null || ctx.caster.IsDead) yield break;
                ctx.origin = ctx.caster.transform.position;
                foreach (var e in inner) e?.Apply(ctx);
                yield return new WaitForSeconds(interval);
            }
        }

        public override string Describe(float power)
        {
            var parts = new List<string>();
            foreach (var e in inner)
            {
                var d = e?.Describe(power);
                if (d != null) parts.Add(d);
            }
            return $"{times}× over {times * interval:0.#}s: " + string.Join("; ", parts);
        }
    }
}
