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
            this.label = label;
            RefreshLabel();
            label.fontSize = 3f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.55f, 0.4f);
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            label.sortingOrder = SortOrder.Text - 5;
            label.rectTransform.sizeDelta = new Vector2(10, 2);
        }

        TextMeshPro label;
        bool sealedShown;

        BossKind Kind => WorldManager.KindForGate(transform.position);
        bool Sealed => !WorldManager.IsUnlocked(Kind);
        string SealedText => $"Sealed: first defeat {BossTitan.BossName(WorldManager.Previous(Kind))}";

        void RefreshLabel()
        {
            sealedShown = Sealed;
            if (sealedShown)
                label.text = $"{BossTitan.LairName(Kind)}\n<size=70%><color=#bbb>{SealedText}</color></size>";
            else
                label.text = BossTitan.LairName(Kind) + $"\n<size=70%>{BossTitan.BossName(Kind)} · Recommended level {recommendedLevel}</size>";
        }

        bool Defeated => Profile.Data.defeatedGates.Contains(WorldManager.GateKey(transform.position));

        void Update()
        {
            var hero = HeroController.Instance;
            if (hero == null) return;
            if (Defeated)
            {
                // A beaten gate stays as a quiet marker.
                if (transform.childCount > 1 && transform.GetChild(1).gameObject.activeSelf)
                {
                    transform.GetChild(0).gameObject.SetActive(false);
                    transform.GetChild(1).gameObject.SetActive(false);
                    var label = GetComponentInChildren<TMPro.TextMeshPro>();
                    if (label) label.text = BossTitan.BossName(Kind) + ", vanquished";
                }
                return;
            }
            if (label != null && sealedShown != Sealed) RefreshLabel();
            if (Sealed)
            {
                // Sealed: a dim, still portal that won't let you in.
                transform.GetChild(0).gameObject.SetActive(false);
                bool near = Vector2.Distance(hero.transform.position, transform.position) < 1.3f;
                if (near && !prompted)
                {
                    prompted = true;
                    GameUI.Instance?.Toast($"Sealed. First defeat {BossTitan.BossName(WorldManager.Previous(Kind))}.", new Color(1f, 0.5f, 0.35f), 3.5f);
                }
                if (!near) prompted = false;
                return;
            }
            transform.GetChild(0).gameObject.SetActive(true);
            transform.GetChild(1).Rotate(0, 0, 40f * Time.deltaTime);
            bool inside = Vector2.Distance(hero.transform.position, transform.position) < 1.3f;
            if (inside && !prompted)
            {
                prompted = true;
                ConfirmDialog.Show($"Enter the boss arena?\n<size=70%>Recommended level {recommendedLevel}. You are level {Profile.Data.level}.</size>",
                    "Enter", () => WorldManager.Instance.EnterBoss(recommendedLevel, transform.position));
            }
            if (!inside) prompted = false;
        }
    }
}
