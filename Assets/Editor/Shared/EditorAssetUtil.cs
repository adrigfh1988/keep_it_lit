using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared editor asset and SerializedObject wiring utilities used across KEEP IT LIT builders.
/// </summary>
public static class EditorAssetUtil
{
    /// <summary>
    /// Recursively ensures every folder segment in <paramref name="folderPath"/> (e.g. "Assets/SourceFiles/Materials/Interactables") exists.
    /// </summary>
    public static void EnsureFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath)) return;

        string normalized = folderPath.Replace('\\', '/').TrimEnd('/');
        string[] parts = normalized.Split('/');
        if (parts.Length == 0) return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    public static void Wire(SerializedObject so, string field, Object value)
    {
        if (so == null || string.IsNullOrEmpty(field)) return;
        SerializedProperty prop = so.FindProperty(field);
        if (prop != null) prop.objectReferenceValue = value;
    }

    public static void Wire(Component target, params (string field, Object value)[] bindings)
    {
        if (target == null || bindings == null || bindings.Length == 0) return;
        SerializedObject so = new SerializedObject(target);
        for (int i = 0; i < bindings.Length; i++)
        {
            SerializedProperty prop = so.FindProperty(bindings[i].field);
            if (prop != null) prop.objectReferenceValue = bindings[i].value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
