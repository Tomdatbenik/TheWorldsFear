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
        string pickedClass = "warrior";
        TextMeshProUGUI classInfo;
        Image preview;
        readonly System.Collections.Generic.List<Image> cards = new System.Collections.Generic.List<Image>();

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
                UIFactory.Button("Continue", mainMenu, $"Continue\n<size=60%>{Profile.Class.displayName} · Level {d.level}</size>", () => SceneManager.LoadScene(worldScene), new Color(0.2f, 0.4f, 0.15f, 1f), 46);
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

            foreach (var cls in GameDatabase.Instance.classes)
            {
                var c = cls;
                var card = UIFactory.Panel(c.id, row, new Color(0.1f, 0.08f, 0.1f, 0.95f));
                cards.Add(card);
                var img = UIFactory.Image("Hero", card.transform, CharacterSpriteGenerator.Get(HeroController.OutfitFor(c.id)).idle[0][0], Color.white);
                img.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(200, 230));
                img.preserveAspect = true;
                var name = UIFactory.Text("Name", card.transform, c.displayName, 44, c.accentColor);
                name.fontStyle = FontStyles.Bold;
                name.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(400, 60));
                card.gameObject.AddComponent<Button>().onClick.AddListener(() => Pick(c.id));
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

        void Pick(string id)
        {
            pickedClass = id;
            var cls = GameDatabase.Instance.FindClass(id);
            var sb = new System.Text.StringBuilder();
            sb.Append($"<size=130%><b><color={Palette.Hex(cls.accentColor)}>{cls.displayName}</color></b></size>\n\n{cls.description}\n\n");
            var stats = new CharacterStats();
            stats.SetBases(cls.baseStats);
            foreach (StatType st in new[] { StatType.MaxHP, StatType.Damage, StatType.Armor, StatType.AttackSpeed, StatType.MoveSpeed, StatType.CritChance })
                sb.Append($"{StatInfo.Name(st)}: <color=#fff>{StatInfo.FormatValue(st, stats.Get(st))}</color>\n");
            sb.Append($"\nAttack: <color=#fff>{(cls.basicAttack == HeroClassDefinition.BasicAttackKind.Melee ? "Melee" : "Ranged")}</color>");
            var starter = cls.skillTree.Find(cls.starterNode);
            if (starter != null) sb.Append($"\nStarting skill: <color=#fff>{starter.DisplayName}</color>");
            classInfo.text = sb.ToString();
            for (int i = 0; i < cards.Count; i++)
                cards[i].color = GameDatabase.Instance.classes[i].id == id ? new Color(0.3f, 0.22f, 0.12f, 1f) : new Color(0.1f, 0.08f, 0.1f, 0.95f);
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
            var set = CharacterSpriteGenerator.Get(HeroController.OutfitFor(pickedClass));
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
            Pick(pickedClass);
        }

        void StartNew()
        {
            Profile.DeleteSave();
            Profile.NewGame(pickedClass);
            Profile.Save();
            SceneManager.LoadScene(worldScene);
        }
    }
}
