using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // Runs this assembly's EditMode tests and writes Logs/core-tests.txt: "PASS n" or "FAIL f of n", then one line per failure.
    public static class RunCoreTests
    {
        const string ResultPath = "Logs/core-tests.txt";

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
            File.Delete(ResultPath);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "TheGrid.Core.Tests" } }));
        }

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var failures = new List<string>();
                Collect(result, failures);
                int total = result.PassCount + result.FailCount;
                var lines = new List<string> { failures.Count == 0 && total > 0 ? "PASS " + total : "FAIL " + failures.Count + " of " + total };
                lines.AddRange(failures);
                Directory.CreateDirectory("Logs");
                File.WriteAllLines(ResultPath, lines);
                Debug.Log("[core-tests] " + lines[0]);
            }

            static void Collect(ITestResultAdaptor r, List<string> failures)
            {
                if (!r.HasChildren)
                {
                    if (r.TestStatus == TestStatus.Failed) failures.Add(r.FullName + ": " + (r.Message ?? "").Replace("\n", " | "));
                    return;
                }
                foreach (var c in r.Children) Collect(c, failures);
            }
        }
    }
}
