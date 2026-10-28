using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TWF.EditorTools
{
    // Sets up URP for the HD-2D look: pipeline assets (High/Low), materials and post-processing profiles.
    public static class RenderBuilder
    {
        const string SettingsDir = "Assets/Settings";
        const string ResDir = "Assets/Resources/TWF";

        [MenuItem("TWF/Build Render Setup")]
        public static void Build()
        {
            Dir(SettingsDir);
            Dir(ResDir);

            var high = MakePipeline("TWF_URP_High", true);
            var low = MakePipeline("TWF_URP_Low", false);
            GraphicsSettings.defaultRenderPipeline = high;
            // Every quality level uses the default pipeline; the game swaps High/Low at runtime.
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = null;
            }
            QualitySettings.SetQualityLevel(current, false);

            var holder = Asset<RenderAssets>($"{ResDir}/RenderAssets.asset");
            holder.high = high;
            holder.low = low;
            holder.litSprite = Material("LitSprite", "TWF/LitSprite", m => { m.SetFloat("_Cutoff", 0.5f); });
            holder.litGround = Material("LitGround", "TWF/LitSprite", m => { m.SetFloat("_Cutoff", 0f); m.SetFloat("_FlatGround", 1f); });
            holder.litDetail = Material("LitDetail", "TWF/LitSprite", m => { m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_FlatGround", 1f); });
            holder.litEmissive = Material("LitEmissive", "TWF/LitSprite", m => { m.SetFloat("_Cutoff", 0.3f); m.SetFloat("_Emission", 1.6f); });
            holder.litWind = Material("LitWind", "TWF/LitSprite", m => { m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_Wind", 1f); });
            holder.unlitSprite = Material("UnlitSprite", "Universal Render Pipeline/2D/Sprite-Unlit-Default", null);
            holder.water = Material("Water", "TWF/Water", m => { m.SetFloat("_Lava", 0f); });
            holder.lava = Material("Lava", "TWF/Water", m => { m.SetFloat("_Lava", 1f); });

            holder.volumes = new VolumeProfile[System.Enum.GetValues(typeof(Biome)).Length + 1];
            foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
                holder.volumes[(int)b] = MakeProfile(b.ToString(), PostFor(b));
            holder.volumes[holder.volumes.Length - 1] = MakeProfile("Boss", new Post { tint = new Color(1f, 0.82f, 0.8f), temperature = 5, contrast = 18, saturation = 2, exposure = -0.2f, vignette = 0.42f });

            // Pixel art: no mipmaps (they bleed transparent edges into tile seams) and point filtering.
            foreach (var texPath in new[] { "Assets/Plugins/Tiny RPG Forest/Artwork/Environment/tileset.png", "Assets/Plugins/Tiny RPG Forest/Artwork/Environment/tileset-sliced.png" })
            {
                var imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (imp == null || (!imp.mipmapEnabled && imp.filterMode == FilterMode.Point)) continue;
                imp.mipmapEnabled = false;
                imp.filterMode = FilterMode.Point;
                imp.SaveAndReimport();
            }

            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();
            Debug.Log("TWF render setup built.");
        }

        static UniversalRenderPipelineAsset MakePipeline(string name, bool high)
        {
            string rendererPath = $"{SettingsDir}/{name}_Renderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var rso = new SerializedObject(renderer);
            Set(rso, "m_RenderingMode", 0); // Forward
            var ppd = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            var ppProp = rso.FindProperty("postProcessData");
            if (ppProp != null && ppd != null) ppProp.objectReferenceValue = ppd;
            rso.ApplyModifiedPropertiesWithoutUndo();

            string path = $"{SettingsDir}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, path);
            }
            var so = new SerializedObject(asset);
            Set(so, "m_SupportsHDR", high ? 1 : 0);
            Set(so, "m_MSAA", 1);
            Set(so, "m_RequireDepthTexture", high ? 1 : 0);
            Set(so, "m_MainLightRenderingMode", 1);
            Set(so, "m_MainLightShadowsSupported", high ? 1 : 0);
            Set(so, "m_MainLightShadowmapResolution", high ? 2048 : 1024);
            Set(so, "m_AdditionalLightsRenderingMode", 1); // per pixel
            Set(so, "m_AdditionalLightsPerObjectLimit", high ? 8 : 4);
            Set(so, "m_AdditionalLightShadowsSupported", 0);
            Set(so, "m_ShadowDistance", 45f);
            Set(so, "m_ShadowCascadeCount", 1);
            Set(so, "m_SoftShadowsSupported", high ? 1 : 0);
            Set(so, "m_RenderScale", high ? 1f : 0.8f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static void Set(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"URP property {prop} not found"); return; }
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: p.floatValue = v; break;
                case SerializedPropertyType.Boolean: p.boolValue = v > 0.5f; break;
                case SerializedPropertyType.Enum: p.enumValueIndex = (int)v; break;
                default: p.intValue = (int)v; break;
            }
        }

        static Material Material(string name, string shader, System.Action<Material> setup)
        {
            string path = $"{ResDir}/{name}.mat";
            var sh = Shader.Find(shader);
            if (sh == null) Debug.LogError("Shader not found: " + shader);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = sh;
            m.enableInstancing = true;
            setup?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        struct Post
        {
            public Color tint;
            public float temperature, contrast, saturation, exposure, vignette;
        }

        static Post PostFor(Biome b)
        {
            switch (b)
            {
                case Biome.Village: return new Post { tint = new Color(1f, 0.96f, 0.88f), temperature = 9, contrast = 8, saturation = 5, exposure = 0.1f, vignette = 0.3f };
                case Biome.Forest: return new Post { tint = new Color(1f, 0.97f, 0.85f), temperature = 7, contrast = 10, saturation = 6, exposure = 0.05f, vignette = 0.3f };
                case Biome.Marsh: return new Post { tint = new Color(0.85f, 0.98f, 0.9f), temperature = -5, contrast = 7, saturation = -2, exposure = -0.1f, vignette = 0.38f };
                case Biome.Ruins: return new Post { tint = new Color(0.9f, 0.95f, 1f), temperature = -9, contrast = 11, saturation = -3, exposure = 0f, vignette = 0.34f };
                case Biome.Ember: return new Post { tint = new Color(1f, 0.88f, 0.82f), temperature = 12, contrast = 14, saturation = 7, exposure = -0.05f, vignette = 0.4f };
                default: return new Post { tint = Color.white, contrast = 10, saturation = 10, vignette = 0.3f };
            }
        }

        static VolumeProfile MakeProfile(string name, Post p)
        {
            string path = $"{ResDir}/Post_{name}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null) AssetDatabase.DeleteAsset(path);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var tone = Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.65f);

            // HD-2D tilt-shift: everything beyond the play area melts into a soft blur.
            var dof = Add<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(20f);
            dof.gaussianEnd.Override(42f);
            dof.gaussianMaxRadius.Override(1.5f);
            dof.highQualitySampling.Override(false);

            var color = Add<ColorAdjustments>();
            color.postExposure.Override(p.exposure);
            color.contrast.Override(p.contrast);
            color.saturation.Override(p.saturation);
            color.colorFilter.Override(p.tint);

            var wb = Add<WhiteBalance>();
            wb.temperature.Override(p.temperature);

            var vignette = Add<Vignette>();
            vignette.intensity.Override(p.vignette);
            vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(a, path);
            }
            return a;
        }

        static void Dir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Dir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
