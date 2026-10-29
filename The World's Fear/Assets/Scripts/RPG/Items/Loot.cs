using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    public static class Loot
    {
        static readonly System.Random rng = new System.Random();

        public static void Drop(Combatant monster, Vector2 at)
        {
            int level = monster.level;
            var rank = monster.rank;

            float goldChance = rank == MonsterRank.Normal ? 0.45f : 1f;
            if (rng.NextDouble() < goldChance)
            {
                float mult = rank == MonsterRank.Elite ? 3f : rank == MonsterRank.Rare ? 6f : rank == MonsterRank.Boss ? 40f : 1f;
                int amount = Mathf.Max(1, Mathf.RoundToInt(level * (2f + (float)rng.NextDouble() * 3f) * mult));
                Pickup.SpawnGold(at + Random.insideUnitCircle * 0.5f, amount);
            }

            double potionChance = rank == MonsterRank.Normal ? 0.05 : 0.3;
            if (rng.NextDouble() < potionChance) Pickup.SpawnPotion(at + Random.insideUnitCircle * 0.8f);

            // Skill parts for crafting.
            double glyphChance = rank == MonsterRank.Normal ? 0.06 : rank == MonsterRank.Elite ? 0.25 : rank == MonsterRank.Rare ? 0.6 : 1.0;
            if (rng.NextDouble() < glyphChance) Pickup.SpawnGlyph(at + Random.insideUnitCircle * 0.8f, SkillParts.Roll(rng, level, rank));

            int items = 0;
            switch (rank)
            {
                case MonsterRank.Normal: items = rng.NextDouble() < 0.09 ? 1 : 0; break;
                case MonsterRank.Elite: items = rng.NextDouble() < 0.4 ? 1 : 0; break;
                case MonsterRank.Rare: items = rng.NextDouble() < 0.5 ? 2 : 1; break;
                case MonsterRank.Boss: items = 4; break;
            }
            for (int i = 0; i < items; i++)
            {
                var rarity = rank == MonsterRank.Boss && i == 0 ? Rarity.Legendary : ItemGenerator.RollRarity(rng, rank, WorldGen.MagicFind(level));
                var item = ItemGenerator.Generate(level + (rank == MonsterRank.Normal ? 0 : 1), rarity, ItemGenerator.RandomSlot(rng), rng);
                Pickup.SpawnItem(at + Random.insideUnitCircle * 1.2f, item);
            }
        }
    }

    // Loot lying on the ground. Walk over it to collect.
    public class Pickup : MonoBehaviour
    {
        public ItemInstance item;
        public int gold;
        public bool potion;
        public PartRef glyph;

        float age;
        Transform visual;
        SpriteRenderer beam;
        bool collecting;

        public static void SpawnGold(Vector2 at, int amount)
        {
            var go = new GameObject("Gold");
            go.transform.position = at;
            var p = go.AddComponent<Pickup>();
            p.gold = amount;
            var sr = GameAssets.CreateWorldSprite("coin", GameAssets.Circle, Billboard.Create(go.transform), Palette.Gold, SortOrder.Pickup, 0.35f);
            GameAssets.CreateWorldSprite("shine", GameAssets.SoftCircle, go.transform, new Color(1f, 0.9f, 0.4f, 0.35f), SortOrder.Pickup - 1, 0.8f);
            p.visual = sr.transform;
        }

        public static void SpawnGlyph(Vector2 at, PartRef part)
        {
            var go = new GameObject("Glyph " + part.FullName);
            go.transform.position = at;
            var p = go.AddComponent<Pickup>();
            p.glyph = part;
            Color c = part.kind == PartKind.Element ? SkillParts.ElementColor((ElementPart)part.id) : SkillParts.KindColor(part.kind);
            var bb = Billboard.Create(go.transform);
            var sr = GameAssets.CreateWorldSprite("glyph", IconFactory.Get(SkillParts.Icon(part.kind, part.id), c), bb, Color.white, SortOrder.Pickup + 1, 0.7f);
            RenderAssets.MakeLit(sr, true, RenderAssets.Instance != null ? RenderAssets.Instance.litEmissive : null);
            p.visual = sr.transform;
            PointLights.Attach(go.transform, c, 2.5f, 1.2f, new Vector3(0, 0, -0.8f), true);
        }

        public static void SpawnPotion(Vector2 at)
        {
            var go = new GameObject("Potion");
            go.transform.position = at;
            var p = go.AddComponent<Pickup>();
            p.potion = true;
            var sr = GameAssets.CreateWorldSprite("flask", PotionIcon, Billboard.Create(go.transform), Color.white, SortOrder.Pickup, 0.7f);
            p.visual = sr.transform;
        }

        public static Sprite PotionIcon => GameAssets.Make("potion_icon", 10, 12, (x, y, w, h) =>
        {
            bool neck = y >= 8 && x >= 3 && x <= 6;
            bool cork = y >= 10 && x >= 3 && x <= 6;
            float dx = x - 4.5f, dy = y - 4f;
            bool bulb = dx * dx + dy * dy <= 16f;
            bool edge = bulb && dx * dx + dy * dy > 10f;
            if (cork) return new Color(0.55f, 0.36f, 0.2f);
            if (neck) return new Color(0.8f, 0.85f, 0.9f, 0.9f);
            if (edge) return new Color(0.5f, 0.05f, 0.08f);
            if (bulb) return y > 5 ? new Color(0.95f, 0.25f, 0.3f) : new Color(0.8f, 0.1f, 0.15f);
            return Color.clear;
        }, 16, FilterMode.Point);

        public static void SpawnItem(Vector2 at, ItemInstance item)
        {
            var go = new GameObject("Item " + item.name);
            go.transform.position = at;
            var p = go.AddComponent<Pickup>();
            p.item = item;
            Color c = RarityInfo.Color(item.rarity);

            var bb = Billboard.Create(go.transform);
            var icon = GameAssets.CreateWorldSprite("icon", ItemIcons.Get(item), bb, Color.white, SortOrder.Pickup + 1, 0.8f);
            RenderAssets.MakeLit(icon, true, RenderAssets.Instance != null ? RenderAssets.Instance.litEmissive : null);
            if (item.rarity >= Rarity.Rare) PointLights.Attach(go.transform, c, 3f, 1.5f, new Vector3(0, 0, -1f), true);
            p.visual = icon.transform;
            if (item.rarity >= Rarity.Magic)
            {
                p.beam = GameAssets.CreateWorldSprite("beam", GameAssets.Pixel, bb, new Color(c.r, c.g, c.b, 0.35f), SortOrder.Pickup - 1);
                float unit = GameAssets.Pixel.bounds.size.x;
                float h = item.rarity >= Rarity.Mythic ? 9f : item.rarity == Rarity.Legendary ? 6f : item.rarity == Rarity.Rare ? 4f : 2.5f;
                p.beam.transform.localScale = new Vector3(0.25f / unit, h / unit, 1f);
                p.beam.transform.localPosition = new Vector3(0, h / 2f, 0);
            }

            var label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(bb, false);
            label.transform.localPosition = new Vector3(0, 1.1f, 0);
            label.font = GameAssets.Font;
            label.text = item.name;
            label.fontSize = 2.2f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = c;
            label.outlineWidth = 0.3f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 2;
            label.rectTransform.sizeDelta = new Vector2(8, 1);

            if (item.rarity >= Rarity.Legendary)
            {
                GameUI.Instance?.Toast(item.rarity == Rarity.Mythic ? "A MYTHIC item dropped!" : "A legendary item dropped!", RarityInfo.Color(item.rarity));
                VFX.Ring(at, c, 3f, 0.8f);
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (visual) visual.localPosition = new Vector3(0, 0.4f + Mathf.Sin(age * 3f) * 0.08f, 0);
            if (beam) beam.color = new Color(beam.color.r, beam.color.g, beam.color.b, 0.25f + Mathf.Sin(age * 2f) * 0.1f);

            var hero = HeroController.Instance;
            if (hero == null || hero.Combatant.IsDead || age < 0.4f) return;
            float d = Vector2.Distance(hero.transform.position, transform.position);

            if (gold > 0)
            {
                if (d < 2.6f) collecting = true;
                if (collecting)
                {
                    transform.position = Vector2.MoveTowards(transform.position, hero.transform.position, Time.deltaTime * 12f);
                    if (d < 0.4f)
                    {
                        Profile.AddGold(gold);
                        Sound.Play(Sfx.Coin, transform.position, 0.3f);
                        FloatingText.Spawn(hero.transform.position + View.Up * 1.8f, $"+{gold} gold", Palette.Gold, 0.8f);
                        Destroy(gameObject);
                    }
                }
                return;
            }

            if (glyph != null && d < 1.1f)
            {
                Color c = glyph.kind == PartKind.Element ? SkillParts.ElementColor((ElementPart)glyph.id) : SkillParts.KindColor(glyph.kind);
                GameUI.Instance?.Toast(Crafting.AddPart(glyph), c);
                Sound.Play(Sfx.Pickup, transform.position, 0.5f);
                Profile.Save();
                Destroy(gameObject);
                return;
            }

            if (potion && d < 1f)
            {
                if (Profile.Data.potions < Profile.MaxPotions)
                {
                    Profile.Data.potions++;
                    FloatingText.Spawn(hero.transform.position + View.Up * 1.8f, "+1 Potion", Palette.HP, 0.8f);
                    Sound.Play(Sfx.Pickup, transform.position, 0.4f);
                    Destroy(gameObject);
                }
                return;
            }

            if (item != null && d < 1f)
            {
                if (Profile.AddItem(item))
                {
                    FloatingText.Spawn(hero.transform.position + View.Up * 2.1f, item.name, RarityInfo.Color(item.rarity), 0.9f, 1.2f);
                    Sound.Play(Sfx.Pickup, transform.position, 0.4f);
                    Profile.Save();
                    Destroy(gameObject);
                }
                else if (age > 1f && Mathf.Repeat(age, 3f) < Time.deltaTime * 2f)
                {
                    GameUI.Instance?.Toast("Your bag is full - sell items in the Bag menu", new Color(1f, 0.5f, 0.4f));
                }
            }
        }
    }

    // Procedural pixel icons for items, drawn from small templates and tinted by rarity.
    public static class ItemIcons
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        // o outline, m metal/main, d dark, l light, g gem
        static readonly Dictionary<string, string[]> templates = new Dictionary<string, string[]>
        {
            { "sword", new[] {
                "...........oo",
                "..........olo",
                ".........olmo",
                "........olmo.",
                ".......olmo..",
                "......olmo...",
                "..o..olmo....",
                "..oooomo.....",
                "...odgo......",
                "...oddo......",
                "..odooo......",
                ".odo.........",
                ".oo..........",
            } },
            { "bow", new[] {
                "....ooo......",
                "...odddo.....",
                "...od..ooo...",
                "..od.....oo..",
                "..od......o..",
                ".odd......o..",
                ".ogd.....lo..",
                ".odd......o..",
                "..od......o..",
                "..od.....oo..",
                "...od..ooo...",
                "...odddo.....",
                "....ooo......",
            } },
            { "staff", new[] {
                "........ooo..",
                ".......oglgo.",
                ".......oggo..",
                "......oddo...",
                ".....oddo....",
                "....oddo.....",
                "....odo......",
                "...oddo......",
                "..oddo.......",
                "..odo........",
                ".oddo........",
                ".odo.........",
                ".oo..........",
            } },
            { "helm", new[] {
                ".............",
                "....ooooo....",
                "...olllmmo...",
                "..olmmmmmdo..",
                "..ommmgmmdo..",
                "..ommmmmmdo..",
                "..omoooodmo..",
                "..omo...omo..",
                "..omo...omo..",
                "..oo.....oo..",
                ".............",
                ".............",
                ".............",
            } },
            { "chest", new[] {
                ".............",
                "..oo.....oo..",
                ".olmo...omdo.",
                ".olmmoooomdo.",
                ".olmmmgmmmdo.",
                "..olmmmmmdo..",
                "..olmmmmmdo..",
                "..olmmmmmdo..",
                "..ommmmmmdo..",
                "..oddddddo...",
                "...ooooooo...",
                ".............",
                ".............",
            } },
            { "gloves", new[] {
                ".............",
                "....o.o......",
                "...olololo...",
                "...olmlmlmo..",
                "...olmmmmmo..",
                ".o.ommmmmdo..",
                "olomommmmdo..",
                "olmmmmmmmdo..",
                ".ommmmmmdo...",
                "..oddgdddo...",
                "..ooooooo....",
                ".............",
                ".............",
            } },
            { "boots", new[] {
                ".............",
                "...oooo......",
                "...olmdo.....",
                "...olmdo.....",
                "...olmdo.....",
                "...olmdo.....",
                "...olmmdoo...",
                "..olmmmmmdo..",
                "..ommmmmmmdo.",
                "..ogddddddo..",
                "..oooooooo...",
                ".............",
                ".............",
            } },
            { "ring", new[] {
                ".............",
                ".....ooo.....",
                "....oglgo....",
                "....oggo.....",
                "...ooooooo...",
                "..olmo.omdo..",
                "..olo...odo..",
                "..olo...odo..",
                "..ommo.omdo..",
                "...ommmmdo...",
                "....ooooo....",
                ".............",
                ".............",
            } },
            { "amulet", new[] {
                "..o.......o..",
                "...o.....o...",
                "....o...o....",
                ".....o.o.....",
                "....ooooo....",
                "...olmmmdo...",
                "..olmglgmdo..",
                "..ommgggmdo..",
                "...ommmmdo...",
                "....odddo....",
                ".....ooo.....",
                ".............",
                ".............",
            } },
        };

        public static Sprite Get(ItemInstance item)
        {
            if (item.slot == ItemSlot.Weapon) return Weapons.Sprite((WeaponType)item.weaponType);
            string shape;
            switch (item.slot)
            {
                case ItemSlot.Weapon:
                    shape = item.baseName.Contains("Bow") ? "bow" : item.baseName.Contains("Staff") || item.baseName.Contains("Wand") || item.baseName.Contains("Scepter") ? "staff" : "sword";
                    break;
                default: shape = item.slot.ToString().ToLowerInvariant(); break;
            }
            string key = shape + (int)item.rarity + (item.seed % 5);
            if (cache.TryGetValue(key, out var s) && s) return s;

            var tpl = templates[shape];
            int size = tpl.Length;
            var tex = GameAssets.NewTexture(size, size);
            var rng = new System.Random(item.seed);
            Color metal = item.rarity == Rarity.Legendary ? new Color(0.95f, 0.65f, 0.25f)
                : item.rarity == Rarity.Rare ? new Color(0.9f, 0.85f, 0.45f)
                : item.rarity == Rarity.Magic ? new Color(0.55f, 0.65f, 0.95f)
                : new Color(0.72f, 0.72f, 0.74f);
            if (item.slot == ItemSlot.Chest || item.slot == ItemSlot.Gloves || item.slot == ItemSlot.Boots || item.slot == ItemSlot.Helm)
                if (item.rarity == Rarity.Common) metal = new Color(0.6f, 0.45f, 0.3f);
            Color gem = Color.HSVToRGB((float)rng.NextDouble(), 0.8f, 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                char ch = x < tpl[size - 1 - y].Length ? tpl[size - 1 - y][x] : '.';
                Color c;
                switch (ch)
                {
                    case 'o': c = new Color(0.08f, 0.06f, 0.06f); break;
                    case 'm': c = metal; break;
                    case 'l': c = Color.Lerp(metal, Color.white, 0.5f); break;
                    case 'd': c = Color.Lerp(metal, Color.black, 0.45f); break;
                    case 'g': c = gem; break;
                    default: c = Color.clear; break;
                }
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), GameAssets.WorldPPU);
            cache[key] = s;
            return s;
        }
    }
}
