using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F100 (layer 1): the data the hub's wayfinding lines read, added to the hand-edited hub prefab without regenerating it
/// (build-missing-only): each mission's <c>where</c> word (the slip's order and the arrival line) (the Commissary's room name now comes from the Hub table, hub.shopdoor.room). Spec: plannings/hub-wayfinding-plan.md.
/// </summary>
public static partial class AsylumHubBuilder
{
    // index 0 = floor 1: where the giver stands. Matches the word after the '·' in HubMission.objectiveOffered.
    private static readonly string[] MissionWhere = { "NURSE STATION", "KITCHEN", "CLASSROOM", "EXAM ROOM" };

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

        Debug.Log("Hub wayfinding: " + (done.Count == 0 ? "nothing to add." : string.Join("; ", done)));
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
    }
}
