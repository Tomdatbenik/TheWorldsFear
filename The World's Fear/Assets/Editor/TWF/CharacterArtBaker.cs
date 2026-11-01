using System.IO;
using UnityEditor;
using UnityEngine;

namespace TWF.EditorTools
{
    // Bakes the generated character art to PNG sheets: an albedo (flat color) and a normal map per
    // layer, 8 frames x 4 rows of 64x96 (idle 2, walk 4, attack 2; rows front, right, back, left).
    // These are the art assets: edit them by hand, or regenerate after tuning the generator.
    public static class CharacterArtBaker
    {
        const string Folder = "Assets/Content/CharacterArt";

        [MenuItem("TWF/Bake Character Art")]
        public static void Bake()
        {
            foreach (bool female in new[] { false, true })
            {
                string dir = Path.Combine(Folder, female ? "Female" : "Male");
                Directory.CreateDirectory(dir);
                var albedo = new Texture2D(BodyRenderer.W * 8, BodyRenderer.H * 4, TextureFormat.RGBA32, false);
                var normal = new Texture2D(BodyRenderer.W * 8, BodyRenderer.H * 4, TextureFormat.RGBA32, false);
                albedo.SetPixels(new Color[albedo.width * albedo.height]);
                normal.SetPixels(new Color[normal.width * normal.height]);
                for (int row = 0; row < 4; row++)
                for (int f = 0; f < 8; f++)
                {
                    var facing = row == 0 ? BodyRenderer.Facing.Front : row == 2 ? BodyRenderer.Facing.Back : BodyRenderer.Facing.Side;
                    var pose = new BodyRenderer.Pose
                    {
                        facing = facing,
                        step = f >= 2 && f < 6 ? new[] { 1, 0, -1, 0 }[f - 2] : 0,
                        bob = f < 2 ? f : f < 6 ? (f % 2 == 1 ? 1 : 0) : 0,
                    };
                    var maps = BodyRenderer.MannequinMaps(female, Appearance.SkinTones[1], pose);
                    bool mirror = row == 3;
                    for (int x = 0; x < BodyRenderer.W; x++)
                    for (int y = 0; y < BodyRenderer.H; y++)
                    {
                        int sx = mirror ? BodyRenderer.W - 1 - x : x;
                        albedo.SetPixel(f * BodyRenderer.W + x, row * BodyRenderer.H + y, maps.albedo[sx, y]);
                        var n = maps.normal[sx, y];
                        if (mirror) n.r = 1f - n.r;
                        normal.SetPixel(f * BodyRenderer.W + x, row * BodyRenderer.H + y, n);
                    }
                }
                File.WriteAllBytes(Path.Combine(dir, "body_albedo.png"), albedo.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(dir, "body_normal.png"), normal.EncodeToPNG());
            }
            AssetDatabase.Refresh();
            Debug.Log("Character art baked to " + Folder);
        }
    }
}
