using UnityEditor;

namespace TWF.EditorTools
{
    // The 3D-rendered hero's sprite strips: crisp pixels, never resized or compressed.
    public class Hero3DImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/TWF/Hero3D")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Sprite;
            t.spriteImportMode = SpriteImportMode.Single;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = false;
            t.filterMode = UnityEngine.FilterMode.Point;
            t.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            t.npotScale = TextureImporterNPOTScale.None;
            t.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
