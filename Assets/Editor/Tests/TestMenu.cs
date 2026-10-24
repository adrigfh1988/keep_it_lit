using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace KeepItLit.Tests
{
    /// <summary>
    /// F118: runs the EditMode tests from the open Editor (menu, or eval "KeepItLit.Tests.TestMenu.RunAll();") and writes
    /// Logs/TestResults.txt when the run finishes. Batch mode: see CLAUDE.md "Commands".
    /// </summary>
    public static class TestMenu
    {
        private static TestRunnerApi _api;
        private static ResultsWriter _writer;

        [MenuItem("KEEP IT LIT/Debug/Run Tests (Fast)", priority = 210)]
        public static void RunFast() => Run(new[] { "Fast" }, false);

        [MenuItem("KEEP IT LIT/Debug/Run Tests (All)", priority = 211)]
        public static void RunAll() => Run(new[] { "Fast", "Maze" }, false);

        /// <summary>For -executeMethod if EnterPlayMode misbehaves under -runTests: same job, exits with the result.</summary>
        public static void RunAllFromCommandLine() => Run(new[] { "Fast", "Maze" }, true);

        /// <summary>Runs the Fast unit test suite in batch mode and exits with the result.</summary>
        public static void RunFastFromCommandLine() => Run(new[] { "Fast" }, true);

        private static void Run(string[] categories, bool exitWhenDone)
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "TestResults.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "RUNNING " + string.Join(",", categories) + " " + DateTime.Now.ToString("s") + "\n");

            if (_api == null) _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            if (_writer != null) _api.UnregisterCallbacks(_writer);
            _writer = new ResultsWriter(path, exitWhenDone);
            _api.RegisterCallbacks(_writer);
            _api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, categoryNames = categories }));
        }

        private sealed class ResultsWriter : ICallbacks
        {
            private readonly string _path;
            private readonly bool _exit;
            private readonly StringBuilder _lines = new StringBuilder();
            private int _passed, _failed, _skipped;

            public ResultsWriter(string path, bool exit) { _path = path; _exit = exit; }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                string state = result.TestStatus.ToString();
                if (result.TestStatus == TestStatus.Passed) _passed++;
                else if (result.TestStatus == TestStatus.Failed) _failed++;
                else _skipped++;
                _lines.AppendLine(state.ToUpperInvariant().PadRight(12) + " " + result.Test.FullName + " (" + result.Duration.ToString("0.0") + "s)");
                if (!string.IsNullOrEmpty(result.Message)) _lines.AppendLine("    " + result.Message.Replace("\n", "\n    "));
                if (!string.IsNullOrEmpty(result.Output)) _lines.AppendLine("    output: " + result.Output.Replace("\n", "\n    "));
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                string summary = "DONE passed=" + _passed + " failed=" + _failed + " skipped/inconclusive=" + _skipped + " total=" + result.Duration.ToString("0.0") + "s\n";
                File.WriteAllText(_path, summary + _lines);
                Debug.Log("Tests: " + summary.Trim() + " -> " + _path);
                if (_exit) EditorApplication.Exit(_failed > 0 ? 1 : 0);
            }
        }
    }
}
