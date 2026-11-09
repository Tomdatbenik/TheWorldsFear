using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TWF
{
    // Ashvale in the new art: buildings, lamps, trees, shopkeepers and townsfolk walking the roads. Roads and plaza
    // are the same ones the village already has; only the buildings and props are replaced.
    public static class EnvVillage
    {
        public static bool Available => EnvArt.Tex("inn_sheet") != null;

        struct Spot { public string art; public float x, y; }

        static readonly Spot[] Buildings =
        {
            new Spot { art = "inn", x = -13f, y = 15f },
            new Spot { art = "thatch_cottage", x = -4.5f, y = 17f },
            new Spot { art = "slate_house", x = 9f, y = 15.5f },
            new Spot { art = "red_cottage", x = 17.5f, y = 15f },
            new Spot { art = "moss_cottage", x = -22.5f, y = 14f },
            new Spot { art = "timber_house", x = 27f, y = 15f },
            new Spot { art = "red_cottage", x = -15f, y = 27f },
            new Spot { art = "thatch_cottage", x = 11f, y = 26f },
            new Spot { art = "slate_house", x = -13f, y = -9f },
            new Spot { art = "moss_cottage", x = -3.5f, y = -11.5f },
            new Spot { art = "timber_house", x = 10f, y = -11f },
            new Spot { art = "thatch_cottage", x = 19.5f, y = -9f },
            new Spot { art = "red_cottage", x = -24f, y = -8f },
            new Spot { art = "slate_house", x = 27f, y = -11f },
        };

        public static void Build(Transform parent, RectInt area, Vector2 plaza, float plazaRadius)
        {
            var blocked = new List<Rect>
            {
                new Rect(plaza.x - plazaRadius - 1f, plaza.y - plazaRadius - 1f, plazaRadius * 2f + 2f, plazaRadius * 2f + 2f),
                new Rect(0f, area.yMin, 5.5f, area.height),
                new Rect(area.xMin, 2f, area.width, 5.5f),
                new Rect(-16f, 34f, 38f, 22f),   // the terrace spot further north has its own art
            };
            // Roads and plaza with natural edges. The road north of the plaza stops where the terrace spot paints its own.
            EnvArt.PaintGround(parent, new Rect(-3.5f, area.yMin, 12f, 34f - area.yMin), "dirt_tile", (x, y) => 1.65f - Mathf.Abs(x - 2.5f), "Road NS", -0.02f);
            EnvArt.PaintGround(parent, new Rect(area.xMin, -1.5f, area.width, 12f), "dirt_tile", (x, y) => 1.65f - Mathf.Abs(y - 4.5f), "Road EW", -0.03f);
            EnvArt.PaintGround(parent, new Rect(plaza.x - plazaRadius - 2f, plaza.y - plazaRadius - 2f, plazaRadius * 2f + 4f, plazaRadius * 2f + 4f), "cobble_tile",
                (x, y) => plazaRadius - Vector2.Distance(new Vector2(x, y), plaza), "Plaza", -0.04f);
            foreach (var b in Buildings)
            {
                EnvArt.Place(b.art, parent, new Vector2(b.x, b.y));
                blocked.Add(new Rect(b.x - 5.5f, b.y - 5f, 11f, 10f));
            }
            EnvArt.Place("well", parent, new Vector2(-2.2f, 0.2f));

            // Lamps along the roads and around the plaza, as before.
            for (float y = area.yMin + 6f; y < area.yMax - 4f; y += 9f)
                if (Mathf.Abs(y - plaza.y) > plazaRadius + 1f && !(y > 34f && y < 56f)) Lamp(parent, new Vector2(y % 18f < 9f ? 0.2f : 4.8f, y));
            for (float x = area.xMin + 6f; x < area.xMax - 4f; x += 9f)
                if (Mathf.Abs(x - plaza.x) > plazaRadius + 1f) Lamp(parent, new Vector2(x, x % 18f < 9f ? 2.2f : 6.8f));
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Lamp(parent, plaza + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (plazaRadius - 0.6f));
            }

            // Trees thicken toward the edge of the village; bushes hug the houses.
            var rng = new System.Random(1717);
            int placed = 0;
            for (int t = 0; t < 1800 && placed < 110; t++)
            {
                var p = new Vector2(area.xMin + (float)rng.NextDouble() * area.width, area.yMin + (float)rng.NextDouble() * area.height);
                float d = Vector2.Distance(p, plaza);
                if (d < 15f || rng.NextDouble() > Mathf.InverseLerp(14f, 40f, d) * 0.9f + 0.1f || Inside(blocked, p, 1.5f)) continue;
                EnvArt.PlaceTree(rng.NextDouble() < 0.5 ? "oak" : "oak2", parent, p);
                blocked.Add(new Rect(p.x - 2f, p.y - 2f, 4f, 4f));
                placed++;
            }
            for (int t = 0; t < 900 && placed < 190; t++)
            {
                var p = new Vector2(area.xMin + (float)rng.NextDouble() * area.width, area.yMin + (float)rng.NextDouble() * area.height);
                if (Inside(blocked, p, 0.2f) || Vector2.Distance(p, plaza) < plazaRadius + 1f) continue;
                var bush = EnvArt.PlaceTree("bush", parent, p);
                if (bush != null) Object.Destroy(bush.GetComponent<CircleCollider2D>());
                blocked.Add(new Rect(p.x - 0.7f, p.y - 0.7f, 1.4f, 1.4f));
                placed++;
            }

            new GameObject("Village Life").AddComponent<EnvVillageLife>();
        }

        static bool Inside(List<Rect> rects, Vector2 p, float pad)
        {
            foreach (var r in rects)
                if (p.x > r.xMin - pad && p.x < r.xMax + pad && p.y > r.yMin - pad && p.y < r.yMax + pad) return true;
            return false;
        }

        static void Lamp(Transform parent, Vector2 p)
        {
            EnvArt.Place("lamp", parent, p);
            var holder = new GameObject("Lamp Light");
            holder.transform.SetParent(parent, false);
            holder.transform.position = new Vector3(p.x, p.y, -3.6f);
            var light = PointLights.Attach(holder.transform, new Color(1f, 0.78f, 0.45f), 7f, 1.3f, Vector3.zero, true);
            if (light != null) holder.AddComponent<NightLight>().target = light;
        }
    }

    // Puts the shopkeepers in the new art and sends townsfolk walking between the road nodes.
    public class EnvVillageLife : MonoBehaviour
    {
        static readonly Vector2[] Ring =
        {
            new Vector2(7.5f, 4f), new Vector2(5.9f, 7.9f), new Vector2(2f, 9.5f), new Vector2(-1.9f, 7.9f),
            new Vector2(-3.5f, 4f), new Vector2(-1.9f, 0.1f), new Vector2(2f, -1.5f), new Vector2(5.9f, 0.1f),
        };

        // A road: starts at a ring node and runs outward.
        static readonly (int ring, Vector2[] nodes)[] Roads =
        {
            (2, new[] { new Vector2(2.5f, 13f), new Vector2(2.5f, 20f), new Vector2(2.5f, 28f) }),
            (6, new[] { new Vector2(2.5f, -6f), new Vector2(2.5f, -13f), new Vector2(2.5f, -20f) }),
            (4, new[] { new Vector2(-10f, 4.5f), new Vector2(-19f, 4.5f), new Vector2(-30f, 4.5f) }),
            (0, new[] { new Vector2(12f, 4.5f), new Vector2(20f, 4.5f), new Vector2(30f, 4.5f) }),
        };

        readonly List<Vector2> nodes = new List<Vector2>();
        readonly List<List<int>> links = new List<List<int>>();

        void Awake()
        {
            nodes.AddRange(Ring);
            for (int i = 0; i < Ring.Length; i++) links.Add(new List<int> { (i + 1) % Ring.Length, (i + Ring.Length - 1) % Ring.Length });
            foreach (var r in Roads)
            {
                int prev = r.ring;
                foreach (var n in r.nodes)
                {
                    int id = nodes.Count;
                    nodes.Add(n);
                    links.Add(new List<int>());
                    links[id].Add(prev);
                    links[prev].Add(id);
                    prev = id;
                }
            }
        }

        IEnumerator Start()
        {
            yield return null;          // the shopkeepers build their visuals in their own Start
            foreach (var m in FindObjectsByType<Merchant>())
            {
                var anim = m.GetComponentInChildren<CharacterAnimator>();
                if (anim == null) continue;
                var sr = anim.GetComponent<SpriteRenderer>();
                Destroy(anim);
                var npc = sr.gameObject.AddComponent<EnvNpcSprite>();
                npc.Init(ShopLook(m.displayName), sr);
                npc.Facing = Vector2.down;
            }
            var rng = new System.Random(4242);
            string[] looks = { "villager_a", "m_villager_a", "villager_b", "m_villager_b", "guard", "m_guard", "m_villager_c", "villager_c", "m_villager_a", "villager_b" };
            for (int i = 0; i < looks.Length; i++)
            {
                int start = rng.Next(nodes.Count);
                var go = new GameObject("Townsperson " + i);
                go.transform.SetParent(transform, false);
                go.transform.position = nodes[start];
                var walker = go.AddComponent<EnvWalker>();
                walker.Init(looks[i], nodes, links, start, rng.Next());
            }
        }

        static string ShopLook(string name)
        {
            switch (name)
            {
                case "Merchant": return "merchant";
                case "Weaponmaster": return "m_smith";
                case "Arcane Forge": return "m_mystic";
                case "Mystic": return "mystic";
                default: return "m_villager_b";
            }
        }
    }

    // Plays an NPC's idle or walk strip, in the direction it is facing on screen.
    public class EnvNpcSprite : MonoBehaviour
    {
        const float WalkFps = 11f, IdleFps = 5f;
        const int W = 96, H = 144;
        static readonly Dictionary<string, Sprite[]> strips = new Dictionary<string, Sprite[]>();
        static readonly string[] Directions = { "right", "up_right", "up", "up_left", "left", "down_left", "down", "down_right" };

        public Vector2 Facing = Vector2.down;
        public bool Walking;
        string look;
        SpriteRenderer sr;
        float clock;

        public void Init(string id, SpriteRenderer renderer)
        {
            look = id;
            sr = renderer;
            clock = Random.value * 3f;
        }

        Sprite[] Strip(string anim, int dir)
        {
            string key = look + "/" + anim + "_" + Directions[dir];
            if (strips.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("TWF/Env/NPC/" + key);
            s = new Sprite[tex != null ? tex.width / W : 0];
            for (int i = 0; i < s.Length; i++)
                s[i] = Sprite.Create(tex, new Rect(i * W, 0, W, H), new Vector2(0.5f, 0.01f), 72f, 0, SpriteMeshType.FullRect);
            strips[key] = s;
            return s;
        }

        void Update()
        {
            if (sr == null || look == null) return;
            clock += Time.deltaTime;
            int dir = Hero3DSet.DirectionOf(View.ToScreen(Facing));
            var strip = Strip(Walking ? "walk" : "idle", dir);
            if (strip.Length == 0) return;
            sr.sprite = strip[(int)(clock * (Walking ? WalkFps : IdleFps)) % strip.Length];
        }
    }

    // A townsperson: walks from node to node along the roads, pausing now and then.
    public class EnvWalker : MonoBehaviour
    {
        const float Speed = 1.15f;
        List<Vector2> nodes;
        List<List<int>> links;
        EnvNpcSprite npc;
        System.Random rng;
        int current, target;
        float wait;

        public void Init(string look, List<Vector2> nodes, List<List<int>> links, int start, int seed)
        {
            this.nodes = nodes;
            this.links = links;
            current = target = start;
            rng = new System.Random(seed);
            wait = (float)rng.NextDouble() * 3f;

            var visual = Billboard.Create(transform, "Visual");
            var sr = visual.gameObject.AddComponent<SpriteRenderer>();
            sr.sortingOrder = SortOrder.Units;
            RenderAssets.MakeLit(sr);
            npc = visual.gameObject.AddComponent<EnvNpcSprite>();
            npc.Init(look, sr);

            var shadow = GameAssets.CreateWorldSprite("Shadow", GameAssets.SoftCircle, transform, new Color(0, 0, 0, 0.3f), SortOrder.Units - 1, 1f);
            shadow.transform.localScale = new Vector3(shadow.transform.localScale.x, shadow.transform.localScale.y * 0.4f, 1f);
            shadow.transform.localPosition = new Vector3(0f, 0f, Ground.DecalZ);
        }

        void Update()
        {
            if (wait > 0f)
            {
                wait -= Time.deltaTime;
                npc.Walking = false;
                return;
            }
            var pos = (Vector2)transform.position;
            var goal = nodes[target];
            var to = goal - pos;
            float step = Speed * Time.deltaTime;
            if (to.magnitude <= step)
            {
                transform.position = goal;
                current = target;
                var options = links[current];
                target = options[rng.Next(options.Count)];
                if (rng.NextDouble() < 0.35) wait = 1f + (float)rng.NextDouble() * 5f;
                return;
            }
            npc.Walking = true;
            npc.Facing = to.normalized;
            transform.position = pos + to.normalized * step;
        }
    }
}
