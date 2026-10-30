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

        public const int SlotCount = 7;          // 4 crafted skills + the weapon skill + 2 from unique armor sockets
        public const int WeaponSlot = 4;
        public const int FirstSocketSlot = 5;

        // Socketed skills set to auto-cast, each with its own cooldown.
        readonly List<SkillDefinition> autoSkills = new List<SkillDefinition>();
        readonly List<float> autoCooldowns = new List<float>();
        readonly string[] socketIds = new string[2];
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
        readonly object attributeSource = new object();
        readonly object weaponSource = new object();
        WeaponProfile weaponProfile;

        public WeaponProfile Weapon => weaponProfile ?? Weapons.Get(Weapons.Equipped);

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
            // HD-2D: lit, shadow-casting sprites; a faint contact shadow grounds the feet.
            var shadow = transform.Find("Shadow");
            if (shadow)
            {
                var ssr = shadow.GetComponent<SpriteRenderer>();
                ssr.sprite = GameAssets.SoftCircle;
                ssr.color = new Color(0, 0, 0, 0.22f);
                RenderAssets.MakeUnlit(ssr);
                shadow.position = Ground.Decal(shadow.position);
            }
            RenderAssets.MakeLit(spriteRenderer);
            if (weapon) RenderAssets.MakeLit(weapon.GetComponent<SpriteRenderer>());
            if (animator) animator.SetOutfit(Outfit.Adventurer);
            if (weapon) weapon.gameObject.SetActive(false);

            RebuildStats();
            Combatant.ResetLife();
            RebuildSkills();

            Profile.SkillsChanged += OnSkillsChanged;
            Profile.ItemsChanged += RebuildStats;
            Profile.ItemsChanged += RebuildSkills;   // unique armor sockets add skills
            Profile.LeveledUp += OnLevelUp;
            Combatant.Damaged += OnDamaged;
            Combatant.Died += OnDied;
            Combatant.Killed += OnKilled;
        }


        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Profile.SkillsChanged -= OnSkillsChanged;
            Profile.ItemsChanged -= RebuildStats;
            Profile.LeveledUp -= OnLevelUp;
        }

        // ---------- Stats and loadout ----------

        // The hero's sprite shows the helm, chest armor, gloves and boots being worn.
        void DressInGear()
        {
            if (!animator) return;
            var g = new CharacterSpriteGenerator.Gear { helm = -1, chest = -1, gloves = -1, boots = -1 };
            foreach (var item in Profile.Data.equipment)
            {
                if (item == null) continue;
                int r = (int)item.rarity;
                switch (item.slot)
                {
                    case ItemSlot.Helm: g.helm = r; break;
                    case ItemSlot.Chest: g.chest = r; break;
                    case ItemSlot.Gloves: g.gloves = r; break;
                    case ItemSlot.Boots: g.boots = r; break;
                }
            }
            animator.SetSprites(CharacterSpriteGenerator.GetGeared(g));
        }

        public void RebuildStats()
        {
            float hpFraction = Combatant.MaxHP > 0 ? Combatant.HPFraction : 1f;
            var stats = Combatant.Stats;
            Combatant.level = Profile.Data.level;   // the level gap with monsters affects damage both ways
            stats.SetBases(Class.baseStats);
            stats.RemoveModifiersFrom(levelSource);
            int lv = Profile.Data.level - 1;
            foreach (var g in Class.growthPerLevel)
                if (lv > 0) stats.AddModifier(new StatModifier(g.stat, ModKind.Flat, g.value * lv, levelSource));

            stats.RemoveModifiersFrom(itemSource);
            foreach (var item in Profile.Data.equipment)
                foreach (var m in item.Modifiers(itemSource)) stats.AddModifier(m);

            stats.RemoveModifiersFrom(attributeSource);
            for (int i = 0; i < Attributes.Count; i++)
                foreach (var m in Attributes.Modifiers((Attribute)i, Profile.Data.attributes[i], attributeSource)) stats.AddModifier(m);

            // The equipped weapon sets the attack rhythm and adds its crit bonus.
            weaponProfile = Weapons.Get(Weapons.Equipped);
            stats.RemoveModifiersFrom(weaponSource);
            if (weaponProfile.critBonus > 0f) stats.AddModifier(new StatModifier(StatType.CritChance, ModKind.Flat, weaponProfile.critBonus, weaponSource));
            if (weapon) weapon.GetComponent<SpriteRenderer>().sprite = Weapons.Sprite(weaponProfile.type);

            if (Combatant.MaxHP > 0 && !Combatant.IsDead) Combatant.SetHP(Combatant.MaxHP * hpFraction);
            DressInGear();
            ItemPowers.Refresh(this);
        }

        void OnSkillsChanged() => RebuildSkills();

        public void RebuildSkills()
        {
            foreach (var c in passiveCleanups) c();
            passiveCleanups.Clear();

            var tree = Class.skillTree;
            if (tree != null)
                foreach (var node in tree.nodes)
                {
                    int rank = Profile.RankOf(node.id);
                    if (rank > 0 && node.passive != null) passiveCleanups.Add(node.passive.Apply(Combatant, rank));
                }

            // Active skills are all crafted.
            Crafting.ClearCache();
            // Unique armor sockets: up to two extra buttons, the rest auto-cast.
            socketIds[0] = socketIds[1] = null;
            autoSkills.Clear();
            autoCooldowns.Clear();
            int buttons = 0;
            foreach (var item in Profile.Data.equipment)
            {
                if (item == null || !item.socket || string.IsNullOrEmpty(item.socketSkill)) continue;
                var sk = Crafting.Get(item.socketSkill);
                if (sk == null) continue;
                if (item.socketAuto || buttons >= 2) { autoSkills.Add(sk); autoCooldowns.Add(1f); }
                else socketIds[buttons++] = item.socketSkill;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                var skill = i == WeaponSlot ? WeaponSkills.Current()
                    : i >= FirstSocketSlot ? Crafting.Get(socketIds[i - FirstSocketSlot])
                    : Crafting.Get(Profile.Data.equippedSkills[i]);
                if (slotSkills[i] != skill) cooldowns[i] = 0f;
                slotSkills[i] = skill;
                slotRanks[i] = skill != null ? 1 : 0;
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
            UpdateAutoCasts(dt);
            // The weapon skill follows whatever weapon is in your hands.
            var weaponSkill = WeaponSkills.Current();
            if (slotSkills[WeaponSlot] != weaponSkill)
            {
                slotSkills[WeaponSlot] = weaponSkill;
                slotRanks[WeaponSlot] = weaponSkill != null ? 1 : 0;
                cooldowns[WeaponSlot] = 0f;
            }
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

            CurrentTarget = Combat.NearestHostile(Team.Player, transform.position, Weapon.range + 0.2f);
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

        // The equipped weapon decides how the basic attack works.
        void TryBasicAttack(Combatant target)
        {
            if (attackTimer > 0f || castLock > 0f || Combatant.IsStunned) return;
            var w = Weapon;
            attackTimer = 1f / (w.attacksPerSecond * Combatant.Stats.Get(StatType.AttackSpeed));
            Vector2 dir = ((Vector2)(target.transform.position - transform.position)).normalized;
            Facing = dir;
            attackAnimTimer = 0.2f;

            if (!w.ranged)
            {
                StartCoroutine(MeleeAttack(w, target, dir));
                return;
            }

            var spec = new ProjectileSpec
            {
                speed = w.projectileSpeed * (1f + Combatant.Stats.Get(StatType.ProjectileSpeed)), range = w.range + 2f, hitRadius = 0.3f,
                damageMult = w.damageMult, color = w.color, size = w.projectileSize, pierce = w.pierce, explodeRadius = w.explodeRadius,
                sprite = w.arrow ? HeroVisuals.ArrowSprite : null, rotateSprite = true, homing = w.homing, status = WeaponStatus(w),
                elements = Elements.Mask(Weapons.EquippedElement),
            };
            if (Weapons.EquippedElement != ElementPart.Physical && !w.arrow) spec.color = Elements.Color(Weapons.EquippedElement);
            Projectile.Fire(Combatant, (Vector2)transform.position + dir * 0.4f, dir, spec, w.homing ? target : null);
            int extra = Mathf.RoundToInt(Combatant.Stats.Get(StatType.ExtraProjectiles));
            for (int k = 1; k <= extra; k++)
            {
                float a = (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 14f * (1f + Combatant.Stats.Get(StatType.ProjectileSpread));
                var d2 = (Vector2)(Quaternion.Euler(0, 0, a) * dir);
                var s2 = spec;
                s2.homing = false;
                Projectile.Fire(Combatant, (Vector2)transform.position + d2 * 0.4f, d2, s2);
            }
            Shoot(dir, w.type == WeaponType.Bow || w.type == WeaponType.Crossbow);
            Sound.Play(Sfx.Swing, transform.position, 0.3f);
        }

        // An elemental weapon sometimes applies its element's effect (burn, chill, ...).
        static StatusSpec WeaponStatus(WeaponProfile w)
        {
            var e = Weapons.EquippedElement;
            if (e == ElementPart.Physical || w.status.IsSet) return w.status;
            var st = SkillComposer.ElementStatus(e);
            if (st.IsSet) st.chance = st.type == StatusType.Stun ? st.chance : 0.25f;
            return st;
        }

        IEnumerator MeleeAttack(WeaponProfile w, Combatant target, Vector2 dir)
        {
            for (int h = 0; h < w.hits; h++)
            {
                if (w.arc >= 360f)
                {
                    // Hammer: slam a circle at the target.
                    Vector2 at = (Vector2)transform.position + dir * Mathf.Min(w.range * 0.7f, Vector2.Distance(transform.position, target.transform.position));
                    VFX.Ring(at, new Color(1f, 0.9f, 0.7f), w.range * 0.9f, 0.3f);
                    CameraFollow.Shake(0.08f);
                    foreach (var c in Combat.HostilesInRadius(Team.Player, at, w.range * 0.9f))
                        Combat.Hit(Combatant, c, w.damageMult, WeaponStatus(w), true, w.knockback, -1f, Elements.Mask(Weapons.EquippedElement));
                }
                else
                {
                    VFX.Slash(transform.position, dir, w.range * 0.9f, w.arc, new Color(1f, 1f, 1f, 0.7f));
                    foreach (var c in Combat.HostilesInArc(Team.Player, transform.position, dir, w.range, w.arc))
                        Combat.Hit(Combatant, c, w.damageMult, WeaponStatus(w), true, w.knockback, -1f, Elements.Mask(Weapons.EquippedElement));
                }
                Swing(dir, w.arc >= 360f ? 90f : w.arc);
                Sound.Play(Sfx.Swing, transform.position, 0.5f);
                if (h < w.hits - 1) yield return new WaitForSeconds(0.12f);
            }
        }

        void Swing(Vector2 dir, float arc)
        {
            if (weapon == null) return;
            if (swingRoutine != null) StopCoroutine(swingRoutine);
            swingRoutine = StartCoroutine(SwingRoutine(dir, arc));
        }

        // Ranged weapons don't swing: bows are held out toward the target and drawn back, then released;
        // staves and wands thrust forward.
        void Shoot(Vector2 dir, bool bow)
        {
            if (weapon == null) return;
            if (swingRoutine != null) StopCoroutine(swingRoutine);
            swingRoutine = StartCoroutine(ShootRoutine(dir, bow));
        }

        IEnumerator ShootRoutine(Vector2 dir, bool bow)
        {
            weapon.gameObject.SetActive(true);
            Vector2 onScreen = View.ToScreen(dir);
            float screenAngle = Mathf.Atan2(onScreen.y, onScreen.x) * Mathf.Rad2Deg;
            // A bow is held across the line of fire with its belly toward the target; a staff points along it.
            weapon.rotation = View.Rotation * Quaternion.Euler(0, 0, bow ? screenAngle : screenAngle - 90f);
            const float duration = 0.22f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                float reach = bow
                    ? (k < 0.45f ? Mathf.Lerp(0.6f, 0.45f, k / 0.45f) : Mathf.Lerp(0.45f, 0.62f, (k - 0.45f) / 0.55f))   // draw, then release
                    : 0.45f + Mathf.Sin(k * Mathf.PI) * 0.35f;                                                            // thrust
                weapon.position = (Vector3)((Vector2)transform.position + dir * reach) + View.Up * 0.7f;
                yield return null;
            }
            weapon.gameObject.SetActive(false);
            swingRoutine = null;
        }

        IEnumerator SwingRoutine(Vector2 dir, float arc)
        {
            weapon.gameObject.SetActive(true);
            float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float t = 0f;
            const float duration = 0.16f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float a = baseAngle + Mathf.Lerp(arc / 2f, -arc / 2f, t / duration);
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
                Vector2? aim = MobaInput.MouseAim(transform.position, slotSkills[i] != null ? SkillRange(slotSkills[i]) : 5f);
                if (aim.HasValue) TryCast(i, aim.Value, true);
                else TryCast(i, Vector2.zero, false);
            }
        }

        // Gear can extend how far skills reach.
        public float SkillRange(SkillDefinition skill) => skill == null ? 0f : skill.range * (1f + Combatant.Stats.Get(StatType.SkillRange));

        public bool CanCast(int slot) =>
            slotSkills[slot] != null && cooldowns[slot] <= 0f && !Combatant.IsDead && !Combatant.IsStunned && RequirementsMet(slot);

        // A crafted skill whose attribute requirements are no longer met can't be used.
        public bool RequirementsMet(int slot)
        {
            if (slot == WeaponSlot) return true;
            var c = Crafting.Find(slot >= FirstSocketSlot ? socketIds[slot - FirstSocketSlot] : Profile.Data.equippedSkills[slot]);
            if (c == null) return true;
            // Weapon arts need their weapon in your hands.
            var needs = SkillParts.WeaponOf(c.delivery);
            if (needs.HasValue && needs.Value != Weapons.Equipped) return false;
            return SkillParts.RequirementsMet(c);
        }

        // Auto-cast socketed skills fire by themselves when ready: buffs and auras whenever enemies are
        // around, attacks when an enemy is in range. Auto-casting takes 50% longer to recharge.
        void UpdateAutoCasts(float dt)
        {
            if (Combatant.IsDead || Combatant.IsStunned || Panels.AnyOpen) return;
            for (int i = 0; i < autoSkills.Count; i++)
            {
                if (autoCooldowns[i] > 0f) { autoCooldowns[i] -= dt; continue; }
                var skill = autoSkills[i];
                float range = skill.targeting == Targeting.Self ? 7f : SkillRange(skill) + 1f;
                var target = Combat.NearestHostile(Team.Player, transform.position, range);
                if (target == null) continue;
                Vector2 to = (Vector2)target.transform.position - (Vector2)transform.position;
                Cast(skill, 1, to.normalized, (Vector2)transform.position + Vector2.ClampMagnitude(to, SkillRange(skill)));
                autoCooldowns[i] = skill.Cooldown(1, Combatant.Stats.Get(StatType.CooldownReduction)) * 1.5f;
            }
        }

        void Cast(SkillDefinition skill, int rank, Vector2 dir, Vector2 point)
        {
            var ctx = new SkillContext
            {
                caster = Combatant, runner = this, body = body, origin = transform.position, direction = dir.normalized,
                point = point, rank = rank, power = skill.Power(rank) * (1f + Combatant.Stats.Get(StatType.SkillPower)),
            };
            skill.Cast(ctx);
        }

        // aim: offset from the hero, magnitude 0..1 of the skill's range. aimed=false = quick cast at the auto-target.
        public bool TryCast(int slot, Vector2 aim, bool aimed)
        {
            if (!CanCast(slot)) return false;
            var skill = slotSkills[slot];
            Vector2 pos = transform.position;
            float range = SkillRange(skill);
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
                point = pos + dir * range * Mathf.Clamp01(aim.magnitude);
            }
            else
            {
                var target = Combat.NearestHostile(Team.Player, pos, range + 1.5f);
                if (target != null)
                {
                    Vector2 to = (Vector2)target.transform.position - pos;
                    dir = to.normalized;
                    point = pos + Vector2.ClampMagnitude(to, range);
                }
                else
                {
                    dir = Facing;
                    point = pos + Facing * range * (skill.targeting == Targeting.Dash ? 1f : 0.6f);
                }
            }

            if (dir.sqrMagnitude < 0.0001f) dir = Facing;
            Facing = dir;

            var ctx = new SkillContext
            {
                caster = Combatant, runner = this, body = body, origin = pos, direction = dir.normalized,
                point = point, rank = slotRanks[slot], power = skill.Power(slotRanks[slot]) * (1f + Combatant.Stats.Get(StatType.SkillPower)),
            };
            skill.Cast(ctx);

            cooldownTotals[slot] = skill.Cooldown(slotRanks[slot], Combatant.Stats.Get(StatType.CooldownReduction));
            cooldowns[slot] = cooldownTotals[slot];
            castLock = skill.castLock;
            attackAnimTimer = 0.25f;
            if (skill.targeting == Targeting.Dash) Sound.Play(Sfx.Dash, pos, 0.6f);
            else Sound.Play(Sfx.Cast, pos, 0.4f);
            SkillCast?.Invoke(slot);
            return true;
        }
    }
}
