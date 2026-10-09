using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F100 (layer 1): the data the hub's wayfinding lines read, added to the hand-edited hub prefab without regenerating it
/// (build-missing-only): each mission's <c>where</c> word (the slip's order and the arrival line) and the SHOP door's
/// <c>roomName</c> (THE SHOP IS OPEN · ...). Spec: plannings/hub-wayfinding-plan.md.
/// </summary>
public static partial class AsylumHubBuilder
{
    // index 0 = floor 1: where the giver stands. Matches the word after the '·' in HubMission.objectiveOffered.
    private static readonly string[] MissionWhere = { "NURSE STATION", "KITCHEN", "CLASSROOM", "EXAM ROOM" };

    /// <summary>Rooms tried for the SHOP door's name, smallest box wins, with the words the arrival line uses.</summary>
    private static readonly (string room, string label)[] ShopDoorLabels =
    {
        ("Entrance", "THE ENTRANCE HALL"), ("OfficeA", "THE RECORDS ROOM"), ("HallDownstair", "THE GROUND FLOOR HALL"),
    };

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it) or the wayfinding data is all there.</summary>
    public static string WayfindingProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null;
        List<string> missing = new List<string>();
        if (hub.life != null)
        {
            foreach (HubMission m in hub.life.GetComponentsInChildren<HubMission>(true)) if (string.IsNullOrEmpty(m.where)) missing.Add($"Missions/Floor{m.floor}.where");
        }
        if (hub.shopDoor != null && string.IsNullOrEmpty(hub.shopDoor.roomName)) missing.Add("ShopDoor.roomName");
        return missing.Count == 0 ? null : "missing: " + string.Join(", ", missing);
    }

    /// <summary>ProjectSetup's fix. Marks the scene dirty; the caller saves.</summary>
    public static void WayfindingRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Hub wayfinding: there is no hub in the scene (Build Missing Pieces places it first)."); return; }

        List<string> done = new List<string>();
        if (hub.life != null)
        {
            foreach (HubMission m in hub.life.GetComponentsInChildren<HubMission>(true))
            {
                if (!string.IsNullOrEmpty(m.where) || m.floor < 1 || m.floor > MissionWhere.Length) continue;
                SerializedObject so = new SerializedObject(m);
                so.FindProperty("where").stringValue = MissionWhere[m.floor - 1];
                so.ApplyModifiedPropertiesWithoutUndo();
                done.Add($"floor {m.floor} = {MissionWhere[m.floor - 1]}");
            }
        }

        if (hub.shopDoor != null && string.IsNullOrEmpty(hub.shopDoor.roomName) && hub.building != null)
        {
            bool wasActive = hub.ContentActive;
            hub.SetContentActive(true);
            Physics.SyncTransforms();
            string label = null;
            float smallest = float.MaxValue;
            Vector3 at = hub.shopDoor.transform.position + Vector3.up * 1.2f - hub.shopDoor.transform.forward * 0.3f; // just inside the room
            foreach ((string room, string text) in ShopDoorLabels)
            {
                Bounds b = RoomBounds(hub.building, room, 0.4f);
                if (b.size == Vector3.zero || !b.Contains(at)) continue;
                float area = b.size.x * b.size.z;
                if (area < smallest) { smallest = area; label = text; }
            }
            if (label == null) { label = "THE ENTRANCE HALL"; Debug.LogWarning($"Hub wayfinding: the SHOP door at {hub.shopDoor.transform.position:F1} is in none of {string.Join(", ", ShopDoorLabels.Select(l => l.room))}; named it {label}. Edit ShopDoor.roomName by hand if that is wrong."); }
            SerializedObject sd = new SerializedObject(hub.shopDoor);
            sd.FindProperty("roomName").stringValue = label;
            sd.ApplyModifiedPropertiesWithoutUndo();
            done.Add("shop door = " + label);
            if (!wasActive) hub.SetContentActive(false);
        }

        Debug.Log("Hub wayfinding: " + (done.Count == 0 ? "nothing to add." : string.Join("; ", done)));
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
    }
}
