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

        void LateUpdate()
        {
            if (sprites == null || sr == null) return;
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
