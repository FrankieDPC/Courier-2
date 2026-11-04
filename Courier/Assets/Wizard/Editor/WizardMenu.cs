using System.IO;
using System.Linq;
using Wizard.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wizard.EditorTools
{
    public static class WizardMenu
    {
        const string SceneFolder = "Assets/Wizard/Scenes";
        const string ScenePath = SceneFolder + "/Tower.unity";

        [MenuItem("Wizard/Create Tower Scene", priority = 0)]
        public static void CreateTowerScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Wizard", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var game = new GameObject("Game");
            game.AddComponent<GameBootstrap>();

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);

            Selection.activeGameObject = game;
            Debug.Log($"Wizard: created {ScenePath}. Press Play.");
        }

        [MenuItem("Wizard/Open Tower Scene", priority = 1)]
        public static void OpenTowerScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Wizard/Open Tower Scene", true)]
        static bool CanOpenTowerScene() => File.Exists(ScenePath);

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => File.Exists(s.path)).ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
