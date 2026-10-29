using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // The cartographer sells the locations of boss lairs; bought lairs show up on the world map.
    public class CartographerPanel : GamePanel
    {
        RectTransform list;
        TextMeshProUGUI goldText;

        public static CartographerPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<CartographerPanel>(root, "Cartographer", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            var intro = UIFactory.Text("Intro", content,
                "\"I've charted every lair from here to the edge of the world. For a price, I'll mark one on your map.\"",
                26, Palette.MutedText, TextAlignmentOptions.Left);
            intro.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0), new Vector2(1500, 70));
            goldText = UIFactory.Text("Gold", content, "", 28, Palette.Gold, TextAlignmentOptions.Right);
            goldText.rectTransform.Place(new Vector2(1, 1), new Vector2(0, 0), new Vector2(400, 40));

            list = UIFactory.Rect("List", content);
            list.Stretch();
            list.offsetMax = new Vector2(0, -84);
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10;
            v.childForceExpandHeight = false;
            v.childControlHeight = false;
            v.childForceExpandWidth = true;
            v.childControlWidth = true;
        }

        public override void Refresh()
        {
            foreach (Transform c in list) Destroy(c.gameObject);
            goldText.text = $"{Profile.Data.gold} gold";
            var hero = HeroController.Instance;
            Vector2 from = hero != null ? (Vector2)hero.transform.position : Vector2.zero;

            foreach (var kind in WorldManager.BossOrder)
            {
                var row = UIFactory.Panel(kind.ToString(), list, new Color(0.12f, 0.1f, 0.12f, 0.95f));
                ((RectTransform)row.transform).sizeDelta = new Vector2(0, 112);
                var name = UIFactory.Text("Name", row.transform, $"{BossTitan.BossName(kind)}\n<size=70%><color=#aaa>{BossTitan.LairName(kind)}</color></size>",
                    30, kind == BossKind.WorldsFear ? new Color(1f, 0.45f, 0.35f) : Palette.Text, TextAlignmentOptions.Left);
                name.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(620, 100));

                string status;
                bool slain = WorldManager.Slain(kind);
                bool known = BossAtlas.Known(kind, out var lair);
                if (slain && kind != BossKind.WorldsFear) status = "<color=#8f8>Slain</color>";
                else if (known) status = $"<color=#8cf>Marked on your map: {BossAtlas.Directions(from, lair)}</color>";
                else if (!WorldManager.IsUnlocked(kind)) status = $"<color=#f88>Sealed until {BossTitan.BossName(WorldManager.Previous(kind))} falls</color>";
                else status = "<color=#ccc>Location unknown</color>";
                var st = UIFactory.Text("Status", row.transform, status, 24, Palette.Text, TextAlignmentOptions.Left);
                st.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(660, 0), new Vector2(560, 100));

                if (!known && !(slain && kind != BossKind.WorldsFear))
                {
                    var k = kind;
                    int price = BossAtlas.Price(kind);
                    var buy = UIFactory.Button("Buy", row.transform, $"Buy map  {price}g", () =>
                    {
                        if (BossAtlas.Buy(k))
                        {
                            Sound.Play(Sfx.Buy, Vector3.zero, 0.5f);
                            GameUI.Instance?.Toast($"{BossTitan.LairName(k)} marked on your map", Palette.Gold);
                        }
                        Refresh();
                    }, Profile.Data.gold >= price ? new Color(0.2f, 0.4f, 0.15f, 1f) : new Color(0.25f, 0.2f, 0.2f, 1f), 26);
                    ((RectTransform)buy.transform).Place(new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(300, 80));
                    buy.interactable = Profile.Data.gold >= price;
                }
            }
        }
    }
}
