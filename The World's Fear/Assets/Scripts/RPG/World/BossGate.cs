using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    // Walk into it to enter the boss arena (when you're ready).
    public class BossGate : MonoBehaviour
    {
        public int recommendedLevel = 22;
        bool prompted;

        void Start()
        {
            GameAssets.CreateWorldSprite("Portal", GameAssets.SoftCircle, transform, new Color(0.9f, 0.2f, 0.1f, 0.7f), SortOrder.Ground + 6, 3f);
            GameAssets.CreateWorldSprite("Ring", GameAssets.Ring, transform, new Color(1f, 0.5f, 0.2f, 0.9f), SortOrder.Ground + 7, 3f);
            var label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(Billboard.Create(transform), false);
            label.transform.localPosition = new Vector3(0, 2.4f, 0);
            label.font = GameAssets.Font;
            label.text = $"The Heart of Fear\n<size=70%>Recommended level {recommendedLevel}</size>";
            label.fontSize = 3f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.55f, 0.4f);
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 5;
            label.rectTransform.sizeDelta = new Vector2(10, 2);
        }

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            transform.GetChild(1).Rotate(0, 0, 40f * Time.deltaTime);
            bool inside = Vector2.Distance(hero.transform.position, transform.position) < 1.3f;
            if (inside && !prompted)
            {
                prompted = true;
                ConfirmDialog.Show($"Enter the boss arena?\n<size=70%>Recommended level {recommendedLevel}. You are level {Profile.Data.level}.</size>",
                    "Enter", () => WorldManager.Instance.EnterBoss(recommendedLevel));
            }
            if (!inside) prompted = false;
        }
    }
}
