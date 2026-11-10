using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheGrid.EditorTools
{
    public static class GridMenu
    {
        const string RosterScene = "Assets/TheGrid/Roster.unity";
        const string DemoScene = "Assets/TheGrid/Demo.unity";

        // Rebuilds the roster scene: a camera and one RosterApp. The roster opens first in a build; the recorded demo is second.
        [MenuItem("The Grid/Create Roster Scene")]
        public static void CreateRosterScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Roster").AddComponent<RosterApp>();
            if (!EditorSceneManager.SaveScene(scene, RosterScene)) throw new InvalidOperationException("could not save " + RosterScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(RosterScene, true), new EditorBuildSettingsScene(DemoScene, true) };
            AssetDatabase.SaveAssets();
        }
    }
}
