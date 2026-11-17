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

        // Graphics quality: 0 low (no shadows, fewer lights, smaller picture), 1 medium (small hard-edged shadows:
        // most of the look for a fraction of the cost), 2 high (large soft shadows). Phones start on medium.
        public const int Low = 0, Medium = 1, High = 2;

        public static int Quality
        {
            get
            {
                if (PlayerPrefs.HasKey("twf_quality")) return Mathf.Clamp(PlayerPrefs.GetInt("twf_quality"), Low, High);
                // The old on/off setting: off stays low.
                if (PlayerPrefs.GetInt("twf_quality_high", 1) == 0) return Low;
                return Application.isMobilePlatform ? Medium : High;
            }
            set
            {
                PlayerPrefs.SetInt("twf_quality", Mathf.Clamp(value, Low, High));
                Apply();
            }
        }

        public static string QualityName => Quality == High ? "High" : Quality == Medium ? "Medium (cheap shadows)" : "Low (fastest)";

        // Anything above low: spell lights and the like are on.
        public static bool HighQuality
        {
            get => Quality > Low;
            set => Quality = value ? High : Low;
        }

        // The sun's shadows at this quality: soft only on high.
        public static LightShadows SunShadows => Quality == High ? LightShadows.Soft : Quality == Medium ? LightShadows.Hard : LightShadows.None;
        // How many dropped items may carry a light at once (the nearest to the hero keep theirs).
        public static int LootLights => Quality == High ? 6 : Quality == Medium ? 3 : 0;

        public static void Apply()
        {
            var r = Instance;
            if (r == null) return;
            QualitySettings.renderPipeline = Quality > Low ? r.high : r.low;
            if (r.high is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.mainLightShadowmapResolution = Quality == High ? 2048 : 1024;
            foreach (var l in Object.FindObjectsByType<Light>())
                if (l.type == LightType.Directional && l.shadows != LightShadows.None) l.shadows = SunShadows == LightShadows.None ? LightShadows.Hard : SunShadows;
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
