using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TWF
{
    // Main menu: continue, or start a new hero by picking a class.
    public class TitleScreen : MonoBehaviour
    {
        public Sprite background;
        public string worldScene = "World";

        RectTransform root, mainMenu, classMenu;
        WeaponType pickedWeapon = WeaponType.Sword;
        static readonly WeaponType[] starters = { WeaponType.Sword, WeaponType.Bow, WeaponType.Staff };
        TextMeshProUGUI classInfo;
        Image preview;
        readonly System.Collections.Generic.List<Image> cards = new System.Collections.Generic.List<Image>();

        // Set before loading the title to jump straight to picking a new hero's weapon.
        public static bool OpenNewHero;

        void Start()
        {
            Time.timeScale = 1f;
            var canvas = UIFactory.CreateCanvas("Title", 10);
            root = (RectTransform)canvas.transform;

            var bg = UIFactory.Image("Background", root, background != null ? background : GameAssets.Pixel,
                background != null ? Color.white : new Color(0.08f, 0.06f, 0.05f));
            bg.rectTransform.Stretch();
            if (background != null) bg.preserveAspect = false;
            var dim = UIFactory.Image("Dim", root, GameAssets.Pixel, new Color(0, 0, 0, 0.45f));
            dim.rectTransform.Stretch();

            var title = UIFactory.Text("Title", root, "The World's Fear", 110, Palette.Gold);
            title.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            title.outlineWidth = 0.2f;
            title.outlineColor = Color.black;
            title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(1600, 150));

            BuildMain();
            BuildClassSelect();
            ShowMain();
            if (OpenNewHero)
            {
                OpenNewHero = false;
                ShowClasses();
            }
        }

        void BuildMain()
        {
            mainMenu = UIFactory.Rect("Main", root).Place(new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(560, 420));
            var v = mainMenu.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 24;
            v.childForceExpandHeight = v.childForceExpandWidth = true;
            if (Profile.HasSave && Profile.Load())
            {
                var d = Profile.Data;
                UIFactory.Button("Continue", mainMenu, $"Continue\n<size=60%>Level {d.level}</size>", () => SceneManager.LoadScene(worldScene), new Color(0.2f, 0.4f, 0.15f, 1f), 46);
            }
            UIFactory.Button("NewGame", mainMenu, "New Hero", () =>
            {
                if (Profile.HasSave) ConfirmDialog.Show("Start a new hero?\n<size=70%>Your current hero will be lost.</size>", "New hero", ShowClasses);
                else ShowClasses();
            }, null, 46);
        }

        void BuildClassSelect()
        {
            classMenu = UIFactory.Rect("Classes", root).Stretch();
            classMenu.offsetMax = new Vector2(-60, -220);
            classMenu.offsetMin = new Vector2(60, 40);

            var row = UIFactory.Rect("Row", classMenu);
            row.anchorMin = new Vector2(0, 0.45f);
            row.anchorMax = new Vector2(0.6f, 1f);
            row.offsetMin = row.offsetMax = Vector2.zero;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 24;
            h.childForceExpandHeight = h.childForceExpandWidth = true;

            foreach (var wt in starters)
            {
                var w = wt;
                var card = UIFactory.Panel(w.ToString(), row, new Color(0.1f, 0.08f, 0.1f, 0.95f));
                cards.Add(card);
                var img = UIFactory.Image("Weapon", card.transform, Weapons.Sprite(w), Color.white);
                img.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(200, 200));
                img.preserveAspect = true;
                var name = UIFactory.Text("Name", card.transform, Weapons.Name(w), 44, Palette.Gold);
                name.fontStyle = FontStyles.Bold;
                name.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(400, 60));
                card.gameObject.AddComponent<Button>().onClick.AddListener(() => Pick(w));
            }

            var info = UIFactory.Panel("Info", classMenu, new Color(0.1f, 0.08f, 0.1f, 0.95f));
            info.rectTransform.anchorMin = new Vector2(0.62f, 0f);
            info.rectTransform.anchorMax = new Vector2(1f, 1f);
            info.rectTransform.offsetMin = info.rectTransform.offsetMax = Vector2.zero;
            classInfo = UIFactory.Text("Text", info.transform, "", 30, Palette.Text, TextAlignmentOptions.TopLeft);
            classInfo.rectTransform.Stretch(26);
            classInfo.rectTransform.offsetMin = new Vector2(26, 130);
            var start = UIFactory.Button("Start", info.transform, "Begin", StartNew, new Color(0.2f, 0.4f, 0.15f, 1f), 44);
            ((RectTransform)start.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(420, 90));

            var previewPanel = UIFactory.Panel("Preview", classMenu, new Color(0.1f, 0.08f, 0.1f, 0.6f));
            previewPanel.rectTransform.anchorMin = new Vector2(0, 0);
            previewPanel.rectTransform.anchorMax = new Vector2(0.6f, 0.42f);
            previewPanel.rectTransform.offsetMin = previewPanel.rectTransform.offsetMax = Vector2.zero;
            preview = UIFactory.Image("Anim", previewPanel.transform, null, Color.white);
            preview.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, 256));
            preview.preserveAspect = true;
            var back = UIFactory.Button("Back", previewPanel.transform, "Back", ShowMain, null, 30);
            ((RectTransform)back.transform).Place(new Vector2(0, 0), new Vector2(20, 20), new Vector2(200, 70));
        }

        void Pick(WeaponType w)
        {
            pickedWeapon = w;
            var p = Weapons.Get(w);
            var sb = new System.Text.StringBuilder();
            sb.Append($"<size=130%><b><color=#ffd040>{Weapons.Name(w)}</color></b></size>\n\n");
            sb.Append(w == WeaponType.Sword ? "A balanced blade: quick arcs in front of you." :
                      w == WeaponType.Bow ? "Fast homing arrows from a safe distance." : "Slow arcane orbs that explode on impact.");
            sb.Append($"\n\n{(p.ranged ? "Ranged" : "Melee")} · range {p.range:0.#}m · {p.attacksPerSecond:0.##} attacks/s");
            sb.Append("\n\n<color=#aaa>There are no classes: find any of 9 weapon types, spend attribute points as you level, " +
                      "and craft your own skills from elements, deliveries and modifiers at the Arcane Forge.</color>");
            sb.Append("\n\n<color=#f88>One life. If you fall, the run is over.</color>");
            classInfo.text = sb.ToString();
            for (int i = 0; i < cards.Count; i++)
                cards[i].color = starters[i] == w ? new Color(0.3f, 0.22f, 0.12f, 1f) : new Color(0.1f, 0.08f, 0.1f, 0.95f);
        }

        float animT;
        int animFrame;

        void Update()
        {
            if (classMenu == null || !classMenu.gameObject.activeSelf || preview == null) return;
            animT += Time.deltaTime;
            if (animT > 0.14f)
            {
                animT = 0f;
                animFrame++;
            }
            var set = CharacterSpriteGenerator.Get(Outfit.Adventurer);
            int dir = (animFrame / 16) % 4;
            preview.sprite = (animFrame / 8) % 2 == 0 ? set.walk[dir][animFrame % 4] : set.attack[dir][animFrame % 2];
        }

        void ShowMain()
        {
            mainMenu.gameObject.SetActive(true);
            classMenu.gameObject.SetActive(false);
        }

        void ShowClasses()
        {
            mainMenu.gameObject.SetActive(false);
            classMenu.gameObject.SetActive(true);
            Pick(pickedWeapon);
        }

        void StartNew()
        {
            Profile.DeleteSave();
            Profile.NewGame(pickedWeapon);
            Profile.Save();
            SceneManager.LoadScene(worldScene);
        }
    }
}
