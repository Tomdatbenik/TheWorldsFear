using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TWF
{
    // Small settings menu: graphics quality, key bindings on desktop, back to the main menu, start a new hero, or quit on desktop.
    public class SettingsPanel : GamePanel
    {
        Button qualityButton, uiSizeButton;

        public static SettingsPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<SettingsPanel>(root, "Settings", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            var col = UIFactory.Rect("Buttons", content).Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, MobaInput.Desktop ? 820 : 520));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 20;
            v.childForceExpandHeight = v.childForceExpandWidth = true;
            UIFactory.Button("Resume", col, "Resume", () => Panels.CloseAll(), new Color(0.2f, 0.4f, 0.15f, 1f), 36);
            qualityButton = UIFactory.Button("Quality", col, "", () => { RenderAssets.HighQuality = !RenderAssets.HighQuality; Refresh(); }, null, 32);
            if (MobaInput.Desktop) uiSizeButton = UIFactory.Button("UiSize", col, "", () => { UIFactory.CycleUiScale(); Refresh(); }, null, 32);
            if (MobaInput.Desktop) UIFactory.Button("KeyBinds", col, "Key bindings", () => Panels.Toggle(PanelKind.KeyBinds), null, 32);
            UIFactory.Button("MainMenu", col, "Main menu", () =>
            {
                Profile.Save();
                Panels.CloseAll();
                SceneManager.LoadScene("Title");
            }, null, 32);
            UIFactory.Button("NewHero", col, "New hero", () =>
                ConfirmDialog.Show("Start a new hero?\n<size=70%>Your current hero will be lost.</size>", "New hero", () =>
                {
                    Panels.CloseAll();
                    CharacterCreator.Pending = null;
                    SceneManager.LoadScene("CharacterCreator");
                }), new Color(0.4f, 0.15f, 0.1f, 1f), 32);
            if (MobaInput.Desktop) UIFactory.Button("Quit", col, "Quit game", () =>
            {
                Profile.Save();
                Application.Quit();
            }, null, 32);
        }

        public override void Refresh()
        {
            qualityButton.GetComponentInChildren<TextMeshProUGUI>().text = $"Graphics: {(RenderAssets.HighQuality ? "High" : "Low (faster)")}";
            if (uiSizeButton != null)
                uiSizeButton.GetComponentInChildren<TextMeshProUGUI>().text = $"UI size: {Mathf.RoundToInt(UIFactory.UiScale * 100)}%";
        }
    }
}
