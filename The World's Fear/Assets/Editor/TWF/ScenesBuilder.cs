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

        // Copies BossBattle and swaps the old hero, joysticks and HUD for the RPG systems.
        [MenuItem("TWF/Build Boss Arena")]
        public static void BuildBossArena()
        {
            const string path = "Assets/Scenes/BossArena.unity";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CopyAsset("Assets/Scenes/BossBattle.unity", path);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            Vector3 spawn = Vector3.zero;
            foreach (var root in scene.GetRootGameObjects())
            {
                switch (root.name)
                {
                    case "Hero":
                        spawn = root.transform.position;
                        Object.DestroyImmediate(root);
                        break;
                    case "Joysticks":
                    case "UI":
                    case "Scene Handler":
                    case "Spider":
                    case "EventSystem":
                        Object.DestroyImmediate(root);
                        break;
                }
            }

            foreach (var vcam in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (vcam != null && vcam.name == "Virtual Cam") vcam.gameObject.SetActive(false);

            // Old renderers used custom sorting layers; draw the arena under the RPG characters.
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.sortingLayerName == "Background" || r.sortingLayerName == "Vegitation" || r.sortingLayerName == "Decorating")
                {
                    int order = r.sortingLayerName == "Background" ? -95 : r.sortingLayerName == "Vegitation" ? -93 : -92;
                    r.sortingLayerName = "Default";
                    r.sortingOrder = order;
                }
                else if (r.sortingLayerName != "Default")
                {
                    r.sortingLayerName = "Default";
                    r.sortingOrder = SortOrder.Units + 2;
                }
            }

            var armor = GameObject.Find("Boss Armor");
            if (armor != null)
            {
                armor.AddComponent<Combatant>();
                armor.AddComponent<BossBridge>();
            }

            var spawnGo = new GameObject("HeroSpawn");
            spawnGo.transform.position = new Vector3(spawn.x, spawn.y, 0f);
            var game = new GameObject("Game").AddComponent<GameBootstrap>();
            game.defaultSpawn = spawnGo.transform;

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetParent(null, true);
                cam.transform.position = new Vector3(spawn.x, spawn.y, -10f);
                cam.orthographicSize = 9f;
                var follow = cam.gameObject.AddComponent<CameraFollow>();
                follow.orthographicSize = 9f;
            }

            EditorSceneManager.SaveScene(scene);
        }

        public static void ConfigureProject()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene("Assets/Scenes/Title.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/World.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/BossArena.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Victory.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/GameOver.unity", true),
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
