using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public struct ProjectileSpec
    {
        public float speed;
        public float range;
        public float hitRadius;
        public float damageMult;
        public int pierce;            // extra targets after the first; -1 = unlimited
        public float explodeRadius;   // > 0 = explodes on first hit / end of range
        public StatusSpec status;
        public Color color;
        public float size;
        public Sprite sprite;
        public bool rotateSprite;
        public float knockback;
        public bool homing;
        public float snapshotDamage;
        public System.Action<Combatant> onHit;
        public int elements;
        public string look;           // drawn look: "arrow", "knife", "spear", "axe", "orb", "bolt"; empty picks from the sprite and size

        public static ProjectileSpec Default(Color color) => new ProjectileSpec
        {
            speed = 12f, range = 8f, hitRadius = 0.35f, damageMult = 1f, color = color, size = 0.5f,
        };
    }

    public class Projectile : MonoBehaviour
    {
        ProjectileSpec spec;
        Combatant owner;
        Team team;
        Vector2 dir;
        float travelled;
        int pierceLeft;
        readonly HashSet<Combatant> hit = new HashSet<Combatant>();
        Combatant homingTarget;

        // What guardians read to step into a shot's path.
        public static readonly List<Projectile> Live = new List<Projectile>();
        public Team OwnerTeam => team;
        public Vector2 Direction => dir;
        public float Speed => spec.speed;
        public float RangeLeft => spec.range - travelled;

        void OnEnable() => Live.Add(this);
        void OnDisable() => Live.Remove(this);

        public static Projectile Fire(Combatant owner, Vector2 origin, Vector2 direction, ProjectileSpec spec, Combatant homingTarget = null)
        {
            var go = new GameObject("Projectile");
            go.transform.position = origin;
            var p = go.AddComponent<Projectile>();
            p.spec = spec;
            if (owner != null) p.spec.snapshotDamage = owner.Stats.Get(StatType.Damage);
            p.owner = owner;
            p.team = owner != null ? owner.team : Team.Enemy;
            p.dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            p.pierceLeft = spec.pierce;
            p.homingTarget = homingTarget;

            // The drawn look in the shot's element: arrows, knives, spears, axes, bolts and orbs.
            var drawn = Fx.Projectile(spec, go.transform);
            if (drawn != null) { }
            else if (spec.sprite != null)
                GameAssets.CreateWorldSprite("visual", spec.sprite, go.transform, spec.color, SortOrder.Projectiles, spec.size);
            else
            {
                GameAssets.CreateWorldSprite("visual", GameAssets.PixelDot, go.transform, spec.color, SortOrder.Projectiles, spec.size);
                var glow = GameAssets.CreateWorldSprite("glow", GameAssets.PixelGlow, go.transform,
                    new Color(spec.color.r, spec.color.g, spec.color.b, 0.45f), SortOrder.Projectiles - 1, spec.size * 2.4f);
                var core = GameAssets.CreateWorldSprite("core", GameAssets.PixelDot, go.transform,
                    new Color(1f, 1f, 1f, 0.9f), SortOrder.Projectiles + 1, spec.size * 0.45f);
            }
            // Fly at chest height (the ground is z = 0 and up is -z).
            foreach (Transform child in go.transform) child.localPosition += Vector3.back * 0.7f;
            if (owner != null && owner.team == Team.Player) PointLights.Attach(go.transform, spec.color, 3.5f, 2.2f, new Vector3(0, 0, -0.9f));
            if (spec.rotateSprite || spec.sprite != null || drawn != null)
                go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(p.dir.y, p.dir.x) * Mathf.Rad2Deg);
            return p;
        }

        void Update()
        {
            float step = spec.speed * Time.deltaTime;
            if (spec.homing && homingTarget != null && !homingTarget.IsDead)
            {
                Vector2 to = ((Vector2)homingTarget.transform.position - (Vector2)transform.position).normalized;
                dir = Vector2.Lerp(dir, to, Time.deltaTime * 8f).normalized;
                transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            }
            transform.position += (Vector3)(dir * step);
            travelled += step;

            // Walls, pillars, rocks and trees stop projectiles.
            if (Physics2D.OverlapPoint(transform.position, Walls.Mask) is Collider2D wall && !wall.isTrigger)
            {
                if (spec.explodeRadius > 0f) Explode();
                else
                {
                    Fx.Hit(transform.position + Vector3.back * 0.7f, Element, 0.3f);
                    Destroy(gameObject);
                }
                return;
            }

            foreach (var c in Combat.HostilesInRadius(team, transform.position, spec.hitRadius))
            {
                if (hit.Contains(c)) continue;
                hit.Add(c);
                // A guardian with its guard up takes the shot on its shield: a little damage, and the shot stops there.
                var guard = team == Team.Player ? c.GetComponent<MonsterBrain>() : null;
                if (guard != null && guard.TryBlockProjectile(dir))
                {
                    Combat.Hit(owner, c, spec.damageMult * MonsterBrain.BlockedDamage, default, false, 0f, spec.snapshotDamage, spec.elements);
                    Destroy(gameObject);
                    return;
                }
                if (spec.explodeRadius > 0f)
                {
                    Explode();
                    return;
                }
                Combat.Hit(owner, c, spec.damageMult, spec.status, false, spec.knockback, spec.snapshotDamage, spec.elements);
                spec.onHit?.Invoke(c);
                Fx.HitOn(c, spec.elements, spec.color);
                if (spec.pierce >= 0)
                {
                    if (pierceLeft <= 0)
                    {
                        Destroy(gameObject);
                        return;
                    }
                    pierceLeft--;
                }
                break;
            }

            if (travelled >= spec.range)
            {
                if (spec.explodeRadius > 0f) Explode();
                else Destroy(gameObject);
            }
        }

        string Element => Fx.Element(spec.elements, spec.color);

        void Explode()
        {
            Fx.Blast(transform.position, spec.explodeRadius, spec.elements, spec.color);
            foreach (var c in Combat.HostilesInRadius(team, transform.position, spec.explodeRadius).ToArray())
            {
                Combat.Hit(owner, c, spec.damageMult, spec.status, false, spec.knockback, spec.snapshotDamage, spec.elements);
                spec.onHit?.Invoke(c);
            }
            Destroy(gameObject);
        }
    }

    public static class Walls
    {
        static int mask = -1;
        public static int Mask => mask >= 0 ? mask : (mask = LayerMask.GetMask("Walls"));

        // True when a wall (pillar, cliff) stands between two points.
        public static bool Blocked(Vector2 from, Vector2 to)
        {
            var hit = Physics2D.Linecast(from, to, Mask);
            return hit.collider != null && !hit.collider.isTrigger;
        }
    }

    public struct AreaSpec
    {
        public float radius;
        public float delay;        // telegraph time before the first hit
        public int ticks;          // number of damage pulses (>= 1)
        public float interval;
        public float damageMult;
        public StatusSpec status;
        public Color color;
        public float knockback;
        public bool followOwner;   // e.g. Whirlwind, auras
        public float snapshotDamage;
        public System.Action<Combatant> onHit;
        public int elements;
    }

    // A circle on the ground: telegraphed, then deals damage one or more times.
    public class AreaEffect : MonoBehaviour
    {
        AreaSpec spec;
        Combatant owner;
        Team team;
        SpriteRenderer fillSr, ringSr;

        public static AreaEffect Spawn(Combatant owner, Vector2 center, AreaSpec spec)
        {
            var go = new GameObject("Area");
            go.transform.position = Ground.Decal(center);
            var a = go.AddComponent<AreaEffect>();
            a.spec = spec;
            a.spec.ticks = Mathf.Max(1, spec.ticks);
            if (owner != null) a.spec.snapshotDamage = owner.Stats.Get(StatType.Damage);
            a.owner = owner;
            a.team = owner != null ? owner.team : Team.Enemy;
            var c = spec.color;
            // Drawn at their real size so the warning stays crisp instead of turning into a blocky grid.
            a.ringSr = GameAssets.CreateWorldSprite("ring", Fx.Ring(spec.radius), go.transform, new Color(c.r, c.g, c.b, 0.85f), SortOrder.Telegraph + 1, spec.radius * 2f);
            a.fillSr = GameAssets.CreateWorldSprite("fill", Fx.Disc(spec.radius), go.transform, new Color(c.r, c.g, c.b, 0.3f), SortOrder.Telegraph, spec.radius * 2f);
            a.StartCoroutine(a.Run());
            return a;
        }

        IEnumerator Run()
        {
            var c = spec.color;
            float t = 0f;
            Vector3 full = fillSr.transform.localScale;
            while (t < spec.delay)
            {
                t += Time.deltaTime;
                Follow();
                fillSr.transform.localScale = full * Mathf.Lerp(0.1f, 1f, t / spec.delay);
                yield return null;
            }
            fillSr.transform.localScale = full;

            // One pulse explodes; several leave a pool that pulses (and, when it stays put, starts with a blast).
            var els = Fx.Parts(spec.elements, c);
            bool lingers = spec.ticks > 1;
            if (lingers)
                Fx.Linger(transform.position, spec.radius, spec.elements, c, spec.interval * (spec.ticks - 1) + 0.5f,
                    spec.followOwner && owner != null ? owner.transform : null);
            for (int i = 0; i < spec.ticks; i++)
            {
                Follow();
                fillSr.color = new Color(c.r, c.g, c.b, lingers ? 0.16f : 0.22f);
                if (!lingers) Fx.Blast(transform.position, spec.radius, spec.elements, c);
                else if (i == 0 && !spec.followOwner) Fx.Blast(transform.position, spec.radius * 0.8f, spec.elements, c);
                else Fx.Shock(transform.position, spec.radius, Fx.Name(els[i % els.Count]), 0.55f);   // pulses take turns per element
                foreach (var target in Combat.HostilesInRadius(team, transform.position, spec.radius).ToArray())
                {
                    Combat.Hit(owner, target, spec.damageMult, spec.status, false, spec.knockback, spec.snapshotDamage, spec.elements);
                    spec.onHit?.Invoke(target);
                }
                float w = 0f;
                while (w < spec.interval && i < spec.ticks - 1)
                {
                    w += Time.deltaTime;
                    Follow();
                    fillSr.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.16f, 0.1f, w / Mathf.Max(0.01f, spec.interval)));
                    yield return null;
                }
            }

            float fade = 0.25f;
            while (fade > 0f)
            {
                fade -= Time.deltaTime;
                fillSr.color = new Color(c.r, c.g, c.b, 0.2f * fade / 0.25f);
                ringSr.color = new Color(c.r, c.g, c.b, 0.85f * fade / 0.25f);
                yield return null;
            }
            Destroy(gameObject);
        }

        void Follow()
        {
            if (spec.followOwner && owner != null) transform.position = owner.transform.position;
        }
    }

    // A rectangle on the ground warning of a charge or beam.
    public static class LineTelegraph
    {
        public static GameObject Show(Vector2 from, Vector2 dir, float length, float width, Color color, float duration)
        {
            var go = new GameObject("LineTelegraph");
            go.transform.position = Ground.Decal(from);
            go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            var sr = GameAssets.CreateWorldSprite("rect", GameAssets.Pixel, go.transform, color, SortOrder.Telegraph);
            float unit = sr.sprite.bounds.size.x;
            sr.transform.localScale = new Vector3(length / unit, width / unit, 1f);
            sr.transform.localPosition = new Vector3(length / 2f, 0f, 0f);
            Object.Destroy(go, duration);
            return go;
        }
    }

    // Tiny sprite-based particle effects; no particle system assets required.
    public class VFX : MonoBehaviour
    {
        Vector3 velocity;
        float life, maxLife;
        SpriteRenderer sr;
        Color color;
        float growTo;
        Vector3 startScale;

        public static void Burst(Vector3 pos, Color color, int count, float spread)
        {
            for (int i = 0; i < count; i++)
            {
                // Square sparks of 2-3 art pixels, the same pixel size as the characters.
                var sr = GameAssets.CreateWorldSprite("spark", GameAssets.Pixel, null, color, SortOrder.Effects, Random.Range(2, 4) / Fx.PPU);
                sr.transform.position = new Vector3(pos.x, pos.y, -0.5f);
                sr.transform.rotation = View.Rotation;
                var v = sr.gameObject.AddComponent<VFX>();
                v.sr = sr;
                v.color = color;
                v.velocity = (Vector3)(Random.insideUnitCircle.normalized * Random.Range(1.5f, 4f) * Mathf.Max(0.4f, spread));
                v.life = v.maxLife = Random.Range(0.25f, 0.45f);
                v.startScale = sr.transform.localScale;
                v.growTo = 1f;
            }
        }

        public static void Ring(Vector3 pos, Color color, float radius, float duration)
        {
            var sr = GameAssets.CreateWorldSprite("ring", Fx.Ring(radius), null, color, SortOrder.Effects, radius * 2f);
            sr.transform.position = new Vector3(pos.x, pos.y, Ground.DecalZ * 2f);
            var v = sr.gameObject.AddComponent<VFX>();
            v.sr = sr;
            v.color = color;
            v.life = v.maxLife = duration;
            v.startScale = sr.transform.localScale * 0.3f;
            v.growTo = 3.3f;
        }

        // A drawn swing trail on the ground (see Fx.Slash).
        public static void Slash(Vector3 pos, Vector2 dir, float radius, float arcDeg, Color color) =>
            Fx.Slash(pos, dir, radius, arcDeg, Fx.Element(0, color));

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            float t = 1f - life / maxLife;
            transform.position += velocity * Time.deltaTime;
            velocity *= 1f - 6f * Time.deltaTime;
            transform.localScale = startScale * Mathf.Lerp(1f, growTo, t);
            sr.color = new Color(color.r, color.g, color.b, color.a * (1f - t));
        }
    }
}
