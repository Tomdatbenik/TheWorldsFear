using UnityEditor;

namespace TWF.EditorTools
{
    // World art from the environment pipeline: crisp pixels, never resized or compressed.
    // Alpha carries per-pixel depth on sprites, so nothing may touch it.
    public class EnvArtImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            // The 3D kit's textures (env_textures.py): pixel art that repeats over a mesh.
            bool kit = assetPath.Contains("/Resources/TWF/Env3D/");
            if (!kit && !assetPath.Contains("/Resources/TWF/Env/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.alphaIsTransparency = false;
            t.mipmapEnabled = false;
            t.filterMode = UnityEngine.FilterMode.Point;
            bool tiled = kit || assetPath.EndsWith("_tile.png");
            t.wrapMode = tiled ? UnityEngine.TextureWrapMode.Repeat : UnityEngine.TextureWrapMode.Clamp;
            t.isReadable = tiled && !kit;   // the road is painted from the dirt tile at runtime
            t.npotScale = TextureImporterNPOTScale.None;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.maxTextureSize = 8192;
        }
    }
}
