using UnityEditor;

namespace TWF.EditorTools
{
    // The 3D-rendered monsters' sprite strips: crisp pixels, never resized or compressed.
    public class Mob3DImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/TWF/Mobs/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Sprite;
            t.spriteImportMode = SpriteImportMode.Single;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = false;
            t.filterMode = UnityEngine.FilterMode.Point;
            t.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            t.npotScale = TextureImporterNPOTScale.None;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.maxTextureSize = 16384;                     // a wide strip must never be shrunk: the frames are cut by exact pixels
        }
    }
}
