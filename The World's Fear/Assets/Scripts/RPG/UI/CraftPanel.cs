using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // Element -> Delivery -> Modifiers. At the Arcane Forge you can craft and buy parts;
    // anywhere else you can view and equip your crafted skills.
    public class CraftPanel : GamePanel
    {
        public static bool AtForge;

        RectTransform elementRow, deliveryRow, modRow, craftedList, stockRow;
        TextMeshProUGUI previewTitle, previewBody, stockTitle;
        Button craftButton;
        readonly List<ElementPart> elements = new List<ElementPart>();   // first = main element
        DeliveryPart? delivery;
        readonly List<ModPart> mods = new List<ModPart>();
        string focusText = "";

        public static CraftPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<CraftPanel>(root, "Skill Crafting", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            // The three part sections stack and size to their contents, scrolling if they don't fit.
            var view = UIFactory.Rect("Parts", content);
            view.anchorMin = new Vector2(0, 0.3f);
            view.anchorMax = new Vector2(0.58f, 1);
            view.offsetMin = view.offsetMax = Vector2.zero;
            view.offsetMin = new Vector2(0, 8);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            var left = UIFactory.Rect("List", view);
            left.anchorMin = new Vector2(0, 1);
            left.anchorMax = new Vector2(1, 1);
            left.pivot = new Vector2(0.5f, 1);
            left.sizeDelta = Vector2.zero;
            var v = left.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;
            left.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = left;
            elementRow = Section(left, "1. Element", 0);
            deliveryRow = Section(left, "2. Delivery", 1);
            modRow = Section(left, "3. Modifiers", 2);

            var bottom = UIFactory.Panel("Crafted", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            bottom.rectTransform.anchorMin = new Vector2(0, 0);
            bottom.rectTransform.anchorMax = new Vector2(0.58f, 0.28f);
            bottom.rectTransform.offsetMin = bottom.rectTransform.offsetMax = Vector2.zero;
            var bt = UIFactory.Text("Title", bottom.transform, "Your skills  <size=70%><color=#aaa>tap 1-3 or U to put a skill on a button</color></size>", 26, Palette.Gold, TextAlignmentOptions.Left);
            bt.rectTransform.Place(new Vector2(0, 1), new Vector2(16, -6), new Vector2(900, 36));
            craftedList = UIFactory.Rect("List", bottom.transform);
            craftedList.Stretch(12);
            craftedList.offsetMax = new Vector2(-12, -44);
            var grid = craftedList.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(520, 58);
            grid.spacing = new Vector2(10, 6);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            var right = UIFactory.Panel("Preview", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            right.rectTransform.anchorMin = new Vector2(0.6f, 0);
            right.rectTransform.anchorMax = new Vector2(1, 1);
            right.rectTransform.offsetMin = right.rectTransform.offsetMax = Vector2.zero;
            previewTitle = UIFactory.Text("Title", right.transform, "", 36, Palette.Text);
            previewTitle.fontStyle = FontStyles.Bold;
            previewTitle.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(680, 56));
            previewBody = UIFactory.Text("Body", right.transform, "", 24, Palette.Text, TextAlignmentOptions.TopLeft);
            previewBody.rectTransform.Stretch(20);
            previewBody.rectTransform.offsetMax = new Vector2(-20, -74);
            previewBody.rectTransform.offsetMin = new Vector2(20, 300);
            previewBody.enableAutoSizing = true;
            previewBody.fontSizeMin = 16;
            previewBody.fontSizeMax = 24;

            stockTitle = UIFactory.Text("StockTitle", right.transform, "Forge stock", 24, Palette.Gold, TextAlignmentOptions.Left);
            stockTitle.rectTransform.Place(new Vector2(0, 0), new Vector2(20, 230), new Vector2(600, 34));
            stockRow = UIFactory.Rect("Stock", right.transform).Place(new Vector2(0, 0), new Vector2(20, 120), new Vector2(680, 104));
            var h = stockRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10;
            h.childForceExpandWidth = false;
            h.childControlWidth = false;

            craftButton = UIFactory.Button("Craft", right.transform, "Craft", OnCraft, new Color(0.2f, 0.4f, 0.15f, 1f), 30);
            ((RectTransform)craftButton.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(520, 84));
        }

        RectTransform Section(RectTransform parent, string title, int index)
        {
            var r = UIFactory.Rect(title, parent);
            r.gameObject.AddComponent<LayoutElement>().preferredHeight = 130;
            var t = UIFactory.Text("Title", r, title, 28, Palette.Gold, TextAlignmentOptions.Left);
            t.rectTransform.Place(new Vector2(0, 1), new Vector2(6, 0), new Vector2(600, 36));
            var row = UIFactory.Rect("Row", r);
            row.Stretch();
            row.offsetMax = new Vector2(0, -40);
            var g = row.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(74, 74);
            g.spacing = new Vector2(5, 5);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 11;
            return row;
        }

        public override void Refresh()
        {
            foreach (Transform c in elementRow) Destroy(c.gameObject);
            foreach (Transform c in deliveryRow) Destroy(c.gameObject);
            foreach (Transform c in modRow) Destroy(c.gameObject);
            foreach (Transform c in craftedList) Destroy(c.gameObject);
            foreach (Transform c in stockRow) Destroy(c.gameObject);

            // Only parts you've found are shown; the rest are left for you to discover.
            Fit(elementRow, Row(elementRow, PartKind.Element, i => elements.Contains((ElementPart)i)));
            Fit(deliveryRow, Row(deliveryRow, PartKind.Delivery, i => delivery.HasValue && (int)delivery.Value == i));
            Fit(modRow, Row(modRow, PartKind.Modifier, i => mods.Contains((ModPart)i)));

            foreach (var c in Profile.Data.craftedSkills) CraftedRow(c);

            stockTitle.gameObject.SetActive(AtForge);
            stockRow.gameObject.SetActive(AtForge);
            craftButton.gameObject.SetActive(AtForge);
            if (AtForge) foreach (var part in ForgeStock.Items) StockButton(part);
            UpdatePreview();
        }

        // A section is as tall as its title plus however many rows of parts it holds.
        static void Fit(RectTransform row, int tiles)
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(tiles / 11f));
            row.parent.GetComponent<LayoutElement>().preferredHeight = 44 + rows * 79;
        }

        int Row(RectTransform row, PartKind kind, System.Func<int, bool> isSelected)
        {
            int hidden = 0, shown = 0;
            for (int i = 0; i < SkillParts.Count(kind); i++)
            {
                bool weaponArt = kind == PartKind.Delivery && i >= SkillParts.FirstWeaponArt;
                if (weaponArt && !BossUnlocks.WeaponArts) continue;   // not even a hint until unlocked
                if (Crafting.Owned(kind, i) == null) { hidden++; continue; }
                PartButton(row, kind, i, isSelected(i));
                shown++;
            }
            if (hidden > 0)
            {
                var q = UIFactory.Image("Undiscovered", row, GameAssets.RoundedRect, new Color(0.06f, 0.06f, 0.07f), false);
                var t = UIFactory.Text("Count", q.transform, $"?\n<size=60%>{hidden} more</size>", 26, Palette.MutedText);
                t.rectTransform.Stretch();
                shown++;
            }
            return shown;
        }

        void PartButton(RectTransform row, PartKind kind, int id, bool selected)
        {
            var owned = Crafting.Owned(kind, id);
            bool met = owned != null && SkillParts.RequirementsMet(kind, id, owned.tier);
            var bg = UIFactory.Image("Part", row, GameAssets.RoundedRect, selected ? new Color(0.35f, 0.28f, 0.12f) : new Color(0.08f, 0.07f, 0.08f), true);
            bg.rectTransform.sizeDelta = new Vector2(96, 112);
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = selected ? Color.white : owned == null ? new Color(1, 1, 1, 0.1f) : met ? SkillParts.KindColor(kind) : new Color(1f, 0.3f, 0.3f);
            outline.effectDistance = selected ? new Vector2(3, -3) : new Vector2(2, -2);
            Color iconColor = kind == PartKind.Element ? SkillParts.ElementColor((ElementPart)id) : SkillParts.KindColor(kind);
            var icon = UIFactory.Image("Icon", bg.transform, IconFactory.Get(SkillParts.Icon(kind, id), iconColor), owned != null ? Color.white : new Color(1, 1, 1, 0.2f));
            icon.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(46, 46));
            icon.preserveAspect = true;
            var label = UIFactory.Text("Name", bg.transform, SkillParts.PartName(kind, id) + (owned != null ? " " + SkillParts.Roman(owned.tier) : ""), 13,
                owned != null ? Palette.Text : Palette.MutedText);
            label.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(80, 28));
            label.enableAutoSizing = true;
            label.fontSizeMin = 9;
            label.fontSizeMax = 13;
            bg.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                focusText = $"<b>{SkillParts.PartName(kind, id)}</b>{(owned != null ? " " + SkillParts.Roman(owned.tier) : " <color=#888>(not found yet)</color>")}: " +
                            $"{SkillParts.Describe(kind, id)}\n{SkillParts.RequirementText(kind, id, owned != null ? owned.tier : 1)}";
                if (owned == null) { UpdatePreview(); return; }
                switch (kind)
                {
                    case PartKind.Element:
                        var e = (ElementPart)id;
                        if (elements.Contains(e)) elements.Remove(e);
                        else if (BossUnlocks.MaxElements <= 1) { elements.Clear(); elements.Add(e); }
                        else if (elements.Count < BossUnlocks.MaxElements) elements.Add(e);
                        break;
                    case PartKind.Delivery: delivery = (DeliveryPart)id; break;
                    default:
                        var m = (ModPart)id;
                        if (mods.Contains(m)) mods.Remove(m);
                        else if (mods.Count < SkillParts.MaxMods(Profile.Data.level)) mods.Add(m);
                        break;
                }
                Refresh();
            });
        }

        void UpdatePreview()
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(focusText)) sb.Append(focusText).Append("\n\n");
            if (elements.Count == 0 || !delivery.HasValue)
            {
                previewTitle.text = "New skill";
                previewTitle.color = Palette.Text;
                sb.Append("<color=#aaa>Pick ").Append(BossUnlocks.MaxElements > 1 ? $"up to {BossUnlocks.MaxElements} elements" : "an element")
                  .Append(", then how it's delivered, then up to ")
                  .Append(SkillParts.MaxMods(Profile.Data.level)).Append(" modifiers.\n\nParts drop from monsters (better ones far from the village) and some are sold here.")
                  .Append("\n\nDefeating bosses unlocks more: extra elements, weapon arts and modifier slots.")
                  .Append(AtForge ? "" : "\n\nVisit the Arcane Forge in the village to craft.").Append("</color>");
                previewBody.text = sb.ToString();
                craftButton.interactable = false;
                return;
            }

            var recipe = new CraftedSkill { uid = "preview", element = elements[0], delivery = delivery.Value, mods = new List<ModPart>(mods) };
            recipe.elementTier = Crafting.Owned(PartKind.Element, (int)elements[0]).tier;
            for (int i = 1; i < elements.Count; i++)
            {
                recipe.extraElements.Add(elements[i]);
                recipe.extraElementTiers.Add(Crafting.Owned(PartKind.Element, (int)elements[i]).tier);
            }
            recipe.deliveryTier = Crafting.Owned(PartKind.Delivery, (int)delivery.Value).tier;
            foreach (var m in mods) recipe.modTiers.Add(Crafting.Owned(PartKind.Modifier, (int)m).tier);
            recipe.name = SkillParts.NameFor(elements, recipe.delivery, recipe.mods);
            var skill = SkillComposer.Build(recipe);
            previewTitle.text = recipe.name;
            previewTitle.color = skill.color;
            sb.Append(skill.Describe(1));
            if (recipe.mods.Count >= 3) sb.Append("\n<color=#ffb060>Ultimate: can go on the U button.</color>");
            sb.Append("\n\n");
            for (int i = 0; i < elements.Count; i++)
                sb.Append($"{elements[i]}: {SkillParts.RequirementText(PartKind.Element, (int)elements[i], Crafting.Owned(PartKind.Element, (int)elements[i]).tier)}\n");
            var art = SkillParts.WeaponOf(recipe.delivery);
            if (art.HasValue) sb.Append($"<color=#fc8>Weapon art: only castable with a {Weapons.Name(art.Value)} in hand.</color>\n");
            sb.Append($"{recipe.delivery}: {SkillParts.RequirementText(PartKind.Delivery, (int)recipe.delivery, recipe.deliveryTier)}\n");
            for (int i = 0; i < recipe.mods.Count; i++)
                sb.Append($"{recipe.mods[i]}: {SkillParts.RequirementText(PartKind.Modifier, (int)recipe.mods[i], recipe.modTiers[i])}\n");
            int cost = SkillParts.CraftCost(Profile.Data.level, mods.Count + elements.Count - 1);
            bool ok = Crafting.CanCraft(elements, recipe.delivery, mods, out var why);
            if (AtForge) sb.Append($"\n<color=#ffd040>Cost: {cost} gold</color> <size=80%>(you have {Profile.Data.gold})</size>");
            if (!ok && AtForge) sb.Append($"\n<color=#f77>{why}</color>");
            previewBody.text = sb.ToString();
            craftButton.interactable = ok;
            Object.Destroy(skill);
        }

        void OnCraft()
        {
            if (elements.Count == 0 || !delivery.HasValue) return;
            var c = Crafting.Craft(elements, delivery.Value, mods);
            if (c != null)
            {
                Sound.Play(Sfx.LevelUp, Vector3.zero, 0.4f);
                GameUI.Instance?.Toast($"Crafted {c.name}!", SkillParts.ElementColor(c.element));
                mods.Clear();
            }
            Refresh();
        }

        void CraftedRow(CraftedSkill c)
        {
            var row = UIFactory.Image("Skill", craftedList, GameAssets.RoundedRect, new Color(0.08f, 0.07f, 0.08f), true);
            bool met = SkillParts.RequirementsMet(c);
            var icon = UIFactory.Image("Icon", row.transform, IconFactory.Get(SkillParts.Icon(PartKind.Element, (int)c.element), SkillParts.ElementColor(c.element)), Color.white);
            icon.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(46, 46));
            var name = UIFactory.Text("Name", row.transform, c.name + (met ? "" : " <color=#f66>(requirements!)</color>"), 18, met ? Palette.Text : Palette.MutedText, TextAlignmentOptions.Left);
            name.rectTransform.Stretch();
            name.rectTransform.offsetMin = new Vector2(58, 0);
            name.rectTransform.offsetMax = new Vector2(-210, 0);
            string id = "craft:" + c.uid;
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                bool equipped = Profile.Data.equippedSkills[i] == id;
                bool allowed = i < 3 || c.mods.Count >= 3;
                var b = UIFactory.Button("Slot" + i, row.transform, i == 3 ? "U" : (i + 1).ToString(), () =>
                {
                    Profile.Equip(slot, id);
                    Refresh();
                }, equipped ? new Color(0.5f, 0.38f, 0.1f) : new Color(0.18f, 0.16f, 0.2f), 20);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(-46 - (3 - i) * 40, 0), new Vector2(36, 44));
                b.interactable = allowed;
            }
            var del = UIFactory.Button("Delete", row.transform, "X", () =>
                ConfirmDialog.Show($"Delete {c.name}?", "Delete", () => { Crafting.Delete(c); Refresh(); }), new Color(0.45f, 0.12f, 0.1f), 20);
            ((RectTransform)del.transform).Place(new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(36, 44));
        }

        void StockButton(PartRef part)
        {
            int price = ForgeStock.Price(part);
            var bg = UIFactory.Image("Stock", stockRow, GameAssets.RoundedRect, new Color(0.08f, 0.07f, 0.08f), true);
            bg.rectTransform.sizeDelta = new Vector2(122, 104);
            Color iconColor = part.kind == PartKind.Element ? SkillParts.ElementColor((ElementPart)part.id) : SkillParts.KindColor(part.kind);
            var icon = UIFactory.Image("Icon", bg.transform, IconFactory.Get(SkillParts.Icon(part.kind, part.id), iconColor), Color.white);
            icon.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(52, 52));
            icon.preserveAspect = true;
            var t = UIFactory.Text("Label", bg.transform, $"{part.FullName}\n<color=#ffd040>{price}g</color>", 15, Palette.Text);
            t.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(122, 46));
            bg.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (Profile.Data.gold < price) { GameUI.Instance?.Toast("Not enough gold", new Color(1f, 0.5f, 0.4f)); return; }
                Profile.AddGold(-price);
                ForgeStock.Items.Remove(part);
                GameUI.Instance?.Toast(Crafting.AddPart(part), iconColor);
                Sound.Play(Sfx.Buy, Vector3.zero, 0.5f);
                Refresh();
            });
        }
    }

    // A few parts for sale at the forge, refreshed each level.
    public static class ForgeStock
    {
        static int stockLevel = -1;
        static readonly List<PartRef> items = new List<PartRef>();

        public static List<PartRef> Items
        {
            get
            {
                if (stockLevel != Profile.Data.level)
                {
                    stockLevel = Profile.Data.level;
                    items.Clear();
                    var rng = new System.Random(Profile.Data.level * 97 + Profile.Data.kills);
                    for (int i = 0; i < 5; i++) items.Add(SkillParts.Roll(rng, Profile.Data.level, MonsterRank.Normal));
                }
                return items;
            }
        }

        public static int Price(PartRef p) => 60 * p.tier * p.tier + Profile.Data.level * 8;
    }
}
