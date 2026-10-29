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

        // A boss roar: a growling low voice ring-modulated with clanging, bell-like partials so it sounds
        // like a beast inside a suit of armor.
        public static AudioClip MetalRoar
        {
            get
            {
                double ph = 0, ring = 0, clang1 = 0, clang2 = 0;
                float lp = 0f;
                const float dt = 1f / Rate;
                return Get("metalroar", 2.6f, (t, d) =>
                {
                    float k = t / d;
                    // Pitch swells up, then sags as the roar runs out of breath, with a rough growl.
                    float f0 = (t < 0.35f ? Mathf.Lerp(62f, 96f, t / 0.35f) : Mathf.Lerp(96f, 52f, (t - 0.35f) / (d - 0.35f)))
                               * (1f + Mathf.Sin(t * 2f * Mathf.PI * 6.5f) * 0.03f);
                    ph += f0 * dt; ring += f0 * 3.07f * dt; clang1 += f0 * 5.43f * dt; clang2 += f0 * 8.91f * dt;
                    float saw = (float)(ph - System.Math.Floor(ph)) * 2f - 1f;
                    float growl = 0.75f + 0.25f * Mathf.Sin(t * 2f * Mathf.PI * 31f);
                    float voice = saw * growl;
                    // Metallic part: inharmonic partials multiplied by the voice (ring modulation).
                    float metal = (Mathf.Sin((float)(ring % 1.0) * 2f * Mathf.PI) * 0.6f
                                 + Mathf.Sin((float)(clang1 % 1.0) * 2f * Mathf.PI) * 0.35f
                                 + Mathf.Sin((float)(clang2 % 1.0) * 2f * Mathf.PI) * 0.2f) * voice;
                    lp += (Noise() - lp) * 0.08f;   // breathy rumble
                    float x = voice * 0.55f + metal * (0.45f + 0.3f * k) + lp * 0.5f;
                    return (float)System.Math.Tanh(x * 2.2f) * 0.6f * Env(t, d, 0.12f, 1.1f);
                });
            }
        }

        // Spider: a rising, rasping hiss with rapid mandible clicks.
        public static AudioClip SpiderScreech
        {
            get
            {
                double ph = 0;
                float hp = 0f, prev = 0f;
                const float dt = 1f / Rate;
                return Get("spiderscreech", 1.6f, (t, d) =>
                {
                    float k = t / d;
                    // High-passed noise for the hiss.
                    float n = Noise();
                    hp = 0.85f * (hp + n - prev); prev = n;
                    // A shrill, wavering screech tone.
                    float f = Mathf.Lerp(900f, 1500f, Mathf.Sin(k * Mathf.PI)) * (1f + Mathf.Sin(t * 2f * Mathf.PI * 23f) * 0.06f);
                    ph += f * dt;
                    float tone = Mathf.Sin((float)(ph % 1.0) * 2f * Mathf.PI) * Mathf.Sin((float)(ph * 0.5 % 1.0) * 2f * Mathf.PI);
                    // Clicks: short bursts 18 times a second.
                    float click = Mathf.Repeat(t * 18f, 1f) < 0.08f ? Noise() * 0.9f : 0f;
                    return (hp * 0.45f + tone * 0.3f + click * 0.4f) * Env(t, d, 0.05f, 0.6f);
                });
            }
        }

        // Skeleton: a hollow, ghostly wail over rattling bones.
        public static AudioClip BoneWail
        {
            get
            {
                double ph = 0;
                const float dt = 1f / Rate;
                return Get("bonewail", 2.2f, (t, d) =>
                {
                    float k = t / d;
                    float f = Mathf.Lerp(420f, 260f, k) * (1f + Mathf.Sin(t * 2f * Mathf.PI * 5f) * 0.035f);
                    ph += f * dt;
                    float p = (float)(ph % 1.0) * 2f * Mathf.PI;
                    // Hollow: odd harmonics only, like blowing over a bottle.
                    float wail = Mathf.Sin(p) * 0.6f + Mathf.Sin(p * 3f) * 0.18f + Mathf.Sin(p * 5f) * 0.07f;
                    // Rattle: random dry clicks, denser at the start.
                    float rattle = noise.NextDouble() < Mathf.Lerp(0.012f, 0.003f, k) ? 1f : 0f;
                    return (wail * 0.5f * Mathf.Clamp01(t / 0.3f) + rattle * Noise() * 0.8f) * Env(t, d, 0.02f, 0.8f);
                });
            }
        }

        // Zombie: a slow, wet, guttural groan.
        public static AudioClip ZombieGroan
        {
            get
            {
                double ph = 0;
                float lp = 0f;
                const float dt = 1f / Rate;
                return Get("zombiegroan", 2.4f, (t, d) =>
                {
                    float k = t / d;
                    float f = Mathf.Lerp(85f, 60f, k) * (1f + Mathf.Sin(t * 2f * Mathf.PI * 2.2f) * 0.06f);
                    ph += f * dt;
                    float saw = (float)(ph - System.Math.Floor(ph)) * 2f - 1f;
                    // Vocal "uuh-oh" formant: a slow sweep of a lowpass on the saw.
                    float cutoff = Mathf.Lerp(0.04f, 0.12f, 0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI * 0.7f));
                    lp += (saw - lp) * cutoff;
                    float gurgle = Mathf.Sin(t * 2f * Mathf.PI * 9f) > 0.6f ? Noise() * 0.25f : 0f;
                    return (float)System.Math.Tanh((lp * 1.8f + gurgle) * 1.6f) * 0.6f * Env(t, d, 0.25f, 0.9f);
                });
            }
        }

        // Slime: a deep, bubbling gurgle that wobbles up and down.
        public static AudioClip SlimeGurgle
        {
            get
            {
                double ph = 0;
                const float dt = 1f / Rate;
                return Get("slimegurgle", 1.9f, (t, d) =>
                {
                    // Pitch jumps between "bubbles" every ~90 ms, each one bending upward.
                    int bubble = (int)(t / 0.09f);
                    float local = t - bubble * 0.09f;
                    float baseF = 70f + ((bubble * 7919) % 11) * 9f;
                    float f = baseF * (1f + local * 9f);
                    ph += f * dt;
                    float wob = Mathf.Sin((float)(ph % 1.0) * 2f * Mathf.PI);
                    float pop = local < 0.006f ? Noise() * 0.6f : 0f;
                    return (wob * 0.7f * Mathf.Exp(-local * 18f) + pop + Mathf.Sin(t * 2f * Mathf.PI * 45f) * 0.25f) * Env(t, d, 0.04f, 0.6f);
                });
            }
        }

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
