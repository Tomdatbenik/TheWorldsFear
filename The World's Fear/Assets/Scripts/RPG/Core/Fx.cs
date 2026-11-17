using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Drawn combat effects (explosions, shockwaves, swing trails, lingering pools, status flames).
    // The sheets come from pixelart-gen/tools/fx_gen.py and live in Resources/TWF/FX with fx.json.
    public static class Fx
    {
        public const float PPU = 72f;   // the same art scale as the characters

        [Serializable] class Item { public string name, mode; public int w, h, frames; public float fps, px, py, unit; public bool loop; }
        [Serializable] class Catalog { public Item[] items; }

        public class Sheet
        {
            public Sprite[] frames;
            public float fps, unit;
            public bool loop, ground;
        }

        static Dictionary<string, Item> items;
        static readonly Dictionary<string, Sheet> sheets = new Dictionary<string, Sheet>();
        static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>(), discs = new Dictionary<int, Sprite>();

        public static Sheet Get(string name)
        {
            if (sheets.TryGetValue(name, out var s)) return s;
            if (items == null)
            {
                items = new Dictionary<string, Item>();
                var json = Resources.Load<TextAsset>("TWF/FX/fx");
                if (json != null)
                    foreach (var it in JsonUtility.FromJson<Catalog>(json.text).items) items[it.name] = it;
            }
            s = null;
            if (items.TryGetValue(name, out var item) && Resources.Load<Texture2D>("TWF/FX/" + name) is Texture2D tex)
            {
                s = new Sheet { frames = new Sprite[item.frames], fps = item.fps, unit = item.unit, loop = item.loop, ground = item.mode == "ground" };
                for (int i = 0; i < item.frames; i++)
                {
                    s.frames[i] = Sprite.Create(tex, new Rect(i * item.w, 0, item.w, item.h), new Vector2(item.px, item.py), PPU, 0, SpriteMeshType.FullRect);
                    s.frames[i].name = name + "_" + i;
                }
            }
            sheets[name] = s;
            return s;
        }

        // ---------- Which element an effect shows ----------

        public static string Name(ElementPart e) => e.ToString().ToLowerInvariant();

        static float ColorDistance(ElementPart e, Color color)
        {
            var c = SkillParts.ElementColor(e);
            return (c.r - color.r) * (c.r - color.r) + (c.g - color.g) * (c.g - color.g) + (c.b - color.b) * (c.b - color.b);
        }

        // Every element of a multi-element spell, the main one first (its colour leans toward the main element).
        // With no elements, the closest element colour (monster attacks), else physical.
        public static List<ElementPart> Parts(int mask, Color color)
        {
            var list = new List<ElementPart>(Elements.In(mask));
            if (list.Count == 0)
            {
                var best = ElementPart.Physical;
                float bestD = 0.08f;
                foreach (ElementPart e in Enum.GetValues(typeof(ElementPart)))
                {
                    if (e == ElementPart.Physical) continue;
                    float d = ColorDistance(e, color);
                    if (d < bestD) { bestD = d; best = e; }
                }
                list.Add(best);
            }
            else if (list.Count > 1)
            {
                list.Sort((a, b) => ColorDistance(a, color).CompareTo(ColorDistance(b, color)));
                // A crafted spell names its main element; otherwise the colour decides.
                if (Elements.Main(mask) is ElementPart main && list.Remove(main)) list.Insert(0, main);
            }
            return list;
        }

        // The main element.
        public static string Element(int mask, Color color) => Name(Parts(mask, color)[0]);

        static void Above(FxPlayer p, int layer)
        {
            if (p != null && layer > 0) p.GetComponent<SpriteRenderer>().sortingOrder += layer;
        }

        // ---------- Playing ----------

        // radius: the size in world units the sheet's reference size maps to (blast radius, flame height, ...).
        public static FxPlayer Play(string name, Vector2 at, float radius, Color? tint = null, float angle = 0f, float life = -1f)
        {
            var sheet = Get(name);
            if (sheet == null || radius <= 0f) return null;
            var go = new GameObject("FX " + name);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sheet.frames[0];
            RenderAssets.MakeUnlit(sr);
            if (sheet.ground)
            {
                go.transform.position = Ground.Decal(at);
                go.transform.rotation = Ground.Spin(angle) * Ground.Flat;
                sr.sortingOrder = SortOrder.Telegraph + 2;
            }
            else
            {
                go.transform.position = at.W();
                go.AddComponent<Billboard>();
                sr.sortingOrder = SortOrder.Effects;
            }
            go.transform.localScale = Vector3.one * (radius * PPU / sheet.unit);
            var p = go.AddComponent<FxPlayer>();
            p.Init(sheet, sr, tint ?? Color.white, life);
            return p;
        }

        // An explosion: the upright blast plus its shockwave on the ground.
        public static void Blast(Vector2 at, float radius, string element)
        {
            Play("shock_" + element, at, radius);
            Play("blast_" + element, at, radius * 0.85f);
        }

        // A multi-element explosion: the main element's blast, with each extra element's blast layered over it.
        public static void Blast(Vector2 at, float radius, int mask, Color color)
        {
            var els = Parts(mask, color);
            Blast(at, radius, Name(els[0]));
            for (int i = 1; i < els.Count; i++)
            {
                string n = Name(els[i]);
                var off = UnityEngine.Random.insideUnitCircle * radius * 0.25f;
                Above(Play("shock_" + n, at, radius * 0.75f, new Color(1f, 1f, 1f, 0.8f), UnityEngine.Random.Range(0f, 360f)), i);
                Above(Play("blast_" + n, at + off, radius * 0.6f), i);
            }
        }

        public static void Shock(Vector2 at, float radius, string element, float alpha = 1f) =>
            Play("shock_" + element, at, radius, new Color(1f, 1f, 1f, alpha));

        // Slides an effect toward the camera along its view. With the orthographic camera it stays exactly where it
        // is on screen, but now draws in front of the bodies it overlaps instead of behind them.
        public static FxPlayer Lift(FxPlayer p, float distance)
        {
            var cam = Camera.main;
            if (p != null && cam != null) p.transform.position -= cam.transform.forward * distance;
            return p;
        }

        // A spark at a point in the air (at keeps its height).
        public static void Hit(Vector3 at, string element, float size = 0.4f)
        {
            var p = Play("hit_" + element, at.G(), size);
            if (p != null) p.transform.position = at;
            Lift(p, 1f);
        }

        // On a creature's chest, in front of it.
        static FxPlayer OnBody(string name, Combatant c, float size)
        {
            var p = Play(name, c.transform.G(), size);
            if (p != null) p.transform.position += View.Up * (c.height * 0.55f);
            return Lift(p, 1f);
        }

        public static void HitOn(Combatant c, string element)
        {
            if (c != null) OnBody("hit_" + element, c, 0.4f + 0.1f * c.height);
        }

        // A multi-element hit: the main element's spark, plus each extra element's small hit extra.
        public static void HitOn(Combatant c, int mask, Color color)
        {
            if (c == null) return;
            var els = Parts(mask, color);
            HitOn(c, Name(els[0]));
            for (int i = 1; i < els.Count; i++) Proc(c, els[i]);
        }

        // What an elemental weapon adds to each hit (small bolts, poison puffs, embers, shards...). Nothing for plain weapons.
        public static void Proc(Combatant c, ElementPart e)
        {
            if (c != null && e != ElementPart.Physical) OnBody("proc_" + Name(e), c, 0.45f + 0.12f * c.height);
        }

        public const float SwingHeight = 0.7f;   // screen-up offset of a melee swing from the feet

        // A swing trail in the element's look, centred on dir. Thin arcs become a thrust.
        public static void Slash(Vector3 pos, Vector2 dir, float radius, float arc, string element)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            string name;
            if (arc <= 50f) name = "thrust_" + element;
            else if (arc >= 300f) name = "slash_360_" + element;
            else
            {
                int[] arcs = { 90, 120, 200 };
                int best = arcs[0];
                foreach (int a in arcs) if (Mathf.Abs(a - arc) < Mathf.Abs(best - arc)) best = a;
                name = "slash_" + best + "_" + element;
            }
            var p = Play(name, pos.G(), radius, null, angle);
            if (p == null) return;
            var sr = p.GetComponent<SpriteRenderer>();
            // The sheets sweep counter-clockwise; the blade (HeroController.SwingRoutine) swings clockwise, from
            // +arc/2 to -arc/2. Mirrored across the swing direction, the trail follows the blade.
            sr.flipY = true;
            if (arc < 300f)
            {
                // Drawn like the blade: facing the camera, round the chest, turned to the swing's direction on screen.
                // Laid on the ground instead, the tilted view squashed it and a swing to the side read as angled down.
                Vector2 onScreen = View.ToScreen(dir);
                p.transform.SetPositionAndRotation(pos + View.Up * SwingHeight,
                    View.Rotation * Quaternion.Euler(0f, 0f, Mathf.Atan2(onScreen.y, onScreen.x) * Mathf.Rad2Deg));
                sr.sortingOrder = SortOrder.Effects;
            }
            // Lifted toward the camera so a swing away from it isn't hidden behind the hero's own body.
            Lift(p, 2.5f);
        }

        // A multi-element swing: one trail per element, each extra a little shorter so they nest.
        public static void Slash(Vector3 pos, Vector2 dir, float radius, float arc, int mask, Color color)
        {
            var els = Parts(mask, color);
            for (int i = 0; i < els.Count; i++)
                Slash(pos, dir, radius * (1f - 0.18f * i), arc, Name(els[i]));
        }

        // ---------- Projectiles ----------

        // Natural size (world units across the sheet's reference length) and the spec size that maps to it.
        static (float world, float baseSize) LookSize(string look)
        {
            switch (look)
            {
                case "arrow": return (0.85f, 0.9f);
                case "knife": return (0.5f, 0.55f);
                case "spear": return (1.45f, 1.1f);
                case "axe": return (0.75f, 0.9f);
                default: return (1.3f, 1f);   // bolt / orb: the ball's diameter follows the shot size
            }
        }

        // Puts the drawn look of a shot under its transform (flat, pointing along +X), or returns null.
        public static FxPlayer Projectile(ProjectileSpec spec, Transform parent)
        {
            string look = spec.look;
            if (string.IsNullOrEmpty(look))
                look = spec.sprite != null ? (spec.sprite == HeroVisuals.ArrowSprite ? "arrow" : null) : (spec.size >= 0.42f ? "orb" : "bolt");
            if (look == null) return null;
            var (world, baseSize) = LookSize(look);
            float size = look == "bolt" || look == "orb"
                ? world * Mathf.Max(0.2f, spec.size)
                : world * Mathf.Clamp(spec.size / baseSize, 0.8f, 1.4f);
            var els = Parts(spec.elements, spec.color);
            var p = Attach(look + "_" + Name(els[0]), parent, size, Vector3.zero, SortOrder.Projectiles);
            if (p == null) return null;
            // Extra elements fly along as small bolts beside and just behind the shot.
            float ball = look == "bolt" || look == "orb" ? size : Mathf.Max(0.3f, size * 0.45f);
            for (int i = 1; i < els.Count; i++)
            {
                float side = (i % 2 == 1 ? 1f : -1f) * ball * 0.32f;
                Attach("bolt_" + Name(els[i]), parent, ball * 0.6f, new Vector3(-ball * 0.3f * i, 0f, side), SortOrder.Projectiles - i);
            }
            return p;
        }

        static FxPlayer Attach(string name, Transform parent, float size, Vector3 local, int order)
        {
            var p = Play(name, parent.G(), size);
            if (p == null) return null;
            p.transform.SetParent(parent, false);
            p.transform.localPosition = local;
            p.transform.localRotation = Quaternion.identity;
            p.GetComponent<SpriteRenderer>().sortingOrder = order;
            return p;
        }

        // A lingering zone: a looping pool, with a few flames standing in it.
        public static void Linger(Vector2 at, float radius, string element, float duration, Transform follow = null) =>
            Linger(at, radius, new List<string> { element }, duration, follow);

        // A multi-element pool: the main element's pool, a smaller pool per extra element on top, and flames of every element.
        public static void Linger(Vector2 at, float radius, int mask, Color color, float duration, Transform follow = null)
        {
            var names = new List<string>();
            foreach (var e in Parts(mask, color)) names.Add(Name(e));
            Linger(at, radius, names, duration, follow);
        }

        static void Linger(Vector2 at, float radius, List<string> elements, float duration, Transform follow)
        {
            var pool = Play("linger_" + elements[0], at, radius, new Color(1f, 1f, 1f, 0.9f), UnityEngine.Random.Range(0f, 360f), duration);
            if (pool == null) return;
            pool.Follow(follow);
            for (int i = 1; i < elements.Count; i++)
            {
                var extra = Play("linger_" + elements[i], at, radius * (0.7f - 0.12f * (i - 1)), new Color(1f, 1f, 1f, 0.8f), UnityEngine.Random.Range(0f, 360f), duration);
                Above(extra, i);
                extra?.Follow(follow);
            }
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 3f), 3, 10);
            if (elements.Count > 1) count = Mathf.Max(count, 2 * elements.Count);
            for (int i = 0; i < count; i++)
            {
                var off = UnityEngine.Random.insideUnitCircle * radius * 0.7f;
                var f = Play("flame_" + elements[i % elements.Count], at + off, UnityEngine.Random.Range(0.4f, 0.65f), null, 0f, duration);
                if (f == null) continue;
                f.Follow(follow);
                f.StartAt(UnityEngine.Random.Range(0, 8));
            }
        }

        // ---------- Crisp telegraph shapes, drawn at the real size so they never turn blocky ----------

        // Rings and discs are drawn at about their real size so a warning stays crisp, in steps of 48 pixels: a
        // texture per exact radius meant a new pair of large textures, built pixel by pixel through a callback and
        // outlined by Unity, for nearly every monster's area attack - 60 ms on a PC the first time each size showed.
        static int PixelsFor(float radius) => Mathf.Clamp(Mathf.CeilToInt(radius * 2f * PPU / 48f) * 48, 48, 624);

        public static Sprite Ring(float radius) => Round(rings, PixelsFor(radius), true);
        public static Sprite Disc(float radius) => Round(discs, PixelsFor(radius), false);

        static Sprite Round(Dictionary<int, Sprite> made, int n, bool ring)
        {
            if (made.TryGetValue(n, out var s) && s) return s;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = (ring ? "fxring" : "fxdisc") + n };
            // Written straight into the texture's own memory: no managed array of a million bytes for the collector.
            var px = tex.GetRawTextureData<Color32>();
            var white = new Color32(255, 255, 255, 255); var faint = new Color32(255, 255, 255, 115); var clear = new Color32(0, 0, 0, 0);
            float r = n / 2f;
            // Squared distances from the centre, row by row: no callback, no square roots.
            float outer = r * r, band = (r - 2.5f) * (r - 2.5f), in0 = (r - 5f) * (r - 5f), in1 = (r - 6f) * (r - 6f);
            for (int y = 0; y < n; y++)
            {
                float dy = y + 0.5f - r, dy2 = dy * dy;
                int row = y * n;
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - r, d2 = dx * dx + dy2;
                    px[row + x] = d2 > outer ? clear : !ring || d2 > band ? white : d2 <= in0 && d2 > in1 ? faint : clear;
                }
            }
            tex.Apply(false, true);
            // A plain quad: the default tight mesh makes Unity trace the outline of the whole texture.
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect);
            s.name = tex.name;
            return made[n] = s;
        }
    }
}
