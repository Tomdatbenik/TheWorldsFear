using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // The quest book: the quest in hand with its explanation, progress and reward, and under it the ones already done.
    public class QuestPanel : GamePanel
    {
        RectTransform list;
        ScrollRect scroll;

        public static QuestPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<QuestPanel>(root, "Quest Book", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            var view = UIFactory.Panel("View", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            view.rectTransform.Stretch();
            var viewport = UIFactory.Rect("Viewport", view.transform).Stretch(16);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            list = UIFactory.Rect("List", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = Vector2.zero;
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12;
            v.childControlHeight = true;      // every card is as tall as its text
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = list;
        }

        public override void Refresh()
        {
            Quests.MarkRead();
            foreach (Transform c in list) Destroy(c.gameObject);
            int index = Quests.Index, total = Quests.All.Count;
            var q = Quests.Current;
            if (q != null)
            {
                int have = Quests.Progress(q);
                string reward = Quests.RewardLine(q);
                Card($"<size=70%><color=#aaa>Quest {index + 1} of {total}</color></size>\n" +
                     $"<color=#ffd040><b>{q.title}</b></color>\n" +
                     $"<size=85%>{q.story}</size>\n\n" +
                     $"<color=#8cf>Goal: {q.goalText}</color>" + (q.goal > 1 ? $"  <color=#fff>{have} / {q.goal}</color>" : "") +
                     (reward.Length > 0 ? $"\n<color=#ffd040>Reward: {reward}</color>" : ""),
                     30, new Color(0.16f, 0.13f, 0.08f, 1f));
            }
            else Card("<color=#ffd040><b>Every quest is done</b></color>\n<size=85%>The World's Fear is dead. The country is yours to wander.</size>", 30, new Color(0.16f, 0.13f, 0.08f, 1f));

            if (index > 0) Card("<color=#aaa>Completed</color>", 26, new Color(0, 0, 0, 0));
            // Newest first; the explanations stay readable, they are the game's manual.
            for (int i = Mathf.Min(index, total) - 1; i >= 0; i--)
            {
                var d = Quests.All[i];
                Card($"<color=#8f8>Done</color>  <b>{d.title}</b>  <size=80%><color=#aaa>{d.goalText}</color></size>\n<size=75%><color=#bbb>{d.story}</color></size>",
                     26, new Color(0.09f, 0.08f, 0.1f, 1f));
            }
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;
        }

        void Card(string text, float size, Color bg)
        {
            var card = UIFactory.Panel("Card", list, bg);
            if (bg.a <= 0f) card.enabled = false;
            var pad = card.gameObject.AddComponent<VerticalLayoutGroup>();
            pad.padding = new RectOffset(24, 24, 16, 18);
            pad.childControlHeight = pad.childControlWidth = true;
            pad.childForceExpandHeight = false;
            var t = UIFactory.Text("Text", card.transform, text, size, Palette.Text, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal;
        }
    }
}
