using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The hub is a hand-editable prefab (Assets/Prefabs/AsylumHub.prefab). The builder only drafts it (or resets it on
/// request); Check Project still runs <see cref="Audit"/> on whatever the hub is now, so a hand edit that blows the
/// light budget, leaves a magenta material or cuts a route off is flagged instead of silently breaking the run.
/// </summary>
public static partial class AsylumHubBuilder
{
    /// <summary>Turns a scene hub into the prefab and connects the scene object to it. Overwrites an existing prefab.</summary>
    public static void SaveAsPrefab(GameObject hub)
    {
        EnsureFolderPath("Assets/Prefabs");
        PrefabUtility.SaveAsPrefabAssetAndConnect(hub, PrefabPath, InteractionMode.AutomatedAction, out bool ok);
        if (!ok) Debug.LogError($"Build Asylum Hub: could not save {PrefabPath}.", hub);
        else Debug.Log($"Asylum hub saved as {PrefabPath}. Edit it by hand (double-click the prefab), then save the scene (Ctrl+S).", hub);
    }

    /// <summary>Puts the saved prefab back in the scene (used when the hub is missing but the prefab exists).</summary>
    private static GameObject InstantiatePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return null;
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Place Asylum Hub");
        Debug.Log($"Asylum hub placed from {PrefabPath}. Save the scene (Ctrl+S) to keep it.", go);
        return go;
    }

    /// <summary>The audit lines that failed (every check line ends in OK when it passes), or null.</summary>
    private static string AuditProblems(Transform root)
    {
        List<string> bad = new List<string>();
        foreach (string raw in Audit(root).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("Asylum audit") || line.StartsWith("bounds:") || line.StartsWith("path ") || line.EndsWith("OK")) continue;
            bad.Add(line);
        }
        return bad.Count == 0 ? null : string.Join("; ", bad);
    }
}
