using System.IO;
using System.Linq;
using Farm.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Farm.EditorTools
{
    public static class FarmMenu
    {
        const string SceneFolder = "Assets/Farm/Scenes";
        const string ScenePath = SceneFolder + "/Farm.unity";

        [MenuItem("Farm/Create Farm Scene", priority = 0)]
        public static void CreateFarmScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Farm", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var game = new GameObject("Game");
            game.AddComponent<GameBootstrap>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);

            Selection.activeGameObject = game;
            Debug.Log($"Farm: created {ScenePath}. Press Play.");
        }

        [MenuItem("Farm/Open Farm Scene", priority = 1)]
        public static void OpenFarmScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Farm/Open Farm Scene", true)]
        static bool CanOpenFarmScene() => File.Exists(ScenePath);

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => File.Exists(s.path)).ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
