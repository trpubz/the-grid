using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // Runs this assembly's EditMode tests. The test framework writes the outcome to
    // <Application.persistentDataPath>/TestResults.xml; read that file for the result.
    public static class RunCoreTests
    {
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
