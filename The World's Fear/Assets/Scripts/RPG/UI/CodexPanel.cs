using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // The codex: monsters you've slain, bosses you've faced, loot you've found and the element chart.
    public class CodexPanel : GamePanel
    {
        enum Tab { Monsters, Bosses, Loot, Elements }

        Tab tab;
        readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
        RectTransform list;
        ScrollRect scroll;

        public static CodexPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<CodexPanel>(root, "Codex", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            int i = 0;
            foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
            {
                var captured = t;
                var b = UIFactory.Button(t.ToString(), content, t.ToString(), () => { tab = captured; Refresh(); }, null, 28);
                ((RectTransform)b.transform).Place(new Vector2(0, 1), new Vector2(i * 236, 0), new Vector2(220, 60));
                tabButtons[t] = b;
                i++;
            }
            var view = UIFactory.Panel("View", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            view.rectTransform.Stretch();
            view.rectTransform.offsetMax = new Vector2(0, -72);
            var viewport = UIFactory.Rect("Viewport", view.transform).Stretch(16);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            list = UIFactory.Rect("List", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = Vector2.zero;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = list;
        }

        public override void Refresh()
        {
            foreach (var kv in tabButtons)
                kv.Value.GetComponent<Image>().color = kv.Key == tab ? new Color(0.45f, 0.35f, 0.12f) : new Color(0.18f, 0.15f, 0.15f);
            foreach (Transform c in list) Destroy(c.gameObject);
            var old = list.GetComponent<LayoutGroup>();
            if (old != null) DestroyImmediate(old);
            switch (tab)
            {
                case Tab.Monsters: Monsters(); break;
                case Tab.Bosses: Bosses(); break;
                case Tab.Loot: Loot(); break;
                default: ElementChart(); break;
            }
            scroll.verticalNormalizedPosition = 1f;
        }

        VerticalLayoutGroup Rows()
        {
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10;
            v.childControlHeight = false;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;
            return v;
        }

        static string Matchup(ElementPart e)
        {
            var weak = Elements.WeaknessOf(e);
            return weak.HasValue ? $"{Elements.Tag(e)}  ·  weak to {Elements.Tag(weak.Value)}  ·  resists {Elements.Tag(e)}" : $"{Elements.Tag(e)}  ·  no weakness, no resistance";
        }

        void Monsters()
        {
            var g = list.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(560, 150);
            g.spacing = new Vector2(12, 12);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 3;
            var all = Profile.Data.codexMonsters;
            if (all.Count == 0) { Note("Slay monsters to fill the codex."); return; }
            foreach (var m in all)
            {
                var card = UIFactory.Panel(m.name, list, new Color(0.09f, 0.08f, 0.1f, 1f));
                var look = Codex.LookOf(m);
                var img = UIFactory.Image("Sprite", card.transform, look.frames[0], Color.white);
                img.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(120, 120));
                img.preserveAspect = true;
                var e = (ElementPart)m.element;
                var t = UIFactory.Text("Info", card.transform,
                    $"<b>{m.name}</b>\n<size=75%>{(Biome)m.biome}  ·  slain {m.kills}  ·  up to level {m.highestLevel}\n{Matchup(e)}</size>",
                    24, Palette.Text, TextAlignmentOptions.Left);
                t.rectTransform.Stretch();
                t.rectTransform.offsetMin = new Vector2(148, 8);
                t.rectTransform.offsetMax = new Vector2(-10, -8);
                t.enableAutoSizing = true;
                t.fontSizeMin = 14;
                t.fontSizeMax = 24;
            }
        }

        void Bosses()
        {
            Rows();
            foreach (var k in WorldManager.BossOrder)
            {
                bool seen = Codex.BossSeen(k);
                bool slain = WorldManager.Slain(k);
                var row = UIFactory.Panel(k.ToString(), list, new Color(0.09f, 0.08f, 0.1f, 1f));
                ((RectTransform)row.transform).sizeDelta = new Vector2(0, 150);
                string body;
                if (!seen && !slain) body = $"<b>???</b>\n<size=75%><color=#888>{BossTitan.LairName(k)}: not yet faced</color></size>";
                else
                {
                    string state = slain ? "<color=#8f8>Slain</color>" : "<color=#f99>Faced, not yet beaten</color>";
                    body = $"<b>{BossTitan.BossName(k)}</b>  <size=70%>{state}</size>\n<size=75%>{Matchup(BossTitan.ElementOf(k))}" +
                           $"\n{(slain ? "Unlocked" : "Defeat it to unlock")}: <color=#ffd040>{BossUnlocks.Describe(k)}</color></size>";
                }
                var t = UIFactory.Text("Info", row.transform, body, 28, Palette.Text, TextAlignmentOptions.Left);
                t.rectTransform.Stretch(20);
                t.enableAutoSizing = true;
                t.fontSizeMin = 16;
                t.fontSizeMax = 28;
            }
        }

        void Loot()
        {
            Rows();
            var d = Profile.Data;
            var sb = new System.Text.StringBuilder("<b>Items found</b>\n<size=80%>");
            for (int r = 0; r < 5; r++)
                sb.Append($"<color={Palette.Hex(RarityInfo.Color((Rarity)r))}>{(Rarity)r}</color>: {d.lootByRarity[r]}    ");
            sb.Append("\nWeapons found: ");
            var names = new List<string>();
            foreach (var w in d.codexWeapons) names.Add(Weapons.Name((WeaponType)w));
            sb.Append(names.Count > 0 ? string.Join(", ", names) : "none yet").Append("</size>");
            Line(sb.ToString(), 120);
            Line($"<b>Legendary powers discovered</b>  <size=70%><color=#aaa>{d.codexPowers.Count} of {ItemPowers.All.Count}</color></size>", 56);
            foreach (var p in ItemPowers.All)
            {
                bool found = d.codexPowers.Contains(p.id);
                Line(found ? $"<color=#ff9a30>{p.name}</color>  <size=80%><color=#ccc>{ItemPowers.Describe(p.id)}</color></size>" : "<color=#666>??? — not found yet</color>", 64);
            }
        }

        void ElementChart()
        {
            Rows();
            Line("<b>Elements</b>  <size=75%><color=#ccc>Hitting a monster's weakness deals x1.5 damage; its own element only x0.5. Physical is always neutral. " +
                 "Weapons can carry an element too, and spells can combine elements once bosses unlock it: the best matchup counts.</color></size>", 130);
            foreach (ElementPart e in System.Enum.GetValues(typeof(ElementPart)))
                Line(Matchup(e), 60);
        }

        void Line(string text, float height)
        {
            var t = UIFactory.Text("Line", list, text, 28, Palette.Text, TextAlignmentOptions.Left);
            t.rectTransform.sizeDelta = new Vector2(0, height);
            t.enableAutoSizing = true;
            t.fontSizeMin = 16;
            t.fontSizeMax = 28;
        }

        void Note(string text)
        {
            var t = UIFactory.Text("Note", list, $"<color=#aaa>{text}</color>", 28, Palette.Text);
            t.rectTransform.sizeDelta = new Vector2(500, 150);
        }
    }
}
