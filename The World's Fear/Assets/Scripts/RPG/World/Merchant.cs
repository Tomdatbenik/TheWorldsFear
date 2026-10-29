using TMPro;
using UnityEngine;

namespace TWF
{
    // A shopkeeper in the village. Walk up to them and tap "Shop".
    public class Merchant : MonoBehaviour
    {
        public string displayName = "Merchant";
        public string subtitle = "Shop";
        public string buttonLabel = "Shop";
        public PanelKind panel = PanelKind.Shop;
        public Outfit outfit = Outfit.Merchant;
        public float talkRange = 2.2f;

        bool inRange;

        void Start()
        {
            var visual = Billboard.Create(transform, "Visual");
            var sr = visual.gameObject.AddComponent<SpriteRenderer>();
            sr.sortingOrder = SortOrder.Units;
            RenderAssets.MakeLit(sr);
            var anim = visual.gameObject.AddComponent<CharacterAnimator>();
            anim.outfit = outfit;
            anim.Facing = Vector2.down;

            var shadow = GameAssets.CreateWorldSprite("Shadow", GameAssets.SoftCircle, transform, new Color(0, 0, 0, 0.35f), SortOrder.Units - 1, 1f);
            shadow.transform.localScale = new Vector3(shadow.transform.localScale.x, shadow.transform.localScale.y * 0.4f, 1f);

            var col = gameObject.AddComponent<CapsuleCollider2D>();
            col.direction = CapsuleDirection2D.Horizontal;
            col.size = new Vector2(0.7f, 0.45f);
            col.offset = new Vector2(0, 0.15f);

            var label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(Billboard.Create(transform), false);
            label.transform.localPosition = new Vector3(0, 2.2f, 0);
            label.font = GameAssets.Font;
            label.text = $"{displayName}\n<size=70%><color=#ffd040>{subtitle}</color></size>";
            label.fontSize = 2.6f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.9f, 0.7f);
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 5;
            label.rectTransform.sizeDelta = new Vector2(8, 2);
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null || GameUI.Instance == null) return;
            bool near = Vector2.Distance(hero.transform.position, transform.position) < talkRange;
            if (near && !inRange) GameUI.Instance.ShowInteract(buttonLabel, () =>
            {
                CraftPanel.AtForge = panel == PanelKind.Craft;
                Panels.Toggle(panel);
            });
            else if (!near && inRange) GameUI.Instance.HideInteract();
            inRange = near;
        }
    }
}
