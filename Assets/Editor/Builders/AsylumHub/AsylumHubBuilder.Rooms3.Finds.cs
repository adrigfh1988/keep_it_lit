using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>F114 (G9), part 1: the seven searchable spots (HubCache) under AsylumHub/Life/RoomFinds.</summary>
public static partial class AsylumHubBuilder
{
    // ---------------------------------------------------------------- serialized-field helpers

    private static void PropObj(SerializedObject so, string name, Object value) => so.FindProperty(name).objectReferenceValue = value;
    private static void PropFloat(SerializedObject so, string name, float value) => so.FindProperty(name).floatValue = value;
    private static void PropInt(SerializedObject so, string name, int value) => so.FindProperty(name).intValue = value;
    private static void PropString(SerializedObject so, string name, string value) => so.FindProperty(name).stringValue = value;

    private static void PropObjects(SerializedObject so, string name, IList<Object> values)
    {
        SerializedProperty p = so.FindProperty(name);
        p.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void PropVectors(SerializedObject so, string name, IList<Vector3> values)
    {
        SerializedProperty p = so.FindProperty(name);
        p.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).vector3Value = values[i];
    }

    private static void PropFloats(SerializedObject so, string name, IList<float> values)
    {
        SerializedProperty p = so.FindProperty(name);
        p.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).floatValue = values[i];
    }

    /// <summary>The meters of motion along world direction `dir`, expressed in `part`'s own local axes (HubCache adds it as closedRot * offset to the local position).</summary>
    private static Vector3 LocalOffset(Transform part, Vector3 dir, float meters)
    {
        Vector3 inParent = part.parent != null ? part.parent.InverseTransformDirection(dir) : dir;
        return Quaternion.Inverse(part.localRotation) * inParent * meters;
    }

    // ---------------------------------------------------------------- one find

    /// <summary>Creates AsylumHub/Life/RoomFinds/&lt;name&gt; (world-aligned unless `yaw` says otherwise) with a trigger box and a configured HubCache.</summary>
    private static string MakeFind(AsylumHub hub, int id, Vector3 center, float yaw, Vector3 boxSize, HubCache.Sound sound,
        IList<Transform> parts, IList<Vector3> offsets, IList<Vector3> eulers, string label)
    {
        Transform finds = hub.life.Find(FindsName);
        GameObject go = new GameObject(FindNames[id]);
        go.transform.SetParent(finds, false);
        go.transform.position = center;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = boxSize;

        HubCache cache = go.AddComponent<HubCache>();
        SerializedObject so = new SerializedObject(cache);
        PropInt(so, "cacheId", id);
        PropInt(so, "sound", (int)sound);
        PropObjects(so, "openParts", parts.Cast<Object>().ToList());
        PropVectors(so, "openOffsets", offsets);
        PropVectors(so, "openEulers", eulers);
        PropObj(so, "trigger", box);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{FindNames[id]}: {label} at {center:F2}, trigger {boxSize:F2}, {parts.Count} moving part(s)";
    }

    // ---------------------------------------------------------------- R1 / R3: a mirror cabinet

    private static string BuildCabinetFind(AsylumHub hub, int id, string floorName, string roomName, string shelfName)
    {
        Bounds room = FloorBox(floorName);
        Transform shelf = Under(roomName, shelfName);
        Transform caseT = shelf != null ? shelf.Find("MirrorShelf_Case") : null;
        Transform left = shelf != null ? shelf.Find("MirrorShelf_DoorL") : null;
        Transform right = shelf != null ? shelf.Find("MirrorShelf_DoorR") : null;
        if (caseT == null || left == null || right == null) return $"cabinet {roomName}/{shelfName} not found";

        Vector3 front = Flat(-shelf.forward);
        if (Vector3.Dot(front, room.center - shelf.position) < 0f) front = -front;
        Vector3 center = RendererBoundsOf(caseT).center + front * 0.3f;
        // The pack pivots each leaf on its outer edge and authors a closed leaf at local yaw 0: the left leaf opens to +yaw, the right one to -yaw.
        Vector3[] eulers = { new Vector3(0f, 100f - YawOf(left), 0f), new Vector3(0f, -100f - YawOf(right), 0f) };
        return MakeFind(hub, id, center, Quaternion.LookRotation(front).eulerAngles.y, new Vector3(0.8f, 0.6f, 0.6f), HubCache.Sound.Rummage,
            new[] { left, right }, new[] { Vector3.zero, Vector3.zero }, eulers, $"cabinet {roomName}/{shelfName}");
    }

    // ---------------------------------------------------------------- R2: the cistern of the last stall

    private static string BuildCisternFind(AsylumHub hub)
    {
        Transform toilet = Under("ToiletsWoman", "Toilet (6)");
        MeshFilter filter = toilet != null ? toilet.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return "toilet (R2 last stall) not found or not readable";

        // The cistern is the tall part at the back: the centroid of the vertices in the top third.
        Mesh mesh = filter.sharedMesh;
        float cut = mesh.bounds.min.y + mesh.bounds.size.y * 0.65f;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Vector3 v in mesh.vertices)
        {
            if (v.y < cut) continue;
            sum += v;
            count++;
        }
        Vector3 cistern = toilet.TransformPoint(count > 0 ? sum / count : mesh.bounds.center);

        // "Back" is the nearest wall; the player reaches in from the other side.
        Vector3 back = Vector3.right;
        float nearest = float.MaxValue;
        foreach (Vector3 dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            if (Physics.Raycast(cistern, dir, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore) && hit.distance < nearest && hit.collider.transform != toilet)
            {
                nearest = hit.distance;
                back = dir;
            }
        }
        Vector3 center = cistern - back * 0.35f;
        return MakeFind(hub, 1, center, Quaternion.LookRotation(-back).eulerAngles.y, new Vector3(0.6f, 0.6f, 0.7f), HubCache.Sound.RummageClink,
            new Transform[0], new Vector3[0], new Vector3[0], $"cistern of {toilet.name} (wall {nearest:F2} m behind it)");
    }

    // ---------------------------------------------------------------- R4: the fridge

    private static string BuildFridgeFind(AsylumHub hub)
    {
        Bounds room = FloorBox("Kitchen_floor");
        Transform fridge = Under("Kitchen", "Fridge");
        Transform door = fridge != null ? fridge.Find("Fridge_DoorD") : null;
        Transform caseT = fridge != null ? fridge.Find("Fridge_Case") : null;
        if (door == null || caseT == null) return "fridge not found";

        Bounds cb = RendererBoundsOf(caseT);
        Vector3 toRoom = room.center - cb.center;
        Vector3 front = Mathf.Abs(toRoom.x) > Mathf.Abs(toRoom.z) ? new Vector3(Mathf.Sign(toRoom.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toRoom.z));
        float depth = Mathf.Abs(front.x) * cb.extents.x + Mathf.Abs(front.z) * cb.extents.z;
        Vector3 center = cb.center + front * (depth + 0.35f);
        center.y = cb.min.y + 1.0f;

        // The pack authors this door ajar (-36 degrees); the search swings it on to a wide 95 in the same direction.
        float authored = YawOf(door);
        float target = (Mathf.Abs(authored) > 5f ? Mathf.Sign(authored) : -1f) * 95f;
        return MakeFind(hub, 3, center, Quaternion.LookRotation(front).eulerAngles.y, new Vector3(0.9f, 0.8f, 0.7f), HubCache.Sound.Rummage,
            new[] { door }, new[] { Vector3.zero }, new[] { new Vector3(0f, target - authored, 0f) }, $"fridge (door authored at {authored:F0}, opens to {target:F0})");
    }

    // ---------------------------------------------------------------- R5: a visitor's case under a chair

    private static string BuildCaseFind(AsylumHub hub)
    {
        const string prefabPath = "Assets/Abandoned_Asylum/Prefabs/SmallMetalicCase.prefab";
        const string fallbackPath = "Assets/Abandoned_Asylum/Prefabs/Box_V1.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? AssetDatabase.LoadAssetAtPath<GameObject>(fallbackPath);
        if (prefab == null) return "SmallMetalicCase.prefab / Box_V1.prefab not found";
        Bounds room = FloorBox("VisitRoom_floor");

        // The leather chair nearest the back wall: the one furthest east, and of those the furthest from the doorway into the room.
        Transform doorFrame = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Door_Frame (1)" && t.parent != null && t.parent.name == "GuardRoom");
        Vector3 doorway = doorFrame != null ? doorFrame.position : new Vector3(room.min.x, room.min.y, room.center.z);
        Transform chair = _building.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("ChairLeather") && t.GetComponent<Renderer>() != null && room.Contains(t.GetComponent<Renderer>().bounds.center))
            .OrderByDescending(t => Mathf.Round(t.GetComponent<Renderer>().bounds.center.x * 4f)).ThenByDescending(t => Mathf.Abs(t.GetComponent<Renderer>().bounds.center.z - doorway.z)).FirstOrDefault();
        if (chair == null) return "no leather chair in the visiting room";
        Bounds chairBounds = chair.GetComponent<Renderer>().bounds;

        // Toward the nearest wall of the room is "back".
        float toEast = room.max.x - chairBounds.center.x, toWest = chairBounds.center.x - room.min.x, toNorth = room.max.z - chairBounds.center.z, toSouth = chairBounds.center.z - room.min.z;
        float closest = Mathf.Min(Mathf.Min(toEast, toWest), Mathf.Min(toNorth, toSouth));
        Vector3 back = closest == toEast ? Vector3.right : closest == toWest ? Vector3.left : closest == toNorth ? Vector3.forward : Vector3.back;

        Transform finds = hub.life.Find(FindsName);
        GameObject holder = new GameObject(FindNames[4]);
        holder.transform.SetParent(finds, false);
        holder.transform.position = chairBounds.center;

        GameObject prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
        PrefabUtility.UnpackPrefabInstance(prop, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        prop.name = "Case";
        foreach (Transform t in prop.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

        // About 0.45 m long, its long axis parallel to the wall it lies against, lying on the floor, half under the chair.
        prop.transform.rotation = Quaternion.identity;
        Bounds b = RendererBoundsOf(prop.transform);
        float longest = Mathf.Max(b.size.x, b.size.z);
        if (longest > 0.001f) prop.transform.localScale *= 0.45f / longest;
        b = RendererBoundsOf(prop.transform);
        bool wallAlongX = Mathf.Abs(back.z) > 0.5f; // the wall runs along X when "back" is north/south
        if ((b.size.x > b.size.z) != wallAlongX) prop.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        b = RendererBoundsOf(prop.transform);
        float floorY = room.min.y + 0.15f;
        Vector3 at = new Vector3(chairBounds.center.x, 0f, chairBounds.center.z) + back * 0.12f;
        prop.transform.position += new Vector3(at.x - b.center.x, floorY - b.min.y, at.z - b.center.z);
        Physics.SyncTransforms();
        b = RendererBoundsOf(prop.transform);

        BoxCollider box = holder.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = holder.transform.InverseTransformPoint(b.center + Vector3.up * 0.05f);
        box.size = b.size + new Vector3(0.3f, 0.3f, 0.3f);

        // Opening: the case tips about its long axis and slides 0.1 m out from under the chair.
        Vector3 longAxisWorld = b.size.x > b.size.z ? Vector3.right : Vector3.forward;
        Vector3 localAxis = Quaternion.Inverse(prop.transform.rotation) * longAxisWorld;
        Vector3 tip = Quaternion.AngleAxis(12f, localAxis).eulerAngles;
        HubCache cache = holder.AddComponent<HubCache>();
        SerializedObject so = new SerializedObject(cache);
        PropInt(so, "cacheId", 4);
        PropInt(so, "sound", (int)HubCache.Sound.Rummage);
        PropObjects(so, "openParts", new Object[] { prop.transform });
        PropVectors(so, "openOffsets", new[] { LocalOffset(prop.transform, -back, 0.1f) });
        PropVectors(so, "openEulers", new[] { tip });
        PropObj(so, "trigger", box);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{FindNames[4]}: case {prop.name} ({b.size:F2}) under {chair.name} at {chairBounds.center:F2}, back {back}";
    }

    // ---------------------------------------------------------------- R6: a drawer of the sheet rack nearest the desk

    private static string BuildDrawerFind(AsylumHub hub)
    {
        Bounds room = FloorBox("GuardRoom_floor");
        Transform desk = Under("GuardRoom", "TableOffice (3)");
        if (desk == null) return "guard room desk not found";
        Vector3 deskAt = desk.GetComponent<Renderer>().bounds.center;

        Transform rack = _building.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("SheetRack (") && t.parent != null && t.parent.name == "GuardRoom" && t.GetComponentsInChildren<Renderer>(true).Length > 0)
            .OrderBy(t => Vector3.Distance(RendererBoundsOf(t).center, deskAt)).FirstOrDefault();
        if (rack == null) return "no sheet rack in the guard room";

        // Top row (about waist height: the row nearest 1.0 m above the floor), a drawer that sits flush, the one nearest the desk.
        float waist = room.min.y + 0.15f + 1.0f;
        Transform[] drawers = rack.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("SheetRackCase_") && t.GetComponent<Renderer>() != null).ToArray();
        float rowY = drawers.Select(t => t.GetComponent<Renderer>().bounds.center.y).OrderBy(y => Mathf.Abs(y - waist)).First();
        Vector3 rackFace = RendererBoundsOf(rack).center;
        Transform[] row = drawers.Where(t => Mathf.Abs(t.GetComponent<Renderer>().bounds.center.y - rowY) < 0.05f).ToArray();
        // "Flush" = the drawer's depth coordinate (towards the room) is the smallest of the row.
        Vector3 into = Flat(room.center - rackFace);
        into = Mathf.Abs(into.x) > Mathf.Abs(into.z) ? new Vector3(Mathf.Sign(into.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(into.z));
        float flush = row.Min(t => Vector3.Dot(t.GetComponent<Renderer>().bounds.center, -into));
        Transform drawer = row.Where(t => Vector3.Dot(t.GetComponent<Renderer>().bounds.center, -into) < flush + 0.03f)
            .OrderBy(t => Vector3.Distance(t.GetComponent<Renderer>().bounds.center, deskAt)).First();

        Bounds db = drawer.GetComponent<Renderer>().bounds;
        float depth = Mathf.Abs(into.x) * db.extents.x + Mathf.Abs(into.z) * db.extents.z;
        Vector3 center = db.center + into * (depth + 0.3f);
        return MakeFind(hub, 5, center, Quaternion.LookRotation(into).eulerAngles.y, new Vector3(0.7f, 0.6f, 0.7f), HubCache.Sound.MetalSlide,
            new[] { drawer }, new[] { LocalOffset(drawer, into, 0.32f) }, new[] { Vector3.zero }, $"drawer {rack.name}/{drawer.name}, comes out along world {into}");
    }

    // ---------------------------------------------------------------- R7: a slit in the cell's mattress

    private static string BuildMattressFind(AsylumHub hub)
    {
        Transform mattress = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "F101_SeclusionRoomA_Mattress")
            ?? hub.dressing.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "F101_SeclusionRoomA_Mattress");
        if (mattress == null) return "cell A mattress not found";
        Bounds b = RendererBoundsOf(mattress);

        Transform finds = hub.life.Find(FindsName);
        GameObject go = new GameObject(FindNames[6]);
        go.transform.SetParent(finds, false);
        go.transform.position = b.center + Vector3.up * 0.05f;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = b.size + new Vector3(0.3f, 0.3f, 0.3f);

        // The slit: a thin dark quad on top, along the mattress' long axis, a little off centre.
        GameObject slit = GameObject.CreatePrimitive(PrimitiveType.Quad);
        slit.name = "Slit";
        Object.DestroyImmediate(slit.GetComponent<Collider>());
        slit.transform.SetParent(go.transform, true);
        bool alongZ = b.size.z >= b.size.x;
        slit.transform.rotation = Quaternion.Euler(90f, alongZ ? 0f : 90f, 0f); // lying flat, face up
        slit.transform.position = new Vector3(b.center.x + (alongZ ? 0.12f : 0f), b.max.y + 0.004f, b.center.z + (alongZ ? 0f : 0.12f));
        slit.transform.localScale = new Vector3(0.02f, 0.34f, 1f);
        MeshRenderer mr = slit.GetComponent<MeshRenderer>();
        mr.sharedMaterial = SlitMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(slit, 0);

        HubCache cache = go.AddComponent<HubCache>();
        SerializedObject so = new SerializedObject(cache);
        PropInt(so, "cacheId", 6);
        PropInt(so, "sound", (int)HubCache.Sound.Paper);
        PropObjects(so, "openParts", new Object[0]);
        PropVectors(so, "openOffsets", new Vector3[0]);
        PropVectors(so, "openEulers", new Vector3[0]);
        PropObj(so, "trigger", box);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{FindNames[6]}: mattress {mattress.name} at {b.center:F2}, trigger {box.size:F2}";
    }

    private static Material SlitMaterial()
    {
        EnsureFolderPath(MaterialFolder);
        string path = MaterialFolder + "/Asylum_RoomFindSlit.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Asylum_RoomFindSlit" };
        m.SetColor("_BaseColor", new Color(0.015f, 0.012f, 0.012f));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
