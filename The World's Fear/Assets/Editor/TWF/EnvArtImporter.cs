using UnityEditor;

namespace TWF.EditorTools
{
    // World art from the environment pipeline: crisp pixels, never resized or compressed.
    // Alpha carries per-pixel depth on sprites, so nothing may touch it.
    public class EnvArtImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/TWF/Env/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.alphaIsTransparency = false;
            t.mipmapEnabled = false;
            t.filterMode = UnityEngine.FilterMode.Point;
            bool tiled = assetPath.EndsWith("_tile.png");
            t.wrapMode = tiled ? UnityEngine.TextureWrapMode.Repeat : UnityEngine.TextureWrapMode.Clamp;
            t.isReadable = tiled;   // the road is painted from the dirt tile at runtime
            t.npotScale = TextureImporterNPOTScale.None;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.maxTextureSize = 8192;
        }
    }
}
