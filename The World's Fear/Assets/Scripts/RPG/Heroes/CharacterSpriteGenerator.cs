using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    public enum Outfit { Warrior, Ranger, Mage, Merchant, Villager, Adventurer, Smith }

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
        const int W = 48, H = 56;
        const int PPU = GameAssets.WorldPPU * 2;   // twice the detail at the same size
        static readonly Dictionary<Outfit, CharacterSprites> cache = new Dictionary<Outfit, CharacterSprites>();

        struct Look
        {
            public Color skin, hair, top, topDark, pants, boots, accent, hat, hatDark, cape, hands;   // hands: gloves (clear = bare)
            public bool helmet, hood, wizardHat, robe, cape_, quiver, beard, apron, plume;
            public bool armored;   // metal chest: sheen, rivets
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
                        helmet = true, cape_ = true, plume = true, armored = true,
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
                case Outfit.Adventurer:
                    return new Look
                    {
                        skin = C("#e8b48a"), hair = C("#7a4a26"), top = C("#4f6f8f"), topDark = C("#33485f"), pants = C("#5a4636"),
                        boots = C("#3a2a1e"), accent = C("#c9a24a"), hat = C("#7a4a26"), hatDark = C("#5a341a"), cape = C("#8a3a2e"),
                        cape_ = true,
                    };
                case Outfit.Smith:
                    return new Look
                    {
                        skin = C("#c98f6a"), hair = C("#2e2a28"), top = C("#6a6a72"), topDark = C("#44444a"), pants = C("#3a3230"),
                        boots = C("#241c18"), accent = C("#5a3a24"), hat = C("#2e2a28"), hatDark = C("#1e1a18"), cape = C("#000000"),
                        apron = true, beard = true,
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

        static Color Hand(Look l) => l.hands.a > 0f ? l.hands : l.skin;

        // ---------- The hero, dressed in their equipped gear ----------

        // Rarity of each worn armor piece, or -1 when the slot is empty.
        public struct Gear
        {
            public int helm, chest, gloves, boots;
            public string Key => $"{helm}_{chest}_{gloves}_{boots}";
        }

        static readonly Dictionary<string, CharacterSprites> gearCache = new Dictionary<string, CharacterSprites>();

        // Armor metal by rarity: iron, steel-blue, gold, burnished orange, crimson.
        static Color Metal(int rarity)
        {
            switch (rarity)
            {
                case 1: return C("#7d93c4");
                case 2: return C("#cfae58");
                case 3: return C("#e08a3a");
                case 4: return C("#c43a5e");
                default: return C("#9aa0aa");
            }
        }

        public static CharacterSprites GetGeared(Gear g)
        {
            string key = g.Key;
            if (gearCache.TryGetValue(key, out var set) && set.idle[0] != null && set.idle[0][0] != null) return set;
            var l = LookFor(Outfit.Adventurer);
            l.cape_ = false;
            if (g.helm >= 0)
            {
                var m = Metal(g.helm);
                l.helmet = true;
                l.hat = m;
                l.hatDark = m * 0.6f;
                l.plume = g.helm >= 2;
                l.accent = Color.Lerp(m, Color.white, 0.3f);
            }
            if (g.chest >= 0)
            {
                var m = Metal(g.chest);
                l.top = m;
                l.topDark = m * 0.62f;
                l.armored = true;
                // Rare and better chest armor comes with a cloak in its color.
                l.cape_ = g.chest >= 2;
                l.cape = g.chest >= 4 ? C("#7a1a34") : g.chest == 3 ? C("#a8501c") : C("#8a3a2e");
            }
            if (g.gloves >= 0) l.hands = Metal(g.gloves) * 0.85f;
            if (g.boots >= 0) l.boots = Metal(g.boots) * 0.7f;
            l.hat.a = l.hatDark.a = l.top.a = l.topDark.a = l.boots.a = l.accent.a = 1f;
            if (g.gloves >= 0) l.hands.a = 1f;
            set = Build(l, "Hero" + key, Outfit.Adventurer);
            gearCache[key] = set;
            return set;
        }

        public static CharacterSprites Get(Outfit outfit)
        {
            if (cache.TryGetValue(outfit, out var set) && set.idle[0] != null && set.idle[0][0] != null) return set;
            set = Build(LookFor(outfit), outfit.ToString(), outfit);
            cache[outfit] = set;
            return set;
        }

        static CharacterSprites Build(Look look, string name, Outfit outfit)
        {
            CharacterSprites set;

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
                var s = Sprite.Create(tex, new Rect(f * W, d * H, W, H), new Vector2(0.5f, 0.04f), PPU);
                s.name = $"{name}_{(FacingDir)d}_{f}";
                if (f < 2) set.idle[d][f] = s;
                else if (f < 6) set.walk[d][f - 2] = s;
                else set.attack[d][f - 6] = s;
            }
            set.weapon = WeaponSprite(outfit, look);
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

        // ---------- Drawing (48x56 px at 32 px per unit: same size in the world, twice the detail) ----------

        static Color Mul(Color c, float k) { var r = c * k; r.a = 1f; return r; }

        // Light comes from the upper left: brighter on the left edge of a shape, darker on the right.
        static Color Lit(Color c, int x, int x0, int x1)
        {
            float t = x1 > x0 ? (x - x0) / (float)(x1 - x0) : 0.5f;
            return Mul(c, Mathf.Lerp(1.12f, 0.72f, t));
        }

        static void Shaded(Canvas c, int x0, int y0, int w, int h, Color col)
        {
            for (int x = x0; x < x0 + w; x++)
            for (int y = y0; y < y0 + h; y++)
                c.Set(x, y, Lit(col, x, x0, x0 + w - 1));
        }

        // A rounded block (chamfered corners), shaded.
        static void Rounded(Canvas c, int x0, int y0, int w, int h, Color col, int r = 2)
        {
            for (int x = x0; x < x0 + w; x++)
            for (int y = y0; y < y0 + h; y++)
            {
                int dx = Mathf.Min(x - x0, x0 + w - 1 - x), dy = Mathf.Min(y - y0, y0 + h - 1 - y);
                if (dx + dy < r) continue;
                c.Set(x, y, Lit(col, x, x0, x0 + w - 1));
            }
        }

        static void Disc(Canvas c, float cx, float cy, float rad, Color col)
        {
            for (int x = Mathf.FloorToInt(cx - rad); x <= Mathf.CeilToInt(cx + rad); x++)
            for (int y = Mathf.FloorToInt(cy - rad); y <= Mathf.CeilToInt(cy + rad); y++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy > rad * rad) continue;
                c.Set(x, y, Mul(col, dx < 0 && dy > 0 ? 1.15f : dx > 0 ? 0.8f : 1f));
            }
        }

        static Color[,] Draw(Look l, Pose p)
        {
            var c = new Canvas();
            bool side = p.dir == DrawDir.Side, up = p.dir == DrawDir.Up, down = p.dir == DrawDir.Down;
            const int cx = W / 2;
            int b = p.bob;
            int hip = 17 - b;
            var ink = new Color(0.1f, 0.07f, 0.09f);
            var eyeWhite = new Color(0.95f, 0.95f, 0.98f);

            // ----- Cape behind everything -----
            if (l.cape_ && !up)
            {
                var cape = Mul(l.cape, 0.75f);
                if (down) Shaded(c, cx - 11, 8 - b, 22, hip + 15 - 8, cape);
                if (side) Shaded(c, cx - 11, 7 - b, 5, hip + 14 - 7, cape);
            }

            // ----- Legs and boots -----
            if (!l.robe)
            {
                if (side)
                {
                    int f = p.step * 3;
                    Shaded(c, cx - 3 - f, 7, 5, hip - 6, Mul(l.pants, 0.8f));   // back leg
                    Rounded(c, cx - 4 - f, 2, 7, 6, Mul(l.boots, 0.8f), 1);
                    Shaded(c, cx - 2 + f, 7, 5, hip - 6, l.pants);              // front leg
                    Rounded(c, cx - 2 + f, 2, 8, 6, l.boots, 1);
                    c.Rect(cx - 2 + f, 7, 6, 1, Mul(l.boots, 1.25f));             // boot cuff
                }
                else
                {
                    int lo = p.step == 1 ? 2 : 0, ro = p.step == -1 ? 2 : 0;
                    Shaded(c, cx - 7, 7 + lo, 6, hip - 6 - lo, l.pants);
                    Shaded(c, cx + 1, 7 + ro, 6, hip - 6 - ro, l.pants);
                    Rounded(c, cx - 8, 2 + lo, 7, 7, l.boots, 1);
                    Rounded(c, cx + 1, 2 + ro, 7, 7, l.boots, 1);
                    c.Rect(cx - 8, 8 + lo, 7, 1, Mul(l.boots, 1.3f));
                    c.Rect(cx + 1, 8 + ro, 7, 1, Mul(l.boots, 1.3f));
                    c.Rect(cx - 7, 2 + lo, 5, 1, Mul(l.boots, 0.6f));             // soles
                    c.Rect(cx + 2, 2 + ro, 5, 1, Mul(l.boots, 0.6f));
                }
            }

            // ----- Torso -----
            int torsoY = hip, torsoH = 16;
            int tw = side ? 12 : 16;
            int tx = cx - tw / 2;
            if (l.robe)
            {
                for (int y = 2; y < torsoY + torsoH; y++)
                {
                    int half = y < torsoY ? 9 + (torsoY - y) / 4 : 8;
                    if (side) half -= 2;
                    for (int x = cx - half; x < cx + half; x++) c.Set(x, y, Lit(y < 5 ? l.topDark : l.top, x, cx - half, cx + half - 1));
                }
                for (int y = 2; y < torsoY + torsoH - 2; y++) c.Set(cx - 1, y, Mul(l.accent, 0.9f));   // trim
                c.Rect(cx - 8, torsoY + 3, 16, 2, Mul(l.accent, 0.85f));                               // sash
            }
            else
            {
                Rounded(c, tx, torsoY - 2, tw, torsoH + 2, l.top, 2);
                // Tunic hem below the belt.
                Shaded(c, tx + 1, torsoY - 3, tw - 2, 2, l.topDark);
                // Belt with buckle.
                c.Rect(tx, torsoY + 2, tw, 2, Mul(l.accent, 0.55f));
                c.Rect(cx - 1, torsoY + 2, 3, 2, Mul(l.accent, 1.15f));
                // Collar.
                if (down)
                {
                    c.Rect(cx - 2, torsoY + torsoH - 3, 4, 3, l.skin);
                    c.Set(cx - 3, torsoY + torsoH - 2, l.topDark);
                    c.Set(cx + 2, torsoY + torsoH - 2, l.topDark);
                }
                // Metal armor: a breastplate sheen, rivets and shoulder plates.
                if (l.armored)
                {
                    for (int y = torsoY + 5; y < torsoY + torsoH - 3; y++) c.Set(tx + 3, y, Mul(l.top, 1.35f));
                    if (!up) { c.Set(tx + 2, torsoY + 6, Mul(l.top, 1.5f)); c.Set(tx + tw - 3, torsoY + 6, Mul(l.top, 0.6f)); }
                }
                if (l.apron && !up) Shaded(c, cx - 6, torsoY - 3, 12, 12, l.accent);
            }
            // Shoulder plates.
            if (l.armored && !side)
            {
                var plate = l.helmet ? l.hat : l.top;
                Disc(c, tx, torsoY + torsoH - 3, 3.5f, plate);
                Disc(c, tx + tw, torsoY + torsoH - 3, 3.5f, plate);
            }

            // ----- Arms -----
            int armTop = torsoY + torsoH - 2;
            int swing = p.attack > 0 ? 0 : p.step * 2;
            Color sleeve = l.robe ? l.top : l.topDark;
            if (side)
            {
                int ax = p.attack == 2 ? cx + 4 : p.attack == 1 ? cx - 5 : cx - 2 + swing;
                int ay = p.attack == 1 ? armTop - 4 : armTop - 10;
                Shaded(c, ax, ay, 4, 10, sleeve);
                Rounded(c, ax, ay - 3, 4, 4, Hand(l), 1);
            }
            else
            {
                int raise = p.attack == 1 ? 6 : p.attack == 2 ? -2 : 0;
                Shaded(c, tx - 4, armTop - 11 + swing, 4, 11, sleeve);
                Shaded(c, tx + tw, armTop - 11 - swing + raise, 4, 11, sleeve);
                Rounded(c, tx - 4, armTop - 14 + swing, 4, 4, Hand(l), 1);
                Rounded(c, tx + tw, armTop - 14 - swing + raise, 4, 4, Hand(l), 1);
            }

            // ----- Head -----
            int headY = torsoY + torsoH;
            int hw = side ? 14 : 16;
            int hx = side ? cx - 6 : cx - 8;
            c.Rect(cx - 2, headY - 1, 4, 2, Mul(l.skin, 0.8f));   // neck
            Rounded(c, hx, headY, hw, 15, l.skin, 3);

            if (up)
            {
                Rounded(c, hx - 1, headY + 2, hw + 2, 14, l.hair, 3);
                for (int y = headY + 4; y < headY + 14; y += 3) c.Set(cx - 3 + (y % 2), y, Mul(l.hair, 0.7f));   // strands
            }
            else if (side)
            {
                // Profile: hair at the back and top, one eye, a nose.
                Rounded(c, hx - 1, headY + 8, hw + 1, 8, l.hair, 2);
                Shaded(c, hx - 1, headY + 3, 6, 7, l.hair);
                c.Rect(hx + 5, headY + 5, 2, 3, Mul(l.skin, 0.8f));                   // ear
                c.Rect(hx + 10, headY + 6, 2, 3, ink);                               // eye
                c.Set(hx + 10, headY + 8, eyeWhite);
                c.Rect(hx + hw, headY + 5, 1, 2, Mul(l.skin, 0.9f));                  // nose
                c.Rect(hx + 10, headY + 3, 3, 1, Mul(l.skin, 0.7f));                  // mouth
                c.Rect(hx + 9, headY + 9, 4, 1, Mul(l.hair, 0.9f));                   // brow
            }
            else
            {
                // Face.
                c.Rect(cx - 5, headY + 5, 2, 3, ink);
                c.Rect(cx + 3, headY + 5, 2, 3, ink);
                c.Set(cx - 5, headY + 7, eyeWhite);
                c.Set(cx + 3, headY + 7, eyeWhite);
                c.Rect(cx - 6, headY + 9, 3, 1, Mul(l.hair, 0.85f));                  // brows
                c.Rect(cx + 3, headY + 9, 3, 1, Mul(l.hair, 0.85f));
                c.Set(cx, headY + 4, Mul(l.skin, 0.82f));                             // nose
                c.Rect(cx - 1, headY + 2, 3, 1, Mul(l.skin, 0.68f));                  // mouth
                c.Rect(cx - 7, headY + 3, 2, 2, l.skin * new Color(1f, 0.82f, 0.82f)); // cheeks
                c.Rect(cx + 5, headY + 3, 2, 2, l.skin * new Color(1f, 0.82f, 0.82f));
                // Hair: a cap with a jagged fringe and side locks.
                Rounded(c, hx - 1, headY + 10, hw + 2, 6, l.hair, 2);
                for (int x = hx; x < hx + hw; x++)
                    if ((x + 1) % 3 != 0) c.Set(x, headY + 9 - (x % 2), Lit(l.hair, x, hx, hx + hw));
                Shaded(c, hx - 1, headY + 3, 2, 8, l.hair);
                Shaded(c, hx + hw - 1, headY + 3, 2, 8, l.hair);
                c.Rect(hx + 2, headY + 13, 4, 1, Mul(l.hair, 1.3f));                  // shine
            }
            if (l.beard && !up)
            {
                if (down) { Rounded(c, cx - 6, headY - 2, 12, 5, l.hair, 2); c.Rect(cx - 1, headY + 2, 3, 1, Mul(l.skin, 0.68f)); }
                else Rounded(c, hx + 6, headY - 2, 8, 5, l.hair, 2);
            }

            // ----- Headgear -----
            int top = headY + 15;
            if (l.helmet)
            {
                Rounded(c, hx - 2, headY + 7, hw + 4, 10, l.hat, 4);
                c.Rect(hx - 2, headY + 7, hw + 4, 2, l.hatDark);                       // rim
                if (down)
                {
                    c.Rect(cx - 5, headY + 5, 10, 2, l.hat);                           // cheek guards
                    c.Rect(cx - 1, headY + 3, 2, 5, l.hat);                            // nose guard
                    c.Rect(cx - 5, headY + 7, 3, 1, ink);                              // eye slits
                    c.Rect(cx + 2, headY + 7, 3, 1, ink);
                }
                if (side) c.Rect(hx + 8, headY + 7, 5, 1, ink);
                c.Rect(hx + 1, headY + 13, 3, 2, Mul(l.hat, 1.4f));                    // sheen
                if (l.plume)
                    for (int i = 0; i < 6; i++) c.Rect(cx - 1 - (side ? i / 2 : 0), top + i - 1, 3, 1, Mul(l.accent, 1f - i * 0.05f));
            }
            else if (l.hood)
            {
                Rounded(c, hx - 2, headY + 1, hw + 4, 16, l.hat, 4);
                c.Rect(hx - 2, top, hw + 4, 1, l.hatDark);
                if (down)
                {
                    Rounded(c, hx + 2, headY + 1, hw - 4, 10, l.skin, 2);
                    c.Rect(cx - 5, headY + 5, 2, 3, ink); c.Rect(cx + 3, headY + 5, 2, 3, ink);
                    c.Set(cx - 5, headY + 7, eyeWhite); c.Set(cx + 3, headY + 7, eyeWhite);
                    c.Rect(cx - 1, headY + 2, 3, 1, Mul(l.skin, 0.68f));
                }
                if (side)
                {
                    Rounded(c, hx + 6, headY + 1, hw - 6, 10, l.skin, 2);
                    c.Rect(hx + 10, headY + 6, 2, 3, ink);
                }
            }
            else if (l.wizardHat)
            {
                Rounded(c, hx - 4, headY + 10, hw + 8, 3, l.hat, 1);                  // brim
                for (int i = 0; i < 12; i++)
                {
                    int wHat = Mathf.Max(2, hw - 2 - i);
                    int off = side ? -i / 3 : (i > 8 ? 1 : 0);
                    for (int x = cx - wHat / 2 + off; x < cx + wHat / 2 + off; x++)
                        c.Set(x, headY + 13 + i, i == 2 || i == 3 ? l.accent : Lit(l.hat, x, cx - wHat / 2 + off, cx + wHat / 2 + off));
                }
            }
            else if (l.apron)
            {
                Rounded(c, hx - 1, headY + 11, hw + 2, 4, l.hat, 1);                  // cap
            }

            // ----- On the back: cape seen from behind, quiver -----
            if (up && l.cape_)
            {
                Shaded(c, cx - 10, torsoY - 4, 20, torsoH + 3, l.cape);
                for (int y = torsoY - 3; y < torsoY + torsoH - 2; y += 1)
                    if (y % 4 == 0) { c.Set(cx - 4, y, Mul(l.cape, 0.7f)); c.Set(cx + 3, y, Mul(l.cape, 0.7f)); }
            }
            if (l.quiver && (up || side))
            {
                int qx = up ? cx + 2 : cx - 8;
                Shaded(c, qx, torsoY + 3, 5, 14, new Color(0.45f, 0.3f, 0.18f));
                for (int i = 0; i < 3; i++) c.Rect(qx + i * 2, torsoY + 17, 1, 3, new Color(0.92f, 0.9f, 0.85f));
            }

            // ----- Selective outline: a dark version of whatever it borders -----
            var result = new Color[W, H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (c.Filled(x, y))
                {
                    var col = c.px[x, y];
                    col.a = 1f;
                    result[x, y] = col;
                    continue;
                }
                Color? n = null;
                if (c.Filled(x + 1, y)) n = c.px[x + 1, y];
                else if (c.Filled(x - 1, y)) n = c.px[x - 1, y];
                else if (c.Filled(x, y + 1)) n = c.px[x, y + 1];
                else if (c.Filled(x, y - 1)) n = c.px[x, y - 1];
                result[x, y] = n.HasValue ? Color.Lerp(Mul(n.Value, 0.3f), ink, 0.6f) : Color.clear;
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
