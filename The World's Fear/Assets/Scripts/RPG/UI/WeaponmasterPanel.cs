using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // The weaponmaster teaches signature skills for each weapon type and attaches one per weapon.
    // The attached skill of the weapon in your hands goes on the fifth skill button.
    public class WeaponmasterPanel : GamePanel
    {
        RectTransform tabs, cards;
        TextMeshProUGUI goldText, hint;
        WeaponType shown;
        bool shownSet;

        public static WeaponmasterPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<WeaponmasterPanel>(root, "Weaponmaster", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            goldText = UIFactory.Text("Gold", content, "", 28, Palette.Gold, TextAlignmentOptions.Right);
            goldText.rectTransform.Place(new Vector2(1, 1), new Vector2(0, 0), new Vector2(400, 40));
            hint = UIFactory.Text("Hint", content, "", 24, Palette.MutedText, TextAlignmentOptions.Left);
            hint.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0), new Vector2(1300, 40));

            tabs = UIFactory.Rect("Tabs", content);
            tabs.anchorMin = new Vector2(0, 1);
            tabs.anchorMax = new Vector2(1, 1);
            tabs.pivot = new Vector2(0.5f, 1);
            tabs.sizeDelta = new Vector2(0, 120);
            tabs.anchoredPosition = new Vector2(0, -50);
            var h = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10;
            h.childForceExpandWidth = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            cards = UIFactory.Rect("Cards", content);
            cards.Stretch();
            cards.offsetMax = new Vector2(0, -190);
            var g = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = 24;
            g.childForceExpandWidth = true;
            g.childControlWidth = true;
            g.childControlHeight = true;
        }

        public override void Open()
        {
            base.Open();
            shown = Weapons.Equipped;   // start on the weapon in your hands
            shownSet = true;
        }

        public override void Refresh()
        {
            if (!shownSet) { shown = Weapons.Equipped; shownSet = true; }
            foreach (Transform c in tabs) Destroy(c.gameObject);
            foreach (Transform c in cards) Destroy(c.gameObject);
            goldText.text = $"{Profile.Data.gold} gold";
            hint.text = $"You are holding: <color=#fff>{Weapons.Name(Weapons.Equipped)}</color>   ·   Its attached skill is on the 5th skill button";

            for (int i = 0; i < 10; i++)
            {
                var w = (WeaponType)i;
                var tab = UIFactory.Image(w.ToString(), tabs, GameAssets.RoundedRect, w == shown ? new Color(0.35f, 0.28f, 0.12f) : new Color(0.1f, 0.09f, 0.1f), true);
                var icon = UIFactory.Image("Icon", tab.transform, Weapons.Sprite(w), Color.white);
                icon.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(70, 70));
                icon.preserveAspect = true;
                var label = UIFactory.Text("Name", tab.transform, Weapons.Name(w) + (WeaponSkills.Attached(w).HasValue ? " <color=#8f8>*</color>" : ""), 18,
                    w == Weapons.Equipped ? Palette.Gold : Palette.Text);
                label.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(140, 30));
                var captured = w;
                tab.gameObject.AddComponent<Button>().onClick.AddListener(() => { shown = captured; Refresh(); });
            }

            foreach (var id in WeaponSkills.For(shown)) Card(id);
        }

        void Card(WeaponSkillId id)
        {
            var color = WeaponSkills.ColorOf(id);
            var card = UIFactory.Panel(id.ToString(), cards, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            var icon = UIFactory.Image("Icon", card.transform, IconFactory.Get(WeaponSkills.Icon(id), color), Color.white);
            icon.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(120, 120));
            icon.preserveAspect = true;
            var title = UIFactory.Text("Title", card.transform, WeaponSkills.Name(id), 38, Palette.Text);
            title.fontStyle = FontStyles.Bold;
            title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(600, 50));
            var def = WeaponSkills.Get(id);
            var body = UIFactory.Text("Body", card.transform,
                $"{WeaponSkills.Describe(id)}\n\n<color=#aaa>{Weapons.Name(WeaponSkills.WeaponOf(id))} skill  ·  {def.baseCooldown:0} s cooldown</color>",
                24, Palette.Text, TextAlignmentOptions.Top);
            body.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -210), new Vector2(600, 180));

            bool owned = WeaponSkills.Owned(id);
            bool attached = WeaponSkills.Attached(WeaponSkills.WeaponOf(id)) == id;
            string label;
            Color bg;
            System.Action onClick;
            if (!owned)
            {
                int price = WeaponSkills.Price(id);
                label = $"Learn  {price}g";
                bg = Profile.Data.gold >= price ? new Color(0.2f, 0.4f, 0.15f, 1f) : new Color(0.25f, 0.2f, 0.2f, 1f);
                onClick = () =>
                {
                    if (WeaponSkills.Buy(id))
                    {
                        Sound.Play(Sfx.LevelUp, Vector3.zero, 0.4f);
                        GameUI.Instance?.Toast($"Learned {WeaponSkills.Name(id)}", Palette.Gold);
                    }
                    Refresh();
                };
            }
            else if (attached)
            {
                label = "Attached";
                bg = new Color(0.3f, 0.26f, 0.1f, 1f);
                onClick = null;
            }
            else
            {
                label = "Attach";
                bg = new Color(0.15f, 0.25f, 0.35f, 1f);
                onClick = () => { WeaponSkills.Attach(id); Sound.Play(Sfx.Buy, Vector3.zero, 0.4f); Refresh(); };
            }
            var btn = UIFactory.Button("Action", card.transform, label, onClick ?? (() => { }), bg, 30);
            ((RectTransform)btn.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(420, 84));
            btn.interactable = onClick != null && (owned || Profile.Data.gold >= WeaponSkills.Price(id));
        }
    }
}
