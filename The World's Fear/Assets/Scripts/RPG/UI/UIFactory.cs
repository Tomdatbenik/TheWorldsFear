using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TWF
{
    // Helpers for building uGUI in code.
    public static class UIFactory
    {
        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.6f;
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            UnityEngine.Object.DontDestroyOnLoad(es);
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // Anchor a rect to a point of its parent, with pivot at the same point.
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Image(string name, Transform parent, Sprite sprite, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = GameAssets.Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            return t;
        }

        public static Image Panel(string name, Transform parent, Color? bg = null, bool border = true)
        {
            var img = Image(name, parent, GameAssets.RoundedRect, bg ?? Palette.PanelBg, true);
            if (border)
            {
                var outline = img.gameObject.AddComponent<Outline>();
                outline.effectColor = Palette.PanelBorder;
                outline.effectDistance = new Vector2(2, -2);
            }
            return img;
        }

        public static Button Button(string name, Transform parent, string label, Action onClick, Color? color = null, float fontSize = 30f)
        {
            var img = Image(name, parent, GameAssets.RoundedRect, color ?? new Color(0.25f, 0.2f, 0.15f, 0.95f), true);
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.PanelBorder;
            outline.effectDistance = new Vector2(2, -2);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var t = Text("label", img.transform, label, fontSize, Palette.Text);
            t.rectTransform.Stretch(6f);
            return b;
        }

        public static Image Circle(string name, Transform parent, float size, Color color, bool raycast = false)
        {
            var img = Image(name, parent, GameAssets.Circle, color, raycast);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        public static Image Bar(string name, Transform parent, Color fill, out Image fillImage)
        {
            var bg = Image(name, parent, GameAssets.RoundedRect, new Color(0, 0, 0, 0.7f));
            fillImage = Image("fill", bg.transform, GameAssets.Pixel, fill);
            fillImage.type = UnityEngine.UI.Image.Type.Filled;
            fillImage.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fillImage.rectTransform.Stretch(4f);
            return bg;
        }
    }
}
