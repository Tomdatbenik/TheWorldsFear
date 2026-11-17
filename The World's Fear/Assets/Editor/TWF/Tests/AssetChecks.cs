using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TWF.Tests
{
    // What the game needs on disk: materials, shaders, sounds, scenes.
    public static class AssetChecks
    {
        [TwfCheck("Assets/Render materials exist and their shaders work")]
        static void RenderMaterials()
        {
            var r = RenderAssets.Instance;
            Check.That(r != null, "Resources/TWF/RenderAssets is missing");
            Check.That(r.high != null && r.low != null, "A render pipeline asset (high or low) is missing");
            foreach (var (name, m) in new[] { ("litSprite", r.litSprite), ("litGround", r.litGround), ("litDetail", r.litDetail), ("litEmissive", r.litEmissive),
                         ("litWind", r.litWind), ("unlitSprite", r.unlitSprite), ("water", r.water), ("lava", r.lava) })
            {
                Check.That(m != null, $"RenderAssets.{name} is missing");
                Check.That(m.shader != null && m.shader.isSupported, $"RenderAssets.{name}: shader '{(m.shader != null ? m.shader.name : "none")}' is not supported");
                Check.That(m.shader.name != "Hidden/InternalErrorShader", $"RenderAssets.{name} uses the error shader (it failed to compile)");
            }
        }

        [TwfCheck("Assets/Shaders compile without errors")]
        static void ShadersCompile()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                var messages = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
                Check.That(messages.Length == 0, $"{path}: {(messages.Length > 0 ? messages[0].message + " (line " + messages[0].line + ")" : "")}");
            }
        }

        [TwfCheck("Assets/Every sound event has recorded variations")]
        static void SoundBank()
        {
            var bank = Resources.Load<SfxBank>("TWF/SfxBank");
            Check.That(bank != null, "Resources/TWF/SfxBank is missing (run TWF > Build Sfx Bank)");
            foreach (string key in new[] { "BoneWail", "Cast", "Coin", "Dash", "Explosion", "Hit", "LevelUp", "MetalRoar", "MonsterDeath", "Pickup", "SlimeGurgle", "SpiderScreech", "Swing", "ZombieGroan" })
            {
                var e = bank.entries.FirstOrDefault(x => x.key == key);
                Check.That(e != null && e.clips != null && e.clips.Length > 0, $"No clips for sound event '{key}'");
                Check.That(e.clips.All(c => c != null), $"Sound event '{key}' has a missing clip");
                Check.That(e.clips.All(c => c.channels == 1), $"Sound event '{key}' has a stereo clip (positional sounds must be mono)");
            }
            // The game's own accessors answer with a clip whether or not a recording exists.
            foreach (var clip in new[] { Sfx.Swing, Sfx.Hit, Sfx.MonsterDeath, Sfx.Dash, Sfx.Pickup, Sfx.Coin, Sfx.LevelUp, Sfx.Explosion, Sfx.Cast, Sfx.Potion, Sfx.Buy })
                Check.That(clip != null && clip.length > 0.01f, "Sfx returned an empty clip");
        }

        [TwfCheck("Assets/Game scenes are in the build")]
        static void ScenesInBuild()
        {
            var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToArray();
            foreach (string scene in new[] { "Title", "World", "Dungeon" })
                Check.That(enabled.Contains(scene), $"Scene '{scene}' is not enabled in the build settings");
            foreach (var s in EditorBuildSettings.scenes.Where(s => s.enabled))
                Check.That(System.IO.File.Exists(s.path), $"Build settings list a scene that does not exist: {s.path}");
            // The debug scene is left out of the build on purpose; the play checks open it by path.
            Check.That(System.IO.File.Exists("Assets/Scenes/TestRange.unity"), "The debug scene Assets/Scenes/TestRange.unity is missing");
        }

        [TwfCheck("Assets/Graphics quality tiers get cheaper")]
        static void QualityTiers()
        {
            int before = RenderAssets.Quality;
            try
            {
                RenderAssets.Quality = RenderAssets.High;
                Check.That(RenderAssets.SunShadows == LightShadows.Soft, "High quality should have soft shadows");
                int highLights = RenderAssets.LootLights;
                RenderAssets.Quality = RenderAssets.Medium;
                Check.That(RenderAssets.SunShadows == LightShadows.Hard, "Medium quality should have hard (cheap) shadows");
                Check.That(RenderAssets.LootLights < highLights && RenderAssets.LootLights > 0, "Medium quality should light fewer dropped items than high, but some");
                Check.That(QualitySettings.renderPipeline == RenderAssets.Instance.high, "Medium quality should still use the shadowed pipeline");
                RenderAssets.Quality = RenderAssets.Low;
                Check.That(RenderAssets.SunShadows == LightShadows.None && RenderAssets.LootLights == 0, "Low quality should have no shadows and no loot lights");
                Check.That(QualitySettings.renderPipeline == RenderAssets.Instance.low, "Low quality should use the low pipeline");
            }
            finally { RenderAssets.Quality = before; }
        }
    }
}
