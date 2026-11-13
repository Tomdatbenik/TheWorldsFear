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

        RectTransform root, mainMenu, classMenu, legacyMenu, heroesMenu;
        Button beginButton;
        WeaponType pickedWeapon = WeaponType.Sword;
        static readonly WeaponType[] starters =
        {
            WeaponType.Sword, WeaponType.Axe, WeaponType.Hammer,
            WeaponType.Daggers, WeaponType.Spear, WeaponType.Bow,
            WeaponType.Crossbow, WeaponType.Staff, WeaponType.Wand,
        };
        const int CardsPerRow = 3;
        TextMeshProUGUI classInfo;
        Image preview, previewBack, previewFront;   // body, and the held weapon's layers behind and in front of it
        static readonly int[] PreviewDirections = { 6, 0, 2, 4 };   // front, right, back, left in Hero3DSet's order
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
            BuildLegacy();
            heroesMenu = UIFactory.Rect("Heroes", root).Stretch();
            heroesMenu.offsetMax = new Vector2(-60, -200);
            heroesMenu.offsetMin = new Vector2(60, 40);
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
                UIFactory.Button("Continue", mainMenu, $"Continue\n<size=60%>Hero {Profile.Slot + 1}  ·  Level {d.level}</size>", () => SceneManager.LoadScene(worldScene), new Color(0.2f, 0.4f, 0.15f, 1f), 46);
            }
            int used = 0;
            for (int i = 0; i < Profile.Slots; i++) if (Profile.SlotUsed(i)) used++;
            UIFactory.Button("Heroes", mainMenu, $"Heroes<size=60%><br>{used} of {Profile.Slots} slots used</size>", ShowHeroes, null, 46);
            UIFactory.Button("Achievements", mainMenu, "Achievements", ShowLegacy, new Color(0.35f, 0.27f, 0.1f, 1f), 46);
            if (MobaInput.Desktop) UIFactory.Button("Quit", mainMenu, "Quit", Application.Quit, null, 46);
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
            var v = row.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14;
            v.childForceExpandHeight = v.childForceExpandWidth = true;

            RectTransform cardRow = null;
            for (int i = 0; i < starters.Length; i++)
            {
                if (i % CardsPerRow == 0)
                {
                    cardRow = UIFactory.Rect("CardRow", row);
                    var h = cardRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                    h.spacing = 14;
                    h.childForceExpandHeight = h.childForceExpandWidth = true;
                }
                var w = starters[i];
                var card = UIFactory.Panel(w.ToString(), cardRow, new Color(0.1f, 0.08f, 0.1f, 0.95f));
                cards.Add(card);
                bool open = Legacy.WeaponUnlocked(w);
                var img = UIFactory.Image("Weapon", card.transform, Weapons.Sprite(w), open ? Color.white : new Color(0.2f, 0.2f, 0.2f, 0.8f));
                img.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(104, 104));
                img.preserveAspect = true;
                int runs = Legacy.RunsUntil(w);
                string label = open ? Weapons.Name(w) : $"<color=#888>{Weapons.Name(w)}</color>\n<size=55%><color=#c88>Locked: {runs} more run{(runs == 1 ? "" : "s")}</color></size>";
                var name = UIFactory.Text("Name", card.transform, label, 34, Palette.Gold, TextAlignmentOptions.Left);
                name.fontStyle = FontStyles.Bold;
                name.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(128, 0), new Vector2(200, 60));
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
            beginButton = start;
            ((RectTransform)start.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(420, 90));

            var previewPanel = UIFactory.Panel("Preview", classMenu, new Color(0.1f, 0.08f, 0.1f, 0.6f));
            previewPanel.rectTransform.anchorMin = new Vector2(0, 0);
            previewPanel.rectTransform.anchorMax = new Vector2(0.6f, 0.42f);
            previewPanel.rectTransform.offsetMin = previewPanel.rectTransform.offsetMax = Vector2.zero;
            previewBack = PreviewLayer("AnimBack", previewPanel.transform);
            preview = PreviewLayer("Anim", previewPanel.transform);
            previewFront = PreviewLayer("AnimFront", previewPanel.transform);
            var back = UIFactory.Button("Back", previewPanel.transform, "Back", ShowMain, null, 30);
            ((RectTransform)back.transform).Place(new Vector2(0, 0), new Vector2(20, 20), new Vector2(200, 70));
        }

        void Pick(WeaponType w)
        {
            pickedWeapon = w;
            var p = Weapons.Get(w);
            var sb = new System.Text.StringBuilder();
            sb.Append($"<size=130%><b><color=#ffd040>{Weapons.Name(w)}</color></b></size>\n\n");
            sb.Append(Blurb(w));
            if (p.trait.Length > 0) sb.Append($"\n\n<color=#ffd040><b>{p.trait}</b></color>: {p.traitText}");
            sb.Append($"\n\n{(p.ranged ? "Ranged" : "Melee")} · range {p.range:0.#}m · {p.attacksPerSecond:0.##} attacks/s");
            float guard = Weapons.GuardArmor(w, 1);
            if (guard > 0f) sb.Append($" · +{guard:0} armor");
            sb.Append("\n\n<color=#aaa>There are no classes: find any of 9 weapon types, spend attribute points as you level, " +
                      "and craft your own skills from elements, deliveries and modifiers at the Arcane Forge.</color>");
            sb.Append("\n\n<color=#f88>One life. If you fall, the run is over.</color>");
            bool open = Legacy.WeaponUnlocked(w);
            if (!open)
            {
                int runs = Legacy.RunsUntil(w);
                sb.Append($"\n\n<color=#f99><b>Locked.</b> Finish {runs} more run{(runs == 1 ? "" : "s")} to unlock it. A run is finished when your hero falls or defeats the World's Fear.</color>");
            }
            string bonus = Legacy.StartBonusText();
            if (bonus.Length > 0) sb.Append($"\n\n<color=#ffd040>{bonus}</color>");
            if (beginButton != null) beginButton.interactable = open;
            classInfo.text = sb.ToString();
            for (int i = 0; i < cards.Count; i++)
                cards[i].color = starters[i] == w ? new Color(0.3f, 0.22f, 0.12f, 1f) : new Color(0.1f, 0.08f, 0.1f, 0.95f);
        }

        static string Blurb(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Sword: return "A balanced blade that hits hard: quick arcs in front of you.";
                case WeaponType.Axe: return "Heavy, wide sweeps that hit everything beside you.";
                case WeaponType.Hammer: return "Slow, crushing blows all around you that can stun.";
                case WeaponType.Daggers: return "Very fast twin strikes up close that crit often and hard.";
                case WeaponType.Spear: return "Long reach in a narrow line, and every hit feeds you.";
                case WeaponType.Bow: return "A rapid stream of homing arrows from a safe distance.";
                case WeaponType.Crossbow: return "Slow, heavy bolts that pierce through enemies.";
                case WeaponType.Staff: return "Slow arcane orbs that explode on impact.";
                case WeaponType.Wand: return "Homing bolts that burst among the enemies they hit.";
                default: return "";
            }
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
            int turn = (animFrame / 16) % 4;
            bool attacking = (animFrame / 8) % 2 != 0;
            var hero = Hero3DSet.For((CharacterCreator.Pending ?? new Appearance()).Female);
            if (hero == null)
            {
                var set = CharacterSpriteGenerator.Get(Outfit.Adventurer);
                preview.sprite = attacking ? set.attack[turn][animFrame % 2] : set.walk[turn][animFrame % 4];
                preview.enabled = true;
                previewBack.enabled = previewFront.enabled = false;
                return;
            }
            // The same layered strips the hero plays in the world, holding the picked weapon.
            int dir = PreviewDirections[turn];
            string animation = attacking ? Hero3DSet.AttackFor(pickedWeapon) : "walk";
            string piece = Hero3DSet.WeaponPiece(pickedWeapon);
            ShowPreviewLayer(preview, hero.Get(animation, dir));
            ShowPreviewLayer(previewBack, hero.GetLayer(piece, false, animation, dir));
            ShowPreviewLayer(previewFront, hero.GetLayer(piece, true, animation, dir));
        }

        static Image PreviewLayer(string name, Transform parent)
        {
            var layer = UIFactory.Image(name, parent, null, Color.white);
            layer.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, 256));
            layer.preserveAspect = true;
            layer.enabled = false;
            return layer;
        }

        void ShowPreviewLayer(Image layer, Sprite[] frames)
        {
            layer.enabled = frames.Length > 0;
            if (layer.enabled) layer.sprite = frames[animFrame % frames.Length];
        }

        void ShowMain()
        {
            mainMenu.gameObject.SetActive(true);
            classMenu.gameObject.SetActive(false);
            legacyMenu.gameObject.SetActive(false);
            heroesMenu.gameObject.SetActive(false);
        }

        void ShowHeroes()
        {
            mainMenu.gameObject.SetActive(false);
            classMenu.gameObject.SetActive(false);
            legacyMenu.gameObject.SetActive(false);
            heroesMenu.gameObject.SetActive(true);
            BuildHeroes();
        }

        // The three hero slots: play or delete a hero, or start a new one in an empty slot.
        void BuildHeroes()
        {
            foreach (Transform c in heroesMenu) Destroy(c.gameObject);
            var row = UIFactory.Rect("Row", heroesMenu).Stretch();
            row.offsetMin = new Vector2(0, 110);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 24;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            h.childControlWidth = h.childControlHeight = true;
            for (int i = 0; i < Profile.Slots; i++) HeroCard(row, i);
            var back = UIFactory.Button("Back", heroesMenu, "Back", ShowMain, null, 30);
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(260, 70));
        }

        void HeroCard(RectTransform row, int slot)
        {
            var d = Profile.Peek(slot);
            bool last = slot == Profile.Slot && d != null;
            var card = UIFactory.Panel("Slot" + slot, row, last ? new Color(0.2f, 0.16f, 0.08f, 0.97f) : new Color(0.1f, 0.08f, 0.1f, 0.95f));
            var head = UIFactory.Text("Head", card.transform, $"Hero {slot + 1}", 30, new Color(0.7f, 0.65f, 0.6f));
            head.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(400, 44));

            if (d == null)
            {
                var empty = UIFactory.Text("Empty", card.transform, "<color=#888>Empty slot</color>", 36, Palette.Text);
                empty.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(400, 60));
                var make = UIFactory.Button("New", card.transform, "New Hero", () =>
                {
                    // New heroes start in the character creator, which comes back here for the weapon pick.
                    Profile.UseSlot(slot);
                    SceneManager.LoadScene("CharacterCreator");
                }, new Color(0.2f, 0.4f, 0.15f, 1f), 36);
                ((RectTransform)make.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(360, 90));
                return;
            }

            var weapon = WeaponType.Fists;
            foreach (var it in d.equipment)
                if (it != null && it.slot == ItemSlot.Weapon) { weapon = (WeaponType)it.weaponType; break; }
            var icon = UIFactory.Image("Weapon", card.transform, Weapons.Sprite(weapon), Color.white);
            icon.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(130, 130));
            icon.preserveAspect = true;
            string bosses = d.guardiansSlain.Count > 0 ? $"<br>Guardians slain: {d.guardiansSlain.Count}" : "";
            var info = UIFactory.Text("Info", card.transform,
                $"<b><color=#ffd040>Level {d.level}</color></b><br><size=80%>{Weapons.Name(weapon)}<br>" +
                $"<color=#bbb>Play time: {SettingsPanel.PlayTime(d.playTime)}<br>Furthest: {Mathf.RoundToInt(d.farthest)}m{bosses}</color></size>",
                42, Palette.Text, TextAlignmentOptions.Top);
            info.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -360), new Vector2(420, 260));

            var play = UIFactory.Button("Play", card.transform, "Play", () =>
            {
                Profile.UseSlot(slot);
                if (Profile.Load()) SceneManager.LoadScene(worldScene);
            }, new Color(0.2f, 0.4f, 0.15f, 1f), 36);
            ((RectTransform)play.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 120), new Vector2(360, 84));
            var del = UIFactory.Button("Delete", card.transform, "Delete", () =>
                ConfirmDialog.Show($"Delete Hero {slot + 1}?<br><size=70%>Level {d.level}. This can't be undone.</size>", "Delete", () =>
                {
                    Profile.DeleteSlot(slot);
                    BuildHeroes();
                }), new Color(0.4f, 0.15f, 0.1f, 1f), 28);
            ((RectTransform)del.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(240, 66));
        }

        void ShowLegacy()
        {
            mainMenu.gameObject.SetActive(false);
            classMenu.gameObject.SetActive(false);
            legacyMenu.gameObject.SetActive(true);
            heroesMenu.gameObject.SetActive(false);
        }

        // Achievements and unlocks: what every new hero carries over from the ones before.
        void BuildLegacy()
        {
            legacyMenu = UIFactory.Rect("Legacy", root).Stretch();
            legacyMenu.offsetMax = new Vector2(-60, -200);
            legacyMenu.offsetMin = new Vector2(60, 40);
            var panel = UIFactory.Panel("Panel", legacyMenu, new Color(0.1f, 0.08f, 0.1f, 0.95f));
            panel.rectTransform.Stretch();

            var ld = Legacy.Data;
            int got = 0;
            foreach (var a in Legacy.All) if (Legacy.Has(a.id)) got++;
            var open = Legacy.UnlockedWeapons();
            string next = Legacy.TiersOpen < Legacy.WeaponTiers.Length
                ? "  ·  Next run unlocks: " + string.Join(" and ", System.Array.ConvertAll(Legacy.WeaponTiers[Legacy.TiersOpen], Weapons.Name))
                : "";
            var head = UIFactory.Text("Head", panel.transform,
                $"<b>Achievements {got}/{Legacy.All.Count}</b>\n<size=70%><color=#ccc>Runs finished: {ld.runsFinished}  ·  Lairs destroyed: {ld.lairsDestroyed}  ·  Weapons: {open.Count}/9{next}</color></size>",
                38, Palette.Gold, TextAlignmentOptions.TopLeft);
            head.rectTransform.anchorMin = new Vector2(0, 1);
            head.rectTransform.anchorMax = new Vector2(1, 1);
            head.rectTransform.pivot = new Vector2(0.5f, 1);
            head.rectTransform.offsetMin = new Vector2(28, -128);
            head.rectTransform.offsetMax = new Vector2(-28, -18);

            var viewport = UIFactory.Rect("Viewport", panel.transform).Stretch();
            viewport.offsetMin = new Vector2(24, 110);
            viewport.offsetMax = new Vector2(-24, -140);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            var list = UIFactory.Rect("List", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            list.sizeDelta = Vector2.zero;
            var grid = list.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(860, 96);
            grid.spacing = new Vector2(16, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = list;

            foreach (var a in Legacy.All)
            {
                bool has = Legacy.Has(a.id);
                var row = UIFactory.Panel(a.id, list, has ? new Color(0.24f, 0.19f, 0.08f, 1f) : new Color(0.13f, 0.11f, 0.13f, 1f), has);
                var t = UIFactory.Text("Text", row.transform,
                    $"<b>{(has ? "<color=#ffd040>" : "<color=#999>")}{a.title}</color></b>  <size=75%><color=#bbb>{a.how}</color></size>\n" +
                    $"<size=80%>{(has ? "<color=#cfe8a0>" : "<color=#777>")}{a.bonus}</color></size>",
                    28, Palette.Text, TextAlignmentOptions.Left);
                t.rectTransform.Stretch(14);
            }

            var back = UIFactory.Button("Back", panel.transform, "Back", ShowMain, null, 30);
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(260, 70));
        }

        void ShowClasses()
        {
            mainMenu.gameObject.SetActive(false);
            classMenu.gameObject.SetActive(true);
            heroesMenu.gameObject.SetActive(false);
            legacyMenu.gameObject.SetActive(false);
            Pick(pickedWeapon);
        }

        void StartNew()
        {
            Profile.DeleteSave();
            Profile.NewGame(pickedWeapon, CharacterCreator.Pending);
            CharacterCreator.Pending = null;
            Profile.Save();
            SceneManager.LoadScene(worldScene);
        }
    }
}
