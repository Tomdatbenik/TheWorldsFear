using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // The mystic's reforge: choose an item, choose one affix, pay, then keep the old roll or take a new one.
    public class ReforgePanel : GamePanel
    {
        RectTransform itemList, affixList, optionList;
        TextMeshProUGUI itemTitle, costText, goldText, help;
        Button rollButton;
        ItemInstance selected;
        int affixIndex = -1;
        List<RolledAffix> pending;   // new rolls waiting for a choice

        public static ReforgePanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<ReforgePanel>(root, "Mystic's Reforge", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            // Left: your items (equipped first), scrolling.
            var left = UIFactory.Panel("Items", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            left.rectTransform.anchorMin = new Vector2(0, 0);
            left.rectTransform.anchorMax = new Vector2(0.34f, 1);
            left.rectTransform.offsetMin = left.rectTransform.offsetMax = Vector2.zero;
            var view = UIFactory.Rect("View", left.transform).Stretch(14);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            itemList = UIFactory.Rect("List", view);
            itemList.anchorMin = new Vector2(0, 1);
            itemList.anchorMax = new Vector2(1, 1);
            itemList.pivot = new Vector2(0.5f, 1);
            itemList.sizeDelta = Vector2.zero;
            var v = itemList.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 8;
            v.childControlHeight = false;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;
            itemList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = itemList;

            // Middle: the chosen item's affixes.
            var mid = UIFactory.Panel("Affixes", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            mid.rectTransform.anchorMin = new Vector2(0.355f, 0);
            mid.rectTransform.anchorMax = new Vector2(0.68f, 1);
            mid.rectTransform.offsetMin = mid.rectTransform.offsetMax = Vector2.zero;
            itemTitle = UIFactory.Text("Title", mid.transform, "", 30, Palette.Text, TextAlignmentOptions.TopLeft);
            itemTitle.rectTransform.anchorMin = new Vector2(0, 1);
            itemTitle.rectTransform.anchorMax = new Vector2(1, 1);
            itemTitle.rectTransform.pivot = new Vector2(0.5f, 1);
            itemTitle.rectTransform.sizeDelta = new Vector2(-40, 110);
            itemTitle.rectTransform.anchoredPosition = new Vector2(0, -16);
            itemTitle.enableAutoSizing = true;
            itemTitle.fontSizeMin = 16;
            itemTitle.fontSizeMax = 30;
            affixList = UIFactory.Rect("List", mid.transform).Stretch(20);
            affixList.offsetMax = new Vector2(-20, -140);
            var av = affixList.gameObject.AddComponent<VerticalLayoutGroup>();
            av.spacing = 10;
            av.childControlHeight = false;
            av.childForceExpandHeight = false;
            av.childControlWidth = true;
            av.childForceExpandWidth = true;

            // Right: cost, reroll, and the choice between old and new rolls.
            var right = UIFactory.Panel("Reforge", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            right.rectTransform.anchorMin = new Vector2(0.695f, 0);
            right.rectTransform.anchorMax = new Vector2(1, 1);
            right.rectTransform.offsetMin = right.rectTransform.offsetMax = Vector2.zero;
            help = UIFactory.Text("Help", right.transform, "", 22, Palette.MutedText, TextAlignmentOptions.TopLeft);
            help.rectTransform.anchorMin = new Vector2(0, 1);
            help.rectTransform.anchorMax = new Vector2(1, 1);
            help.rectTransform.pivot = new Vector2(0.5f, 1);
            help.rectTransform.sizeDelta = new Vector2(-40, 150);
            help.rectTransform.anchoredPosition = new Vector2(0, -16);
            help.enableAutoSizing = true;
            help.fontSizeMin = 14;
            help.fontSizeMax = 22;
            optionList = UIFactory.Rect("Options", right.transform).Stretch(20);
            optionList.offsetMax = new Vector2(-20, -180);
            optionList.offsetMin = new Vector2(20, 230);
            var ov = optionList.gameObject.AddComponent<VerticalLayoutGroup>();
            ov.spacing = 12;
            ov.childControlHeight = false;
            ov.childForceExpandHeight = false;
            ov.childControlWidth = true;
            ov.childForceExpandWidth = true;
            costText = UIFactory.Text("Cost", right.transform, "", 26, Palette.Gold, TextAlignmentOptions.Left);
            costText.rectTransform.Place(new Vector2(0, 0), new Vector2(20, 170), new Vector2(560, 40));
            goldText = UIFactory.Text("Gold", right.transform, "", 24, Palette.MutedText, TextAlignmentOptions.Left);
            goldText.rectTransform.Place(new Vector2(0, 0), new Vector2(20, 130), new Vector2(560, 36));
            rollButton = UIFactory.Button("Reforge", right.transform, "Reforge", OnRoll, new Color(0.35f, 0.2f, 0.45f, 1f), 30);
            ((RectTransform)rollButton.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(420, 84));
        }

        public override void Close()
        {
            // Closing while a choice is open keeps the old affix.
            if (pending != null && selected != null) Reforge.Choose(selected, affixIndex, null);
            pending = null;
            base.Close();
        }

        IEnumerable<ItemInstance> Items()
        {
            foreach (var i in Profile.Data.equipment) if (i != null && i.affixes.Count > 0) yield return i;
            foreach (var i in Profile.Data.inventory) if (i != null && i.affixes.Count > 0) yield return i;
        }

        public override void Refresh()
        {
            foreach (Transform c in itemList) Destroy(c.gameObject);
            foreach (Transform c in affixList) Destroy(c.gameObject);
            foreach (Transform c in optionList) Destroy(c.gameObject);
            goldText.text = $"You have {Profile.Data.gold} gold";

            foreach (var item in Items())
            {
                var it = item;
                bool equipped = Profile.Data.equipment.Contains(item);
                var b = UIFactory.Button("Item", itemList,
                    $"<color={Palette.Hex(RarityInfo.Color(item.rarity))}>{item.name}</color>{(equipped ? "  <size=70%><color=#8f8>worn</color></size>" : "")}",
                    () => { if (pending != null) return; selected = it; affixIndex = it.reforgedAffix; Refresh(); },
                    item == selected ? new Color(0.3f, 0.25f, 0.12f, 1f) : new Color(0.16f, 0.14f, 0.16f, 1f), 22);
                ((RectTransform)b.transform).sizeDelta = new Vector2(0, 64);
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                t.enableAutoSizing = true;
                t.fontSizeMin = 14;
                t.fontSizeMax = 22;
            }

            if (selected == null || (!Profile.Data.inventory.Contains(selected) && !Profile.Data.equipment.Contains(selected)))
            {
                selected = null;
                itemTitle.text = "<color=#aaa>Choose an item to reforge.</color>";
                help.text = "Pick one property of an item and reroll it. You then choose: keep what you had, or take one of two new rolls.\n\n" +
                            "Once you reforge a property, it is the only one on that item you can reforge again. Each reforge costs more.";
                costText.text = "";
                rollButton.interactable = false;
                return;
            }

            itemTitle.text = $"<color={Palette.Hex(RarityInfo.Color(selected.rarity))}><b>{selected.name}</b></color>\n<size=70%><color=#999>{selected.rarity} {selected.slot} · Item level {selected.itemLevel}" +
                             $"{(selected.reforgeCount > 0 ? $" · reforged {selected.reforgeCount}x" : "")}</color></size>";
            for (int i = 0; i < selected.affixes.Count; i++)
            {
                int idx = i;
                bool allowed = Reforge.CanReforge(selected, i);
                string lockNote = allowed ? "" : "  <size=70%><color=#888>(locked)</color></size>";
                var b = UIFactory.Button("Affix", affixList, $"{selected.affixes[i].Describe()}{lockNote}",
                    () => { if (pending != null || !Reforge.CanReforge(selected, idx)) return; affixIndex = idx; Refresh(); },
                    i == affixIndex ? new Color(0.35f, 0.22f, 0.45f, 1f) : allowed ? new Color(0.16f, 0.14f, 0.18f, 1f) : new Color(0.1f, 0.1f, 0.1f, 1f), 24);
                ((RectTransform)b.transform).sizeDelta = new Vector2(0, 66);
                b.interactable = allowed;
            }

            int cost = Reforge.Cost(selected);
            costText.text = $"Cost: {cost} gold";
            if (pending != null)
            {
                help.text = "<color=#fff>Choose what to keep:</color>";
                Option("Keep: " + selected.affixes[affixIndex].Describe(), null, new Color(0.2f, 0.2f, 0.22f, 1f));
                foreach (var o in pending) Option("New: " + o.Describe(), o, new Color(0.25f, 0.18f, 0.35f, 1f));
                rollButton.interactable = false;
            }
            else
            {
                help.text = affixIndex >= 0 ? $"Reforge <color=#c9f>{selected.affixes[affixIndex].Describe()}</color>?" : "Pick a property in the middle.";
                rollButton.interactable = affixIndex >= 0 && Profile.Data.gold >= cost && Reforge.CanReforge(selected, affixIndex);
            }
        }

        void Option(string label, RolledAffix choice, Color color)
        {
            var b = UIFactory.Button("Option", optionList, label, () =>
            {
                Reforge.Choose(selected, affixIndex, choice);
                pending = null;
                if (choice != null) Sound.Play(Sfx.LevelUp, Vector3.zero, 0.35f);
                Refresh();
            }, color, 24);
            ((RectTransform)b.transform).sizeDelta = new Vector2(0, 80);
        }

        void OnRoll()
        {
            if (selected == null || affixIndex < 0) return;
            pending = Reforge.Roll(selected, affixIndex);
            if (pending != null) Sound.Play(Sfx.Cast, Vector3.zero, 0.5f);
            Refresh();
        }
    }
}
