using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Plays one sheet, then removes itself (or loops for `life` seconds, fading in and out).
    public class FxPlayer : MonoBehaviour
    {
        void OnEnable() => HeightLift.On(gameObject);   // stands on plateaus and stairs too

        Fx.Sheet sheet;
        SpriteRenderer sr;
        Color tint;
        float age, life;
        int startFrame;
        Transform follow;
        Vector3 offset;
        const float Fade = 0.25f;

        public void Init(Fx.Sheet s, SpriteRenderer renderer, Color c, float lifetime)
        {
            sheet = s;
            sr = renderer;
            tint = c;
            life = lifetime;
            sr.color = life > 0f ? new Color(c.r, c.g, c.b, 0f) : c;
        }

        public void StartAt(int frame) => startFrame = frame;
        // Moves along with t, keeping where it is now relative to it (height and camera lift included).
        public void Follow(Transform t)
        {
            follow = t;
            if (t != null) offset = transform.position - t.position;
        }

        // Ends a looping effect with a short fade.
        public void Stop()
        {
            if (life < 0f || life > age + Fade) life = age + Fade;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (follow != null) transform.position = follow.position + offset;
            int f = startFrame + Mathf.FloorToInt(age * sheet.fps);
            if (sheet.loop) f %= sheet.frames.Length;
            else if (f >= sheet.frames.Length)
            {
                if (life < 0f) { Destroy(gameObject); return; }
                f = sheet.frames.Length - 1;
            }
            sr.sprite = sheet.frames[f];
            if (life >= 0f)
            {
                if (age >= life) { Destroy(gameObject); return; }
                float a = Mathf.Min(1f, age / Fade) * Mathf.Min(1f, (life - age) / Fade);
                sr.color = new Color(tint.r, tint.g, tint.b, tint.a * a);
            }
        }
    }
}
