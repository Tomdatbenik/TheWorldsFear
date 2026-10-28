using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Original procedural spiders: body, patterned abdomen, eye cluster, fangs and eight jointed legs.
    // Front view (facing the camera) and back view, two walk frames each.
    public static class SpiderSpriteGenerator
    {
        const int W = 24, H = 18;
        static readonly Dictionary<long, MonsterLook> cache = new Dictionary<long, MonsterLook>();

        struct Look
        {
            public Color body, dark, light, mark, eye, outline;
            public int pattern;   // 0 hourglass, 1 stripes, 2 spots, 3 skull
            public bool hairy;
            public int eyes;      // 4, 6 or 8
            public float abdomen; // abdomen size
        }

        public static MonsterLook Generate(int seed, Biome biome)
        {
            long key = ((long)seed << 4) | (long)biome;
            if (cache.TryGetValue(key, out var cached) && cached.frames[0] != null) return cached;
            var rng = new System.Random(seed);
            var l = MakeLook(biome, rng);

            var tex = GameAssets.NewTexture(W * 2, H * 2);
            tex.SetPixels(new Color[W * 2 * H * 2]);
            for (int frame = 0; frame < 2; frame++)
            {
                var front = Draw(l, frame, false);
                var back = Draw(l, frame, true);
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    tex.SetPixel(frame * W + x, y, front[x, y]);
                    tex.SetPixel(frame * W + x, H + y, back[x, y]);
                }
            }
            tex.Apply();
            var look = new MonsterLook
            {
                frames = new[]
                {
                    Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.1f), GameAssets.WorldPPU),
                    Sprite.Create(tex, new Rect(W, 0, W, H), new Vector2(0.5f, 0.1f), GameAssets.WorldPPU),
                },
                backFrames = new[]
                {
                    Sprite.Create(tex, new Rect(0, H, W, H), new Vector2(0.5f, 0.1f), GameAssets.WorldPPU),
                    Sprite.Create(tex, new Rect(W, H, W, H), new Vector2(0.5f, 0.1f), GameAssets.WorldPPU),
                },
                primary = l.body,
                accent = l.mark,
                pixelSize = W,
            };
            cache[key] = look;
            return look;
        }

        static Look MakeLook(Biome b, System.Random rng)
        {
            float R(float a, float c) => a + (float)rng.NextDouble() * (c - a);
            Color body, mark, eye;
            switch (b)
            {
                case Biome.Marsh: body = Color.HSVToRGB(R(0.25f, 0.4f), R(0.3f, 0.5f), R(0.3f, 0.45f)); mark = new Color(0.75f, 0.9f, 0.3f); eye = new Color(0.8f, 1f, 0.3f); break;
                case Biome.Ruins: body = Color.HSVToRGB(R(0.55f, 0.7f), R(0.05f, 0.15f), R(0.35f, 0.5f)); mark = new Color(0.85f, 0.85f, 0.8f); eye = new Color(0.4f, 0.9f, 1f); break;
                case Biome.Ember: body = Color.HSVToRGB(R(0f, 0.04f), R(0.5f, 0.8f), R(0.18f, 0.3f)); mark = new Color(1f, 0.5f, 0.1f); eye = new Color(1f, 0.85f, 0.3f); break;
                default: body = Color.HSVToRGB(R(0.05f, 0.1f), R(0.35f, 0.6f), R(0.25f, 0.4f)); mark = rng.NextDouble() < 0.5 ? new Color(0.85f, 0.2f, 0.15f) : new Color(0.95f, 0.75f, 0.3f); eye = new Color(1f, 0.25f, 0.2f); break;
            }
            return new Look
            {
                body = body,
                dark = body * 0.6f,
                light = Color.Lerp(body, Color.white, 0.25f),
                mark = mark,
                eye = eye,
                outline = new Color(0.05f, 0.04f, 0.05f),
                pattern = rng.Next(4),
                hairy = rng.NextDouble() < 0.5,
                eyes = new[] { 4, 6, 8 }[rng.Next(3)],
                abdomen = R(4.2f, 5.4f),
            };
        }

        static Color[,] Draw(Look l, int frame, bool back)
        {
            var px = new Color[W, H];
            void P(int x, int y, Color c)
            {
                if (x >= 0 && y >= 0 && x < W && y < H) px[x, y] = c;
            }
            void Line(int x0, int y0, int x1, int y1, Color c)
            {
                int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
                while (true)
                {
                    P(x0, y0, c);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }
            void Ellipse(float cx, float cy, float rx, float ry, System.Func<int, int, Color> col)
            {
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                    if (nx * nx + ny * ny <= 1f) px[x, y] = col(x, y);
                }
            }

            float cx = W / 2f;
            int bob = frame;
            float bodyY = 6f + bob;

            // Legs: four per side, jointed (hip -> knee up and out -> foot on the ground).
            for (int i = 0; i < 4; i++)
            {
                int lift = ((i + frame) % 2 == 0) ? 1 : 0;
                int hipY = Mathf.RoundToInt(bodyY + 1 - i * 0.6f);
                int kneeX = 3 + i, kneeY = hipY + 4 - i / 2 + lift;
                int footX = Mathf.Max(0, 1 + i * 2 - (i == 3 ? 1 : 0)), footY = lift;
                Color legC = i % 2 == 0 ? l.dark : l.body * 0.8f;
                int hipX = Mathf.RoundToInt(cx - 3);
                Line(hipX, hipY, kneeX, kneeY, legC);
                Line(kneeX, kneeY, footX, footY, legC);
                Line(W - 1 - hipX, hipY, W - 1 - kneeX, kneeY, legC);
                Line(W - 1 - kneeX, kneeY, W - 1 - footX, footY, legC);
                if (l.hairy) { P(kneeX, kneeY + 1, l.dark); P(W - 1 - kneeX, kneeY + 1, l.dark); }
            }

            if (!back)
            {
                // Abdomen peeks out above/behind the body from the front.
                Ellipse(cx, bodyY + 5.5f, l.abdomen, l.abdomen * 0.75f, (x, y) => y > bodyY + 7 ? l.light : l.body);
                // Cephalothorax.
                Ellipse(cx, bodyY + 1.5f, 4.2f, 3.2f, (x, y) => y > bodyY + 2.5f ? l.body : l.dark);
                // Eye cluster: two big eyes plus smaller ones.
                int ey = Mathf.RoundToInt(bodyY + 2);
                P(Mathf.FloorToInt(cx) - 2, ey, l.eye); P(Mathf.FloorToInt(cx) + 1, ey, l.eye);
                P(Mathf.FloorToInt(cx) - 1, ey, l.eye * 0.9f); P(Mathf.FloorToInt(cx), ey, l.eye * 0.9f);
                if (l.eyes >= 6) { P(Mathf.FloorToInt(cx) - 3, ey + 1, l.eye * 0.8f); P(Mathf.FloorToInt(cx) + 2, ey + 1, l.eye * 0.8f); }
                if (l.eyes >= 8) { P(Mathf.FloorToInt(cx) - 1, ey + 2, l.eye * 0.7f); P(Mathf.FloorToInt(cx), ey + 2, l.eye * 0.7f); }
                // Glint.
                P(Mathf.FloorToInt(cx) - 2, ey + 1, Color.white);
                // Fangs.
                P(Mathf.FloorToInt(cx) - 1, Mathf.RoundToInt(bodyY - 1), l.mark * 0.8f);
                P(Mathf.FloorToInt(cx), Mathf.RoundToInt(bodyY - 1), l.mark * 0.8f);
                P(Mathf.FloorToInt(cx) - 1, Mathf.RoundToInt(bodyY - 2), new Color(0.9f, 0.9f, 0.85f));
                P(Mathf.FloorToInt(cx), Mathf.RoundToInt(bodyY - 2), new Color(0.9f, 0.9f, 0.85f));
            }
            else
            {
                // From behind: the big abdomen covers most of the body, with its marking.
                Ellipse(cx, bodyY + 1f, 3.6f, 2.6f, (x, y) => l.dark);
                float ay = bodyY + 4f;
                Ellipse(cx, ay, l.abdomen + 0.6f, l.abdomen * 0.85f, (x, y) =>
                {
                    float dx = Mathf.Abs(x + 0.5f - cx), dy = y + 0.5f - ay;
                    switch (l.pattern)
                    {
                        case 0: if (dx < 1.2f + Mathf.Abs(dy) * 0.5f && Mathf.Abs(dy) < 2.5f && dx < 2f) return l.mark; break;
                        case 1: if (Mathf.RoundToInt(dy + 10) % 3 == 0) return l.mark * 0.9f; break;
                        case 2: if ((x * 7 + y * 13) % 11 == 0) return l.mark; break;
                        case 3: if ((dx < 2f && dy > 0 && dy < 2.5f) || (dx < 0.8f && dy <= 0 && dy > -2f)) return Color.Lerp(l.mark, Color.white, 0.4f); break;
                    }
                    return dy > l.abdomen * 0.4f ? l.light : l.body;
                });
            }

            // Outline everything.
            var result = new Color[W, H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (px[x, y].a > 0f)
                {
                    var c = px[x, y];
                    c.a = 1f;
                    result[x, y] = c;
                }
                else if (Filled(px, x + 1, y) || Filled(px, x - 1, y) || Filled(px, x, y + 1) || Filled(px, x, y - 1)) result[x, y] = l.outline;
                else result[x, y] = Color.clear;
            }
            return result;
        }

        static bool Filled(Color[,] p, int x, int y) => x >= 0 && y >= 0 && x < W && y < H && p[x, y].a > 0f;
    }
}
