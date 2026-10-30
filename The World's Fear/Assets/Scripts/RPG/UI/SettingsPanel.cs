using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TWF
{
    // Small settings menu: graphics quality, back to the main menu, or start a new hero.
    public class SettingsPanel : GamePanel
    {
        Button qualityButton;

        public static SettingsPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<SettingsPanel>(root, "Settings", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            var col = UIFactory.Rect("Buttons", content).Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 520));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 20;
            v.childForceExpandHeight = v.childForceExpandWidth = true;
            UIFactory.Button("Resume", col, "Resume", () => Panels.CloseAll(), new Color(0.2f, 0.4f, 0.15f, 1f), 36);
            qualityButton = UIFactory.Button("Quality", col, "", () => { RenderAssets.HighQuality = !RenderAssets.HighQuality; Refresh(); }, null, 32);
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
                    TitleScreen.OpenNewHero = true;
                    SceneManager.LoadScene("Title");
                }), new Color(0.4f, 0.15f, 0.1f, 1f), 32);
        }

        public override void Refresh()
        {
            qualityButton.GetComponentInChildren<TextMeshProUGUI>().text = $"Graphics: {(RenderAssets.HighQuality ? "High" : "Low (faster)")}";
        }
    }
}
