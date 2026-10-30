using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // Builds and runs the in-game HUD and touch controls.
    public class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; }

        Canvas canvas;
        RectTransform root;
        Image hpFill, hpShield, xpFill;
        TextMeshProUGUI hpText, levelText, goldText, xpText, zoneBanner, toastText, skillPointBadge;
        CanvasGroup bannerGroup, toastGroup;
        GameObject deathOverlay;
        TextMeshProUGUI deathText;
        RectTransform controls;
        Button interactButton;
        TextMeshProUGUI interactLabel;
        Image interactIcon;
        Image attackIcon;
        System.Action interactAction;
        Image potionCooldown;
        Combatant boss;
        Image bossFill;
        TextMeshProUGUI bossText;
        GameObject bossBar;
        TextMeshProUGUI potionCount;

        public RectTransform Root => root;
        public Canvas Canvas => canvas;

        void Awake()
        {
            Instance = this;
            canvas = UIFactory.CreateCanvas("GameUI", 10);
            canvas.transform.SetParent(transform, false);
            root = (RectTransform)canvas.transform;
            BuildHUD();
            BuildControls();
            BuildOverlays();
        }

        void OnEnable()
        {
            Profile.LeveledUp += OnLevelUp;
        }

        void OnDisable()
        {
            Profile.LeveledUp -= OnLevelUp;
        }

        // ---------- HUD ----------

        void BuildHUD()
        {
            var top = UIFactory.Panel("Status", root, new Color(0.05f, 0.04f, 0.06f, 0.75f));
            top.rectTransform.Place(new Vector2(0, 1), new Vector2(24, -24), new Vector2(560, 150));

            var badge = UIFactory.Circle("LevelBadge", top.transform, 110, new Color(0.18f, 0.12f, 0.08f, 1f));
            badge.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(110, 110));
            var badgeRing = UIFactory.Image("ring", badge.transform, GameAssets.Ring, Palette.Gold);
            badgeRing.rectTransform.Stretch();
            levelText = UIFactory.Text("Level", badge.transform, "1", 46, Palette.Gold);
            levelText.fontStyle = FontStyles.Bold;
            levelText.rectTransform.Stretch();

            var hpBar = UIFactory.Bar("HP", top.transform, Palette.HP, out hpFill);
            hpBar.rectTransform.Place(new Vector2(0, 1), new Vector2(145, -22), new Vector2(395, 46));
            hpShield = UIFactory.Image("shield", hpBar.transform, GameAssets.Pixel, new Color(Palette.Shield.r, Palette.Shield.g, Palette.Shield.b, 0.7f));
            hpShield.type = Image.Type.Filled;
            hpShield.fillMethod = Image.FillMethod.Horizontal;
            hpShield.rectTransform.anchorMin = new Vector2(0, 0);
            hpShield.rectTransform.anchorMax = new Vector2(1, 0.3f);
            hpShield.rectTransform.offsetMin = new Vector2(4, 4);
            hpShield.rectTransform.offsetMax = new Vector2(-4, 0);
            hpText = UIFactory.Text("hpText", hpBar.transform, "", 26, Color.white);
            hpText.rectTransform.Stretch();
            hpText.fontStyle = FontStyles.Bold;

            var xpBar = UIFactory.Bar("XP", top.transform, Palette.XP, out xpFill);
            xpBar.rectTransform.Place(new Vector2(0, 1), new Vector2(145, -76), new Vector2(395, 22));
            xpText = UIFactory.Text("xpText", xpBar.transform, "", 16, Color.white);
            xpText.rectTransform.Stretch();

            goldText = UIFactory.Text("Gold", top.transform, "", 26, Palette.Gold, TextAlignmentOptions.Left);
            goldText.rectTransform.Place(new Vector2(0, 1), new Vector2(148, -104), new Vector2(260, 34));

            // Menu buttons, top right.
            var menu = UIFactory.Rect("Menu", root).Place(new Vector2(1, 1), new Vector2(-24, -24), new Vector2(552, 100));
            var layout = menu.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            var skillsBtn = UIFactory.IconButton("SkillsBtn", menu, "book", new Color(0.75f, 0.55f, 0.95f), () => Panels.Toggle(PanelKind.Skills));
            skillPointBadge = UIFactory.Text("Badge", skillsBtn.transform, "", 22, Color.white);
            skillPointBadge.rectTransform.Place(new Vector2(1, 1), new Vector2(10, 10), new Vector2(44, 44));
            var badgeBg = UIFactory.Circle("bg", skillPointBadge.transform, 44, new Color(0.85f, 0.2f, 0.2f));
            badgeBg.rectTransform.Stretch();
            badgeBg.transform.SetAsFirstSibling();
            UIFactory.IconButton("CraftBtn", menu, "arcane", new Color(1f, 0.6f, 0.3f), () => { CraftPanel.AtForge = false; Panels.Toggle(PanelKind.Craft); });
            UIFactory.IconButton("BagBtn", menu, "bag", new Color(0.8f, 0.55f, 0.3f), () => Panels.Toggle(PanelKind.Inventory));
            UIFactory.IconButton("MapBtn", menu, "map", new Color(0.85f, 0.75f, 0.5f), () => Panels.Toggle(PanelKind.Map));
            UIFactory.IconButton("CodexBtn", menu, "codex", new Color(0.6f, 0.85f, 0.75f), () => Panels.Toggle(PanelKind.Codex));

            var bannerRt = UIFactory.Rect("ZoneBanner", root).Place(new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(1100, 190));
            bannerGroup = bannerRt.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;
            zoneBanner = UIFactory.Text("Text", bannerRt, "", 64, Palette.Text);
            zoneBanner.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            zoneBanner.rectTransform.Stretch();
            zoneBanner.outlineWidth = 0.2f;
            zoneBanner.outlineColor = Color.black;

            var toastRt = UIFactory.Rect("Toast", root).Place(new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1200, 100));
            toastGroup = toastRt.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.blocksRaycasts = false;
            toastText = UIFactory.Text("Text", toastRt, "", 44, Palette.Gold);
            toastText.fontStyle = FontStyles.Bold;
            toastText.enableAutoSizing = true;   // long messages shrink instead of spilling into the zone banner
            toastText.fontSizeMin = 24;
            toastText.fontSizeMax = 44;
            toastText.rectTransform.Stretch();
            toastText.outlineWidth = 0.2f;
            toastText.outlineColor = Color.black;
        }

        // ---------- Touch controls ----------

        void BuildControls()
        {
            controls = UIFactory.Rect("Controls", root).Stretch();

            // Left half: floating joystick zone.
            var zone = UIFactory.Image("JoystickZone", controls, GameAssets.Pixel, new Color(0, 0, 0, 0), true);
            zone.rectTransform.anchorMin = new Vector2(0, 0);
            zone.rectTransform.anchorMax = new Vector2(0.45f, 0.65f);
            zone.rectTransform.offsetMin = zone.rectTransform.offsetMax = Vector2.zero;
            var baseImg = UIFactory.Circle("Base", zone.transform, 260, new Color(1, 1, 1, 0.12f));
            baseImg.rectTransform.Place(new Vector2(0, 0), new Vector2(200, 170), new Vector2(260, 260));
            baseImg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            baseImg.rectTransform.anchoredPosition = new Vector2(330, 300);
            var baseRing = UIFactory.Image("Ring", baseImg.transform, GameAssets.Ring, new Color(1, 1, 1, 0.35f));
            baseRing.rectTransform.Stretch();
            var handle = UIFactory.Circle("Handle", baseImg.transform, 120, new Color(1, 1, 1, 0.45f));
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var joy = zone.gameObject.AddComponent<VirtualJoystick>();
            joy.baseRect = baseImg.rectTransform;
            joy.handle = handle.rectTransform;
            joy.radius = 110f;

            // Right side: attack button with skills in an arc around it.
            var cluster = UIFactory.Rect("SkillCluster", controls).Place(new Vector2(1, 0), new Vector2(-60, 50), new Vector2(520, 520));

            var attack = UIFactory.Circle("Attack", cluster, 210, new Color(0.35f, 0.12f, 0.1f, 0.85f), true);
            attack.rectTransform.Place(new Vector2(1, 0), new Vector2(-20, 20), new Vector2(210, 210));
            var atkRing = UIFactory.Image("Ring", attack.transform, GameAssets.Ring, new Color(1f, 0.75f, 0.5f, 0.9f));
            atkRing.rectTransform.Stretch();
            attackIcon = UIFactory.Image("Icon", attack.transform, null, Color.white);
            attackIcon.rectTransform.Stretch(45);
            attackIcon.preserveAspect = true;
            attack.gameObject.AddComponent<AttackButton>();

            var cancel = UIFactory.Circle("Cancel", controls, 130, new Color(0.6f, 0.1f, 0.1f, 0.75f), true);
            cancel.rectTransform.Place(new Vector2(1, 0), new Vector2(-560, 520), new Vector2(130, 130));
            var cancelText = UIFactory.Text("x", cancel.transform, "Cancel", 26, Color.white);
            cancelText.rectTransform.Stretch();
            cancel.gameObject.SetActive(false);

            // Positions relative to the attack button, MOBA style arc.
            Vector2 atkCenter = new Vector2(-125, 125);
            // Slot 4 is the weapon skill, out on the left of the arc.
            // Slots 5 and 6 come from unique armor sockets and only appear when filled.
            Vector2[] offsets = { new Vector2(-235, -10), new Vector2(-195, 150), new Vector2(-50, 245), new Vector2(-340, 280), new Vector2(-400, 70),
                                  new Vector2(-510, 200), new Vector2(-470, 370) };
            float[] sizes = { 135, 135, 135, 155, 120, 110, 110 };
            for (int i = 0; i < HeroController.SlotCount; i++)
                MakeSkillButton(cluster, i, atkCenter + offsets[i], sizes[i], cancel.rectTransform);

            // Camera rotation buttons, under the menu.
            for (int i = 0; i < 2; i++)
            {
                float dir = i == 0 ? -1f : 1f;
                var rb = UIFactory.Circle(i == 0 ? "RotateLeft" : "RotateRight", controls, 96, new Color(0.1f, 0.1f, 0.12f, 0.7f), true);
                rb.rectTransform.Place(new Vector2(1, 1), new Vector2(i == 0 ? -140 : -24, -130), new Vector2(96, 96));
                var ring = UIFactory.Image("Ring", rb.transform, GameAssets.Ring, new Color(1, 1, 1, 0.5f));
                ring.rectTransform.Stretch();
                var ri = UIFactory.Image("Icon", rb.transform, IconFactory.Get(i == 0 ? "rotl" : "rotr", new Color(0.85f, 0.85f, 0.9f)), Color.white);
                ri.rectTransform.Stretch(16);
                ri.preserveAspect = true;
                rb.gameObject.AddComponent<RotateButton>().direction = dir;
            }

            // Potion button, left of the skill arc.
            var potion = UIFactory.Circle("Potion", controls, 120, new Color(0.3f, 0.06f, 0.08f, 0.85f), true);
            potion.rectTransform.Place(new Vector2(1, 0), new Vector2(-640, 40), new Vector2(120, 120));
            var potRing = UIFactory.Image("Ring", potion.transform, GameAssets.Ring, new Color(1f, 0.45f, 0.45f, 0.9f));
            potRing.rectTransform.Stretch();
            var potIcon = UIFactory.Image("Icon", potion.transform, Pickup.PotionIcon, Color.white);
            potIcon.rectTransform.Stretch(26);
            potIcon.preserveAspect = true;
            potionCooldown = UIFactory.Image("Cooldown", potion.transform, GameAssets.Circle, new Color(0, 0, 0, 0.65f));
            potionCooldown.type = Image.Type.Filled;
            potionCooldown.fillMethod = Image.FillMethod.Radial360;
            potionCooldown.fillOrigin = (int)Image.Origin360.Top;
            potionCooldown.rectTransform.Stretch();
            potionCount = UIFactory.Text("Count", potion.transform, "", 30, Color.white);
            potionCount.fontStyle = FontStyles.Bold;
            potionCount.rectTransform.Place(new Vector2(1, 0), new Vector2(6, -6), new Vector2(50, 40));
            potion.gameObject.AddComponent<Button>().onClick.AddListener(() => HeroController.Instance?.UsePotion());

            // Context button (talk to merchant, etc.).
            interactButton = UIFactory.Button("Interact", controls, "", () => interactAction?.Invoke(), new Color(0.35f, 0.25f, 0.08f, 0.95f), 40);
            ((RectTransform)interactButton.transform).Place(new Vector2(1, 0), new Vector2(-600, 220), new Vector2(240, 100));
            interactLabel = interactButton.GetComponentInChildren<TextMeshProUGUI>();
            interactLabel.rectTransform.offsetMin = new Vector2(90, 6);
            interactLabel.alignment = TextAlignmentOptions.Left;
            interactIcon = UIFactory.Image("Icon", interactButton.transform, null, Color.white);
            interactIcon.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(72, 72));
            interactIcon.preserveAspect = true;
            interactButton.gameObject.SetActive(false);

            if (!Application.isMobilePlatform)
            {
                var hint = UIFactory.Text("KeyHint", controls, "WASD move · Space/Click attack · Q E F skills · R ultimate · H potion · Z/C rotate camera", 22, new Color(1, 1, 1, 0.5f));
                hint.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(1000, 36));
            }
        }

        void MakeSkillButton(RectTransform parent, int slot, Vector2 pos, float size, RectTransform cancel)
        {
            var bg = UIFactory.Circle("Skill" + slot, parent, size, new Color(0, 0, 0, 0.5f), true);
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(1, 0);
            bg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bg.rectTransform.anchoredPosition = pos;
            var ring = UIFactory.Image("Ring", bg.transform, GameAssets.Ring, Color.white);
            ring.rectTransform.Stretch();
            var glyph = UIFactory.Text("Glyph", bg.transform, "", size * 0.3f, Color.white);
            glyph.fontStyle = FontStyles.Bold;
            glyph.textWrappingMode = TextWrappingModes.NoWrap;
            glyph.enableAutoSizing = true;
            glyph.fontSizeMin = 14;
            glyph.fontSizeMax = size * 0.3f;
            glyph.rectTransform.Stretch(8);
            var skillIcon = UIFactory.Image("Icon", bg.transform, null, Color.white);
            skillIcon.rectTransform.Stretch(size * 0.2f);
            skillIcon.preserveAspect = true;
            skillIcon.enabled = false;
            var overlay = UIFactory.Image("Cooldown", bg.transform, GameAssets.Circle, new Color(0, 0, 0, 0.65f));
            overlay.type = Image.Type.Filled;
            overlay.fillMethod = Image.FillMethod.Radial360;
            overlay.fillOrigin = (int)Image.Origin360.Top;
            overlay.fillClockwise = false;
            overlay.fillAmount = 0f;
            overlay.rectTransform.Stretch();
            var cdText = UIFactory.Text("CdText", bg.transform, "", size * 0.3f, Color.white);
            cdText.fontStyle = FontStyles.Bold;
            cdText.rectTransform.Stretch();
            var rank = UIFactory.Text("Rank", bg.transform, "", 20, Palette.Gold);
            rank.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, -6), new Vector2(80, 26));
            if (!Application.isMobilePlatform)
            {
                var key = UIFactory.Text("Key", bg.transform, MobaInput.KeyName(slot), 20, new Color(1, 1, 1, 0.6f));
                key.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0), new Vector2(30, 26));
            }

            var btn = bg.gameObject.AddComponent<SkillButton>();
            btn.slot = slot;
            btn.cooldownOverlay = overlay;
            btn.cooldownText = cdText;
            btn.glyph = glyph;
            btn.icon = skillIcon;
            btn.ring = ring;
            btn.background = bg;
            btn.rankText = rank;
            btn.cancelZone = cancel;
        }

        // ---------- Overlays ----------

        void BuildOverlays()
        {
            var dim = UIFactory.Image("Death", root, GameAssets.Pixel, new Color(0.25f, 0f, 0f, 0.55f), true);
            dim.rectTransform.Stretch();
            deathText = UIFactory.Text("Text", dim.transform, "", 80, new Color(1f, 0.85f, 0.8f));
            deathText.fontStyle = FontStyles.Bold;
            deathText.rectTransform.Stretch();
            deathOverlay = dim.gameObject;
            deathOverlay.SetActive(false);
        }

        public void ShowDeath(string text)
        {
            deathOverlay.SetActive(true);
            deathText.text = text;
        }

        public void HideDeath() => deathOverlay.SetActive(false);

        public void ShowDeathButton(string label, System.Action action)
        {
            var b = UIFactory.Button("DeathButton", deathOverlay.transform, label, () => action(), new Color(0.35f, 0.1f, 0.08f, 1f), 40);
            ((RectTransform)b.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 120), new Vector2(420, 100));
        }

        public void SetControlsVisible(bool visible) => controls.gameObject.SetActive(visible);

        RectTransform barTop, barBottom;
        CanvasGroup titleGroup;
        TextMeshProUGUI titleText;
        GameObject hudStatus, hudMenu;

        // Letterbox bars for cutscenes; hides the HUD and controls.
        public void SetCinematic(bool on)
        {
            if (barTop == null)
            {
                barTop = UIFactory.Image("BarTop", root, GameAssets.Pixel, Color.black).rectTransform;
                barTop.anchorMin = new Vector2(0, 1); barTop.anchorMax = new Vector2(1, 1); barTop.pivot = new Vector2(0.5f, 1);
                barBottom = UIFactory.Image("BarBottom", root, GameAssets.Pixel, Color.black).rectTransform;
                barBottom.anchorMin = new Vector2(0, 0); barBottom.anchorMax = new Vector2(1, 0); barBottom.pivot = new Vector2(0.5f, 0);
                hudStatus = root.Find("Status")?.gameObject;
                hudMenu = root.Find("Menu")?.gameObject;
            }
            StopCoroutine(nameof(AnimateBars));
            StartCoroutine(AnimateBars(on ? 150f : 0f));
            SetControlsVisible(!on);
            if (hudStatus) hudStatus.SetActive(!on);
            if (hudMenu) hudMenu.SetActive(!on);
        }

        IEnumerator AnimateBars(float target)
        {
            float start = barTop.sizeDelta.y;
            for (float t = 0; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                float h = Mathf.Lerp(start, target, t / 0.5f);
                barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0, h);
                yield return null;
            }
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0, target);
        }

        // Big boss name card during the entrance.
        public void ShowBossTitle(string title, string subtitle)
        {
            if (titleText == null)
            {
                var rt = UIFactory.Rect("BossTitle", root).Place(new Vector2(0.5f, 0.5f), new Vector2(0, -180), new Vector2(1600, 220));
                titleGroup = rt.gameObject.AddComponent<CanvasGroup>();
                titleGroup.blocksRaycasts = false;
                titleText = UIFactory.Text("Text", rt, "", 96, new Color(1f, 0.5f, 0.35f));
                titleText.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
                titleText.outlineWidth = 0.25f;
                titleText.outlineColor = Color.black;
                titleText.rectTransform.Stretch();
            }
            titleText.text = $"{title}\n<size=40%><color=#ddd>{subtitle}</color></size>";
            StopCoroutine(nameof(FadeTitle));
            StartCoroutine(FadeTitle());
        }

        IEnumerator FadeTitle()
        {
            for (float t = 0; t < 0.4f; t += Time.unscaledDeltaTime)
            {
                titleGroup.alpha = t / 0.4f;
                titleText.transform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, t / 0.4f);
                yield return null;
            }
            titleGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(2f);
            for (float t = 0; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                titleGroup.alpha = 1f - t / 0.6f;
                yield return null;
            }
            titleGroup.alpha = 0f;
        }

        public void ShowBossBar(Combatant c)
        {
            boss = c;
            if (bossBar == null)
            {
                var bg = UIFactory.Bar("BossBar", root, new Color(0.8f, 0.2f, 0.1f), out bossFill);
                bg.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -190), new Vector2(900, 40));
                bossText = UIFactory.Text("Name", bg.transform, "", 30, Color.white);
                bossText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, 44), new Vector2(900, 44));
                bossText.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
                bossBar = bg.gameObject;
            }
            bossText.text = c.displayName;
            bossBar.SetActive(true);
        }

        public void ShowInteract(string label, System.Action action, string iconKey = null)
        {
            interactAction = action;
            interactLabel.text = label;
            if (iconKey == null) iconKey = label == "Shop" ? "coin" : label == "Travel" ? "portal" : label == "Forge" ? "anvil" : null;
            interactIcon.sprite = IconFactory.Get(iconKey, new Color(1f, 0.85f, 0.4f));
            interactIcon.enabled = interactIcon.sprite != null;
            interactButton.gameObject.SetActive(true);
        }

        public void HideInteract()
        {
            interactAction = null;
            interactButton.gameObject.SetActive(false);
        }

        // ---------- Updates ----------

        void Update()
        {
            var hero = HeroController.Instance;
            var data = Profile.Data;
            if (hero != null)
            {
                var c = hero.Combatant;
                hpFill.fillAmount = c.HPFraction;
                hpShield.fillAmount = c.MaxHP > 0 ? Mathf.Clamp01(c.ShieldHP / c.MaxHP) : 0f;
                hpText.text = $"{Mathf.CeilToInt(c.HP)} / {Mathf.RoundToInt(c.MaxHP)}";
                potionCooldown.fillAmount = hero.PotionCooldownFraction;
            }
            if (bossBar != null)
            {
                bossBar.SetActive(boss != null && !boss.IsDead);
                if (boss != null) bossFill.fillAmount = boss.HPFraction;
            }
            // The attack button shows the equipped weapon.
            if (hero != null) attackIcon.sprite = Weapons.Sprite(hero.Weapon.type);
            levelText.text = data.level.ToString();
            potionCount.text = data.potions.ToString();
            xpFill.fillAmount = Profile.XPFraction;
            xpText.text = data.level >= Profile.MaxLevel ? "MAX LEVEL" : $"{data.xp} / {Profile.XPToNext(data.level)} XP";
            goldText.text = $"{data.gold} gold";
            skillPointBadge.transform.parent.gameObject.SetActive(true);
            int points = data.skillPoints + data.attributePoints;
            skillPointBadge.gameObject.SetActive(points > 0);
            skillPointBadge.text = points.ToString();
        }

        void OnLevelUp(int level) => Toast($"Level {level}!  +1 skill point", Palette.Gold);

        public void Toast(string text, Color color, float duration = 2.2f)
        {
            toastText.text = text;
            toastText.color = color;
            StopCoroutine(nameof(FadeToast));
            StartCoroutine(FadeToast(duration));
        }

        IEnumerator FadeToast(float duration)
        {
            toastGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(duration);
            for (float t = 0; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                toastGroup.alpha = 1f - t / 0.5f;
                yield return null;
            }
            toastGroup.alpha = 0f;
        }

        public void ShowZoneBanner(string title, string subtitle, Color color)
        {
            zoneBanner.text = $"{title}\n<size=34><color=#ccc>{subtitle}</color></size>";
            zoneBanner.color = color;
            StopCoroutine(nameof(FadeBanner));
            StartCoroutine(FadeBanner());
        }

        IEnumerator FadeBanner()
        {
            for (float t = 0; t < 0.4f; t += Time.unscaledDeltaTime)
            {
                bannerGroup.alpha = t / 0.4f;
                yield return null;
            }
            bannerGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(2.5f);
            for (float t = 0; t < 0.8f; t += Time.unscaledDeltaTime)
            {
                bannerGroup.alpha = 1f - t / 0.8f;
                yield return null;
            }
            bannerGroup.alpha = 0f;
        }
    }
}
