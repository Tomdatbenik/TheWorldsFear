using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TWF
{
    // The World's Fear is the final boss; the four guardians must be slain first. (Titan is the old
    // generated boss, kept so older saves still load.)
    public enum BossKind { WorldsFear, Titan, Spider, Skeleton, Zombie, Slime }

    // A boss fight: it leaps from wall to wall around a round arena, with more attacks each phase.
    // Pillars block the view (and the attacks), so you rotate the camera to keep track.
    //   World's Fear: the original boss art (armor, head, two floating hands) with its hand slams,
    //                 fire and risk spot.
    //   Titan: a huge generated creature (different per gate) with shockwaves, spirals and charges.
    [RequireComponent(typeof(Combatant))]
    public class BossTitan : MonoBehaviour
    {
        public static readonly BossKind[] Guardians = { BossKind.Slime, BossKind.Spider, BossKind.Zombie, BossKind.Skeleton };

        public static string BossName(BossKind k)
        {
            switch (k)
            {
                case BossKind.WorldsFear: return "The World's Fear";
                case BossKind.Spider: return "Vexa, the Brood Mother";
                case BossKind.Skeleton: return "The Bone King";
                case BossKind.Zombie: return "The Rotting Colossus";
                case BossKind.Slime: return "The Slime Sovereign";
                default: return "The Titan";
            }
        }

        public static string LairName(BossKind k)
        {
            switch (k)
            {
                case BossKind.WorldsFear: return "Lair of the World's Fear";
                case BossKind.Spider: return "The Brood Nest";
                case BossKind.Skeleton: return "The Bone Crypt";
                case BossKind.Zombie: return "The Rotting Pit";
                case BossKind.Slime: return "The Slime Pool";
                default: return "Titan's Arena";
            }
        }

        static string Subtitle(BossKind k)
        {
            switch (k)
            {
                case BossKind.WorldsFear: return "The terror at the end of the world";
                case BossKind.Spider: return "Guardian of the Forest";
                case BossKind.Skeleton: return "Guardian of the Ruins";
                case BossKind.Zombie: return "Guardian of the Marsh";
                case BossKind.Slime: return "Guardian of the Deep";
                default: return $"Titan of the {WorldManager.PendingBossBiome}";
            }
        }

        // Each boss has an element (and so a weakness).
        public static ElementPart ElementOf(BossKind k)
        {
            switch (k)
            {
                case BossKind.Slime: return ElementPart.Frost;
                case BossKind.Spider: return ElementPart.Poison;
                case BossKind.Zombie: return ElementPart.Shadow;
                case BossKind.Skeleton: return ElementPart.Arcane;
                case BossKind.WorldsFear: return ElementPart.Fire;
                default: return ElementPart.Physical;
            }
        }

        // Each guardian fights on its own home ground.
        public static Biome ArenaBiome(BossKind k, Biome gate)
        {
            switch (k)
            {
                case BossKind.WorldsFear: return Biome.Ruins;
                case BossKind.Spider: return Biome.Forest;
                case BossKind.Skeleton: return Biome.Ruins;
                case BossKind.Zombie: return Biome.Marsh;
                default: return gate == Biome.Village ? Biome.Ember : gate;
            }
        }

        static Color SlimeTint(Biome b) => b == Biome.Ember ? new Color(1f, 0.45f, 0.2f) : b == Biome.Ruins ? new Color(0.55f, 0.5f, 0.95f) : b == Biome.Marsh ? new Color(0.55f, 0.7f, 0.25f) : new Color(0.35f, 0.85f, 0.4f);

        public float reach = 30f;      // max length of rings, beams and fire lines
        public string returnScene = "World";

        Combatant self;
        SpriteRenderer sr;          // armor (body)
        SpriteRenderer head;
        Transform visual;           // body billboard
        Vector3 baseScale;
        Hand leftHand, rightHand;
        GameDatabase db;
        BossKind kind;
        MonsterLook look;           // Titan only
        bool introDone;

        // One of the boss's floating hands (the original boss art).
        class Hand
        {
            public Transform root, bb;
            public SpriteRenderer sr, shadow;
            public float side;      // -1 left, +1 right (as seen on screen)
            public bool busy;
        }
        int phase = 1;
        bool busy, dead;
        float animT;
        int frame;
        Vector2 lastMove;

        Combatant Hero => HeroController.Instance != null ? HeroController.Instance.Combatant : null;

        public static BossTitan Spawn(Vector2 at, int level, Biome biome, BossKind kind)
        {
            var go = new GameObject(kind.ToString());
            go.transform.position = at;
            go.layer = LayerMask.NameToLayer("Monsters");
            var c = go.AddComponent<Combatant>();
            c.team = Team.Enemy;
            c.rank = MonsterRank.Boss;
            c.level = level;
            c.radius = 1.8f;
            c.height = 5.5f;
            c.destroyOnDeath = false;
            if (biome == Biome.Village) biome = Biome.Ember;
            c.displayName = kind == BossKind.Titan
                ? "The " + MonsterGenerator.MakeName(new System.Random(level * 31 + (int)biome), biome, MonsterArchetype.Brute) + " Titan"
                : BossName(kind);
            c.Stats.SetBase(StatType.MaxHP, MonsterGenerator.BaseHP(level) * (kind == BossKind.WorldsFear ? 24f : 18f));
            c.Stats.SetBase(StatType.Damage, MonsterGenerator.BaseDamage(level) * 1.5f);
            c.Stats.SetBase(StatType.Armor, MonsterGenerator.BaseArmor(level) * 1.5f);
            c.Stats.SetBase(StatType.MoveSpeed, 4f);
            c.Stats.SetBase(StatType.AttackSpeed, 1f);

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 1.5f;

            c.element = ElementOf(kind);
            var t = go.AddComponent<BossTitan>();
            t.db = GameDatabase.Instance;
            t.kind = kind;
            var v = Billboard.Create(go.transform, "Visual");
            if (kind == BossKind.WorldsFear)
            {
                // Body: the original armor with the head on top, standing as a billboard.
                t.sr = Part(v, t.db.bossArmor, 1.1f, 0f);
                float armorTop = t.db.bossArmor.bounds.size.y * 1.1f;
                t.head = Part(v, t.db.bossHead, 1.1f, armorTop * 0.78f);
                t.leftHand = t.MakeHand(-1f);
                t.rightHand = t.MakeHand(1f);
            }
            else
            {
                float scale = 3.2f;
                switch (kind)
                {
                    case BossKind.Spider: t.look = SpiderSpriteGenerator.Generate(level * 7919 + 5, biome); scale = 4.6f; c.height = 3.6f; break;
                    case BossKind.Skeleton: t.look = BossSpriteGenerator.Generate(BossBody.Skeleton, Color.white); scale = 3.3f; break;
                    case BossKind.Zombie: t.look = BossSpriteGenerator.Generate(BossBody.Zombie, new Color(0.5f, 0.6f, 0.4f)); scale = 3.4f; break;
                    case BossKind.Slime: t.look = BossSpriteGenerator.Generate(BossBody.Slime, SlimeTint(biome)); scale = 3.6f; c.height = 4.4f; break;
                    default:
                        // A huge generated beast, different for each gate.
                        t.look = MonsterGenerator.Create(level * 7919 + (int)biome * 13, biome, MonsterArchetype.Brute).look;
                        scale = 4.5f;
                        break;
                }
                var holder = new GameObject("Body");
                holder.transform.SetParent(v, false);
                holder.transform.localScale = Vector3.one * scale;
                t.sr = holder.AddComponent<SpriteRenderer>();
                t.sr.sprite = t.look.frames[0];
                RenderAssets.MakeLit(t.sr);
            }
            t.visual = v;
            t.baseScale = v.localScale;
            var shadow = GameAssets.CreateWorldSprite("Shadow", GameAssets.SoftCircle, go.transform, new Color(0, 0, 0, 0.3f), SortOrder.Units - 1, 4.5f);
            shadow.transform.localScale = new Vector3(shadow.transform.localScale.x, shadow.transform.localScale.y * 0.5f, 1f);
            shadow.transform.localPosition = new Vector3(0, 0, Ground.DecalZ);
            c.CacheRenderers();
            c.ResetLife();
            return t;
        }

        // A sprite part inside a billboard, standing on its bottom edge at local height y.
        static SpriteRenderer Part(Transform parent, Sprite sprite, float scale, float y)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = new Vector3(0, y + sprite.bounds.extents.y * scale - sprite.bounds.center.y * scale, 0);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            RenderAssets.MakeLit(r);
            return r;
        }

        Hand MakeHand(float side)
        {
            var h = new Hand { side = side };
            h.root = new GameObject(side < 0 ? "Left Hand" : "Right Hand").transform;
            h.root.position = transform.position;
            h.bb = Billboard.Create(h.root, "Visual");
            h.sr = Part(h.bb, db.bossHand, 1f, 0f);
            h.sr.flipX = side < 0;
            var sh = new GameObject("HandShadow");
            sh.transform.SetParent(h.root, false);
            sh.transform.localPosition = new Vector3(0, 0, Ground.DecalZ);
            h.shadow = sh.AddComponent<SpriteRenderer>();
            h.shadow.sprite = db.bossHandShadow;
            h.shadow.color = new Color(1, 1, 1, 0.7f);
            RenderAssets.MakeUnlit(h.shadow);
            return h;
        }

        // Where a resting hand floats: beside the body, as seen from the camera.
        Vector3 HandRest(Hand h, float t) =>
            transform.position + View.Right * (h.side * 3.2f) + Vector3.back * (2.2f + Mathf.Sin(t * 2f + h.side) * 0.3f);

        void LateUpdate()
        {
            if (leftHand == null) return;
            foreach (var h in new[] { leftHand, rightHand })
            {
                if (h.busy) continue;
                var rest = HandRest(h, Time.time);
                h.root.position = Vector3.Lerp(h.root.position, new Vector3(rest.x, rest.y, 0f), Time.deltaTime * 8f);
                h.bb.localPosition = new Vector3(0, 0, rest.z);
                h.shadow.transform.localScale = Vector3.one * 0.8f;
            }
        }

        void OnDestroy()
        {
            if (leftHand != null && leftHand.root) Destroy(leftHand.root.gameObject);
            if (rightHand != null && rightHand.root) Destroy(rightHand.root.gameObject);
        }

        void Awake() => self = GetComponent<Combatant>();

        void Start()
        {
            self.Died += OnDied;
            ZoneLighting.Instance?.SetBoss();
            StartCoroutine(Brain());
        }

        void Update()
        {
            if (dead) return;
            // Phases at 66% and 33% life.
            int p = self.HPFraction > 0.66f ? 1 : self.HPFraction > 0.33f ? 2 : 3;
            if (p != phase)
            {
                phase = p;
                GameUI.Instance?.Toast($"{self.displayName} {(phase == 3 ? "is enraged!" : "grows stronger!")}", new Color(1f, 0.4f, 0.3f));
                Sound.Play(RoarClip, transform.position, 0.8f);
                CameraFollow.Shake(0.4f);
            }
            Animate();
        }

        void Animate()
        {
            if (look != null)
            {
                animT += Time.deltaTime;
                if (animT > (busy ? 0.15f : 0.35f)) { animT = 0f; frame++; }
                Vector2 onScreen = View.ToScreen(lastMove);
                bool away = lastMove.sqrMagnitude > 0.01f && onScreen.y > Mathf.Abs(onScreen.x);
                var frames = away && look.backFrames != null ? look.backFrames : look.frames;
                sr.sprite = frames[frame % frames.Length];
                if (Mathf.Abs(onScreen.x) > 0.1f) sr.flipX = onScreen.x < 0f;
            }
            if (head != null) head.sprite = phase == 3 && db.bossHeadSad != null ? db.bossHeadSad : db.bossHead;
            // Breathing bob.
            if (!busy) visual.localPosition = new Vector3(0, 0, -Mathf.Abs(Mathf.Sin(Time.time * 1.6f)) * 0.15f);
        }

        float Speed => phase == 3 ? 1.35f : 1f;

        // ---------- Behaviour ----------

        // Entrance: letterbox, camera pans to the boss as it crashes down, title card, back to the hero.
        // Where the boss's face is right now (the middle of the head, or the top of a generated body).
        Light faceLight;   // key light on the face during the intro close-up

        Vector3 FacePoint()
        {
            if (head != null) return head.bounds.center;
            // Generated bodies: a point up the sprite where its face is drawn.
            // (fractions of the whole sprite, which the slime and spider only partly fill)
            float frac = kind == BossKind.Spider ? 0.45f : kind == BossKind.Slime ? 0.36f : kind == BossKind.Zombie ? 0.76f : 0.78f;
            float h = sr.sprite.bounds.size.y * sr.transform.lossyScale.y;
            return sr.transform.position + sr.transform.up * (h * frac);
        }

        // Eases the camera's pitch and distance while its focus blends between the feet (f=0) and the face (f=1).
        IEnumerator CameraMove(CameraFollow cam, Transform anchor, float p0, float p1, float d0, float d1, float f0, float f1, float dur)
        {
            for (float t = 0f; ; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                cam.pitch = Mathf.Lerp(p0, p1, k);
                cam.distance = Mathf.Lerp(d0, d1, k);
                var face = FacePoint();
                float f = Mathf.Lerp(f0, f1, k);
                var p = Vector3.Lerp(transform.position, face, f);
                anchor.position = new Vector3(p.x, p.y, 0f);
                cam.focusHeight = -p.z;
                if (faceLight != null) faceLight.transform.position = face + (cam.transform.position - face).normalized * 2.5f;
                if (t >= dur) break;
                yield return null;
            }
        }

        IEnumerator Intro()
        {
            MobaInput.Blocked = true;
            self.Invulnerable = true;
            var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            var heroT = HeroController.Instance != null ? HeroController.Instance.transform : null;
            GameUI.Instance?.SetCinematic(true);
            visual.gameObject.SetActive(false);
            if (leftHand != null) { leftHand.root.gameObject.SetActive(false); rightHand.root.gameObject.SetActive(false); }
            yield return new WaitForSeconds(0.6f);
            if (cam != null) cam.target = transform;
            yield return new WaitForSeconds(1.1f);

            // Crash down from the sky.
            visual.gameObject.SetActive(true);
            float t = 0f;
            while (t < 0.7f)
            {
                t += Time.deltaTime;
                visual.localPosition = Vector3.back * Mathf.Lerp(22f, 0f, (t / 0.7f) * (t / 0.7f));
                yield return null;
            }
            visual.localPosition = Vector3.zero;
            CameraFollow.Shake(0.6f);
            VFX.Ring(transform.position, new Color(1f, 0.6f, 0.4f), 6f, 0.6f);
            VFX.Burst(transform.position, new Color(0.8f, 0.7f, 0.6f), 40, 4f);
            Sound.Play(Sfx.Explosion, transform.position, 0.9f);
            if (leftHand != null)
            {
                leftHand.root.gameObject.SetActive(true);
                rightHand.root.gameObject.SetActive(true);
                leftHand.root.position = rightHand.root.position = transform.position;
            }
            yield return new WaitForSeconds(0.4f);

            // Push in on the boss's face for the title card. The camera follows an anchor that
            // glides from the boss's feet to the middle of its face (which leans with the billboard).
            float pitch0 = cam != null ? cam.pitch : 52f, dist0 = cam != null ? cam.distance : 17f;
            var anchor = new GameObject("Face Anchor").transform;
            anchor.position = transform.position;
            faceLight = PointLights.Attach(anchor, new Color(1f, 0.8f, 0.7f), 8f, 2.5f, Vector3.zero, true);
            if (cam != null)
            {
                cam.target = anchor;
                yield return CameraMove(cam, anchor, pitch0, 12f, dist0, 8.5f, 0f, 1f, 1f);
            }
            Sound.Play(RoarClip, transform.position, 1f);
            CameraFollow.Shake(0.25f);
            Codex.SeeBoss(kind);
            var weak = Elements.WeaknessOf(self.element);
            GameUI.Instance?.ShowBossTitle(self.displayName, Subtitle(kind) + (weak.HasValue ? $"  ·  {Elements.Tag(self.element)}, weak to {Elements.Tag(weak.Value)}" : ""));
            yield return Windup(0.8f);
            yield return new WaitForSeconds(1.4f);
            if (cam != null) yield return CameraMove(cam, anchor, 12f, pitch0, 8.5f, dist0, 1f, 0f, 0.7f);
            Destroy(anchor.gameObject);

            if (cam != null && heroT != null) cam.target = heroT;
            yield return new WaitForSeconds(0.6f);
            GameUI.Instance?.SetCinematic(false);
            GameUI.Instance?.ShowBossBar(self);
            MobaInput.Blocked = false;
            self.Invulnerable = false;
            introDone = true;
        }

        IEnumerator Brain()
        {
            yield return Intro();
            yield return new WaitForSeconds(0.5f);
            var attacks = new List<System.Func<IEnumerator>>();
            while (!dead)
            {
                if (Hero == null || Hero.IsDead) { yield return null; continue; }

                // Always reposition first: leap to the wall or run along it.
                if (kind == BossKind.Skeleton) yield return BoneTeleport(WallPoint());
                else if (kind == BossKind.Zombie) yield return Lumber(WallPoint());
                else if (kind == BossKind.Slime) yield return SlimeHop();
                else yield return Random.value < 0.6f ? Leap(WallPoint()) : WallRun();

                attacks.Clear();
                // Each guardian has one signature: the spider slows, the skeleton shoots, the zombie
                // brings a horde, the slime jumps and sends shockwaves. The World's Fear does it all.
                if (kind == BossKind.WorldsFear)
                {
                    attacks.Add(HandSlam);    // its signature: fists smashing down, shockwaves from the hands
                    attacks.Add(HandSlam);
                    attacks.Add(FireRain);
                    attacks.Add(WebVolley);   // spider: slow
                    attacks.Add(BoneVolley);  // skeleton: ranged
                    attacks.Add(HandQuake);   // slime: jumps and shockwaves (from its hands)
                    if (Hero != null && Vector2.Distance(Hero.transform.position, transform.position) < 6f) attacks.Add(Clap);
                    if (phase >= 2) { attacks.Add(FireLine); attacks.Add(RiskSpot); attacks.Add(Horde); attacks.Add(WebTraps); attacks.Add(BoneSpikes); }
                    if (phase >= 3) { attacks.Add(Sweep); attacks.Add(HandSlam); attacks.Add(HandQuake); attacks.Add(Spiral); }
                }
                else if (kind == BossKind.Spider)
                {
                    attacks.Add(WebVolley);
                    attacks.Add(WebTraps);
                    attacks.Add(Pounce);
                    if (phase >= 2) { attacks.Add(WebVolley); attacks.Add(WallRun); }
                    if (phase >= 3) { attacks.Add(WebTraps); attacks.Add(Pounce); }
                }
                else if (kind == BossKind.Skeleton)
                {
                    attacks.Add(BoneVolley);
                    attacks.Add(BoneSpikes);
                    attacks.Add(Spiral);
                    if (phase >= 2) { attacks.Add(Sweep); attacks.Add(BoneVolley); }
                    if (phase >= 3) { attacks.Add(BoneSpikes); attacks.Add(Spiral); }
                }
                else if (kind == BossKind.Zombie)
                {
                    attacks.Add(Horde);
                    attacks.Add(Horde);
                    attacks.Add(Vomit);
                    if (phase >= 2) { attacks.Add(Charge); attacks.Add(Horde); }
                    if (phase >= 3) { attacks.Add(Horde); attacks.Add(Vomit); }
                }
                else if (kind == BossKind.Slime)
                {
                    attacks.Add(Bounce);
                    attacks.Add(Slam);
                    attacks.Add(Quake);
                    if (phase >= 2) { attacks.Add(Bounce); attacks.Add(Quake); }
                    if (phase >= 3) { attacks.Add(Bounce); attacks.Add(Slam); }
                }
                else
                {
                    attacks.Add(Slam);
                    attacks.Add(Spiral);
                    attacks.Add(Charge);
                    if (phase >= 2) { attacks.Add(MeteorRain); attacks.Add(Summon); attacks.Add(Charge); }
                    if (phase >= 3) { attacks.Add(Sweep); attacks.Add(Slam); attacks.Add(Spiral); }
                }
                int combo = phase == 3 ? 2 : 1;
                for (int i = 0; i < combo && !dead; i++)
                    yield return attacks[Random.Range(0, attacks.Count)]();
                yield return new WaitForSeconds(Random.Range(0.6f, 1.2f) / Speed);
            }
        }

        static bool InArena(Vector2 p, float margin) => BossArenaSetup.Instance == null || BossArenaSetup.Instance.Inside(p, margin);
        static Vector2 ClampArena(Vector2 p, float margin) => BossArenaSetup.Instance != null ? BossArenaSetup.Instance.ClampInside(p, margin) : p;

        Vector2 WallPoint()
        {
            // A point on the wall, preferring the far side from the hero.
            Vector2 hero = Hero != null ? (Vector2)Hero.transform.position : Vector2.zero;
            var arena = BossArenaSetup.Instance;
            return arena != null ? arena.EdgePoint(hero) : -hero.normalized * 10f;
        }

        IEnumerator Leap(Vector2 to)
        {
            busy = true;
            Vector2 from = transform.position;
            lastMove = to - from;
            var landing = AreaEffect.Spawn(self, to, new AreaSpec { radius = 3.6f, delay = 1.0f / Speed, damageMult = 1.4f, color = new Color(1f, 0.3f, 0.2f), knockback = 3f });
            self.Targetable = false;
            float t = 0f, dur = 1.0f / Speed;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                Vector2 p = Vector2.Lerp(from, to, k);
                transform.position = p;
                // Arc up into the air (up is -Z).
                visual.localPosition = Vector3.back * Mathf.Sin(k * Mathf.PI) * 6f;
                if (leftHand != null)
                    foreach (var hand in new[] { leftHand, rightHand }) if (!hand.busy) hand.bb.localPosition = Vector3.back * (2.2f + Mathf.Sin(k * Mathf.PI) * 6f);
                yield return null;
            }
            visual.localPosition = Vector3.zero;
            self.Targetable = true;
            CameraFollow.Shake(0.45f);
            VFX.Burst(transform.position, new Color(0.8f, 0.7f, 0.6f), 30, 3f);
            busy = false;
        }

        Vector2 EdgeAlong(float ang, float margin)
        {
            var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float r = 2f;
            for (float k = 2f; k < reach; k += 0.5f)
            {
                if (!InArena(d * k, margin)) break;
                r = k;
            }
            return d * r;
        }

        IEnumerator WallRun()
        {
            busy = true;
            Vector2 p = transform.position;
            float ang = Mathf.Atan2(p.y, p.x);
            float dir = Random.value < 0.5f ? 1f : -1f;
            float t = 0f;
            float fireTimer = 0f;
            while (t < 2.6f)
            {
                t += Time.deltaTime;
                ang += dir * Time.deltaTime * 0.6f * Speed;
                // Follow the arena's edge (it isn't round): the farthest open spot along this angle.
                var target = EdgeAlong(ang, 2.2f);
                var np = Vector2.MoveTowards(transform.position, target, 14f * Speed * Time.deltaTime);
                lastMove = np - (Vector2)transform.position;
                transform.position = np;
                fireTimer -= Time.deltaTime;
                if (fireTimer <= 0f && Hero != null)
                {
                    fireTimer = 0.35f / Speed;
                    Vector2 d = ((Vector2)Hero.transform.position - np).normalized;
                    var spec = new ProjectileSpec { speed = 9f, range = 30f, hitRadius = 0.45f, damageMult = 0.5f, color = new Color(1f, 0.5f, 0.2f), size = 0.7f };
                    if (kind == BossKind.Spider) { spec.color = WebColor; spec.status = StatusSpec.Slow(0.45f, 2f); }
                    Projectile.Fire(self, np + d * 1.5f, d, spec);
                }
                yield return null;
            }
            busy = false;
        }

        // Expanding ring shockwaves with a gap you have to step into.
        IEnumerator Slam()
        {
            busy = true;
            yield return Windup(0.7f);
            CameraFollow.Shake(0.35f);
            int waves = phase >= 2 ? 3 : 2;
            for (int i = 0; i < waves; i++)
            {
                RingWave.Spawn(self, transform.position, Random.Range(0f, 360f), 9f * Speed, reach);
                yield return new WaitForSeconds(0.6f / Speed);
            }
            busy = false;
        }

        // Rotating bullet spiral.
        IEnumerator Spiral()
        {
            busy = true;
            yield return Windup(0.5f);
            int arms = phase >= 2 ? 6 : 4;
            float rot = 0f;
            float t = 0f;
            while (t < 2.4f)
            {
                for (int a = 0; a < arms; a++)
                {
                    float ang = (rot + a * 360f / arms) * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    Projectile.Fire(self, (Vector2)transform.position + d * 1.6f, d, new ProjectileSpec
                    {
                        speed = 7f * Speed, range = reach, hitRadius = 0.4f, damageMult = 0.45f, color = Theme, size = 0.6f,
                    });
                }
                rot += 14f;
                t += 0.16f;
                yield return new WaitForSeconds(0.16f / Speed);
            }
            busy = false;
        }

        // Telegraphed charge straight across the arena.
        IEnumerator Charge()
        {
            busy = true;
            if (Hero == null) { busy = false; yield break; }
            Vector2 start = transform.position;
            Vector2 dir = ((Vector2)Hero.transform.position - start).normalized;
            float len = reach * 0.8f;
            LineTelegraph.Show(start, dir, len, 3f, Palette.EnemyTelegraph, 0.9f / Speed);
            yield return Windup(0.9f / Speed);
            bool hit = false;
            float t = 0f, dur = 0.5f;
            while (t < dur)
            {
                t += Time.deltaTime;
                Vector2 p = start + dir * len * (t / dur);
                if (!InArena(p, 1.6f)) break;
                if (Physics2D.OverlapCircle(p + dir * 1.2f, 0.8f, Walls.Mask) is Collider2D c && c.attachedRigidbody != GetComponent<Rigidbody2D>()) break;
                lastMove = dir;
                transform.position = p;
                if (!hit && Hero != null && Vector2.Distance(p, Hero.transform.position) < 2f)
                {
                    hit = true;
                    Combat.Hit(self, Hero, 1.6f, StatusSpec.Stun(0.8f), false, 3f);
                }
                yield return null;
            }
            CameraFollow.Shake(0.3f);
            busy = false;
        }

        IEnumerator MeteorRain()
        {
            busy = true;
            yield return Windup(0.4f);
            int n = phase >= 3 ? 14 : 9;
            for (int i = 0; i < n && Hero != null; i++)
            {
                Vector2 at = (Vector2)Hero.transform.position + Random.insideUnitCircle * 4f;
                at = ClampArena(at, 1f);
                AreaEffect.Spawn(self, at, new AreaSpec { radius = 1.7f, delay = 1.1f, damageMult = 0.9f, color = new Color(1f, 0.45f, 0.15f), status = StatusSpec.Burn(0.3f, 2f) });
                yield return new WaitForSeconds(0.15f);
            }
            busy = false;
        }

        IEnumerator Summon()
        {
            busy = true;
            yield return Windup(0.6f);
            BossHelpers.SpawnMinions(transform.position * 0.6f, phase >= 3 ? 4 : 2);
            busy = false;
        }

        // A beam from the boss that sweeps around the arena.
        IEnumerator Sweep()
        {
            busy = true;
            if (Hero == null) { busy = false; yield break; }
            float start = Mathf.Atan2(Hero.transform.position.y - transform.position.y, Hero.transform.position.x - transform.position.x) * Mathf.Rad2Deg - 50f;
            yield return SweepBeam.Run(self, transform.position, start, 100f, reach, 2.2f / Speed);
            busy = false;
        }

        // ---------- The original boss's attacks, in 2.5D ----------

        // A hand rises over the hero (fist raised), its shadow tracks them, then it slams down.
        IEnumerator HandSlam()
        {
            busy = true;
            var hands = new List<Hand> { leftHand, rightHand };
            // Fists rain down one after another, alternating hands; each impact sends a shockwave.
            int count = phase + 1;
            int first = Random.Range(0, 2);
            for (int i = 0; i < count && Hero != null; i++)
            {
                var h = hands[(i + first) % 2];
                while (h.busy) yield return null;
                StartCoroutine(OneSlam(h, true));
                yield return new WaitForSeconds(0.6f / Speed);
            }
            yield return new WaitForSeconds(1.2f / Speed);
            busy = false;
        }

        IEnumerator OneSlam(Hand h, bool wave = false)
        {
            h.busy = true;
            h.sr.sprite = db.bossFistReady;
            float track = 1.1f / Speed, t = 0f;
            Vector2 target = Hero.transform.position;
            while (t < track && Hero != null)
            {
                t += Time.deltaTime;
                target = Vector2.Lerp(target, Hero.transform.position, Time.deltaTime * 4f);
                h.root.position = Vector3.Lerp(h.root.position, target, Time.deltaTime * 6f);
                h.bb.localPosition = new Vector3(0, 0, -4.5f);
                h.shadow.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 2f, t / track);
                yield return null;
            }
            h.sr.sprite = db.bossFist;
            float drop = 0.12f;
            for (float d = 0; d < drop; d += Time.deltaTime)
            {
                h.bb.localPosition = new Vector3(0, 0, Mathf.Lerp(-4.5f, 0f, d / drop));
                yield return null;
            }
            h.bb.localPosition = Vector3.zero;
            CameraFollow.Shake(0.3f);
            VFX.Ring(h.root.position, new Color(1f, 0.85f, 0.7f), 2.2f, 0.3f);
            VFX.Burst(h.root.position, new Color(0.7f, 0.6f, 0.5f), 18, 1.5f);
            foreach (var c in Combat.HostilesInRadius(Team.Enemy, h.root.position, 1.9f))
                Combat.Hit(self, c, 1.3f, StatusSpec.Stun(0.6f), false, 2f);
            if (wave) RingWave.Spawn(self, h.root.position, Random.Range(0f, 360f), 8f * Speed, phase >= 3 ? reach : 14f);
            yield return new WaitForSeconds(0.5f);
            h.sr.sprite = db.bossHand;
            h.busy = false;
        }

        // World's Fear: leaps in, then both fists hammer the ground at once and shockwaves roll out
        // from each hand (its version of the slime's jumps and shockwaves).
        IEnumerator HandQuake()
        {
            if (Hero == null) yield break;
            if (phase >= 2) yield return Leap(ClampArena(Vector2.Lerp(transform.position, HeroPos, 0.6f), 2f));
            busy = true;
            leftHand.busy = rightHand.busy = true;
            leftHand.sr.sprite = rightHand.sr.sprite = db.bossFistReady;
            Vector2 dir = ToHero, perp = new Vector2(-dir.y, dir.x);
            Vector2 lp = ClampArena((Vector2)transform.position + dir * 2.5f - perp * 3f, 1f);
            Vector2 rp = ClampArena((Vector2)transform.position + dir * 2.5f + perp * 3f, 1f);
            AreaEffect.Spawn(self, lp, new AreaSpec { radius = 2f, delay = 0.9f / Speed, damageMult = 1.2f, color = new Color(1f, 0.35f, 0.25f), knockback = 2f });
            AreaEffect.Spawn(self, rp, new AreaSpec { radius = 2f, delay = 0.9f / Speed, damageMult = 1.2f, color = new Color(1f, 0.35f, 0.25f), knockback = 2f });
            // Raise both fists high over their targets...
            for (float t = 0f, dur = 0.75f / Speed; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                leftHand.root.position = Vector3.Lerp(leftHand.root.position, lp, Time.deltaTime * 8f);
                rightHand.root.position = Vector3.Lerp(rightHand.root.position, rp, Time.deltaTime * 8f);
                leftHand.bb.localPosition = rightHand.bb.localPosition = new Vector3(0, 0, -Mathf.Lerp(1.5f, 5f, k));
                yield return null;
            }
            // ...and bring them down together.
            leftHand.sr.sprite = rightHand.sr.sprite = db.bossFist;
            for (float d = 0f; d < 0.15f; d += Time.deltaTime)
            {
                leftHand.bb.localPosition = rightHand.bb.localPosition = new Vector3(0, 0, Mathf.Lerp(-5f, 0f, d / 0.15f));
                yield return null;
            }
            leftHand.bb.localPosition = rightHand.bb.localPosition = Vector3.zero;
            CameraFollow.Shake(0.55f);
            int rings = phase >= 3 ? 3 : 2;
            for (int i = 0; i < rings && !dead; i++)
            {
                foreach (var h in new[] { leftHand, rightHand })
                {
                    VFX.Burst(h.root.position, new Color(0.7f, 0.6f, 0.5f), 16, 1.5f);
                    RingWave.Spawn(self, h.root.position, Random.Range(0f, 360f), 8.5f * Speed, reach);
                }
                yield return new WaitForSeconds(0.55f / Speed);
            }
            leftHand.sr.sprite = rightHand.sr.sprite = db.bossHand;
            leftHand.busy = rightHand.busy = false;
            busy = false;
        }

        // Both hands clap together in front of the boss.
        IEnumerator Clap()
        {
            busy = true;
            if (Hero == null) { busy = false; yield break; }
            Vector2 dir = ((Vector2)Hero.transform.position - (Vector2)transform.position).normalized;
            Vector2 center = (Vector2)transform.position + dir * 3f;
            var perp = new Vector2(-dir.y, dir.x);
            leftHand.busy = rightHand.busy = true;
            leftHand.sr.sprite = rightHand.sr.sprite = db.bossFistReady;
            AreaEffect.Spawn(self, center, new AreaSpec { radius = 2.4f, delay = 0.75f / Speed, damageMult = 1.2f, color = new Color(1f, 0.35f, 0.25f), knockback = 3f });
            float t = 0f, dur = 0.75f / Speed;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                float spread = Mathf.Lerp(4f, 0.6f, k * k);
                leftHand.root.position = center - perp * spread;
                rightHand.root.position = center + perp * spread;
                leftHand.bb.localPosition = rightHand.bb.localPosition = new Vector3(0, 0, -1.2f);
                yield return null;
            }
            CameraFollow.Shake(0.35f);
            leftHand.sr.sprite = rightHand.sr.sprite = db.bossHand;
            yield return new WaitForSeconds(0.4f);
            leftHand.busy = rightHand.busy = false;
            busy = false;
        }

        // The original fireballs: falling fire with a shadow showing where it lands.
        IEnumerator FireRain()
        {
            busy = true;
            yield return Windup(0.4f);
            int n = phase >= 2 ? 6 : 4;
            for (int i = 0; i < n && Hero != null; i++)
            {
                Vector2 at = (Vector2)Hero.transform.position + Random.insideUnitCircle * (i == 0 ? 0.5f : 4f);
                at = ClampArena(at, 1f);
                StartCoroutine(FallingFire(at));
                yield return new WaitForSeconds(0.25f / Speed);
            }
            busy = false;
        }

        IEnumerator FallingFire(Vector2 at)
        {
            AreaEffect.Spawn(self, at, new AreaSpec { radius = 1.4f, delay = 1.0f, damageMult = 0.9f, color = new Color(1f, 0.45f, 0.15f), status = StatusSpec.Burn(0.3f, 2f) });
            var go = new GameObject("BossFire");
            var bb = Billboard.Create(go.transform);
            var fire = Part(bb, db.bossFire, 1f, 0f);
            RenderAssets.MakeLit(fire, false, RenderAssets.Instance.litEmissive);
            PointLights.Attach(go.transform, new Color(1f, 0.5f, 0.2f), 4f, 2f, new Vector3(0, 0, -0.5f));
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime;
                go.transform.position = new Vector3(at.x, at.y, Mathf.Lerp(-14f, 0f, t * t));
                yield return null;
            }
            VFX.Burst(at, new Color(1f, 0.55f, 0.2f), 16, 1.2f);
            // The fire keeps burning where it landed.
            GroundFire.Spawn(self, at, db.bossFire, 1.3f);
            Destroy(go, 0.3f);
        }

        // A line of fire racing along the ground from the boss toward the hero.
        IEnumerator FireLine()
        {
            busy = true;
            if (Hero == null) { busy = false; yield break; }
            yield return Windup(0.4f);
            Vector2 from = transform.position;
            Vector2 dir = ((Vector2)Hero.transform.position - from).normalized;
            for (float d = 2f; d < reach; d += 1.3f)
            {
                Vector2 p = from + dir * d;
                if (!InArena(p, 0.8f) || Walls.Blocked(from, p)) break;
                AreaEffect.Spawn(self, p, new AreaSpec { radius = 1.1f, delay = 0.35f, ticks = 3, interval = 0.4f, damageMult = 0.45f, color = new Color(1f, 0.5f, 0.15f), status = StatusSpec.Burn(0.3f, 2f) });
                // Every other step leaves fire burning on the ground.
                if (((int)(d / 1.3f) & 1) == 0) GroundFire.Spawn(self, p, db.bossFire, 1.1f);
                else if (db.bossFire != null) StartCoroutine(FlameBurst(p));
                yield return new WaitForSeconds(0.07f);
            }
            busy = false;
        }

        IEnumerator FlameBurst(Vector2 p)
        {
            var go = new GameObject("Flame");
            go.transform.position = p;
            var bb = Billboard.Create(go.transform);
            var f = Part(bb, db.bossFire, 0.6f, 0f);
            RenderAssets.MakeLit(f, false, RenderAssets.Instance.litEmissive);
            yield return new WaitForSeconds(1.4f);
            Destroy(go);
        }

        // The risk spot: a big glowing circle. Standing in it hurts, but doubles your damage.
        GameObject riskSpot;

        IEnumerator RiskSpot()
        {
            busy = true;
            // Right beside the boss, on the hero's side, and it moves with the boss: the reward is
            // only worth it because you have to fight in its reach.
            Vector2 side = Hero != null ? (HeroPos - (Vector2)transform.position).normalized : Vector2.down;
            if (side.sqrMagnitude < 0.01f) side = Vector2.down;
            Vector2 offset = side * 2.6f;
            Vector2 at = ClampArena((Vector2)transform.position + offset, 1.5f);
            var go = new GameObject("RiskSpot");
            riskSpot = go;
            go.transform.position = Ground.Decal(at);
            var r = go.AddComponent<SpriteRenderer>();
            RenderAssets.MakeUnlit(r);
            r.color = new Color(1f, 1f, 1f, 0.85f);
            var frames = db.bossRiskSpot;
            float size = 4f;
            if (frames != null && frames.Length > 0) go.transform.localScale = Vector3.one * size / frames[0].bounds.size.x;
            PointLights.Attach(go.transform, new Color(1f, 0.85f, 0.4f), 6f, 2f, new Vector3(0, 0, -1f), true);
            GameUI.Instance?.Toast("A risk spot appeared: stand in it for double damage!", Palette.Gold, 2f);
            float t = 0f, nextTick = 0f;
            busy = false; // the boss keeps attacking meanwhile
            while (t < 6f && !dead)
            {
                t += Time.deltaTime;
                at = ClampArena((Vector2)transform.position + offset, 1.5f);
                go.transform.position = Ground.Decal(at);
                if (frames != null && frames.Length > 0) r.sprite = frames[Mathf.FloorToInt(t * 10f) % frames.Length];
                var hero = Hero;
                if (hero != null && Vector2.Distance(hero.transform.position, at) < size * 0.45f && t >= nextTick)
                {
                    nextTick = t + 1f;
                    hero.TakeDamage(new DamageInfo { amount = hero.MaxHP * 0.04f, trueDamage = true });
                    BossHelpers.RiskSpotBuff();
                }
                yield return null;
            }
            Destroy(go);
        }

        // ---------- Guardian attacks ----------

        Color Theme
        {
            get
            {
                switch (kind)
                {
                    case BossKind.Spider: return new Color(0.6f, 0.95f, 0.3f);
                    case BossKind.Skeleton: return new Color(0.85f, 0.95f, 1f);
                    case BossKind.Zombie: return new Color(0.55f, 0.8f, 0.25f);
                    case BossKind.Slime: return look != null ? Color.Lerp(look.primary, Color.white, 0.25f) : Color.green;
                    default: return new Color(0.8f, 0.4f, 1f);
                }
            }
        }

        // Every boss has its own voice.
        AudioClip RoarClip
        {
            get
            {
                switch (kind)
                {
                    case BossKind.Spider: return Sfx.SpiderScreech;
                    case BossKind.Skeleton: return Sfx.BoneWail;
                    case BossKind.Zombie: return Sfx.ZombieGroan;
                    case BossKind.Slime: return Sfx.SlimeGurgle;
                    default: return Sfx.MetalRoar;
                }
            }
        }

        Vector2 HeroPos => Hero != null ? (Vector2)Hero.transform.position : Vector2.zero;
        Vector2 ToHero => (HeroPos - (Vector2)transform.position).normalized;

        // Skeleton: crumbles into bones and reassembles somewhere else.
        IEnumerator BoneTeleport(Vector2 to)
        {
            busy = true;
            VFX.Burst(transform.position, Theme, 30, 2.5f);
            self.Targetable = false;
            visual.gameObject.SetActive(false);
            yield return new WaitForSeconds(0.5f / Speed);
            transform.position = to;
            AreaEffect.Spawn(self, to, new AreaSpec { radius = 3f, delay = 0.6f / Speed, damageMult = 1.1f, color = Theme, knockback = 2f });
            yield return new WaitForSeconds(0.6f / Speed);
            visual.gameObject.SetActive(true);
            VFX.Burst(to, Theme, 30, 2.5f);
            self.Targetable = true;
            busy = false;
        }

        // Zombie: lumbers across the arena; every step shakes the ground.
        IEnumerator Lumber(Vector2 to)
        {
            busy = true;
            float step = 0f;
            for (float t = 0f; t < 3.5f && Vector2.Distance(transform.position, to) > 0.4f; t += Time.deltaTime)
            {
                var p = Vector2.MoveTowards(transform.position, to, 4.2f * Speed * Time.deltaTime);
                lastMove = p - (Vector2)transform.position;
                transform.position = p;
                step += Time.deltaTime;
                if (step > 0.55f / Speed)
                {
                    step = 0f;
                    CameraFollow.Shake(0.12f);
                    if (Hero != null && Vector2.Distance(HeroPos, p) < 2.6f) Combat.Hit(self, Hero, 0.6f, default, false, 3f);
                }
                yield return null;
            }
            busy = false;
        }

        // Spider: leaps straight at the hero.
        IEnumerator Pounce()
        {
            if (Hero == null) yield break;
            yield return Windup(0.35f);
            yield return Leap(ClampArena(HeroPos, 2f));
        }

        // Spider: a fan of sticky webs that slow.
        IEnumerator WebVolley()
        {
            busy = true;
            yield return Windup(0.4f);
            int waves = phase >= 3 ? 3 : 2, n = phase >= 2 ? 7 : 5;
            for (int w = 0; w < waves && Hero != null; w++)
            {
                float baseAng = Mathf.Atan2(ToHero.y, ToHero.x) * Mathf.Rad2Deg;
                for (int i = 0; i < n; i++)
                {
                    float a = (baseAng + (i - (n - 1) / 2f) * 12f + (w % 2) * 6f) * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Projectile.Fire(self, (Vector2)transform.position + d * 1.6f, d, new ProjectileSpec
                    {
                        speed = 9f * Speed, range = reach, hitRadius = 0.45f, damageMult = 0.4f, color = new Color(0.92f, 0.92f, 0.95f), size = 0.7f,
                        status = StatusSpec.Slow(0.45f, 2f),
                    });
                }
                yield return new WaitForSeconds(0.45f / Speed);
            }
            busy = false;
        }

        // Spider: web globs land around the hero and leave sticky webs that slow.
        IEnumerator WebTraps()
        {
            busy = true;
            yield return Windup(0.4f);
            int n = phase >= 3 ? 6 : 4;
            for (int i = 0; i < n && Hero != null; i++)
            {
                Vector2 at = ClampArena(HeroPos + Random.insideUnitCircle * (i == 0 ? 0.5f : 4f), 1f);
                AreaEffect.Spawn(self, at, new AreaSpec { radius = 1.5f, delay = 0.9f, damageMult = 0.6f, color = WebColor, status = StatusSpec.Slow(0.5f, 2f) });
                StartCoroutine(PoolLater(at, 0.9f, 1.6f, WebColor, StatusSpec.Slow(0.55f, 1f)));
                yield return new WaitForSeconds(0.2f / Speed);
            }
            busy = false;
        }

        static readonly Color WebColor = new Color(0.92f, 0.92f, 0.96f);

        IEnumerator PoolLater(Vector2 at, float delay, float radius, Color color, StatusSpec status)
        {
            yield return new WaitForSeconds(delay);
            if (!dead) GroundFire.SpawnPool(self, at, color, radius, status);
        }

        // Skeleton: bursts of fast bone arrows aimed at (and just ahead of) the hero.
        IEnumerator BoneVolley()
        {
            busy = true;
            yield return Windup(0.35f);
            int bursts = phase >= 3 ? 5 : phase >= 2 ? 4 : 3;
            for (int b = 0; b < bursts && Hero != null; b++)
            {
                Vector2 from = transform.position;
                for (int i = -1; i <= 1; i++)
                {
                    float a = Mathf.Atan2(ToHero.y, ToHero.x) + i * 0.12f;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Projectile.Fire(self, from + d * 1.6f, d, new ProjectileSpec
                    {
                        speed = 13f * Speed, range = reach, hitRadius = 0.35f, damageMult = 0.45f, color = new Color(0.9f, 0.95f, 1f), size = 0.55f,
                    });
                }
                yield return new WaitForSeconds(0.35f / Speed);
            }
            busy = false;
        }

        // Slime: one huge jump to the middle of the arena, then rings of shockwaves.
        IEnumerator Quake()
        {
            yield return Windup(0.4f);
            yield return Leap(ClampArena(Vector2.Lerp(transform.position, HeroPos, 0.5f), 2f));
            busy = true;
            CameraFollow.Shake(0.5f);
            int rings = phase >= 2 ? 4 : 3;
            for (int i = 0; i < rings && !dead; i++)
            {
                RingWave.Spawn(self, transform.position, Random.Range(0f, 360f), 8.5f * Speed, reach);
                yield return new WaitForSeconds(0.45f / Speed);
            }
            busy = false;
        }

        // Skeleton: rows of bone spikes burst from the ground toward the hero.
        IEnumerator BoneSpikes()
        {
            busy = true;
            if (Hero == null) { busy = false; yield break; }
            yield return Windup(0.4f);
            int lines = phase >= 2 ? 3 : 1;
            Vector2 from = transform.position;
            float baseAng = Mathf.Atan2(ToHero.y, ToHero.x);
            for (float d = 2f; d < reach; d += 1.4f)
            {
                bool any = false;
                for (int l = 0; l < lines; l++)
                {
                    float a = baseAng + (l - (lines - 1) / 2f) * 0.35f;
                    Vector2 p = from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    if (!InArena(p, 0.8f) || Walls.Blocked(from, p)) continue;
                    any = true;
                    AreaEffect.Spawn(self, p, new AreaSpec { radius = 1f, delay = 0.45f, damageMult = 0.8f, color = Theme, knockback = 1f });
                }
                if (!any) break;
                yield return new WaitForSeconds(0.06f);
            }
            busy = false;
        }

        // Zombie: retches a cone of rot.
        IEnumerator Vomit()
        {
            busy = true;
            if (Hero == null) { busy = false; yield break; }
            yield return Windup(0.5f);
            float baseAng = Mathf.Atan2(ToHero.y, ToHero.x) * Mathf.Rad2Deg;
            for (float t = 0f; t < 1.2f; t += 0.08f)
            {
                float a = (baseAng + Random.Range(-28f, 28f)) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Projectile.Fire(self, (Vector2)transform.position + d * 1.8f, d, new ProjectileSpec
                {
                    speed = Random.Range(6f, 9f) * Speed, range = Random.Range(8f, 13f), hitRadius = 0.5f, damageMult = 0.35f, color = Theme, size = 0.8f,
                    status = StatusSpec.Poison(0.3f, 3f),
                });
                yield return new WaitForSeconds(0.08f / Speed);
            }
            // The puddle it leaves in front of itself.
            GroundFire.SpawnPool(self, ClampArena((Vector2)transform.position + ToHero * 4f, 1f), Theme, 2.2f, StatusSpec.Poison(0.25f, 2f), 6f);
            busy = false;
        }

        // Slime: its only way of moving is jumping on the hero; every landing sends a shockwave.
        IEnumerator SlimeHop()
        {
            if (Hero == null) yield break;
            yield return Leap(ClampArena(HeroPos, 2f));
            RingWave.Spawn(self, transform.position, Random.Range(0f, 360f), 8f * Speed, 10f);
        }

        // Slime: heavy bounces right onto the hero, each sending out a shockwave.
        IEnumerator Bounce()
        {
            int n = phase >= 2 ? 4 : 3;
            for (int i = 0; i < n && Hero != null && !dead; i++)
            {
                yield return Leap(ClampArena(HeroPos, 2f));
                RingWave.Spawn(self, transform.position, Random.Range(0f, 360f), 8f * Speed, 9f);
                yield return new WaitForSeconds(0.2f / Speed);
            }
        }

        // Zombie (and the World's Fear): a horde of minions.
        IEnumerator Horde() => SummonOwn(phase >= 3 ? 6 : phase >= 2 ? 5 : 4);

        MonsterSpecies MinionSpecies(BossKind k)
        {
            var sp = MonsterGenerator.Create(self.level * 13 + (int)k, WorldManager.PendingBossBiome, k == BossKind.Spider ? MonsterArchetype.Swarmling : MonsterArchetype.Chaser, k == BossKind.Spider);
            switch (k)
            {
                case BossKind.Spider: sp.name = "Spiderling"; sp.onHitStatus = StatusSpec.Slow(0.4f, 1.5f); break;
                case BossKind.Skeleton: sp.name = "Risen Skeleton"; sp.look = BossSpriteGenerator.Generate(BossBody.Skeleton, new Color(0.8f, 0.8f, 0.75f), 1); sp.scale = 0.6f; break;
                case BossKind.Zombie: sp.name = "Shambler"; sp.look = BossSpriteGenerator.Generate(BossBody.Zombie, new Color(0.45f, 0.55f, 0.35f), 1); sp.scale = 0.55f; sp.moveSpeed = 2.4f; break;
                default: sp.name = "Slimeling"; sp.look = BossSpriteGenerator.Generate(BossBody.Slime, new Color(0.35f, 0.85f, 0.4f), 1); sp.scale = 0.45f; break;
            }
            return sp;
        }

        // Minions that match the boss: spiderlings, skeletons, zombies or little slimes.
        IEnumerator SummonOwn(int count)
        {
            busy = true;
            yield return Windup(0.6f);
            for (int i = 0; i < count; i++)
            {
                // The World's Fear calls on all four guardians' kin.
                var sp = MinionSpecies(kind == BossKind.WorldsFear ? Guardians[i % Guardians.Length] : kind);
                Vector2 at = ClampArena((Vector2)transform.position + Random.insideUnitCircle * 3.5f, 1f);
                VFX.Burst(at, Theme, 12, 1f);
                var m = MonsterBrain.Spawn(sp, Mathf.Max(1, self.level - 2), MonsterRank.Normal, at);
                m.leashRange = 100f;
                m.Aggro();
            }
            busy = false;
        }

        IEnumerator Windup(float t)
        {
            float e = 0f;
            while (e < t)
            {
                e += Time.deltaTime;
                float k = e / t;
                visual.localScale = new Vector3(baseScale.x * (1f + k * 0.12f), baseScale.y * (1f - k * 0.1f), 1f);
                yield return null;
            }
            visual.localScale = baseScale;
        }

        // ---------- Victory ----------

        void OnDied(Combatant killer)
        {
            dead = true;
            StopAllCoroutines();
            if (riskSpot != null) Destroy(riskSpot);
            visual.localPosition = Vector3.zero;
            // The body no longer blocks anything, so the loot it drops can be picked up.
            foreach (var col in GetComponentsInChildren<Collider2D>()) col.enabled = false;
            self.Targetable = false;
            StartCoroutine(Victory());
        }

        IEnumerator AnnounceUnlock(BossKind k)
        {
            yield return new WaitForSeconds(5.5f);
            GameUI.Instance?.Toast($"New mechanic unlocked: {BossUnlocks.Describe(k)}", Palette.Gold, 5f);
        }

        IEnumerator Victory()
        {
            foreach (var m in FindObjectsByType<MonsterBrain>()) m.Combatant.Die(null);
            for (int i = 0; i < 10; i++)
            {
                VFX.Burst(transform.position + (Vector3)Random.insideUnitCircle * 3f, new Color(1f, 0.5f, 0.2f), 20, 2f);
                CameraFollow.Shake(0.3f);
                yield return new WaitForSeconds(0.2f);
            }
            Profile.AddXP(Profile.MonsterXP(self.level, MonsterRank.Boss));
            Profile.Data.bossesKilled++;
            bool firstKill = !Profile.Data.guardiansSlain.Contains(kind.ToString());
            if (firstKill && kind != BossKind.Titan) Profile.Data.guardiansSlain.Add(kind.ToString());
            Profile.Data.defeatedGates.Add(WorldManager.PendingGateKey);
            Loot.Drop(self, transform.position);
            Pickup.SpawnGlyph((Vector2)transform.position + Vector2.right * 2f, SkillParts.Roll(new System.Random(), self.level, MonsterRank.Boss));
            Profile.Save();
            GameUI.Instance?.Toast("Victory! Grab the loot, then step into the portal.", Palette.Gold, 4f);
            if (System.Array.IndexOf(Guardians, kind) >= 0)
            {
                var left = WorldManager.GuardiansLeft();
                string msg = left.Count > 0 ? $"A new lair is unsealed: {BossName(left[0])} awaits further out."
                    : "All four guardians have fallen. The World's Fear awaits at the edge of the world!";
                GameUI.Instance?.Toast(msg, new Color(1f, 0.5f, 0.35f), 5f);
            }
            // Each boss's first defeat unlocks a new crafting mechanic.
            if (firstKill && BossUnlocks.Describe(kind) != null) StartCoroutine(AnnounceUnlock(kind));
            ExitPortal.Spawn(Vector2.zero, returnScene);
            visual.gameObject.SetActive(false);
            if (leftHand != null)
            {
                leftHand.root.gameObject.SetActive(false);
                rightHand.root.gameObject.SetActive(false);
            }
        }
    }

    // An expanding ring of force with a gap; touching the ring outside the gap hurts.
    public class RingWave : MonoBehaviour
    {
        Combatant owner;
        float radius, speed, maxRadius, gapAngle;
        const float GapWidth = 50f;
        readonly List<SpriteRenderer> pieces = new List<SpriteRenderer>();
        bool hitHero;

        public static void Spawn(Combatant owner, Vector2 at, float gapAngle, float speed, float maxRadius)
        {
            var go = new GameObject("RingWave");
            go.transform.position = Ground.Decal(at);
            var w = go.AddComponent<RingWave>();
            w.owner = owner;
            w.speed = speed;
            w.maxRadius = maxRadius;
            w.gapAngle = gapAngle;
            w.radius = 1.5f;
            // Dashes around the circle, leaving the gap empty.
            for (int i = 0; i < 48; i++)
            {
                float a = i * 7.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, gapAngle)) < GapWidth / 2f) continue;
                var sr = GameAssets.CreateWorldSprite("seg", GameAssets.Pixel, go.transform, new Color(1f, 0.55f, 0.3f, 0.85f), SortOrder.Telegraph + 3);
                sr.transform.localRotation = Quaternion.Euler(0, 0, a + 90f);
                w.pieces.Add(sr);
            }
        }

        void Update()
        {
            radius += speed * Time.deltaTime;
            float unit = GameAssets.Pixel.bounds.size.x;
            for (int i = 0, k = 0; i < 48; i++)
            {
                float a = i * 7.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, gapAngle)) < GapWidth / 2f) continue;
                var sr = pieces[k++];
                var local = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * radius;
                sr.transform.localPosition = local;
                sr.transform.localScale = new Vector3(radius * 0.14f / unit, 0.35f / unit, 1f);
                // Pillars stop the wave: the part behind them is gone.
                if (sr.enabled && Walls.Blocked(transform.position, transform.position + local)) sr.enabled = false;
            }
            var hero = HeroController.Instance != null ? HeroController.Instance.Combatant : null;
            if (!hitHero && hero != null && !hero.IsDead)
            {
                Vector2 to = (Vector2)(hero.transform.position - transform.position);
                float ang = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
                bool inGap = Mathf.Abs(Mathf.DeltaAngle(ang, gapAngle)) < GapWidth / 2f;
                bool covered = Walls.Blocked(transform.position, hero.transform.position);
                if (!inGap && !covered && Mathf.Abs(to.magnitude - radius) < 0.5f)
                {
                    hitHero = true;
                    Combat.Hit(owner, hero, 0.9f, default, false, 2.5f);
                }
            }
            if (radius > maxRadius) Destroy(gameObject);
        }
    }

    // A sweeping beam telegraph that damages along its line.
    public static class SweepBeam
    {
        public static IEnumerator Run(Combatant owner, Vector2 from, float startAngle, float sweep, float length, float duration)
        {
            var go = new GameObject("Beam");
            go.transform.position = Ground.Decal(from);
            var sr = GameAssets.CreateWorldSprite("beam", GameAssets.Pixel, go.transform, new Color(1f, 0.3f, 0.2f, 0.35f), SortOrder.Telegraph + 3);
            float unit = GameAssets.Pixel.bounds.size.x;
            sr.transform.localScale = new Vector3(length / unit, 1.2f / unit, 1f);
            sr.transform.localPosition = new Vector3(length / 2f, 0, 0);
            // Warn first, then fire.
            go.transform.rotation = Quaternion.Euler(0, 0, startAngle);
            yield return new WaitForSeconds(0.8f);
            sr.color = new Color(1f, 0.85f, 0.5f, 0.9f);
            var light = PointLights.Attach(go.transform, new Color(1f, 0.6f, 0.3f), 8f, 3f, new Vector3(length * 0.3f, 0, -1f), true);
            float t = 0f, lastHit = -1f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float a = startAngle + sweep * (t / duration);
                go.transform.rotation = Quaternion.Euler(0, 0, a);
                var beamDir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                var wallHit = Physics2D.Raycast(from + beamDir * 2f, beamDir, length, Walls.Mask);
                float reach = wallHit.collider != null ? wallHit.distance + 2f : length;
                sr.transform.localScale = new Vector3(reach / unit, 1.2f / unit, 1f);
                sr.transform.localPosition = new Vector3(reach / 2f, 0, 0);
                var hero = HeroController.Instance != null ? HeroController.Instance.Combatant : null;
                if (hero != null && !hero.IsDead && Time.time - lastHit > 0.5f)
                {
                    Vector2 to = (Vector2)hero.transform.position - from;
                    var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                    float along = Vector2.Dot(to, dir);
                    float side = Mathf.Abs(to.x * dir.y - to.y * dir.x);
                    if (along > 0f && along < reach && side < 0.8f)
                    {
                        lastHit = Time.time;
                        Combat.Hit(owner, hero, 0.7f, StatusSpec.Burn(0.3f, 2f), false, 1f);
                    }
                }
                yield return null;
            }
            Object.Destroy(go);
        }
    }

    // Takes you back to the overworld after a boss.
    public class ExitPortal : MonoBehaviour
    {
        string scene;
        float age;

        public static void Spawn(Vector2 at, string scene)
        {
            var go = new GameObject("ExitPortal");
            go.transform.position = at;
            var p = go.AddComponent<ExitPortal>();
            p.scene = scene;
            var glow = GameAssets.CreateWorldSprite("glow", GameAssets.PixelGlow, go.transform, new Color(0.5f, 0.8f, 1f, 0.7f), SortOrder.Ground + 6, 3f);
            glow.transform.localPosition = new Vector3(0, 0, Ground.DecalZ);
            GameAssets.CreateWorldSprite("ring", GameAssets.PixelRing, go.transform, new Color(0.6f, 0.9f, 1f, 0.9f), SortOrder.Ground + 7, 3f)
                .transform.localPosition = new Vector3(0, 0, Ground.DecalZ * 2f);
            PointLights.Attach(go.transform, new Color(0.5f, 0.8f, 1f), 7f, 3f, new Vector3(0, 0, -1.5f), true);
        }

        void Update()
        {
            age += Time.deltaTime;
            transform.GetChild(1).Rotate(0, 0, 60f * Time.deltaTime);
            var hero = HeroController.Instance;
            if (hero == null || age < 1f) return;
            if (Vector2.Distance(hero.transform.position, transform.position) < 1.3f)
            {
                Profile.Save();
                SceneManager.LoadScene(scene);
            }
        }
    }
}
