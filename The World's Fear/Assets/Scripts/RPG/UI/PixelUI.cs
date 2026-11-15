using UnityEngine;

namespace TWF
{
    // Pixel-art pieces for the UI, drawn in code like the icons (IconFactory): a gilded frame for panels, bevelled
    // buttons, a sunken bar trough and a stepped rounded box. Each art pixel is Scale UI units, so they read as chunky
    // pixel art at the same scale as the world sprites. All are 9-sliced: only the flat middle stretches.
    // Text in the pixel font that ends up small (set smaller later, or shrunk by auto-sizing) switches to the plain
    // font for good: below GameAssets.PixelFontMinSize the pixel font's one-pixel gaps close up.
    public class PixelFontGuard : MonoBehaviour
    {
        TMPro.TMP_Text text;

        void Awake() => text = GetComponent<TMPro.TMP_Text>();

        void LateUpdate()
        {
            if (text == null) { enabled = false; return; }
            if (text.font != GameAssets.PixelFont) { enabled = false; return; }
            if (text.fontSize < GameAssets.PixelFontMinSize)
            {
                text.font = GameAssets.Font;
                enabled = false;
            }
        }
    }

    public static class PixelUI
    {
        public const float Scale = 4f;                 // UI units per art pixel
        const float PPU = 100f / Scale;                // the canvas draws 100 units per sprite unit

        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        static readonly Color32 Ink = new Color32(16, 11, 12, 255);
        static readonly Color32 GoldHi = new Color32(244, 208, 120, 255);
        static readonly Color32 Gold = new Color32(201, 152, 68, 255);
        static readonly Color32 GoldLo = new Color32(122, 82, 38, 255);
        static readonly Color32 Rivet = new Color32(255, 236, 170, 255);

        static Sprite frame, box, button, buttonDown, trough, divider;

        // Mostly white so the Image colour tints it; a light top-left and a darker bottom-right edge give the bevel.
        static Color32 Grey(float v, byte a = 255) { byte b = (byte)(v * 255); return new Color32(b, b, b, a); }

        static Sprite Make(string name, int size, int border, System.Func<int, int, Color32> px)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "PixelUI " + name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var c = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                c[y * size + x] = px(x, y);
            t.SetPixels32(c);
            t.Apply();
            var s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            s.name = "pixelui_" + name;
            return s;
        }

        // Distance in from the nearest edge, and whether (x, y) is cut off by a stepped corner of `cut` pixels.
        static int Ring(int x, int y, int n) => Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y));
        static bool Cut(int x, int y, int n, int cut)
        {
            int cx = Mathf.Min(x, n - 1 - x), cy = Mathf.Min(y, n - 1 - y);
            return cx + cy < cut;
        }

        // A plain box with stepped pixel corners (replaces the smooth rounded rectangle everywhere).
        public static Sprite Box => box ? box : box = Make("box", 12, 4, (x, y) =>
        {
            const int n = 12;
            if (Cut(x, y, n, 2)) return Clear;
            int r = Ring(x, y, n);
            if (r == 0 || Cut(x, y, n, 3)) return Grey(y > x ? 1f : 0.62f);   // lit upper-left, shaded lower-right
            return Grey(0.88f);
        });

        // A gilded frame round a panel: an ink line, a bevelled gold band, rivets in the corners. The middle is
        // empty (the panel's own colour shows through).
        public static Sprite Frame => frame ? frame : frame = Make("frame", 16, 6, (x, y) =>
        {
            const int n = 16;
            if (Cut(x, y, n, 2)) return Clear;
            int r = Ring(x, y, n);
            bool topLeft = y > x;   // the upper-left half catches the light
            if (r == 0 || Cut(x, y, n, 3)) return Ink;
            if (r == 1) return topLeft ? GoldHi : Gold;
            if (r == 2) return topLeft ? Gold : GoldLo;
            if (r == 3) return Ink;
            if (r == 4 && Mathf.Min(x, n - 1 - x) == 4 && Mathf.Min(y, n - 1 - y) == 4) return Rivet;
            return Clear;
        });

        // A raised button: light top edge, dark lip along the bottom. Down, the lip moves to the top.
        public static Sprite Button => button ? button : button = MakeButton("button", false);
        public static Sprite ButtonDown => buttonDown ? buttonDown : buttonDown = MakeButton("button_down", true);

        static Sprite MakeButton(string name, bool down) => Make(name, 12, 4, (x, y) =>
        {
            const int n = 12;
            if (Cut(x, y, n, 2)) return Clear;
            int r = Ring(x, y, n);
            if (r == 0 || Cut(x, y, n, 3)) return Ink;
            if (!down)
            {
                if (y == 1) return Grey(0.45f);                 // the lip it stands on
                if (y == 2) return Grey(0.62f);
                if (y == n - 2 || x == 1) return Grey(1f);      // lit top and left
                if (x == n - 2) return Grey(0.7f);
                return Grey(0.86f);
            }
            if (y == n - 2) return Grey(0.45f);                 // pressed in: shade along the top
            if (y == n - 3) return Grey(0.62f);
            return Grey(0.78f);
        });

        // The sunken trough behind a bar: dark inside, an ink line and a light lower rim.
        public static Sprite Trough => trough ? trough : trough = Make("trough", 10, 3, (x, y) =>
        {
            const int n = 10;
            if (Cut(x, y, n, 1)) return Clear;
            int r = Ring(x, y, n);
            if (r == 0) return Ink;
            if (r == 1) return y == 1 || x == n - 2 ? Grey(0.55f) : Grey(0.2f);
            return Grey(0.12f);
        });

        // A short gold rule to split sections of a panel.
        public static Sprite Divider => divider ? divider : divider = Make("divider", 8, 3, (x, y) =>
        {
            if (y < 2 || y > 5) return Clear;
            if (y == 2 || y == 5) return Ink;
            return y == 4 ? GoldHi : GoldLo;
        });

        // Lays the gilded frame over a panel as its first child (outside any layout group).
        public static void AddFrame(Transform panel)
        {
            var img = UIFactory.Image("Frame", panel, Frame, Color.white);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.fillCenter = false;
            img.rectTransform.Stretch(-Scale);
            img.transform.SetAsFirstSibling();
            img.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        }
    }
}
