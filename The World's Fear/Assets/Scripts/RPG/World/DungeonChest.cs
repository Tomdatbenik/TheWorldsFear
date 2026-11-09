using System;
using TMPro;
using UnityEngine;

namespace TWF
{
    // A chest inside a dungeon. It opens once its room is clear and pays out once per hero.
    public class DungeonChest : MonoBehaviour
    {
        const float Reach = 2.4f;
        const int NormalTier = 5, VaultTier = 10;   // ChallengeChest's colour tiers

        public string key;
        public int level;
        public bool vault;
        public Func<bool> roomClear;
        public Action opened;

        SpriteRenderer sprite;
        TextMeshPro label;
        Light glow;
        bool isOpen, inRange, lockedLast;

        int Tier => vault ? VaultTier : NormalTier;

        void Start()
        {
            isOpen = Profile.Data.openedChests.Contains(key);
            var bb = Billboard.Create(transform, "Visual");
            var go = new GameObject("Chest");
            go.transform.SetParent(bb, false);
            go.transform.localScale = Vector3.one * (vault ? 1.9f : 1.35f);
            sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = ChallengeChest.ChestSprite(isOpen, Tier);
            RenderAssets.MakeLit(sprite);
            var shadow = GameAssets.CreateWorldSprite("Shadow", GameAssets.SoftCircle, transform, new Color(0, 0, 0, 0.35f), SortOrder.Units - 1, 1.6f);
            shadow.transform.localScale = new Vector3(shadow.transform.localScale.x, shadow.transform.localScale.y * 0.5f, 1f);
            shadow.transform.localPosition = new Vector3(0, 0, Ground.DecalZ);
            if (!isOpen) glow = PointLights.Attach(transform, ChallengeChest.TierColor(Tier), 4.5f, 1.5f, new Vector3(0, 0, -1f), true);
            gameObject.layer = LayerMask.NameToLayer("Walls");
            gameObject.AddComponent<BoxCollider2D>().size = new Vector2(vault ? 1.8f : 1.2f, 0.7f);

            label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(Billboard.Create(transform), false);
            label.transform.localPosition = new Vector3(0, vault ? 2.5f : 2f, 0);
            label.font = GameAssets.Font;
            label.fontSize = 2.4f;
            label.alignment = TextAlignmentOptions.Center;
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 5;
            label.rectTransform.sizeDelta = new Vector2(8, 2);
            RefreshLabel();
        }

        void RefreshLabel()
        {
            label.color = isOpen ? new Color(0.7f, 0.7f, 0.7f) : ChallengeChest.TierColor(Tier);
            string name = vault ? "Vault" : "Chest";
            label.text = isOpen ? $"Opened {name.ToLower()}" : roomClear() ? name : $"{name}\n<size=70%>Clear the room</size>";
            lockedLast = !roomClear();
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null || GameUI.Instance == null) return;
            if (!isOpen && lockedLast == roomClear()) RefreshLabel();
            bool near = !isOpen && roomClear() && Vector2.Distance(hero.transform.position, transform.position) < Reach;
            if (near && !inRange) GameUI.Instance.ShowInteract("Open", Open);
            else if (!near && inRange) GameUI.Instance.HideInteract();
            inRange = near;
        }

        void Open()
        {
            if (isOpen) return;
            isOpen = true;
            inRange = false;
            GameUI.Instance?.HideInteract();
            Profile.Data.openedChests.Add(key);
            sprite.sprite = ChallengeChest.ChestSprite(true, Tier);
            if (glow != null) Destroy(glow.gameObject);
            RefreshLabel();
            Sound.Play(Sfx.LevelUp, transform.position, 0.7f);
            VFX.Burst(transform.position, Palette.Gold, vault ? 60 : 30, vault ? 3f : 2f);
            DropLoot();
            Profile.Save();
            opened?.Invoke();
        }

        void DropLoot()
        {
            var rng = new System.Random(key.GetHashCode() ^ Environment.TickCount);
            Vector2 at = transform.position + Vector3.down * 1.4f;
            Vector2 Spot() => at + UnityEngine.Random.insideUnitCircle * 1.8f;

            int items = vault ? 5 : 2;
            for (int i = 0; i < items; i++)
            {
                var rarity = vault && i == 0 ? Rarity.Legendary : rng.NextDouble() < (vault ? 0.4 : 0.2) ? Rarity.Legendary : Rarity.Rare;
                Pickup.SpawnItem(Spot(), ItemGenerator.Generate(level + (vault ? 2 : 1), rarity, ItemGenerator.RandomSlot(rng), rng));
            }
            Pickup.SpawnGold(Spot(), level * (vault ? 90 : 25));
            for (int i = 0; i < (vault ? 2 : 1); i++) Pickup.SpawnGlyph(Spot(), SkillParts.Roll(rng, level + 10, MonsterRank.Rare));
            if (vault || rng.NextDouble() < 0.5) Pickup.SpawnPotion(Spot());
            if (vault) Profile.AddXP(Profile.MonsterXP(level, MonsterRank.Rare) * 6);
            GameUI.Instance?.Toast(vault ? "The vault is yours!" : "Chest opened", Palette.Gold, 2.5f);
        }
    }
}
