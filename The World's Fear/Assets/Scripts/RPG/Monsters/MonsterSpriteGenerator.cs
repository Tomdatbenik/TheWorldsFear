using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public class MonsterLook
    {
        public Sprite[] frames;
        public Sprite[] backFrames; // seen from behind: no face
        public Color primary;
        public Color accent;
        public int pixelSize;
    }

    // Seeded procedural pixel-art creatures. Same seed + biome + archetype = same creature.
    public static class MonsterSpriteGenerator
    {
        static readonly Dictionary<long, MonsterLook> cache = new Dictionary<long, MonsterLook>();

        struct BodyPlan
        {
            public int size;
            public float bodyCy, bodyRx, bodyRy;
            public bool head;
            public float headCy, headR;
            public int legPairs, legLength;
            public int horns;       // 0 none, 1 antennae, 2 horns, 3 spikes
            public bool arms;
            public float noise;
            public int eyes;        // pairs of eyes
            public bool core;       // glowing core (exploders)
            public bool hover;      // no legs, tattered bottom (casters)
        }

        public static MonsterLook Generate(int seed, Biome biome, MonsterArchetype archetype)
        {
            long key = ((long)seed << 8) | ((long)biome << 4) | (long)archetype;
            if (cache.TryGetValue(key, out var look) && look.frames[0] != null) return look;

            var rng = new System.Random(seed);
            var plan = Plan(archetype, rng);
            var pal = BiomePalette(biome, rng);

            int s = plan.size;
            var tex = GameAssets.NewTexture(s * 2, s * 2, FilterMode.Point);
            var clear = new Color[s * 2 * s * 2];
            tex.SetPixels(clear);

            // Mask built once from the seed; frames only change legs/bob.
            var patternRng = new System.Random(seed * 7 + 3);
            var noiseField = new float[s, s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                noiseField[x, y] = (float)patternRng.NextDouble();
            int pattern = patternRng.Next(0, 4); // 0 plain, 1 spots, 2 stripes, 3 belly

            for (int frame = 0; frame < 2; frame++)
            {
                var mask = BuildMask(plan, noiseField, frame);
                var colors = Colorize(mask, plan, pal, noiseField, pattern, frame, false);
                var back = Colorize(mask, plan, pal, noiseField, pattern, frame, true);
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    tex.SetPixel(frame * s + x, y, colors[x, y]);
                    tex.SetPixel(frame * s + x, s + y, back[x, y]);
                }
            }
            tex.Apply();

            look = new MonsterLook
            {
                frames = new[]
                {
                    Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.3f), GameAssets.WorldPPU),
                    Sprite.Create(tex, new Rect(s, 0, s, s), new Vector2(0.5f, 0.3f), GameAssets.WorldPPU),
                },
                backFrames = new[]
                {
                    Sprite.Create(tex, new Rect(0, s, s, s), new Vector2(0.5f, 0.3f), GameAssets.WorldPPU),
                    Sprite.Create(tex, new Rect(s, s, s, s), new Vector2(0.5f, 0.3f), GameAssets.WorldPPU),
                },
                primary = pal.body,
                accent = pal.accent,
                pixelSize = s,
            };
            cache[key] = look;
            return look;
        }

        static BodyPlan Plan(MonsterArchetype a, System.Random rng)
        {
            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            var p = new BodyPlan { size = 16, noise = R(0.15f, 0.35f), eyes = 1 };
            switch (a)
            {
                case MonsterArchetype.Chaser:
                    p.bodyCy = R(6, 8); p.bodyRx = R(4.5f, 6f); p.bodyRy = R(3f, 4.2f);
                    p.head = rng.NextDouble() < 0.7; p.headCy = p.bodyCy + R(3, 4.5f); p.headR = R(2.5f, 3.5f);
                    p.legPairs = rng.Next(1, 3); p.legLength = rng.Next(2, 4);
                    p.horns = rng.Next(0, 4); p.eyes = rng.Next(1, 3);
                    break;
                case MonsterArchetype.Swarmling:
                    p.size = 12;
                    p.bodyCy = R(4.5f, 5.5f); p.bodyRx = R(3f, 4.5f); p.bodyRy = R(2.5f, 3.5f);
                    p.legPairs = rng.Next(2, 4); p.legLength = 2;
                    p.horns = 1; p.eyes = rng.Next(1, 3);
                    break;
                case MonsterArchetype.Spitter:
                    p.bodyCy = R(5.5f, 7); p.bodyRx = R(3.5f, 4.5f); p.bodyRy = R(3.5f, 4.5f);
                    p.head = true; p.headCy = p.bodyCy + R(4, 5.5f); p.headR = R(3f, 4f);
                    p.legPairs = 1; p.legLength = rng.Next(2, 4);
                    p.horns = rng.Next(0, 2) * 3; p.eyes = rng.Next(1, 4);
                    break;
                case MonsterArchetype.Charger:
                    p.bodyCy = R(6, 7); p.bodyRx = R(5.5f, 7f); p.bodyRy = R(3.5f, 4.5f);
                    p.head = true; p.headCy = p.bodyCy + R(2.5f, 3.5f); p.headR = R(3f, 3.8f);
                    p.legPairs = 2; p.legLength = rng.Next(2, 4);
                    p.horns = 2; p.eyes = 1;
                    break;
                case MonsterArchetype.Brute:
                    p.size = 20;
                    p.bodyCy = R(8, 9.5f); p.bodyRx = R(6.5f, 8.5f); p.bodyRy = R(5f, 6.5f);
                    p.head = rng.NextDouble() < 0.6; p.headCy = p.bodyCy + R(4.5f, 6f); p.headR = R(3f, 4f);
                    p.legPairs = 1; p.legLength = rng.Next(3, 5);
                    p.arms = true; p.horns = rng.Next(0, 4); p.eyes = 1;
                    break;
                case MonsterArchetype.Caster:
                    p.bodyCy = R(6.5f, 7.5f); p.bodyRx = R(3.5f, 4.5f); p.bodyRy = R(4.5f, 5.5f);
                    p.head = true; p.headCy = p.bodyCy + R(4.5f, 5.5f); p.headR = R(2.5f, 3.2f);
                    p.hover = true; p.arms = true; p.horns = rng.Next(1, 4); p.eyes = 1;
                    break;
                case MonsterArchetype.Exploder:
                    p.bodyCy = R(6.5f, 7.5f); p.bodyRx = R(5f, 6.5f); p.bodyRy = R(5f, 6f);
                    p.legPairs = rng.Next(0, 2); p.legLength = 2;
                    p.horns = 3; p.core = true; p.eyes = rng.Next(1, 3);
                    break;
            }
            return p;
        }

        struct Pal
        {
            public Color body, dark, light, accent, eye, outline;
        }

        static Pal BiomePalette(Biome b, System.Random rng)
        {
            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            float h, s, v;
            switch (b)
            {
                case Biome.Forest: h = R(0.18f, 0.38f); s = R(0.4f, 0.7f); v = R(0.5f, 0.75f); if (rng.NextDouble() < 0.3) h = R(0.06f, 0.12f); break;
                case Biome.Marsh: h = R(0.72f, 0.9f); s = R(0.35f, 0.6f); v = R(0.45f, 0.7f); if (rng.NextDouble() < 0.35) h = R(0.22f, 0.3f); break;
                case Biome.Ruins: h = R(0.05f, 0.6f); s = R(0.05f, 0.2f); v = R(0.5f, 0.75f); break;
                case Biome.Ember: h = R(0.0f, 0.08f); s = R(0.6f, 0.9f); v = R(0.55f, 0.85f); if (rng.NextDouble() < 0.3) { h = R(0.0f, 0.05f); s = 0.2f; v = 0.3f; } break;
                default: h = (float)rng.NextDouble(); s = 0.5f; v = 0.7f; break;
            }
            var body = Color.HSVToRGB(h, s, v);
            float ah = (h + R(0.35f, 0.65f)) % 1f;
            var accent = Color.HSVToRGB(ah, Mathf.Clamp01(s + 0.25f), Mathf.Clamp01(v + 0.15f));
            Color eye;
            switch (b)
            {
                case Biome.Marsh: eye = new Color(0.8f, 1f, 0.3f); break;
                case Biome.Ruins: eye = new Color(0.4f, 0.9f, 1f); break;
                case Biome.Ember: eye = new Color(1f, 0.95f, 0.4f); break;
                default: eye = rng.NextDouble() < 0.5 ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.95f, 0.6f); break;
            }
            return new Pal
            {
                body = body,
                dark = Color.HSVToRGB(h, Mathf.Clamp01(s + 0.1f), v * 0.55f),
                light = Color.HSVToRGB(h, s * 0.7f, Mathf.Clamp01(v * 1.3f)),
                accent = accent,
                eye = eye,
                outline = Color.HSVToRGB(h, Mathf.Clamp01(s + 0.2f), 0.12f),
            };
        }

        // 0 = empty, 1 = body, 2 = limb/horn, 3 = head
        static int[,] BuildMask(BodyPlan p, float[,] noise, int frame)
        {
            int s = p.size;
            var m = new int[s, s];
            float cx = (s - 1) / 2f;
            int bob = frame == 1 && !p.hover ? 1 : 0;
            int hoverBob = frame == 1 && p.hover ? 1 : 0;

            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x - cx) / p.bodyRx;
                float dy = (y - (p.bodyCy + bob + hoverBob)) / p.bodyRy;
                float d = dx * dx + dy * dy;
                float threshold = 1f + (noise[Mirror(x, s), y] - 0.5f) * p.noise * 2f;
                if (d <= threshold) m[x, y] = 1;

                if (p.head)
                {
                    float hx = (x - cx) / p.headR;
                    float hy = (y - (p.headCy + bob + hoverBob)) / p.headR;
                    if (hx * hx + hy * hy <= 1f + (noise[Mirror(x, s), y] - 0.5f) * p.noise) m[x, y] = 3;
                }
            }

            // Legs: vertical stubs under the body, alternating per frame.
            int bottom = Mathf.FloorToInt(p.bodyCy - p.bodyRy + 1);
            for (int i = 0; i < p.legPairs; i++)
            {
                int lx = Mathf.RoundToInt(cx - p.bodyRx * (0.35f + 0.5f * (i + 1) / (p.legPairs + 1f)));
                int shift = ((i + frame) % 2 == 0) ? 0 : 1;
                for (int k = 0; k < p.legLength + 1; k++)
                {
                    int y = bottom - k + shift + bob;
                    int x = lx + (k == p.legLength ? -1 : 0);
                    Set(m, x, y, 2, s);
                }
            }

            // Hovering casters get a tattered robe hem instead of legs.
            if (p.hover)
            {
                for (int x = 0; x < s; x++)
                {
                    int top = -1;
                    for (int y = 0; y < s; y++)
                        if (m[x, y] != 0) { top = y; break; }
                    if (top < 0) continue;
                    int len = 1 + (int)(noise[Mirror(x, s), 0] * 3f) + ((x + frame) % 2);
                    for (int k = 1; k <= len; k++) Set(m, x, top - k, 1, s);
                }
            }

            // Arms: diagonal limbs from the body's sides.
            if (p.arms)
            {
                int ay = Mathf.RoundToInt(p.bodyCy + bob + hoverBob);
                int ax = Mathf.RoundToInt(cx - p.bodyRx);
                for (int k = 0; k < 4; k++)
                {
                    int y = ay - k + (frame == 1 ? 1 : 0);
                    Set(m, ax - 1 - k / 2, y, 2, s);
                    Set(m, s - 1 - (ax - 1 - k / 2), y, 2, s);
                }
            }

            // Horns / antennae / spikes on top.
            float topY = p.head ? p.headCy + p.headR : p.bodyCy + p.bodyRy;
            int ty = Mathf.RoundToInt(topY) + bob + hoverBob;
            if (p.horns == 1)
            {
                for (int k = 0; k < 3; k++)
                {
                    Set(m, Mathf.RoundToInt(cx - 1.5f - k * 0.7f), ty + k, 2, s);
                    Set(m, Mathf.RoundToInt(cx + 1.5f + k * 0.7f), ty + k, 2, s);
                }
            }
            else if (p.horns == 2)
            {
                for (int k = 0; k < 3; k++)
                {
                    Set(m, Mathf.RoundToInt(cx - 2.5f - k), ty - 1 + k, 2, s);
                    Set(m, Mathf.RoundToInt(cx + 2.5f + k), ty - 1 + k, 2, s);
                }
            }
            else if (p.horns == 3)
            {
                for (int i = -1; i <= 1; i++) Set(m, Mathf.RoundToInt(cx + i * 2.5f), ty + (i == 0 ? 2 : 1), 2, s);
            }

            // Enforce symmetry from the left half.
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s / 2; x++)
                m[s - 1 - x, y] = Mathf.Max(m[x, y], m[s - 1 - x, y]);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s / 2; x++)
                m[x, y] = m[s - 1 - x, y];

            // Clear the 1px border so the outline fits.
            for (int i = 0; i < s; i++)
            {
                m[i, 0] = m[i, s - 1] = 0;
                m[0, i] = m[s - 1, i] = 0;
            }
            return m;
        }

        static int Mirror(int x, int s) => x < s / 2 ? x : s - 1 - x;

        static void Set(int[,] m, int x, int y, int v, int s)
        {
            if (x < 0 || y < 0 || x >= s || y >= s) return;
            if (m[x, y] == 0) m[x, y] = v;
        }

        static Color[,] Colorize(int[,] m, BodyPlan p, Pal pal, float[,] noise, int pattern, int frame, bool back)
        {
            int s = p.size;
            var c = new Color[s, s];
            float cx = (s - 1) / 2f;
            int bob = frame == 1 ? 1 : 0;

            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                if (m[x, y] == 0)
                {
                    // Outline around any filled neighbour.
                    if (Filled(m, x + 1, y, s) || Filled(m, x - 1, y, s) || Filled(m, x, y + 1, s) || Filled(m, x, y - 1, s))
                        c[x, y] = pal.outline;
                    else c[x, y] = Color.clear;
                    continue;
                }

                Color col = m[x, y] == 2 ? pal.dark : pal.body;
                // Light from the top-left: top edges lighter, bottom edges darker.
                bool topEdge = !Filled(m, x, y + 1, s);
                bool bottomEdge = !Filled(m, x, y - 1, s);
                bool leftEdge = !Filled(m, x - 1, y, s);
                if (m[x, y] != 2)
                {
                    if (topEdge || (leftEdge && x < cx)) col = pal.light;
                    else if (bottomEdge || y < p.bodyCy - p.bodyRy * 0.5f) col = pal.dark;

                    float n = noise[Mirror(x, s), y];
                    switch (pattern)
                    {
                        case 1: if (n > 0.86f && !topEdge) col = pal.accent; break;
                        case 2: if ((y + (int)(p.bodyCy)) % 3 == 0 && m[x, y] == 1 && !topEdge && !bottomEdge) col = Color.Lerp(col, pal.accent, 0.6f); break;
                        case 3: if (Mathf.Abs(x - cx) < p.bodyRx * 0.45f && y < p.bodyCy + bob && m[x, y] == 1) col = Color.Lerp(pal.light, pal.accent, 0.3f); break;
                    }
                }
                c[x, y] = col;
            }

            // From behind: darker, no face or belly, a ridge down the spine.
            if (back)
            {
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    if (m[x, y] == 0) continue;
                    c[x, y] = Color.Lerp(c[x, y], pal.dark, 0.35f);
                    if (Mathf.Abs(x - cx) < 1f && m[x, y] != 2 && y > p.bodyCy - p.bodyRy * 0.6f) c[x, y] = pal.accent * 0.8f;
                }
                return c;
            }

            // Glowing core.
            if (p.core)
            {
                int cy = Mathf.RoundToInt(p.bodyCy) + bob;
                for (int y = cy - 1; y <= cy + 1; y++)
                for (int x = Mathf.FloorToInt(cx) - 1; x <= Mathf.CeilToInt(cx) + 1; x++)
                    if (x >= 0 && y >= 0 && x < s && y < s && m[x, y] != 0)
                        c[x, y] = (Mathf.Abs(x - cx) < 1f && y == cy) ? Color.white : new Color(1f, 0.7f, 0.2f);
            }

            // Eyes on the head (or upper body).
            float eyeY = p.head ? p.headCy + bob + (p.hover ? bob : 0) : p.bodyCy + p.bodyRy * 0.35f + bob;
            for (int i = 0; i < p.eyes; i++)
            {
                int ey = Mathf.RoundToInt(eyeY) - i * 2 + (i > 0 ? 1 : 0);
                int ex = Mathf.RoundToInt(cx - 1.5f - i * 1.2f);
                SetEye(c, m, ex, ey, pal, s);
                SetEye(c, m, s - 1 - ex, ey, pal, s);
            }
            return c;
        }

        static void SetEye(Color[,] c, int[,] m, int x, int y, Pal pal, int s)
        {
            if (x < 0 || y < 0 || x >= s || y >= s || m[x, y] == 0) return;
            c[x, y] = pal.eye;
            if (y - 1 >= 0 && m[x, y - 1] != 0) c[x, y - 1] = Color.Lerp(pal.eye, Color.black, 0.6f);
        }

        static bool Filled(int[,] m, int x, int y, int s) => x >= 0 && y >= 0 && x < s && y < s && m[x, y] != 0;
    }
}
