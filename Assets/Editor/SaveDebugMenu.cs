using UnityEditor;
using UnityEngine;

/// <summary>F83: debug helper. PlayerPrefs survive between Editor Play presses, so a stale save shows CONTINUE on the title screen.</summary>
public static class SaveDebugMenu
{
    [MenuItem("KEEP IT LIT/Debug/Delete Save", priority = 200)]
    private static void DeleteSave()
    {
        SaveSystem.Delete();
        Debug.Log("Save deleted.");
    }
}
