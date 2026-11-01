using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Sculpted pixel figures. Every body part is a mask (built from anatomical contours and tapered
    // limbs), shaded from its own shape: a distance field gives each part a rounded surface lit from the
    // upper left, snapped to a five-tone ramp with cool shadows and warm highlights. Parts drawn on top of
    // others leave a dark contact line on what's beneath. Clothes and armor will be drawn the same way.
    public static class BodyRenderer
    {
        // Shapes are designed on a 64x96 grid and drawn at 1.5x (96x144 px, about 130 px tall),
        // close to the resolution of the reference art, so faces and gear get room for detail.
        public const float S = 1.5f;
        public const int W = 96, H = 144;
        public const float PPU = 72f;   // keeps the figure 2 world units tall
        static readonly Color Ink = new Color(0.09f, 0.06f, 0.08f);

        public enum Facing { Front, Back, Side }

        public struct Pose
        {
            public Facing facing;
            public int step;    // walk: -1, 0, 1
            public int bob;     // 0/1 body drop
            public int breath;  // idle: 0..3, chest and shoulders rise and fall
        }

        // ---------- Masks ----------

        public class Part
        {
            public readonly bool[,] mask = new bool[W, H];
            public Color color;
            public float roundness = 4f;   // how far in (px) the surface keeps curving
            public Func<int, int, Color, Color> pattern;   // surface detail painted into the albedo
            public Part(Color c) { color = c; }

            public void Fill(int x, int y) { if (x >= 0 && y >= 0 && x < W && y < H) mask[x, y] = true; }

            // A limb that tapers from a (radius ra) to b (radius rb).
            public Part Limb(Vector2 a, Vector2 b, float ra, float rb)
            {
                a *= S; b *= S; ra *= S; rb *= S;
                var d = b - a;
                float len = Mathf.Max(0.001f, d.magnitude);
                d /= len;
                for (int x = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - 8); x <= Mathf.CeilToInt(Mathf.Max(a.x, b.x) + 8); x++)
                for (int y = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - 8); y <= Mathf.CeilToInt(Mathf.Max(a.y, b.y) + 8); y++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / len);
                    float r = Mathf.Lerp(ra, rb, t);
                    if (Vector2.Distance(p, a + d * t * len) <= r) Fill(x, y);
                }
                return this;
            }

            // A limb with a bulge partway down (calves, forearms, thighs).
            public Part Muscle(Vector2 a, Vector2 b, float ra, float rMid, float rb, float midAt = 0.35f)
            {
                int n = 6;
                for (int i = 0; i < n; i++)
                {
                    float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                    Limb(Vector2.Lerp(a, b, t0), Vector2.Lerp(a, b, t1), R(t0), R(t1));
                }
                return this;
                float R(float t) => t < midAt ? Mathf.Lerp(ra, rMid, Mathf.SmoothStep(0, 1, t / midAt)) : Mathf.Lerp(rMid, rb, Mathf.SmoothStep(0, 1, (t - midAt) / (1 - midAt)));
            }

            public Part Ellipse(float cx, float cy, float rx, float ry)
            {
                cx *= S; cy *= S; rx *= S; ry *= S;
                for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
                for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Fill(x, y);
                }
                return this;
            }

            // A shape given its left and right edges at key heights (smoothly interpolated).
            public Part Contour(float cx, (float y, float left, float right)[] keys)
            {
                cx *= S;
                var scaled = new (float y, float left, float right)[keys.Length];
                for (int n = 0; n < keys.Length; n++) scaled[n] = (keys[n].y * S, keys[n].left * S, keys[n].right * S);
                keys = scaled;
                for (int k = 0; k < keys.Length - 1; k++)
                {
                    var lo = keys[k];
                    var hi = keys[k + 1];
                    for (int y = Mathf.RoundToInt(lo.y); y < Mathf.RoundToInt(hi.y); y++)
                    {
                        float t = Mathf.SmoothStep(0f, 1f, (y + 0.5f - lo.y) / Mathf.Max(0.001f, hi.y - lo.y));
                        float l = Mathf.Lerp(lo.left, hi.left, t), r = Mathf.Lerp(lo.right, hi.right, t);
                        for (int x = Mathf.RoundToInt(cx - l); x < Mathf.RoundToInt(cx + r); x++) Fill(x, y);
                    }
                }
                return this;
            }

            public Part Symmetric(float cx, (float y, float half)[] keys)
            {
                var k2 = new (float, float, float)[keys.Length];
                for (int i = 0; i < keys.Length; i++) k2[i] = (keys[i].y, keys[i].half, keys[i].half);
                return Contour(cx, k2);
            }
        }

        // ---------- Rendering ----------

        public class Result
        {
            public readonly Color[,] px = new Color[W, H];       // pre-lit (baked light) look
            public readonly Color[,] albedo = new Color[W, H];   // flat material color, for dynamic lighting
            public readonly Vector3[,] normal = new Vector3[W, H];
            public readonly int[,] part = new int[W, H];
        }

        // Albedo + normal map pair for dynamically lit sprites.
        public struct Maps
        {
            public Color[,] albedo, normal;
        }

        // Draws parts in order (later on top), shading each from its own shape.
        public static Result Render(List<Part> parts)
        {
            var res = new Result();
            for (int i = 0; i < parts.Count; i++) Shade(res, parts[i], i + 1);
            return res;
        }

        static readonly Vector3 Light = new Vector3(-0.72f, 0.42f, 0.55f).normalized;

        static void Shade(Result res, Part p, int id)
        {
            // Distance to the part's edge (chamfer), as a height field.
            var dist = new float[W, H];
            const float big = 999f;
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) dist[x, y] = p.mask[x, y] ? big : 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                int x0 = pass == 0 ? 0 : W - 1, x1 = pass == 0 ? W : -1, dx = pass == 0 ? 1 : -1;
                int y0 = pass == 0 ? 0 : H - 1, y1 = pass == 0 ? H : -1, dy = pass == 0 ? 1 : -1;
                for (int y = y0; y != y1; y += dy)
                for (int x = x0; x != x1; x += dx)
                {
                    if (!p.mask[x, y]) continue;
                    float best = dist[x, y];
                    best = Mathf.Min(best, D(x - dx, y) + 1f);
                    best = Mathf.Min(best, D(x, y - dy) + 1f);
                    best = Mathf.Min(best, D(x - dx, y - dy) + 1.414f);
                    best = Mathf.Min(best, D(x + dx, y - dy) + 1.414f);
                    dist[x, y] = best;
                }
            }
            float D(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? 0f : dist[x, y];
            float Hgt(int x, int y)
            {
                float d = D(x, y);
                float rr = p.roundness * S;
                float t = Mathf.Clamp01(d / rr);
                return Mathf.Sqrt(1f - (1f - t) * (1f - t)) * rr;   // a rounded (circular) profile
            }

            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                if (!p.mask[x, y]) continue;
                var n = new Vector3(-(Hgt(x + 1, y) - Hgt(x - 1, y)) * 0.5f, -(Hgt(x, y + 1) - Hgt(x, y - 1)) * 0.5f, 1f).normalized;
                float diffuse = Vector3.Dot(n, Light);
                float light = Mathf.Lerp(0.62f, 1.24f, Mathf.Pow(Mathf.InverseLerp(0.05f, 0.95f, diffuse), 1.1f));
                // Patterns are written on the design grid.
                var baseCol = p.pattern != null ? p.pattern(Mathf.FloorToInt(x / S), Mathf.FloorToInt(y / S), p.color) : p.color;
                res.px[x, y] = Tone(baseCol, light);
                res.albedo[x, y] = baseCol;
                res.normal[x, y] = n;
                res.part[x, y] = id;
            }
        }

        // Five tones: deep shadow (cool), shadow, base, light, highlight (warm).
        public static Color Tone(Color c, float light)
        {
            int band = light < 0.8f ? 0 : light < 0.93f ? 1 : light < 1.05f ? 2 : light < 1.15f ? 3 : 4;
            switch (band)
            {
                case 0: return Mul(Color.Lerp(c, new Color(0.3f, 0.16f, 0.42f), 0.34f), 0.6f);
                case 1: return Mul(Color.Lerp(c, new Color(0.46f, 0.26f, 0.5f), 0.16f), 0.8f);
                case 3: return Mul(Color.Lerp(c, new Color(1f, 0.9f, 0.72f), 0.1f), 1.08f);
                case 4: return Color.Lerp(Mul(c, 1.12f), new Color(1f, 0.97f, 0.88f), 0.3f);
                default: return Mul(c, 1f);
            }
        }

        public static Color Mul(Color c, float k) { var r = c * k; r.a = 1f; return r; }

        // Final pass: contact lines where a part sits on top of another, and a colored outline outside.
        public static Color[,] Finish(Result r)
        {
            var o = new Color[W, H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int me = r.part[x, y];
                if (me > 0)
                {
                    bool under = Above(r, x - 1, y, me) || Above(r, x + 1, y, me) || Above(r, x, y - 1, me) || Above(r, x, y + 1, me);
                    o[x, y] = under ? Color.Lerp(Mul(r.px[x, y], 0.5f), Ink, 0.3f) : r.px[x, y];
                    continue;
                }
                Color? n = null;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (nx >= 0 && ny >= 0 && nx < W && ny < H && r.part[nx, ny] > 0) { n = r.px[nx, ny]; break; }
                o[x, y] = n.HasValue ? Color.Lerp(Mul(n.Value, 0.32f), Ink, 0.55f) : Color.clear;
            }
            return o;
        }

        // Maps for dynamic lighting: flat colors with contact lines and outline, and a normal per pixel.
        public static Maps FinishMaps(Result r)
        {
            var m = new Maps { albedo = new Color[W, H], normal = new Color[W, H] };
            var flat = new Color(0.5f, 0.5f, 1f, 1f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int me = r.part[x, y];
                if (me > 0)
                {
                    bool under = Above(r, x - 1, y, me) || Above(r, x + 1, y, me) || Above(r, x, y - 1, me) || Above(r, x, y + 1, me);
                    var a = r.albedo[x, y];
                    m.albedo[x, y] = under ? Color.Lerp(Mul(a, 0.5f), Ink, 0.3f) : a;
                    var n = r.normal[x, y];
                    m.normal[x, y] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    continue;
                }
                Color? nb = null;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (nx >= 0 && ny >= 0 && nx < W && ny < H && r.part[nx, ny] > 0) { nb = r.albedo[nx, ny]; break; }
                m.albedo[x, y] = nb.HasValue ? Color.Lerp(Mul(nb.Value, 0.32f), Ink, 0.55f) : Color.clear;
                m.normal[x, y] = nb.HasValue ? flat : new Color(0.5f, 0.5f, 1f, 0f);
            }
            return m;
        }

        static bool Above(Result r, int x, int y, int me) => x >= 0 && y >= 0 && x < W && y < H && r.part[x, y] > me &&
                                                              (Mathf.Abs(r.px[x, y].r - r.px[x - 0, y].r) >= 0f);

        // ---------- The figure ----------

        // What the figure wears and holds (the creator's test options; items map onto these later).
        public class Gear
        {
            public ChestStyle chest = ChestStyle.Tunic;
            public bool trousers = true, skirt, cape;
            public HelmStyle helm = HelmStyle.None;
            public GloveStyle gloves = GloveStyle.None;
            public BootStyle boots = BootStyle.Shoes;
            // Weapon slots, each attached to a bone: main hand, off hand (weapon or shield), back, hip.
            public WeaponType? weapon;          // main hand
            public OffHand offHand = OffHand.None;
            public WeaponType? offWeapon;       // when offHand == Weapon
            public WeaponType? backWeapon;      // stowed across the back
            public WeaponType? hipWeapon;       // sheathed at the hip
            public Color cloth = new Color(0.32f, 0.44f, 0.58f), pants = new Color(0.36f, 0.28f, 0.22f), capeColor = new Color(0.55f, 0.16f, 0.16f);
            public Color metal = new Color(0.72f, 0.75f, 0.8f), trim = new Color(0.9f, 0.75f, 0.35f), leather = new Color(0.42f, 0.28f, 0.17f);

            public string Key => $"{chest}{trousers}{skirt}{cape}{helm}{gloves}{boots}{weapon}{offHand}{offWeapon}{backWeapon}{hipWeapon}{cloth}{pants}{capeColor}{metal}";
        }

        public enum OffHand { None, Weapon, RoundShield, KiteShield, Orb, Tome, Quiver }

        // Every landmark and joint for one pose; the body and all gear are built from it.
        class Rig
        {
            public bool female, side, back;
            public float cx, crotch, waist, chest, shoulder, neck, chin, knee;
            public float hw, hh, hcx, hcy;
            public Vector2[] hip = new Vector2[2], kn = new Vector2[2], an = new Vector2[2];   // [0] = viewer's left (or back leg in profile)
            public Vector2[] sh = new Vector2[2], el = new Vector2[2], wr = new Vector2[2];
            public (float y, float l, float r)[] torso;
            public float armR, legR;   // thickness scale
        }

        // A named bone: a segment with a thickness at each end. Body parts and equipment attach to bones by name.
        public struct Bone
        {
            public string name;
            public Vector2 a, b;
            public Bone(string n, Vector2 a, Vector2 b) { name = n; this.a = a; this.b = b; }
        }

        // The skeleton for a pose, as named bones (for the overlay and for attaching layers).
        public static List<Bone> Bones(bool female, Pose pose)
        {
            var k = MakeRig(female, pose);
            string L(int i) => k.side ? (i == 0 ? "_far" : "_near") : (i == 0 ? "_L" : "_R");
            var list = new List<Bone>
            {
                new Bone("hips", new Vector2(k.cx, k.crotch), new Vector2(k.cx, k.waist)),
                new Bone("chest", new Vector2(k.cx, k.waist), new Vector2(k.cx, k.shoulder)),
                new Bone("neck", new Vector2(k.cx, k.shoulder), new Vector2(k.cx, k.chin)),
                new Bone("head", new Vector2(k.hcx, k.chin), new Vector2(k.hcx, k.hcy + k.hh)),
                new Bone("hair", new Vector2(k.hcx, k.hcy), new Vector2(k.hcx, k.hcy + k.hh + 1)),
            };
            for (int i = 0; i < 2; i++)
            {
                list.Add(new Bone("shoulder" + L(i), new Vector2(k.cx, k.shoulder - 1), k.sh[i]));
                list.Add(new Bone("upperarm" + L(i), k.sh[i], k.el[i]));
                list.Add(new Bone("forearm" + L(i), k.el[i], k.wr[i]));
                list.Add(new Bone("hand" + L(i), k.wr[i], k.wr[i] + Vector2.down * 4f));
                list.Add(new Bone("thigh" + L(i), k.hip[i], k.kn[i]));
                list.Add(new Bone("shin" + L(i), k.kn[i], k.an[i]));
                list.Add(new Bone("foot" + L(i), k.an[i], k.an[i] + new Vector2(k.side ? 3f : 0f, -4f)));
                // Joints (a == b): where knee guards, elbow plates and the like attach.
                list.Add(new Bone("hipjoint" + L(i), k.hip[i], k.hip[i]));
                list.Add(new Bone("knee" + L(i), k.kn[i], k.kn[i]));
                list.Add(new Bone("ankle" + L(i), k.an[i], k.an[i]));
                list.Add(new Bone("shoulderjoint" + L(i), k.sh[i], k.sh[i]));
                list.Add(new Bone("elbow" + L(i), k.el[i], k.el[i]));
                list.Add(new Bone("wrist" + L(i), k.wr[i], k.wr[i]));
            }
            list.Add(new Bone("waist", new Vector2(k.cx, k.waist), new Vector2(k.cx, k.waist)));
            return list;
        }

        static Rig MakeRig(bool female, Pose pose)
        {
            var k = new Rig { female = female, side = pose.facing == Facing.Side, back = pose.facing == Facing.Back, cx = W / 2f };
            float b = -pose.bob, s = pose.step;
            // Breathing: the chest, shoulders, arms and head rise a pixel at the top of each breath.
            float br = pose.breath == 1 || pose.breath == 2 ? 1f : 0f;
            k.crotch = (female ? 45 : 46) + b; k.waist = (female ? 56 : 57) + b; k.chest = (female ? 63 : 64) + b + br;
            k.shoulder = (female ? 69 : 71) + b + br; k.neck = (female ? 71 : 73) + b + br; k.chin = (female ? 73 : 75) + b + br;
            k.knee = female ? 26 : 27;
            k.hw = female ? 5.9f : 6.2f; k.hh = 7f; k.hcx = k.cx + (k.side ? 1f : 0f); k.hcy = k.chin + k.hh;
            float cx = k.cx;
            bool standing = pose.step == 0 && pose.bob == 0 || pose.breath > 0;
            if (!k.side)
            {
                // Standing still, the weight sits on one leg (contrapposto).
                float tilt = standing ? 1f : 0f;
                for (int i = 0; i < 2; i++)
                {
                    int sg = i == 0 ? -1 : 1;
                    bool weight = i == 1;   // the character's left leg (viewer's right) carries the weight
                    float lift = (sg == -1 ? s == 1 : s == -1) ? 3f : 0f;
                    k.hip[i] = new Vector2(cx + sg * 4f + tilt * 0.6f, k.crotch + 2 + (weight ? tilt * 0.8f : -tilt * 0.4f));
                    k.kn[i] = new Vector2(cx + sg * 3.6f + (weight ? tilt * 0.8f : tilt * 1.6f), k.knee + lift * 0.6f + (weight ? 0 : -tilt * 0.6f));
                    k.an[i] = new Vector2(cx + sg * 3.4f + (weight ? tilt * 0.4f : tilt * 0.6f), 9 + lift);
                    float sx = female ? 7.2f : 10.2f;
                    float sw = sg * s * 1.5f + (pose.breath == 2 ? sg * 0.4f : 0f);
                    k.sh[i] = new Vector2(cx + sg * sx, k.shoulder - 2.5f);
                    k.el[i] = new Vector2(cx + sg * (sx + 1.6f), k.waist + 1 + sw + br * 0.5f);
                    k.wr[i] = new Vector2(cx + sg * (sx + 1.2f), k.crotch + 1 + sw * 1.4f + br * 0.5f);
                }
                k.torso = female
                    ? new[] { (k.crotch - 1, 8.4f, 8.4f), (k.crotch + 3, 8.6f, 8.6f), (k.waist - 3, 5.8f, 5.8f), (k.waist, 5f, 5f), (k.chest - 3, 6.2f, 6.2f), (k.chest, 7.2f, 7.2f), (k.shoulder - 2, 7.6f, 7.6f), (k.shoulder, 6.6f, 6.6f), (k.shoulder + 1.5f, 3.6f, 3.6f) }
                    : new[] { (k.crotch - 1, 8f, 8f), (k.crotch + 3, 8.1f, 8.1f), (k.waist, 7.2f, 7.2f), (k.chest - 4, 8.6f, 8.6f), (k.chest, 10f, 10f), (k.shoulder - 2, 10.8f, 10.8f), (k.shoulder, 9.2f, 9.2f), (k.shoulder + 1.5f, 4.4f, 4.4f) };
            }
            if (!k.side && standing)
            {
                var t = k.torso;
                for (int n = 0; n < t.Length; n++)
                {
                    float y = t[n].y;
                    float hipShift = y < k.waist ? 0.7f : 0f, shoulderShift = y > k.chest ? -0.4f : 0f;
                    float d = hipShift + shoulderShift;
                    t[n] = (y, t[n].l - d, t[n].r + d);
                }
            }
            else if (k.side)
            {
                // Profile facing right; index 0 = far (back) limb, 1 = near limb.
                for (int i = 0; i < 2; i++)
                {
                    float d = i == 0 ? -s : s;
                    k.hip[i] = new Vector2(cx, k.crotch + 2);
                    k.kn[i] = new Vector2(cx + 4f * d, k.knee);
                    k.an[i] = new Vector2(cx + 6.5f * d, 9 + (d > 0 ? 1 : 0));
                    float a = i == 0 ? s : -s;
                    k.sh[i] = new Vector2(cx + (i == 0 ? -1f : 0.5f), k.shoulder - 2);
                    k.el[i] = new Vector2(k.sh[i].x + a * 2.5f, k.waist + 1 + br * 0.5f);
                    k.wr[i] = new Vector2(k.sh[i].x + a * 4f, k.crotch + 1 + br * 0.5f);
                }
                k.torso = female
                    ? new[] { (k.crotch - 1, 5.6f, 3.8f), (k.crotch + 3, 5.8f, 4.2f), (k.waist, 3f, 3.2f), (k.chest - 3, 3.6f, 4.4f), (k.chest, 3.8f, 6f), (k.chest + 3, 4f, 4.2f), (k.shoulder, 3.8f, 3.6f), (k.shoulder + 1.5f, 2.2f, 2.2f) }
                    : new[] { (k.crotch - 1, 4.8f, 4f), (k.crotch + 3, 5f, 4.2f), (k.waist, 4f, 4.2f), (k.chest, 4.6f, 5.6f), (k.shoulder, 4.6f, 4.4f), (k.shoulder + 1.5f, 2.6f, 2.6f) };
            }
            return k;
        }

        static (float, float, float)[] Grow((float y, float l, float r)[] keys, float g, float fromY = -999f, float toY = 999f)
        {
            var list = new List<(float, float, float)>();
            foreach (var key in keys) if (key.y >= fromY - 0.01f && key.y <= toY + 0.01f) list.Add((key.y, key.l + g, key.r + g));
            return list.ToArray();
        }

        // Legs, arms, torso and head as single sculpted pieces.
        static Part Leg(Rig k, int i, Color c, float grow = 0f, float fromT = 0f, float toT = 1f, bool foot = true)
        {
            var p = new Part(c) { roundness = 3.5f };
            var hipKnee = (k.hip[i], k.kn[i]);
            if (fromT < 0.5f) p.Muscle(Vector2.Lerp(k.hip[i], k.kn[i], fromT * 2f), toT < 0.5f ? Vector2.Lerp(k.hip[i], k.kn[i], toT * 2f) : k.kn[i], (k.female ? 4.6f : 4.4f) + grow, (k.female ? 3.7f : 3.8f) + grow, 2.2f + grow, 0.25f);
            if (toT > 0.5f) p.Muscle(fromT > 0.5f ? Vector2.Lerp(k.kn[i], k.an[i], (fromT - 0.5f) * 2f) : k.kn[i], Vector2.Lerp(k.kn[i], k.an[i], (toT - 0.5f) * 2f), 2.2f + grow, (k.female ? 2.7f : 3f) + grow, 1.4f + grow, 0.28f);
            if (foot)
            {
                // A shaped foot: heel under the ankle, toes pointing forward (out in profile).
                var heel = new Vector2(k.an[i].x, k.an[i].y - 4.2f);
                var toe = heel + (k.side ? new Vector2(3.2f, -0.6f) : new Vector2(i == 0 ? -0.6f : 0.6f, -1.2f));
                p.Limb(heel, toe, 1.6f + grow * 0.6f, 1.3f + grow * 0.6f);
            }
            return p;
        }

        static Part Arm(Rig k, int i, Color c, float grow = 0f, float toT = 1f, bool hand = true)
        {
            var p = new Part(c) { roundness = 3f };
            float g = grow;
            p.Ellipse(k.sh[i].x, k.sh[i].y - 0.8f, (k.female ? 2.1f : 2.7f) + g, (k.female ? 2.2f : 2.6f) + g);
            p.Muscle(k.sh[i], toT < 0.5f ? Vector2.Lerp(k.sh[i], k.el[i], toT * 2f) : k.el[i], (k.female ? 2.1f : 2.7f) + g, (k.female ? 2.2f : 2.9f) + g, (k.female ? 1.6f : 2.1f) + g, 0.3f);
            if (toT > 0.5f) p.Muscle(k.el[i], Vector2.Lerp(k.el[i], k.wr[i], (toT - 0.5f) * 2f), (k.female ? 1.6f : 2.1f) + g, (k.female ? 1.9f : 2.5f) + g, 1.3f + g, 0.25f);
            if (hand) p.Ellipse(k.wr[i].x, k.wr[i].y - 2.3f, 1.7f + g, 2.5f + g);
            return p;
        }

        public static Color[,] Figure(Appearance look, Gear gear, Pose pose) => Finish(BuildFigure(look, gear, pose));
        public static Maps FigureMaps(Appearance look, Gear gear, Pose pose) => FinishMaps(BuildFigure(look, gear, pose));

        // The bare mannequin (kept for the art bake).
        public static Color[,] Mannequin(bool female, Color skin, Pose pose) => Figure(Bare(female, skin), null, pose);
        public static Maps MannequinMaps(bool female, Color skin, Pose pose) => FigureMaps(Bare(female, skin), null, pose);

        static Appearance Bare(bool female, Color skin)
        {
            int tone = Array.IndexOf(Appearance.SkinTones, skin);
            return new Appearance { body = female ? 1 : 0, skin = tone < 0 ? 1 : tone };
        }

        static Result BuildFigure(Appearance look, Gear g, Pose pose)
        {
            var parts = new List<Part>();
            bool female = look.Female;
            var skin = Appearance.SkinTones[Mathf.Clamp(look.skin, 0, Appearance.SkinTones.Length - 1)];
            var k = MakeRig(female, pose);
            Part Add(Part p) { parts.Add(p); return p; }
            var far = Mul(skin, 0.88f);
            // Behind everything: cape, the back weapon (seen from the front and side), and anything held
            // when we see the character from behind.
            if (g != null && g.cape && !k.back) Add(Cape(k, g, true));
            if (g != null && !k.back) { foreach (var p in BackSlot(k, g)) Add(p); foreach (var p in QuiverSlot(k, g)) Add(p); }
            if (g != null && k.back) { foreach (var p in MainHand(k, g)) Add(p); foreach (var p in OffHandSlot(k, g)) Add(p); }
            // Far limbs in profile (the off hand is the far one).
            if (k.side)
            {
                if (g != null) foreach (var p in OffHandSlot(k, g)) Add(p);
                Add(Arm(k, 0, far));
                if (g != null) foreach (var p in Sleeve(k, 0, g)) Add(p);
                Add(Leg(k, 0, far));
                if (g != null) foreach (var p in LegWear(k, 0, g, true)) Add(p);
            }
            // Legs and their clothing.
            for (int i = k.side ? 1 : 0; i < 2; i++)
            {
                Add(Leg(k, i, skin));
                if (g != null) foreach (var p in LegWear(k, i, g, false)) Add(p);
            }
            // Long hair hangs behind the shoulders, seen from the front and side.
            if (!k.back && !(g != null && (g.helm == HelmStyle.GreatHelm || g.helm == HelmStyle.Hood))) foreach (var p in HairBack(k, look)) Add(p);
            // Torso, chest bust in front, and what's worn on it.
            Add(new Part(skin) { roundness = 7f }.Contour(k.cx, k.torso));
            if (female && pose.facing == Facing.Front)
            {
                Add(new Part(skin) { roundness = 2.5f }.Ellipse(k.cx - 3.2f, k.chest - 0.8f, 3.1f, 2.7f));
                Add(new Part(skin) { roundness = 2.5f }.Ellipse(k.cx + 3.2f, k.chest - 0.8f, 3.1f, 2.7f));
            }
            if (g != null) foreach (var p in Chest(k, g, pose)) Add(p);
            // Arms over the torso.
            for (int i = k.side ? 1 : 0; i < 2; i++)
            {
                Add(Arm(k, i, skin));
                if (g != null) foreach (var p in Sleeve(k, i, g)) Add(p);
            }
            // Hair behind the head: from behind it lies over the back; otherwise it was drawn before the torso.
            bool hairShown = !(g != null && (g.helm == HelmStyle.GreatHelm || g.helm == HelmStyle.Hood));
            if (hairShown && k.back) foreach (var p in HairBack(k, look)) Add(p);
            // Neck and head.
            Add(new Part(skin) { roundness = 2f }.Limb(new Vector2(k.cx, k.neck - 2), new Vector2(k.cx + (k.side ? 0.5f : 0f), k.chin + 2), female ? 1.8f : 2.4f, female ? 1.7f : 2.2f));
            var head = Add(new Part(skin) { roundness = 4f });
            head.Ellipse(k.hcx, k.hcy + 0.5f, k.hw, k.hh - 0.5f);
            head.Contour(k.hcx, new (float, float, float)[] { (k.chin, female ? 1.5f : 2.2f, female ? 1.5f : 2.2f), (k.chin + 3, k.hw - 1f, k.hw - 1f), (k.hcy, k.hw, k.hw) });
            if (k.side) head.Ellipse(k.hcx + k.hw - 0.3f, k.chin + 4f, 1.1f, 1.2f);
            if (!k.side) { head.Ellipse(k.hcx - k.hw, k.chin + 5f, 1f, 1.6f); head.Ellipse(k.hcx + k.hw, k.chin + 5f, 1f, 1.6f); }
            else head.Ellipse(k.hcx - 1f, k.chin + 5f, 1.2f, 1.7f);
            if (!(g != null && (g.helm == HelmStyle.GreatHelm || g.helm == HelmStyle.Hood))) foreach (var p in HairFront(k, look)) Add(p);
            if (g != null && g.helm != HelmStyle.None) foreach (var p in Helm(k, g)) Add(p);
            if (g != null && g.cape && k.back) Add(Cape(k, g, false));
            if (g != null && k.back) { foreach (var p in BackSlot(k, g)) Add(p); foreach (var p in QuiverSlot(k, g)) Add(p); }
            if (g != null) foreach (var p in HipSlot(k, g)) Add(p);
            if (g != null && !k.back)
            {
                foreach (var p in MainHand(k, g)) Add(p);
                if (!k.side) foreach (var p in OffHandSlot(k, g)) Add(p);
            }

            var res = Render(parts);
            if (g == null || (g.helm != HelmStyle.GreatHelm))
                Face(res, female, skin, pose, k.hcx, k.chin, k.hw, Appearance.EyeColors[Mathf.Clamp(look.eyes, 0, Appearance.EyeColors.Length - 1)], HairColor(look));
            return res;
        }

        // ---------- Hair (on the hair bone) ----------

        static Color HairColor(Appearance a) => Appearance.HairColors[Mathf.Clamp(a.hairColor, 0, Appearance.HairColors.Length - 1)];

        // Shine band and strands painted into hair.
        static Func<int, int, Color, Color> Strands(Rig k)
        {
            int shine = Mathf.RoundToInt(k.hcy + k.hh - 3);
            return (x, y, c) => (y == shine || y == shine + 1) && x % 3 != 0 ? Color.Lerp(c, new Color(1f, 0.97f, 0.9f), 0.4f) : (x % 3 == 0 ? Mul(c, 0.85f) : c);
        }

        static IEnumerable<Part> HairBack(Rig k, Appearance a)
        {
            var c = HairColor(a);
            int style = Mathf.Clamp(a.hair, 0, 4);
            float top = k.hcy + k.hh;
            if (a.Female)
            {
                if (style == 0)   // long, down the back
                {
                    var p = new Part(Mul(c, 0.9f)) { roundness = 4f, pattern = Strands(k) };
                    if (k.side) p.Contour(k.hcx - 3f, new (float, float, float)[] { (k.chest - 6, 3.4f, 1.6f), (k.chin, k.hw - 0.5f, 2.5f), (k.hcy, k.hw, 2.5f) });
                    else p.Contour(k.hcx, new (float, float, float)[] { (k.chest - 6, k.hw - 1f, k.hw - 1f), (k.chest, k.hw + 1.2f, k.hw + 1.2f), (k.chin + 2, k.hw + 2.2f, k.hw + 2.2f), (k.hcy, k.hw + 1.6f, k.hw + 1.6f) });
                    var wavy = p.pattern;
                    p.pattern = (x, y, col) => (x + y / 2) % 4 == 0 ? Mul(col, 0.82f) : wavy(x, y, col);   // flowing strands
                    yield return p;
                }
                if (style == 2 && !(!k.side && !k.back))   // ponytail
                    yield return new Part(c) { roundness = 2.5f, pattern = Strands(k) }.Muscle(new Vector2(k.hcx - (k.side ? k.hw - 1 : 0), top - 3), new Vector2(k.hcx - (k.side ? k.hw + 3 : 0), k.shoulder - 8), 2f, 2.6f, 1.2f);
                if (style == 3)   // twin braids
                    foreach (float sx in k.side ? new[] { -1f } : new[] { -1f, 1f })
                    {
                        var b = new Part(c) { roundness = 2f, pattern = Strands(k) };
                        for (int i = 0; i < 5; i++) b.Ellipse(k.hcx + sx * (k.hw - 0.5f), k.chin - i * 3f, 1.6f, 1.6f);
                        yield return b;
                    }
            }
            else if (style == 2 && !(!k.side && !k.back))   // long, tied back
                yield return new Part(c) { roundness = 2f, pattern = Strands(k) }.Muscle(new Vector2(k.hcx - (k.side ? k.hw - 1 : 0), top - 4), new Vector2(k.hcx - (k.side ? k.hw + 1 : 0), k.shoulder - 3), 1.6f, 1.8f, 1f);
        }

        static IEnumerable<Part> HairFront(Rig k, Appearance a)
        {
            var c = HairColor(a);
            int style = Mathf.Clamp(a.hair, 0, 4);
            float top = k.hcy + k.hh;
            // The cap of hair over the skull, stopping at a jagged fringe above the eyes.
            var cap = new Part(c) { roundness = 4f, pattern = Strands(k) };
            float lift = !a.Female && style == 3 ? 1.5f : 0f;   // buzz cut sits close
            cap.Ellipse(k.hcx - (k.side ? 0.7f : 0f), k.hcy + 1.8f + lift, k.hw + 1.3f - lift * 0.6f, k.hh + 0.2f - lift);
            float fringe = k.chin + 8.5f;
            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                if (!cap.mask[x, y] || k.back) continue;
                float dx = (x + 0.5f) / S, dy = (y + 0.5f) / S;
                bool face = k.side ? dx >= k.hcx - 1.5f : Mathf.Abs(dx - k.hcx) < k.hw - 1.2f;
                if (face && dy < fringe - ((Mathf.FloorToInt(dx) % 3 == 1) ? 0.7f : 0f)) cap.mask[x, y] = false;
            }
            yield return cap;
            if (a.Female)
            {
                if ((style == 0 || style == 1) && !k.back)   // locks framing the face
                    foreach (float sx in k.side ? new[] { -1f } : new[] { -1f, 1f })
                        yield return new Part(c) { roundness = 1.5f, pattern = Strands(k) }.Muscle(new Vector2(k.hcx + sx * (k.hw + 0.2f), k.hcy + 1), new Vector2(k.hcx + sx * (k.hw + 0.4f), style == 0 ? k.shoulder - 4 : k.chin + 1), 1.2f, 1.3f, 0.8f);
                if (style == 4 && !k.back)   // pixie sweep
                    yield return new Part(c) { roundness = 2f, pattern = Strands(k) }.Limb(new Vector2(k.hcx - 2, top - 1.5f), new Vector2(k.hcx + k.hw - 0.5f, k.chin + 8), 1.4f, 1f);
            }
            else
            {
                if (style == 0)   // short: a few tufts on top and a fringe swept to one side
                {
                    for (int i = 0; i < 3; i++)
                        yield return new Part(c) { roundness = 1.5f, pattern = Strands(k) }.Limb(new Vector2(k.hcx - 3 + i * 2.6f, top - 1f), new Vector2(k.hcx - 2.4f + i * 2.8f, top + 1.2f), 1.3f, 0.5f);
                    if (!k.back) yield return new Part(c) { roundness = 1.5f, pattern = Strands(k) }.Limb(new Vector2(k.hcx - k.hw + 1.5f, top - 2f), new Vector2(k.hcx + 1.5f, k.chin + 9.5f), 1.6f, 0.9f);
                }
                if (style == 1)   // messy spikes
                    for (int i = 0; i < 4; i++)
                        yield return new Part(c) { roundness = 1.5f, pattern = Strands(k) }.Limb(new Vector2(k.hcx - 4 + i * 2.6f, top - 1.5f), new Vector2(k.hcx - 5 + i * 3f, top + 2f), 1.2f, 0.4f);
                if (style == 4 && !k.back)   // swept fringe
                    yield return new Part(c) { roundness = 2f, pattern = Strands(k) }.Limb(new Vector2(k.hcx - k.hw + 1, top - 1.5f), new Vector2(k.hcx + k.hw - 0.5f, k.chin + 8.5f), 1.8f, 1.1f);
            }
        }

        // ---------- Clothing and armor ----------

        static IEnumerable<Part> LegWear(Rig k, int i, Gear g, bool farSide)
        {
            float dim = farSide ? 0.85f : 1f;
            if (g.trousers && g.chest != ChestStyle.Robe)
                yield return Pattern(Leg(k, i, Mul(g.pants, dim), 0.35f, 0f, 0.92f, false),
                    (x, y, c) => Mathf.Abs(y - k.kn[i].y) < 1.5f && (x + y) % 3 == 0 ? Mul(c, 0.86f) : c);   // creases at the knee
            switch (g.boots)
            {
                case BootStyle.Shoes:
                    yield return Leg(k, i, Mul(g.leather * 0.8f, dim), 0.7f, 0.98f, 1f, true);
                    break;
                case BootStyle.Sandals:
                    yield return Leg(k, i, Mul(g.leather, dim), 0.3f, 0.99f, 1f, true);
                    break;
                case BootStyle.Leather:
                    yield return Pattern(Leg(k, i, Mul(g.leather, dim), 1f, 0.78f, 1f, true), (x, y, c) => y == Mathf.RoundToInt(Mathf.Lerp(k.kn[i].y, k.an[i].y, 0.56f)) ? Mul(c, 1.3f) : c);
                    break;
                case BootStyle.Greaves:
                    yield return Leg(k, i, Mul(g.metal, dim), 1.2f, 0.5f, 1f, true);
                    yield return new Part(Mul(g.metal, dim * 1.05f)) { roundness = 2.5f }.Ellipse(k.kn[i].x, k.kn[i].y, 3.4f, 3f);   // knee cop
                    break;
                case BootStyle.Striders:
                    yield return Leg(k, i, Mul(Color.Lerp(g.leather, g.cloth, 0.3f), dim), 0.9f, k.female ? 0.35f : 0.62f, 1f, true);
                    break;
            }
        }

        static IEnumerable<Part> Sleeve(Rig k, int i, Gear g)
        {
            float dim = k.side && i == 0 ? 0.85f : 1f;
            switch (g.chest)
            {
                case ChestStyle.Tunic: case ChestStyle.Apron: yield return Arm(k, i, Mul(g.cloth, dim), 0.25f, 0.34f, false); break;
                case ChestStyle.PaddedVest: yield return Arm(k, i, Mul(g.cloth, dim * 0.9f), 0.6f, 0.9f, false); break;
                case ChestStyle.Chainmail: yield return Pattern(Arm(k, i, Mul(g.metal * 0.85f, dim), 0.7f, 0.6f, false), Chain); break;
                case ChestStyle.Plate: case ChestStyle.Aegis:
                    yield return Arm(k, i, Mul(g.cloth, dim * 0.8f), 0.6f, 0.9f, false);
                    yield return new Part(Mul(g.metal, dim)) { roundness = 3f }.Ellipse(k.sh[i].x + (k.side ? 0 : (i == 0 ? -0.8f : 0.8f)), k.sh[i].y - 0.5f, k.female ? 3.6f : 4.4f, k.female ? 3.2f : 3.8f);   // pauldron
                    break;
                case ChestStyle.Robe: yield return Arm(k, i, Mul(g.cloth, dim), 1f, 1f, false); break;
            }
            switch (g.gloves)
            {
                case GloveStyle.Wraps: yield return Pattern(ArmFrom(k, i, Color.Lerp(g.cloth, Color.white, 0.5f), 0.4f, 0.75f), (x, y, c) => y % 2 == 0 ? Mul(c, 0.85f) : c); break;
                case GloveStyle.Leather: yield return ArmFrom(k, i, Mul(g.leather, dim), 0.45f, 0.8f); break;
                case GloveStyle.Gauntlets: case GloveStyle.Talons: yield return ArmFrom(k, i, Mul(g.metal, dim), 0.8f, 0.6f); break;
            }
        }

        // Forearm from partway down to the hand (gloves).
        static Part ArmFrom(Rig k, int i, Color c, float grow, float fromT)
        {
            var p = new Part(c) { roundness = 2.5f };
            var start = Vector2.Lerp(k.el[i], k.wr[i], (fromT - 0.5f) * 2f);
            p.Limb(start, k.wr[i], (k.female ? 1.7f : 2.2f) + grow, 1.4f + grow);
            p.Ellipse(k.wr[i].x, k.wr[i].y - 2.3f, 1.8f + grow, 2.6f + grow);
            return p;
        }

        static IEnumerable<Part> Chest(Rig k, Gear g, Pose pose)
        {
            bool front = pose.facing == Facing.Front;
            switch (g.chest)
            {
                case ChestStyle.Tunic:
                case ChestStyle.Apron:
                    yield return Pattern(new Part(g.cloth) { roundness = 6f }.Contour(k.cx, Grow(k.torso, 0.6f)), Folds(k));
                    yield return new Part(g.leather * 0.8f) { roundness = 2f }.Contour(k.cx, new (float, float, float)[] { (k.waist - 4, Width(k, k.waist - 4) + 0.8f, Width(k, k.waist - 4, true) + 0.8f), (k.waist - 2, Width(k, k.waist - 2) + 0.8f, Width(k, k.waist - 2, true) + 0.8f) });   // belt
                    if (g.chest == ChestStyle.Apron && !k.back)
                        yield return new Part(new Color(0.88f, 0.8f, 0.62f)) { roundness = 3f }.Contour(k.cx, new (float, float, float)[] { (k.crotch - 10, 7f, 7f), (k.waist, 5f, 5f), (k.chest + 1, 4f, 4f) });
                    break;
                case ChestStyle.PaddedVest:
                    yield return Pattern(new Part(Color.Lerp(g.leather, g.cloth, 0.3f)) { roundness = 6f }.Contour(k.cx, Hem(k, Grow(k.torso, 1f), 5f)), (x, y, c) => (y % 3 == 0 || (x + y) % 6 == 0) ? Mul(c, 0.85f) : c);
                    break;
                case ChestStyle.Chainmail:
                    yield return Pattern(new Part(g.metal * 0.85f) { roundness = 6f }.Contour(k.cx, Hem(k, Grow(k.torso, 1f), 9f)), Chain);
                    if (!k.side) yield return new Part(g.capeColor) { roundness = 4f }.Contour(k.cx, new (float, float, float)[] { (k.crotch - 8, 3.5f, 3.5f), (k.shoulder - 2, 3.5f, 3.5f) });   // surcoat strip
                    break;
                case ChestStyle.Plate:
                case ChestStyle.Aegis:
                    yield return new Part(g.cloth * 0.8f) { roundness = 5f }.Contour(k.cx, Hem(k, Grow(k.torso, 0.6f), 6f));   // padding under the plate
                    var plate = new Part(g.metal) { roundness = 8f };
                    plate.Contour(k.cx, Grow(k.torso, 1.3f, k.waist - 3));
                    yield return Pattern(plate, (x, y, c) => front && Mathf.Abs(x + 0.5f - k.cx) < 0.6f ? Mul(c, 1.2f) : c);   // center ridge
                    yield return new Part(g.trim) { roundness = 2f }.Contour(k.cx, new (float, float, float)[] { (k.waist - 4, Width(k, k.waist - 4) + 1.6f, Width(k, k.waist - 4, true) + 1.6f), (k.waist - 2.5f, Width(k, k.waist - 2.5f) + 1.6f, Width(k, k.waist - 2.5f, true) + 1.6f) });
                    // Tassets over the hips.
                    yield return Pattern(new Part(g.metal * 0.95f) { roundness = 4f }.Contour(k.cx, new (float, float, float)[] { (k.crotch - 7, Width(k, k.crotch) + 2.5f, Width(k, k.crotch, true) + 2.5f), (k.waist - 3, Width(k, k.waist - 3) + 1.5f, Width(k, k.waist - 3, true) + 1.5f) }),
                        (x, y, c) => y % 3 == 0 ? Mul(c, 0.8f) : c);
                    if (g.chest == ChestStyle.Aegis && !k.side)
                        yield return Pattern(new Part(g.capeColor) { roundness = 3f }.Contour(k.cx, new (float, float, float)[] { (k.crotch - 18, 4f, 4f), (k.waist - 2, 3.6f, 3.6f), (k.chest + 1, 4f, 4f) }),
                            (x, y, c) => Mathf.Abs(x + 0.5f - k.cx) > 3f ? g.trim : c);   // tabard with trim
                    break;
                case ChestStyle.Robe:
                    yield return Pattern(new Part(g.cloth) { roundness = 7f }.Contour(k.cx, Robe(k)), (x, y, c) => Mathf.Abs(x + 0.5f - k.cx) < 0.6f ? g.trim : (x % 5 == 0 && y < k.crotch ? Mul(c, 0.85f) : c));
                    break;
            }
            if (g.skirt && g.chest != ChestStyle.Robe)
                yield return Pattern(new Part(Color.Lerp(g.cloth, g.capeColor, 0.5f)) { roundness = 5f }.Contour(k.cx, new (float, float, float)[] { (k.knee + 6, Width(k, k.crotch) + 6f, Width(k, k.crotch, true) + 6f), (k.crotch, Width(k, k.crotch) + 3f, Width(k, k.crotch, true) + 3f), (k.waist - 2, Width(k, k.waist - 2) + 1.2f, Width(k, k.waist - 2, true) + 1.2f) }),
                    (x, y, c) => x % 4 == 0 ? Mul(c, 0.82f) : (y == Mathf.RoundToInt(k.knee + 6) ? g.trim : c));   // pleats, trimmed hem
        }

        // Torso half width (left side, or right with right=true) at a height.
        static float Width(Rig k, float y, bool right = false)
        {
            var t = k.torso;
            if (y <= t[0].y) return right ? t[0].r : t[0].l;
            for (int i = 0; i < t.Length - 1; i++)
                if (y < t[i + 1].y)
                {
                    float u = Mathf.SmoothStep(0, 1, (y - t[i].y) / (t[i + 1].y - t[i].y));
                    return right ? Mathf.Lerp(t[i].r, t[i + 1].r, u) : Mathf.Lerp(t[i].l, t[i + 1].l, u);
                }
            return right ? t[t.Length - 1].r : t[t.Length - 1].l;
        }

        // Extends a torso shape down into a hem below the crotch.
        static (float, float, float)[] Hem(Rig k, (float, float, float)[] keys, float length)
        {
            var list = new List<(float, float, float)>(keys);
            var first = keys[0];
            list.Insert(0, (first.Item1 - length, first.Item2 + 1.2f, first.Item3 + 1.2f));
            return list.ToArray();
        }

        static (float, float, float)[] Robe(Rig k)
        {
            var list = new List<(float, float, float)> { (3f, k.side ? 7f : 11f, k.side ? 7f : 11f), (k.knee, k.side ? 6.5f : 10f, k.side ? 6.5f : 10f) };
            foreach (var key in Grow(k.torso, 1f)) list.Add(key);
            return list.ToArray();
        }

        static IEnumerable<Part> Helm(Rig k, Gear g)
        {
            float top = k.hcy + k.hh;
            switch (g.helm)
            {
                case HelmStyle.LeatherCap:
                    yield return new Part(g.leather) { roundness = 3f }.Ellipse(k.hcx, top - 3f, k.hw + 0.8f, 3.6f);
                    break;
                case HelmStyle.IronHelm:
                    yield return Pattern(new Part(g.metal) { roundness = 4f }.Ellipse(k.hcx, top - 3.5f, k.hw + 1.2f, 4.6f), (x, y, c) => y == Mathf.RoundToInt(top - 7) ? Mul(c, 0.75f) : c);
                    if (!k.side && !k.back) yield return new Part(g.metal) { roundness = 1f }.Limb(new Vector2(k.hcx, top - 6), new Vector2(k.hcx, k.chin + 3), 0.7f, 0.6f);   // nasal
                    break;
                case HelmStyle.GreatHelm:
                    yield return Pattern(new Part(g.metal) { roundness = 5f }.Ellipse(k.hcx, k.hcy + 0.5f, k.hw + 1.4f, k.hh + 1f),
                        (x, y, c) => !k.back && y == Mathf.RoundToInt(k.chin + 4) && (k.side ? x > k.hcx : Mathf.Abs(x + 0.5f - k.hcx) < k.hw - 1) ? new Color(0.1f, 0.08f, 0.1f) : c);   // visor slit
                    yield return new Part(g.capeColor) { roundness = 2f }.Muscle(new Vector2(k.hcx, top), new Vector2(k.hcx - (k.side ? 4f : 0f), top + 4f), 1.4f, 1.8f, 0.8f);   // plume
                    break;
                case HelmStyle.Hood:
                    var hood = new Part(g.capeColor) { roundness = 4f };
                    hood.Ellipse(k.hcx - (k.side ? 1f : 0f), k.hcy + 1f, k.hw + 2f, k.hh + 2f);
                    // Leave the face open.
                    for (int x = 0; x < W; x++) for (int y = 0; y < H; y++)
                        if (hood.mask[x, y] && !k.back && (y + 0.5f) / S < k.chin + 8 && (y + 0.5f) / S > k.chin - 1 && (k.side ? (x + 0.5f) / S > k.hcx - 1 : Mathf.Abs((x + 0.5f) / S - k.hcx) < k.hw - 1.5f)) hood.mask[x, y] = false;
                    yield return hood;
                    break;
                case HelmStyle.Crown:
                    yield return new Part(g.trim) { roundness = 1.5f }.Limb(new Vector2(k.hcx - k.hw, top - 2.5f), new Vector2(k.hcx + k.hw, top - 2.5f), 1f, 1f);
                    break;
                case HelmStyle.WizardHat:
                    var hat = new Part(g.cloth) { roundness = 3f };
                    hat.Limb(new Vector2(k.hcx - k.hw - 4, top - 3), new Vector2(k.hcx + k.hw + 4, top - 3), 1.2f, 1.2f);
                    hat.Limb(new Vector2(k.hcx, top - 2), new Vector2(k.hcx - (k.side ? 4 : 0) + 1, top + 10), k.hw - 0.5f, 0.6f);
                    yield return hat;
                    break;
            }
        }

        static Part Cape(Rig k, Gear g, bool behind)
        {
            var c = new Part(behind ? Mul(g.capeColor, 0.75f) : g.capeColor) { roundness = 6f };
            if (k.side) c.Contour(k.cx - 4f, new (float, float, float)[] { (6f, 3f, 4f), (k.waist, 2.5f, 2.5f), (k.shoulder, 2f, 2f) });
            else c.Contour(k.cx, new (float, float, float)[] { (5f, Width(k, k.shoulder) + 5f, Width(k, k.shoulder, true) + 5f), (k.waist, Width(k, k.shoulder) + 2f, Width(k, k.shoulder, true) + 2f), (k.shoulder + 1, Width(k, k.shoulder), Width(k, k.shoulder, true)) });
            c.pattern = (x, y, col) => x % 5 == 0 ? Mul(col, 0.82f) : x % 5 == 1 ? Mul(col, 1.08f) : col;   // folds
            return c;
        }

        // ---------- Weapon slots ----------

        static readonly Color Wood = new Color(0.45f, 0.3f, 0.18f), Steel = new Color(0.82f, 0.85f, 0.9f), Grip = new Color(0.3f, 0.2f, 0.14f);

        // Main hand: the near hand (in profile) or the character's right hand (viewer's left) from the front.
        static IEnumerable<Part> MainHand(Rig k, Gear g)
        {
            if (!g.weapon.HasValue || g.weapon.Value == WeaponType.Fists) yield break;
            int hand = k.side ? 1 : 0;
            Vector2 up = k.side ? new Vector2(0.35f, 1f).normalized : new Vector2(-0.25f, 1f).normalized;
            foreach (var p in WeaponParts(g.weapon.Value, k.wr[hand] + Vector2.down * 2f, up, g, 1f)) yield return p;
        }

        // Off hand: a second weapon or a shield on the forearm.
        static IEnumerable<Part> OffHandSlot(Rig k, Gear g)
        {
            if (g.offHand == OffHand.None) yield break;
            int hand = k.side ? 0 : 1;
            float dim = k.side || k.back ? 0.82f : 1f;
            if (g.offHand == OffHand.Quiver) yield break;   // worn on the back, see QuiverSlot
            if (g.offHand == OffHand.Orb)
            {
                // A crystal orb floating just above the open hand.
                var o = k.wr[hand] + new Vector2(k.side ? 1.5f : 0.5f, 1.5f);
                yield return new Part(Mul(new Color(0.55f, 0.8f, 1f), dim)) { roundness = 2.5f, pattern = (x, y, c) => (x + y) % 5 == 0 ? Color.Lerp(c, Color.white, 0.4f) : c }.Ellipse(o.x, o.y, 2.6f, 2.6f);
                yield break;
            }
            if (g.offHand == OffHand.Tome)
            {
                var t = k.wr[hand] + new Vector2(k.side ? 1.5f : 1f, -1.5f);
                var cover = Mul(Color.Lerp(g.capeColor, g.leather, 0.4f), dim);
                yield return new Part(cover) { roundness = 2f, pattern = (x, y, c) => Mathf.Abs(x + 0.5f - t.x) < 0.6f ? Mul(g.trim, dim) : c }.Contour(t.x, new (float, float, float)[] { (t.y - 3.5f, 2.6f, 2.6f), (t.y + 3.5f, 2.6f, 2.6f) });
                yield break;
            }
            if (g.offHand == OffHand.Weapon)
            {
                if (!g.offWeapon.HasValue || g.offWeapon.Value == WeaponType.Fists) yield break;
                Vector2 up = k.side ? new Vector2(0.3f, 1f).normalized : new Vector2(0.25f, 1f).normalized;
                foreach (var p in WeaponParts(g.offWeapon.Value, k.wr[hand] + Vector2.down * 2f, up, g, dim)) yield return p;
                yield break;
            }
            // Shield strapped to the forearm, its face toward the viewer.
            var mid = Vector2.Lerp(k.el[hand], k.wr[hand], 0.55f) + (k.side ? new Vector2(2f, 0f) : new Vector2(1.5f, 0f));
            var face = new Part(Mul(Color.Lerp(g.metal, g.capeColor, 0.55f), dim)) { roundness = 5f };
            if (g.offHand == OffHand.RoundShield) face.Ellipse(mid.x, mid.y, k.side ? 3.5f : 6f, 6f);
            else face.Contour(mid.x, new (float, float, float)[] { (mid.y - 9, 0.5f, 0.5f), (mid.y - 3, (k.side ? 2.6f : 4.6f), (k.side ? 2.6f : 4.6f)), (mid.y + 5, (k.side ? 3f : 5.2f), (k.side ? 3f : 5.2f)), (mid.y + 6, (k.side ? 2.6f : 4.8f), (k.side ? 2.6f : 4.8f)) });
            face.pattern = (x, y, c) => Mathf.Abs(x + 0.5f - mid.x) < 0.6f || Mathf.Abs(y + 0.5f - mid.y) < 0.6f ? Mul(c, 1.15f) : c;   // painted cross
            yield return face;
            if (!k.back)
            {
                yield return new Part(Mul(g.trim, dim)) { roundness = 1.5f }.Ellipse(mid.x, mid.y, 1.6f, 1.6f);   // boss
                // Rim.
                var rim = new Part(Mul(g.metal, dim)) { roundness = 1f };
                if (g.offHand == OffHand.RoundShield)
                    for (int a = 0; a < 24; a++)
                    {
                        float t0 = a / 24f * Mathf.PI * 2, t1 = (a + 1) / 24f * Mathf.PI * 2;
                        float rx = k.side ? 3.5f : 6f;
                        rim.Limb(mid + new Vector2(Mathf.Cos(t0) * rx, Mathf.Sin(t0) * 6f), mid + new Vector2(Mathf.Cos(t1) * rx, Mathf.Sin(t1) * 6f), 0.55f, 0.55f);
                    }
                else rim.Limb(mid + new Vector2(0, 6), mid + new Vector2(0, -9), 0.5f, 0.5f);
                yield return rim;
            }
        }

        // A quiver of arrows on the back (off-hand item for archers).
        static IEnumerable<Part> QuiverSlot(Rig k, Gear g)
        {
            if (g.offHand != OffHand.Quiver) yield break;
            var bottom = new Vector2(k.cx + (k.side ? -4f : 4f), k.waist - 2f);
            var up = k.side ? new Vector2(-0.25f, 1f).normalized : new Vector2(-0.3f, 1f).normalized;
            float dim = k.back ? 1f : 0.82f;
            for (int i = -1; i <= 1; i++)
                yield return new Part(Mul(new Color(0.92f, 0.9f, 0.85f), dim)) { roundness = 0.5f }.Limb(bottom + up * 9f + new Vector2(i * 0.9f, 0), bottom + up * 13f + new Vector2(i * 1.1f, 0), 0.5f, 0.5f);   // fletching
            yield return new Part(Mul(g.leather, dim)) { roundness = 2f, pattern = (x, y, c) => y % 4 == 0 ? Mul(c, 0.8f) : c }.Limb(bottom, bottom + up * 10f, 1.9f, 2.3f);
        }

        // Back: stowed diagonally across the back.
        static IEnumerable<Part> BackSlot(Rig k, Gear g)
        {
            if (!g.backWeapon.HasValue || g.backWeapon.Value == WeaponType.Fists) yield break;
            var start = new Vector2(k.cx + (k.side ? -3f : 5f), k.waist - 4f);
            var up = k.side ? new Vector2(-0.35f, 1f).normalized : new Vector2(-0.55f, 1f).normalized;
            float dim = k.back ? 1f : 0.82f;
            foreach (var p in WeaponParts(g.backWeapon.Value, start, up, g, dim)) yield return p;
            if (k.back) yield return new Part(Mul(g.leather, 0.9f)) { roundness = 1f }.Limb(new Vector2(k.cx - 6, k.shoulder - 1), new Vector2(k.cx + 6, k.waist - 2), 0.8f, 0.8f);   // strap
        }

        // Hip: sheathed at the character's left hip, hanging down and back.
        static IEnumerable<Part> HipSlot(Rig k, Gear g)
        {
            if (!g.hipWeapon.HasValue || g.hipWeapon.Value == WeaponType.Fists) yield break;
            float side = k.side ? -1f : k.back ? -1f : 1f;
            var top = new Vector2(k.cx + side * (Width(k, k.waist - 3, side > 0) + 1.5f), k.waist - 3f);
            var down = k.side ? new Vector2(-0.55f, -1f).normalized : new Vector2(side * 0.18f, -1f).normalized;
            // A scabbard for blades; other weapons just hang.
            var w = g.hipWeapon.Value;
            if (w == WeaponType.Sword || w == WeaponType.Daggers)
            {
                float len = w == WeaponType.Sword ? 15f : 7f;
                yield return new Part(g.leather) { roundness = 1.5f, pattern = (x, y, c) => y % 5 == 0 ? g.trim : c }.Limb(top, top + down * len, 1.1f, 0.8f);
                yield return new Part(g.trim) { roundness = 1f }.Limb(top + new Vector2(-2f, 0.5f), top + new Vector2(2f, 0.5f), 0.6f, 0.6f);   // guard
                yield return new Part(Grip) { roundness = 1f }.Limb(top, top - down * 3f, 0.6f, 0.6f);
            }
            else foreach (var p in WeaponParts(w, top + down * 2f, -down, g, 1f)) yield return p;
        }

        // A weapon held at h, pointing along up. Blades get a fuller, guards and pommels the trim color.
        static IEnumerable<Part> WeaponParts(WeaponType w, Vector2 h, Vector2 up, Gear g, float dim)
        {
            var steel = Mul(Steel, dim);
            var wood = Mul(Wood, dim);
            var trim = Mul(g.trim, dim);
            var side = new Vector2(up.y, -up.x);
            Func<int, int, Color, Color> fuller = null;
            switch (w)
            {
                case WeaponType.Sword:
                    fuller = (x, y, c) => Mathf.Abs(Vector2.Dot(new Vector2(x + 0.5f, y + 0.5f) - h, side)) < 0.5f ? Mul(c, 1.2f) : c;
                    yield return new Part(steel) { roundness = 1.5f, pattern = fuller }.Limb(h + up * 2f, h + up * 18f, 1.2f, 0.5f);
                    yield return new Part(trim) { roundness = 1f }.Limb(h + up * 1.5f - side * 3.2f, h + up * 1.5f + side * 3.2f, 0.7f, 0.7f);
                    yield return new Part(Mul(Grip, dim)) { roundness = 1f }.Limb(h + up * 1f, h - up * 2.5f, 0.7f, 0.7f);
                    yield return new Part(trim) { roundness = 1f }.Ellipse(h.x - up.x * 3.2f, h.y - up.y * 3.2f, 1f, 1f);   // pommel
                    break;
                case WeaponType.Axe:
                    yield return new Part(wood) { roundness = 1f }.Limb(h - up * 3f, h + up * 14f, 0.75f, 0.75f);
                    var head = new Part(steel) { roundness = 2f };
                    head.Limb(h + up * 12f, h + up * 12f + side * 3.5f, 1.5f, 3.4f);
                    yield return head;
                    break;
                case WeaponType.Hammer:
                    yield return new Part(wood) { roundness = 1f }.Limb(h - up * 3f, h + up * 13f, 0.75f, 0.75f);
                    yield return new Part(steel) { roundness = 2f }.Limb(h + up * 13f - side * 3.5f, h + up * 13f + side * 3.5f, 2.3f, 2.3f);
                    break;
                case WeaponType.Daggers:
                    yield return new Part(steel) { roundness = 1f }.Limb(h + up * 1.5f, h + up * 8.5f, 0.9f, 0.3f);
                    yield return new Part(trim) { roundness = 1f }.Limb(h + up - side * 1.8f, h + up + side * 1.8f, 0.5f, 0.5f);
                    break;
                case WeaponType.Spear:
                    yield return new Part(wood) { roundness = 1f }.Limb(h - up * 10f, h + up * 22f, 0.6f, 0.6f);
                    yield return new Part(steel) { roundness = 1f }.Limb(h + up * 21.5f, h + up * 26.5f, 1.3f, 0.2f);
                    break;
                case WeaponType.Bow:
                {
                    var bow = new Part(wood) { roundness = 1f };
                    for (int t = 0; t < 10; t++)
                    {
                        float a0 = Mathf.Lerp(-1.2f, 1.2f, t / 10f), a1 = Mathf.Lerp(-1.2f, 1.2f, (t + 1) / 10f);
                        bow.Limb(h + side * (Mathf.Cos(a0) * 3f) + up * (Mathf.Sin(a0) * 12f), h + side * (Mathf.Cos(a1) * 3f) + up * (Mathf.Sin(a1) * 12f), 0.65f, 0.65f);
                    }
                    yield return bow;
                    yield return new Part(Mul(new Color(0.9f, 0.88f, 0.8f), dim)) { roundness = 0.5f }.Limb(h + side * 1.1f + up * 11f, h + side * 1.1f - up * 11f, 0.3f, 0.3f);   // string
                    break;
                }
                case WeaponType.Crossbow:
                    yield return new Part(wood) { roundness = 1f }.Limb(h - up * 1f, h + up * 8f, 1f, 1f);
                    yield return new Part(steel) { roundness = 1f }.Limb(h + up * 7f - side * 5f, h + up * 7f + side * 5f, 0.7f, 0.7f);
                    break;
                case WeaponType.Staff:
                    yield return new Part(wood) { roundness = 1f }.Limb(h - up * 12f, h + up * 21f, 0.7f, 0.8f);
                    yield return new Part(Mul(new Color(0.55f, 0.85f, 1f), dim)) { roundness = 1.5f }.Ellipse(h.x + up.x * 23f, h.y + up.y * 23f, 1.9f, 1.9f);   // crystal
                    break;
                case WeaponType.Wand:
                    yield return new Part(wood) { roundness = 1f }.Limb(h, h + up * 9f, 0.55f, 0.4f);
                    yield return new Part(trim) { roundness = 1f }.Ellipse(h.x + up.x * 9.5f, h.y + up.y * 9.5f, 0.9f, 0.9f);
                    break;
            }
        }

        static Part Pattern(Part p, Func<int, int, Color, Color> f) { p.pattern = f; return p; }

        static Color Chain(int x, int y, Color c) => ((x + y) & 1) == 0 ? Mul(c, 1.15f) : Mul(c, 0.82f);

        static Func<int, int, Color, Color> Folds(Rig k) => (x, y, c) =>
            y > k.waist - 3 && y < k.waist + 4 && (x + (y - (int)k.waist)) % 4 == 0 ? Mul(c, 0.82f) : c;

        // The face, placed pixel by pixel at full resolution: anime eyes (lash line, two-tone iris, highlight),
        // arched brows, a nose shadow away from the light and a small mouth. Written into the albedo, so
        // dynamic light still shades the face.
        static void Face(Result r, bool female, Color skin, Pose pose, float hcx, float chin, float hw, Color eye, Color hair)
        {
            if (pose.facing == Facing.Back) return;
            var lash = new Color(0.14f, 0.08f, 0.1f);
            var white = new Color(0.96f, 0.95f, 0.97f);
            var irisDark = Color.Lerp(Mul(eye, 0.55f), lash, 0.2f);
            var irisLight = Color.Lerp(eye, white, 0.15f);
            var brow = Color.Lerp(Mul(hair, 0.75f), lash, 0.35f);
            var lip = female ? Color.Lerp(Mul(skin, 0.82f), new Color(0.86f, 0.36f, 0.42f), 0.5f) : Mul(skin, 0.72f);
            float cx = hcx * S, ey = chin * S + 7f;
            void P(float x, float y, Color c)
            {
                int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
                if (ix >= 0 && iy >= 0 && ix < W && iy < H && r.part[ix, iy] > 0) { r.px[ix, iy] = c; r.albedo[ix, iy] = c; }
            }
            if (pose.facing == Facing.Front)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    // Columns run from the nose outward: 0..3.
                    float X(int col) => s < 0 ? cx - 2f - col : cx + 1f + col;
                    for (int c = 0; c < 4; c++) P(X(c), ey + 2, lash);                      // upper lash
                    if (female) { P(X(4), ey + 2, lash); P(X(4), ey + 1, lash); }          // flick at the outer corner
                    P(X(0), ey + 1, irisDark); P(X(1), ey + 1, irisDark); P(X(2), ey + 1, irisDark); P(X(3), ey + 1, white);
                    P(X(0), ey, irisLight); P(X(1), ey, white); P(X(2), ey, irisLight); P(X(3), ey, Mul(skin, 0.86f));
                    P(X(1), ey + 1, Color.Lerp(white, irisDark, 0.2f));                      // catchlight
                    // Brow: arched, thicker on men.
                    P(X(0), ey + 4, brow); P(X(1), ey + 5, brow); P(X(2), ey + 5, brow); P(X(3), ey + 4, brow);
                    if (!female) { P(X(1), ey + 4, brow); P(X(2), ey + 4, brow); }
                }
                P(cx + 0.5f, ey - 1, Mul(skin, 0.85f));                                    // nose shadow
                P(cx + 0.5f, ey - 2, Mul(skin, 0.78f));
                P(cx - 0.5f, ey - 3, Mul(skin, 0.86f));
                for (int c = -1; c <= 1; c++) P(cx + c, ey - 6, c == 0 ? Mul(lip, 0.85f) : lip);   // mouth
                if (female)
                {
                    var blush = Color.Lerp(skin, new Color(1f, 0.55f, 0.55f), 0.22f);
                    P(cx - 5, ey - 3, blush); P(cx - 4, ey - 3, blush); P(cx + 4, ey - 3, blush); P(cx + 5, ey - 3, blush);
                }
            }
            else
            {
                float ex = (hcx + hw) * S - 4.5f;
                for (int c = -1; c < 3; c++) P(ex + c, ey + 2, lash);
                if (female) P(ex - 2, ey + 1, lash);
                P(ex, ey + 1, irisDark); P(ex + 1, ey + 1, irisDark); P(ex - 1, ey + 1, white);
                P(ex, ey, irisLight); P(ex + 1, ey, white); P(ex - 1, ey, Mul(skin, 0.86f));
                P(ex - 1, ey + 4, brow); P(ex, ey + 5, brow); P(ex + 1, ey + 5, brow); P(ex + 2, ey + 4, brow);
                P((hcx + hw) * S + 0.5f, ey - 2, Mul(skin, 0.86f));                    // nose tip
                P((hcx + hw) * S - 2f, ey - 6, lip); P((hcx + hw) * S - 3f, ey - 6, lip);
                P(hcx * S - 1.5f, ey - 1, Mul(skin, 0.8f));                              // ear
            }
        }
    }
}
