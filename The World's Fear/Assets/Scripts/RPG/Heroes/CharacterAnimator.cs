using UnityEngine;

namespace TWF
{
    // Plays the generated character frames based on what the hero is doing.
    public class CharacterAnimator : MonoBehaviour
    {
        public Outfit outfit;
        public float walkFrameTime = 0.12f;

        SpriteRenderer sr;
        CharacterSprites sprites;
        float t;
        int frame;

        public Vector2 Velocity { get; set; }
        public Vector2 Facing { get; set; } = Vector2.down;
        public bool Attacking { get; set; }

        void Start()
        {
            sr = GetComponent<SpriteRenderer>();
            sprites = CharacterSpriteGenerator.Get(outfit);
        }

        public void SetOutfit(Outfit o)
        {
            outfit = o;
            sprites = CharacterSpriteGenerator.Get(o);
        }

        // A custom sprite set (the hero wearing their gear).
        public void SetSprites(CharacterSprites set)
        {
            if (set != null) sprites = set;
        }

        // ---------- The hero rendered from the 3D model ----------

        const float RunAbove = 4f;             // world units a second: slowed or a light stick push walks, full speed runs
        const float IdleFrameTime = 0.16f;
        float attackFrameTime = 0.05f;

        Hero3DSet hero3D;
        string attackAnimation = "slash";
        string playing;
        bool wasAttacking;
        float attackLeft;

        // World units a second the hero moves at full input.
        public float MoveSpeed { get; set; }

        public void UseHero3D(Hero3DSet set, string attack, float attackFrame = 0.05f)
        {
            hero3D = set;
            attackAnimation = attack;
            attackFrameTime = attackFrame;
        }

        class GearLayer
        {
            public string piece;
            public SpriteRenderer back, front;
        }

        readonly System.Collections.Generic.List<GearLayer> gear = new System.Collections.Generic.List<GearLayer>();

        // True while the hero's hand shows a rendered weapon, so the old swinging weapon sprite stays hidden.
        public bool HoldsRenderedWeapon { get; private set; }

        // Worn and held pieces, innermost first: each is drawn as a layer behind and a layer in front of the body.
        public void SetGear3D(System.Collections.Generic.List<string> pieces, bool holdsWeapon)
        {
            HoldsRenderedWeapon = hero3D != null && holdsWeapon;
            bool same = gear.Count == pieces.Count;
            for (int i = 0; same && i < pieces.Count; i++) same = gear[i].piece == pieces[i];
            if (same) return;
            foreach (var g in gear)
            {
                if (g.back) Destroy(g.back.gameObject);
                if (g.front) Destroy(g.front.gameObject);
            }
            gear.Clear();
            foreach (var p in pieces)
                gear.Add(new GearLayer { piece = p, back = MakeLayer(p + "_back"), front = MakeLayer(p + "_front") });
        }

        SpriteRenderer MakeLayer(string layerName)
        {
            var go = new GameObject(layerName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<SpriteRenderer>();
        }

        // A piece without its own on-the-move strip uses the standing attack's, which has the same upper body.
        Sprite[] LayerFrames(string piece, bool front, string animation, int direction)
        {
            var frames = hero3D.GetLayer(piece, front, animation, direction);
            if (frames.Length == 0 && animation.EndsWith("_move"))
                frames = hero3D.GetLayer(piece, front, animation.Substring(0, animation.Length - 5), direction);
            return frames;
        }

        void ShowLayer(SpriteRenderer layer, Sprite[] frames, int order)
        {
            layer.sprite = frames.Length > 0 ? frames[frame % frames.Length] : null;
            layer.sharedMaterial = sr.sharedMaterial;
            layer.sortingLayerID = sr.sortingLayerID;
            layer.sortingOrder = order;
            layer.color = sr.color;
            layer.shadowCastingMode = sr.shadowCastingMode;
            layer.receiveShadows = sr.receiveShadows;
        }

        void AnimateHero3D()
        {
            Vector2 look = View.ToScreen(Velocity.sqrMagnitude > 0.01f && attackLeft <= 0f ? Velocity : Facing);
            int direction = Hero3DSet.DirectionOf(look);
            sr.flipX = false;

            // A hit plays to its end even if the game's short attack flag has already dropped.
            if (Attacking && !wasAttacking && attackLeft <= 0f)
            {
                attackLeft = hero3D.Get(attackAnimation, direction).Length * attackFrameTime;
                playing = null;
            }
            wasAttacking = Attacking;

            string animation;
            float frameTime;
            float speed = Velocity.magnitude * MoveSpeed;
            if (attackLeft > 0f)
            {
                attackLeft -= Time.deltaTime;
                // A melee hero who keeps moving swings with the legs still running, so the feet don't slide.
                string moving = attackAnimation + "_move";
                animation = speed > 0.05f && hero3D.Get(moving, direction).Length > 0 ? moving : attackAnimation;
                frameTime = attackFrameTime;
            }
            else if (speed > 0.05f)
            {
                bool run = speed > RunAbove;
                animation = run ? "run" : "walk";
                frameTime = Mathf.Clamp((run ? Hero3DSet.RunPerFrame : Hero3DSet.WalkPerFrame) / speed, 0.03f, 0.2f);
            }
            else
            {
                animation = "idle";
                frameTime = IdleFrameTime;
            }

            if (animation != playing)
            {
                // Starting or stopping mid-swing changes the legs only: the swing carries on from its frame.
                bool sameSwing = playing != null && attackLeft > 0f && playing.StartsWith(attackAnimation) && animation.StartsWith(attackAnimation);
                playing = animation;
                if (!sameSwing)
                {
                    frame = 0;
                    t = 0f;
                }
            }
            t += Time.deltaTime;
            while (t >= frameTime)
            {
                t -= frameTime;
                frame++;
            }
            var set = hero3D.Get(animation, direction);
            if (set.Length > 0) sr.sprite = set[frame % set.Length];
            for (int i = 0; i < gear.Count; i++)
            {
                ShowLayer(gear[i].back, LayerFrames(gear[i].piece, false, animation, direction), sr.sortingOrder - gear.Count + i);
                ShowLayer(gear[i].front, LayerFrames(gear[i].piece, true, animation, direction), sr.sortingOrder + 1 + i);
            }
        }

        void LateUpdate()
        {
            if (sr == null) return;
            if (hero3D != null)
            {
                AnimateHero3D();
                return;
            }
            if (sprites == null) return;
            // Pick the side of the character that faces the camera.
            Vector2 f = View.ToScreen(Velocity.sqrMagnitude > 0.01f && !Attacking ? Velocity : Facing);
            FacingDir dir = Mathf.Abs(f.x) > Mathf.Abs(f.y)
                ? (f.x < 0f ? FacingDir.Left : FacingDir.Right)
                : (f.y > 0f ? FacingDir.Up : FacingDir.Down);
            sr.flipX = false;

            t += Time.deltaTime;
            Sprite[] set;
            float frameTime;
            if (Attacking)
            {
                set = sprites.attack[(int)dir];
                frameTime = 0.08f;
            }
            else if (Velocity.sqrMagnitude > 0.05f)
            {
                set = sprites.walk[(int)dir];
                frameTime = walkFrameTime;
            }
            else
            {
                set = sprites.idle[(int)dir];
                frameTime = 0.5f;
            }
            if (t >= frameTime)
            {
                t = 0f;
                frame++;
            }
            sr.sprite = set[frame % set.Length];
        }
    }
}
