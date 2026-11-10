using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // Runs this assembly's EditMode tests. The test framework writes the outcome to
    // <Application.persistentDataPath>/TestResults.xml; read that file for the result.
    public static class RunCoreTests
    {
        const string PendingKey = "TheGrid.RunCoreTestsAfterReload";

        // Picks up changed files, then runs the tests once the scripts have recompiled.
        [MenuItem("The Grid/Refresh And Run Core Tests")]
        public static void RefreshAndRun()
        {
            SessionState.SetBool(PendingKey, true);
            AssetDatabase.Refresh();
            // If the refresh recompiled, a domain reload follows and discards this callback; RunPending then fires from the reload instead.
            EditorApplication.update += RunPending;
        }

        [InitializeOnLoadMethod]
        static void AfterReload() => EditorApplication.update += RunPending;

        // On update, not delayCall: delayCall does not fire while the editor sits in the background.
        static void RunPending()
        {
            EditorApplication.update -= RunPending;
            if (!SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            Run();
        }

        [MenuItem("The Grid/Run Core Tests")]
        public static void Run()
        {
            if (EditorUtility.scriptCompilationFailed)
            {
                // The old assemblies are still loaded; running them would report a pass for code that no longer builds.
                Debug.LogError("[core-tests] not run: scripts did not compile");
                return;
            }
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "TheGrid.Core.Tests" } }));
        }
    }
}
