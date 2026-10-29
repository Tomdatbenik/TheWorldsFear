using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    public static class PanelsBuilder
    {
        public static void Build(GameUI ui)
        {
            Panels.Register(PanelKind.Skills, SkillTreePanel.Create(ui.Root));
            Panels.Register(PanelKind.Inventory, InventoryPanel.Create(ui.Root));
            Panels.Register(PanelKind.Map, MapPanel.Create(ui.Root));
            Panels.Register(PanelKind.Shop, ShopPanel.Create(ui.Root));
            Panels.Register(PanelKind.Craft, CraftPanel.Create(ui.Root));
        }

        // Dimmed full-screen backdrop with a framed window, title and close button.
        public static RectTransform Frame<T>(Transform root, string title, out T panel) where T : GamePanel
        {
            var dim = UIFactory.Image(typeof(T).Name, root, GameAssets.Pixel, new Color(0, 0, 0, 0.7f), true);
            dim.rectTransform.Stretch();
            panel = dim.gameObject.AddComponent<T>();
            var window = UIFactory.Panel("Window", dim.transform);
            window.rectTransform.Stretch(40f);
            var t = UIFactory.Text("Title", window.transform, title, 48, Palette.Gold);
            t.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            t.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(900, 64));
            var close = UIFactory.Button("Close", window.transform, "X", () => Panels.CloseAll(), new Color(0.45f, 0.12f, 0.1f, 1f), 36);
            ((RectTransform)close.transform).Place(new Vector2(1, 1), new Vector2(-16, -14), new Vector2(76, 70));
            var content = UIFactory.Rect("Content", window.transform);
            content.Stretch();
            content.offsetMax = new Vector2(-24, -90);
            content.offsetMin = new Vector2(24, 24);
            return content;
        }
    }

    // ================= Skill tree =================

    public class SkillTreePanel : GamePanel
    {
        class NodeView
        {
            public SkillTreeNode node;
            public Image bg, ring, lockOverlay;
            public TextMeshProUGUI glyph, rank;
        }

        readonly List<NodeView> views = new List<NodeView>();
        readonly List<TextMeshProUGUI> tierLabels = new List<TextMeshProUGUI>();
        TextMeshProUGUI pointsText, detailTitle, detailBody;
        Button learnButton, respecButton;
        readonly Button[] equipButtons = new Button[4];
        readonly TextMeshProUGUI[] slotLabels = new TextMeshProUGUI[4];
        SkillTreeNode selected;
        RectTransform treeArea;
        HeroClassDefinition builtFor;

        public static SkillTreePanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<SkillTreePanel>(root, "Passives", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            treeArea = UIFactory.Rect("Tree", content);
            treeArea.anchorMin = new Vector2(0, 0);
            treeArea.anchorMax = new Vector2(0.6f, 1);
            treeArea.offsetMin = new Vector2(0, 150);
            treeArea.offsetMax = Vector2.zero;

            pointsText = UIFactory.Text("Points", content, "", 34, Palette.Gold, TextAlignmentOptions.Left);
            pointsText.rectTransform.Place(new Vector2(0, 0), new Vector2(10, 80), new Vector2(600, 50));
            respecButton = UIFactory.Button("Respec", content, "Reset points", () =>
            {
                ConfirmDialog.Show($"Reset all skill points?\n<size=70%>Costs {RespecCost} gold.</size>", "Reset", () =>
                {
                    if (Profile.Data.gold < RespecCost) return;
                    Profile.AddGold(-RespecCost);
                    Profile.Respec();
                    Refresh();
                });
            });
            ((RectTransform)respecButton.transform).Place(new Vector2(0, 0), new Vector2(10, -10), new Vector2(300, 64));

            var detail = UIFactory.Panel("Detail", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            detail.rectTransform.anchorMin = new Vector2(0.62f, 0);
            detail.rectTransform.anchorMax = new Vector2(1, 1);
            detail.rectTransform.offsetMin = detail.rectTransform.offsetMax = Vector2.zero;
            detailTitle = UIFactory.Text("Title", detail.transform, "", 40, Palette.Text);
            detailTitle.fontStyle = FontStyles.Bold;
            detailTitle.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(620, 60));
            detailBody = UIFactory.Text("Body", detail.transform, "Select a skill.", 27, Palette.Text, TextAlignmentOptions.TopLeft);
            detailBody.rectTransform.anchorMin = new Vector2(0, 0);
            detailBody.rectTransform.anchorMax = new Vector2(1, 1);
            detailBody.rectTransform.offsetMin = new Vector2(24, 250);
            detailBody.rectTransform.offsetMax = new Vector2(-24, -90);

            learnButton = UIFactory.Button("Learn", detail.transform, "Learn", OnLearn, new Color(0.2f, 0.4f, 0.15f, 1f), 34);
            ((RectTransform)learnButton.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(420, 80));

            var equipRow = UIFactory.Rect("Equip", detail.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(620, 100));
            var h = equipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                equipButtons[i] = UIFactory.Button("Slot" + i, equipRow, i == 3 ? "Ult" : $"Slot {i + 1}", () => OnEquip(slot), null, 24);
                slotLabels[i] = equipButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        int RespecCost => 50 + Profile.Data.level * 25;

        void BuildTree()
        {
            foreach (Transform c in treeArea) Destroy(c.gameObject);
            views.Clear();
            tierLabels.Clear();
            var tree = Profile.Class.skillTree;
            builtFor = Profile.Class;
            if (tree == null) return;

            int tiers = tree.TierCount;
            for (int t = 0; t < tiers; t++)
            {
                float y0 = 1f - (t + 1f) / tiers;
                float y1 = 1f - (float)t / tiers;
                var row = UIFactory.Rect("Tier" + t, treeArea);
                row.anchorMin = new Vector2(0, y0);
                row.anchorMax = new Vector2(1, y1);
                row.offsetMin = row.offsetMax = Vector2.zero;
                var label = UIFactory.Text("Label", row, "", 22, Palette.MutedText, TextAlignmentOptions.Left);
                label.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(170, 90));
                tierLabels.Add(label);
            }

            foreach (var node in tree.nodes)
            {
                var row = treeArea.GetChild(Mathf.Clamp(node.tier, 0, tiers - 1));
                float x = 220 + node.column * 180;
                var bg = UIFactory.Circle(node.id, row, 100, new Color(0.15f, 0.12f, 0.12f), true);
                bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0, 0.5f);
                bg.rectTransform.anchoredPosition = new Vector2(x, 0);
                // Actives are round, passives are diamonds.
                if (!node.IsActive) bg.sprite = GameAssets.Diamond;
                var ring = UIFactory.Image("Ring", bg.transform, node.IsActive ? GameAssets.Ring : GameAssets.Diamond, node.Color);
                ring.rectTransform.Stretch(node.IsActive ? 0 : 10);
                if (!node.IsActive) ring.color = new Color(node.Color.r, node.Color.g, node.Color.b, 0.35f);
                string iconId = node.active != null ? node.active.id : node.passive != null ? node.passive.id : node.id;
                var nodeIcon = UIFactory.Image("Icon", bg.transform, IconFactory.Get(IconFactory.KeyFor(iconId, node.DisplayName), Color.Lerp(node.Color, Color.white, 0.15f)), Color.white);
                nodeIcon.rectTransform.Stretch(node.IsActive ? 24 : 30);
                nodeIcon.preserveAspect = true;
                var glyph = UIFactory.Text("Glyph", bg.transform, "", 22, Color.white);
                glyph.fontStyle = FontStyles.Bold;
                glyph.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, 14), new Vector2(120, 30));
                var lockImg = UIFactory.Image("Lock", bg.transform, node.IsActive ? GameAssets.Circle : GameAssets.Diamond, new Color(0, 0, 0, 0.6f));
                lockImg.rectTransform.Stretch();
                // Rank badge sits on the node's lower edge so it never runs into the next tier's row.
                var badge = UIFactory.Image("RankBadge", bg.transform, GameAssets.Circle, new Color(0.07f, 0.06f, 0.06f, 0.92f));
                badge.type = Image.Type.Simple;
                badge.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(76, 32));
                var rank = UIFactory.Text("Rank", badge.transform, "", 24, Palette.Gold);
                rank.rectTransform.Stretch();
                if (node.active != null && node.active.isUltimate)
                {
                    var ultTag = UIFactory.Text("Ult", bg.transform, "ULT", 18, new Color(1f, 0.5f, 0.3f));
                    ultTag.rectTransform.Place(new Vector2(1, 1), new Vector2(26, 6), new Vector2(60, 24));
                }
                var btn = bg.gameObject.AddComponent<Button>();
                var captured = node;
                btn.onClick.AddListener(() => Select(captured));
                views.Add(new NodeView { node = node, bg = bg, ring = ring, glyph = glyph, rank = rank, lockOverlay = lockImg });
            }
        }

        public override void Refresh()
        {
            if (builtFor != Profile.Class || views.Count == 0) BuildTree();
            var tree = Profile.Class.skillTree;
            if (tree == null) return;
            var data = Profile.Data;
            int spent = SkillTreeDefinition.PointsSpent(Profile.Ranks);
            pointsText.text = $"Passive points: <b>{data.skillPoints}</b>   <size=70%><color=#aaa>Spent: {spent}</color></size>";
            respecButton.interactable = spent > 1 && data.gold >= RespecCost;

            for (int t = 0; t < tierLabels.Count; t++)
            {
                int need = tree.pointsToUnlockTier[t];
                bool open = spent >= need;
                tierLabels[t].text = open ? $"Tier {t + 1}" : $"Tier {t + 1}\n<size=80%>{need} points</size>";
                tierLabels[t].color = open ? Palette.Text : Palette.MutedText;
            }

            foreach (var v in views)
            {
                int r = Profile.RankOf(v.node.id);
                var state = tree.Check(v.node, Profile.Ranks, data.level, data.skillPoints);
                bool locked = r == 0 && state != SkillTreeDefinition.CanRankUp.Yes && state != SkillTreeDefinition.CanRankUp.NoPoints;
                v.lockOverlay.gameObject.SetActive(locked);
                v.rank.text = $"{r}/{v.node.MaxRank}";
                v.rank.color = r > 0 ? Palette.Gold : Palette.MutedText;
                v.bg.transform.localScale = v.node == selected ? Vector3.one * 1.12f : Vector3.one;
                int eq = Array.IndexOf(data.equippedSkills, v.node.id);
                v.glyph.text = eq >= 0 ? $"<color=#ffd040>{(eq == 3 ? "ULT" : "#" + (eq + 1))}</color>" : "";
            }
            UpdateDetail();
        }

        void Select(SkillTreeNode node)
        {
            selected = node;
            Refresh();
        }

        void UpdateDetail()
        {
            var data = Profile.Data;
            for (int i = 0; i < 4; i++)
            {
                var node = Profile.Class.skillTree.Find(data.equippedSkills[i]);
                string current = node != null ? node.DisplayName : "empty";
                slotLabels[i].text = (i == 3 ? "Ult" : $"Slot {i + 1}") + $"\n<size=70%>{current}</size>";
            }

            if (selected == null)
            {
                detailTitle.text = "Passive board";
                detailBody.text = "<color=#aaa>Passives are always on. You earn 1 passive point per level; deeper tiers open as you spend points.\n\n" +
                                  "Active skills are crafted at the Arcane Forge in the village.</color>";
                learnButton.gameObject.SetActive(false);
                foreach (var b in equipButtons) b.gameObject.SetActive(false);
                return;
            }

            var tree = Profile.Class.skillTree;
            int rank = Profile.RankOf(selected.id);
            detailTitle.text = selected.DisplayName;
            detailTitle.color = selected.Color;
            string kind = selected.IsActive ? (selected.active.isUltimate ? "Ultimate skill" : "Active skill") : "Passive";
            string body = $"<color=#aaa>{kind} · Rank {rank}/{selected.MaxRank}</color>\n\n{selected.Describe(Mathf.Max(1, rank))}";
            if (rank > 0 && rank < selected.MaxRank)
                body += $"\n\n<color=#7f7>Next rank:</color>\n<size=85%>{selected.Describe(rank + 1)}</size>";
            var state = tree.Check(selected, Profile.Ranks, data.level, data.skillPoints);
            switch (state)
            {
                case SkillTreeDefinition.CanRankUp.LevelTooLow: body += $"\n\n<color=#f77>Requires level {selected.requiredLevel}</color>"; break;
                case SkillTreeDefinition.CanRankUp.TierLocked: body += $"\n\n<color=#f77>Spend {tree.pointsToUnlockTier[selected.tier]} points in the tree to unlock this tier</color>"; break;
                case SkillTreeDefinition.CanRankUp.MissingRequirement: body += $"\n\n<color=#f77>Requires {tree.Find(selected.requires)?.DisplayName}</color>"; break;
            }
            detailBody.text = body;

            learnButton.gameObject.SetActive(state != SkillTreeDefinition.CanRankUp.Maxed);
            learnButton.interactable = state == SkillTreeDefinition.CanRankUp.Yes;
            learnButton.GetComponentInChildren<TextMeshProUGUI>().text = rank == 0 ? "Learn (1 point)" : "Rank up (1 point)";

            bool canEquip = selected.IsActive && rank > 0;
            for (int i = 0; i < 4; i++)
            {
                bool ultSlot = i == 3;
                equipButtons[i].gameObject.SetActive(canEquip && ultSlot == selected.active.isUltimate);
            }
        }

        void OnLearn()
        {
            if (selected != null && Profile.RankUp(selected)) VFX.Burst(Vector3.zero, Palette.Gold, 1, 0.1f);
            Refresh();
        }

        void OnEquip(int slot)
        {
            if (selected == null) return;
            Profile.Equip(slot, selected.id);
            Refresh();
        }
    }

    // ================= Inventory / character =================

    public class InventoryPanel : GamePanel
    {
        RectTransform equipArea, bagArea;
        TextMeshProUGUI statsText, detailText, goldText, attrHeader;
        readonly TextMeshProUGUI[] attrLabels = new TextMeshProUGUI[5];
        readonly Button[] attrPlus = new Button[5];
        Button attrReset;
        Button equipButton, sellButton, sellJunkButton;
        ItemInstance selected;
        readonly Dictionary<ItemSlot, RectTransform> slotRects = new Dictionary<ItemSlot, RectTransform>();

        public static InventoryPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<InventoryPanel>(root, "Character", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            var left = UIFactory.Panel("Left", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            left.rectTransform.anchorMin = new Vector2(0, 0);
            left.rectTransform.anchorMax = new Vector2(0.3f, 1);
            left.rectTransform.offsetMin = left.rectTransform.offsetMax = Vector2.zero;
            // Attributes with + buttons on top, derived stats below.
            attrHeader = UIFactory.Text("AttrHeader", left.transform, "", 28, Palette.Gold, TextAlignmentOptions.Left);
            attrHeader.rectTransform.Place(new Vector2(0, 1), new Vector2(22, -14), new Vector2(520, 40));
            for (int i = 0; i < Attributes.Count; i++)
            {
                var a = (Attribute)i;
                var row = UIFactory.Rect("Attr" + a, left.transform).Place(new Vector2(0, 1), new Vector2(18, -60 - i * 58), new Vector2(540, 54));
                var label = UIFactory.Text("Label", row, "", 26, Attributes.Color(a), TextAlignmentOptions.Left);
                label.rectTransform.Stretch();
                label.rectTransform.offsetMax = new Vector2(-70, 0);
                attrLabels[i] = label;
                var plus = UIFactory.Button("Plus", row, "+", () => { Profile.AddAttribute(a); Refresh(); }, new Color(0.2f, 0.4f, 0.15f, 1f), 34);
                ((RectTransform)plus.transform).Place(new Vector2(1, 0.5f), new Vector2(0, 0), new Vector2(60, 50));
                attrPlus[i] = plus;
            }
            attrReset = UIFactory.Button("Reset", left.transform, "", () =>
            {
                ConfirmDialog.Show($"Reset attribute points?\n<size=70%>Costs {Profile.AttributeResetCost} gold.</size>", "Reset", () => { Profile.ResetAttributes(); Refresh(); });
            }, null, 22);
            ((RectTransform)attrReset.transform).Place(new Vector2(0, 1), new Vector2(18, -356), new Vector2(300, 50));
            statsText = UIFactory.Text("Stats", left.transform, "", 22, Palette.Text, TextAlignmentOptions.TopLeft);
            statsText.rectTransform.Stretch(22);
            statsText.rectTransform.offsetMax = new Vector2(-22, -420);
            statsText.enableAutoSizing = true;
            statsText.fontSizeMin = 13;
            statsText.fontSizeMax = 22;

            equipArea = UIFactory.Rect("Equipment", content);
            equipArea.anchorMin = new Vector2(0.31f, 0.42f);
            equipArea.anchorMax = new Vector2(0.62f, 1f);
            equipArea.offsetMin = equipArea.offsetMax = Vector2.zero;
            // Paper-doll layout.
            var layout = new Dictionary<ItemSlot, Vector2>
            {
                { ItemSlot.Helm, new Vector2(0.5f, 0.86f) }, { ItemSlot.Amulet, new Vector2(0.8f, 0.86f) },
                { ItemSlot.Weapon, new Vector2(0.2f, 0.5f) }, { ItemSlot.Chest, new Vector2(0.5f, 0.5f) }, { ItemSlot.Ring, new Vector2(0.8f, 0.5f) },
                { ItemSlot.Gloves, new Vector2(0.2f, 0.14f) }, { ItemSlot.Boots, new Vector2(0.5f, 0.14f) },
            };
            foreach (var kv in layout)
            {
                var r = UIFactory.Rect(kv.Key.ToString(), equipArea);
                r.anchorMin = r.anchorMax = kv.Value;
                r.sizeDelta = new Vector2(140, 140);
                slotRects[kv.Key] = r;
            }

            bagArea = UIFactory.Rect("Bag", content);
            bagArea.anchorMin = new Vector2(0.31f, 0f);
            bagArea.anchorMax = new Vector2(0.62f, 0.4f);
            bagArea.offsetMin = bagArea.offsetMax = Vector2.zero;
            var grid = bagArea.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(92, 92);
            grid.spacing = new Vector2(8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;
            grid.childAlignment = TextAnchor.UpperCenter;

            var detail = UIFactory.Panel("Detail", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            detail.rectTransform.anchorMin = new Vector2(0.64f, 0);
            detail.rectTransform.anchorMax = new Vector2(1, 1);
            detail.rectTransform.offsetMin = detail.rectTransform.offsetMax = Vector2.zero;
            detailText = UIFactory.Text("Text", detail.transform, "", 28, Palette.Text, TextAlignmentOptions.TopLeft);
            detailText.rectTransform.Stretch(22);
            detailText.rectTransform.offsetMin = new Vector2(22, 260);
            goldText = UIFactory.Text("Gold", detail.transform, "", 30, Palette.Gold, TextAlignmentOptions.Left);
            goldText.rectTransform.Place(new Vector2(0, 0), new Vector2(22, 200), new Vector2(500, 44));

            equipButton = UIFactory.Button("Equip", detail.transform, "Equip", OnEquip, new Color(0.2f, 0.4f, 0.15f, 1f));
            ((RectTransform)equipButton.transform).Place(new Vector2(0, 0), new Vector2(22, 110), new Vector2(260, 76));
            sellButton = UIFactory.Button("Sell", detail.transform, "Sell", OnSell, new Color(0.45f, 0.3f, 0.1f, 1f));
            ((RectTransform)sellButton.transform).Place(new Vector2(1, 0), new Vector2(-22, 110), new Vector2(260, 76));
            sellJunkButton = UIFactory.Button("SellJunk", detail.transform, "Sell all white & blue", OnSellJunk, null, 26);
            ((RectTransform)sellJunkButton.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(560, 70));
        }

        public override void Refresh()
        {
            // Equipment slots.
            foreach (var kv in slotRects)
            {
                foreach (Transform c in kv.Value) Destroy(c.gameObject);
                var item = Profile.Equipped(kv.Key);
                var cell = MakeCell(kv.Value, item, kv.Key.ToString());
                cell.rectTransform.Stretch();
            }

            // Bag.
            foreach (Transform c in bagArea) Destroy(c.gameObject);
            var inv = Profile.Data.inventory;
            for (int i = 0; i < Profile.InventorySize; i++)
                MakeCell(bagArea, i < inv.Count ? inv[i] : null, null);

            // Stats.
            var hero = HeroController.Instance;
            if (hero != null)
            {
                var s = hero.Combatant.Stats;
                var d = Profile.Data;
                attrHeader.text = $"Attributes  <size=80%><color=#fff>{d.attributePoints} point{(d.attributePoints == 1 ? "" : "s")} to spend</color></size>";
                for (int i = 0; i < Attributes.Count; i++)
                {
                    attrLabels[i].text = $"{(Attribute)i}  <b>{d.attributes[i]}</b>  <size=70%><color=#aaa>{Attributes.Describe((Attribute)i)}</color></size>";
                    attrPlus[i].interactable = d.attributePoints > 0;
                }
                attrReset.GetComponentInChildren<TextMeshProUGUI>().text = $"Reset ({Profile.AttributeResetCost} gold)";
                var sb = new System.Text.StringBuilder();
                sb.Append($"<b>Level {d.level}</b>  <color=#aaa>· {Weapons.Name(Weapons.Equipped)}</color>\n");
                foreach (StatType st in Enum.GetValues(typeof(StatType)))
                {
                    if (st == StatType.CritDamage) sb.Append($"{StatInfo.Name(st)}: <color=#fff>{Mathf.RoundToInt((1.5f + s.Get(st)) * 100)}%</color>\n");
                    else sb.Append($"{StatInfo.Name(st)}: <color=#fff>{StatInfo.FormatValue(st, s.Get(st))}</color>\n");
                }
                sb.Append($"\n<color=#aaa>Damage reduction from armor: {Mathf.RoundToInt((1f - Combat.Mitigate(100f, s.Get(StatType.Armor)) / 100f) * 100)}%</color>");
                sb.Append($"\n<color=#aaa>Monsters slain: {Profile.Data.kills}</color>");
                statsText.text = sb.ToString();
            }

            goldText.text = $"{Profile.Data.gold} gold";
            UpdateDetail();
        }

        Image MakeCell(Transform parent, ItemInstance item, string emptyLabel)
        {
            Color border = item != null ? RarityInfo.Color(item.rarity) : new Color(1, 1, 1, 0.15f);
            var cell = UIFactory.Image("Cell", parent, GameAssets.RoundedRect, new Color(0.08f, 0.07f, 0.08f, 1f), true);
            var outline = cell.gameObject.AddComponent<Outline>();
            outline.effectColor = item == selected && item != null ? Color.white : border;
            outline.effectDistance = item == selected && item != null ? new Vector2(4, -4) : new Vector2(2, -2);
            if (item != null)
            {
                var icon = UIFactory.Image("Icon", cell.transform, ItemIcons.Get(item), Color.white);
                icon.rectTransform.Stretch(10);
                icon.preserveAspect = true;
                var btn = cell.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    selected = item;
                    Refresh();
                });
            }
            else if (emptyLabel != null)
            {
                var t = UIFactory.Text("Label", cell.transform, emptyLabel, 20, new Color(1, 1, 1, 0.3f));
                t.rectTransform.Stretch();
            }
            return cell;
        }

        bool IsEquipped(ItemInstance item) => Profile.Data.equipment.Contains(item);

        void UpdateDetail()
        {
            if (selected != null && !Profile.Data.inventory.Contains(selected) && !IsEquipped(selected)) selected = null;
            if (selected == null)
            {
                detailText.text = "<color=#aaa>Tap an item to see its stats.\n\nMonsters drop gear: <color=#fff>white</color> common, " +
                                  "<color=#6f8cff>blue</color> magic, <color=#ffe64d>yellow</color> rare and <color=#ff8c1a>orange</color> legendary items with special powers.</color>";
                equipButton.gameObject.SetActive(false);
                sellButton.gameObject.SetActive(false);
            }
            else
            {
                bool equipped = IsEquipped(selected);
                var compare = equipped ? null : Profile.Equipped(selected.slot);
                detailText.text = ItemGenerator.Tooltip(selected, compare);
                equipButton.gameObject.SetActive(true);
                equipButton.GetComponentInChildren<TextMeshProUGUI>().text = equipped ? "Unequip" : "Equip";
                sellButton.gameObject.SetActive(!equipped);
            }
        }

        void OnEquip()
        {
            if (selected == null) return;
            if (IsEquipped(selected)) Profile.UnequipItem(selected);
            else Profile.EquipItem(selected);
            Refresh();
        }

        void OnSell()
        {
            if (selected == null) return;
            var item = selected;
            Action sell = () =>
            {
                Profile.SellItem(item);
                selected = null;
                Refresh();
            };
            if (item.rarity >= Rarity.Rare) ConfirmDialog.Show($"Sell {item.name} for {item.SellValue} gold?", "Sell", sell);
            else sell();
        }

        void OnSellJunk()
        {
            foreach (var item in Profile.Data.inventory.ToArray())
                if (item.rarity <= Rarity.Magic) Profile.SellItem(item);
            selected = null;
            Refresh();
        }
    }

    // ================= Map / waypoints =================

    // Explored chunks painted by biome, with waypoints and the hero. Tap a waypoint to travel.
    public class MapPanel : GamePanel
    {
        const int MapChunks = 64; // chunks shown across the map
        RectTransform mapArea, list;
        RawImage mapImage;
        Texture2D mapTex;
        RectTransform heroDot;
        Vector2Int centerChunk;

        public static MapPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<MapPanel>(root, "World Map", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            var mapBg = UIFactory.Panel("Map", content, new Color(0.05f, 0.05f, 0.06f, 1f));
            mapBg.rectTransform.anchorMin = new Vector2(0, 0);
            mapBg.rectTransform.anchorMax = new Vector2(0.62f, 1);
            mapBg.rectTransform.offsetMin = mapBg.rectTransform.offsetMax = Vector2.zero;
            mapArea = UIFactory.Rect("Area", mapBg.transform).Stretch(20);
            mapImage = UIFactory.Rect("Image", mapArea).Stretch().gameObject.AddComponent<RawImage>();
            var fitter = mapImage.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1f;
            mapTex = new Texture2D(MapChunks, MapChunks, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            mapImage.texture = mapTex;

            var right = UIFactory.Panel("Waypoints", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            right.rectTransform.anchorMin = new Vector2(0.64f, 0);
            right.rectTransform.anchorMax = new Vector2(1, 1);
            right.rectTransform.offsetMin = right.rectTransform.offsetMax = Vector2.zero;
            var title = UIFactory.Text("Title", right.transform, "Waypoints", 34, Palette.Gold);
            title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(500, 50));
            var scroll = UIFactory.Rect("Scroll", right.transform);
            scroll.Stretch(20);
            scroll.offsetMax = new Vector2(-20, -70);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            scroll.gameObject.AddComponent<RectMask2D>();
            list = UIFactory.Rect("List", scroll);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = Vector2.zero;
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12;
            v.childForceExpandHeight = false;
            v.childControlHeight = false;
            v.childForceExpandWidth = true;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = list;
            sr.horizontal = false;
        }

        static Color BiomeColor(Biome b)
        {
            switch (b)
            {
                case Biome.Village: return new Color(0.85f, 0.75f, 0.5f);
                case Biome.Forest: return new Color(0.55f, 0.45f, 0.2f);
                case Biome.Marsh: return new Color(0.3f, 0.42f, 0.35f);
                case Biome.Ruins: return new Color(0.5f, 0.52f, 0.58f);
                case Biome.Ember: return new Color(0.55f, 0.18f, 0.1f);
            }
            return Color.gray;
        }

        public override void Refresh()
        {
            foreach (Transform c in mapArea) if (c != mapImage.transform) Destroy(c.gameObject);
            foreach (Transform c in list) Destroy(c.gameObject);
            var wm = WorldManager.Instance;
            var hero = HeroController.Instance;
            if (wm == null || hero == null) return;

            // Paint explored chunks around the hero.
            centerChunk = WorldGen.ChunkOf(hero.transform.position);
            int half = MapChunks / 2;
            var px = new Color[MapChunks * MapChunks];
            int S = WorldGen.ChunkSize;
            for (int y = 0; y < MapChunks; y++)
            for (int x = 0; x < MapChunks; x++)
            {
                var c = new Vector2Int(centerChunk.x - half + x, centerChunk.y - half + y);
                if (!Profile.IsExplored(c)) { px[y * MapChunks + x] = new Color(0.06f, 0.06f, 0.07f); continue; }
                var p = new Vector2((c.x + 0.5f) * S, (c.y + 0.5f) * S);
                var col = BiomeColor(WorldGen.BiomeAt(p));
                // Darken with difficulty so you can read how dangerous an area is.
                col *= Mathf.Lerp(1.1f, 0.6f, Mathf.Clamp01(WorldGen.LevelAt(p) / 60f));
                col.a = 1f;
                px[y * MapChunks + x] = col;
            }
            mapTex.SetPixels(px);
            mapTex.Apply();

            foreach (var id in Profile.Data.waypoints)
            {
                var pos = wm.WaypointPosition(id);
                var dot = UIFactory.Circle("wp", mapImage.transform, 22, new Color(0.5f, 0.8f, 1f));
                PlaceWorldPoint(dot.rectTransform, pos);
                string captured = id;
                var b = UIFactory.Button(id, list, wm.WaypointName(id), () => wm.Teleport(captured), new Color(0.15f, 0.25f, 0.35f, 1f), 26);
                ((RectTransform)b.transform).sizeDelta = new Vector2(0, 86);
            }

            heroDot = UIFactory.Circle("Hero", mapImage.transform, 26, Palette.Gold).rectTransform;
            UpdateHero();
        }

        void Update()
        {
            if (heroDot != null) UpdateHero();
        }

        void UpdateHero()
        {
            var hero = HeroController.Instance;
            if (hero != null) PlaceWorldPoint(heroDot, hero.transform.position);
        }

        void PlaceWorldPoint(RectTransform rt, Vector2 world)
        {
            int half = MapChunks / 2;
            float S = WorldGen.ChunkSize;
            var n = new Vector2((world.x / S - (centerChunk.x - half)) / MapChunks, (world.y / S - (centerChunk.y - half)) / MapChunks);
            rt.anchorMin = rt.anchorMax = new Vector2(Mathf.Clamp01(n.x), Mathf.Clamp01(n.y));
            rt.anchoredPosition = Vector2.zero;
            rt.gameObject.SetActive(n.x >= 0 && n.x <= 1 && n.y >= 0 && n.y <= 1);
        }
    }

    // ================= Confirm dialog =================

    public static class ConfirmDialog
    {
        public static void Show(string message, string confirmLabel, Action onConfirm)
        {
            var root = GameUI.Instance != null ? GameUI.Instance.Root : UIFactory.CreateCanvas("Dialog", 50).transform;
            var dim = UIFactory.Image("Confirm", root, GameAssets.Pixel, new Color(0, 0, 0, 0.6f), true);
            dim.rectTransform.Stretch();
            dim.transform.SetAsLastSibling();
            var box = UIFactory.Panel("Box", dim.transform);
            box.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 420));
            var text = UIFactory.Text("Text", box.transform, message, 38, Palette.Text);
            text.rectTransform.Stretch(30);
            text.rectTransform.offsetMin = new Vector2(30, 130);
            float prevScale = Time.timeScale;
            Time.timeScale = 0f;
            Action close = () =>
            {
                UnityEngine.Object.Destroy(dim.gameObject);
                if (!Panels.AnyOpen) Time.timeScale = 1f;
            };
            var yes = UIFactory.Button("Yes", box.transform, confirmLabel, () =>
            {
                close();
                onConfirm?.Invoke();
            }, new Color(0.2f, 0.4f, 0.15f, 1f), 34);
            ((RectTransform)yes.transform).Place(new Vector2(0, 0), new Vector2(40, 30), new Vector2(360, 86));
            var no = UIFactory.Button("No", box.transform, "Cancel", close, new Color(0.35f, 0.15f, 0.12f, 1f), 34);
            ((RectTransform)no.transform).Place(new Vector2(1, 0), new Vector2(-40, 30), new Vector2(360, 86));
        }
    }
}
