using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using TheGrid.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

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

        // Draws the world from the synthetic fixture into Logs/world-far.png, world-near.png and world-near-dim.png, without a window or any agent
        // data: a quick look at new sprites standing where the game puts them. Names, counts and badges are drawn by the window and are not in these pictures.
        [MenuItem("The Grid/Render World Pictures")]
        public static void WorldPictures()
        {
            var snap = JsonUtility.FromJson<RosterSnapshot>(File.ReadAllText("fixtures/world/snapshot.json"));
            var view = new GameObject("World pictures").AddComponent<WorldView>();
            foreach (var far in new[] { true, false })
                File.WriteAllBytes("Logs/world-" + (far ? "far" : "near") + ".png", view.Picture(World.Build(snap, new Plots()), 2000, 1250, far, 0f).EncodeToPNG());
            // And the same sector a beat later: the lights of the buildings bots are working in at the dim half of their pulse.
            File.WriteAllBytes("Logs/world-near-dim.png", view.Picture(World.Build(snap, new Plots()), 2000, 1250, false, 1f).EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(view.gameObject);
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
            Sign("Builds/TheGrid.app");
        }

        // A person may name the certificate to sign with here: one line, a name or SHA-1 as `security find-identity` prints it. The folder is not tracked.
        const string IdentityFile = "UserSettings/TheGridSignIdentity.txt";

        // macOS remembers the answer to "may this app access your Documents folder" against the app's signature. Unity signs a build
        // ad hoc, which pins it to the exact bytes, so every rebuild is a new app and macOS asks again. Signing each build with the
        // same certificate gives them one identity, and the question is asked once.
        static void Sign(string app)
        {
            string identity = File.Exists(IdentityFile) ? File.ReadAllText(IdentityFile).Trim() : "";
            if (identity == "")
            {
                // The first development certificate in the keychain: Xcode makes one for any Apple account, paid or not.
                Run("/usr/bin/security", "find-identity -v -p codesigning", out var listed);
                identity = Regex.Match(listed, "^\\s*\\d+\\) ([0-9A-F]{40}) \"Apple Development: ", RegexOptions.Multiline).Groups[1].Value;
            }
            if (identity == "")
            {
                Debug.LogWarning("[build] not signed: no Apple Development certificate was found, and " + IdentityFile + " names none. macOS will ask for folder access again after every rebuild.");
                return;
            }
            if (Run("/usr/bin/codesign", "--force --deep --sign \"" + identity + "\" \"" + app + "\"", out var output) != 0)
                throw new InvalidOperationException("signing failed: " + output.Trim());
            // Self-check: an identity that survives a rebuild is stated in terms of the certificate, never of this build's hash.
            Run("/usr/bin/codesign", "-d -r- \"" + app + "\"", out var requirement);
            if (!requirement.Contains("designated =>") || requirement.Contains("cdhash"))
                throw new InvalidOperationException("signed, but the app's identity still changes with every build: " + requirement.Trim());
            Debug.Log("[build] signed; macOS will ask for folder access once for builds signed this way");
        }

        static int Run(string exe, string args, out string output)
        {
            var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                var stderr = p.StandardError.ReadToEndAsync();
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                output += stderr.Result;
                return p.ExitCode;
            }
        }
    }
}
