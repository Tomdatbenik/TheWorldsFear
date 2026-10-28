using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    // Materials, pipeline assets and post-processing profiles for the HD-2D look.
    // Built by TWF/Build Render Setup; loaded from Resources/TWF/RenderAssets.
    public class RenderAssets : ScriptableObject
    {
        static RenderAssets instance;

        public static RenderAssets Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<RenderAssets>("TWF/RenderAssets");
                return instance;
            }
        }

        public RenderPipelineAsset high;
        public RenderPipelineAsset low;

        public Material litSprite;
        public Material litGround;   // solid ground tiles: no alpha cutout (avoids seams)
        public Material litDetail;   // overlay tiles with transparency
        public Material litEmissive;
        public Material litWind;
        public Material unlitSprite;
        public Material water;
        public Material lava;

        [Tooltip("Indexed by Biome; the last entry is the boss arena.")]
        public VolumeProfile[] volumes;

        public static bool HighQuality
        {
            get => PlayerPrefs.GetInt("twf_quality_high", 1) == 1;
            set
            {
                PlayerPrefs.SetInt("twf_quality_high", value ? 1 : 0);
                Apply();
            }
        }

        public static void Apply()
        {
            var r = Instance;
            if (r == null) return;
            QualitySettings.renderPipeline = HighQuality ? r.high : r.low;
        }

        // Sprites that stand in the world: lit, cast and receive shadows.
        public static void MakeLit(Renderer renderer, bool castShadows = true, Material material = null)
        {
            var r = Instance;
            if (r == null || renderer == null) return;
            renderer.sharedMaterial = material != null ? material : r.litSprite;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        // Effects, telegraphs and bars: unlit and transparent.
        public static void MakeUnlit(Renderer renderer)
        {
            var r = Instance;
            if (r == null || renderer == null) return;
            renderer.sharedMaterial = r.unlitSprite;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
