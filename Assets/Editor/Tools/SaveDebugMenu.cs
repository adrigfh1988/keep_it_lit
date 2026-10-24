using UnityEditor;
using UnityEngine;

/// <summary>F83: debug helper. Saves survive between Editor Play presses, so a stale save shows CONTINUE on the title screen. F118: the save is a file; photosensitivity card reset.</summary>
public static class SaveDebugMenu
{
    [MenuItem("KEEP IT LIT/Debug/Delete Save", priority = 200)]
    private static void DeleteSave()
    {
        SaveSystem.Delete();
        Debug.Log("Save deleted (" + SavePath() + ").");
    }

    [MenuItem("KEEP IT LIT/Debug/Open Save Folder", priority = 201)]
    private static void OpenSaveFolder()
    {
        string dir = Application.persistentDataPath;
        System.IO.Directory.CreateDirectory(dir);
        string file = System.IO.Path.Combine(dir, "save.json");
        EditorUtility.RevealInFinder(System.IO.File.Exists(file) ? file : dir);
        Debug.Log("Saves live in " + dir);
    }

    [MenuItem("KEEP IT LIT/Debug/Reset Photosensitivity Notice", priority = 202)]
    private static void ResetPhotosensitivityNotice()
    {
        PhotosensitivityNotice.ResetAcknowledged();
        Debug.Log("Photosensitivity notice will show on the next title screen.");
    }

    [MenuItem("KEEP IT LIT/Debug/Delete Patient Record", priority = 203)]
    private static void DeletePatientRecord()
    {
        PatientRecord.Delete();
        Debug.Log("F122: the Patient Record (" + PatientRecord.Key + ") is deleted; the campaign save is untouched.");
    }

    [MenuItem("KEEP IT LIT/Debug/Patient Record: Unlock Everything", priority = 204)]
    private static void UnlockEverything()
    {
        PatientRecord.DebugUnlockEverything();
        Debug.Log("F122: the Patient Record now has every unlock (dev only; Delete Patient Record undoes it).");
    }

    private static string SavePath() => System.IO.Path.Combine(Application.persistentDataPath, "save.json");
}
