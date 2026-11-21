using UnityEngine;

namespace TWF
{
    // The hero's latest swing or cast, as the monsters see it: where from, which way, how far it reaches.
    // A quick monster that sees one coming at it steps aside.
    public static class HeroThreat
    {
        public static int Id { get; private set; }
        public static float Time { get; private set; } = -10f;
        public static Vector2 From { get; private set; }
        public static Vector2 Dir { get; private set; }
        public static float Range { get; private set; }

        public static void Swing(Vector2 from, Vector2 dir, float range)
        {
            Id++;
            Time = UnityEngine.Time.time;
            From = from;
            Dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;
            Range = range;
        }

        // Whether a monster at `pos` stands where the latest swing lands, and it is still fresh.
        public static bool Aimed(Vector2 pos, float radius)
        {
            if (UnityEngine.Time.time - Time > 0.15f) return false;
            Vector2 to = pos - From;
            float along = Vector2.Dot(to, Dir);
            if (along < -0.3f || along > Range + radius) return false;
            float side = Mathf.Abs(to.x * Dir.y - to.y * Dir.x);
            return side < Mathf.Max(1.2f, along * 0.75f) + radius;   // a narrow shot or a wide swing in front
        }
    }

    // How the hero has been fighting lately. Monsters answer a habit:
    //   kiting (always on the move in a fight)  -> chargers and chasers aim where the hero is going
    //   standing still to attack or cast        -> exploders and skirmishers rush in
    //   living on potions                       -> shooters' hits make the hero too sick to drink for a moment
    public static class HeroHabits
    {
        public static bool Kiting { get; private set; }
        public static bool Turret { get; private set; }
        public static bool Drinker { get; private set; }
        public static Vector2 HeroVelocity { get; private set; }

        public static float KiteScore { get; private set; }     // 0..1: share of recent fighting spent on the move
        public static float TurretScore { get; private set; }   // 0..1: share spent rooted, attacking or casting

        const float Window = 6f;               // seconds the scores look back over, roughly
        const float FightRange = 8f;           // a monster this close means the hero is fighting
        const float On = 0.8f, Off = 0.6f;     // a habit starts above On and ends below Off
        const float PotionWindow = 90f;
        public const float NauseaSeconds = 3f;
        const float NauseaChance = 0.5f;

        static readonly float[] potions = new float[4];
        static int potionCount;
        static float fightCheck, lastFight = -99f;
        static bool fighting;
        static float noticeAt = -99f;

        public static void Reset()
        {
            Kiting = Turret = Drinker = false;
            KiteScore = TurretScore = 0f;
            potionCount = 0;
            for (int i = 0; i < potions.Length; i++) potions[i] = -999f;
        }

        public static void Tick(HeroController hero, float dt, Vector2 move, bool rooted)
        {
            var body = hero.GetComponent<GroundBody>();
            HeroVelocity = body != null ? body.linearVelocity : Vector2.zero;

            fightCheck -= dt;
            if (fightCheck <= 0f)
            {
                fightCheck = 0.25f;
                fighting = Combat.NearestHostile(Team.Player, hero.transform.G(), FightRange) != null;
                if (fighting) lastFight = Time.time;
            }

            float k = 1f - Mathf.Exp(-dt / Window);
            if (fighting)
            {
                bool moving = move.sqrMagnitude > 0.25f && !rooted;
                KiteScore = Mathf.Lerp(KiteScore, moving ? 1f : 0f, k);
                TurretScore = Mathf.Lerp(TurretScore, !moving && rooted ? 1f : 0f, k);
            }
            else if (Time.time - lastFight > 10f)
            {
                // Out of a fight for a while: the monsters forget.
                float f = 1f - Mathf.Exp(-dt / 20f);
                KiteScore = Mathf.Lerp(KiteScore, 0f, f);
                TurretScore = Mathf.Lerp(TurretScore, 0f, f);
            }

            Set(ref KitingField, KiteScore, hero, "kiting");
            Set(ref TurretField, TurretScore, hero, "standing still");
            Kiting = KitingField;
            Turret = TurretField;

            int recent = 0;
            for (int i = 0; i < potions.Length; i++) if (Time.time - potions[i] < PotionWindow) recent++;
            bool drinker = recent >= 2;
            if (drinker && !Drinker) Notice(hero, "potions");
            Drinker = drinker;
        }

        static bool KitingField, TurretField;

        static void Set(ref bool flag, float score, HeroController hero, string what)
        {
            if (!flag && score > On) { flag = true; Notice(hero, what); }
            else if (flag && score < Off) flag = false;
        }

        // Tells the player, now and then, that the monsters have caught on.
        static void Notice(HeroController hero, string what)
        {
            if (Time.time - noticeAt < 45f) return;
            noticeAt = Time.time;
            FloatingText.Spawn(hero.transform.position + View.Up * (hero.Combatant.HeadHeight + 0.5f), "The monsters read your " + what,
                new Color(1f, 0.7f, 0.4f), 0.75f);
        }

        public static void PotionUsed()
        {
            potions[potionCount++ % potions.Length] = Time.time;
        }

        // A shooter or skirmisher's hit on a hero who lives on potions may leave it too sick to drink.
        public static void HeroHit(HeroController hero, DamageInfo info)
        {
            if (!Drinker || info.source == null || info.silent || hero.Combatant.HPFraction > 0.5f || hero.PotionBlocked) return;
            var m = info.source.GetComponent<MonsterBrain>();
            if (m == null) return;
            var a = m.Species.archetype;
            if (a != MonsterArchetype.Spitter && a != MonsterArchetype.Caster && a != MonsterArchetype.Skirmisher) return;
            if (Random.value > NauseaChance) return;
            hero.PotionBlockedUntil = Time.time + NauseaSeconds;
            FloatingText.Spawn(hero.transform.position + View.Up * hero.Combatant.HeadHeight, "Nauseous", new Color(0.6f, 0.85f, 0.35f), 0.85f);
            VFX.Burst(hero.transform.position + View.Up * 0.8f, new Color(0.55f, 0.8f, 0.3f), 8, 0.4f);
        }

        // For the tests: set a habit outright.
        public static void Force(bool kiting, bool turret, bool drinker)
        {
            KitingField = Kiting = kiting;
            TurretField = Turret = turret;
            KiteScore = kiting ? 1f : 0f;
            TurretScore = turret ? 1f : 0f;
            for (int i = 0; i < potions.Length; i++) potions[i] = drinker ? Time.time : -999f;
            Drinker = drinker;
        }
    }
}
