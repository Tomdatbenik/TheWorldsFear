using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TWF
{
    // Sprites generated at runtime so the systems don't depend on hand-made art.
    public static class GameAssets
    {
        static Sprite pixel, circle, softCircle, ring, roundedRect, diamond;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public const int WorldPPU = 16;

        public static Sprite Pixel => pixel ? pixel : pixel = Make("pixel", 4, 4, (x, y, w, h) => Color.white, 4);

        public static Sprite Circle => circle ? circle : circle = Make("circle", 64, 64, (x, y, w, h) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(w / 2f, h / 2f));
            return new Color(1, 1, 1, Mathf.Clamp01(w / 2f - d));
        }, 64);

        public static Sprite SoftCircle => softCircle ? softCircle : softCircle = Make("softcircle", 64, 64, (x, y, w, h) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(w / 2f, h / 2f)) / (w / 2f);
            return new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d));
        }, 64);

        public static Sprite Ring => ring ? ring : ring = Make("ring", 128, 128, (x, y, w, h) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(w / 2f, h / 2f));
            float outer = w / 2f - 1f;
            float inner = outer - 5f;
            return new Color(1, 1, 1, Mathf.Clamp01(outer - d + 1f) * Mathf.Clamp01(d - inner));
        }, 128);

        public static Sprite Diamond => diamond ? diamond : diamond = Make("diamond", 32, 32, (x, y, w, h) =>
        {
            float d = Mathf.Abs(x + 0.5f - w / 2f) + Mathf.Abs(y + 0.5f - h / 2f);
            return new Color(1, 1, 1, Mathf.Clamp01(w / 2f - d));
        }, 32);

        // 9-sliced rounded rectangle for UI panels and buttons.
        public static Sprite RoundedRect
        {
            get
            {
                if (roundedRect) return roundedRect;
                const int size = 32, r = 10;
                var tex = NewTexture(size, size, FilterMode.Bilinear);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                    float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f)));
                }
                tex.Apply();
                roundedRect = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0,
                    SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                roundedRect.name = "roundedrect";
                return roundedRect;
            }
        }

        public static TMP_FontAsset Font => TMP_Settings.defaultFontAsset;

        public static Texture2D NewTexture(int w, int h, FilterMode filter = FilterMode.Point)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = filter;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        public delegate Color PixelFunc(int x, int y, int w, int h);

        public static Sprite Make(string key, int w, int h, PixelFunc f, float ppu, FilterMode filter = FilterMode.Bilinear)
        {
            if (cache.TryGetValue(key, out var s) && s) return s;
            var tex = NewTexture(w, h, filter);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = f(x, y, w, h);
            tex.SetPixels(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu);
            s.name = key;
            cache[key] = s;
            return s;
        }

        // A sprite renderer child that shows a generated shape in the world.
        public static SpriteRenderer CreateWorldSprite(string name, Sprite sprite, Transform parent, Color color, int sortingOrder, float worldSize = 1f)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            RenderAssets.MakeUnlit(sr);
            float native = sprite.bounds.size.x;
            go.transform.localScale = Vector3.one * (worldSize / native);
            return sr;
        }
    }

    public static class Ground
    {
        // Flat decals (telegraphs, rings) sit a hair above the ground to avoid z-fighting. Up is -Z.
        public const float DecalZ = -0.03f;

        public static Vector3 Decal(Vector2 p) => new Vector3(p.x, p.y, DecalZ);
    }

    public static class SortOrder
    {
        public const int Ground = -50;
        public const int Telegraph = 5;
        public const int Pickup = 8;
        public const int Units = 10;
        public const int Projectiles = 20;
        public const int Effects = 25;
        public const int Bars = 40;
        public const int Text = 50;
    }

    public static class Palette
    {
        public static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        public static readonly Color HP = new Color(0.85f, 0.15f, 0.18f);
        public static readonly Color XP = new Color(0.55f, 0.35f, 0.95f);
        public static readonly Color Shield = new Color(0.75f, 0.85f, 1f);
        public static readonly Color PanelBg = new Color(0.07f, 0.06f, 0.09f, 0.92f);
        public static readonly Color PanelBorder = new Color(0.55f, 0.45f, 0.3f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.9f, 0.84f);
        public static readonly Color MutedText = new Color(0.65f, 0.62f, 0.58f);
        public static readonly Color EnemyTelegraph = new Color(1f, 0.25f, 0.15f, 0.35f);
        public static readonly Color Heal = new Color(0.35f, 1f, 0.45f);

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
