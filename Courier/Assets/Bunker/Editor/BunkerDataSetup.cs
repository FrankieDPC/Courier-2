using System.IO;
using Bunker.Game;
using UnityEditor;
using UnityEngine;

namespace Bunker.EditorTools
{
    /// <summary>
    /// Makes sure the game data asset exists (Assets/Bunker/Resources/BunkerData.asset, with the built-in
    /// numbers) and adds Bunker > Edit Game Data to jump to it. Never overwrites an existing file.
    /// </summary>
    [InitializeOnLoad]
    public static class BunkerDataSetup
    {
        const string Parent = "Assets/Bunker";
        const string Folder = Parent + "/Resources";
        const string AssetPath = Folder + "/" + BunkerData.ResourcePath + ".asset";

        static BunkerDataSetup()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            EnsureSoon(20);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) Ensure(); // if play mode got in the way before
            };
        }

        /// <summary>Tries after the editor settles; if it's still busy importing, tries again a little later.</summary>
        static void EnsureSoon(int triesLeft)
        {
            EditorApplication.delayCall += () =>
            {
                bool busy = EditorApplication.isUpdating || EditorApplication.isCompiling;
                if (Ensure() == null && busy && triesLeft > 0) EnsureSoon(triesLeft - 1);
            };
        }

        static BunkerData Ensure()
        {
            var data = AssetDatabase.LoadAssetAtPath<BunkerData>(AssetPath);
            if (data != null)
            {
                SaveIfUpgraded(data);
                return data;
            }
            if (File.Exists(AssetPath)) return null; // there, just not imported yet: leave it alone
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || EditorApplication.isCompiling) return null;

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Parent, "Resources");
            data = ScriptableObject.CreateInstance<BunkerData>();
            data.UpgradedOnLoad = false; // brand new: already up to date
            AssetDatabase.CreateAsset(data, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Bunker: created {AssetPath} with the built-in bot and crop stats. Edit it to tune them (Bunker > Edit Game Data).");
            return data;
        }

        /// <summary>An asset from an older version was brought up to date as it loaded: write that to disk.</summary>
        static void SaveIfUpgraded(BunkerData data)
        {
            if (!data.UpgradedOnLoad) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || EditorApplication.isCompiling) return;
            data.UpgradedOnLoad = false;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log($"Bunker: updated {AssetPath} for this version (crop grow times are now in watered hours; added water settings).");
        }

        [MenuItem("Bunker/Edit Game Data", priority = 20)]
        public static void Edit()
        {
            var data = Ensure();
            if (data == null) data = AssetDatabase.LoadAssetAtPath<BunkerData>(AssetPath);
            if (data == null)
            {
                Debug.LogWarning($"Bunker: {AssetPath} isn't ready yet (Unity may still be importing). Try again in a moment.");
                return;
            }
            Selection.activeObject = data;
            EditorGUIUtility.PingObject(data);
        }
    }
}
