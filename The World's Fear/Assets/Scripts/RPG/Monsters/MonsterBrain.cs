using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Monster AI: idle near home, aggro on the hero, attack by archetype, leash back home.
    [RequireComponent(typeof(Combatant), typeof(Rigidbody2D))]
    public class MonsterBrain : MonoBehaviour
    {
        public static event Action<MonsterBrain, Combatant> AnyDied;

        public MonsterSpecies Species { get; private set; }
        public Combatant Combatant { get; private set; }
        public List<MonsterAffix> Affixes { get; } = new List<MonsterAffix>();
        public List<MonsterBrain> Pack { get; set; }
        public Vector2 Home { get; set; }
        public ZoneDefinition Zone { get; set; }

        public float aggroRange = 7.5f;
        public float leashRange = 16f;

        enum State { Idle, Chase, Busy, Return }
        State state = State.Idle;

        Rigidbody2D body;
        SpriteRenderer sr;
        Transform visual;
        Vector2 wanderTarget;
        float wanderTimer;
        float attackTimer;
        float specialTimer;
        float affixTimer, affixTimer2;
        float animTimer;
        int frame;
        Vector2 desired;
        Vector3 baseScale;
        bool busy;
        float strafeSeed;

        Combatant Hero => HeroController.Instance != null ? HeroController.Instance.Combatant : null;

        // ---------- Spawning ----------

        public static MonsterBrain Spawn(MonsterSpecies species, int level, MonsterRank rank, Vector2 position, IList<MonsterAffix> affixes = null, ZoneDefinition zone = null)
        {
            var go = new GameObject(species.name);
            go.transform.position = position;
            go.layer = LayerMask.NameToLayer("Monsters");
            go.tag = "Enemie";

            float rankScale = rank == MonsterRank.Elite ? 1.15f : rank == MonsterRank.Rare ? 1.3f : 1f;
            float scale = species.scale * rankScale;

            var shadow = GameAssets.CreateWorldSprite("Shadow", GameAssets.SoftCircle, go.transform, new Color(0, 0, 0, 0.2f), SortOrder.Units - 1,
                species.look.pixelSize / 16f * 0.9f * scale);
            shadow.transform.localScale = new Vector3(shadow.transform.localScale.x, shadow.transform.localScale.y * 0.45f, 1f);

            var visualGo = new GameObject("Visual");
            visualGo.transform.SetParent(go.transform, false);
            var sr = visualGo.AddComponent<SpriteRenderer>();
            sr.sprite = species.look.frames[0];
            sr.sortingOrder = SortOrder.Units;
            RenderAssets.MakeLit(sr);
            visualGo.transform.localScale = Vector3.one * scale;
            visualGo.AddComponent<Billboard>();

            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.mass = species.archetype == MonsterArchetype.Brute ? 6f : 1f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            var col = go.AddComponent<CircleCollider2D>();
            float radius = species.look.pixelSize / 16f * 0.3f * scale;
            col.radius = radius;
            col.offset = new Vector2(0, radius * 0.6f);

            var c = go.AddComponent<Combatant>();
            c.team = Team.Enemy;
            c.level = level;
            c.rank = rank;
            c.radius = radius + 0.15f;
            c.height = species.look.pixelSize / 16f * scale * 0.75f;
            c.displayName = species.name;

            float hp = MonsterGenerator.BaseHP(level) * species.hpMult;
            float dmg = MonsterGenerator.BaseDamage(level) * species.damageMult;
            if (rank == MonsterRank.Elite) { hp *= 2.5f; dmg *= 1.35f; }
            if (rank == MonsterRank.Rare) { hp *= 4f; dmg *= 1.6f; }
            c.Stats.SetBase(StatType.MaxHP, hp);
            c.Stats.SetBase(StatType.Damage, dmg);
            c.Stats.SetBase(StatType.Armor, MonsterGenerator.BaseArmor(level) * species.armorMult);
            c.Stats.SetBase(StatType.MoveSpeed, species.moveSpeed);
            c.Stats.SetBase(StatType.AttackSpeed, 1f);
            c.Stats.SetBase(StatType.CritChance, 0.05f);

            var brain = go.AddComponent<MonsterBrain>();
            brain.Species = species;
            brain.Home = position;
            brain.Zone = zone;
            brain.visual = visualGo.transform;
            brain.sr = sr;
            brain.baseScale = visualGo.transform.localScale;
            if (affixes != null) brain.Affixes.AddRange(affixes);
            brain.ApplyAffixes();

            c.CacheRenderers();
            c.ResetLife();

            string label = rank == MonsterRank.Normal ? "" : species.name;
            Color labelColor = rank == MonsterRank.Elite ? new Color(0.45f, 0.65f, 1f) : rank == MonsterRank.Rare ? Palette.Gold : Color.white;
            if (rank == MonsterRank.Rare && brain.Affixes.Count > 0)
            {
                var names = new List<string>();
                foreach (var a in brain.Affixes) names.Add(MonsterGenerator.AffixName(a));
                label = species.name + "\n<size=70%><color=#ddd>" + string.Join(" · ", names) + "</color></size>";
            }
            float barY = species.look.pixelSize / 16f * scale * 0.85f + 0.2f;
            float barWidth = rank == MonsterRank.Normal ? 0.9f : 1.4f;
            WorldBar.Attach(c, barY, barWidth, label, labelColor, rank == MonsterRank.Normal ? Palette.HP : new Color(0.9f, 0.35f, 0.1f), false);
            c.CacheRenderers();
            return brain;
        }

        void Awake()
        {
            Combatant = GetComponent<Combatant>();
            body = GetComponent<Rigidbody2D>();
            strafeSeed = UnityEngine.Random.Range(0f, 100f);
        }

        void Start()
        {
            Combatant.Damaged += OnDamaged;
            Combatant.Died += OnDied;
            attackTimer = UnityEngine.Random.Range(0.2f, 0.8f);
            specialTimer = UnityEngine.Random.Range(2f, 4f);
            wanderTarget = Home;
        }

        void ApplyAffixes()
        {
            var c = GetComponent<Combatant>();
            foreach (var a in Affixes)
            {
                switch (a)
                {
                    case MonsterAffix.Fast:
                        c.Stats.AddModifier(new StatModifier(StatType.MoveSpeed, ModKind.Percent, 0.45f, this));
                        break;
                    case MonsterAffix.ExtraHealth:
                        c.Stats.AddModifier(new StatModifier(StatType.MaxHP, ModKind.Percent, 0.6f, this));
                        break;
                    case MonsterAffix.ExtraStrong:
                        c.Stats.AddModifier(new StatModifier(StatType.Damage, ModKind.Percent, 0.35f, this));
                        break;
                    case MonsterAffix.Vampiric:
                        c.Stats.AddModifier(new StatModifier(StatType.LifeSteal, ModKind.Flat, 0.4f, this));
                        break;
                    case MonsterAffix.FrostAura:
                        GameAssets.CreateWorldSprite("FrostAura", GameAssets.Ring, transform, new Color(0.6f, 0.85f, 1f, 0.5f), SortOrder.Telegraph, 7f);
                        break;
                }
            }
        }

        bool Has(MonsterAffix a) => Affixes.Contains(a);

        float CooldownScale => Has(MonsterAffix.Fast) ? 0.7f : 1f;

        // ---------- Events ----------

        void OnDamaged(DamageInfo info, float dealt)
        {
            if (info.source != null && info.source.team == Team.Player) Aggro();
            // Damage over time (burn, poison) ticks quietly; direct hits make a sound.
            if (!info.trueDamage && !info.silent) Sound.Play(Sfx.Hit, transform.position, 0.15f);
        }

        void OnDied(Combatant killer)
        {
            body.linearVelocity = Vector2.zero;
            Sound.Play(Sfx.MonsterDeath, transform.position, 0.35f);
            VFX.Burst(transform.position + View.Up * 0.5f, Species.look.primary, 14, 0.8f);
            if (Has(MonsterAffix.Molten))
            {
                AreaEffect.Spawn(null, transform.position, new AreaSpec
                {
                    radius = 2.2f, delay = 0.9f, damageMult = Combatant.Stats.Get(StatType.Damage) * 1.5f,
                    color = new Color(1f, 0.45f, 0.1f), status = StatusSpec.Burn(0.3f, 2f),
                });
            }
            AnyDied?.Invoke(this, killer);
        }

        public void Aggro()
        {
            if (Combatant.IsDead || state == State.Return) return;
            if (state == State.Idle) state = State.Chase;
            if (Pack == null) return;
            foreach (var m in Pack)
                if (m != null && m != this && m.state == State.Idle) m.state = State.Chase;
        }

        // ---------- Update ----------

        void Update()
        {
            if (Combatant.IsDead) return;
            float dt = Time.deltaTime;
            attackTimer -= dt;
            specialTimer -= dt;
            Animate(dt);

            var hero = Hero;
            Vector2 pos = transform.position;
            bool heroAlive = hero != null && !hero.IsDead;
            float distHero = heroAlive ? Vector2.Distance(pos, hero.transform.position) : float.MaxValue;
            float distHome = Vector2.Distance(pos, Home);

            if (heroAlive && state != State.Return) UpdateAffixes(dt, hero, distHero);

            switch (state)
            {
                case State.Idle:
                    if (heroAlive && distHero < aggroRange) Aggro();
                    Wander(dt, pos);
                    break;

                case State.Chase:
                    if (!heroAlive || distHome > leashRange)
                    {
                        state = State.Return;
                        break;
                    }
                    if (!busy) Fight(hero, pos, distHero);
                    break;

                case State.Return:
                    desired = (Home - pos).normalized;
                    if (distHome < 0.6f)
                    {
                        desired = Vector2.zero;
                        state = State.Idle;
                        Combatant.Heal(Combatant.MaxHP, false);
                    }
                    break;
            }
        }

        void FixedUpdate()
        {
            if (Combatant.IsDead)
            {
                body.linearVelocity = Vector2.zero;
                return;
            }
            if (busy) return;
            float speed = Combatant.MoveSpeed * (state == State.Return ? 1.5f : state == State.Idle ? 0.4f : 1f);
            body.linearVelocity = desired * speed;
        }

        void Wander(float dt, Vector2 pos)
        {
            wanderTimer -= dt;
            if (wanderTimer <= 0f)
            {
                wanderTimer = UnityEngine.Random.Range(1.5f, 4f);
                wanderTarget = UnityEngine.Random.value < 0.4f ? pos : Home + UnityEngine.Random.insideUnitCircle * 2.5f;
            }
            Vector2 to = wanderTarget - pos;
            desired = to.magnitude > 0.3f ? to.normalized : Vector2.zero;
        }

        void Fight(Combatant hero, Vector2 pos, float dist)
        {
            Vector2 toHero = ((Vector2)hero.transform.position - pos).normalized;
            var sp = Species;

            // Movement: ranged keep their distance, melee close in.
            if (sp.preferredDistance > 0f)
            {
                if (dist > sp.attackRange * 0.9f) desired = toHero;
                else if (dist < sp.preferredDistance - 1f) desired = -toHero;
                else desired = new Vector2(-toHero.y, toHero.x) * Mathf.Sign(Mathf.Sin(Time.time * 0.7f + strafeSeed));
            }
            else desired = dist > sp.attackRange * 0.85f ? toHero : Vector2.zero;

            switch (sp.archetype)
            {
                case MonsterArchetype.Charger:
                    if (specialTimer <= 0f && dist > 3f && dist < 9f)
                    {
                        specialTimer = UnityEngine.Random.Range(5f, 7f) * CooldownScale;
                        StartCoroutine(ChargeAttack(hero));
                        return;
                    }
                    if (dist <= sp.attackRange && attackTimer <= 0f) StartCoroutine(MeleeAttack(hero));
                    break;

                case MonsterArchetype.Brute:
                    if (dist <= sp.attackRange + 1f && attackTimer <= 0f) StartCoroutine(SlamAttack(hero));
                    break;

                case MonsterArchetype.Spitter:
                    if (dist <= sp.attackRange && attackTimer <= 0f) StartCoroutine(SpitAttack(hero));
                    break;

                case MonsterArchetype.Caster:
                    if (dist <= sp.attackRange && attackTimer <= 0f) StartCoroutine(CastAttack(hero));
                    break;

                case MonsterArchetype.Exploder:
                    if (dist <= sp.attackRange) StartCoroutine(Explode());
                    break;

                default:
                    if (dist <= sp.attackRange && attackTimer <= 0f) StartCoroutine(MeleeAttack(hero));
                    break;
            }

            if (Has(MonsterAffix.Multishot) && specialTimer <= 0f && sp.archetype != MonsterArchetype.Charger)
            {
                specialTimer = 4f;
                FireVolley(toHero, 3, 30f, 0.8f);
            }
        }

        // ---------- Attacks ----------

        IEnumerator Windup(float duration, Color tint)
        {
            busy = true;
            body.linearVelocity = Vector2.zero;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = t / duration;
                visual.localScale = new Vector3(baseScale.x * (1f + k * 0.15f), baseScale.y * (1f - k * 0.15f), 1f);
                sr.color = Color.Lerp(Color.white, tint, k * 0.6f);
                yield return null;
                if (Combatant.IsDead || Combatant.IsStunned) break;
            }
            visual.localScale = baseScale;
            sr.color = Color.white;
        }

        IEnumerator MeleeAttack(Combatant hero)
        {
            attackTimer = Species.attackCooldown * CooldownScale;
            yield return Windup(0.25f, new Color(1f, 0.6f, 0.5f));
            if (!Combatant.IsDead && !Combatant.IsStunned && hero != null &&
                Vector2.Distance(transform.position, hero.transform.position) <= Species.attackRange + 0.45f)
            {
                Vector2 dir = (hero.transform.position - transform.position).normalized;
                StartCoroutine(Lunge(dir));
                Combat.Hit(Combatant, hero, 1f, Species.onHitStatus);
            }
            busy = false;
        }

        IEnumerator Lunge(Vector2 dir)
        {
            float t = 0f;
            while (t < 0.12f)
            {
                t += Time.deltaTime;
                visual.localPosition = dir * Mathf.Sin(t / 0.12f * Mathf.PI) * 0.25f;
                yield return null;
            }
            visual.localPosition = Vector3.zero;
        }

        IEnumerator ChargeAttack(Combatant hero)
        {
            busy = true;
            body.linearVelocity = Vector2.zero;
            Vector2 start = transform.position;
            Vector2 dir = ((Vector2)hero.transform.position - start).normalized;
            const float length = 7.5f;
            LineTelegraph.Show(start, dir, length, 1.1f, Palette.EnemyTelegraph, 0.7f);
            yield return Windup(0.7f, new Color(1f, 0.4f, 0.3f));
            if (Combatant.IsDead || Combatant.IsStunned)
            {
                busy = false;
                yield break;
            }

            bool hit = false;
            float t = 0f;
            const float duration = 0.35f;
            while (t < duration && !Combatant.IsDead)
            {
                t += Time.fixedDeltaTime;
                body.linearVelocity = dir * length / duration;
                if (!hit && hero != null && Vector2.Distance(transform.position, hero.transform.position) < Combatant.radius + hero.radius + 0.3f)
                {
                    hit = true;
                    Combat.Hit(Combatant, hero, 1.6f, StatusSpec.Stun(0.6f), false, 2f);
                }
                yield return new WaitForFixedUpdate();
            }
            body.linearVelocity = Vector2.zero;
            attackTimer = 0.8f;
            busy = false;
        }

        IEnumerator SlamAttack(Combatant hero)
        {
            attackTimer = Species.attackCooldown * CooldownScale;
            Vector2 dir = ((Vector2)hero.transform.position - (Vector2)transform.position).normalized;
            Vector2 center = (Vector2)transform.position + dir * 1.2f;
            AreaEffect.Spawn(Combatant, center, new AreaSpec
            {
                radius = 2.1f, delay = 0.85f, damageMult = 1f, color = new Color(1f, 0.3f, 0.2f), knockback = 2f,
            });
            yield return Windup(0.85f, new Color(1f, 0.5f, 0.4f));
            CameraFollow.Shake(0.15f);
            busy = false;
        }

        IEnumerator SpitAttack(Combatant hero)
        {
            attackTimer = Species.attackCooldown * CooldownScale;
            yield return Windup(0.3f, Species.projectileColor);
            if (!Combatant.IsDead && hero != null)
            {
                Vector2 dir = ((Vector2)hero.transform.position - (Vector2)transform.position).normalized;
                FireVolley(dir, Has(MonsterAffix.Multishot) ? 3 : 1, 25f, 1f);
            }
            busy = false;
        }

        void FireVolley(Vector2 dir, int count, float spread, float mult)
        {
            float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            for (int i = 0; i < count; i++)
            {
                float a = count == 1 ? baseAngle : baseAngle - spread / 2f + spread * i / (count - 1);
                var d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                Projectile.Fire(Combatant, (Vector2)transform.position + d * 0.4f, d, new ProjectileSpec
                {
                    speed = Species.projectileSpeed, range = Species.attackRange + 2f, hitRadius = 0.3f, damageMult = mult,
                    color = Species.projectileColor, size = 0.45f, status = Species.onHitStatus,
                });
            }
        }

        IEnumerator CastAttack(Combatant hero)
        {
            attackTimer = Species.attackCooldown * CooldownScale;
            Vector2 target = (Vector2)hero.transform.position + hero.GetComponent<Rigidbody2D>().linearVelocity * 0.4f;
            AreaEffect.Spawn(Combatant, target, new AreaSpec
            {
                radius = 1.8f, delay = 1.1f, damageMult = 1f, color = Species.projectileColor, status = Species.onHitStatus,
            });
            yield return Windup(0.5f, Species.projectileColor);
            busy = false;
        }

        IEnumerator Explode()
        {
            busy = true;
            body.linearVelocity = Vector2.zero;
            AreaEffect.Spawn(Combatant, transform.position, new AreaSpec
            {
                radius = 2.3f, delay = 0.7f, damageMult = 1f, color = new Color(1f, 0.55f, 0.15f), knockback = 2.5f, followOwner = true,
            });
            float t = 0f;
            while (t < 0.7f && !Combatant.IsDead)
            {
                t += Time.deltaTime;
                sr.color = Mathf.Repeat(t * 10f, 1f) > 0.5f ? Color.white : new Color(1f, 0.4f, 0.2f);
                visual.localScale = baseScale * (1f + t * 0.4f);
                yield return null;
            }
            if (!Combatant.IsDead)
            {
                VFX.Burst(transform.position, new Color(1f, 0.6f, 0.2f), 25, 2f);
                CameraFollow.Shake(0.2f);
                Combatant.Die(null);
            }
        }

        // ---------- Rare affixes ----------

        void UpdateAffixes(float dt, Combatant hero, float distHero)
        {
            if (Affixes.Count == 0 || state != State.Chase) return;
            affixTimer -= dt;
            affixTimer2 -= dt;

            if (Has(MonsterAffix.FrostAura) && affixTimer <= 0f)
            {
                affixTimer = 0.5f;
                if (distHero < 3.5f) hero.ApplyStatus(StatusSpec.Slow(0.35f, 0.7f), Combatant, 0f);
            }
            if (Has(MonsterAffix.Shielded) && affixTimer2 <= 0f)
            {
                affixTimer2 = 7f;
                Combatant.AddShield(Combatant.MaxHP * 0.35f, 3f);
                VFX.Ring(transform.position, Palette.Shield, 1.2f, 0.4f);
            }
            if (Has(MonsterAffix.Teleporter) && !busy && distHero > 3f && UnityEngine.Random.value < dt / 6f)
            {
                VFX.Burst(transform.position, new Color(0.7f, 0.4f, 1f), 12, 0.8f);
                Vector2 to = (Vector2)hero.transform.position + UnityEngine.Random.insideUnitCircle.normalized * 1.8f;
                body.position = to;
                transform.position = to;
                VFX.Burst(to, new Color(0.7f, 0.4f, 1f), 12, 0.8f);
            }
        }

        // ---------- Animation ----------

        void Animate(float dt)
        {
            // Show the monster's back when it moves away from the camera.
            Vector2 move = body.linearVelocity;
            Vector2 onScreen = View.ToScreen(move);
            bool away = move.sqrMagnitude > 0.05f && onScreen.y > Mathf.Abs(onScreen.x) * 0.6f;
            var frames = away && Species.look.backFrames != null ? Species.look.backFrames : Species.look.frames;
            if (frames.Length > 1)
            {
                float speed = body.linearVelocity.sqrMagnitude > 0.05f ? 0.18f : 0.45f;
                animTimer += dt;
                if (animTimer >= speed)
                {
                    animTimer = 0f;
                    frame = (frame + 1) % frames.Length;
                }
            }
            sr.sprite = frames[frame % frames.Length];
            if (Mathf.Abs(onScreen.x) > 0.1f) sr.flipX = onScreen.x < 0f;
        }
    }
}
