using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Sound effects synthesized in code, so the game ships only original audio.
    public static class Sfx
    {
        const int Rate = 22050;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static readonly System.Random noise = new System.Random(1234);

        public static AudioClip Swing => Get("swing", 0.16f, (t, d) => Noise() * Env(t, d, 0.01f, 0.12f) * 0.5f * Mathf.Lerp(0.3f, 1f, Mathf.Sin(t / d * Mathf.PI)));
        public static AudioClip Hit => Get("hit", 0.12f, (t, d) => (Noise() * 0.6f + Square(t, Mathf.Lerp(180f, 70f, t / d)) * 0.5f) * Env(t, d, 0.002f, 0.1f));
        public static AudioClip MonsterDeath => Get("mdeath", 0.35f, (t, d) => (Square(t, Mathf.Lerp(260f, 60f, t / d)) * 0.5f + Noise() * 0.25f) * Env(t, d, 0.005f, 0.3f));
        public static AudioClip Dash => Get("dash", 0.22f, (t, d) => Noise() * Env(t, d, 0.03f, 0.18f) * 0.45f);
        public static AudioClip Pickup => Get("pickup", 0.18f, (t, d) => Sine(t, t < 0.07f ? 880f : 1320f) * Env(t, d, 0.005f, 0.15f) * 0.45f);
        public static AudioClip Coin => Get("coin", 0.2f, (t, d) => Square(t, t < 0.05f ? 1500f : 2000f) * Env(t, d, 0.002f, 0.18f) * 0.25f);
        public static AudioClip LevelUp => Get("levelup", 0.7f, (t, d) =>
        {
            float[] notes = { 523f, 659f, 784f, 1047f };
            int i = Mathf.Min(3, (int)(t / 0.12f));
            return (Square(t, notes[i]) * 0.3f + Sine(t, notes[i] * 2f) * 0.2f) * Env(t, d, 0.01f, 0.4f);
        });
        public static AudioClip Explosion => Get("boom", 0.5f, (t, d) => (Noise() * 0.7f + Sine(t, Mathf.Lerp(90f, 30f, t / d)) * 0.5f) * Env(t, d, 0.002f, 0.45f));
        public static AudioClip Cast => Get("cast", 0.25f, (t, d) => (Sine(t, Mathf.Lerp(400f, 900f, t / d)) * 0.4f + Noise() * 0.1f) * Env(t, d, 0.02f, 0.2f));
        public static AudioClip Potion => Get("potion", 0.35f, (t, d) => Sine(t, 500f + Mathf.Sin(t * 60f) * 120f) * Env(t, d, 0.02f, 0.3f) * 0.4f);
        public static AudioClip Buy => Get("buy", 0.3f, (t, d) => Square(t, t < 0.1f ? 1200f : 1800f) * Env(t, d, 0.002f, 0.25f) * 0.25f);

        delegate float Synth(float t, float duration);

        static AudioClip Get(string key, float duration, Synth f)
        {
            if (cache.TryGetValue(key, out var c) && c != null) return c;
            int n = Mathf.CeilToInt(duration * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, duration), -1f, 1f);
            c = AudioClip.Create(key, n, 1, Rate, false);
            c.SetData(data, 0);
            cache[key] = c;
            return c;
        }

        static float Noise() => (float)(noise.NextDouble() * 2.0 - 1.0);
        static float Sine(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);
        static float Square(float t, float f) => Mathf.Repeat(t * f, 1f) < 0.5f ? 1f : -1f;

        // Attack / release envelope.
        static float Env(float t, float d, float attack, float release)
        {
            if (t < attack) return t / attack;
            float r = Mathf.Clamp01((d - t) / Mathf.Max(0.001f, release));
            return r * r;
        }
    }
}
