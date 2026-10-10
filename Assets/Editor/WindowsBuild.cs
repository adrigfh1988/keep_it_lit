using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// LIGHTS OUT &gt; Build &gt; Windows Player (Steam plan S1). Builds the 64-bit Windows standalone player into
/// Builds/Windows/LightsOut.exe from the enabled scenes in the Build Settings list, as a release-style (non-development)
/// Mono build. <see cref="Build"/> is the dialog-free core; the menu item wraps it with a result dialog and
/// <see cref="BuildFromCommandLine"/> wraps it for <c>-executeMethod</c> / <c>unity command eval</c> (exit code in batch mode).
///
/// Note: BuildPipeline switches the active build target to StandaloneWindows64 and leaves it there; switching back to WebGL
/// reimports textures again, so that is left to the caller. This is a build, not a scene piece, so there is no ProjectSetup piece.
/// Spec: plannings/steam-release-plan.md, phase S1.
/// </summary>
public static class WindowsBuild
{
    private const string OutputFolder = "Builds/Windows";
    private const string ExeName = "LightsOut.exe";

    /// <summary>Menu entry: builds, then reports the result in a dialog.</summary>
    [MenuItem("LIGHTS OUT/Build/Windows Player", priority = 110)]
    public static void BuildFromMenu()
    {
        BuildReport report = Build();
        BuildSummary s = report.summary;
        if (s.result == BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("Windows Player",
                $"Build succeeded.\n\n{s.outputPath}\n{s.totalSize / (1024.0 * 1024.0):F1} MB in {s.totalTime.TotalSeconds:F0} s.", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Windows Player",
                $"Build {s.result} ({s.totalErrors} errors). See the Console / Editor.log.", "OK");
        }
    }

    /// <summary>Entry point for <c>-batchmode -executeMethod WindowsBuild.BuildFromCommandLine</c>; exits non-zero on failure when run in batch mode.</summary>
    public static void BuildFromCommandLine()
    {
        BuildReport report = Build();
        bool ok = report != null && report.summary.result == BuildResult.Succeeded;
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>Dialog-free build core. Returns the report (never null unless BuildPlayer threw); logs result, size and duration.</summary>
    public static BuildReport Build()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[WindowsBuild] No enabled scenes in the Build Settings list.");
            return null;
        }

        string outputPath = Path.Combine(OutputFolder, ExeName);
        Directory.CreateDirectory(OutputFolder);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };

        // S9: the Localization string tables live in Addressables; build that content first (also on by "Build Addressables on Player Build").
        UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.BuildPlayerContent();

        Debug.Log($"[WindowsBuild] Building {scenes.Length} scene(s) to {outputPath} (active target before: {EditorUserBuildSettings.activeBuildTarget}).");
        DateTime start = DateTime.UtcNow;
        BuildReport report;
        try { report = BuildPipeline.BuildPlayer(options); }
        finally { SsaoBuildGuard.Restore(); }
        BuildSummary summary = report.summary;
        double seconds = (DateTime.UtcNow - start).TotalSeconds;

        string line = $"[WindowsBuild] {summary.result}: {summary.totalSize / (1024.0 * 1024.0):F1} MB, " +
                      $"{summary.totalErrors} errors, {summary.totalWarnings} warnings, build {summary.totalTime.TotalSeconds:F0} s (wall {seconds:F0} s) -> {summary.outputPath}";
        if (summary.result == BuildResult.Succeeded) Debug.Log(line); else Debug.LogError(line);
        return report;
    }
}
