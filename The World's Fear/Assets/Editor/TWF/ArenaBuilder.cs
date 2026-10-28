using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TWF.EditorTools
{
    // A small test scene: open ground, the hero and endless monsters from one zone.
    public static class ArenaBuilder
    {
        const string TileDir = "Assets/Plugins/Tiny RPG Forest/Artwork/Environment/";

        [MenuItem("TWF/Build Test Arena")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.08f, 0.06f);
            camGo.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<CameraFollow>();

            var grid = new GameObject("Grid").AddComponent<Grid>();
            var tmGo = new GameObject("Ground");
            tmGo.transform.SetParent(grid.transform, false);
            var tm = tmGo.AddComponent<Tilemap>();
            var tr = tmGo.AddComponent<TilemapRenderer>();
            tr.sortingOrder = -100;
            var plain = new[] { 222, 223, 226, 299, 351, 360 };
            var detail = new[] { 224, 225, 234, 237, 311, 331 };
            var rng = new System.Random(5);
            for (int x = -30; x < 30; x++)
            for (int y = -30; y < 30; y++)
            {
                int id = rng.NextDouble() < 0.15 ? detail[rng.Next(detail.Length)] : plain[rng.Next(plain.Length)];
                tm.SetTile(new Vector3Int(x, y, 0), AssetDatabase.LoadAssetAtPath<TileBase>($"{TileDir}tileset-sliced_{id}.asset"));
            }

            var boot = new GameObject("Game").AddComponent<GameBootstrap>();
            boot.arenaZoneId = "forest";
            boot.arenaMonsterCount = 6;

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Arena.unity");
        }
    }
}
