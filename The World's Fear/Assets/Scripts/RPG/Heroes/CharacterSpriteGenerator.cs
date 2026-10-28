using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum Outfit { Warrior, Ranger, Mage, Merchant, Villager }

    // Front, back, left and right views. In 2.5D the view is chosen relative to the camera.
    public enum FacingDir { Down, Up, Left, Right }

    enum DrawDir { Down, Up, Side }

    // Frames for one character: [FacingDir][frame].
    public class CharacterSprites
    {
        public Sprite[][] idle = new Sprite[4][];
        public Sprite[][] walk = new Sprite[4][];
        public Sprite[][] attack = new Sprite[4][];
        public Sprite weapon;
    }

    // Original procedural pixel-art characters (no third-party art).
    public static class CharacterSpriteGenerator
    {
        const int W = 24, H = 28;
        static readonly Dictionary<Outfit, CharacterSprites> cache = new Dictionary<Outfit, CharacterSprites>();

        struct Look
        {
            public Color skin, hair, top, topDark, pants, boots, accent, hat, hatDark, cape;
            public bool helmet, hood, wizardHat, robe, cape_, quiver, beard, apron, plume;
        }

        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static Look LookFor(Outfit o)
        {
            switch (o)
            {
                case Outfit.Warrior:
                    return new Look
                    {
                        skin = C("#e8b48a"), hair = C("#5a3a22"), top = C("#9aa6b4"), topDark = C("#5f6875"), pants = C("#4a3a30"),
                        boots = C("#3a2a22"), accent = C("#c8372d"), hat = C("#b8c2cc"), hatDark = C("#6c7681"), cape = C("#a52a24"),
                        helmet = true, cape_ = true, plume = true,
                    };
                case Outfit.Ranger:
                    return new Look
                    {
                        skin = C("#e8b48a"), hair = C("#c58a3a"), top = C("#7a5a36"), topDark = C("#4e3a24"), pants = C("#4a5a34"),
                        boots = C("#3c2a1c"), accent = C("#d9a441"), hat = C("#3f7a3a"), hatDark = C("#2a5228"), cape = C("#356a32"),
                        hood = true, cape_ = true, quiver = true,
                    };
                case Outfit.Mage:
                    return new Look
                    {
                        skin = C("#f0c39c"), hair = C("#d8d8e8"), top = C("#5b3aa0"), topDark = C("#3a2470"), pants = C("#3a2470"),
                        boots = C("#2a1c40"), accent = C("#f2c94c"), hat = C("#4a2e88"), hatDark = C("#2e1c58"), cape = C("#3a2470"),
                        wizardHat = true, robe = true, beard = true,
                    };
                case Outfit.Merchant:
                    return new Look
                    {
                        skin = C("#d9a07a"), hair = C("#3a2a1e"), top = C("#b07a3a"), topDark = C("#7a5226"), pants = C("#4a3a2a"),
                        boots = C("#2e2218"), accent = C("#e8d8b0"), hat = C("#8a2a2a"), hatDark = C("#5a1a1a"), cape = C("#000000"),
                        apron = true, beard = true,
                    };
                default:
                    return new Look
                    {
                        skin = C("#e8b48a"), hair = C("#6a4a2a"), top = C("#5a7a9a"), topDark = C("#3a5068"), pants = C("#5a4a3a"),
                        boots = C("#3a2a22"), accent = C("#d0c0a0"), hat = C("#6a4a2a"), hatDark = C("#4a321c"), cape = C("#000000"),
                    };
            }
        }

        public static CharacterSprites Get(Outfit outfit)
        {
            if (cache.TryGetValue(outfit, out var set) && set.idle[0] != null && set.idle[0][0] != null) return set;
            var look = LookFor(outfit);

            // Atlas: rows = direction, columns = idle(2) walk(4) attack(2).
            var tex = GameAssets.NewTexture(W * 8, H * 4);
            tex.SetPixels(new Color[W * 8 * H * 4]);
            set = new CharacterSprites();
            for (int d = 0; d < 4; d++)
            {
                var facing = (FacingDir)d;
                var drawDir = facing == FacingDir.Down ? DrawDir.Down : facing == FacingDir.Up ? DrawDir.Up : DrawDir.Side;
                bool mirror = facing == FacingDir.Left;
                set.idle[d] = new Sprite[2];
                set.walk[d] = new Sprite[4];
                set.attack[d] = new Sprite[2];
                for (int f = 0; f < 8; f++)
                {
                    var pose = new Pose { dir = drawDir };
                    if (f < 2) pose.bob = f;
                    else if (f < 6) pose.step = new[] { 1, 0, -1, 0 }[f - 2];
                    else pose.attack = f - 5;
                    if (f >= 2 && f < 6) pose.bob = (f % 2 == 1) ? 1 : 0;
                    var px = Draw(look, pose);
                    for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        tex.SetPixel(f * W + x, d * H + y, px[mirror ? W - 1 - x : x, y]);
                }
            }
            tex.Apply();
            for (int d = 0; d < 4; d++)
            for (int f = 0; f < 8; f++)
            {
                var s = Sprite.Create(tex, new Rect(f * W, d * H, W, H), new Vector2(0.5f, 0.12f), GameAssets.WorldPPU);
                s.name = $"{outfit}_{(FacingDir)d}_{f}";
                if (f < 2) set.idle[d][f] = s;
                else if (f < 6) set.walk[d][f - 2] = s;
                else set.attack[d][f - 6] = s;
            }
            set.weapon = WeaponSprite(outfit, look);
            cache[outfit] = set;
            return set;
        }

        struct Pose
        {
            public DrawDir dir;
            public int step;   // -1, 0, 1 leg offset
            public int bob;    // 0/1 body height
            public int attack; // 0 none, 1 windup, 2 strike
        }

        class Canvas
        {
            public readonly Color[,] px = new Color[W, H];

            public void Set(int x, int y, Color c)
            {
                if (x >= 0 && y >= 0 && x < W && y < H) px[x, y] = c;
            }

            public void Rect(int x, int y, int w, int h, Color c)
            {
                for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    Set(x + i, y + j, c);
            }

            public bool Filled(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && px[x, y].a > 0f;
        }

        static Color[,] Draw(Look l, Pose p)
        {
            var c = new Canvas();
            bool side = p.dir == DrawDir.Side, up = p.dir == DrawDir.Up, down = p.dir == DrawDir.Down;
            int cx = W / 2;
            int b = p.bob;
            int legTop = 8;

            // Cape behind (drawn first).
            if (l.cape_ && !up)
            {
                if (down) c.Rect(cx - 5, legTop + 1 - b, 10, 8, l.cape * 0.8f);
                if (side) c.Rect(cx - 6, legTop - b, 3, 9, l.cape * 0.85f);
            }

            // Legs.
            if (!l.robe)
            {
                if (side)
                {
                    c.Rect(cx - 2 + p.step, 3, 3, legTop - 3, l.pants);
                    c.Rect(cx - 1 - p.step, 3, 3, legTop - 3, l.pants * 0.8f);
                    c.Rect(cx - 2 + p.step, 2, 4, 2, l.boots);
                    c.Rect(cx - 1 - p.step, 2, 4, 2, l.boots * 0.85f);
                }
                else
                {
                    int lo = p.step == 1 ? 1 : 0, ro = p.step == -1 ? 1 : 0;
                    c.Rect(cx - 4, 3 + lo, 3, legTop - 3, l.pants);
                    c.Rect(cx + 1, 3 + ro, 3, legTop - 3, l.pants);
                    c.Rect(cx - 4, 2 + lo, 3, 2, l.boots);
                    c.Rect(cx + 1, 2 + ro, 3, 2, l.boots);
                }
            }

            // Torso.
            int torsoY = legTop - b;
            int torsoH = 8;
            if (l.robe)
            {
                // Long robe flaring to the ground.
                for (int y = 2; y < torsoY + torsoH; y++)
                {
                    int half = y < torsoY ? 5 + (torsoY - y) / 3 : 4;
                    if (side) half = Mathf.Max(3, half - 1);
                    c.Rect(cx - half, y, half * 2, 1, y < 4 ? l.topDark : l.top);
                }
                c.Rect(cx - 1, 2, 2, torsoY + torsoH - 2, l.accent * 0.9f);
                c.Rect(cx - (side ? 3 : 4), 2 + (p.step != 0 ? 1 : 0), 3, 1, l.boots);
            }
            else
            {
                int tw = side ? 6 : 8;
                c.Rect(cx - tw / 2, torsoY, tw, torsoH, l.top);
                c.Rect(cx - tw / 2, torsoY, tw, 2, l.topDark);
                c.Rect(cx - tw / 2, torsoY + 2, tw, 1, l.accent * 0.8f); // belt
                if (l.apron && !up) c.Rect(cx - 3, torsoY - 1, 6, 6, l.accent);
                if (l.helmet && !up) c.Rect(cx - 1, torsoY + 3, 2, 4, l.topDark); // plate seam
            }

            // Arms (swing with walk, raised on attack).
            int armY = torsoY + 3;
            int swing = p.attack > 0 ? 0 : p.step;
            Color sleeve = l.robe ? l.top : l.topDark;
            if (side)
            {
                int ax = p.attack == 2 ? cx + 3 : p.attack == 1 ? cx - 3 : cx - 1 + swing;
                int ay = p.attack == 1 ? armY + 3 : armY;
                c.Rect(ax, ay, 2, 4, sleeve);
                c.Rect(ax, ay - 1, 2, 1, l.skin);
            }
            else
            {
                int raise = p.attack == 1 ? 3 : p.attack == 2 ? -1 : 0;
                c.Rect(cx - 6, armY + swing, 2, 5, sleeve);
                c.Rect(cx + 4, armY - swing + raise, 2, 5, sleeve);
                c.Rect(cx - 6, armY - 1 + swing, 2, 1, l.skin);
                c.Rect(cx + 4, armY - 1 - swing + raise, 2, 1, l.skin);
            }

            // Head.
            int headY = torsoY + torsoH;
            int hw = side ? 7 : 8;
            int hx = side ? cx - 3 : cx - 4;
            c.Rect(hx, headY, hw, 7, l.skin);
            c.Rect(hx + 1, headY - 1, hw - 2, 1, l.skin * 0.85f);
            // Hair.
            if (up) c.Rect(hx, headY + 1, hw, 6, l.hair);
            else if (side) { c.Rect(hx, headY + 4, hw, 3, l.hair); c.Rect(hx, headY + 1, 2, 4, l.hair); }
            else c.Rect(hx, headY + 5, hw, 2, l.hair);
            // Face.
            if (down)
            {
                c.Set(cx - 2, headY + 3, new Color(0.1f, 0.08f, 0.1f));
                c.Set(cx + 1, headY + 3, new Color(0.1f, 0.08f, 0.1f));
                c.Set(cx - 3, headY + 2, l.skin * new Color(1f, 0.8f, 0.8f));
                c.Set(cx + 2, headY + 2, l.skin * new Color(1f, 0.8f, 0.8f));
            }
            else if (side)
            {
                c.Set(cx + 2, headY + 3, new Color(0.1f, 0.08f, 0.1f));
                c.Set(cx + 4, headY + 2, l.skin * 0.9f); // nose
            }
            if (l.beard && !up)
            {
                if (down) c.Rect(cx - 3, headY - 1, 6, 3, l.hair);
                else c.Rect(cx, headY - 1, 4, 3, l.hair);
            }

            // Headgear.
            int top = headY + 7;
            if (l.helmet)
            {
                c.Rect(hx - 1, headY + 3, hw + 2, 5, l.hat);
                c.Rect(hx - 1, headY + 3, hw + 2, 1, l.hatDark);
                if (down) { c.Rect(cx - 3, headY + 2, 6, 2, l.hat); c.Rect(cx - 2, headY + 3, 4, 1, new Color(0.1f, 0.1f, 0.12f)); }
                if (side) c.Rect(cx + 1, headY + 3, 3, 1, new Color(0.1f, 0.1f, 0.12f));
                if (l.plume) c.Rect(cx - 1, top, 2, 3, l.accent);
            }
            else if (l.hood)
            {
                c.Rect(hx - 1, headY + 2, hw + 2, 6, l.hat);
                c.Rect(hx - 1, top, hw + 2, 1, l.hatDark);
                if (down) c.Rect(hx + 1, headY + 1, hw - 2, 5, l.skin);
                if (side) c.Rect(hx + 3, headY + 1, hw - 3, 5, l.skin);
                if (down) { c.Set(cx - 2, headY + 3, new Color(0.1f, 0.08f, 0.1f)); c.Set(cx + 1, headY + 3, new Color(0.1f, 0.08f, 0.1f)); }
                if (side) c.Set(cx + 2, headY + 3, new Color(0.1f, 0.08f, 0.1f));
            }
            else if (l.wizardHat)
            {
                c.Rect(hx - 2, headY + 5, hw + 4, 2, l.hat); // brim
                for (int i = 0; i < 6; i++)
                {
                    int wHat = Mathf.Max(1, hw - i - 1);
                    int off = side ? -i / 2 : 0;
                    c.Rect(cx - wHat / 2 + off, headY + 7 + i, wHat, 1, i % 3 == 1 ? l.accent : l.hat);
                }
            }
            else if (l.apron)
            {
                c.Rect(hx, headY + 5, hw, 2, l.hat); // cap
            }

            // Cape over the back when seen from behind, quiver on the back.
            if (up && l.cape_) c.Rect(cx - 5, torsoY - 2, 10, torsoH + 1, l.cape);
            if (l.quiver && (up || side))
            {
                int qx = up ? cx + 1 : cx - 4;
                c.Rect(qx, torsoY + 2, 3, 8, new Color(0.45f, 0.3f, 0.18f));
                c.Rect(qx, torsoY + 10, 1, 2, Color.white);
                c.Rect(qx + 2, torsoY + 10, 1, 2, Color.white);
            }

            // Outline.
            var outline = new Color(0.07f, 0.05f, 0.07f, 1f);
            var result = new Color[W, H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (c.Filled(x, y))
                {
                    var col = c.px[x, y];
                    col.a = 1f;
                    result[x, y] = col;
                }
                else if (c.Filled(x + 1, y) || c.Filled(x - 1, y) || c.Filled(x, y + 1) || c.Filled(x, y - 1)) result[x, y] = outline;
                else result[x, y] = Color.clear;
            }
            return result;
        }

        static Sprite WeaponSprite(Outfit outfit, Look l)
        {
            const int s = 16;
            var tex = GameAssets.NewTexture(s, s);
            var px = new Color[s * s];
            void P(int x, int y, Color c)
            {
                if (x >= 0 && y >= 0 && x < s && y < s) px[y * s + x] = c;
            }
            var dark = new Color(0.07f, 0.05f, 0.07f);
            if (outfit == Outfit.Ranger)
            {
                for (int y = 1; y < 15; y++)
                {
                    int x = 4 + Mathf.RoundToInt(Mathf.Sin((y - 1) / 13f * Mathf.PI) * 4f);
                    P(x, y, new Color(0.55f, 0.36f, 0.2f));
                    P(x - 1, y, dark);
                    P(4, y, new Color(0.9f, 0.9f, 0.85f)); // string
                }
            }
            else if (outfit == Outfit.Mage)
            {
                for (int y = 0; y < 13; y++) { P(7, y, new Color(0.45f, 0.3f, 0.18f)); P(8, y, dark); }
                for (int x = 5; x < 11; x++) for (int y = 12; y < 16; y++) P(x, y, (x + y) % 3 == 0 ? Color.white : l.accent);
            }
            else
            {
                for (int y = 5; y < 16; y++) { P(7, y, new Color(0.85f, 0.88f, 0.92f)); P(8, y, new Color(0.6f, 0.64f, 0.7f)); P(6, y, dark); P(9, y, dark); }
                for (int x = 4; x < 12; x++) P(x, 4, l.accent);
                for (int y = 0; y < 4; y++) { P(7, y, new Color(0.4f, 0.26f, 0.16f)); P(8, y, new Color(0.3f, 0.2f, 0.12f)); }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.15f), GameAssets.WorldPPU);
        }

        // Original arrow sprite for the Ranger.
        public static Sprite Arrow()
        {
            return GameAssets.Make("gen_arrow", 16, 5, (x, y, w, h) =>
            {
                if (y == 2 && x >= 2 && x < 13) return new Color(0.6f, 0.42f, 0.25f);
                if (x >= 12 && Mathf.Abs(y - 2) <= 15 - x - 1) return new Color(0.82f, 0.85f, 0.9f);
                if (x < 4 && y != 2 && (x + y) % 2 == 0) return new Color(0.9f, 0.9f, 0.9f);
                return Color.clear;
            }, GameAssets.WorldPPU, FilterMode.Point);
        }
    }
}
