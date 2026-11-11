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

        // The first element in the mask; with none, the closest element colour (monster attacks), else physical.
        public static string Element(int mask, Color color)
        {
            for (int i = 0; i < 8; i++)
                if ((mask & (1 << i)) != 0) return Name((ElementPart)i);
            string best = "physical";
            float bestD = 0.08f;
            foreach (ElementPart e in Enum.GetValues(typeof(ElementPart)))
            {
                if (e == ElementPart.Physical) continue;
                var c = SkillParts.ElementColor(e);
                float d = (c.r - color.r) * (c.r - color.r) + (c.g - color.g) * (c.g - color.g) + (c.b - color.b) * (c.b - color.b);
                if (d < bestD) { bestD = d; best = Name(e); }
            }
            return best;
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
                go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                sr.sortingOrder = SortOrder.Telegraph + 2;
            }
            else
            {
                go.transform.position = new Vector3(at.x, at.y, 0f);
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
            var p = Play("hit_" + element, at, size);
            if (p != null) p.transform.position = at;
            Lift(p, 1f);
        }

        // On a creature's chest, in front of it.
        static FxPlayer OnBody(string name, Combatant c, float size)
        {
            var p = Play(name, c.transform.position, size);
            if (p != null) p.transform.position += View.Up * (c.height * 0.55f);
            return Lift(p, 1f);
        }

        public static void HitOn(Combatant c, string element)
        {
            if (c != null) OnBody("hit_" + element, c, 0.4f + 0.1f * c.height);
        }

        // What an elemental weapon adds to each hit (small bolts, poison puffs, embers, shards...). Nothing for plain weapons.
        public static void Proc(Combatant c, ElementPart e)
        {
            if (c != null && e != ElementPart.Physical) OnBody("proc_" + Name(e), c, 0.45f + 0.12f * c.height);
        }

        // A swing trail on the ground in the element's look, centred on dir. Thin arcs become a thrust.
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
            // Lifted so a swing away from the camera isn't hidden behind the hero's own body.
            Lift(Play(name, pos, radius, null, angle), 2.5f);
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
            var p = Play(look + "_" + Element(spec.elements, spec.color), parent.position, size);
            if (p == null) return null;
            p.transform.SetParent(parent, false);
            p.transform.localPosition = Vector3.zero;
            p.transform.localRotation = Quaternion.identity;
            p.GetComponent<SpriteRenderer>().sortingOrder = SortOrder.Projectiles;
            return p;
        }

        // A lingering zone: a looping pool, with a few flames standing in it.
        public static void Linger(Vector2 at, float radius, string element, float duration, Transform follow = null)
        {
            var pool = Play("linger_" + element, at, radius, new Color(1f, 1f, 1f, 0.9f), UnityEngine.Random.Range(0f, 360f), duration);
            if (pool == null) return;
            pool.Follow(follow);
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 3f), 3, 10);
            for (int i = 0; i < count; i++)
            {
                var off = UnityEngine.Random.insideUnitCircle * radius * 0.7f;
                var f = Play("flame_" + element, at + off, UnityEngine.Random.Range(0.4f, 0.65f), null, 0f, duration);
                if (f == null) continue;
                f.Follow(follow);
                f.StartAt(UnityEngine.Random.Range(0, 8));
            }
        }

        // ---------- Crisp telegraph shapes, drawn at the real size so they never turn blocky ----------

        static int PixelsFor(float radius) => Mathf.Clamp(Mathf.CeilToInt(radius * 2f * PPU / 4f) * 4, 8, 640);

        public static Sprite Ring(float radius)
        {
            int n = PixelsFor(radius);
            if (rings.TryGetValue(n, out var s) && s) return s;
            s = GameAssets.Make("fxring" + n, n, n, (x, y, w, h) =>
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(w / 2f, h / 2f));
                float r = w / 2f;
                if (d <= r && d > r - 2.5f) return Color.white;
                if (d <= r - 5f && d > r - 6f) return new Color(1f, 1f, 1f, 0.45f);
                return Color.clear;
            }, PPU, FilterMode.Point);
            return rings[n] = s;
        }

        public static Sprite Disc(float radius)
        {
            int n = PixelsFor(radius);
            if (discs.TryGetValue(n, out var s) && s) return s;
            s = GameAssets.Make("fxdisc" + n, n, n, (x, y, w, h) =>
                Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(w / 2f, h / 2f)) <= w / 2f ? Color.white : Color.clear, PPU, FilterMode.Point);
            return discs[n] = s;
        }
    }
}
