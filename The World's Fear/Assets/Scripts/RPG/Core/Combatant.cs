using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Anything that has life and can be hit: the hero, monsters, the boss.
    [DisallowMultipleComponent]
    public class Combatant : MonoBehaviour
    {
        public static readonly List<Combatant> All = new List<Combatant>();

        public Team team = Team.Enemy;
        public string displayName = "";
        public int level = 1;
        public MonsterRank rank = MonsterRank.Normal;
        [Tooltip("Hit radius in world units.")]
        public float radius = 0.5f;
        public bool destroyOnDeath = true;
        public bool showDamageNumbers = true;
        [Tooltip("How tall the character stands, for text and bars above it.")]
        public float height = 1.6f;
        public float HeadHeight => height + 0.2f;

        [Header("Legacy hitboxes (old boss attacks)")]
        [Tooltip("Old 'Damage' components deal Strength x this to the hero.")]
        public float legacyDamageScale = 3f;
        public float legacyHitCooldown = 0.6f;

        public CharacterStats Stats { get; } = new CharacterStats();
        public float HP { get; private set; }
        public float ShieldHP { get; private set; }
        public float MaxHP => Stats.Get(StatType.MaxHP);
        public float HPFraction => MaxHP > 0 ? Mathf.Clamp01(HP / MaxHP) : 0f;
        public bool IsDead { get; private set; }
        public bool Invulnerable { get; set; }
        public bool Targetable { get; set; } = true;
        public bool IsStunned => stunTimer > 0f;
        public float SlowFactor { get; private set; } = 1f;
        public float MoveSpeed => IsStunned ? 0f : Stats.Get(StatType.MoveSpeed) * SlowFactor;

        public event Action<DamageInfo, float> Damaged;         // on the victim
        public event Action<Combatant, DamageInfo, float> DealtDamage; // on the attacker
        public event Action<Combatant> Died;                    // killer (may be null)
        public event Action<Combatant> Killed;                  // on the attacker: victim
        public event Action<float> Healed;

        class ActiveStatus
        {
            public StatusType type;
            public float magnitude;
            public float remaining;
            public float damagePerSecond;
            public Combatant source;
        }

        readonly List<ActiveStatus> statuses = new List<ActiveStatus>();
        float stunTimer;
        float shieldTimer;
        float legacyTimer;
        float regenAccumulator;
        SpriteRenderer[] renderers;
        Color[] baseColors;
        Coroutine flashRoutine;
        Rigidbody2D body;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            CacheRenderers();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void CacheRenderers()
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;
        }

        public void SetBaseColor(SpriteRenderer sr, Color c)
        {
            sr.color = c;
            if (renderers == null) return;
            int i = Array.IndexOf(renderers, sr);
            if (i >= 0) baseColors[i] = c;
        }

        // Call after setting stats to start at full life.
        public void ResetLife()
        {
            IsDead = false;
            HP = MaxHP;
            statuses.Clear();
            stunTimer = 0f;
            SlowFactor = 1f;
        }

        public void SetHP(float hp) => HP = Mathf.Clamp(hp, 0f, MaxHP);

        void Update()
        {
            if (IsDead) return;
            float dt = Time.deltaTime;

            if (legacyTimer > 0f) legacyTimer -= dt;
            if (stunTimer > 0f) stunTimer -= dt;
            if (shieldTimer > 0f)
            {
                shieldTimer -= dt;
                if (shieldTimer <= 0f) ShieldHP = 0f;
            }

            float regen = Stats.Get(StatType.HPRegen);
            if (regen > 0f && HP < MaxHP)
            {
                regenAccumulator += regen * dt;
                if (regenAccumulator >= 1f)
                {
                    HP = Mathf.Min(MaxHP, HP + regenAccumulator);
                    regenAccumulator = 0f;
                }
            }

            UpdateStatuses(dt);
        }

        void UpdateStatuses(float dt)
        {
            float slow = 0f;
            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                var s = statuses[i];
                s.remaining -= dt;
                if (s.type == StatusType.Slow || s.type == StatusType.Chill) slow = Mathf.Max(slow, s.magnitude);
                if (s.damagePerSecond > 0f)
                {
                    var info = new DamageInfo { source = s.source, amount = s.damagePerSecond * dt, trueDamage = true, silent = true };
                    TakeDamage(info);
                    if (IsDead) return;
                }
                if (s.remaining <= 0f) statuses.RemoveAt(i);
            }
            SlowFactor = 1f - Mathf.Clamp(slow, 0f, 0.8f);
        }

        public bool HasStatus(StatusType type) => statuses.Exists(s => s.type == type);

        public void ApplyStatus(StatusSpec spec, Combatant source, float hitDamage)
        {
            if (!spec.IsSet || IsDead) return;
            if (spec.chance > 0f && spec.chance < 1f && UnityEngine.Random.value > spec.chance) return;

            float duration = spec.duration;
            if (rank == MonsterRank.Boss) duration *= 0.5f; // bosses shrug off crowd control faster

            if (spec.type == StatusType.Stun)
            {
                stunTimer = Mathf.Max(stunTimer, duration);
                FloatingText.Spawn(transform.position + View.Up * (HeadHeight + 0.4f), "Stunned", new Color(1f, 0.9f, 0.4f), 0.8f);
                return;
            }

            var existing = statuses.Find(s => s.type == spec.type);
            float dps = (spec.type == StatusType.Burn || spec.type == StatusType.Poison) ? hitDamage * spec.magnitude : 0f;
            if (existing != null)
            {
                existing.remaining = Mathf.Max(existing.remaining, duration);
                existing.magnitude = Mathf.Max(existing.magnitude, spec.magnitude);
                existing.damagePerSecond = Mathf.Max(existing.damagePerSecond, dps);
                return;
            }
            statuses.Add(new ActiveStatus { type = spec.type, magnitude = spec.magnitude, remaining = duration, damagePerSecond = dps, source = source });
        }

        public void AddShield(float amount, float duration)
        {
            ShieldHP = Mathf.Max(ShieldHP, amount);
            shieldTimer = Mathf.Max(shieldTimer, duration);
        }

        public void Stun(float duration) => ApplyStatus(StatusSpec.Stun(duration), null, 0f);

        public float TakeDamage(DamageInfo info)
        {
            if (IsDead || Invulnerable || !isActiveAndEnabled) return 0f;

            float amount = info.trueDamage ? info.amount : Combat.Mitigate(info.amount, Stats.Get(StatType.Armor));
            amount = Mathf.Max(info.amount > 0f ? 1f : 0f, amount);
            if (info.silent) amount = info.amount;

            if (ShieldHP > 0f)
            {
                float absorbed = Mathf.Min(ShieldHP, amount);
                ShieldHP -= absorbed;
                amount -= absorbed;
            }

            HP -= amount;

            if (!info.silent)
            {
                if (showDamageNumbers)
                    FloatingText.Damage(transform.position + View.Up * HeadHeight, amount, info.isCrit, team == Team.Player);
                Flash(team == Team.Player ? new Color(1f, 0.35f, 0.35f) : Color.white);
            }

            if (info.knockback != Vector2.zero && body != null && rank != MonsterRank.Boss)
                StartCoroutine(Knockback(info.knockback));

            if (info.source != null && info.source != this)
            {
                float steal = info.source.Stats.Get(StatType.LifeSteal);
                if (steal > 0f && !info.silent) info.source.Heal(amount * steal, false);
                info.source.DealtDamage?.Invoke(this, info, amount);
            }

            if (info.status.IsSet) ApplyStatus(info.status, info.source, info.amount);

            Damaged?.Invoke(info, amount);

            if (HP <= 0f) Die(info.source);
            return amount;
        }

        public void Heal(float amount, bool showText = true)
        {
            if (IsDead || amount <= 0f) return;
            float before = HP;
            HP = Mathf.Min(MaxHP, HP + amount);
            float healed = HP - before;
            if (showText && healed >= 1f)
                FloatingText.Spawn(transform.position + View.Up * HeadHeight, "+" + Mathf.RoundToInt(healed), Palette.Heal, 0.9f);
            Healed?.Invoke(healed);
        }

        public void Die(Combatant killer)
        {
            if (IsDead) return;
            IsDead = true;
            HP = 0f;
            statuses.Clear();
            Died?.Invoke(killer);
            if (killer != null) killer.Killed?.Invoke(this);
            if (destroyOnDeath) StartCoroutine(DeathFade());
        }

        IEnumerator DeathFade()
        {
            foreach (var col in GetComponentsInChildren<Collider2D>()) col.enabled = false;
            float t = 0f;
            Vector3 startScale = transform.localScale;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float k = 1f - t / 0.35f;
                transform.localScale = new Vector3(startScale.x * (1f + (1f - k) * 0.3f), startScale.y * k, startScale.z);
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i]) renderers[i].color = new Color(baseColors[i].r, baseColors[i].g, baseColors[i].b, baseColors[i].a * k);
                yield return null;
            }
            Destroy(gameObject);
        }

        IEnumerator Knockback(Vector2 force)
        {
            float t = 0f;
            while (t < 0.12f && body != null)
            {
                t += Time.fixedDeltaTime;
                body.MovePosition(body.position + force * Time.fixedDeltaTime / 0.12f);
                yield return new WaitForFixedUpdate();
            }
        }

        public void Flash(Color c)
        {
            if (!isActiveAndEnabled || renderers == null) return;
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine(c));
        }

        IEnumerator FlashRoutine(Color c)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] && !renderers[i].CompareTag("EditorOnly") && renderers[i].sortingOrder < SortOrder.Bars)
                    renderers[i].color = Color.Lerp(baseColors[i], c, 0.75f);
            yield return new WaitForSeconds(0.08f);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] && renderers[i].sortingOrder < SortOrder.Bars) renderers[i].color = baseColors[i];
            flashRoutine = null;
        }

        // Old attacks (the boss's fists, fire lines, risk spots) are trigger colliders
        // tagged "Damage"/"Enemie" carrying a legacy Damage component.
        void OnTriggerStay2D(Collider2D other) => LegacyHit(other);
        void OnTriggerEnter2D(Collider2D other) => LegacyHit(other);

        void LegacyHit(Collider2D other)
        {
            if (team != Team.Player || legacyTimer > 0f || IsDead) return;
            if (!other.CompareTag("Damage") && !other.CompareTag("Enemie")) return;
            var legacy = other.GetComponent<global::Damage>();
            if (legacy == null) return;
            legacyTimer = legacyHitCooldown;
            TakeDamage(new DamageInfo { amount = legacy.GetStrength() * legacyDamageScale });
        }
    }
}
