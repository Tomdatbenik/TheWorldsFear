using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // The player's hero: movement, auto-targeted basic attacks, skills, stats from level/items/passives.
    [RequireComponent(typeof(Combatant), typeof(Rigidbody2D))]
    public class HeroController : MonoBehaviour
    {
        public static HeroController Instance { get; private set; }

        public const int SlotCount = 4;
        public const int UltimateSlot = 3;

        [Tooltip("How far the hero walks on its own to reach a target while attack is held.")]
        public float acquireRange = 6f;
        public Transform weapon;
        public AudioSource audioSource;

        public Combatant Combatant { get; private set; }
        public HeroClassDefinition Class { get; private set; }
        public Vector2 Facing { get; private set; } = Vector2.down;
        public Combatant CurrentTarget { get; private set; }

        public event Action<int> SkillCast;
        public event Action Died;

        readonly SkillDefinition[] slotSkills = new SkillDefinition[SlotCount];
        readonly int[] slotRanks = new int[SlotCount];
        readonly float[] cooldowns = new float[SlotCount];
        readonly float[] cooldownTotals = new float[SlotCount];
        readonly List<Action> passiveCleanups = new List<Action>();
        readonly object levelSource = new object();
        readonly object itemSource = new object();

        Rigidbody2D body;
        CharacterAnimator animator;
        SpriteRenderer spriteRenderer;
        float attackTimer;
        float castLock;
        float attackAnimTimer;
        float damageAnimTimer;
        Vector2 moveInput;
        Coroutine swingRoutine;

        public const float PotionCooldown = 6f;
        float potionTimer;
        public float PotionCooldownFraction => Mathf.Clamp01(potionTimer / PotionCooldown);

        public bool UsePotion()
        {
            if (Combatant.IsDead || potionTimer > 0f || Profile.Data.potions <= 0) return false;
            Profile.Data.potions--;
            potionTimer = PotionCooldown;
            Combatant.Heal(Combatant.MaxHP * 0.45f);
            VFX.Burst(transform.position + View.Up * 0.5f, Palette.Heal, 16, 0.6f);
            Sound.Play(Sfx.Potion, transform.position, 0.6f);
            return true;
        }

        public SkillDefinition SkillIn(int slot) => slotSkills[slot];
        public int RankIn(int slot) => slotRanks[slot];
        public float CooldownLeft(int slot) => cooldowns[slot];
        public float CooldownFraction(int slot) => cooldownTotals[slot] > 0 ? cooldowns[slot] / cooldownTotals[slot] : 0f;

        void Awake()
        {
            Instance = this;
            Combatant = GetComponent<Combatant>();
            body = GetComponent<Rigidbody2D>();
            animator = GetComponentInChildren<CharacterAnimator>();
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Combatant.team = Team.Player;
            Combatant.destroyOnDeath = false;
            Combatant.radius = 0.45f;
        }

        void Start()
        {
            Class = Profile.Class;
            Combatant.displayName = Class.displayName;
            var shadow = transform.Find("Shadow");
            if (shadow) shadow.GetComponent<SpriteRenderer>().sprite = GameAssets.SoftCircle;
            var outfit = OutfitFor(Class.id);
            if (animator) animator.SetOutfit(outfit);
            if (weapon)
            {
                weapon.GetComponent<SpriteRenderer>().sprite = CharacterSpriteGenerator.Get(outfit).weapon;
                weapon.gameObject.SetActive(false);
            }

            RebuildStats();
            Combatant.ResetLife();
            RebuildSkills();

            Profile.SkillsChanged += OnSkillsChanged;
            Profile.ItemsChanged += RebuildStats;
            Profile.LeveledUp += OnLevelUp;
            Combatant.Damaged += OnDamaged;
            Combatant.Died += OnDied;
            Combatant.Killed += OnKilled;
        }

        public static Outfit OutfitFor(string classId) =>
            classId == "ranger" ? Outfit.Ranger : classId == "mage" ? Outfit.Mage : Outfit.Warrior;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Profile.SkillsChanged -= OnSkillsChanged;
            Profile.ItemsChanged -= RebuildStats;
            Profile.LeveledUp -= OnLevelUp;
        }

        // ---------- Stats and loadout ----------

        public void RebuildStats()
        {
            float hpFraction = Combatant.MaxHP > 0 ? Combatant.HPFraction : 1f;
            var stats = Combatant.Stats;
            stats.SetBases(Class.baseStats);
            stats.RemoveModifiersFrom(levelSource);
            int lv = Profile.Data.level - 1;
            foreach (var g in Class.growthPerLevel)
                if (lv > 0) stats.AddModifier(new StatModifier(g.stat, ModKind.Flat, g.value * lv, levelSource));

            stats.RemoveModifiersFrom(itemSource);
            foreach (var item in Profile.Data.equipment)
                foreach (var m in item.Modifiers(itemSource)) stats.AddModifier(m);

            if (Combatant.MaxHP > 0 && !Combatant.IsDead) Combatant.SetHP(Combatant.MaxHP * hpFraction);
            ItemPowers.Refresh(this);
        }

        void OnSkillsChanged() => RebuildSkills();

        public void RebuildSkills()
        {
            foreach (var c in passiveCleanups) c();
            passiveCleanups.Clear();

            var tree = Class.skillTree;
            if (tree == null) return;
            foreach (var node in tree.nodes)
            {
                int rank = Profile.RankOf(node.id);
                if (rank > 0 && node.passive != null) passiveCleanups.Add(node.passive.Apply(Combatant, rank));
            }

            for (int i = 0; i < SlotCount; i++)
            {
                var node = tree.Find(Profile.Data.equippedSkills[i]);
                int rank = node != null ? Profile.RankOf(node.id) : 0;
                var skill = node != null && rank > 0 ? node.active : null;
                if (slotSkills[i] != skill) cooldowns[i] = 0f;
                slotSkills[i] = skill;
                slotRanks[i] = rank;
            }
        }

        void OnLevelUp(int level)
        {
            RebuildStats();
            Combatant.Heal(Combatant.MaxHP, false);
            FloatingText.Spawn(transform.position + View.Up * 2f, "LEVEL UP!", Palette.Gold, 1.6f, 1.6f);
            VFX.Ring(transform.position, Palette.Gold, 2.5f, 0.6f);
            VFX.Burst(transform.position, Palette.Gold, 30, 1.5f);
            Sound.Play(Sfx.LevelUp, transform.position, 0.8f);
            Profile.Save();
        }

        void OnKilled(Combatant victim)
        {
            if (victim.team == Team.Player) return;
            Profile.Data.kills++;
            Profile.AddXP(Profile.MonsterXP(victim.level, victim.rank));
        }

        void OnDamaged(DamageInfo info, float dealt)
        {
            damageAnimTimer = 0.15f;
            if (!info.silent) CameraFollow.Shake(Mathf.Clamp(dealt / Combatant.MaxHP * 2f, 0.05f, 0.35f));
        }

        void OnDied(Combatant killer)
        {
            body.linearVelocity = Vector2.zero;
            Died?.Invoke();
        }

        public void Revive(Vector2 at)
        {
            body.position = at;
            transform.position = at;
            Combatant.ResetLife();
            for (int i = 0; i < SlotCount; i++) cooldowns[i] = 0f;
            potionTimer = 0f;
            Profile.Data.potions = Mathf.Max(Profile.Data.potions, 1);
            Combatant.Invulnerable = false;
            StartCoroutine(SpawnProtection());
        }

        IEnumerator SpawnProtection()
        {
            Combatant.Invulnerable = true;
            yield return new WaitForSeconds(2f);
            Combatant.Invulnerable = false;
        }

        // ---------- Update ----------

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < SlotCount; i++)
                if (cooldowns[i] > 0f) cooldowns[i] = Mathf.Max(0f, cooldowns[i] - dt);
            if (attackTimer > 0f) attackTimer -= dt;
            if (potionTimer > 0f) potionTimer -= dt;
            if (castLock > 0f) castLock -= dt;
            if (attackAnimTimer > 0f) attackAnimTimer -= dt;
            if (damageAnimTimer > 0f) damageAnimTimer -= dt;

            if (Combatant.IsDead || Time.timeScale == 0f)
            {
                moveInput = Vector2.zero;
                UpdateAnimator();
                return;
            }

            moveInput = MobaInput.Move;
            HandleKeyboardSkills();
            if (!MobaInput.Blocked && (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.Alpha1))) UsePotion();

            CurrentTarget = Combat.NearestHostile(Team.Player, transform.position, Class.attackRange + 0.2f);
            if (MobaInput.AttackHeld)
            {
                var chase = CurrentTarget ?? Combat.NearestHostile(Team.Player, transform.position, acquireRange);
                if (CurrentTarget != null) TryBasicAttack(CurrentTarget);
                else if (chase != null && moveInput == Vector2.zero)
                    moveInput = ((Vector2)(chase.transform.position - transform.position)).normalized;
                if (CurrentTarget == null) CurrentTarget = chase;
            }

            if (moveInput.sqrMagnitude > 0.01f && castLock <= 0f) Facing = moveInput.normalized;
            UpdateAnimator();
        }

        void FixedUpdate()
        {
            if (Combatant.IsDead || castLock > 0f || Combatant.IsStunned)
            {
                body.linearVelocity = Vector2.zero;
                return;
            }
            Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
            body.linearVelocity = input * Combatant.MoveSpeed;
        }

        void UpdateAnimator()
        {
            if (animator == null) return;
            animator.Velocity = castLock > 0f || Combatant.IsDead ? Vector2.zero : moveInput;
            animator.Facing = Facing;
            animator.Attacking = attackAnimTimer > 0f;
        }

        // ---------- Basic attack ----------

        void TryBasicAttack(Combatant target)
        {
            if (attackTimer > 0f || castLock > 0f || Combatant.IsStunned) return;
            attackTimer = 1f / Combatant.Stats.Get(StatType.AttackSpeed);
            Vector2 dir = ((Vector2)(target.transform.position - transform.position)).normalized;
            Facing = dir;
            attackAnimTimer = 0.2f;

            if (Class.basicAttack == HeroClassDefinition.BasicAttackKind.Melee)
            {
                VFX.Slash(transform.position, dir, Class.attackRange * 0.9f, Class.attackArc, new Color(1f, 1f, 1f, 0.7f));
                foreach (var c in Combat.HostilesInArc(Team.Player, transform.position, dir, Class.attackRange, Class.attackArc))
                    Combat.Hit(Combatant, c, Class.attackDamageMult, default, true, 0.4f);
                Swing(dir);
                Sound.Play(Sfx.Swing, transform.position, 0.5f);
            }
            else
            {
                var spec = new ProjectileSpec
                {
                    speed = Class.projectileSpeed, range = Class.attackRange + 2f, hitRadius = 0.3f,
                    damageMult = Class.attackDamageMult, color = Class.projectileColor,
                    size = Class.useArrowSprite ? 0.9f : 0.4f, pierce = 0,
                    sprite = Class.useArrowSprite ? HeroVisuals.ArrowSprite : null, rotateSprite = true, homing = true,
                };
                Projectile.Fire(Combatant, (Vector2)transform.position + dir * 0.4f, dir, spec, target);
            }
        }

        void Swing(Vector2 dir)
        {
            if (weapon == null || !Class.showSword) return;
            if (swingRoutine != null) StopCoroutine(swingRoutine);
            swingRoutine = StartCoroutine(SwingRoutine(dir));
        }

        IEnumerator SwingRoutine(Vector2 dir)
        {
            weapon.gameObject.SetActive(true);
            float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float t = 0f;
            const float duration = 0.16f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float a = baseAngle + Mathf.Lerp(Class.attackArc / 2f, -Class.attackArc / 2f, t / duration);
                Vector2 off = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                // Swing around the hero at chest height, drawn facing the camera.
                weapon.position = (Vector3)((Vector2)transform.position + off * 0.8f) + View.Up * 0.7f;
                Vector2 onScreen = View.ToScreen(off);
                weapon.rotation = View.Rotation * Quaternion.Euler(0, 0, Mathf.Atan2(onScreen.y, onScreen.x) * Mathf.Rad2Deg - 90f);
                yield return null;
            }
            weapon.gameObject.SetActive(false);
            swingRoutine = null;
        }

        // ---------- Skills ----------

        void HandleKeyboardSkills()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!MobaInput.KeyboardSkillPressed(i)) continue;
                Vector2? aim = MobaInput.MouseAim(transform.position, slotSkills[i] != null ? slotSkills[i].range : 5f);
                if (aim.HasValue) TryCast(i, aim.Value, true);
                else TryCast(i, Vector2.zero, false);
            }
        }

        public bool CanCast(int slot) =>
            slotSkills[slot] != null && cooldowns[slot] <= 0f && !Combatant.IsDead && !Combatant.IsStunned;

        // aim: offset from the hero, magnitude 0..1 of the skill's range. aimed=false = quick cast at the auto-target.
        public bool TryCast(int slot, Vector2 aim, bool aimed)
        {
            if (!CanCast(slot)) return false;
            var skill = slotSkills[slot];
            Vector2 pos = transform.position;
            Vector2 dir;
            Vector2 point;

            if (skill.targeting == Targeting.Self)
            {
                dir = Facing;
                point = pos;
            }
            else if (aimed && aim.sqrMagnitude > 0.0001f)
            {
                dir = aim.normalized;
                point = pos + dir * skill.range * Mathf.Clamp01(aim.magnitude);
            }
            else
            {
                var target = Combat.NearestHostile(Team.Player, pos, skill.range + 1.5f);
                if (target != null)
                {
                    Vector2 to = (Vector2)target.transform.position - pos;
                    dir = to.normalized;
                    point = pos + Vector2.ClampMagnitude(to, skill.range);
                }
                else
                {
                    dir = Facing;
                    point = pos + Facing * skill.range * (skill.targeting == Targeting.Dash ? 1f : 0.6f);
                }
            }

            if (dir.sqrMagnitude < 0.0001f) dir = Facing;
            Facing = dir;

            var ctx = new SkillContext
            {
                caster = Combatant, runner = this, body = body, origin = pos, direction = dir.normalized,
                point = point, rank = slotRanks[slot], power = skill.Power(slotRanks[slot]),
            };
            skill.Cast(ctx);

            cooldownTotals[slot] = skill.Cooldown(slotRanks[slot], Combatant.Stats.Get(StatType.CooldownReduction));
            cooldowns[slot] = cooldownTotals[slot];
            castLock = skill.castLock;
            attackAnimTimer = 0.25f;
            if (skill.targeting == Targeting.Dash) Sound.Play(Sfx.Dash, pos, 0.6f);
            else if (Class.basicAttack == HeroClassDefinition.BasicAttackKind.Melee) Swing(dir);
            SkillCast?.Invoke(slot);
            return true;
        }
    }
}
