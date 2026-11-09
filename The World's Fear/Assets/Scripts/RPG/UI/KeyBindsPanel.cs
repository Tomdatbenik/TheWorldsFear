using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // Click an action, press a key. Escape cancels and the left mouse button stays reserved for clicking and attacking.
    public class KeyBindsPanel : GamePanel
    {
        const float RowHeight = 70f;

        static KeyCode[] bindable;

        readonly Dictionary<GameAction, TextMeshProUGUI> keyTexts = new Dictionary<GameAction, TextMeshProUGUI>();
        GameAction? waiting;

        public static KeyBindsPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<KeyBindsPanel>(root, "Key Bindings", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            var left = Column(content, 0f, 0.49f);
            var right = Column(content, 0.51f, 1f);
            foreach (var a in KeyBinds.Actions) Row(a < GameAction.Skill1 ? left : right, a);

            var note = UIFactory.Text("Note", content, "Click an action, then press a key. Esc cancels. Taking a key that's in use swaps the two.", 26, new Color(1, 1, 1, 0.6f));
            note.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 74), new Vector2(1300, 40));
            var back = UIFactory.Button("Back", content, "Back", () => Panels.Toggle(PanelKind.Settings), null, 30);
            ((RectTransform)back.transform).Place(new Vector2(0, 0), Vector2.zero, new Vector2(240, 66));
            var reset = UIFactory.Button("Reset", content, "Reset to defaults", () => { Stop(); KeyBinds.ResetToDefaults(); Refresh(); }, new Color(0.4f, 0.15f, 0.1f, 1f), 30);
            ((RectTransform)reset.transform).Place(new Vector2(1, 0), Vector2.zero, new Vector2(340, 66));
        }

        static RectTransform Column(RectTransform parent, float xMin, float xMax)
        {
            var col = UIFactory.Rect("Column", parent);
            col.anchorMin = new Vector2(xMin, 0);
            col.anchorMax = new Vector2(xMax, 1);
            col.offsetMin = new Vector2(0, 124);
            col.offsetMax = Vector2.zero;
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            return col;
        }

        void Row(RectTransform parent, GameAction action)
        {
            var row = UIFactory.Rect(action.ToString(), parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            var label = UIFactory.Text("Action", row, KeyBinds.ActionName(action), 30, Palette.Text, TextAlignmentOptions.Left);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = new Vector2(0.62f, 1);
            label.rectTransform.offsetMin = new Vector2(12, 0);
            label.rectTransform.offsetMax = Vector2.zero;
            var button = UIFactory.Button("Key", row, "", () => Begin(action), null, 30);
            var rt = (RectTransform)button.transform;
            rt.anchorMin = new Vector2(0.64f, 0);
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            keyTexts[action] = button.GetComponentInChildren<TextMeshProUGUI>();
        }

        void Begin(GameAction action)
        {
            waiting = action;
            KeyBinds.Capturing = true;
            Refresh();
        }

        void Stop()
        {
            waiting = null;
            KeyBinds.Capturing = false;
        }

        void OnDisable() => Stop();

        void Update()
        {
            if (!waiting.HasValue) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Stop();
                Refresh();
                return;
            }
            foreach (var key in Bindable())
            {
                if (!Input.GetKeyDown(key)) continue;
                var action = waiting.Value;
                Stop();
                KeyBinds.Set(action, key);
                Refresh();
                return;
            }
        }

        static KeyCode[] Bindable()
        {
            if (bindable != null) return bindable;
            var list = new List<KeyCode>();
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                if (k != KeyCode.None && k != KeyCode.Escape && k != KeyCode.Mouse0 && k < KeyCode.JoystickButton0) list.Add(k);
            return bindable = list.ToArray();
        }

        public override void Refresh()
        {
            foreach (var kv in keyTexts)
            {
                bool listening = waiting == kv.Key;
                kv.Value.text = listening ? "Press a key..." : KeyBinds.Label(kv.Key);
                kv.Value.color = listening ? Palette.Gold : Palette.Text;
            }
        }
    }
}
