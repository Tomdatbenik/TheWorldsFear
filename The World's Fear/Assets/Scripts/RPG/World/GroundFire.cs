using UnityEngine;

namespace TWF
{
    // A patch of fire (or a poison pool) left on the ground by a boss. It lasts a while and
    // hurts the hero while they stand in it.
    public class GroundFire : MonoBehaviour
    {
        Combatant owner;
        float life, age, radius, nextTick;
        Transform flames;
        Vector3 baseScale;
        Light glow;
        float seed;
        SpriteRenderer pool;
        Color poolColor;
        StatusSpec status = StatusSpec.Burn(0.2f, 1.5f);

        public static GroundFire SpawnPool(Combatant owner, Vector2 at, Color color, float radius, StatusSpec status, float duration = -1f)
        {
            var gf = Spawn(owner, at, null, radius, duration > 0f ? duration : Random.Range(8f, 11f));
            gf.status = status;
            gf.poolColor = new Color(color.r, color.g, color.b, 0.55f);
            gf.pool = GameAssets.CreateWorldSprite("Pool", GameAssets.SoftCircle, gf.transform, gf.poolColor, SortOrder.Ground + 5, radius * 2.3f);
            gf.pool.transform.localPosition = new Vector3(0, 0, Ground.DecalZ);
            if (gf.glow != null) { gf.glow.color = color; gf.glow.range = radius * 2.5f; }
            return gf;
        }

        public static GroundFire Spawn(Combatant owner, Vector2 at, Sprite sprite, float radius = 1.1f, float duration = -1f)
        {
            var go = new GameObject("GroundFire");
            go.transform.position = at;
            var gf = go.AddComponent<GroundFire>();
            gf.owner = owner;
            gf.radius = radius;
            gf.life = duration > 0f ? duration : Random.Range(10f, 15f);
            gf.seed = Random.value * 10f;
            gf.nextTick = 0.4f;

            var bb = Billboard.Create(go.transform);
            if (sprite != null)
            {
                var f = new GameObject("Flames");
                f.transform.SetParent(bb, false);
                float scale = radius * 1.4f / Mathf.Max(0.01f, sprite.bounds.size.x);
                f.transform.localScale = Vector3.one * scale;
                f.transform.localPosition = new Vector3(0, sprite.bounds.extents.y * scale - sprite.bounds.center.y * scale, 0);
                var r = f.AddComponent<SpriteRenderer>();
                r.sprite = sprite;
                RenderAssets.MakeLit(r, false, RenderAssets.Instance.litEmissive);
                gf.flames = f.transform;
                gf.baseScale = f.transform.localScale;
            }
            gf.glow = PointLights.Attach(go.transform, new Color(1f, 0.5f, 0.2f), 3.5f, 1.6f, new Vector3(0, 0, -0.6f));
            return gf;
        }

        Vector3 poolScale;
        Vector3 baseScaleOf(SpriteRenderer r)
        {
            if (poolScale == Vector3.zero) poolScale = r.transform.localScale;
            return poolScale;
        }

        void Update()
        {
            age += Time.deltaTime;
            // Grow in, flicker, then die down over the last second.
            float k = Mathf.Clamp01(age / 0.3f) * Mathf.Clamp01(life - age);
            float flick = 1f + Mathf.Sin((age + seed) * 13f) * 0.08f + Mathf.Sin((age + seed) * 21f) * 0.05f;
            if (flames != null) flames.localScale = new Vector3(baseScale.x * k, baseScale.y * k * flick, baseScale.z);
            if (glow != null) glow.intensity = (pool != null ? 0.8f : 1.6f) * k * flick;
            if (pool != null)
            {
                float bubble = 1f + Mathf.Sin((age + seed) * 4f) * 0.04f;
                pool.transform.localScale = baseScaleOf(pool) * Mathf.Clamp01(age / 0.3f) * bubble;
                pool.color = new Color(poolColor.r, poolColor.g, poolColor.b, poolColor.a * Mathf.Clamp01(life - age));
                if (Random.value < Time.deltaTime * 3f) VFX.Burst((Vector2)transform.position + Random.insideUnitCircle * radius * 0.7f, poolColor, 2, 0.3f);
            }

            if (owner == null || owner.IsDead) { life = Mathf.Min(life, age + 1f); }
            else if (age >= nextTick)
            {
                nextTick = age + 0.5f;
                var hero = HeroController.Instance != null ? HeroController.Instance.Combatant : null;
                if (hero != null && !hero.IsDead && Vector2.Distance(hero.transform.position, transform.position) < radius)
                    Combat.Hit(owner, hero, 0.18f, status);
            }
            if (age >= life) Destroy(gameObject);
        }
    }
}
