using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TWF.EditorTools
{
    // Title scene, the RPG version of the boss arena, build settings and player settings.
    public static class ScenesBuilder
    {
        [MenuItem("TWF/Build Everything")]
        public static void BuildEverything()
        {
            RenderBuilder.Build();
            ArtGen.Generate();
            ContentBuilder.BuildAll();
            WorldBuilder.Build();
            BuildTitle();
            BuildBossArena();
            ConfigureProject();
        }

        [MenuItem("TWF/Build Title Scene")]
        public static void BuildTitle()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.transform.position = new Vector3(0, 0, -10);
            var title = new GameObject("Title").AddComponent<TitleScreen>();
            title.background = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/StartScreen.png");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Title.unity");
        }

        // The 2.5D boss arena: everything is generated at runtime by BossArenaSetup.
        [MenuItem("TWF/Build Boss Arena")]
        public static void BuildBossArena()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0, -9, -10);
            var follow = camGo.AddComponent<CameraFollow>();
            follow.distance = 20f;
            var spawn = new GameObject("HeroSpawn");
            spawn.transform.position = new Vector3(0, -9, 0);
            new GameObject("Arena").AddComponent<BossArenaSetup>();
            var game = new GameObject("Game").AddComponent<GameBootstrap>();
            game.defaultSpawn = spawn.transform;
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/BossArena.unity");
        }

        public static void ConfigureProject()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene("Assets/Scenes/Title.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/World.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/BossArena.unity", true),
            };
            EditorBuildSettings.scenes = scenes.ToArray();

            PlayerSettings.productName = "The World's Fear";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.IceWollow.TheWorldsFear");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            AssetDatabase.SaveAssets();
        }
    }
}
