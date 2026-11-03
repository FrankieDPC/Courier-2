using System.IO;
using System.Linq;
using Courier.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Courier.EditorTools
{
    public static class CourierMenu
    {
        const string SceneFolder = "Assets/Courier/Scenes";
        const string ScenePath = SceneFolder + "/Town.unity";

        [MenuItem("Courier/Create Town Scene", priority = 0)]
        public static void CreateTownScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Courier", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var game = new GameObject("Game");
            game.AddComponent<GameBootstrap>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);

            Selection.activeGameObject = game;
            Debug.Log($"Courier: created {ScenePath}. Press Play.");
        }

        [MenuItem("Courier/Open Town Scene", priority = 1)]
        public static void OpenTownScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Courier/Open Town Scene", true)]
        static bool CanOpenTownScene() => File.Exists(ScenePath);

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
