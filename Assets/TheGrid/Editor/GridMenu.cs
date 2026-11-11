using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
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

        // Builds Builds/TheGrid.app: a resizable window that keeps running when it is not in front.
        [MenuItem("The Grid/Build Mac App")]
        public static void BuildMac()
        {
            PlayerSettings.productName = "The Grid";
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            // Pixels. A retina screen halves these, giving a 1000 by 625 point window: an ordinary landscape desktop window.
            PlayerSettings.defaultScreenWidth = 2000;
            PlayerSettings.defaultScreenHeight = 1250;
            PlayerSettings.runInBackground = true;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { RosterScene, DemoScene },
                locationPathName = "Builds/TheGrid.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("build failed: " + report.summary.result);
        }
    }
}
