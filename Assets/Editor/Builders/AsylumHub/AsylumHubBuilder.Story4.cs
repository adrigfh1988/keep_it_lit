using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F93 G6: the finale pieces - AsylumHub/Life/Finale with the desk drawer, the master switch (a clone of the kit's fuse box, stripped of its
/// logic and scaled up) and the main-doors exit. Build-missing-only, called by AddStoryLayer.
/// </summary>
public static partial class AsylumHubBuilder
{
    private static List<string> G6Missing(Transform life)
    {
        List<string> missing = new List<string>();
        if (life == null || life.Find("Finale") == null) missing.Add("Life/Finale");
        return missing;
    }

    private static void AddG6(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        if (life.Find("Finale") != null) return;
        Physics.SyncTransforms();
        Transform deskT = FindBuilding("TableOffice (1)");
        Transform door = FindBuilding("MainDoor");
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (deskT == null || door == null || kit == null || kit.FuseBox == null)
        {
            Debug.LogError("Asylum story layer: the Director's desk (TableOffice (1)), the MainDoor or the kit's fuse box was not found; the finale was not built.");
            return;
        }

        Transform set = Group(life, "Finale");
        Bounds desk = RendBounds(deskT.gameObject);

        // The drawer: a trigger over the desk and the space in front of it.
        GameObject drawer = new GameObject("Desk");
        drawer.transform.SetParent(set, false);
        drawer.transform.position = new Vector3(desk.center.x, desk.max.y + 0.15f, desk.center.z - 0.35f);
        BoxCollider dc = drawer.AddComponent<BoxCollider>();
        dc.isTrigger = true;
        dc.size = new Vector3(desk.size.x + 0.4f, 1.0f, desk.size.z + 0.9f);
        HubFinaleDesk deskComp = drawer.AddComponent<HubFinaleDesk>();

        // The master switch: on the wall above the desk, facing into the room.
        Vector3 from = new Vector3(desk.center.x, desk.max.y + 1.2f, desk.center.z);
        Vector3 wallPoint = Physics.Raycast(from, Vector3.forward, out RaycastHit wall, 2.5f, ~0, QueryTriggerInteraction.Ignore) ? wall.point : from + Vector3.forward * 0.6f;
        FuseBox source = kit.FuseBox;
        FuseBox clone = Object.Instantiate(source, set);
        clone.gameObject.SetActive(true);
        clone.name = "MasterSwitch";
        foreach (Light l in clone.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
        foreach (ObjectiveToken t in clone.GetComponentsInChildren<ObjectiveToken>(true)) Object.DestroyImmediate(t);
        foreach (BoxCollider b in clone.GetComponents<BoxCollider>()) Object.DestroyImmediate(b);
        Object.DestroyImmediate(clone);
        Transform sw = set.Find("MasterSwitch");
        sw.localScale *= 2.2f;
        sw.rotation = Quaternion.Euler(0f, 180f, 0f); // +Z (its face) into the room, toward -Z
        Bounds sb = RendBounds(sw.gameObject);
        sw.position += new Vector3(from.x - sb.center.x, from.y - sb.center.y, (wallPoint.z - 0.01f - sb.size.z * 0.5f) - sb.center.z);
        sb = RendBounds(sw.gameObject);
        BoxCollider swc = sw.gameObject.AddComponent<BoxCollider>();
        swc.isTrigger = true;
        swc.center = sw.InverseTransformPoint(sb.center);
        swc.size = new Vector3(sb.size.x + 0.3f, sb.size.y + 0.3f, 0.9f);
        MasterSwitch swComp = sw.gameObject.AddComponent<MasterSwitch>();
        sw.gameObject.SetActive(false);

        // The main doors: a trigger in front of them, +Z of the object into the hall.
        GameObject exit = new GameObject("ExitDoor");
        exit.transform.SetParent(set, false);
        exit.transform.position = new Vector3(door.position.x, door.position.y + 1.2f, door.position.z + 0.7f);
        exit.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        BoxCollider ec = exit.AddComponent<BoxCollider>();
        ec.isTrigger = true;
        ec.size = new Vector3(2.4f, 2.4f, 1.4f);
        HubExitDoor exitComp = exit.AddComponent<HubExitDoor>();
        SerializedObject eo = new SerializedObject(exitComp);
        eo.FindProperty("leaf").objectReferenceValue = door;
        eo.ApplyModifiedPropertiesWithoutUndo();

        HubFinale finale = set.gameObject.AddComponent<HubFinale>();
        SerializedObject so = new SerializedObject(finale);
        so.FindProperty("desk").objectReferenceValue = deskComp;
        so.FindProperty("masterSwitch").objectReferenceValue = swComp;
        so.FindProperty("exitDoor").objectReferenceValue = exitComp;
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine($"Finale: desk at {desk.center}, master switch at {sb.center}, main door exit at {door.position}");
    }
}
