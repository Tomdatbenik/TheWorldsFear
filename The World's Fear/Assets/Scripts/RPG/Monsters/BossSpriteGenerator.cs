using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum BossBody { Skeleton, Zombie, Slime }

    // Original pixel art for the guardian bosses (and their smaller minions): a crowned skeleton king,
    // a hulking zombie and a slime sovereign. Front and back views, two frames each.
    public static class BossSpriteGenerator
    {
        const int S = 32;
        static readonly Dictionary<long, MonsterLook> cache = new Dictionary<long, MonsterLook>();

        static readonly Color Ink = new Color(0.07f, 0.05f, 0.06f);

        public static MonsterLook Generate(BossBody body, Color tint, int seed = 0)
        {
            long key = ((long)body << 40) ^ ((long)seed << 24) ^ (long)(tint.r * 255) << 16 ^ (long)(tint.g * 255) << 8 ^ (long)(tint.b * 255);
            if (cache.TryGetValue(key, out var cached) && cached.frames[0] != null) return cached;

            var tex = GameAssets.NewTexture(S * 2, S * 2);
            tex.SetPixels(new Color[S * 2 * S * 2]);
            for (int frame = 0; frame < 2; frame++)
            for (int back = 0; back < 2; back++)
            {
                var c = new Canvas();
                switch (body)
                {
                    case BossBody.Skeleton: Skeleton(c, frame, back == 1, tint); break;
                    case BossBody.Zombie: Zombie(c, frame, back == 1, tint); break;
                    default: Slime(c, frame, back == 1, tint); break;
                }
                c.Outline();
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    tex.SetPixel(frame * S + x, back * S + y, c.px[x, y]);
            }
            tex.Apply();
            Sprite Sp(int fx, int by) => Sprite.Create(tex, new Rect(fx * S, by * S, S, S), new Vector2(0.5f, 0.03f), GameAssets.WorldPPU);
            var look = new MonsterLook
            {
                frames = new[] { Sp(0, 0), Sp(1, 0) },
                backFrames = new[] { Sp(0, 1), Sp(1, 1) },
                primary = tint,
                accent = body == BossBody.Skeleton ? new Color(0.4f, 1f, 0.9f) : body == BossBody.Zombie ? new Color(0.6f, 0.9f, 0.3f) : Color.Lerp(tint, Color.white, 0.4f),
                pixelSize = S,
            };
            cache[key] = look;
            return look;
        }

        class Canvas
        {
            public readonly Color[,] px = new Color[S, S];

            public void Set(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < S && y < S) px[x, y] = c; }
            public void Rect(int x, int y, int w, int h, Color c) { for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) Set(x + i, y + j, c); }
            public void Ellipse(float cx, float cy, float rx, float ry, Color c)
            {
                for (int x = 0; x < S; x++)
                for (int y = 0; y < S; y++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) px[x, y] = c;
                }
            }
            public void Line(int x0, int y0, int x1, int y1, Color c)
            {
                int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                for (int i = 0; i <= n; i++)
                {
                    float t = n == 0 ? 0 : i / (float)n;
                    Set(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), c);
                }
            }
            public bool Filled(int x, int y) => x >= 0 && y >= 0 && x < S && y < S && px[x, y].a > 0f;

            public void Outline()
            {
                var add = new List<Vector2Int>();
                for (int x = 0; x < S; x++)
                for (int y = 0; y < S; y++)
                    if (!Filled(x, y) && (Filled(x + 1, y) || Filled(x - 1, y) || Filled(x, y + 1) || Filled(x, y - 1)))
                        add.Add(new Vector2Int(x, y));
                foreach (var p in add) px[p.x, p.y] = Ink;
            }
        }

        static Color Shade(Color c, float k) { var r = c * k; r.a = 1f; return r; }

        // ---------- Skeleton king: crowned skull, ribcage, tattered cape and a scythe ----------

        static void Skeleton(Canvas c, int frame, bool back, Color tint)
        {
            var bone = Color.Lerp(new Color(0.9f, 0.88f, 0.8f), tint, 0.15f); bone.a = 1f;
            var boneD = Shade(bone, 0.68f);
            var dark = new Color(0.14f, 0.11f, 0.12f);
            var glow = new Color(0.4f, 1f, 0.9f);
            var gold = new Color(0.95f, 0.75f, 0.25f);
            var cape = new Color(0.32f, 0.13f, 0.42f);
            var capeD = Shade(cape, 0.7f);
            var wood = new Color(0.4f, 0.28f, 0.18f);
            var steel = new Color(0.75f, 0.8f, 0.85f);
            int l = frame == 0 ? 0 : 1, r = 1 - l;

            if (!back)
            {
                // Cape behind the body.
                c.Rect(9, 8, 14, 13, capeD);
                for (int x = 9; x < 23; x += 2) c.Set(x, 7, capeD);
            }
            // Legs.
            c.Rect(12, 1 + l, 2, 9, bone); c.Rect(11, 1 + l, 3, 1, boneD); c.Set(12, 5 + l, boneD); c.Set(13, 5 + l, boneD);
            c.Rect(18, 1 + r, 2, 9, bone); c.Rect(18, 1 + r, 3, 1, boneD); c.Set(18, 5 + r, boneD); c.Set(19, 5 + r, boneD);
            // Pelvis and spine.
            c.Rect(11, 10, 10, 2, bone); c.Rect(14, 10, 4, 1, boneD);
            for (int y = 12; y <= 20; y++) c.Rect(15, y, 2, 1, y % 2 == 0 ? bone : boneD);
            // Ribs.
            int[] ribY = { 13, 15, 17, 19 };
            int[] ribW = { 4, 6, 6, 5 };
            for (int i = 0; i < ribY.Length; i++)
            {
                c.Rect(15 - ribW[i], ribY[i], ribW[i], 1, bone);
                c.Rect(17, ribY[i], ribW[i], 1, bone);
                c.Set(15 - ribW[i], ribY[i] - 1, boneD);
                c.Set(16 + ribW[i], ribY[i] - 1, boneD);
            }
            // Shoulders and arms.
            c.Rect(8, 21, 16, 2, bone);
            int sway = frame == 0 ? 0 : 1;
            c.Rect(7, 12 + sway, 2, 9 - sway, bone); c.Rect(6, 11 + sway, 3, 2, boneD);
            c.Rect(23, 12, 2, 9, bone); c.Rect(23, 11, 3, 2, boneD);
            // The scythe in the right hand.
            c.Line(26, 3, 26, 29, wood);
            for (int i = 0; i < 8; i++) { c.Set(25 - i, 29 - i / 3, steel); c.Set(25 - i, 28 - i / 3, Shade(steel, 0.75f)); }

            if (back)
            {
                // From behind: the tattered cape covers the ribs.
                c.Rect(9, 8, 14, 14, cape);
                for (int x = 9; x < 23; x += 2) c.Set(x, 7, cape);
                c.Line(16, 9, 16, 20, capeD);
            }

            // Skull.
            c.Ellipse(16f, 25.5f, 4.6f, 4f, bone);
            c.Rect(13, 21, 6, 2, boneD);
            if (!back)
            {
                c.Rect(13, 25, 2, 2, dark); c.Rect(17, 25, 2, 2, dark);
                c.Set(13, 25, glow); c.Set(18, 25, glow);
                c.Set(16, 23, dark);
                for (int x = 13; x < 19; x += 2) c.Set(x, 21, dark);
            }
            else c.Rect(14, 24, 4, 1, boneD);
            // Crown.
            c.Rect(12, 29, 8, 1, gold);
            c.Set(12, 30, gold); c.Set(12, 31, gold); c.Set(16, 30, gold); c.Set(16, 31, gold); c.Set(19, 30, gold); c.Set(19, 31, gold);
            c.Set(14, 30, gold); c.Set(18, 30, gold);
            if (!back) c.Set(16, 29, new Color(0.9f, 0.15f, 0.2f));
        }

        // ---------- Zombie colossus: hunched, one bloated arm, torn clothes, exposed ribs ----------

        static void Zombie(Canvas c, int frame, bool back, Color tint)
        {
            var skin = Color.Lerp(new Color(0.46f, 0.6f, 0.38f), tint, 0.2f); skin.a = 1f;
            var skinD = Shade(skin, 0.68f);
            var rot = new Color(0.45f, 0.3f, 0.4f);
            var cloth = new Color(0.36f, 0.28f, 0.2f);
            var clothD = Shade(cloth, 0.7f);
            var blood = new Color(0.42f, 0.08f, 0.08f);
            var bone = new Color(0.85f, 0.82f, 0.7f);
            var eye = new Color(1f, 0.9f, 0.3f);
            var dark = new Color(0.12f, 0.1f, 0.08f);
            int l = frame == 0 ? 0 : 1, r = 1 - l;

            // Legs in torn trousers.
            c.Rect(10, 1 + l, 5, 8, cloth); c.Rect(10, 1 + l, 6, 2, skinD); c.Set(12, 5 + l, skin);
            c.Rect(17, 1 + r, 5, 8, cloth); c.Rect(16, 1 + r, 6, 2, skinD); c.Set(19, 6 + r, skin);
            // Bulky torso.
            c.Ellipse(16f, 15f, 9f, 7.5f, skin);
            c.Ellipse(16f, 13f, 8f, 4.5f, cloth);           // rags around the belly
            for (int x = 8; x < 25; x += 3) c.Set(x, 9, clothD);
            c.Ellipse(19f, 21f, 7f, 3f, skinD);              // hunched shoulders
            if (!back)
            {
                // Exposed ribs and a stitched wound.
                for (int y = 15; y <= 19; y += 2) c.Rect(9, y, 4, 1, bone);
                c.Rect(9, 14, 4, 1, blood);
                c.Line(18, 18, 22, 14, dark);
                for (int i = 0; i < 4; i++) c.Set(18 + i, 17 - i, rot);
            }
            else
            {
                // Spine bumps along the back.
                for (int y = 11; y <= 21; y += 2) c.Set(16, y, skinD);
                c.Rect(12, 15, 3, 2, rot);
            }
            // Arms: a thin one and a huge bloated one dragging low.
            int sway = frame == 0 ? 0 : 1;
            c.Rect(5, 8 + sway, 3, 12, skin); c.Rect(4, 6 + sway, 4, 3, skinD);
            c.Set(4, 5 + sway, bone); c.Set(6, 5 + sway, bone);
            c.Ellipse(26.5f, 14f, 3.6f, 6f, skin); c.Ellipse(27f, 12f, 2f, 2.5f, rot);
            c.Rect(24, 3 + (1 - sway), 6, 4, skinD);
            c.Set(24, 2 + (1 - sway), bone); c.Set(26, 2 + (1 - sway), bone); c.Set(28, 2 + (1 - sway), bone);
            // Small head hunched forward.
            c.Ellipse(14f, 24.5f, 4f, 4f, skin);
            c.Rect(11, 27, 6, 2, dark); c.Set(12, 29, dark); c.Set(15, 29, dark);   // patchy hair
            if (!back)
            {
                c.Rect(12, 24, 2, 2, eye);
                c.Rect(15, 24, 2, 2, dark);
                c.Rect(12, 20, 5, 2, dark);                        // hanging jaw
                c.Set(13, 21, bone); c.Set(15, 21, bone);
                c.Set(16, 22, blood);
            }
            else c.Rect(11, 22, 6, 2, skinD);
        }

        // ---------- Slime sovereign: a crowned jelly dome with swallowed bones inside ----------

        static void Slime(Canvas c, int frame, bool back, Color tint)
        {
            var body = tint; body.a = 1f;
            var light = Color.Lerp(body, Color.white, 0.45f);
            var deep = Shade(body, 0.62f);
            var bone = new Color(0.9f, 0.88f, 0.8f);
            var gold = new Color(0.95f, 0.75f, 0.25f);
            var dark = new Color(0.08f, 0.1f, 0.08f);

            // Squash and stretch between the two frames.
            float rx = frame == 0 ? 14f : 15.2f, ry = frame == 0 ? 17f : 15f;
            c.Ellipse(16f, 1f, rx, ry, body);
            c.Ellipse(16f, 1f, rx - 1f, ry - 1f, body);
            // Darker base, lighter dome.
            for (int x = 0; x < S; x++) { if (c.Filled(x, 1)) c.Set(x, 1, deep); if (c.Filled(x, 2)) c.Set(x, 2, deep); }
            c.Ellipse(11f, ry - 4f, 3f, 2f, light);
            c.Set(19, (int)ry - 3, light);
            // Swallowed bones and bubbles inside.
            c.Ellipse(21f, 6f, 2.4f, 2f, Color.Lerp(bone, body, 0.35f));
            c.Set(20, 6, deep); c.Set(22, 6, deep);
            c.Line(7, 4, 11, 6, Color.Lerp(bone, body, 0.4f));
            c.Set(25, 10, light); c.Set(6, 9, light); c.Set(14, 4, light);
            if (!back)
            {
                int ey = frame == 0 ? 11 : 10;
                c.Rect(10, ey, 4, 4, Color.white); c.Rect(18, ey, 4, 4, Color.white);
                c.Rect(11, ey, 2, 2, dark); c.Rect(19, ey, 2, 2, dark);
                c.Rect(12, ey - 4, 8, 2, dark);               // wide grin
                c.Set(11, ey - 3, dark); c.Set(20, ey - 3, dark);
                c.Set(14, ey - 3, Color.white); c.Set(17, ey - 3, Color.white);
            }
            // A crown perched on top, a little crooked.
            int top = Mathf.Min(S - 4, (int)ry);
            c.Rect(12, top, 8, 2, gold);
            c.Set(12, top + 2, gold); c.Set(15, top + 2, gold); c.Set(16, top + 3, gold); c.Set(19, top + 2, gold);
            c.Set(16, top + 1, new Color(0.3f, 0.6f, 1f));
        }
    }
}
