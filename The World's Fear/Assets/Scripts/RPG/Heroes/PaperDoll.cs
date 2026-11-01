using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // What a hero looks like, chosen in the character creator.
    [Serializable]
    public class Appearance
    {
        public int body;        // 0 male, 1 female
        public int skin;
        public int hair;        // style, per body type
        public int hairColor;
        public int eyes;
        public int beard;       // male only: 0 none, 1 stubble, 2 full

        public bool Female => body == 1;
        public string Key => $"{body}{skin}{hair}{hairColor}{eyes}{beard}";

        public static readonly Color[] SkinTones =
        {
            new Color(1f, 0.86f, 0.74f), new Color(0.96f, 0.78f, 0.62f), new Color(0.87f, 0.66f, 0.5f),
            new Color(0.74f, 0.52f, 0.38f), new Color(0.56f, 0.38f, 0.27f), new Color(0.4f, 0.27f, 0.2f),
        };
        public static readonly Color[] HairColors =
        {
            new Color(0.2f, 0.15f, 0.12f), new Color(0.45f, 0.28f, 0.16f), new Color(0.78f, 0.6f, 0.33f), new Color(0.92f, 0.86f, 0.7f),
            new Color(0.62f, 0.2f, 0.12f), new Color(0.85f, 0.87f, 0.92f), new Color(0.3f, 0.35f, 0.6f), new Color(0.38f, 0.62f, 0.4f),
        };
        public static readonly Color[] EyeColors =
        {
            new Color(0.3f, 0.5f, 0.85f), new Color(0.35f, 0.65f, 0.35f), new Color(0.5f, 0.32f, 0.18f),
            new Color(0.8f, 0.2f, 0.25f), new Color(0.6f, 0.45f, 0.85f), new Color(0.9f, 0.7f, 0.25f),
        };
        public static readonly string[] MaleHair = { "Short", "Messy", "Long tied", "Buzz", "Swept" };
        public static readonly string[] FemaleHair = { "Long", "Bob", "Ponytail", "Braids", "Pixie" };
        public static readonly string[] Beards = { "None", "Stubble", "Full beard" };

        public int HairStyles => 5;

        public static Appearance Random(System.Random rng)
        {
            var a = new Appearance { body = rng.Next(2), skin = rng.Next(SkinTones.Length), hair = rng.Next(5), hairColor = rng.Next(HairColors.Length), eyes = rng.Next(EyeColors.Length) };
            a.beard = a.Female ? 0 : rng.Next(3);
            return a;
        }
    }

    public enum ChestStyle { Tunic, PaddedVest, Chainmail, Plate, Aegis, Robe, Apron }
    public enum HelmStyle { None, LeatherCap, IronHelm, GreatHelm, Crown, Hood, WizardHat }
    public enum GloveStyle { None, Wraps, Leather, Gauntlets, Talons }
    public enum BootStyle { Shoes, Sandals, Leather, Greaves, Striders }

    // Everything needed to draw one character.
    public struct DollSpec
    {
        public Appearance look;
        public ChestStyle chest; public int chestRarity;
        public HelmStyle helm; public int helmRarity;
        public GloveStyle gloves; public int gloveRarity;
        public BootStyle boots; public int bootRarity;
        public bool cape; public Color capeColor;
        public Color cloth, trousers;   // the plain clothes under the armor

        public string Key => $"{look.Key}|{chest}{chestRarity}|{helm}{helmRarity}|{gloves}{gloveRarity}|{boots}{bootRarity}|{cape}{capeColor}|{cloth}{trousers}";
    }

    // Tall layered characters (64x96 px at 48 px per unit). Each layer (body, hair, chest, helm, gloves,
    // boots, cape) has male and female shapes. Any layer can be replaced by hand-drawn art: put a
    // 512x384 PNG (8 frames x 4 rows, same layout as the generated atlas, Read/Write enabled) at
    // Resources/TWF/CharacterArt/<Male|Female>/<layer>.png, e.g. Female/chest_Plate.png or Male/hair_2.png.
    public static class PaperDoll
    {
        public const int W = 64, H = 96;
        const int PPU = 48;
        static readonly Dictionary<string, CharacterSprites> cache = new Dictionary<string, CharacterSprites>();
        static readonly Dictionary<string, Texture2D> overrides = new Dictionary<string, Texture2D>();

        static readonly Color Ink = new Color(0.09f, 0.06f, 0.08f);

        // ---------- Item looks ----------

        // Armor stays steel; rarity tints it slightly and shows mostly in the trim.
        static readonly Color Steel = new Color(0.7f, 0.73f, 0.78f);

        static Color RarityHue(int rarity)
        {
            switch (rarity)
            {
                case 1: return new Color(0.45f, 0.6f, 1f);
                case 2: return new Color(1f, 0.85f, 0.35f);
                case 3: return new Color(1f, 0.55f, 0.15f);
                case 4: return new Color(0.95f, 0.25f, 0.5f);
                default: return Steel;
            }
        }

        public static Color Metal(int rarity) => Color.Lerp(Steel, RarityHue(rarity), rarity == 0 ? 0f : 0.22f);

        static Color Trim(int rarity) => rarity >= 2 ? Color.Lerp(new Color(1f, 0.82f, 0.35f), RarityHue(rarity), rarity >= 4 ? 0.5f : 0.2f) : rarity == 1 ? new Color(0.55f, 0.65f, 0.9f) : new Color(0.45f, 0.42f, 0.4f);

        // The look of the hero wearing their equipped gear.
        public static DollSpec ForHero(Appearance look, IEnumerable<ItemInstance> equipment)
        {
            var s = new DollSpec { look = look ?? new Appearance(), cloth = new Color(0.32f, 0.44f, 0.58f), trousers = new Color(0.36f, 0.28f, 0.22f) };
            foreach (var item in equipment)
            {
                if (item == null) continue;
                int r = (int)item.rarity, tier = ItemGenerator.BaseTier(item);
                switch (item.slot)
                {
                    case ItemSlot.Chest:
                        s.chest = new[] { ChestStyle.PaddedVest, ChestStyle.Chainmail, ChestStyle.Plate, ChestStyle.Aegis }[tier];
                        s.chestRarity = r;
                        s.cape = r >= 2;
                        s.capeColor = r >= 4 ? new Color(0.48f, 0.1f, 0.2f) : r == 3 ? new Color(0.62f, 0.3f, 0.12f) : new Color(0.5f, 0.2f, 0.18f);
                        break;
                    case ItemSlot.Helm: s.helm = new[] { HelmStyle.LeatherCap, HelmStyle.IronHelm, HelmStyle.GreatHelm, HelmStyle.Crown }[tier]; s.helmRarity = r; break;
                    case ItemSlot.Gloves: s.gloves = new[] { GloveStyle.Wraps, GloveStyle.Leather, GloveStyle.Gauntlets, GloveStyle.Talons }[tier]; s.gloveRarity = r; break;
                    case ItemSlot.Boots: s.boots = new[] { BootStyle.Sandals, BootStyle.Leather, BootStyle.Greaves, BootStyle.Striders }[tier]; s.bootRarity = r; break;
                }
            }
            return s;
        }

        // Villagers and shopkeepers, drawn with the same system so everyone matches.
        public static DollSpec ForOutfit(Outfit o)
        {
            var look = new Appearance { body = 0, skin = 1, hair = 0, hairColor = 1, eyes = 2 };
            var s = new DollSpec { look = look, cloth = new Color(0.4f, 0.5f, 0.55f), trousers = new Color(0.36f, 0.28f, 0.22f), boots = BootStyle.Leather };
            switch (o)
            {
                case Outfit.Warrior:
                    look.hairColor = 1; look.beard = 1;
                    s.chest = ChestStyle.Plate; s.helm = HelmStyle.IronHelm; s.gloves = GloveStyle.Gauntlets; s.boots = BootStyle.Greaves;
                    s.cape = true; s.capeColor = new Color(0.6f, 0.16f, 0.14f);
                    break;
                case Outfit.Ranger:
                    look.body = 1; look.hair = 3; look.hairColor = 2; look.eyes = 1; look.skin = 2;
                    s.chest = ChestStyle.PaddedVest; s.cloth = new Color(0.32f, 0.45f, 0.28f); s.helm = HelmStyle.Hood; s.gloves = GloveStyle.Leather;
                    s.cape = true; s.capeColor = new Color(0.22f, 0.4f, 0.22f);
                    break;
                case Outfit.Mage:
                    look.hairColor = 5; look.beard = 2; look.skin = 0; look.hair = 2;
                    s.chest = ChestStyle.Robe; s.cloth = new Color(0.36f, 0.24f, 0.62f); s.helm = HelmStyle.WizardHat;
                    break;
                case Outfit.Smith:
                    look.skin = 3; look.hair = 3; look.hairColor = 0; look.beard = 2;
                    s.chest = ChestStyle.Apron; s.cloth = new Color(0.45f, 0.45f, 0.48f); s.gloves = GloveStyle.Leather;
                    break;
                case Outfit.Merchant:
                    look.skin = 2; look.hair = 4; look.beard = 2; look.hairColor = 0;
                    s.chest = ChestStyle.Apron; s.cloth = new Color(0.68f, 0.46f, 0.22f); s.helm = HelmStyle.LeatherCap; s.helmRarity = 0;
                    break;
                case Outfit.Villager:
                    look.body = 1; look.hair = 1; look.hairColor = 4;
                    s.cloth = new Color(0.62f, 0.38f, 0.38f);
                    break;
            }
            return s;
        }

        // ---------- Building the atlas ----------

        public static CharacterSprites Get(DollSpec spec)
        {
            string key = spec.Key;
            if (cache.TryGetValue(key, out var set) && set.idle[0] != null && set.idle[0][0] != null) return set;
            var tex = GameAssets.NewTexture(W * 8, H * 4);
            tex.SetPixels(new Color[W * 8 * H * 4]);
            set = new CharacterSprites();
            for (int d = 0; d < 4; d++)
            {
                var facing = (FacingDir)d;
                var view = facing == FacingDir.Down ? View.Front : facing == FacingDir.Up ? View.Back : View.Side;
                bool mirror = facing == FacingDir.Left;
                set.idle[d] = new Sprite[2];
                set.walk[d] = new Sprite[4];
                set.attack[d] = new Sprite[2];
                for (int f = 0; f < 8; f++)
                {
                    var pose = new Pose { view = view };
                    if (f < 2) pose.bob = f;
                    else if (f < 6) { pose.step = new[] { 1, 0, -1, 0 }[f - 2]; pose.bob = (f % 2 == 1) ? 1 : 0; }
                    else pose.attack = f - 5;
                    var px = Draw(spec, pose, d, f);
                    for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        tex.SetPixel(f * W + x, d * H + y, px[mirror ? W - 1 - x : x, y]);
                }
            }
            tex.Apply();
            for (int d = 0; d < 4; d++)
            for (int f = 0; f < 8; f++)
            {
                var s = Sprite.Create(tex, new Rect(f * W, d * H, W, H), new Vector2(0.5f, 0.02f), PPU);
                s.name = $"doll_{(FacingDir)d}_{f}";
                if (f < 2) set.idle[d][f] = s;
                else if (f < 6) set.walk[d][f - 2] = s;
                else set.attack[d][f - 6] = s;
            }
            set.weapon = null;
            cache[key] = set;
            return set;
        }

        enum View { Front, Back, Side }

        struct Pose
        {
            public View view;
            public int step, bob, attack;
        }

        // ---------- Canvas and shading ----------

        // Pixels remember which shape drew them, so overlapping pieces get a dark line where they meet.
        class Canvas
        {
            public readonly Color[,] px = new Color[W, H];
            public readonly int[,] part = new int[W, H];
            public int current;
            public void Begin() => current++;
            public void Set(int x, int y, Color c)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                px[x, y] = c;
                part[x, y] = c.a > 0f ? current : 0;
            }
            public bool Filled(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && px[x, y].a > 0f;
        }

        static Color Mul(Color c, float k) { var r = c * k; r.a = 1f; return r; }

        // Pixel-art shading: light values snap to five tones, with cool purple shadows and warm highlights.
        static Color Tone(Color c, float light)
        {
            int band = light < 0.8f ? 0 : light < 0.92f ? 1 : light < 1.04f ? 2 : light < 1.13f ? 3 : 4;
            switch (band)
            {
                case 0: return Mul(Color.Lerp(c, new Color(0.3f, 0.24f, 0.45f), 0.35f), 0.6f);
                case 1: return Mul(Color.Lerp(c, new Color(0.4f, 0.32f, 0.55f), 0.15f), 0.8f);
                case 3: return Mul(Color.Lerp(c, new Color(1f, 0.92f, 0.7f), 0.12f), 1.1f);
                case 4: return Color.Lerp(Mul(c, 1.15f), new Color(1f, 0.97f, 0.85f), 0.35f);
                default: return Mul(c, 1f);
            }
        }

        // A rounded limb from a to b (radius ra to rb), lit from the upper left so it looks round.
        static void Limb(Canvas c, Vector2 a, Vector2 b, float ra, float rb, Color col, Func<int, int, Color, Color> pattern = null)
        {
            c.Begin();
            var dir = b - a;
            float len = Mathf.Max(0.001f, dir.magnitude);
            dir /= len;
            var n = new Vector2(-dir.y, dir.x);
            if (n.x < 0) n = -n;   // n points to the shadow (right) side
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - Mathf.Max(ra, rb) - 1), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + Mathf.Max(ra, rb) + 1);
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - Mathf.Max(ra, rb) - 1), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + Mathf.Max(ra, rb) + 1);
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, dir) / len);
                var q = a + dir * t * len;
                float r = Mathf.Lerp(ra, rb, t);
                float d = Vector2.Distance(p, q);
                if (d > r) continue;
                float side = Vector2.Dot(p - q, n) / r;   // -1 lit .. 1 shadow
                var shade = Tone(col, Mathf.Lerp(1.2f, 0.7f, (side + 1f) / 2f) * (d > r - 0.8f ? 0.92f : 1f));
                c.Set(x, y, pattern != null ? pattern(x, y, shade) : shade);
            }
        }

        static void Ellipse(Canvas c, float cx, float cy, float rx, float ry, Color col, Func<int, int, Color, Color> pattern = null, Func<int, int, bool> mask = null)
        {
            c.Begin();
            for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
            for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy > 1f) continue;
                if (mask != null && !mask(x, y)) continue;
                var shade = Tone(col, Mathf.Lerp(1.18f, 0.72f, (dx + 1f) / 2f) + (dy > 0.45f ? 0.06f : 0f) - (dy < -0.6f ? 0.08f : 0f));
                c.Set(x, y, pattern != null ? pattern(x, y, shade) : shade);
            }
        }

        // A torso-like shape given half widths at key heights, shaded across.
        static void Body(Canvas c, float cx, (float y, float half)[] keys, Color col, Func<int, int, Color, Color> pattern = null, float lean = 0f)
        {
            c.Begin();
            for (int k = 0; k < keys.Length - 1; k++)
            {
                var lo = keys[k];
                var hi = keys[k + 1];
                for (int y = Mathf.RoundToInt(lo.y); y < Mathf.RoundToInt(hi.y); y++)
                {
                    float t = (y - lo.y) / Mathf.Max(0.001f, hi.y - lo.y);
                    float half = Mathf.Lerp(lo.half, hi.half, Mathf.SmoothStep(0f, 1f, t));
                    float mid = cx + lean * (y - keys[0].y);
                    for (int x = Mathf.RoundToInt(mid - half); x < Mathf.RoundToInt(mid + half); x++)
                    {
                        float u = (x + 0.5f - mid) / half;
                        var shade = Tone(col, Mathf.Lerp(1.18f, 0.7f, (u + 1f) / 2f) - (y < lo.y + 1 && k == 0 ? 0.08f : 0f));
                        c.Set(x, y, pattern != null ? pattern(x, y, shade) : shade);
                    }
                }
            }
        }

        static void Rect(Canvas c, int x, int y, int w, int h, Color col) { c.Begin(); for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) c.Set(x + i, y + j, col); }

        // ---------- The figure ----------

        // Joint positions for a pose.
        struct Skeleton
        {
            public float cx, shoulderY, chestY, waistY, hipY, chinY, headH, headW;
            public float shoulderHalf, chestHalf, waistHalf, hipHalf;
            public Vector2 lHip, lKnee, lAnkle, rHip, rKnee, rAnkle;
            public Vector2 lShoulder, lElbow, lWrist, rShoulder, rElbow, rWrist;
        }

        static Skeleton Rig(bool female, Pose p)
        {
            var k = new Skeleton { cx = W / 2f };
            float b = -p.bob;
            if (female)
            {
                k.shoulderY = 67 + b; k.chestY = 60 + b; k.waistY = 52 + b; k.hipY = 44 + b; k.chinY = 71 + b; k.headH = 18; k.headW = 8.2f;
                k.shoulderHalf = 9.5f; k.chestHalf = 9f; k.waistHalf = 6.5f; k.hipHalf = 9.5f;
            }
            else
            {
                k.shoulderY = 69 + b; k.chestY = 62 + b; k.waistY = 53 + b; k.hipY = 45 + b; k.chinY = 73 + b; k.headH = 18; k.headW = 8.6f;
                k.shoulderHalf = 12.5f; k.chestHalf = 11.5f; k.waistHalf = 9f; k.hipHalf = 9f;
            }
            float cx = k.cx;
            if (p.view == View.Side)
            {
                k.shoulderHalf = female ? 5f : 6f; k.chestHalf = female ? 6f : 6.5f; k.waistHalf = female ? 4.5f : 5.5f; k.hipHalf = female ? 6f : 5.5f;
                float s = p.step;
                k.lHip = new Vector2(cx - 0.5f, k.hipY); k.lKnee = new Vector2(cx + 4f * s, 25); k.lAnkle = new Vector2(cx + 6f * s, 7 + (s > 0 ? 1 : 0));
                k.rHip = new Vector2(cx + 0.5f, k.hipY); k.rKnee = new Vector2(cx - 4f * s, 25); k.rAnkle = new Vector2(cx - 6f * s, 7 + (s < 0 ? 1 : 0));
                float swing = p.attack > 0 ? 0 : -s * 4f;
                k.rShoulder = new Vector2(cx, k.shoulderY - 1);
                k.rElbow = new Vector2(cx + swing * 0.6f, k.waistY + 1);
                k.rWrist = new Vector2(cx + swing, k.hipY - 1);
                if (p.attack == 1) { k.rElbow = new Vector2(cx - 3, k.shoulderY + 4); k.rWrist = new Vector2(cx - 5, k.shoulderY + 10); }
                if (p.attack == 2) { k.rElbow = new Vector2(cx + 6, k.shoulderY - 3); k.rWrist = new Vector2(cx + 12, k.shoulderY - 6); }
                k.lShoulder = new Vector2(cx - 1, k.shoulderY - 1);
                k.lElbow = new Vector2(cx - swing * 0.6f - 1, k.waistY + 1);
                k.lWrist = new Vector2(cx - swing - 1, k.hipY - 1);
                return k;
            }
            int lLift = p.step == 1 ? 3 : 0, rLift = p.step == -1 ? 3 : 0;
            float hipX = k.hipHalf - 3.8f;
            k.lHip = new Vector2(cx - hipX, k.hipY); k.lKnee = new Vector2(cx - 3.6f, 25 + lLift * 0.6f); k.lAnkle = new Vector2(cx - 3.8f, 7 + lLift);
            k.rHip = new Vector2(cx + hipX, k.hipY); k.rKnee = new Vector2(cx + 3.6f, 25 + rLift * 0.6f); k.rAnkle = new Vector2(cx + 3.8f, 7 + rLift);
            float sw = p.attack > 0 ? 0 : p.step * 2f;
            float sx = k.shoulderHalf - 1.2f;
            k.lShoulder = new Vector2(cx - sx, k.shoulderY - 1); k.lElbow = new Vector2(cx - sx - 1.5f, k.waistY + 1 + sw); k.lWrist = new Vector2(cx - sx - 1f, k.hipY - 1 + sw * 1.5f);
            k.rShoulder = new Vector2(cx + sx, k.shoulderY - 1); k.rElbow = new Vector2(cx + sx + 1.5f, k.waistY + 1 - sw); k.rWrist = new Vector2(cx + sx + 1f, k.hipY - 1 - sw * 1.5f);
            if (p.attack == 1) { k.rElbow = new Vector2(cx + sx + 4, k.shoulderY + 3); k.rWrist = new Vector2(cx + sx + 3, k.shoulderY + 11); }
            if (p.attack == 2) { k.rElbow = new Vector2(cx + sx + 4, k.waistY + 4); k.rWrist = new Vector2(cx + sx + 8, k.waistY - 2); }
            return k;
        }

        static (float, float)[] TorsoKeys(Skeleton k, float grow = 0f) => new (float, float)[]
        {
            (k.hipY - 3, k.hipHalf + grow), (k.waistY, k.waistHalf + grow), (k.chestY, k.chestHalf + grow), (k.shoulderY, k.shoulderHalf + grow), (k.shoulderY + 2, k.shoulderHalf - 3 + grow),
        };

        static Color[,] Draw(DollSpec s, Pose p, int dir, int frame)
        {
            var c = new Canvas();
            var a = s.look ?? new Appearance();
            bool female = a.Female;
            var k = Rig(female, p);
            var skin = Appearance.SkinTones[Mathf.Clamp(a.skin, 0, Appearance.SkinTones.Length - 1)];
            var hairC = Appearance.HairColors[Mathf.Clamp(a.hairColor, 0, Appearance.HairColors.Length - 1)];
            bool side = p.view == View.Side, back = p.view == View.Back;
            string gender = female ? "Female" : "Male";

            void Layer(string name, Action draw)
            {
                if (!Blit(c, $"{gender}/{name}", dir, frame)) draw();
            }

            // Cape behind the body.
            if (s.cape && !back) Layer("cape", () => Cape(c, k, s, p, behind: true));
            // Back limbs in profile.
            if (side)
            {
                Layer("body_backleg", () => Leg(c, k.lHip, k.lKnee, k.lAnkle, Mul(skin, 0.85f), female));
                Layer("body_backarm", () => Arm(c, k.lShoulder, k.lElbow, k.lWrist, Mul(skin, 0.85f)));
            }
            // Base body.
            Layer("body", () =>
            {
                if (!side) { Leg(c, k.lHip, k.lKnee, k.lAnkle, skin, female); }
                Leg(c, k.rHip, k.rKnee, k.rAnkle, skin, female);
                Body(c, k.cx, TorsoKeys(k), skin);
            });
            // Plain clothes under the armor.
            Layer("clothes", () => Clothes(c, k, s, p, female));
            Layer($"boots_{s.boots}", () => Boots(c, k, s, p, female));
            Layer($"chest_{s.chest}", () => Chest(c, k, s, p, female, skin));
            // Arms on top of the torso.
            Layer("arms", () =>
            {
                if (!side) Arm(c, k.lShoulder, k.lElbow, k.lWrist, skin);
                Arm(c, k.rShoulder, k.rElbow, k.rWrist, skin);
                Sleeves(c, k, s, p);
            });
            if (s.gloves != GloveStyle.None) Layer($"gloves_{s.gloves}", () => Gloves(c, k, s, p));
            // Head, face, hair.
            Layer("head", () => Head(c, k, p, a, skin, hairC));
            Layer($"hair_{a.hair}", () => Hair(c, k, p, a, hairC, s.helm));
            if (s.helm != HelmStyle.None) Layer($"helm_{s.helm}", () => Helm(c, k, s, p, female));
            if (s.cape && back) Layer("cape", () => Cape(c, k, s, p, behind: false));

            return Outline(c);
        }

        // Paste a hand-drawn layer frame if one exists.
        static bool Blit(Canvas c, string name, int dir, int frame)
        {
            if (!overrides.TryGetValue(name, out var tex))
            {
                tex = Resources.Load<Texture2D>("TWF/CharacterArt/" + name);
                if (tex != null && !tex.isReadable) { Debug.LogWarning($"Character art {name} needs Read/Write enabled"); tex = null; }
                overrides[name] = tex;
            }
            if (tex == null || tex.width < W * 8 || tex.height < H * 4) return false;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var col = tex.GetPixel(frame * W + x, dir * H + y);
                if (col.a > 0.5f) c.Set(x, y, col);
            }
            return true;
        }

        // ---------- Body parts ----------

        static void Leg(Canvas c, Vector2 hip, Vector2 knee, Vector2 ankle, Color col, bool female)
        {
            Limb(c, hip, knee, female ? 4.7f : 4.6f, female ? 3.3f : 3.6f, col);
            Limb(c, knee, ankle, female ? 3.3f : 3.6f, 2.2f, col);
            Ellipse(c, ankle.x + 0.6f, ankle.y - 3.5f, 2.6f, 2f, col);   // foot
        }

        static void Arm(Canvas c, Vector2 shoulder, Vector2 elbow, Vector2 wrist, Color col)
        {
            Limb(c, shoulder, elbow, 3f, 2.5f, col);
            Limb(c, elbow, wrist, 2.5f, 2f, col);
            Ellipse(c, wrist.x, wrist.y - 2f, 2f, 2.3f, col);   // hand
        }

        static void Clothes(Canvas c, Skeleton k, DollSpec s, Pose p, bool female)
        {
            bool side = p.view == View.Side;
            // Trousers (or leggings), then a shirt.
            var backTrousers = side ? Mul(s.trousers, 0.8f) : s.trousers;
            Limb(c, k.lHip, k.lKnee, 5.2f, 3.9f, backTrousers);
            Limb(c, k.lKnee, k.lAnkle + Vector2.up * 2, 3.9f, 2.7f, backTrousers);
            Limb(c, k.rHip, k.rKnee, 5.2f, 3.9f, s.trousers);
            Limb(c, k.rKnee, k.rAnkle + Vector2.up * 2, 3.9f, 2.7f, s.trousers);
            Body(c, k.cx, new (float, float)[] { (k.hipY - 2, k.hipHalf + 0.8f), (k.hipY + 2, k.hipHalf + 0.5f) }, s.trousers);   // seat
            Body(c, k.cx, TorsoKeys(k, 0.6f), s.cloth, (x, y, sh) =>
                // Cloth folds gathering at the belt.
                y > k.waistY - 3 && y < k.waistY + 4 && (x + (y - (int)k.waistY)) % 4 == 0 ? Mul(sh, 0.82f) : sh);
            // Belt.
            for (int x = Mathf.RoundToInt(k.cx - k.waistHalf - 1); x < Mathf.RoundToInt(k.cx + k.waistHalf + 1); x++)
            {
                c.Set(x, Mathf.RoundToInt(k.waistY - 3), new Color(0.32f, 0.22f, 0.14f));
                c.Set(x, Mathf.RoundToInt(k.waistY - 4), new Color(0.26f, 0.18f, 0.11f));
            }
            if (!side && p.view == View.Front) Rect(c, Mathf.RoundToInt(k.cx - 1), Mathf.RoundToInt(k.waistY - 4), 3, 2, new Color(0.85f, 0.72f, 0.35f));
            // A pouch on the hip.
            if (p.view != View.Back)
            {
                float px = side ? k.cx - 1 : k.cx + k.waistHalf - 1;
                Ellipse(c, px, k.waistY - 6.5f, 2.2f, 2.6f, new Color(0.45f, 0.3f, 0.18f));
                c.Set(Mathf.RoundToInt(px), Mathf.RoundToInt(k.waistY - 5), new Color(0.85f, 0.72f, 0.35f));
            }
            // Neckline.
            if (p.view == View.Front)
                for (int i = 0; i < 3; i++) Rect(c, Mathf.RoundToInt(k.cx - 2 + i * 0.5f), Mathf.RoundToInt(k.shoulderY - i), 4 - i, 1, Mul(s.cloth, 0.6f));
        }

        static void Sleeves(Canvas c, Skeleton k, DollSpec s, Pose p)
        {
            Color sleeve = s.chest == ChestStyle.Plate || s.chest == ChestStyle.Aegis ? Metal(s.chestRarity) : s.chest == ChestStyle.Chainmail ? Mul(Metal(s.chestRarity), 0.85f)
                         : s.chest == ChestStyle.Robe ? s.cloth : Mul(s.cloth, 0.9f);
            bool longSleeve = s.chest != ChestStyle.Tunic && s.chest != ChestStyle.Apron;
            void One(Vector2 sh, Vector2 el, Vector2 wr)
            {
                Limb(c, sh, el, 3.6f, 3f, sleeve);
                if (longSleeve) Limb(c, el, wr + (s.chest == ChestStyle.Robe ? Vector2.zero : Vector2.up * 1.5f), 3f, s.chest == ChestStyle.Robe ? 3.6f : 2.6f, sleeve);
            }
            if (p.view != View.Side) One(k.lShoulder, k.lElbow, k.lWrist);
            One(k.rShoulder, k.rElbow, k.rWrist);
            // Shoulder plates on plate armor.
            if ((s.chest == ChestStyle.Plate || s.chest == ChestStyle.Aegis) && p.view != View.Side)
            {
                var m = Metal(s.chestRarity);
                var tr = Trim(s.chestRarity);
                // Three overlapping plates per shoulder, the lowest smallest, rims in the trim color.
                for (int i = 2; i >= 0; i--)
                {
                    float w = 4.6f - i * 0.7f, hgt = 3.2f - i * 0.3f, dy = -1f - i * 2.6f;
                    Ellipse(c, k.lShoulder.x - 0.8f, k.lShoulder.y + dy + 1.5f, w, hgt, i == 0 ? m : Mul(m, 0.92f), (x, y, sh) => y == Mathf.RoundToInt(k.lShoulder.y + dy + 1.5f - hgt + 0.6f) && s.chestRarity >= 1 ? tr : sh);
                    Ellipse(c, k.rShoulder.x + 0.8f, k.rShoulder.y + dy + 1.5f, w, hgt, i == 0 ? m : Mul(m, 0.92f), (x, y, sh) => y == Mathf.RoundToInt(k.rShoulder.y + dy + 1.5f - hgt + 0.6f) && s.chestRarity >= 1 ? tr : sh);
                }
                if (s.chest == ChestStyle.Aegis || s.chestRarity >= 3)
                {
                    c.Set(Mathf.RoundToInt(k.lShoulder.x - 2), Mathf.RoundToInt(k.lShoulder.y - 3), Trim(s.chestRarity));
                    c.Set(Mathf.RoundToInt(k.rShoulder.x + 2), Mathf.RoundToInt(k.rShoulder.y - 3), Trim(s.chestRarity));
                }
            }
        }

        static void Chest(Canvas c, Skeleton k, DollSpec s, Pose p, bool female, Color skin)
        {
            bool front = p.view == View.Front, side = p.view == View.Side;
            var metal = Metal(s.chestRarity);
            var trim = Trim(s.chestRarity);
            switch (s.chest)
            {
                case ChestStyle.Tunic:
                    break;
                case ChestStyle.Apron:
                    if (!p.view.Equals(View.Back)) Body(c, k.cx, new (float, float)[] { (k.hipY - 10, k.hipHalf), (k.waistY, k.waistHalf + 1), (k.chestY + 2, k.chestHalf - 1) }, new Color(0.88f, 0.8f, 0.62f));
                    break;
                case ChestStyle.PaddedVest:
                {
                    var col = s.chestRarity >= 1 ? Color.Lerp(new Color(0.55f, 0.4f, 0.26f), metal, 0.3f) : new Color(0.55f, 0.4f, 0.26f);
                    Body(c, k.cx, TorsoKeys(k, 1f), col, (x, y, sh) => y % 3 == 0 ? Mul(sh, 0.82f) : sh);   // quilting
                    if (female && front)   // laced corset front
                        for (int y = Mathf.RoundToInt(k.waistY - 2); y < k.chestY + 1; y += 2) { c.Set(Mathf.RoundToInt(k.cx - 1), y, trim); c.Set(Mathf.RoundToInt(k.cx), y + 1, trim); }
                    Hem(c, k, col, female ? 3 : 4, 0.4f);
                    break;
                }
                case ChestStyle.Chainmail:
                {
                    var chain = Mul(metal, 0.85f);
                    Body(c, k.cx, TorsoKeys(k, 1f), chain, (x, y, sh) => ((x + y) & 1) == 0 ? Mul(sh, 1.18f) : Mul(sh, 0.82f));
                    Hem(c, k, chain, 7, 0.8f, (x, y, sh) => ((x + y) & 1) == 0 ? Mul(sh, 1.18f) : Mul(sh, 0.82f));
                    if (front) Body(c, k.cx, new (float, float)[] { (k.hipY - 6, 3f), (k.chestY, 3f) }, s.capeColor.a > 0 ? Mul(s.capeColor, 1f) : new Color(0.5f, 0.2f, 0.18f));   // surcoat strip
                    break;
                }
                case ChestStyle.Plate:
                case ChestStyle.Aegis:
                {
                    bool aegis = s.chest == ChestStyle.Aegis;
                    // Breastplate: polished with a center ridge and highlight.
                    Body(c, k.cx, TorsoKeys(k, 1.2f), metal, (x, y, sh) =>
                    {
                        if (front && Mathf.Abs(x + 0.5f - k.cx) < 0.6f) return Mul(sh, 1.25f);
                        if (front && x == Mathf.RoundToInt(k.cx - k.chestHalf * 0.55f) && y > k.waistY && y < k.chestY + 3) return Mul(sh, 1.4f);
                        return sh;
                    });
                    if (female && front)
                    {
                        // Shaped plate over the chest.
                        Ellipse(c, k.cx - 3.2f, k.chestY - 0.5f, 3.6f, 3f, metal);
                        Ellipse(c, k.cx + 3.2f, k.chestY - 0.5f, 3.6f, 3f, metal);
                    }
                    // Plate band at the waist with trim.
                    for (int x = Mathf.RoundToInt(k.cx - k.waistHalf - 1.5f); x < Mathf.RoundToInt(k.cx + k.waistHalf + 1.5f); x++)
                        c.Set(x, Mathf.RoundToInt(k.waistY - 2), aegis || s.chestRarity >= 2 ? trim : Mul(metal, 0.7f));
                    if (female)
                    {
                        // Armored skirt: fabric with plate tassets, flaring to mid thigh.
                        var fabric = aegis ? Color.Lerp(new Color(0.24f, 0.36f, 0.68f), RarityHue(s.chestRarity), s.chestRarity >= 3 ? 0.35f : 0f) : (s.cape ? s.capeColor : new Color(0.4f, 0.22f, 0.3f));
                        float len = aegis ? 26f : 14f;
                        Body(c, k.cx, new (float, float)[] { (k.hipY - len, k.hipHalf + (aegis ? 8f : 5f)), (k.hipY - 4, k.hipHalf + 2f), (k.waistY - 2, k.waistHalf + 1.5f) }, fabric,
                            (x, y, sh) => (x % 5 == 0) ? Mul(sh, 0.8f) : sh);
                        if (!aegis) Body(c, k.cx, new (float, float)[] { (k.hipY - 7, k.hipHalf + 3f), (k.waistY - 2, k.waistHalf + 1.5f) }, metal);
                        else Body(c, k.cx, new (float, float)[] { (k.hipY - len, k.hipHalf + 8.5f), (k.hipY - len + 1, k.hipHalf + 8.3f) }, trim);
                    }
                    else
                    {
                        // Tassets: overlapping plates over the hips; the Aegis adds a tabard.
                        Body(c, k.cx, new (float, float)[] { (k.hipY - 8, k.hipHalf + 3f), (k.waistY - 2, k.waistHalf + 2f) }, metal, (x, y, sh) => (y % 3 == 0) ? Mul(sh, 0.75f) : sh);
                        if (aegis && !side)
                        {
                            var tabard = Color.Lerp(new Color(0.22f, 0.32f, 0.62f), RarityHue(s.chestRarity), s.chestRarity >= 3 ? 0.4f : 0f);
                            Body(c, k.cx, new (float, float)[] { (k.hipY - 20, 4.5f), (k.waistY - 2, 4f), (k.chestY + 1, 4.5f) }, tabard);
                            for (int y = Mathf.RoundToInt(k.hipY - 20); y < k.chestY + 1; y++) { c.Set(Mathf.RoundToInt(k.cx - 5), y, trim); c.Set(Mathf.RoundToInt(k.cx + 4), y, trim); }
                            if (front) { Rect(c, Mathf.RoundToInt(k.cx - 1), Mathf.RoundToInt(k.waistY + 2), 2, 6, trim); Rect(c, Mathf.RoundToInt(k.cx - 3), Mathf.RoundToInt(k.waistY + 5), 6, 2, trim); }
                        }
                    }
                    // Gorget around the neck and rivets on the breastplate.
                    if (!side) Body(c, k.cx, new (float, float)[] { (k.shoulderY - 1, 4.5f), (k.shoulderY + 2, 3.5f) }, Mul(metal, 1.05f));
                    if (front)
                        foreach (float rx in new[] { -k.chestHalf + 2.5f, k.chestHalf - 2.5f })
                        { c.Set(Mathf.RoundToInt(k.cx + rx), Mathf.RoundToInt(k.chestY + 3), Color.Lerp(metal, Color.white, 0.6f)); c.Set(Mathf.RoundToInt(k.cx + rx), Mathf.RoundToInt(k.waistY + 1), Color.Lerp(metal, Color.white, 0.6f)); }
                    if (!female)
                    {
                        // Thigh plates and knee guards.
                        foreach (var leg in side ? new[] { (k.rHip, k.rKnee) } : new[] { (k.lHip, k.lKnee), (k.rHip, k.rKnee) })
                        {
                            Limb(c, Vector2.Lerp(leg.Item1, leg.Item2, 0.35f), leg.Item2 + Vector2.up * 1.5f, 4.4f, 3.6f, Mul(metal, 0.95f));
                            Ellipse(c, leg.Item2.x, leg.Item2.y, 3.5f, 3f, metal, (x, y, sh) => y == Mathf.RoundToInt(leg.Item2.y) ? Mul(sh, 0.75f) : sh);
                        }
                    }
                    // Gem on mythic armor.
                    if (s.chestRarity >= 4 && front) Ellipse(c, k.cx, k.chestY + 2, 1.4f, 1.4f, new Color(0.4f, 0.9f, 1f));
                    break;
                }
                case ChestStyle.Robe:
                {
                    Body(c, k.cx, new (float, float)[] { (2f, female ? 14f : 13f), (k.hipY - 6, k.hipHalf + 4f), (k.waistY, k.waistHalf + 1.5f), (k.chestY, k.chestHalf + 1f), (k.shoulderY, k.shoulderHalf + 0.5f), (k.shoulderY + 2, k.shoulderHalf - 2.5f) }, s.cloth);
                    for (int y = 2; y < k.shoulderY; y++) c.Set(Mathf.RoundToInt(k.cx), y, new Color(0.95f, 0.78f, 0.3f));   // trim down the front
                    for (int x = Mathf.RoundToInt(k.cx - k.waistHalf - 2); x < Mathf.RoundToInt(k.cx + k.waistHalf + 2); x++) c.Set(x, Mathf.RoundToInt(k.waistY - 2), new Color(0.95f, 0.78f, 0.3f));
                    break;
                }
            }
        }

        // A skirt-like hem below the belt (vests, mail).
        static void Hem(Canvas c, Skeleton k, Color col, float length, float flare, Func<int, int, Color, Color> pattern = null)
        {
            Body(c, k.cx, new (float, float)[] { (k.hipY - length, k.hipHalf + 1.5f + flare * 2f), (k.waistY - 2, k.waistHalf + 1.2f) }, col, pattern);
        }

        static void Boots(Canvas c, Skeleton k, DollSpec s, Pose p, bool female)
        {
            bool side = p.view == View.Side;
            Color col; float top; float widen;
            switch (s.boots)
            {
                case BootStyle.Sandals: col = new Color(0.5f, 0.34f, 0.2f); top = 9; widen = 0.3f; break;
                case BootStyle.Leather: col = Color.Lerp(new Color(0.38f, 0.25f, 0.16f), Metal(s.bootRarity), s.bootRarity >= 1 ? 0.2f : 0f); top = 18; widen = 0.9f; break;
                case BootStyle.Greaves: col = Metal(s.bootRarity); top = 27; widen = 1.2f; break;
                case BootStyle.Striders: col = Color.Lerp(new Color(0.3f, 0.2f, 0.18f), Metal(s.bootRarity), 0.25f); top = female ? 34 : 24; widen = 0.8f; break;
                default: col = new Color(0.3f, 0.22f, 0.16f); top = 10; widen = 0.6f; break;
            }
            void One(Vector2 knee, Vector2 ankle)
            {
                if (s.boots == BootStyle.Sandals)
                {
                    Ellipse(c, ankle.x + 0.6f, ankle.y - 4.2f, 2.9f, 1.4f, col);
                    for (int i = 0; i < 3; i++) Limb(c, ankle + new Vector2(-2.2f, -2 + i * 2.2f), ankle + new Vector2(2.2f, -2 + i * 2.2f), 0.6f, 0.6f, col);
                    return;
                }
                float t = Mathf.Clamp01((top - ankle.y) / Mathf.Max(1f, knee.y - ankle.y));
                var shin = Vector2.Lerp(ankle, knee, t);
                Limb(c, ankle, shin, 2.5f + widen, 3.1f + widen, col);
                Ellipse(c, ankle.x + 0.8f, ankle.y - 3.6f, 3.1f, 2.3f, col);   // foot
                Limb(c, new Vector2(ankle.x - 2.2f, ankle.y - 5.5f), new Vector2(ankle.x + 3.5f, ankle.y - 5.5f), 0.7f, 0.7f, Mul(col, 0.55f));   // sole
                // Cuff / knee guard, laces on leather boots.
                if (s.boots == BootStyle.Greaves)
                {
                    Ellipse(c, knee.x, knee.y, 3.6f, 3.1f, Mul(col, 1.08f), (x, y, sh) => y == Mathf.RoundToInt(knee.y) ? Mul(sh, 0.75f) : sh);
                    for (int y = Mathf.RoundToInt(ankle.y + 2); y < shin.y - 2; y += 4) Limb(c, new Vector2(ankle.x - 2.4f, y), new Vector2(ankle.x + 2.4f, y), 0.5f, 0.5f, Mul(col, 0.7f));
                }
                else
                {
                    Limb(c, shin + Vector2.left * 3f, shin + Vector2.right * 3f, 1.3f, 1.3f, Mul(col, 1.2f));
                    if (p.view == View.Front)
                        for (int y = Mathf.RoundToInt(ankle.y); y < shin.y - 1; y += 2) c.Set(Mathf.RoundToInt(ankle.x + ((y / 2) % 2 == 0 ? -1 : 0)), y, new Color(0.88f, 0.82f, 0.68f));
                }
            }
            One(k.lKnee, k.lAnkle);
            One(k.rKnee, k.rAnkle);
        }

        static void Gloves(Canvas c, Skeleton k, DollSpec s, Pose p)
        {
            Color col;
            switch (s.gloves)
            {
                case GloveStyle.Wraps: col = new Color(0.75f, 0.68f, 0.55f); break;
                case GloveStyle.Leather: col = new Color(0.4f, 0.27f, 0.17f); break;
                default: col = Metal(s.gloveRarity); break;
            }
            bool big = s.gloves == GloveStyle.Gauntlets || s.gloves == GloveStyle.Talons;
            void One(Vector2 el, Vector2 wr)
            {
                var cuff = Vector2.Lerp(el, wr, big ? 0.35f : 0.6f);
                Limb(c, cuff, wr, big ? 2.8f : 2.3f, big ? 2.3f : 1.9f, col, s.gloves == GloveStyle.Wraps ? (x, y, sh) => y % 2 == 0 ? Mul(sh, 0.85f) : sh : (Func<int, int, Color, Color>)null);
                Ellipse(c, wr.x, wr.y - 2f, big ? 2.6f : 2.1f, big ? 2.6f : 2.3f, col);
                if (s.gloves == GloveStyle.Talons)
                    for (int i = -1; i <= 1; i++) Limb(c, new Vector2(wr.x + i * 1.3f, wr.y - 3.5f), new Vector2(wr.x + i * 1.6f, wr.y - 6f), 0.6f, 0.3f, Mul(col, 1.3f));
            }
            if (p.view != View.Side) One(k.lElbow, k.lWrist);
            One(k.rElbow, k.rWrist);
        }

        static void Head(Canvas c, Skeleton k, Pose p, Appearance a, Color skin, Color hair)
        {
            float hy = k.chinY + k.headH / 2f;
            float cx = k.cx + (p.view == View.Side ? 1f : 0f);
            // Neck.
            Limb(c, new Vector2(k.cx, k.shoulderY), new Vector2(k.cx, k.chinY + 2), 2.4f, 2.2f, Mul(skin, 0.85f));
            Ellipse(c, cx, hy, k.headW, k.headH / 2f, skin);
            // Jaw: narrower chin.
            for (int y = Mathf.RoundToInt(k.chinY); y < k.chinY + 3; y++)
            {
                c.Set(Mathf.RoundToInt(cx - k.headW + (k.chinY + 3 - y)), y, Color.clear);
                c.Set(Mathf.RoundToInt(cx + k.headW - 1 - (k.chinY + 3 - y)), y, Color.clear);
            }
            if (p.view == View.Back) return;
            var eye = Appearance.EyeColors[Mathf.Clamp(a.eyes, 0, Appearance.EyeColors.Length - 1)];
            var white = new Color(0.97f, 0.97f, 1f);
            int ey = Mathf.RoundToInt(k.chinY + 5);
            if (p.view == View.Front)
            {
                // Big anime eyes: thick upper lash with a flick, gradient iris, pupil, two highlights.
                int eh = a.Female ? 5 : 4;
                for (int side = 0; side < 2; side++)
                {
                    int ex = side == 0 ? Mathf.RoundToInt(cx - 6f) : Mathf.RoundToInt(cx + 2f);
                    int outer = side == 0 ? ex - 1 : ex + 4;
                    c.Begin();
                    for (int y = 0; y < eh; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        bool sclera = side == 0 ? x == 0 : x == 3;
                        var col = sclera ? white : (y >= eh - 2 ? Mul(eye, 0.55f) : y == 0 ? Color.Lerp(eye, white, 0.3f) : eye);
                        c.Set(ex + x, ey + y, col);
                    }
                    Rect(c, ex + (side == 0 ? 1 : 1), ey + 1, 2, 2, Mul(eye, 0.3f));                 // pupil
                    c.Set(ex + (side == 0 ? 2 : 1), ey + eh - 2, white);                            // highlights
                    c.Set(ex + (side == 0 ? 1 : 2), ey + 1, Color.Lerp(white, eye, 0.4f));
                    Rect(c, ex - 1, ey + eh, 6, 1, Ink);                                            // upper lash
                    if (a.Female) { Rect(c, outer, ey + eh - 1, 1, 2, Ink); c.Set(side == 0 ? outer - 1 : outer + 1, ey + eh + 1, Ink); }
                    c.Set(side == 0 ? ex + 3 : ex, ey - 1, Mul(skin, 0.75f));                        // lower lid hint
                    Rect(c, ex, ey + eh + 2, 4, 1, Mul(hair, 0.75f));                              // brow
                }
                c.Set(Mathf.RoundToInt(cx), ey - 2, Mul(skin, 0.78f));                   // nose
                Rect(c, Mathf.RoundToInt(cx - 1), ey - 4, 3, 1, a.Female ? new Color(0.8f, 0.42f, 0.45f) : Mul(skin, 0.66f));   // mouth
                Rect(c, Mathf.RoundToInt(cx - 6), ey - 2, 2, 1, skin * new Color(1f, 0.8f, 0.8f));   // cheeks
                Rect(c, Mathf.RoundToInt(cx + 4), ey - 2, 2, 1, skin * new Color(1f, 0.8f, 0.8f));
            }
            else
            {
                int ex = Mathf.RoundToInt(cx + 2.5f);
                Rect(c, ex, ey, 2, 4, white);
                Rect(c, ex + 1, ey, 1, 4, eye);
                Rect(c, ex - 1, ey + 4, 4, 1, Ink);
                Rect(c, ex - 1, ey + 6, 3, 1, Mul(hair, 0.8f));
                Rect(c, Mathf.RoundToInt(cx + k.headW), ey - 2, 1, 2, Mul(skin, 0.9f));   // nose
                Rect(c, Mathf.RoundToInt(cx + 3), ey - 4, 3, 1, Mul(skin, 0.68f));
                Rect(c, Mathf.RoundToInt(cx - 2), ey - 1, 2, 3, Mul(skin, 0.8f));         // ear
            }
            // Beard.
            if (!a.Female && a.beard > 0)
            {
                var bc = Mul(hair, 0.95f);
                if (a.beard == 1)
                    for (int x = Mathf.RoundToInt(cx - k.headW + 2); x < cx + k.headW - 2; x++) if ((x & 1) == 0) c.Set(x, Mathf.RoundToInt(k.chinY + 1 + (x % 3 == 0 ? 1 : 0)), bc);
                if (a.beard == 2) { Ellipse(c, cx + (p.view == View.Side ? 2 : 0), k.chinY + 1.5f, k.headW - 2f, 3.5f, bc); Rect(c, Mathf.RoundToInt(cx - 1), ey - 4, 3, 1, Mul(skin, 0.66f)); }
            }
        }

        // True where the face shows (hair and hoods leave it uncovered).
        static bool InFace(Skeleton k, Pose p, int x, int y, float fringe)
        {
            if (p.view == View.Back || y >= fringe || y < k.chinY - 1) return false;
            float cx = k.cx + (p.view == View.Side ? 1f : 0f);
            return p.view == View.Side ? x >= cx - 2 : Mathf.Abs(x + 0.5f - cx) < k.headW - 1.5f;
        }

        static void Hair(Canvas c, Skeleton k, Pose p, Appearance a, Color hair, HelmStyle helm)
        {
            bool back = p.view == View.Back, side = p.view == View.Side;
            float cx = k.cx + (side ? 1f : 0f);
            float top = k.chinY + k.headH;
            float hy = k.chinY + k.headH / 2f;
            bool covered = helm == HelmStyle.GreatHelm || helm == HelmStyle.Hood;
            int shineY = Mathf.RoundToInt(top - 4);
            Func<int, int, Color, Color> strands = (x, y, sh) =>
                (y == shineY || y == shineY + 1) && x % 3 != 0 ? Color.Lerp(sh, new Color(1f, 0.97f, 0.9f), 0.45f)   // shine band
                : (x % 3 == 0) ? Mul(sh, 0.8f) : sh;
            // Cap of hair over the skull.
            if (!covered)
            {
                // The cap of hair stops at a jagged fringe above the eyes.
                float fringe = k.chinY + 10;
                Ellipse(c, cx - (side ? 1f : 0f), hy + 2f, k.headW + 1f, k.headH / 2f - 0.5f, hair, strands,
                    (x, y) => !InFace(k, p, x, y, fringe - ((x % 3 == 1) ? 1 : 0)));
            }
            bool female = a.Female;
            int style = Mathf.Clamp(a.hair, 0, 4);
            void Lock(float x0, float y0, float x1, float y1, float w) => Limb(c, new Vector2(x0, y0), new Vector2(x1, y1), w, w * 0.7f, hair, strands);
            if (female)
            {
                switch (style)
                {
                    case 0: // long
                        if (back) Body(c, cx, new (float, float)[] { (k.chestY - 6, k.headW - 1), (top - 4, k.headW + 1) }, hair, strands);
                        else { Lock(cx - k.headW, hy + 2, cx - k.headW + 0.5f, k.chestY - 4, 2.4f); if (!side) Lock(cx + k.headW, hy + 2, cx + k.headW - 0.5f, k.chestY - 4, 2.4f); else Lock(cx - k.headW + 1, hy, cx - k.headW - 1, k.chestY - 6, 3f); }
                        break;
                    case 1: // bob
                        Lock(cx - k.headW, hy + 2, cx - k.headW + 0.5f, k.chinY + 1, 2.2f);
                        if (!side) Lock(cx + k.headW, hy + 2, cx + k.headW - 0.5f, k.chinY + 1, 2.2f);
                        if (back) Body(c, cx, new (float, float)[] { (k.chinY, k.headW), (top - 4, k.headW + 1) }, hair, strands);
                        break;
                    case 2: // ponytail
                        if (back || side) Lock(cx - (side ? k.headW : 0), top - 4, cx - (side ? k.headW + 3 : 0), k.shoulderY - 6, 2.4f);
                        Ellipse(c, cx - (side ? k.headW - 1 : 0), top - 3, 2f, 2f, new Color(0.8f, 0.25f, 0.3f));   // tie
                        break;
                    case 3: // twin braids
                        foreach (float sx in side ? new[] { -1f } : new[] { -1f, 1f })
                            for (int i = 0; i < 5; i++) Ellipse(c, cx + sx * (k.headW - 0.5f), k.chinY + 1 - i * 3.2f, 2f, 1.9f, hair, strands);
                        break;
                    case 4: // pixie: just the short cap with a side sweep
                        if (!back) Lock(cx - 2, top - 2, cx + k.headW - 1, k.chinY + 9, 1.8f);
                        break;
                }
            }
            else
            {
                switch (style)
                {
                    case 0: // short
                        break;
                    case 1: // messy spikes
                        for (int i = 0; i < 5; i++) Lock(cx - 6 + i * 3, top - 2, cx - 7 + i * 3.4f, top + 2.5f, 1.4f);
                        break;
                    case 2: // long, tied back
                        if (back || side) Lock(cx - (side ? k.headW - 1 : 0), top - 5, cx - (side ? k.headW + 1 : 0), k.shoulderY - 4, 2f);
                        break;
                    case 3: // buzz: thin and close
                        if (!covered)
                            for (int x = 0; x < W; x++) for (int y = Mathf.RoundToInt(k.chinY + 9); y < H; y++)
                                if (c.px[x, y].a > 0 && IsHair(c.px[x, y], hair) && (x + y) % 2 == 0) c.px[x, y] = Mul(hair, 0.75f);
                        break;
                    case 4: // swept fringe
                        if (!back) Lock(cx - k.headW + 1, top - 2, cx + k.headW, k.chinY + 10, 2.2f);
                        break;
                }
            }
        }

        static bool IsHair(Color px, Color hair) => Mathf.Abs(px.r / Mathf.Max(0.01f, px.g) - hair.r / Mathf.Max(0.01f, hair.g)) < 0.25f &&
                                                     Mathf.Abs(px.b / Mathf.Max(0.01f, px.g) - hair.b / Mathf.Max(0.01f, hair.g)) < 0.25f;

        static void Helm(Canvas c, Skeleton k, DollSpec s, Pose p, bool female)
        {
            bool back = p.view == View.Back, side = p.view == View.Side, front = p.view == View.Front;
            float cx = k.cx + (side ? 1f : 0f);
            float top = k.chinY + k.headH;
            float hy = k.chinY + k.headH / 2f;
            var metal = Metal(s.helmRarity);
            var trim = Trim(s.helmRarity);
            switch (s.helm)
            {
                case HelmStyle.LeatherCap:
                {
                    var leather = new Color(0.45f, 0.3f, 0.18f);
                    Ellipse(c, cx, top - 3.5f, k.headW + 1.2f, 5f, leather);
                    Limb(c, new Vector2(cx - k.headW - 1, top - 6), new Vector2(cx + k.headW + 1, top - 6), 1f, 1f, Mul(leather, 0.75f));
                    break;
                }
                case HelmStyle.IronHelm:
                    Ellipse(c, cx, top - 4f, k.headW + 1.5f, 6f, metal, (x, y, sh) => Mathf.Abs(x + 0.5f - cx) < 0.6f ? Mul(sh, 1.3f) : sh);
                    Limb(c, new Vector2(cx - k.headW - 1.5f, top - 8), new Vector2(cx + k.headW + 1.5f, top - 8), 1.1f, 1.1f, Mul(metal, 0.75f));
                    if (front) Rect(c, Mathf.RoundToInt(cx - 1), Mathf.RoundToInt(k.chinY + 3), 2, 6, metal);    // nose guard
                    break;
                case HelmStyle.GreatHelm:
                    Ellipse(c, cx, hy + 0.5f, k.headW + 1.8f, k.headH / 2f + 1.5f, metal, (x, y, sh) => Mathf.Abs(x + 0.5f - cx) < 0.6f ? Mul(sh, 1.3f) : sh);
                    if (front) { Rect(c, Mathf.RoundToInt(cx - 5), Mathf.RoundToInt(k.chinY + 6), 10, 1, Ink); Rect(c, Mathf.RoundToInt(cx - 1), Mathf.RoundToInt(k.chinY + 2), 2, 4, Ink); }
                    if (side) Rect(c, Mathf.RoundToInt(cx + 1), Mathf.RoundToInt(k.chinY + 6), 7, 1, Ink);
                    // Plume.
                    var plume = s.helmRarity >= 2 ? trim : new Color(0.75f, 0.15f, 0.15f);
                    for (int i = 0; i < 8; i++) Ellipse(c, cx - (side ? i * 0.9f : 0), top + 1 + i * 0.9f - (side ? i * 0.3f : 0), 1.8f, 1.4f, plume);
                    break;
                case HelmStyle.Crown:
                {
                    var gold = s.helmRarity >= 3 ? new Color(0.95f, 0.75f, 0.3f) : Color.Lerp(new Color(0.75f, 0.7f, 0.62f), metal, 0.5f);
                    Limb(c, new Vector2(cx - k.headW, top - 4), new Vector2(cx + k.headW, top - 4), 1.4f, 1.4f, gold);
                    for (int i = -2; i <= 2; i++) Limb(c, new Vector2(cx + i * 3f, top - 3), new Vector2(cx + i * 3.4f, top + 1 + (i == 0 ? 2 : 0)), 0.9f, 0.4f, gold);
                    // Dark horns on the "dread" crown.
                    Limb(c, new Vector2(cx - k.headW + 1, top - 2), new Vector2(cx - k.headW - 3, top + 5), 1.4f, 0.4f, new Color(0.22f, 0.18f, 0.2f));
                    if (!side) Limb(c, new Vector2(cx + k.headW - 1, top - 2), new Vector2(cx + k.headW + 3, top + 5), 1.4f, 0.4f, new Color(0.22f, 0.18f, 0.2f));
                    if (front) Ellipse(c, cx, top - 4, 1.1f, 1.1f, new Color(0.85f, 0.15f, 0.25f));
                    break;
                }
                case HelmStyle.Hood:
                {
                    var hood = s.capeColor.a > 0 ? s.capeColor : new Color(0.3f, 0.4f, 0.25f);
                    // The hood frames the face, leaving it uncovered.
                    Ellipse(c, cx - (side ? 1 : 0), hy + 1, k.headW + 2.5f, k.headH / 2f + 2.5f, hood, null, (x, y) => !InFace(k, p, x, y, k.chinY + 11));
                    if (!back) Limb(c, new Vector2(cx - k.headW + 1, k.chinY + 11), new Vector2(cx + k.headW - 1, k.chinY + 11), 0.8f, 0.8f, Mul(hood, 0.7f));
                    Body(c, k.cx, new (float, float)[] { (k.shoulderY - 4, k.shoulderHalf + 1), (k.chinY + 2, k.headW + 1) }, hood);   // drape over shoulders
                    break;
                }
                case HelmStyle.WizardHat:
                {
                    var hat = Mul(s.cloth, 0.9f);
                    Limb(c, new Vector2(cx - k.headW - 5, top - 4), new Vector2(cx + k.headW + 5, top - 4), 1.6f, 1.6f, hat);   // brim
                    for (int i = 0; i < 16; i++)
                    {
                        float w = Mathf.Lerp(k.headW, 0.8f, i / 15f);
                        float ox = side ? -i * 0.35f : (i > 11 ? (i - 11) * 0.8f : 0);
                        Limb(c, new Vector2(cx - w + ox, top - 2 + i), new Vector2(cx + w + ox, top - 2 + i), 0.6f, 0.6f, i == 2 || i == 3 ? new Color(0.95f, 0.78f, 0.3f) : hat);
                    }
                    break;
                }
            }
        }

        static void Cape(Canvas c, Skeleton k, DollSpec s, Pose p, bool behind)
        {
            var col = behind ? Mul(s.capeColor, 0.72f) : s.capeColor;
            float sway = p.step * 1.5f;
            if (p.view == View.Side)
            {
                Body(c, k.cx - 4f, new (float, float)[] { (6f, 6f), (k.waistY, 4f), (k.shoulderY, 3f) }, col, (x, y, sh) => (x + y / 4) % 4 == 0 ? Mul(sh, 0.82f) : sh, -0.05f);
                return;
            }
            float edgeL = k.cx + sway - k.shoulderHalf - 5f;
            var lining = Color.Lerp(col, new Color(0.9f, 0.85f, 0.7f), 0.35f);
            Body(c, k.cx + sway, new (float, float)[] { (5f, k.shoulderHalf + 5f), (k.waistY, k.shoulderHalf + 2f), (k.shoulderY + 1, k.shoulderHalf) }, col,
                (x, y, sh) => (x % 5 == 0) ? Mul(sh, 0.75f) : (x % 5 == 1) ? Mul(sh, 1.12f) : sh);   // folds catch the light
            if (behind)
                for (int y = 5; y < k.waistY; y++)
                {
                    float half = Mathf.Lerp(k.shoulderHalf + 5f, k.shoulderHalf + 2f, (y - 5f) / (k.waistY - 5f));
                    c.Set(Mathf.RoundToInt(k.cx + sway - half), y, lining);
                    c.Set(Mathf.RoundToInt(k.cx + sway + half - 1), y, lining);
                }
            // Ragged hem.
            for (int x = Mathf.RoundToInt(k.cx - k.shoulderHalf - 5 + sway); x < k.cx + k.shoulderHalf + 5 + sway; x++)
                if ((x * 7) % 5 < 2) c.Set(x, 5, Color.clear);
        }

        // ---------- Finishing ----------

        // Only draw an inner line between pieces that actually look different.
        static bool Distinct(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 0.18f;

        // Selective outline: each edge takes a dark version of the color it borders.
        static Color[,] Outline(Canvas c)
        {
            var result = new Color[W, H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (c.Filled(x, y))
                {
                    var col = c.px[x, y];
                    col.a = 1f;
                    // Inner line: this pixel sits just beneath the edge of a piece drawn on top of it.
                    int me = c.part[x, y];
                    bool under = (x > 0 && c.part[x - 1, y] > me + 0 && Distinct(c.px[x - 1, y], col)) || (x < W - 1 && c.part[x + 1, y] > me && Distinct(c.px[x + 1, y], col)) ||
                                 (y > 0 && c.part[x, y - 1] > me && Distinct(c.px[x, y - 1], col)) || (y < H - 1 && c.part[x, y + 1] > me && Distinct(c.px[x, y + 1], col));
                    result[x, y] = under ? Color.Lerp(Mul(col, 0.45f), Ink, 0.35f) : col;
                    continue;
                }
                Color? n = null;
                if (c.Filled(x + 1, y)) n = c.px[x + 1, y];
                else if (c.Filled(x - 1, y)) n = c.px[x - 1, y];
                else if (c.Filled(x, y + 1)) n = c.px[x, y + 1];
                else if (c.Filled(x, y - 1)) n = c.px[x, y - 1];
                result[x, y] = n.HasValue ? Color.Lerp(Mul(n.Value, 0.3f), Ink, 0.55f) : Color.clear;
            }
            return result;
        }
    }
}
