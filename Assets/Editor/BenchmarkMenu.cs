using UnityEditor;
using UnityEngine;

/// <summary>F118: starts the scripted benchmark (PerfBenchmark) from the Editor. Editor numbers are labelled "editor" in the CSV and are not representative of a built player.</summary>
public static class BenchmarkMenu
{
    [MenuItem("KEEP IT LIT/Debug/Run Benchmark (Play)", priority = 220)]
    private static void Run()
    {
        if (!EditorUtility.DisplayDialog("Run Benchmark",
                "Plays the scene and walks a fixed route at the low / medium / high presets (about 4 stations x 3 presets x 10 s plus loads).\n\n" +
                "Editor numbers are labelled 'editor' in the CSV and are NOT representative - build a player and run it with -benchmark for real numbers. " +
                "Settings are restored afterwards. The CSV path is logged.", "Run", "Cancel"))
            return;
        Start(10, null, null);
    }

    /// <summary>Also callable from eval: KeepItLit benchmark with a shorter segment length.</summary>
    public static void Start(int seconds, string presets, string stations)
    {
        SessionState.SetInt(PerfBenchmark.SessionKey, 1);
        SessionState.SetInt(PerfBenchmark.SessionSecondsKey, seconds);
        SessionState.SetString(PerfBenchmark.SessionPresetsKey, presets ?? "");
        SessionState.SetString(PerfBenchmark.SessionStationsKey, stations ?? "");
        EditorApplication.EnterPlaymode();
    }
}
