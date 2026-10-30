using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    // A challenge chest: start it and survive 3-10 waves around it. Waves grow from a handful of monsters
    // to a crowd (40 in the last wave of a 10-wave chest). Clear them all and the chest opens with loot
    // that scales with its waves; leave the area or die and the challenge fails (the chest stays).
    public class ChallengeChest : MonoBehaviour
    {
        public int waves = 3;
        public string key;

        const float ArenaRadius = 16f;
        enum State { Idle, Running, Opened }
        State state;
        SpriteRenderer sr;
        TextMeshPro label;
        Light glow;
        bool inRange;
        readonly List<MonsterBrain> alive = new List<MonsterBrain>();
        GameObject arenaRing;

        public static Color TierColor(int waves) =>
            waves >= 8 ? new Color(1f, 0.3f, 0.55f) : waves >= 5 ? new Color(1f, 0.75f, 0.25f) : new Color(0.45f, 0.75f, 1f);

        // Largest wave: 6 monsters for a 3-wave chest up to 40 for a 10-wave chest.
        public static int BiggestWave(int waves) => Mathf.RoundToInt(Mathf.Lerp(6f, 40f, (waves - 3) / 7f));
        public static int WaveSize(int waves, int wave) => Mathf.Max(2, Mathf.RoundToInt(Mathf.Lerp(3f, BiggestWave(waves), wave / (float)waves)));

        void Start()
        {
            bool opened = Profile.Data.openedChests.Contains(key);
            state = opened ? State.Opened : State.Idle;
            var bb = Billboard.Create(transform, "Visual");
            var go = new GameObject("Chest");
            go.transform.SetParent(bb, false);
            go.transform.localScale = Vector3.one * (1.2f + (waves - 3) * 0.06f);
            sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = opened ? ChestSprite(true, waves) : ChestSprite(false, waves);
            RenderAssets.MakeLit(sr);
            var shadow = GameAssets.CreateWorldSprite("Shadow", GameAssets.SoftCircle, transform, new Color(0, 0, 0, 0.35f), SortOrder.Units - 1, 1.6f);
            shadow.transform.localScale = new Vector3(shadow.transform.localScale.x, shadow.transform.localScale.y * 0.5f, 1f);
            shadow.transform.localPosition = new Vector3(0, 0, Ground.DecalZ);
            if (!opened) glow = PointLights.Attach(transform, TierColor(waves), 4f, 1.4f, new Vector3(0, 0, -1f), true);
            gameObject.layer = LayerMask.NameToLayer("Walls");
            gameObject.AddComponent<BoxCollider2D>().size = new Vector2(1.2f, 0.7f);

            label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(Billboard.Create(transform), false);
            label.transform.localPosition = new Vector3(0, 2f, 0);
            label.font = GameAssets.Font;
            label.fontSize = 2.6f;
            label.alignment = TextAlignmentOptions.Center;
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 5;
            label.rectTransform.sizeDelta = new Vector2(8, 2);
            RefreshLabel();
        }

        void RefreshLabel()
        {
            var c = TierColor(waves);
            label.color = state == State.Opened ? new Color(0.7f, 0.7f, 0.7f) : c;
            label.text = state == State.Opened ? "Opened chest" : $"Challenge Chest\n<size=70%>{waves} waves</size>";
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null || GameUI.Instance == null) return;
            if (glow != null) glow.intensity = 1.2f + Mathf.Sin(Time.time * 3f) * 0.3f;
            bool near = state == State.Idle && Vector2.Distance(hero.transform.position, transform.position) < 2.4f;
            if (near && !inRange)
                GameUI.Instance.ShowInteract("Start", () => ConfirmDialog.Show(
                    $"Start the challenge?\n<size=70%>{waves} waves, the last one {BiggestWave(waves)} monsters strong. Stay near the chest; " +
                    "leaving or dying ends the challenge.</size>", "Start", Begin), "skull");
            else if (!near && inRange) GameUI.Instance.HideInteract();
            inRange = near;
        }

        void Begin()
        {
            if (state != State.Idle) return;
            GameUI.Instance?.HideInteract();
            inRange = false;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            state = State.Running;
            label.text = $"Challenge\n<size=70%>starting...</size>";
            arenaRing = GameAssets.CreateWorldSprite("Arena", GameAssets.PixelRing, transform, new Color(TierColor(waves).r, TierColor(waves).g, TierColor(waves).b, 0.6f), SortOrder.Ground + 4, ArenaRadius * 2f).gameObject;
            arenaRing.transform.localPosition = new Vector3(0, 0, Ground.DecalZ);
            Sound.Play(Sfx.MetalRoar, transform.position, 0.5f);
            int level = WorldGen.LevelAt(transform.position);
            var zone = WorldGen.RegionAt(transform.position);
            var rng = new System.Random(key.GetHashCode() ^ System.Environment.TickCount);
            var pool = MonsterPool(zone);
            yield return new WaitForSeconds(1.5f);

            for (int w = 1; w <= waves; w++)
            {
                int count = WaveSize(waves, w);
                label.text = $"Wave {w}/{waves}\n<size=70%>{count} monsters</size>";
                GameUI.Instance?.Toast(w == waves ? $"Final wave! {count} monsters" : $"Wave {w}/{waves}", TierColor(waves), 2f);
                for (int i = 0; i < count; i++)
                {
                    if (Failed()) { yield return Fail(); yield break; }
                    // Every wave mixes fighting styles; the first few of each wave include ranged attackers.
                    var sp = i < Mathf.Max(1, count / 5) ? Ranged(pool, rng) : Pick(pool, rng);
                    // Later waves bring elites; long challenges end with rares.
                    float progress = w / (float)waves;
                    var rank = w == waves && waves >= 6 && i < waves / 3 ? MonsterRank.Rare
                             : rng.NextDouble() < progress * 0.25f ? MonsterRank.Elite : MonsterRank.Normal;
                    var m = MonsterBrain.Spawn(sp, level + (waves >= 8 ? 1 : 0), rank, SpawnPoint(rng), null, zone);
                    m.leashRange = 100f;
                    m.Aggro();
                    alive.Add(m);
                    VFX.Burst(m.transform.position, TierColor(waves), 8, 0.8f);
                    if (i % 4 == 3) yield return new WaitForSeconds(0.15f);
                }
                // Wait for the wave to fall.
                while (true)
                {
                    alive.RemoveAll(m => m == null || m.Combatant.IsDead);
                    if (alive.Count == 0) break;
                    if (Failed()) { yield return Fail(); yield break; }
                    yield return new WaitForSeconds(0.25f);
                }
                if (w < waves) yield return new WaitForSeconds(2f);
            }
            Open(level);
        }

        // One species per fighting style for this place: chasers, swarms, spitters that keep their distance
        // and shoot, casters, chargers, brutes and exploders.
        static readonly (MonsterArchetype arch, int weight)[] mix =
        {
            (MonsterArchetype.Chaser, 22), (MonsterArchetype.Swarmling, 16), (MonsterArchetype.Spitter, 18), (MonsterArchetype.Caster, 14),
            (MonsterArchetype.Charger, 12), (MonsterArchetype.Brute, 10), (MonsterArchetype.Exploder, 8),
        };

        List<MonsterSpecies> MonsterPool(ZoneDefinition zone)
        {
            var biome = zone != null ? zone.biome : Biome.Forest;
            var pool = new List<MonsterSpecies>();
            int baseSeed = key.GetHashCode();
            for (int i = 0; i < mix.Length; i++)
            {
                var sp = MonsterGenerator.Create(baseSeed + i * 7919, biome, mix[i].arch);
                sp.name = MonsterGenerator.MakeName(new System.Random(baseSeed + i), biome, mix[i].arch, sp.isSpider);
                pool.Add(sp);
            }
            return pool;
        }

        static MonsterSpecies Pick(List<MonsterSpecies> pool, System.Random rng)
        {
            int total = 0;
            foreach (var m in mix) total += m.weight;
            int r = rng.Next(total);
            for (int i = 0; i < mix.Length; i++)
            {
                r -= mix[i].weight;
                if (r < 0) return pool[i];
            }
            return pool[0];
        }

        static MonsterSpecies Ranged(List<MonsterSpecies> pool, System.Random rng) =>
            pool.Find(p => p.archetype == (rng.NextDouble() < 0.6 ? MonsterArchetype.Spitter : MonsterArchetype.Caster)) ?? pool[0];

        Vector2 SpawnPoint(System.Random rng)
        {
            for (int tries = 0; tries < 12; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var p = (Vector2)transform.position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (7f + (float)rng.NextDouble() * 5f);
                if (Physics2D.OverlapCircle(p, 0.5f, Walls.Mask) == null) return p;
            }
            return (Vector2)transform.position + Random.insideUnitCircle.normalized * 4f;
        }

        bool Failed()
        {
            var hero = HeroController.Instance;
            return hero == null || hero.Combatant.IsDead || Vector2.Distance(hero.transform.position, transform.position) > ArenaRadius + 4f;
        }

        IEnumerator Fail()
        {
            GameUI.Instance?.Toast("Challenge failed: you left the chest behind", new Color(1f, 0.45f, 0.35f), 3f);
            foreach (var m in alive) if (m != null) Destroy(m.gameObject);
            alive.Clear();
            if (arenaRing != null) Destroy(arenaRing);
            state = State.Idle;
            RefreshLabel();
            yield break;
        }

        // ---------- The reward ----------

        void Open(int level)
        {
            state = State.Opened;
            Profile.Data.openedChests.Add(key);
            if (arenaRing != null) Destroy(arenaRing);
            if (glow != null) Destroy(glow.gameObject);
            sr.sprite = ChestSprite(true, waves);
            RefreshLabel();
            Sound.Play(Sfx.LevelUp, transform.position, 0.8f);
            CameraFollow.Shake(0.3f);
            VFX.Ring(transform.position, TierColor(waves), 4f, 0.8f);
            VFX.Burst(transform.position, Palette.Gold, 60, 3f);

            var rng = new System.Random(key.GetHashCode() ^ System.Environment.TickCount);
            Vector2 at = transform.position + Vector3.down * 1.4f;
            Vector2 Spot() => at + Random.insideUnitCircle * 2.2f;

            // Gear: more pieces and better rarities the longer the challenge.
            int items = 2 + waves;
            for (int i = 0; i < items; i++)
            {
                Rarity rarity;
                if (i == 0 && waves >= 10) rarity = Rarity.Mythic;                                   // a 10-wave chest always holds a mythic
                else if (i == 1 && waves >= 8 && rng.NextDouble() < 0.5) rarity = Rarity.Mythic;
                else if (i <= waves / 4) rarity = Rarity.Legendary;                                  // 5+ waves: at least one legendary
                else rarity = rng.NextDouble() < 0.15 + waves * 0.04 ? Rarity.Legendary : Rarity.Rare;
                Pickup.SpawnItem(Spot(), ItemGenerator.Generate(level + 2, rarity, ItemGenerator.RandomSlot(rng), rng));
            }
            // Gold, skill parts, potions.
            int gold = Mathf.RoundToInt(level * 8f * waves * (1f + waves * 0.1f));
            Pickup.SpawnGold(Spot(), gold);
            for (int i = 0; i < 1 + waves / 2; i++)
            {
                var part = SkillParts.Roll(rng, level + 10, MonsterRank.Rare);
                part.tier = Mathf.Clamp(part.tier + (waves >= 7 ? 1 : 0), 1, 3);
                Pickup.SpawnGlyph(Spot(), part);
            }
            for (int i = 0; i < 1 + waves / 3; i++) Pickup.SpawnPotion(Spot());
            // Bonus experience for surviving.
            Profile.AddXP(Profile.MonsterXP(level, MonsterRank.Rare) * waves);
            GameUI.Instance?.Toast($"Challenge complete! {waves} waves survived", Palette.Gold, 4f);
            Profile.Save();
        }

        // ---------- Art ----------

        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        // A pixel-art chest: wooden body, metal bands in the tier's color, open or closed.
        static Sprite ChestSprite(bool open, int waves)
        {
            string k = $"{open}_{waves}";
            if (sprites.TryGetValue(k, out var s) && s) return s;
            const int W = 32, H = 28;
            var px = new Color[W * H];
            var wood = new Color(0.5f, 0.3f, 0.16f);
            var band = Color.Lerp(TierColor(waves), new Color(0.75f, 0.7f, 0.6f), 0.35f);
            var ink = new Color(0.1f, 0.06f, 0.06f);
            void P(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = c; }
            void Rect(int x0, int y0, int w, int h, Color c) { for (int x = x0; x < x0 + w; x++) for (int y = y0; y < y0 + h; y++) P(x, y, c); }
            // Body.
            for (int x = 3; x < 29; x++)
            for (int y = 1; y < 14; y++)
                P(x, y, wood * Mathf.Lerp(1.15f, 0.75f, (x - 3) / 25f) * ((y % 4 == 0) ? 0.85f : 1f));
            if (!open)
            {
                // Domed lid.
                for (int x = 3; x < 29; x++)
                {
                    float t = (x - 3) / 25f;
                    int top = 14 + Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * 5f) + 3;
                    for (int y = 14; y < top; y++) P(x, y, wood * Mathf.Lerp(1.2f, 0.8f, t) * 1.05f);
                }
                Rect(3, 13, 26, 2, band);
                Rect(7, 1, 2, 20, band);
                Rect(23, 1, 2, 20, band);
                Rect(14, 9, 4, 6, band * 1.2f);   // lock
                P(15, 11, ink); P(16, 11, ink); P(15, 10, ink);
            }
            else
            {
                // Lid flipped back, treasure glowing inside.
                Rect(3, 14, 26, 9, wood * 0.7f);
                Rect(3, 20, 26, 2, band);
                Rect(5, 12, 22, 3, new Color(1f, 0.85f, 0.35f));
                for (int i = 0; i < 6; i++) P(7 + i * 3, 14, new Color(1f, 0.95f, 0.7f));
                Rect(7, 1, 2, 12, band);
                Rect(23, 1, 2, 12, band);
            }
            // Outline.
            var outlined = (Color[])px.Clone();
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (px[y * W + x].a > 0f) continue;
                bool edge = (x > 0 && px[y * W + x - 1].a > 0) || (x < W - 1 && px[y * W + x + 1].a > 0) || (y > 0 && px[(y - 1) * W + x].a > 0) || (y < H - 1 && px[(y + 1) * W + x].a > 0);
                if (edge) outlined[y * W + x] = ink;
            }
            for (int i = 0; i < outlined.Length; i++) if (outlined[i].a > 0f) outlined[i].a = 1f;
            var tex = GameAssets.NewTexture(W, H);
            tex.SetPixels(outlined);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.05f), 32);
            sprites[k] = s;
            return s;
        }
    }
}
