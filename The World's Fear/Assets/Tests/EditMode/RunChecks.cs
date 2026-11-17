using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TWF.TestRunner
{
    // Runs the checks from the menu (TWF > Tests) in the open editor and writes what happened to
    // Logs/TestResults-<mode>.txt (one line per test) and Logs/TestResults-<mode>.xml (NUnit).
    // From a terminal with the editor closed, `unity test --mode EditMode` and `unity test --mode PlayMode` do the same.
    [InitializeOnLoad]
    public static class RunChecks
    {
        class Report : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                string mode = SessionState.GetString("twf_test_mode", "Tests");
                Directory.CreateDirectory("Logs");
                var sb = new StringBuilder();
                sb.AppendLine($"{mode}: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped, {result.InconclusiveCount} inconclusive in {result.Duration:0.0}s");
                Lines(result, sb);
                File.WriteAllText($"Logs/TestResults-{mode}.txt", sb.ToString());
                TestRunnerApi.SaveResultToFile(result, $"Logs/TestResults-{mode}.xml");
                Debug.Log("[TwfTests] " + sb.ToString().Split('\n')[0]);
            }

            static void Lines(ITestResultAdaptor r, StringBuilder sb)
            {
                if (!r.Test.IsSuite)
                {
                    sb.AppendLine($"{(r.TestStatus == TestStatus.Passed ? "PASS" : r.TestStatus == TestStatus.Failed ? "FAIL" : r.TestStatus.ToString().ToUpperInvariant())}  {r.Test.Name}  ({r.Duration:0.0}s)");
                    if (r.TestStatus == TestStatus.Failed && !string.IsNullOrEmpty(r.Message))
                        sb.AppendLine("      " + r.Message.Split('\n')[0].Trim());
                    return;
                }
                foreach (var child in r.Children) Lines(child, sb);
            }
        }

        static readonly TestRunnerApi api;

        static RunChecks()
        {
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.hideFlags = HideFlags.HideAndDontSave;
            api.RegisterCallbacks(new Report());
        }

        [MenuItem("TWF/Tests/Run world and asset checks (EditMode)")]
        public static void EditMode() => Start("EditMode", null);

        [MenuItem("TWF/Tests/Run scene checks (PlayMode)")]
        public static void PlayMode() => Start("PlayMode", null);

        // filter: part of a test's name ("TestRange", "World/Performance"), or null for all of them.
        public static void Start(string mode, string filter)
        {
            SessionState.SetString("twf_test_mode", mode);
            if (File.Exists($"Logs/TestResults-{mode}.txt")) File.Delete($"Logs/TestResults-{mode}.txt");
            var f = new Filter { testMode = mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode };
            if (!string.IsNullOrEmpty(filter)) f.groupNames = new[] { ".*" + System.Text.RegularExpressions.Regex.Escape(filter) + ".*" };
            api.Execute(new ExecutionSettings(f));
        }
    }
}
