using System.IO;
using System.Linq;
using Bunker.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bunker.EditorTools
{
    public static class BunkerMenu
    {
        const string SceneFolder = "Assets/Bunker/Scenes";
        const string ScenePath = SceneFolder + "/Bunker.unity";

        [MenuItem("Bunker/Create Bunker Scene", priority = 0)]
        public static void CreateBunkerScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Bunker", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var game = new GameObject("Game");
            game.AddComponent<GameBootstrap>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);

            Selection.activeGameObject = game;
            Debug.Log($"Bunker: created {ScenePath}. Press Play.");
        }

        [MenuItem("Bunker/Open Bunker Scene", priority = 1)]
        public static void OpenBunkerScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Bunker/Open Bunker Scene", true)]
        static bool CanOpenBunkerScene() => File.Exists(ScenePath);

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => File.Exists(s.path)).ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
