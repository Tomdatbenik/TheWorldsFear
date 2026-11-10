using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    // A stairwell cut into the ground. Walk to the top step, confirm, and the hero walks down into the dungeon.
    public class DungeonGate : MonoBehaviour
    {
        public const int HalfWidth = 2;      // tiles either side of the centre column
        public const int HalfLength = 4;     // tiles either side of the centre row
        public const int LevelBonus = 2;     // the monsters inside are this much above the land around the gate

        const int Steps = 9;
        const float Rise = 0.36f;
        const float RimHeight = 0.6f;
        const float PortalHeight = 2.8f;
        const float ReachRadius = 1.3f;

        public int areaLevel = 10;
        public string key;

        bool prompted, walking;
        TextMeshPro label;
        GameObject steps, well, kerb, doorway, rubble;
        readonly List<Light> lights = new List<Light>();

        float W => HalfWidth + 0.5f;
        float L => HalfLength + 0.5f;
        float Depth => Steps * Rise;
        Vector2 Mouth => (Vector2)transform.position + Vector2.down * (L + 0.4f);
        int MonsterLevel => areaLevel + LevelBonus;
        // Cleared once its warden falls (or every chest is opened); after that the way down caves in for good.
        bool Cleared => Profile.Data.openedChests.Contains(DoneKey(key));
        bool Fallen => Profile.Data.openedChests.Contains(FallenKey(key));
        Biome LandBiome => WorldGen.BiomeAt(transform.position);
        int Seed => (int)(WorldGen.Hash(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y)) & 0x7FFFFFFF);
        DungeonVariant Variant => DungeonVariant.For(LandBiome, Seed);

        public static string DoneKey(string gateKey) => $"dgn_{gateKey}_done";
        static string FallenKey(string gateKey) => $"dgn_{gateKey}_fallen";

        void Start()
        {
            BuildStairs();
            BuildColliders();
            foreach (float side in new[] { -1f, 1f })
            {
                var light = PointLights.Attach(transform, Variant.torch, 6f, 0.8f, new Vector3(side * (W + 0.2f), -L + 0.5f, -1.2f), true);
                if (light != null) lights.Add(light);
            }

            label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(Billboard.Create(transform), false);
            label.transform.localPosition = new Vector3(0f, 4.6f, 0f);
            label.font = GameAssets.Font;
            label.fontSize = 3f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.75f, 0.85f, 1f);
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 5;
            label.rectTransform.sizeDelta = new Vector2(12f, 2f);
            RefreshLabel();

            if (Cleared)
            {
                if (Fallen) Collapse();
                else StartCoroutine(Crumble());
            }
        }

        string Title => Variant.NameFor(LandBiome, Seed);

        void RefreshLabel()
        {
            label.text = Cleared
                ? $"{Title}\n<size=70%><color=#bbb>Collapsed</color></size>"
                : $"{Title}\n<size=70%>{Variant.kind} · Monster level {MonsterLevel}</size>";
        }

        void BuildStairs()
        {
            var stone = StoneQuads.Stone(Variant.wall);
            var steps = new StoneQuads { darkDepth = Depth * 1.7f };
            var walls = new StoneQuads { darkDepth = Depth * 1.7f };
            var rim = new StoneQuads();
            float w = W, l = L;

            for (int i = 0; i < Steps; i++)
            {
                float y = -l + i, z = (i + 1) * Rise;
                steps.Add(new Vector3(-w, y, i * Rise), Vector3.right, Vector3.forward, Vector3.down, 2f * w, Rise);
                steps.Add(new Vector3(-w, y, z), Vector3.right, Vector3.up, Vector3.back, 2f * w, 1f);

                // The sides of the well, down to this step.
                walls.Add(new Vector3(-w, y, 0f), Vector3.up, Vector3.forward, Vector3.right, 1f, z);
                walls.Add(new Vector3(w, y, 0f), Vector3.up, Vector3.forward, Vector3.left, 1f, z);
            }

            // The back wall, opening onto a black doorway.
            walls.Add(new Vector3(-w, l, 0f), Vector3.right, Vector3.forward, Vector3.down, 2f * w, Depth);

            // A low stone kerb around the well and a portal at its far end, so it can be seen from a distance.
            var up = Vector3.back;
            foreach (float side in new[] { -1f, 1f })
            {
                var near = new Vector3(side * w, -l, 0f);
                var far = new Vector3(side * (w + 0.5f), -l, 0f);
                rim.Add(near, Vector3.up, up, new Vector3(-side, 0f, 0f), 2f * l, RimHeight);
                rim.Add(far, Vector3.up, up, new Vector3(side, 0f, 0f), 2f * l, RimHeight);
                rim.Add(near + up * RimHeight, Vector3.up, new Vector3(side, 0f, 0f), up, 2f * l, 0.5f);
                rim.Add(new Vector3(Mathf.Min(side * w, far.x), -l, 0f), Vector3.right, up, Vector3.down, 0.5f, RimHeight);
            }
            float outer = w + 0.5f;
            rim.Add(new Vector3(-outer, l, 0f), Vector3.right, up, Vector3.down, 2f * outer, PortalHeight);
            rim.Add(new Vector3(-outer, l + 0.5f, 0f), Vector3.right, up, Vector3.up, 2f * outer, PortalHeight);
            rim.Add(new Vector3(-outer, l, -PortalHeight), Vector3.right, Vector3.up, up, 2f * outer, 0.5f);
            rim.Add(new Vector3(-outer, l, 0f), Vector3.up, up, Vector3.left, 0.5f, PortalHeight);
            rim.Add(new Vector3(outer, l, 0f), Vector3.up, up, Vector3.right, 0.5f, PortalHeight);

            this.steps = steps.Build(transform, "Steps", stone);
            well = walls.Build(transform, "Well", stone);
            kerb = rim.Build(transform, "Kerb", stone, true);

            var door = new StoneQuads { darkDepth = 0.001f };
            door.Add(new Vector3(-1.5f, l - 0.03f, Depth), Vector3.right, up, Vector3.down, 3f, Depth + PortalHeight * 0.75f);
            doorway = door.Build(transform, "Doorway", stone);
            if (doorway != null) doorway.GetComponent<MeshRenderer>().sharedMaterial = BlackMaterial();
        }

        static Material black;

        // Shares the stone material's shader, with no light on it at all.
        static Material BlackMaterial()
        {
            if (black != null) return black;
            black = new Material(StoneQuads.Stone("stone_wall_tile")) { name = "Doorway", color = Color.black };
            black.SetFloat("_Emission", 0f);
            return black;
        }

        // The whole well is solid: nothing wanders in, and the hero only goes down by choosing to.
        void BuildColliders()
        {
            var solid = new GameObject("Well Collider");
            solid.transform.SetParent(transform, false);
            solid.layer = LayerMask.NameToLayer("Walls");
            var box = solid.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2f * W + 1f, 2f * L);
            var solidBody = solid.AddComponent<Rigidbody2D>();
            solidBody.bodyType = RigidbodyType2D.Static;
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null || walking || ConfirmDialog.IsOpen) return;
            bool near = Vector2.Distance(hero.transform.position, Mouth) < ReachRadius;
            if (near && !prompted)
            {
                prompted = true;
                if (Cleared)
                    GameUI.Instance?.Toast("The way down has caved in.", new Color(0.8f, 0.8f, 0.85f), 3f);
                else
                    ConfirmDialog.Show(
                        $"Descend into {Title}?\n<size=70%>{Variant.kind}: {Variant.tagline}.\nMonster level {MonsterLevel}, stronger than the land above. You are level {Profile.Data.level}.</size>",
                        "Descend", () => Descend(hero));
            }
            if (!near) prompted = false;
        }

        // ---------- Caving in ----------

        const float CrumbleSeconds = 2.6f;
        const float CrumbleViewRange = 25f;   // further than this from the hero, it just falls in unseen
        static readonly Color Dust = new Color(0.55f, 0.5f, 0.44f);

        // The first time the hero is back on the surface after clearing it: the gate shakes, the portal sinks
        // and rubble fills the well.
        IEnumerator Crumble()
        {
            yield return new WaitForSeconds(1f);   // let the scene fade in first
            var hero = HeroController.Instance;
            if (hero == null || Vector2.Distance(hero.transform.position, transform.position) > CrumbleViewRange)
            {
                Collapse();
                yield break;
            }

            BuildRubble();
            var kerbStart = kerb != null ? kerb.transform.localPosition : Vector3.zero;
            var lightStart = new List<float>();
            foreach (var l in lights) lightStart.Add(l != null ? l.intensity : 0f);
            GameUI.Instance?.Toast($"{Title} caves in behind you.", new Color(0.8f, 0.8f, 0.85f), 3f);

            float t = 0f, nextDust = 0f;
            while (t < CrumbleSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / CrumbleSeconds);
                float fall = k * k * (3f - 2f * k);
                float jitter = (1f - k) * 0.07f;
                if (kerb != null)
                    kerb.transform.localPosition = kerbStart + new Vector3(Random.Range(-jitter, jitter), 0f, (PortalHeight + RimHeight) * fall);
                if (rubble != null) rubble.transform.localPosition = new Vector3(0f, 0f, Depth * (1f - fall));
                for (int i = 0; i < lights.Count; i++)
                    if (lights[i] != null) lights[i].intensity = lightStart[i] * (1f - fall);
                if (t >= nextDust)
                {
                    nextDust = t + 0.12f;
                    var at = (Vector2)transform.position + new Vector2(Random.Range(-W, W), Random.Range(-L, L));
                    VFX.Burst(at, Dust, 6, 0.9f);
                    CameraFollow.Shake(0.18f * (1f - k) + 0.04f);
                }
                yield return null;
            }
            for (int i = 0; i < 4; i++)
                VFX.Burst((Vector2)transform.position + new Vector2(Random.Range(-W, W), Random.Range(-L, L)), Dust, 10, 1.2f);
            Collapse();
        }

        // What is left once it has fallen in: a sealed mound of rubble, no light, no way down.
        void Collapse()
        {
            foreach (var part in new[] { steps, well, kerb, doorway })
                if (part != null) part.SetActive(false);
            foreach (var l in lights)
                if (l != null) l.enabled = false;
            BuildRubble();
            rubble.transform.localPosition = Vector3.zero;
            if (!Fallen)
            {
                Profile.Data.openedChests.Add(FallenKey(key));
                Profile.Save();
            }
        }

        void BuildRubble()
        {
            if (rubble != null) return;
            var rng = new System.Random(Seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var q = new StoneQuads();
            var up = Vector3.back;
            const float Ground = -0.05f;
            float w = W + 0.5f, l = L;

            // A flat fill over the whole hole, then loose blocks heaped on it.
            q.Add(new Vector3(-w, -l, Ground), Vector3.right, Vector3.up, up, 2f * w, 2f * l);
            for (int i = 0; i < 16; i++)
            {
                float sx = R(0.6f, 1.5f), sy = R(0.5f, 1.3f), h = R(0.25f, 0.95f);
                float x = R(-w, w - sx), y = R(-l, l - sy), top = Ground - h;
                q.Add(new Vector3(x, y, top), Vector3.right, Vector3.up, up, sx, sy);
                q.Add(new Vector3(x, y, top), Vector3.right, Vector3.forward, Vector3.down, sx, h);
                q.Add(new Vector3(x, y + sy, top), Vector3.right, Vector3.forward, Vector3.up, sx, h);
                q.Add(new Vector3(x, y, top), Vector3.up, Vector3.forward, Vector3.left, sy, h);
                q.Add(new Vector3(x + sx, y, top), Vector3.up, Vector3.forward, Vector3.right, sy, h);
            }
            rubble = q.Build(transform, "Rubble", StoneQuads.Stone(Variant.wall), true);
        }

        void Descend(HeroController hero)
        {
            walking = true;
            Panels.CloseAll();
            Vector2 centre = transform.position;
            var biome = WorldGen.BiomeAt(centre);
            var cell = new Vector2Int(Mathf.FloorToInt(centre.x), Mathf.FloorToInt(centre.y));
            hero.StartCoroutine(DungeonTravel.WalkStairs(hero, centre + Vector2.down * L, centre + Vector2.up * (L - 0.6f), 0f, Depth,
                () => WorldManager.Instance.EnterDungeon(MonsterLevel, key, Title, biome, WorldGen.RegionAt(centre), WorldGen.Hash(cell.x, cell.y), Mouth + Vector2.down)));
        }
    }
}
