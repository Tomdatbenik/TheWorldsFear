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
        float attackRoot;       // seconds still to stand still for a ranged attack
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

        // The hero's sprite: their chosen look wearing their helm, chest armor, gloves and boots.
        void DressInGear()
        {
            if (!animator) return;
            // The female hero is rendered from the 3D model; other looks are still drawn by the paper doll.
            var hero3D = Hero3DSet.For(Profile.Data.appearance.Female);
            // Handling speeds a ranged attack up along with the time spent standing still for it.
            float attackFrame = Weapon.attackFrame * (Weapon.rootTime > 0f ? 1f - Combatant.Stats.Get(StatType.Handling) : 1f);
            animator.UseHero3D(hero3D, Hero3DSet.AttackFor(Weapon.type), attackFrame);
            var pieces = new System.Collections.Generic.List<string>();
            string held = null;
            if (hero3D != null)
            {
                foreach (var slot in new[] { ItemSlot.Boots, ItemSlot.Chest, ItemSlot.Gloves, ItemSlot.Helm })
                    foreach (var item in Profile.Data.equipment)
                        if (item != null && item.slot == slot && Hero3DSet.PieceFor(item) != null) pieces.Add(Hero3DSet.PieceFor(item));
                held = Hero3DSet.WeaponPiece(Weapon.type);
                if (held != null) pieces.Add(held);
            }
            animator.SetGear3D(pieces, held != null);
            animator.SetSprites(PaperDoll.Get(PaperDoll.ForHero(Profile.Data.appearance, Profile.Data.equipment)));
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
            if (weaponProfile.critDamage > 0f) stats.AddModifier(new StatModifier(StatType.CritDamage, ModKind.Flat, weaponProfile.critDamage, weaponSource));
            if (weaponProfile.lifeSteal > 0f) stats.AddModifier(new StatModifier(StatType.LifeSteal, ModKind.Flat, weaponProfile.lifeSteal, weaponSource));
            if (weaponProfile.skillPower > 0f) stats.AddModifier(new StatModifier(StatType.SkillPower, ModKind.Flat, weaponProfile.skillPower, weaponSource));
            var weaponItem = Profile.Equipped(ItemSlot.Weapon);
            float guard = weaponItem != null ? Weapons.GuardArmor(weaponProfile.type, weaponItem.itemLevel) : 0f;
            if (guard > 0f) stats.AddModifier(new StatModifier(StatType.Armor, ModKind.Flat, guard, weaponSource));
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
            if (attackRoot > 0f) attackRoot -= dt;
            if (attackAnimTimer > 0f) attackAnimTimer -= dt;
            if (damageAnimTimer > 0f) damageAnimTimer -= dt;

            if (Combatant.IsDead || Time.timeScale == 0f)
            {
                moveInput = Vector2.zero;
                UpdateAnimator();
                return;
            }

            moveInput = AutoWalk != Vector2.zero ? AutoWalk : MobaInput.Move;
            HandleKeyboardSkills();
            if (!MobaInput.Blocked && KeyBinds.Pressed(GameAction.Potion)) UsePotion();

            CurrentTarget = Combat.NearestHostile(Team.Player, transform.position, AttackRange + 0.2f);
            var mouseAim = MobaInput.MouseAttackDirection(transform.position);
            if (mouseAim.HasValue)
            {
                TryBasicAttack(CurrentTarget, mouseAim.Value);   // toward the cursor, whether or not anything is there
            }
            else if (MobaInput.AttackHeld)
            {
                var chase = CurrentTarget ?? Combat.NearestHostile(Team.Player, transform.position, acquireRange);
                if (CurrentTarget != null) TryBasicAttack(CurrentTarget);
                else if (chase != null && moveInput == Vector2.zero)
                    moveInput = ((Vector2)(chase.transform.position - transform.position)).normalized;
                if (CurrentTarget == null) CurrentTarget = chase;
            }

            if (mouseAim.HasValue && castLock <= 0f) Facing = mouseAim.Value;
            else if (moveInput.sqrMagnitude > 0.01f && castLock <= 0f && attackRoot <= 0f) Facing = moveInput.normalized;
            UpdateAnimator();
        }

        void FixedUpdate()
        {
            if (Combatant.IsDead || castLock > 0f || attackRoot > 0f || Combatant.IsStunned)
            {
                body.linearVelocity = Vector2.zero;
                return;
            }
            Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
            body.linearVelocity = input * Combatant.MoveSpeed;
        }

        // Set while the game walks the hero somewhere itself, such as down the dungeon stairs.
        public Vector2 AutoWalk { get; set; }

        void UpdateAnimator()
        {
            if (animator == null) return;
            animator.Velocity = castLock > 0f || attackRoot > 0f || Combatant.IsDead ? Vector2.zero : moveInput;
            animator.Facing = Facing;
            animator.MoveSpeed = Combatant.MoveSpeed;
            animator.Attacking = attackAnimTimer > 0f;
        }

        // ---------- Basic attack ----------

        // The equipped weapon decides how the basic attack works.
        void TryBasicAttack(Combatant target, Vector2? aim = null)
        {
            if (attackTimer > 0f || castLock > 0f || Combatant.IsStunned) return;
            var w = Weapon;
            bool mastery = WeaponMasteries.Active(w.type);
            float interval = 1f / (w.attacksPerSecond * Combatant.Stats.Get(StatType.AttackSpeed));
            if (mastery && w.type == WeaponType.Bow) interval /= WeaponMasteries.RunAndGunSpeed;
            if (mastery && w.type == WeaponType.Crossbow) interval *= WeaponMasteries.RepeaterReload;
            attackTimer = interval;
            Vector2 dir = aim ?? ((Vector2)(target.transform.position - transform.position)).normalized;
            if (aim.HasValue)
            {
                // A homing shot only locks on to something near the line of the aim.
                target = null;
                foreach (var c in Combat.HostilesInArc(Team.Player, transform.position, dir, w.range + 2f, 50f)) { target = c; break; }
            }
            Facing = dir;
            attackAnimTimer = 0.2f;
            float handling = Combatant.Stats.Get(StatType.Handling);
            attackRoot = mastery && w.type == WeaponType.Bow ? 0f : w.rootTime * (1f - handling);

            if (!w.ranged)
            {
                if (mastery && w.type == WeaponType.Daggers) { ThrowKnives(w, dir); return; }
                if (mastery && w.type == WeaponType.Spear && ++javelinCount >= WeaponMasteries.JavelinEvery)
                {
                    javelinCount = 0;
                    ThrowJavelin(w, dir);
                    return;
                }
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
            if (mastery && w.type == WeaponType.Staff) spec.onHit = GravityOnHit;
            if (mastery && w.type == WeaponType.Wand) spec.onHit = ChainOnHit(spec, WeaponMasteries.ChainJumps, new HashSet<Combatant>());
            if (mastery && w.type == WeaponType.Crossbow)
            {
                spec.damageMult *= WeaponMasteries.RepeaterDamage;
                StartCoroutine(RepeaterBurst(w, spec, dir, target));
            }
            else FireVolley(w, spec, dir, target);
        }

        // The main shot plus one for each extra projectile.
        void FireVolley(WeaponProfile w, ProjectileSpec spec, Vector2 dir, Combatant target)
        {
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

        // ---------- Weapon masteries ----------

        int javelinCount;

        // How close an enemy must be before the basic attack starts: thrown knives reach further than a stab.
        float AttackRange => WeaponMasteries.Active(WeaponType.Daggers) && Weapon.type == WeaponType.Daggers ? WeaponMasteries.KnifeRange : Weapon.range;

        IEnumerator RepeaterBurst(WeaponProfile w, ProjectileSpec spec, Vector2 dir, Combatant target)
        {
            for (int i = 0; i < WeaponMasteries.RepeaterBolts; i++)
            {
                if (Combatant.IsDead) yield break;
                FireVolley(w, spec, dir, target);
                if (i < WeaponMasteries.RepeaterBolts - 1) yield return new WaitForSeconds(0.11f);
            }
        }

        // Daggers: a fan of knives, one per strike, extra slash and extra projectile.
        void ThrowKnives(WeaponProfile w, Vector2 dir)
        {
            var stats = Combatant.Stats;
            int knives = w.hits + Mathf.RoundToInt(stats.Get(StatType.ExtraSlashes)) + Mathf.RoundToInt(stats.Get(StatType.ExtraProjectiles));
            var spec = new ProjectileSpec
            {
                speed = 18f * (1f + stats.Get(StatType.ProjectileSpeed)), range = WeaponMasteries.KnifeRange + 1f, hitRadius = 0.3f,
                damageMult = w.damageMult, color = new Color(0.85f, 0.88f, 0.95f), size = 0.55f, sprite = HeroVisuals.ArrowSprite, look = "knife", rotateSprite = true,
                status = WeaponStatus(w), elements = Elements.Mask(Weapons.EquippedElement),
            };
            float spread = 9f * (1f + stats.Get(StatType.ProjectileSpread));
            for (int k = 0; k < knives; k++)
            {
                var d = (Vector2)(Quaternion.Euler(0, 0, (k - (knives - 1) / 2f) * spread) * dir);
                Projectile.Fire(Combatant, (Vector2)transform.position + d * 0.4f, d, spec);
            }
            Shoot(dir, false);
            Sound.Play(Sfx.Swing, transform.position, 0.4f);
        }

        // Spear: a thrown spear that goes through everything in a long line.
        void ThrowJavelin(WeaponProfile w, Vector2 dir)
        {
            var spec = new ProjectileSpec
            {
                speed = 22f * (1f + Combatant.Stats.Get(StatType.ProjectileSpeed)), range = WeaponMasteries.JavelinRange, hitRadius = 0.4f,
                damageMult = w.damageMult * WeaponMasteries.JavelinDamage, pierce = -1, knockback = 0.5f,
                color = new Color(0.9f, 0.8f, 0.6f), size = 1.1f, sprite = HeroVisuals.ArrowSprite, look = "spear", rotateSprite = true,
                status = WeaponStatus(w), elements = Elements.Mask(Weapons.EquippedElement),
            };
            FireVolley(w, spec, dir, null);
        }

        // Staff: everything near the impact is dragged toward it.
        void GravityOnHit(Combatant victim)
        {
            if (victim == null) return;
            Vector2 at = victim.transform.position;
            VFX.Ring(at, new Color(0.75f, 0.5f, 1f), WeaponMasteries.GravityRadius, 0.35f);
            foreach (var c in Combat.HostilesInRadius(Team.Player, at, WeaponMasteries.GravityRadius).ToArray())
            {
                if (c == victim || c.IsDead) continue;
                var info = Combat.Roll(Combatant, 0.15f, true);
                info.knockback = (at - (Vector2)c.transform.position).normalized * WeaponMasteries.GravityPull;
                c.TakeDamage(info);
            }
        }

        // Wand: after a hit the bolt leaps on to the nearest enemy it hasn't struck yet.
        System.Action<Combatant> ChainOnHit(ProjectileSpec spec, int jumpsLeft, HashSet<Combatant> struck) => victim =>
        {
            if (victim == null) return;
            struck.Add(victim);
            if (jumpsLeft <= 0) return;
            Combatant next = null;
            float best = WeaponMasteries.ChainRange;
            foreach (var c in Combat.HostilesInRadius(Team.Player, victim.transform.position, WeaponMasteries.ChainRange))
            {
                if (c == victim || c.IsDead || struck.Contains(c)) continue;
                float d = Vector2.Distance(victim.transform.position, c.transform.position);
                if (d < best) { best = d; next = c; }
            }
            if (next == null) return;
            struck.Add(next);
            var hop = spec;
            hop.homing = true;
            hop.range = WeaponMasteries.ChainRange + 1f;
            hop.onHit = ChainOnHit(spec, jumpsLeft - 1, struck);
            Vector2 from = victim.transform.position;
            var dir = ((Vector2)next.transform.position - from).normalized;
            Projectile.Fire(Combatant, from + dir * 0.9f, dir, hop, next);
        };

        // An elemental weapon sometimes applies its element's effect (burn, chill, ...).
        static StatusSpec WeaponStatus(WeaponProfile w)
        {
            var e = Weapons.EquippedElement;
            if (e == ElementPart.Physical || w.status.IsSet) return w.status;
            var st = SkillComposer.ElementStatus(e);
            if (st.IsSet) st.chance = st.type == StatusType.Stun ? st.chance : 0.25f;
            return st;
        }

        // The axe finishes off enemies that are nearly down.
        static float MeleeMult(WeaponProfile w, Combatant c) =>
            w.damageMult * (w.executeBelow > 0f && c.HPFraction < w.executeBelow ? 1f + w.executeBonus : 1f);

        IEnumerator MeleeAttack(WeaponProfile w, Combatant target, Vector2 dir)
        {
            float reach = w.range * (1f + 0.4f * Combatant.Stats.Get(StatType.Handling));
            int swings = w.hits + Mathf.RoundToInt(Combatant.Stats.Get(StatType.ExtraSlashes)) + (w.type == WeaponType.Sword && WeaponMasteries.DualWielding ? 1 : 0);
            bool spin = w.type == WeaponType.Axe && WeaponMasteries.Active(WeaponType.Axe);
            bool heavy = w.type == WeaponType.Hammer && WeaponMasteries.Active(WeaponType.Hammer);
            string fx = Fx.Name(Weapons.EquippedElement);
            for (int h = 0; h < swings; h++)
            {
                if (w.arc >= 360f)
                {
                    // Hammer: slam a circle at the target: a shockwave with cracks and a puff of dust (or the weapon's element).
                    Vector2 at = (Vector2)transform.position + dir * (target != null ? Mathf.Min(reach * 0.7f, Vector2.Distance(transform.position, target.transform.position)) : reach * 0.7f);
                    Fx.Shock(at, reach * 0.9f, fx);
                    Fx.Play("blast_" + fx, at, reach * 0.42f);
                    CameraFollow.Shake(0.08f);
                    foreach (var c in Combat.HostilesInRadius(Team.Player, at, reach * 0.9f))
                    {
                        Combat.Hit(Combatant, c, MeleeMult(w, c), WeaponStatus(w), true, w.knockback, -1f, Elements.Mask(Weapons.EquippedElement));
                        Fx.HitOn(c, fx);
                        Fx.Proc(c, Weapons.EquippedElement);
                    }
                    if (heavy)
                        AreaEffect.Spawn(Combatant, at, new AreaSpec
                        {
                            radius = reach * 0.9f, delay = 0.35f, ticks = WeaponMasteries.HeavyTicks, interval = 0.45f,
                            damageMult = w.damageMult * WeaponMasteries.HeavyDamage, status = StatusSpec.Slow(0.4f, 1.2f),
                            color = new Color(0.65f, 0.5f, 0.3f), elements = Elements.Mask(Weapons.EquippedElement),
                        });
                }
                else if (spin)
                {
                    // Axe mastery: a full spin around the hero.
                    Fx.Slash(transform.position, dir, reach, 360f, fx);
                    foreach (var c in Combat.HostilesInRadius(Team.Player, transform.position, reach))
                    {
                        Combat.Hit(Combatant, c, MeleeMult(w, c), WeaponStatus(w), true, w.knockback, -1f, Elements.Mask(Weapons.EquippedElement));
                        Fx.HitOn(c, fx);
                        Fx.Proc(c, Weapons.EquippedElement);
                    }
                }
                else
                {
                    Fx.Slash(transform.position, dir, reach * 0.95f, w.arc, fx);
                    foreach (var c in Combat.HostilesInArc(Team.Player, transform.position, dir, reach, w.arc))
                    {
                        Combat.Hit(Combatant, c, MeleeMult(w, c), WeaponStatus(w), true, w.knockback, -1f, Elements.Mask(Weapons.EquippedElement));
                        Fx.HitOn(c, fx);
                        Fx.Proc(c, Weapons.EquippedElement);
                    }
                }
                Swing(dir, w.arc >= 360f ? 90f : w.arc);
                Sound.Play(Sfx.Swing, transform.position, 0.5f);
                if (h < swings - 1) yield return new WaitForSeconds(0.12f);
            }
        }

        void Swing(Vector2 dir, float arc)
        {
            if (weapon == null || animator.HoldsRenderedWeapon) return;
            if (swingRoutine != null) StopCoroutine(swingRoutine);
            swingRoutine = StartCoroutine(SwingRoutine(dir, arc));
        }

        // Ranged weapons don't swing: bows are held out toward the target and drawn back, then released;
        // staves and wands thrust forward.
        void Shoot(Vector2 dir, bool bow)
        {
            if (weapon == null || animator.HoldsRenderedWeapon) return;
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

        // Why a skill button can't be used right now (null when it can).
        public string BlockedReason(int slot)
        {
            if (slot == WeaponSlot || SkillIn(slot) == null) return null;
            var c = Crafting.Find(slot >= FirstSocketSlot ? socketIds[slot - FirstSocketSlot] : Profile.Data.equippedSkills[slot]);
            if (c == null) return null;
            var needs = SkillParts.WeaponOf(c.delivery);
            if (needs.HasValue && needs.Value != Weapons.Equipped) return $"{c.name} needs {Weapons.Name(needs.Value)} in hand";
            if (!SkillParts.RequirementsMet(c)) return $"{c.name}: your attributes no longer meet its requirements";
            return null;
        }

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
