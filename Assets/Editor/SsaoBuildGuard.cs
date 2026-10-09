using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Keeps the hub-only SSAO renderer feature (F95) alive in desktop player builds. The feature is saved INACTIVE in the URP renderer
/// asset and HubRenderingLook switches it on at runtime, but URP's build stripping then drops the SSAO resources (shader + textures)
/// because no active feature needs them, and the player's render pipeline fails to create ("Couldn't find the required resources for the
/// ScreenSpaceAmbientOcclusion render feature" followed by a per-frame "Blitter is already initialized" exception, i.e. a black window).
/// This switches every SSAO feature on for the duration of the build and puts it back afterwards, so the asset on disk stays inactive.
/// WebGL builds are left alone (they are the shipped demo and are not touched by the Steam work).
/// </summary>
public class SsaoBuildGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => -1000;

    private static readonly List<ScreenSpaceAmbientOcclusion> Switched = new List<ScreenSpaceAmbientOcclusion>();

    public void OnPreprocessBuild(BuildReport report)
    {
        Switched.Clear();
        if (report.summary.platform == BuildTarget.WebGL) return;
        foreach (UniversalRendererData data in AllRendererData())
        {
            foreach (ScreenSpaceAmbientOcclusion feature in data.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>())
            {
                if (feature.isActive) continue;
                feature.SetActive(true);
                Switched.Add(feature);
            }
        }
        if (Switched.Count > 0)
            UnityEngine.Debug.Log($"[SsaoBuildGuard] Switched {Switched.Count} SSAO renderer feature(s) on for the build (restored afterwards).");
    }

    public void OnPostprocessBuild(BuildReport report) => Restore();

    /// <summary>Puts the features back to inactive; also callable by hand if a build was aborted mid-way.</summary>
    public static void Restore()
    {
        foreach (ScreenSpaceAmbientOcclusion feature in Switched)
            if (feature != null) feature.SetActive(false);
        Switched.Clear();
    }

    private static IEnumerable<UniversalRendererData> AllRendererData() =>
        AssetDatabase.FindAssets("t:UniversalRendererData")
            .Select(g => AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null);
}
