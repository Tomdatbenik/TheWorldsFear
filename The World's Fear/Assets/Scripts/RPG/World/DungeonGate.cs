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

        float W => HalfWidth + 0.5f;
        float L => HalfLength + 0.5f;
        float Depth => Steps * Rise;
        Vector2 Mouth => (Vector2)transform.position + Vector2.down * (L + 0.4f);
        int MonsterLevel => areaLevel + LevelBonus;
        bool Plundered => Profile.Data.openedChests.Contains(DoneKey(key));
        Biome LandBiome => WorldGen.BiomeAt(transform.position);
        int Seed => (int)(WorldGen.Hash(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y)) & 0x7FFFFFFF);
        DungeonVariant Variant => DungeonVariant.For(LandBiome, Seed);

        public static string DoneKey(string gateKey) => $"dgn_{gateKey}_done";

        void Start()
        {
            BuildStairs();
            BuildColliders();
            foreach (float side in new[] { -1f, 1f })
                PointLights.Attach(transform, Variant.torch, 6f, 0.8f, new Vector3(side * (W + 0.2f), -L + 0.5f, -1.2f), true);

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
        }

        string Title => Variant.NameFor(LandBiome, Seed);

        void RefreshLabel()
        {
            label.text = Plundered
                ? $"{Title}\n<size=70%><color=#bbb>Plundered</color></size>"
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

            steps.Build(transform, "Steps", stone);
            walls.Build(transform, "Well", stone);
            rim.Build(transform, "Kerb", stone, true);

            var door = new StoneQuads { darkDepth = 0.001f };
            door.Add(new Vector3(-1.5f, l - 0.03f, Depth), Vector3.right, up, Vector3.down, 3f, Depth + PortalHeight * 0.75f);
            var doorObject = door.Build(transform, "Doorway", stone);
            if (doorObject != null) doorObject.GetComponent<MeshRenderer>().sharedMaterial = BlackMaterial();
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
                if (Plundered)
                    GameUI.Instance?.Toast("Nothing left below but dust.", new Color(0.8f, 0.8f, 0.85f), 3f);
                else
                    ConfirmDialog.Show(
                        $"Descend into {Title}?\n<size=70%>{Variant.kind}: {Variant.tagline}.\nMonster level {MonsterLevel}, stronger than the land above. You are level {Profile.Data.level}.</size>",
                        "Descend", () => Descend(hero));
            }
            if (!near) prompted = false;
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
