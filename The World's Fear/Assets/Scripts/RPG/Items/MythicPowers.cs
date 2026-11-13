using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Powers only mythic items roll, one pool per gear slot, plus the +1 to +3 passive ranks every mythic carries.
    // Wearing the same mythic power twice doesn't stack it.
    public static class MythicPowers
    {
        public class Power
        {
            public string id, name, where, text;
        }

        public static readonly List<Power> All = new List<Power>
        {
            new Power { id = "m_convergence", name = "Convergence", where = "weapon", text = "Basic attacks cycle through all eight elements. Every eighth attack releases one blast with every element at once." },
            new Power { id = "m_twinedge", name = "Twin Edge", where = "weapon", text = "Your basic attacks carry a second element and deal both." },
            new Power { id = "m_worldbreaker", name = "Worldbreaker", where = "weapon", text = "Critical basic attacks and hammer slams split the ground: a fissure pulses for damage for 3 seconds." },
            new Power { id = "m_starfall", name = "Starfall", where = "helm", text = "Critical hits call a meteor down on the target, at most once every 2 seconds." },
            new Power { id = "m_stormcrown", name = "Stormcrown", where = "helm", text = "Every 3 seconds in combat, lightning strikes the nearest enemy and jumps to two more." },
            new Power { id = "m_phoenix", name = "Phoenix Heart", where = "chest", text = "Once every 90 seconds, a killing blow revives you at 40% life instead and burns everything around you." },
            new Power { id = "m_livingarmor", name = "Living Armor", where = "chest", text = "Taking a hit hardens you: +4% armor per hit for 5 seconds, up to ten times." },
            new Power { id = "m_hunger", name = "Primordial Hunger", where = "gloves", text = "Each kill gives +1% damage, up to 30%. The bonus fades 8 seconds after your last kill." },
            new Power { id = "m_frostbound", name = "Frostbound Stride", where = "boots", text = "Running leaves a trail of frost that chills enemies who walk through it." },
            new Power { id = "m_echo", name = "Echo", where = "amulet", text = "Every fourth crafted spell casts twice. The echo uses the spell's next element." },
            new Power { id = "m_shadowtwin", name = "Shadow Twin", where = "ring, parry dagger", text = "A shadow of you copies your basic attacks for 30% damage." },
            new Power { id = "m_gravitywell", name = "Gravity Well", where = "orb", text = "Area spells pull enemies toward their center before they go off." },
            new Power { id = "m_timefracture", name = "Time Fracture", where = "tome", text = "Killing an elite, rare or boss monster resets every skill cooldown." },
            new Power { id = "m_volley", name = "Endless Volley", where = "quiver", text = "Every fifth shot also fans out five arrows in your weapon's element." },
            new Power { id = "m_aegis", name = "Divine Aegis", where = "shield", text = "Damage your armor and shield stop is stored. 3 seconds later it comes down as a holy pillar on the closest enemy." },
        };

        public static Power Find(string id) => All.Find(p => p.id == id);

        // ---------- Rolling ----------

        static string[] PowersFor(ItemInstance item)
        {
            switch (item.slot)
            {
                case ItemSlot.Weapon: return new[] { "m_convergence", "m_twinedge", "m_worldbreaker" };
                case ItemSlot.Helm: return new[] { "m_starfall", "m_stormcrown" };
                case ItemSlot.Chest: return new[] { "m_phoenix", "m_livingarmor" };
                case ItemSlot.Gloves: return new[] { "m_hunger" };
                case ItemSlot.Boots: return new[] { "m_frostbound" };
                case ItemSlot.Amulet: return new[] { "m_echo" };
                case ItemSlot.Ring: return new[] { "m_shadowtwin" };
                case ItemSlot.OffHand:
                    switch ((OffHandKind)item.offHandKind)
                    {
                        case OffHandKind.Orb: return new[] { "m_gravitywell" };
                        case OffHandKind.Tome: return new[] { "m_timefracture" };
                        case OffHandKind.Quiver: return new[] { "m_volley" };
                        case OffHandKind.Shield: return new[] { "m_aegis" };
                        case OffHandKind.ParryDagger: return new[] { "m_shadowtwin" };
                        default: return new[] { "m_convergence", "m_twinedge", "m_worldbreaker" };   // off-hand sword
                    }
            }
            return new[] { "m_hunger" };
        }

        // The passives each slot leans toward, so the bonus fits the power.
        static string[] PassivesFor(ItemInstance item)
        {
            switch (item.slot)
            {
                case ItemSlot.Weapon: return new[] { "p_mastery", "p_swift", "p_eagle", "p_execute", "p_ignite", "p_static", "p_hydra", "p_flurry" };
                case ItemSlot.Helm: return new[] { "p_mind", "p_eagle", "p_reach", "p_lingering" };
                case ItemSlot.Chest: return new[] { "p_thickskin", "p_hardened", "p_survivor", "p_berserk" };
                case ItemSlot.Gloves: return new[] { "p_bloodlust", "p_explosive", "p_swift", "p_flurry" };
                case ItemSlot.Boots: return new[] { "p_nimble", "p_frostbite" };
                case ItemSlot.Amulet: return new[] { "p_power", "p_expanse", "p_venom" };
                case ItemSlot.Ring: return new[] { "p_eagle", "p_mastery", "p_lingering" };
                case ItemSlot.OffHand:
                    switch ((OffHandKind)item.offHandKind)
                    {
                        case OffHandKind.Orb: return new[] { "p_expanse", "p_power", "p_hydra" };
                        case OffHandKind.Tome: return new[] { "p_mind", "p_lingering" };
                        case OffHandKind.Quiver: return new[] { "p_reach", "p_swift", "p_hydra" };
                        case OffHandKind.Shield: return new[] { "p_thickskin", "p_hardened" };
                        case OffHandKind.ParryDagger: return new[] { "p_eagle", "p_mastery", "p_swift" };
                        default: return new[] { "p_mastery", "p_swift", "p_flurry" };
                    }
            }
            return new[] { "p_thickskin" };
        }

        public static void Roll(ItemInstance item, System.Random rng)
        {
            item.legendaryPower = null;
            var powers = PowersFor(item);
            item.mythicPower = powers[rng.Next(powers.Length)];
            var passives = PassivesFor(item);
            item.bonusPassive = passives[rng.Next(passives.Length)];
            // Hydra and Flurry (+1 projectile, +1 slash) only ever come as +1.
            double r = rng.NextDouble();
            item.bonusRanks = item.bonusPassive == "p_hydra" || item.bonusPassive == "p_flurry" ? 1 : r < 0.5 ? 1 : r < 0.85 ? 2 : 3;
            item.secondElement = -1;
            if (item.mythicPower == "m_twinedge")
            {
                ElementPart e;
                do e = (ElementPart)rng.Next(8);
                while (e == ElementPart.Physical || (int)e == item.element);
                item.secondElement = (int)e;
            }
        }

        // Mythics found before mythic powers existed swap their legendary power for one of these, once.
        public static bool Upgrade(ItemInstance item)
        {
            if (item == null || item.rarity != Rarity.Mythic || !string.IsNullOrEmpty(item.mythicPower)) return false;
            var old = ItemPowers.Find(item.legendaryPower);
            Roll(item, new System.Random(item.seed ^ 0x6d797468));
            string now = "of " + Find(item.mythicPower).name;
            if (old != null && item.name != null && item.name.EndsWith(old.name)) item.name = item.name.Substring(0, item.name.Length - old.name.Length) + now;
            else item.name = (item.name ?? item.baseName) + " " + now;
            return true;
        }

        public static string Describe(ItemInstance item)
        {
            var p = Find(item.mythicPower);
            if (p == null) return "";
            string text = p.text;
            if (p.id == "m_twinedge" && item.secondElement >= 0) text = $"Your basic attacks also deal {Elements.Tag((ElementPart)item.secondElement)} damage.";
            return $"<b>{p.name}</b>. {text}";
        }

        // Extra ranks the equipped mythics give a passive. Two mythics on the same passive add up.
        public static int BonusRanks(string passiveId)
        {
            if (Profile.Data?.equipment == null || passiveId == null) return 0;
            int n = 0;
            foreach (var it in Profile.Data.equipment)
                if (it != null && it.bonusPassive == passiveId) n += it.bonusRanks;
            return n;
        }

        public static string PassiveName(string passiveId)
        {
            var node = Profile.Class != null && Profile.Class.skillTree != null ? Profile.Class.skillTree.Find(passiveId) : null;
            return node != null ? node.DisplayName : passiveId;
        }

        // ---------- In play ----------

        static readonly HashSet<string> active = new HashSet<string>();
        static readonly List<Action> cleanups = new List<Action>();
        static HeroController hero;
        static MythicRunner runner;
        static ElementPart? twin;
        static int attackCount, volleyCount, echoCount;
        static float phoenixReady, starfallReady, fissureReady;
        static int armorStacks, hungerStacks;
        static float armorUntil, hungerUntil;
        static readonly Dictionary<string, SkillDefinition> echoes = new Dictionary<string, SkillDefinition>();

        public static bool Has(string id) => active.Contains(id);

        const int AllElements = 0xDF;   // every element bit but physical

        // The element of this basic attack: the weapon's, or with Convergence the next in the cycle.
        public static ElementPart AttackElement =>
            Has("m_convergence") && hero != null ? (ElementPart)(attackCount % 8) : Weapons.EquippedElement;

        public static int AttackMask
        {
            get
            {
                var e = AttackElement;
                if (twin.HasValue && twin.Value != e) return Elements.Mask(new List<ElementPart> { e, twin.Value });
                return Elements.Mask(e);
            }
        }

        public static void Refresh(HeroController h)
        {
            foreach (var c in cleanups) c();
            cleanups.Clear();
            active.Clear();
            twin = null;
            echoes.Clear();
            hero = h;
            if (h == null) return;
            foreach (var it in Profile.Data.equipment)
            {
                if (it == null || string.IsNullOrEmpty(it.mythicPower)) continue;
                active.Add(it.mythicPower);
                if (it.mythicPower == "m_twinedge" && it.secondElement >= 0) twin = (ElementPart)it.secondElement;
            }
            runner = h.GetComponent<MythicRunner>() ?? h.gameObject.AddComponent<MythicRunner>();
            runner.Setup(h);

            var owner = h.Combatant;
            Action<Combatant, DamageInfo, float> dealt = (t, info, amount) => OnDealt(owner, t, info);
            Action<DamageInfo, float> damaged = (info, amount) => OnDamaged(owner, info, amount);
            Action<Combatant> killed = v => OnKilled(owner, v);
            owner.DealtDamage += dealt;
            owner.Damaged += damaged;
            owner.Killed += killed;
            cleanups.Add(() => { owner.DealtDamage -= dealt; owner.Damaged -= damaged; owner.Killed -= killed; });
            if (Has("m_phoenix"))
            {
                owner.CheatDeath = info => Phoenix(owner);
                cleanups.Add(() => owner.CheatDeath = null);
            }
        }

        // Called at the start of every basic attack.
        public static void OnBasicAttack(HeroController h, Vector2 dir, Combatant target)
        {
            if (!Has("m_convergence")) return;
            attackCount++;
            if (attackCount % 8 != 0) return;
            Vector2 at = target != null ? (Vector2)target.transform.position : (Vector2)h.transform.position + dir * 2.5f;
            const float r = 2.4f;
            Fx.Blast(at, r, AllElements | (((int)ElementPart.Fire + 1) << 8), Elements.Color(ElementPart.Fire));
            CameraFollow.Shake(0.12f);
            FloatingText.Spawn(at + (Vector2)View.Up * 1.5f, "Convergence", new Color(1f, 0.3f, 0.5f), 1f);
            foreach (var c in Combat.HostilesInRadius(Team.Player, at, r).ToArray())
                Combat.Hit(h.Combatant, c, 2f, default, false, 0.4f, -1f, AllElements);
        }

        // Every fifth ranged shot: true when Endless Volley fires its fan.
        public static bool VolleyShot() => Has("m_volley") && ++volleyCount % 5 == 0;

        // A crafted spell was cast from a skill button.
        public static void OnSpellCast(HeroController h, int slot, Vector2 dir, Vector2 point)
        {
            if (!Has("m_echo") || slot == HeroController.WeaponSlot) return;
            var c = Crafting.Find(h.CraftIdIn(slot));
            if (c == null || ++echoCount % 4 != 0) return;
            var next = (ElementPart)(((int)c.element + 1) % 8);
            if (next == ElementPart.Physical) next = (ElementPart)(((int)next + 1) % 8);
            string key = c.uid + ":" + next;
            if (!echoes.TryGetValue(key, out var skill) || skill == null)
            {
                var copy = JsonUtility.FromJson<CraftedSkill>(JsonUtility.ToJson(c));
                copy.element = next;
                skill = SkillComposer.Build(copy);
                echoes[key] = skill;
            }
            if (skill.targeting == Targeting.Dash) return;
            runner.Later(0.3f, () =>
            {
                if (h == null || h.Combatant.IsDead) return;
                FloatingText.Spawn(h.transform.position + View.Up * 2.2f, "Echo", Elements.Color(next), 0.9f);
                h.CastSkill(skill, 1, dir, point);
            });
        }

        // Gravity Well: a crafted area spell drags enemies toward its center first.
        public static void OnSpellArea(Combatant caster, Vector2 at, float r)
        {
            if (!Has("m_gravitywell") || hero == null || caster != hero.Combatant) return;
            float pull = r * 1.9f;
            VFX.Ring(at, new Color(0.7f, 0.45f, 1f), pull, 0.45f);
            foreach (var c in Combat.HostilesInRadius(Team.Player, at, pull).ToArray())
            {
                Vector2 to = at - (Vector2)c.transform.position;
                float d = to.magnitude;
                if (d < 0.4f) continue;
                c.TakeDamage(new DamageInfo { source = caster, amount = 0f, silent = true, trueDamage = true, knockback = to / d * Mathf.Min(d * 0.85f, 4f) });
            }
        }

        static void OnDealt(Combatant owner, Combatant target, DamageInfo info)
        {
            if (info.silent || target == null) return;
            Vector2 at = target.transform.position;

            if (info.isBasicAttack && twin.HasValue && !target.IsDead)
                Fx.HitOn(target, Fx.Name(twin.Value));

            if (Has("m_worldbreaker") && info.isBasicAttack && (info.isCrit || Weapons.Equipped == WeaponType.Hammer) && Time.time >= fissureReady)
            {
                fissureReady = Time.time + 1.2f;
                Fx.Shock(at, 1.6f, "physical");
                CameraFollow.Shake(0.06f);
                AreaEffect.Spawn(owner, at, new AreaSpec
                {
                    radius = 1.5f, delay = 0.05f, ticks = 6, interval = 0.5f, damageMult = 0.4f,
                    color = new Color(0.62f, 0.46f, 0.3f), elements = AttackMask,
                });
            }

            if (Has("m_starfall") && info.isCrit && Time.time >= starfallReady)
            {
                starfallReady = Time.time + 2f;
                AreaEffect.Spawn(owner, at, new AreaSpec
                {
                    radius = 1.7f, delay = 0.55f, ticks = 1, damageMult = 1.8f, status = StatusSpec.Burn(0.3f, 2f),
                    color = new Color(1f, 0.5f, 0.2f), elements = Elements.Mask(ElementPart.Fire),
                });
                runner.Meteor(at, 0.55f);
            }

            if (Has("m_shadowtwin") && info.isBasicAttack && !target.IsDead)
            {
                float amount = info.amount * 0.3f;
                bool crit = info.isCrit;
                int els = info.elements;
                runner.Later(0.15f, () =>
                {
                    if (target == null || target.IsDead) return;
                    target.TakeDamage(new DamageInfo { source = owner, amount = amount, isCrit = crit, elements = els });
                    Fx.HitOn(target, "shadow");
                });
                runner.TwinStrike();
            }
        }

        static void OnDamaged(Combatant owner, DamageInfo info, float dealt)
        {
            if (info.silent) return;
            if (Has("m_livingarmor") && dealt > 0f)
            {
                armorStacks = Time.time < armorUntil ? Mathf.Min(10, armorStacks + 1) : 1;
                armorUntil = Time.time + 5f;
                owner.AddTimedModifiers("m_livingarmor", 5f, (StatType.Armor, ModKind.Percent, 0.04f * armorStacks));
            }
            if (Has("m_aegis") && info.source != null && !info.trueDamage)
                runner.Store(Mathf.Max(0f, info.amount - dealt) + info.amount * 0.25f);
        }

        static void OnKilled(Combatant owner, Combatant victim)
        {
            if (victim == null || victim.team == Team.Player) return;
            if (Has("m_hunger"))
            {
                hungerStacks = Time.time < hungerUntil ? Mathf.Min(30, hungerStacks + 1) : 1;
                hungerUntil = Time.time + 8f;
                owner.AddTimedModifiers("m_hunger", 8f, (StatType.Damage, ModKind.Percent, 0.01f * hungerStacks));
            }
            if (Has("m_timefracture") && victim.rank != MonsterRank.Normal && hero != null)
            {
                hero.ResetCooldowns();
                VFX.Ring(hero.transform.position, new Color(0.5f, 0.85f, 1f), 2.2f, 0.4f);
                FloatingText.Spawn(hero.transform.position + View.Up * 2.2f, "Time Fracture", new Color(0.5f, 0.85f, 1f), 1f);
            }
        }

        static bool Phoenix(Combatant owner)
        {
            if (Time.time < phoenixReady) return false;
            phoenixReady = Time.time + 90f;
            owner.SetHP(owner.MaxHP * 0.4f);
            Vector2 at = owner.transform.position;
            Fx.Blast(at, 3.5f, "fire");
            VFX.Burst(at, new Color(1f, 0.6f, 0.2f), 40, 2.5f);
            CameraFollow.Shake(0.3f);
            FloatingText.Spawn(at + (Vector2)View.Up * 2.4f, "Phoenix Heart!", new Color(1f, 0.55f, 0.2f), 1.4f, 1.3f);
            foreach (var c in Combat.HostilesInRadius(owner.team, at, 3.5f).ToArray())
                Combat.Hit(owner, c, 2.5f, StatusSpec.Burn(0.5f, 4f), false, 1.2f, -1f, Elements.Mask(ElementPart.Fire));
            runner.Shield(1.5f);
            return true;
        }

        public static float PhoenixCooldownLeft => Mathf.Max(0f, phoenixReady - Time.time);
    }

    // Runs the mythic powers that tick on their own: Stormcrown, the frost trail, Divine Aegis and the shadow twin.
    public class MythicRunner : MonoBehaviour
    {
        HeroController hero;
        float stormTimer = 3f, trail, aegisStored, aegisAt;
        Vector2 lastPos;
        SpriteRenderer body, shadow;
        float twinFlash;

        public void Setup(HeroController h)
        {
            hero = h;
            lastPos = h.transform.position;
            var anim = h.GetComponentInChildren<CharacterAnimator>();
            body = anim ? anim.GetComponent<SpriteRenderer>() : null;
        }

        public void Later(float seconds, Action a) => StartCoroutine(LaterRoutine(seconds, a));

        IEnumerator LaterRoutine(float seconds, Action a)
        {
            yield return new WaitForSeconds(seconds);
            a();
        }

        public void Store(float amount)
        {
            if (amount <= 0f) return;
            if (aegisStored <= 0f) aegisAt = Time.time + 3f;
            aegisStored += amount;
        }

        public void Shield(float seconds) => StartCoroutine(ShieldRoutine(seconds));

        IEnumerator ShieldRoutine(float seconds)
        {
            var c = hero.Combatant;
            c.Invulnerable = true;
            yield return new WaitForSeconds(seconds);
            c.Invulnerable = false;
        }

        // A meteor falls from the sky onto the spot over the warning time.
        public void Meteor(Vector2 at, float time) => StartCoroutine(MeteorRoutine(at, time));

        IEnumerator MeteorRoutine(Vector2 at, float time)
        {
            var go = new GameObject("Meteor");
            var sr = GameAssets.CreateWorldSprite("rock", GameAssets.SoftCircle, go.transform, new Color(1f, 0.55f, 0.2f), SortOrder.Telegraph + 5, 0.9f);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = t / time;
                go.transform.position = (Vector3)at + View.Up * Mathf.Lerp(7f, 0.3f, k * k) + (Vector3)(Vector2.left * Mathf.Lerp(2.5f, 0f, k));
                if (UnityEngine.Random.value < 0.5f) VFX.Burst(go.transform.position, new Color(1f, 0.45f, 0.15f), 2, 0.2f);
                yield return null;
            }
            Destroy(go);
        }

        public void TwinStrike() => twinFlash = 0.25f;

        void Update()
        {
            if (hero == null || hero.Combatant.IsDead) { ShowShadow(false); return; }
            float dt = Time.deltaTime;
            Vector2 pos = hero.transform.position;

            if (MythicPowers.Has("m_stormcrown"))
            {
                stormTimer -= dt;
                if (stormTimer <= 0f)
                {
                    var first = Combat.NearestHostile(Team.Player, pos, 7f);
                    if (first != null) { Storm(first); stormTimer = 3f; }
                    else stormTimer = 0.3f;
                }
            }

            if (MythicPowers.Has("m_frostbound"))
            {
                float moved = Vector2.Distance(pos, lastPos);
                if (moved < 1f) trail += moved; // ignore teleports
                if (trail >= 1.1f)
                {
                    trail = 0f;
                    AreaEffect.Spawn(hero.Combatant, pos, new AreaSpec
                    {
                        radius = 0.85f, delay = 0f, ticks = 5, interval = 0.5f, damageMult = 0.15f, quiet = true,
                        status = new StatusSpec { type = StatusType.Chill, magnitude = 0.4f, duration = 1.5f, chance = 1f },
                        color = new Color(0.55f, 0.85f, 1f), elements = Elements.Mask(ElementPart.Frost),
                    });
                }
            }
            lastPos = pos;

            if (aegisStored > 0f && Time.time >= aegisAt)
            {
                var target = Combat.NearestHostile(Team.Player, pos, 9f);
                if (target != null) { Pillar(target.transform.position, aegisStored); aegisStored = 0f; }
                else if (Time.time > aegisAt + 10f) aegisStored = 0f;
            }

            ShowShadow(MythicPowers.Has("m_shadowtwin"));
        }

        void Storm(Combatant first)
        {
            var struck = new HashSet<Combatant>();
            var cur = first;
            Vector2 from = (Vector2)hero.transform.position + (Vector2)View.Up * 3f;
            for (int k = 0; k < 3 && cur != null; k++)
            {
                struck.Add(cur);
                Vector2 at = cur.transform.position;
                VFX.Slash(Vector3.Lerp(from, at, 0.5f), (at - from).normalized, Vector2.Distance(from, at) * 0.5f, 6f, new Color(0.7f, 0.85f, 1f));
                Fx.Play("blast_lightning", at, 0.8f);
                Combat.Hit(hero.Combatant, cur, 0.9f, default, false, 0f, -1f, Elements.Mask(ElementPart.Lightning));
                from = at;
                Combatant next = null;
                float best = 4.5f;
                foreach (var c in Combat.HostilesInRadius(Team.Player, at, 4.5f))
                {
                    if (struck.Contains(c) || c.IsDead) continue;
                    float d = Vector2.Distance(at, c.transform.position);
                    if (d < best) { best = d; next = c; }
                }
                cur = next;
            }
            Sound.Play(Sfx.Cast, hero.transform.position, 0.3f);
        }

        void Pillar(Vector2 at, float stored)
        {
            var c = hero.Combatant;
            float dmg = stored * 2f + c.Stats.Get(StatType.Damage);
            Fx.Blast(at, 1.5f, "holy");
            VFX.Burst(at, new Color(1f, 0.95f, 0.6f), 24, 1.2f);
            FloatingText.Spawn(at + (Vector2)View.Up * 2f, "Divine Aegis", new Color(1f, 0.92f, 0.55f), 0.9f);
            foreach (var t in Combat.HostilesInRadius(Team.Player, at, 1.5f).ToArray())
                t.TakeDamage(new DamageInfo { source = c, amount = dmg, trueDamage = true, elements = Elements.Mask(ElementPart.Holy) });
        }

        // The shadow twin: a dark copy of the hero standing just behind them.
        void ShowShadow(bool on)
        {
            if (!on || body == null)
            {
                if (shadow) shadow.gameObject.SetActive(false);
                return;
            }
            if (shadow == null)
            {
                shadow = new GameObject("ShadowTwin").AddComponent<SpriteRenderer>();
                shadow.transform.SetParent(transform, false);
                RenderAssets.MakeUnlit(shadow);   // the lit sprite material ignores the dark tint
            }
            shadow.gameObject.SetActive(true);
            twinFlash = Mathf.Max(0f, twinFlash - Time.deltaTime);
            shadow.sprite = body.sprite;
            shadow.flipX = body.flipX;
            shadow.sortingLayerID = body.sortingLayerID;
            shadow.sortingOrder = body.sortingOrder - 1;
            var bt = body.transform;
            shadow.transform.rotation = bt.rotation;
            shadow.transform.localScale = bt.lossyScale;
            Vector2 back = -hero.Facing * (twinFlash > 0f ? 0.35f : 0.75f) + Vector2.left * 0.25f;
            shadow.transform.position = bt.position + (Vector3)back;
            shadow.color = new Color(0.16f, 0.05f, 0.24f, twinFlash > 0f ? 0.85f : 0.6f);
        }
    }
}
