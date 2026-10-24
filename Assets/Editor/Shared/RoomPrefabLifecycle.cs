using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared prefab lifecycle helpers for authored room prefabs and theme piece prefabs.
/// </summary>
public static class RoomPrefabLifecycle
{
    public static bool SceneReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("RoomPrefabLifecycle: exit Play Mode before building scene pieces.");
            return false;
        }
        return true;
    }

    public static bool HasPrefab(string prefabPath)
    {
        return !string.IsNullOrEmpty(prefabPath) && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
    }

    public static T InstantiatePrefab<T>(string prefabPath, string instanceName = null) where T : Component
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return null;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (!string.IsNullOrEmpty(instanceName)) instance.name = instanceName;
        return instance.GetComponent<T>();
    }

    /// <summary>Saves an in-memory GameObject as a prefab asset and destroys the temporary scene copy, returning the prefab asset (or component on it).</summary>
    public static GameObject SaveAsPrefab(GameObject root, string folder, string name)
    {
        string prefabFolder = $"{folder}/Prefabs";
        EditorAssetUtil.EnsureFolder(prefabFolder);
        string path = $"{prefabFolder}/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    public static T SaveAsPrefab<T>(GameObject root, string folder, string name) where T : Component
    {
        GameObject prefab = SaveAsPrefab(root, folder, name);
        return prefab != null ? prefab.GetComponent<T>() : null;
    }
}
