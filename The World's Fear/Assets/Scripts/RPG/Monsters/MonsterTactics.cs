using System.Collections;
using UnityEngine;

namespace TWF
{
    // What makes a monster seem to think, on top of its role's attacks:
    //   grab       brutes lunge and hold the hero; the pack's shooters fire meanwhile. A missed grab leaves the brute open.
    //   react      skirmishers (and some chasers) sidestep a swing or a shot aimed at them; casters and spitters hop back.
    //   flee       some wounded monsters run to their healer, or away, howling for help; healed, they come back angry.
    //   terrain    shooters climb to high ground, chargers line up to knock the hero off a ledge, leapers drop on it.
    //   habits     chargers and chasers aim where a kiting hero is going; exploders rush a hero who stands still.
    //   healers    stand back and mend the most wounded; enough damage while they channel breaks the spell.
    public partial class MonsterBrain
    {
        // ---------- Tuning ----------
        public const float GrabReach = 3.8f, GrabMin = 0.5f;          // how far the lunge goes, and the closest it starts from
        public const float GrabHold = 2.2f;                           // seconds the hero is held unless it breaks free
        public const float GrabBreak = 0.12f;                         // share of its life the holder can lose before it lets go
        public const float FleeBelow = 0.3f, FleeSeconds = 3.5f, FleeSpeed = 1.2f;
        public const float EnrageSeconds = 7f, EnragedCooldown = 0.75f;
        public const float HowlRange = 12f;
        public const float HealEvery = 4f, HealRange = 9f, HealChannel = 0.9f, HealShare = 0.3f, HealInterrupt = 0.1f;
        public const float BackstepWithin = 2.6f;
        public const float PounceReach = 6.5f;
        public const float TurretRush = 1.4f, KiteLead = 0.45f, ShoveKnockback = 3.6f;

        // What happened, for the play tests.
        public static int Dodges, Backsteps, Grabs, Throws, Heals, Interrupts, Howls, Pounces, Shoves;

        Vector2? routeGoal;          // where the open-world router heads this frame instead of the hero
        float damageTaken;
        float dodgeTimer, backstepTimer, pounceTimer, healTimer, shotCheck;
        int lastThreat;
        bool fleeNature, fled;
        float fleeUntil, enragedUntil;
        bool hasPerch;
        Vector2 perch;
        float perchUntil, perchCheck;
        Vector2 ledgeDir, ledgePending;
        float lineupUntil;

        public bool Healing { get; private set; }   // channelling a heal
        public bool InFight => state == State.Chase;
        bool Fleeing => Time.time < fleeUntil;
        public bool IsFleeing => Fleeing;
        public bool Enraged => Time.time < enragedUntil;
        bool IsRanged => Ranged || Species.archetype == MonsterArchetype.Healer;

        void SetupTactics()
        {
            var a = Species.archetype;
            bool canFlee = a == MonsterArchetype.Chaser || a == MonsterArchetype.Spitter || a == MonsterArchetype.Caster ||
                           a == MonsterArchetype.Charger || a == MonsterArchetype.Skirmisher;
            fleeNature = a == MonsterArchetype.Healer || (canFlee && UnityEngine.Random.value < 0.5f);
            if (Combatant.rank == MonsterRank.Rare || Combatant.rank == MonsterRank.Boss) fleeNature = false;
            healTimer = UnityEngine.Random.Range(0.8f, 2f);
            dodgeTimer = UnityEngine.Random.Range(0f, 1f);
            pounceTimer = UnityEngine.Random.Range(1f, 3f);
        }

        // For the tests: whether this monster is one that runs when hurt.
        public bool FleeNature { get => fleeNature; set => fleeNature = value; }

        // ---------- React ----------

        bool React(Combatant hero, Vector2 pos, float dist, Vector2 toHero)
        {
            float dt = Time.deltaTime;
            dodgeTimer -= dt;
            backstepTimer -= dt;
            var a = Species.archetype;

            // A swing or a cast coming its way: step out of it.
            float chance = a == MonsterArchetype.Skirmisher ? 0.75f : a == MonsterArchetype.Chaser ? 0.3f : 0f;
            if (chance > 0f && dodgeTimer <= 0f && HeroThreat.Id != lastThreat && HeroThreat.Aimed(pos, Combatant.radius))
            {
                lastThreat = HeroThreat.Id;
                if (UnityEngine.Random.value < chance)
                {
                    Vector2 side = new Vector2(-HeroThreat.Dir.y, HeroThreat.Dir.x);
                    if (Vector2.Dot(side, pos - HeroThreat.From) < 0f) side = -side;   // out the side it already leans to
                    Sidestep(side - toHero * 0.25f, a == MonsterArchetype.Skirmisher);
                    return true;
                }
            }

            // A skirmisher also sees the hero's shots coming.
            if (a == MonsterArchetype.Skirmisher && dodgeTimer <= 0f)
            {
                shotCheck -= dt;
                if (shotCheck <= 0f)
                {
                    shotCheck = 0.1f;
                    if (ShotIncoming(pos, out Vector2 shot) && UnityEngine.Random.value < 0.6f)
                    {
                        Vector2 side = new Vector2(-shot.y, shot.x);
                        if (UnityEngine.Random.value < 0.5f) side = -side;
                        Sidestep(side, true);
                        return true;
                    }
                }
            }

            // Shooters and healers don't let the hero stand on them: they hop back.
            if ((a == MonsterArchetype.Caster || a == MonsterArchetype.Spitter || a == MonsterArchetype.Healer) && backstepTimer <= 0f && dist < BackstepWithin)
            {
                backstepTimer = a == MonsterArchetype.Spitter ? 5.5f : 4f;
                if (a == MonsterArchetype.Spitter && UnityEngine.Random.value > 0.7f) return false;
                Vector2 away = (-toHero + new Vector2(-toHero.y, toHero.x) * UnityEngine.Random.Range(-0.5f, 0.5f)).normalized;
                Backsteps++;
                StartCoroutine(Dash(away, 8.5f, 0.28f, a == MonsterArchetype.Caster ? new Color(0.7f, 0.4f, 1f) : Species.look.primary));
                return true;
            }
            return false;
        }

        void Sidestep(Vector2 dir, bool skirmisher)
        {
            dodgeTimer = skirmisher ? UnityEngine.Random.Range(2.2f, 3f) : UnityEngine.Random.Range(4f, 6f);
            Dodges++;
            StartCoroutine(Dash(dir, 7.5f, 0.22f, Color.white));
        }

        bool ShotIncoming(Vector2 pos, out Vector2 dir)
        {
            dir = Vector2.zero;
            foreach (var p in Projectile.Live)
            {
                if (p == null || p.OwnerTeam != Team.Player) continue;
                Vector2 s = p.transform.G(), d = p.Direction, to = pos - s;
                float along = Vector2.Dot(to, d);
                if (along < 0f || along > 4f) continue;
                if (Mathf.Abs(to.x * d.y - to.y * d.x) > Combatant.radius + 0.5f) continue;
                if (along / Mathf.Max(0.1f, p.Speed) > 0.45f) continue;
                dir = d;
                return true;
            }
            return false;
        }

        IEnumerator Dash(Vector2 dir, float speed, float time, Color puff)
        {
            busy = true;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;
            VFX.Burst(transform.position + View.Up * 0.4f, puff, 5, 0.4f);
            float t = 0f;
            while (t < time && !Combatant.IsDead && !Combatant.IsStunned)
            {
                t += Time.fixedDeltaTime;
                body.linearVelocity = dir * speed;
                yield return new WaitForFixedUpdate();
            }
            body.linearVelocity = Vector2.zero;
            busy = false;
        }

        // ---------- Flee, howl, come back angry ----------

        bool Flee(Combatant hero, Vector2 pos, float dist)
        {
            if (!Fleeing)
            {
                if (!fleeNature || fled || Band != null || Combatant.HPFraction > FleeBelow) return false;
                fled = true;
                fleeUntil = Time.time + FleeSeconds;
                Howl(pos);
            }
            Vector2 heroPos = hero.transform.G();
            var healer = FindHealer(pos);
            Vector2 goal = healer != null
                ? healer.transform.G() + (healer.transform.G() - heroPos).normalized * 1.5f
                : pos + (pos - heroPos).normalized * 6f;
            routeGoal = goal;
            Vector2 to = goal - pos;
            desired = to.magnitude > 0.6f ? to.normalized : Vector2.zero;
            if (desired != Vector2.zero) facing = desired;
            // Cornered: it still bites what is on top of it.
            if (Species.preferredDistance <= 0f && Vector2.Distance(pos, heroPos) <= Species.attackRange && attackTimer <= 0f)
                StartCoroutine(MeleeAttack(hero));
            return true;
        }

        MonsterBrain FindHealer(Vector2 pos)
        {
            MonsterBrain best = null;
            float bestD = 16f * 16f;
            foreach (var c in Combatant.All)
            {
                if (c == null || c == Combatant || c.IsDead || c.team != Team.Enemy) continue;
                float d = (c.transform.G() - pos).sqrMagnitude;
                if (d >= bestD) continue;
                var m = c.GetComponent<MonsterBrain>();
                if (m == null || m.Species.archetype != MonsterArchetype.Healer) continue;
                best = m;
                bestD = d;
            }
            return best;
        }

        // A scared monster's cry: every monster nearby that hasn't joined the fight does.
        void Howl(Vector2 pos)
        {
            Howls++;
            VFX.Ring(transform.position, new Color(1f, 0.85f, 0.45f), 3f, 0.5f);
            FloatingText.Spawn(transform.position + View.Up * (Combatant.HeadHeight + 0.3f), "Howls for help", new Color(1f, 0.85f, 0.45f), 0.7f);
            foreach (var c in Combatant.All)
            {
                if (c == null || c == Combatant || c.IsDead || c.team != Team.Enemy) continue;
                if ((c.transform.G() - pos).sqrMagnitude > HowlRange * HowlRange) continue;
                var m = c.GetComponent<MonsterBrain>();
                if (m != null && m.state == State.Idle) m.Aggro();
            }
        }

        public void Enrage()
        {
            if (Combatant.IsDead) return;
            enragedUntil = Time.time + EnrageSeconds;
            Combatant.AddTimedModifiers("enraged", EnrageSeconds, (StatType.Damage, ModKind.Percent, 0.3f), (StatType.MoveSpeed, ModKind.Percent, 0.2f));
            FloatingText.Spawn(transform.position + View.Up * (Combatant.HeadHeight + 0.3f), "Enraged", new Color(1f, 0.3f, 0.2f), 0.85f);
            VFX.Ring(transform.position, new Color(1f, 0.3f, 0.2f), 1.1f, 0.35f);
            Combatant.Flash(new Color(1f, 0.3f, 0.2f));
        }

        // Mended by a healer while running: it turns and comes back angry.
        void HealedByHealer()
        {
            if (!Fleeing) return;
            fleeUntil = 0f;
            Enrage();
        }

        // ---------- Healer ----------

        void HealerFight(Combatant hero, Vector2 pos, float dist, Vector2 toHero)
        {
            var sp = Species;
            healTimer -= Time.deltaTime;
            if (dist < sp.preferredDistance - 1f) desired = -toHero;
            else if (dist > sp.attackRange + 2f) desired = toHero;
            else desired = new Vector2(-toHero.y, toHero.x) * Mathf.Sign(Mathf.Sin(Time.time * 0.6f + strafeSeed));

            if (healTimer <= 0f)
            {
                var ally = MostWounded(pos);
                if (ally != null)
                {
                    Vector2 a = ally.transform.G();
                    if (Vector2.Distance(pos, a) > HealRange)
                    {
                        routeGoal = a;
                        desired = (a - pos).normalized;
                        return;
                    }
                    healTimer = HealEvery * CooldownScale;
                    StartCoroutine(HealSpell(ally));
                    return;
                }
                healTimer = 0.5f;   // nobody hurt: look again soon
            }
            // Nothing to mend: it pelts the hero, weakly.
            if (dist <= sp.attackRange && attackTimer <= 0f) StartCoroutine(SpitAttack(hero));
        }

        // The most wounded of its side within reach (itself too, when badly hurt); never a boss.
        Combatant MostWounded(Vector2 pos)
        {
            Combatant best = null;
            float bestF = 0.75f;
            float reach = HealRange * 1.6f;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsDead || c.team != Team.Enemy || c.rank == MonsterRank.Boss) continue;
                if (c == Combatant && c.HPFraction > 0.5f) continue;
                if ((c.transform.G() - pos).sqrMagnitude > reach * reach) continue;
                if (c.HPFraction < bestF) { bestF = c.HPFraction; best = c; }
            }
            return best;
        }

        IEnumerator HealSpell(Combatant ally)
        {
            busy = true;
            Healing = true;
            if (Species.mob != null && Species.mob.HasAnim("attack"))
            {
                // A designed healer plays its cast through the channel.
                attackFrameTime = HealChannel / 6f;
                attackLeft = HealChannel;
                playing = null;
            }
            body.linearVelocity = Vector2.zero;
            float start = damageTaken;
            Vector2 from = transform.G(), to = ally.transform.G();
            var beam = (to - from).sqrMagnitude > 0.01f
                ? LineTelegraph.Show(from, (to - from).normalized, Vector2.Distance(from, to), 0.22f, new Color(Palette.Heal.r, Palette.Heal.g, Palette.Heal.b, 0.55f), HealChannel)
                : null;
            VFX.Ring(ally.transform.position, Palette.Heal, 1f, HealChannel);
            facing = (to - from).sqrMagnitude > 0.01f ? (to - from).normalized : facing;
            float t = 0f;
            while (t < HealChannel)
            {
                t += Time.deltaTime;
                sr.color = Color.Lerp(Species.bodyTint, Palette.Heal, 0.5f + 0.5f * Mathf.Sin(t * 18f));
                // Hit hard enough, or stunned, the spell breaks.
                if (Combatant.IsDead || Combatant.IsStunned || damageTaken - start >= Combatant.MaxHP * HealInterrupt)
                {
                    Interrupts++;
                    if (!Combatant.IsDead) FloatingText.Spawn(transform.position + View.Up * (Combatant.HeadHeight + 0.3f), "Interrupted", Color.white, 0.75f);
                    if (beam != null) Destroy(beam);
                    sr.color = Species.bodyTint;
                    healTimer = Mathf.Max(healTimer, 2f);
                    Healing = false;
                    busy = false;
                    yield break;
                }
                yield return null;
            }
            sr.color = Species.bodyTint;
            Healing = false;
            if (ally != null && !ally.IsDead)
            {
                Heals++;
                float share = ally.rank == MonsterRank.Rare ? HealShare * 0.66f : HealShare;
                ally.Heal(ally.MaxHP * share);
                VFX.Burst(ally.transform.position + View.Up * 0.5f, Palette.Heal, 12, 0.6f);
                var m = ally.GetComponent<MonsterBrain>();
                if (m != null) m.HealedByHealer();
            }
            busy = false;
        }

        // ---------- Grab ----------

        bool CanGrab(Combatant hero, float dist)
        {
            if (Species.archetype != MonsterArchetype.Brute || Combatant.rank == MonsterRank.Boss || specialTimer > 0f) return false;
            var h = hero.GetComponent<HeroController>();
            if (h == null || h.Held) return false;
            float d = Vector2.Distance(transform.G(), hero.transform.G());
            return d > GrabMin && d < GrabReach - 0.3f && Mathf.Abs(HeightOverHero(hero)) < ReachHeight;
        }

        IEnumerator GrabAttack(Combatant hero)
        {
            specialTimer = UnityEngine.Random.Range(7f, 9f) * CooldownScale;
            attackTimer = Mathf.Max(attackTimer, 1f);
            var h = hero.GetComponent<HeroController>();
            Vector2 start = transform.G();
            Vector2 dir = (hero.transform.G() - start).normalized;
            LineTelegraph.Show(start, dir, GrabReach, 1.2f, Palette.EnemyTelegraph, 0.6f);
            yield return Windup(0.6f, new Color(0.85f, 0.5f, 1f));
            if (Combatant.IsDead || Combatant.IsStunned || h == null)
            {
                busy = false;
                yield break;
            }

            // The lunge: the first touch is a catch.
            bool caught = false;
            const float duration = 0.22f;
            for (float t = 0f; t < duration && !Combatant.IsDead; t += Time.fixedDeltaTime)
            {
                body.linearVelocity = dir * GrabReach / duration;
                if (Vector2.Distance(transform.G(), hero.transform.G()) < Combatant.radius + hero.radius + 0.35f)
                {
                    caught = h.Grab(Combatant, GrabHold);
                    break;
                }
                yield return new WaitForFixedUpdate();
            }
            body.linearVelocity = Vector2.zero;

            if (!caught)
            {
                // Missed: it stumbles, open to hits.
                if (!Combatant.IsDead)
                {
                    Combatant.Stun(1f);
                    Combatant.Expose(0.3f, 1.6f);
                }
                busy = false;
                yield break;
            }

            Grabs++;
            Combat.Hit(Combatant, hero, 0.4f, default, false, 0f, -1f, Elements.Mask(Species.element));
            CallVolley();
            float lifeAtGrab = Combatant.HP, squeeze = 0.5f;
            while (h != null && h.HeldBy == Combatant)
            {
                facing = (hero.transform.G() - transform.G()).normalized;
                squeeze -= Time.deltaTime;
                if (squeeze <= 0f)
                {
                    squeeze = 0.5f;
                    Combat.Hit(Combatant, hero, 0.2f, default, false, 0f, -1f, Elements.Mask(Species.element));
                }
                // Hurt enough, it lets go.
                if (lifeAtGrab - Combatant.HP > Combatant.MaxHP * GrabBreak) h.Release(true);
                yield return null;
            }
            if (!Combatant.IsDead && h != null)
            {
                if (h.BrokeFree)
                {
                    Combatant.Stun(0.8f);
                    Combatant.Expose(0.25f, 1.5f);
                }
                else if (!hero.IsDead)
                {
                    // Held to the end: thrown.
                    Throws++;
                    Combat.Hit(Combatant, hero, 0.9f, default, false, 3f, -1f, Elements.Mask(Species.element));
                    CameraFollow.Shake(0.2f);
                }
            }
            busy = false;
        }

        // The pack's (or warband's) shooters fire at once.
        void CallVolley()
        {
            var group = Band != null ? Band.Members : Pack;
            if (group == null) return;
            foreach (var m in group)
                if (m != null && m != this && !m.Combatant.IsDead && m.IsRanged) m.OrderAttack();
        }

        // ---------- Terrain ----------

        bool LevelsHere => ChunkWorld.Instance != null && DungeonSetup.Instance == null && TerrainHeight.World != null;

        static int LevelOf(Vector2 p, out Cell kind)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            kind = WorldGen.CellAt(x, y);
            return kind != Cell.Stairs && WorldGen.Standable(kind) ? WorldGen.ElevAt(x, y) : -1;
        }

        // A shooter looks for standing ground a level or more above the hero, still within its reach.
        void SeekHighGround(Combatant hero, Vector2 pos, float dist)
        {
            if (!LevelsHere) return;
            perchCheck -= Time.deltaTime;
            Vector2 heroPos = hero.transform.G();
            int heroLevel = LevelOf(heroPos, out _);
            if (heroLevel < 0) return;
            if (hasPerch && (Time.time > perchUntil || Vector2.Distance(perch, heroPos) > Species.attackRange || LevelOf(perch, out _) <= heroLevel))
                hasPerch = false;
            if (!hasPerch && perchCheck <= 0f)
            {
                perchCheck = 1.5f;
                hasPerch = FindPerch(pos, heroPos, heroLevel, out perch);
                if (hasPerch) perchUntil = Time.time + 6f;
            }
            if (!hasPerch) return;
            Vector2 to = perch - pos;
            if (to.magnitude < 0.5f)
            {
                desired = Vector2.zero;           // up there: stand and shoot
                perchUntil = Time.time + 6f;
            }
            else
            {
                desired = to.normalized;
                routeGoal = perch;
            }
        }

        bool FindPerch(Vector2 pos, Vector2 heroPos, int heroLevel, out Vector2 best)
        {
            best = Vector2.zero;
            int myLevel = LevelOf(pos, out _);
            if (myLevel > heroLevel) return false;       // already above
            float bestD = float.MaxValue;
            float near = Species.preferredDistance * 0.5f, far = Species.attackRange - 0.5f;
            for (int ring = 0; ring < 3; ring++)
            {
                float r = 2f + ring * 1.6f;
                for (int i = 0; i < 8; i++)
                {
                    float ang = (i * 45f + ring * 22.5f) * Mathf.Deg2Rad;
                    Vector2 c = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    c = new Vector2(Mathf.Floor(c.x) + 0.5f, Mathf.Floor(c.y) + 0.5f);
                    int level = LevelOf(c, out _);
                    if (level <= heroLevel || level > Mathf.Max(myLevel, 0) + 3) continue;
                    float dh = Vector2.Distance(c, heroPos);
                    if (dh < near || dh > far) continue;
                    float d = (c - pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = c; }
                }
            }
            return bestD < float.MaxValue;
        }

        // A hero by a ledge: the charger walks round to the far side first, so its charge knocks the hero over.
        bool LineUpCharge(Combatant hero, Vector2 pos)
        {
            if (!LevelsHere) return false;
            Vector2 heroPos = hero.transform.G();
            if (lineupUntil > 0f && Time.time > lineupUntil)
            {
                lineupUntil = -1f;            // took too long: charge from where it is, this time
                return false;
            }
            if (lineupUntil < 0f) return false;
            if (!FindLedge(heroPos, out Vector2 d))
            {
                lineupUntil = 0f;
                return false;
            }
            if (lineupUntil == 0f) lineupUntil = Time.time + 3f;
            ledgePending = d;
            Vector2 spot = heroPos - d * 4.5f;
            if (Vector2.Distance(pos, spot) < 1.3f)
            {
                ledgeDir = ledgePending;
                lineupUntil = 0f;
                Shoves++;
                specialTimer = UnityEngine.Random.Range(5f, 7f) * CooldownScale;
                StartCoroutine(ChargeAttack(hero));
                return true;
            }
            desired = (spot - pos).normalized;
            routeGoal = spot;
            return true;
        }

        // A drop of a few levels right beside the hero, with standing room on the hero's level behind it for a run-up.
        public static bool FindLedge(Vector2 heroPos, out Vector2 dir)
        {
            dir = Vector2.zero;
            int heroLevel = LevelOf(heroPos, out _);
            if (heroLevel <= 0) return false;
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                bool drop = false;
                for (float step = 1f; step <= 1.9f && !drop; step += 0.8f)
                {
                    int level = LevelOf(heroPos + d * step, out _);
                    if (level >= 0 && level < heroLevel && heroLevel - level <= WorldGen.LeapDrop) drop = true;
                    else if (level != heroLevel) break;
                }
                if (!drop) continue;
                if (LevelOf(heroPos - d * 4.5f, out _) != heroLevel || LevelOf(heroPos - d * 2.5f, out _) != heroLevel) continue;
                dir = d;
                return true;
            }
            return false;
        }

        // A leaper on the ledge above the hero drops onto it.
        bool TryPounce(Combatant hero, Vector2 pos, float dist)
        {
            if (Mobility != Mobility.Leap || ground == null) return false;
            pounceTimer -= Time.deltaTime;
            if (pounceTimer > 0f) return false;
            float d = Vector2.Distance(pos, hero.transform.G());
            float over = HeightOverHero(hero);
            if (d < 1.2f || d > PounceReach) return false;
            if (over < WorldGen.LevelHeight * 0.8f || over > WorldGen.LevelHeight * WorldGen.LeapDrop + 0.1f) return false;
            pounceTimer = UnityEngine.Random.Range(6f, 8f);
            StartCoroutine(Pounce(hero));
            return true;
        }

        IEnumerator Pounce(Combatant hero)
        {
            busy = true;
            body.linearVelocity = Vector2.zero;
            Vector2 from = transform.G();
            Vector2 to = hero.transform.G() + HeroHabits.HeroVelocity * 0.3f;
            AreaEffect.Spawn(Combatant, to, new AreaSpec
            {
                radius = 1.5f, delay = 0.75f, damageMult = 1.3f, color = new Color(1f, 0.35f, 0.2f), knockback = 1.5f,
            });
            yield return Windup(0.45f, new Color(1f, 0.45f, 0.3f));
            if (Combatant.IsDead || Combatant.IsStunned)
            {
                busy = false;
                yield break;
            }
            Pounces++;
            var col = GetComponent<Collider>();
            LevelPass.Register(col);
            const float flight = 0.3f;
            for (float t = 0f; t < flight && !Combatant.IsDead; t += Time.fixedDeltaTime)
            {
                body.MovePosition(Vector2.Lerp(from, to, t / flight));
                visual.localPosition = Vector3.up * Mathf.Sin(t / flight * Mathf.PI) * 0.9f;
                yield return new WaitForFixedUpdate();
            }
            visual.localPosition = Vector3.zero;
            LevelPass.Unregister(col);
            CameraFollow.Shake(0.08f);
            busy = false;
        }

        // ---------- Habits ----------

        // Chasers run to where a kiting hero is going, not where it is.
        Vector2 CutAcross(Combatant hero, Vector2 pos, float dist, Vector2 toHero)
        {
            if (!HeroHabits.Kiting) return toHero;
            var a = Species.archetype;
            if (a != MonsterArchetype.Chaser && a != MonsterArchetype.Swarmling && a != MonsterArchetype.Charger) return toHero;
            float ahead = Mathf.Clamp(dist / Mathf.Max(1f, Combatant.MoveSpeed), 0f, 1.2f);
            Vector2 d = hero.transform.G() + HeroHabits.HeroVelocity * ahead - pos;
            return d.sqrMagnitude > 0.01f ? d.normalized : toHero;
        }
    }
}
