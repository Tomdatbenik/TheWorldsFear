using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TWF
{
    // Buy gear and potions from the merchant, or sell your loot.
    public class ShopPanel : GamePanel
    {
        RectTransform stockArea, bagArea;
        TextMeshProUGUI detailText, goldText, potionText;
        Button actionButton, potionButton, restockButton;
        ItemInstance selected;
        bool selectedFromShop;

        public static ShopPanel Create(Transform root)
        {
            var content = PanelsBuilder.Frame<ShopPanel>(root, "Merchant", out var p);
            p.Build(content);
            return p;
        }

        void Build(RectTransform content)
        {
            var stockTitle = UIFactory.Text("StockTitle", content, "For sale", 30, Palette.Gold, TextAlignmentOptions.Left);
            stockTitle.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0), new Vector2(400, 40));
            stockArea = UIFactory.Rect("Stock", content);
            stockArea.anchorMin = new Vector2(0, 0.52f);
            stockArea.anchorMax = new Vector2(0.32f, 1f);
            stockArea.offsetMin = Vector2.zero;
            stockArea.offsetMax = new Vector2(0, -46);
            Grid(stockArea, 4);

            var potions = UIFactory.Panel("Potions", content, new Color(0.18f, 0.08f, 0.1f, 0.95f));
            potions.rectTransform.anchorMin = new Vector2(0, 0.3f);
            potions.rectTransform.anchorMax = new Vector2(0.32f, 0.5f);
            potions.rectTransform.offsetMin = potions.rectTransform.offsetMax = Vector2.zero;
            var icon = UIFactory.Image("Icon", potions.transform, Pickup.PotionIcon, Color.white);
            icon.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(80, 96));
            icon.preserveAspect = true;
            potionText = UIFactory.Text("Text", potions.transform, "", 26, Palette.Text, TextAlignmentOptions.Left);
            potionText.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(260, 100));
            potionButton = UIFactory.Button("BuyPotion", potions.transform, "Buy", () =>
            {
                if (Profile.BuyPotion()) Sound.Play(Sfx.Buy, Vector3.zero, 0.5f);
                Refresh();
            }, new Color(0.2f, 0.4f, 0.15f, 1f), 28);
            ((RectTransform)potionButton.transform).Place(new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(180, 80));

            restockButton = UIFactory.Button("Restock", content, "", () =>
            {
                if (Profile.Data.gold < Profile.RefreshPrice) return;
                Profile.AddGold(-Profile.RefreshPrice);
                Profile.RollShopStock();
                selected = null;
                Refresh();
            }, null, 26);
            ((RectTransform)restockButton.transform).Place(new Vector2(0, 0), new Vector2(0, 110), new Vector2(560, 76));

            goldText = UIFactory.Text("Gold", content, "", 34, Palette.Gold, TextAlignmentOptions.Left);
            goldText.rectTransform.Place(new Vector2(0, 0), new Vector2(6, 20), new Vector2(560, 60));

            var bagTitle = UIFactory.Text("BagTitle", content, "Your bag", 30, Palette.Gold, TextAlignmentOptions.Left);
            bagTitle.rectTransform.Place(new Vector2(0.34f, 1), new Vector2(0, 0), new Vector2(400, 40));
            bagArea = UIFactory.Rect("Bag", content);
            bagArea.anchorMin = new Vector2(0.34f, 0f);
            bagArea.anchorMax = new Vector2(0.64f, 1f);
            bagArea.offsetMin = Vector2.zero;
            bagArea.offsetMax = new Vector2(0, -46);
            Grid(bagArea, 5);

            var detail = UIFactory.Panel("Detail", content, new Color(0.12f, 0.1f, 0.12f, 0.95f));
            detail.rectTransform.anchorMin = new Vector2(0.66f, 0);
            detail.rectTransform.anchorMax = new Vector2(1, 1);
            detail.rectTransform.offsetMin = detail.rectTransform.offsetMax = Vector2.zero;
            detailText = UIFactory.Text("Text", detail.transform, "", 28, Palette.Text, TextAlignmentOptions.TopLeft);
            detailText.rectTransform.Stretch(22);
            detailText.rectTransform.offsetMin = new Vector2(22, 130);
            actionButton = UIFactory.Button("Action", detail.transform, "Buy", OnAction, new Color(0.2f, 0.4f, 0.15f, 1f), 32);
            ((RectTransform)actionButton.transform).Place(new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(460, 86));
        }

        static void Grid(RectTransform rt, int columns)
        {
            var grid = rt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(100, 100);
            grid.spacing = new Vector2(10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
        }

        public override void Refresh()
        {
            foreach (Transform c in stockArea) Destroy(c.gameObject);
            foreach (Transform c in bagArea) Destroy(c.gameObject);
            foreach (var item in Profile.ShopStock()) Cell(stockArea, item, true);
            foreach (var item in Profile.Data.inventory) Cell(bagArea, item, false);

            var d = Profile.Data;
            goldText.text = $"{d.gold} gold";
            potionText.text = $"Health Potion\n<size=80%><color=#ccc>{d.potions}/{Profile.MaxPotions} carried · {Profile.PotionPrice} gold</color></size>";
            potionButton.interactable = d.gold >= Profile.PotionPrice && d.potions < Profile.MaxPotions;
            restockButton.GetComponentInChildren<TextMeshProUGUI>().text = $"New stock ({Profile.RefreshPrice} gold)";
            restockButton.interactable = d.gold >= Profile.RefreshPrice;

            if (selected != null && !d.inventory.Contains(selected) && !d.shopStock.Contains(selected)) selected = null;
            if (selected == null)
            {
                detailText.text = "<color=#aaa>Tap an item to buy or sell it.\n\nThe merchant gets new stock every time you level up.</color>";
                actionButton.gameObject.SetActive(false);
                return;
            }
            actionButton.gameObject.SetActive(true);
            detailText.text = ItemGenerator.Tooltip(selected, Profile.Equipped(selected.slot));
            var label = actionButton.GetComponentInChildren<TextMeshProUGUI>();
            if (selectedFromShop)
            {
                int price = Profile.BuyPrice(selected);
                label.text = $"Buy for {price} gold";
                actionButton.interactable = d.gold >= price && d.inventory.Count < Profile.InventorySize;
            }
            else
            {
                label.text = $"Sell for {selected.SellValue} gold";
                actionButton.interactable = true;
            }
        }

        void Cell(Transform parent, ItemInstance item, bool fromShop)
        {
            var cell = UIFactory.Image("Cell", parent, GameAssets.RoundedRect, new Color(0.08f, 0.07f, 0.08f, 1f), true);
            var outline = cell.gameObject.AddComponent<Outline>();
            outline.effectColor = item == selected ? Color.white : RarityInfo.Color(item.rarity);
            outline.effectDistance = item == selected ? new Vector2(4, -4) : new Vector2(2, -2);
            var icon = UIFactory.Image("Icon", cell.transform, ItemIcons.Get(item), Color.white);
            icon.rectTransform.Stretch(10);
            icon.preserveAspect = true;
            if (fromShop)
            {
                var price = UIFactory.Text("Price", cell.transform, Profile.BuyPrice(item).ToString(), 18, Palette.Gold);
                price.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, -2), new Vector2(100, 24));
            }
            cell.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                selected = item;
                selectedFromShop = fromShop;
                Refresh();
            });
        }

        void OnAction()
        {
            if (selected == null) return;
            if (selectedFromShop)
            {
                if (Profile.BuyItem(selected))
                {
                    Sound.Play(Sfx.Buy, Vector3.zero, 0.5f);
                    selectedFromShop = false;
                }
            }
            else
            {
                var item = selected;
                Action sell = () =>
                {
                    Profile.SellItem(item);
                    Sound.Play(Sfx.Coin, Vector3.zero, 0.4f);
                    selected = null;
                    Refresh();
                };
                if (item.rarity >= Rarity.Rare) ConfirmDialog.Show($"Sell {item.name} for {item.SellValue} gold?", "Sell", sell);
                else sell();
                return;
            }
            Refresh();
        }
    }
}
