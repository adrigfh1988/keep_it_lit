using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F114 (G9): the seven hub rooms that held nothing of ours get one searchable spot (HubCache, under AsylumHub/Life/RoomFinds) and one scare
/// (a HubRoomScare subclass, under AsylumHub/Life/RoomScares). Build-missing-only, called by AddStoryLayer after AddG8 and listed in
/// StoryMissing, so Check Project flags it and Build Missing Pieces adds it; the hub prefab is never regenerated.
///
/// Measured 14 Oct 2026, where the plan's numbers were wrong (all reported by the run that built this): the pack's mirror cabinet leaves are
/// already pivoted on their outer edges (no hinge needed; some are authored ajar at -45 degrees), the fridge door is authored ajar at -36,
/// the cell with the open door has its corridor blocked by a morgue bed, and the other cell and the bathroom have closed doors. The stall doors
/// and the two doors that must be opened have no hinge, and a scene instance may not reparent children of its prefab, so their DoorHinge is
/// inserted into Assets/Prefabs/AsylumHub.prefab itself (<see cref="EnsureG9Hinges"/>), like the main doors' hinge in G8.
/// Pieces are named R1..R7 after the plan's rooms; each exists once its named child under Life/RoomFinds / Life/RoomScares does and its
/// component reports IsComplete.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string FindsName = "RoomFinds";
    private const string ScaresName = "RoomScares";
    private const string TvNoisePath = "Assets/SourceFiles/Textures/Asylum/Asylum_TvStaticNoise.png";

    private static readonly string[] FindNames =
    {
        "R1_ToiletsCabinet", "R2_ToiletsCistern", "R3_BathroomCabinet", "R4_KitchenFridge", "R5_VisitCase", "R6_GuardDrawer", "R7_CellMattress",
    };

    private static readonly string[] ScareNames =
    {
        "R1_StallSlam", "R2_CabinetsOpen", "R3_LightsHandprint", "R4_Television", "R5_ChairScrape", "R6_Cctv", "R7_CellDoor",
    };

    // Floor masks (bit n = floor n): every floor gets 3-4 of the 7 and no scare plays on more than three floors. R7 is never armed on floor 2
    // (the F113 lift chase runs in that basement).
    private const int MaskFloors135 = (1 << 1) | (1 << 3) | (1 << 5);
    private const int MaskFloors24 = (1 << 2) | (1 << 4);
    private const int MaskFloors235 = (1 << 2) | (1 << 3) | (1 << 5);
    private const int MaskFloors134 = (1 << 1) | (1 << 3) | (1 << 4);
    private const int MaskFloors124 = (1 << 1) | (1 << 2) | (1 << 4);
    private const int MaskFloors245 = (1 << 2) | (1 << 4) | (1 << 5);

    // ---------------------------------------------------------------- missing check

    private static List<string> G9Missing(AsylumHub hub)
    {
        List<string> missing = new List<string>();
        Transform life = hub.life;
        if (life == null) return missing; // the Life group itself is reported by StoryMissing
        Transform finds = life.Find(FindsName), scares = life.Find(ScaresName);

        for (int i = 0; i < FindNames.Length; i++)
        {
            Transform t = finds != null ? finds.Find(FindNames[i]) : null;
            HubCache cache = t != null ? t.GetComponent<HubCache>() : null;
            if (cache == null || !cache.IsComplete) { missing.Add($"Life/{FindsName}/{FindNames[i]}"); continue; }
            if (AnyStatic(cache.OpenParts)) missing.Add($"Life/{FindsName}/{FindNames[i]} (moved parts still static)");
        }
        for (int i = 0; i < ScareNames.Length; i++)
        {
            Transform t = scares != null ? scares.Find(ScareNames[i]) : null;
            HubRoomScare scare = t != null ? t.GetComponent<HubRoomScare>() : null;
            if (scare == null || !scare.IsComplete) { missing.Add($"Life/{ScaresName}/{ScareNames[i]}"); continue; }
            if (AnyStatic(scare.MovedParts)) missing.Add($"Life/{ScaresName}/{ScareNames[i]} (moved parts still static)");
        }
        if (LayerMask.NameToLayer(ScareCctv.LayerName) < 0) missing.Add($"layer {ScareCctv.LayerName}");
        return missing;
    }

    private static bool AnyStatic(IEnumerable<Transform> parts)
    {
        foreach (Transform part in parts)
        {
            if (part == null) continue;
            foreach (Transform t in part.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- the build

    private static void AddG9(AsylumHub hub, StringBuilder report)
    {
        Transform life = hub.life;
        if (life == null || G9Missing(hub).Count == 0) return;

        EnsureCctvLayer();
        if (!EnsureG9Hinges(hub, report)) return;

        Physics.SyncTransforms();
        Transform finds = life.Find(FindsName) ?? Group(life, FindsName);
        Transform scares = life.Find(ScaresName) ?? Group(life, ScaresName);

        // 1. doors that have to be open before anything else is placed or measured
        report.AppendLine(OpenG9Door("Door_V2 (4)", "Door", 95f, "R3 bathroom door"));
        report.AppendLine(OpenG9Door("Door_V3 (1)", "Door", 95f, "R7 cell A door"));
        report.AppendLine(OpenG9Stall("Bathroom_ToiletsWall (2)", "ToiletDoor (2)", 80f, "R2 last stall door (opened for the cistern)"));
        Physics.SyncTransforms();

        // 2. the finds
        BuildFind(finds, 0, report, () => BuildCabinetFind(hub, 0, "ToiletsA_floor", "ToiletsManA", "MirrorShelf (3)"));
        BuildFind(finds, 1, report, () => BuildCisternFind(hub));
        BuildFind(finds, 2, report, () => BuildCabinetFind(hub, 2, "Bathroom_floor", "Bathroom", "MirrorShelf (1)"));
        BuildFind(finds, 3, report, () => BuildFridgeFind(hub));
        BuildFind(finds, 4, report, () => BuildCaseFind(hub));
        BuildFind(finds, 5, report, () => BuildDrawerFind(hub));
        BuildFind(finds, 6, report, () => BuildMattressFind(hub));

        // 3. the scares (the television first: the CCTV's static is its noise texture)
        BuildScare(scares, 0, report, () => BuildStallScare(hub));
        BuildScare(scares, 1, report, () => BuildCabinetsScare(hub));
        BuildScare(scares, 2, report, () => BuildHandprintScare(hub));
        BuildScare(scares, 3, report, () => BuildTelevisionScare(hub));
        BuildScare(scares, 4, report, () => BuildChairScare(hub));
        BuildScare(scares, 5, report, () => BuildCctvScare(hub));
        BuildScare(scares, 6, report, () => BuildCellDoorScare(hub));

        // 4. whatever moves must not be static (also repairs a piece whose flags were set again)
        foreach (HubCache c in finds.GetComponentsInChildren<HubCache>(true)) ClearStaticParts(c.OpenParts);
        foreach (HubRoomScare s in scares.GetComponentsInChildren<HubRoomScare>(true)) ClearStaticParts(s.MovedParts);

        EditorUtility.SetDirty(life);
    }

    private static void BuildFind(Transform finds, int index, StringBuilder report, System.Func<string> build)
    {
        Transform existing = finds.Find(FindNames[index]);
        if (existing != null)
        {
            HubCache c = existing.GetComponent<HubCache>();
            if (c != null && c.IsComplete) return;
            RemoveG9Piece(existing);
        }
        string line = build();
        Transform made = finds.Find(FindNames[index]);
        if (made == null) { Debug.LogError($"Asylum room finds: {FindNames[index]} could not be built: {line}"); return; }
        report.AppendLine(line);
    }

    private static void BuildScare(Transform scares, int index, StringBuilder report, System.Func<string> build)
    {
        Transform existing = scares.Find(ScareNames[index]);
        if (existing != null)
        {
            HubRoomScare s = existing.GetComponent<HubRoomScare>();
            if (s != null && s.IsComplete) return;
            RemoveG9Piece(existing);
        }
        string line = build();
        Transform made = scares.Find(ScareNames[index]);
        if (made == null) { Debug.LogError($"Asylum room scares: {ScareNames[index]} could not be built: {line}"); return; }
        report.AppendLine(line);
    }

    /// <summary>
    /// Removes a piece that is incomplete (an earlier draft). A piece that only exists in the scene is destroyed; one that is already part of
    /// the hub prefab asset cannot be destroyed through its instance, so it is removed from the asset itself and the instance refreshed.
    /// </summary>
    private static void RemoveG9Piece(Transform piece)
    {
        if (piece == null) return;
        AsylumHub hub = piece.GetComponentInParent<AsylumHub>(true);
        if (hub == null || !PrefabUtility.IsPartOfPrefabInstance(piece.gameObject) || PrefabUtility.IsAddedGameObjectOverride(piece.gameObject))
        {
            Object.DestroyImmediate(piece.gameObject);
            return;
        }
        string path = AnimationUtility.CalculateTransformPath(piece, hub.transform);
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform t = contents.transform.Find(path);
            if (t != null) Object.DestroyImmediate(t.gameObject);
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
    }

    private static void ClearStaticParts(IEnumerable<Transform> parts)
    {
        foreach (Transform part in parts)
        {
            if (part == null) continue;
            ClearStatic(part);
            foreach (Transform t in part.GetComponentsInChildren<Transform>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
        }
    }

    // ---------------------------------------------------------------- layer

    /// <summary>Adds "CctvOnly" to the first free user layer (8..31) of ProjectSettings/TagManager.asset when the project has none.</summary>
    private static void EnsureCctvLayer()
    {
        if (LayerMask.NameToLayer(ScareCctv.LayerName) >= 0) return;
        SerializedObject tags = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset"));
        SerializedProperty layers = tags.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(slot.stringValue)) continue;
            slot.stringValue = ScareCctv.LayerName;
            tags.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"Asylum room scares: added the layer \"{ScareCctv.LayerName}\" at index {i} (ProjectSettings/TagManager.asset).");
            return;
        }
        Debug.LogError("Asylum room scares: no free user layer (8..31) for CctvOnly.");
    }

    // ---------------------------------------------------------------- lookups

    /// <summary>The transform called `name` whose parent is called `parentName` (or whose parent is the DoorHinge under it), under the building.</summary>
    private static Transform Under(string parentName, string name)
    {
        return _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name && t.parent != null &&
            (t.parent.name == parentName || (t.parent.name == "DoorHinge" && t.parent.parent != null && t.parent.parent.name == parentName)));
    }

    /// <summary>A flat box over the room's floor (from the <c>*_floor</c> renderer): floor-0.15 up to floor+height.</summary>
    private static Bounds FloorBox(string floorName, float height = 3f)
    {
        Transform f = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == floorName);
        Renderer r = f != null ? f.GetComponent<Renderer>() : null;
        if (r == null)
        {
            Debug.LogError($"Asylum room finds: no floor renderer called {floorName}.");
            return new Bounds(Vector3.one * -9999f, Vector3.one);
        }
        Bounds b = r.bounds;
        float y0 = b.max.y - 0.15f, y1 = b.max.y + height;
        return new Bounds(new Vector3(b.center.x, 0.5f * (y0 + y1), b.center.z), new Vector3(b.size.x, y1 - y0, b.size.z));
    }

    private static Bounds RendererBoundsOf(Transform t)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static float YawOf(Transform t)
    {
        float y = t.localEulerAngles.y;
        return y > 180f ? y - 360f : y;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }

    /// <summary>True when a complete path on the Hub agent leads from the main entrance to (near) `p`.</summary>
    private static bool Reach(AsylumHub hub, Vector3 p)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = HubNavMesh.FindAgentType(), areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.8f, filter)) return false;
        NavMeshPath path = new NavMeshPath();
        return NavMesh.CalculatePath(hub.mainEntrance.position, hit.position, filter, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    // ---------------------------------------------------------------- hinges (prefab asset)

    private sealed class HingeJob
    {
        public string Path;
        public bool Stall;
        public bool Straighten;
    }

    /// <summary>The leaves that need a DoorHinge: (door/wall root, leaf, stall door).</summary>
    private static IEnumerable<(string root, string leaf, bool stall)> G9HingeLeaves()
    {
        yield return ("Bathroom_ToiletsWall (4)", "ToiletDoor (2)", true);  // R1: the stall that slams
        yield return ("Bathroom_ToiletsWall (2)", "ToiletDoor (2)", true);  // R2: the stall that is opened for the cistern
        yield return ("Door_V2 (4)", "Door", false);                          // R3: the bathroom door (closed in the pack)
        yield return ("Door_V3 (1)", "Door", false);                          // R7: the cell door (closed in the pack)
    }

    /// <summary>
    /// A scene instance may not reparent children of the prefab, so the hinges for the leaves that have none are hung in the prefab asset
    /// (LoadPrefabContents), exactly as EnsureMainDoorHinge does for the main doors. Door_V leaves use ShopRoom.CreateDoorHinge (the leaf's
    /// min-X edge in the door's frame, like every pack door the B4 pass swung); a stall door gets its hinge on its south (min-Z) edge.
    /// Returns false (error logged) when the instance has not picked the hinges up yet.
    /// </summary>
    private static bool EnsureG9Hinges(AsylumHub hub, StringBuilder report)
    {
        List<HingeJob> jobs = new List<HingeJob>();
        foreach ((string root, string leafName, bool stall) in G9HingeLeaves())
        {
            Transform leaf = Under(root, leafName);
            if (leaf == null) { Debug.LogError($"Asylum room scares: the leaf {root}/{leafName} was not found."); return false; }
            if (leaf.parent != null && leaf.parent.name == "DoorHinge")
            {
                // A first draft of this builder hung stall hinges at a world-identity rotation (a local yaw of 90 under the wall): straighten them.
                Transform h = leaf.parent;
                if (stall && Quaternion.Angle(h.localRotation, Quaternion.identity) > 0.5f && Quaternion.Angle(h.rotation, Quaternion.identity) < 0.5f)
                    jobs.Add(new HingeJob { Path = AnimationUtility.CalculateTransformPath(h, hub.transform), Stall = true, Straighten = true });
                continue;
            }
            jobs.Add(new HingeJob { Path = AnimationUtility.CalculateTransformPath(leaf, hub.transform), Stall = stall });
        }
        if (jobs.Count == 0) return true;

        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (HingeJob job in jobs)
            {
                Transform leaf = contents.transform.Find(job.Path);
                if (leaf == null) { Debug.LogError($"Asylum room scares: {job.Path} was not found in {PrefabPath}."); return false; }
                if (job.Straighten)
                {
                    Transform hingeT = leaf; // the path names the hinge itself
                    Transform child = hingeT.GetChild(0);
                    child.SetParent(hingeT.parent, true);
                    hingeT.localRotation = Quaternion.identity;
                    child.SetParent(hingeT, true);
                    report.AppendLine($"hinge straightened in the prefab: {job.Path}");
                    continue;
                }
                ClearStatic(leaf);
                Transform hinge = job.Stall ? StallHinge(leaf) : ShopRoom.CreateDoorHinge(leaf.parent, leaf);
                if (hinge == null) { Debug.LogError($"Asylum room scares: {job.Path} has no renderer; no hinge."); return false; }
                hinge.name = "DoorHinge";
                GameObjectUtility.SetStaticEditorFlags(hinge.gameObject, 0);
                report.AppendLine($"hinge hung in the prefab: {job.Path}");
            }
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        foreach ((string root, string leafName, bool stall) in G9HingeLeaves())
        {
            Transform leaf = Under(root, leafName);
            if (leaf == null || leaf.parent == null || leaf.parent.name != "DoorHinge")
            {
                Debug.LogError($"Asylum room scares: the hub in the scene has not picked up the new hinge of {root}/{leafName} yet: run KEEP IT LIT > Build Missing Pieces again.");
                return false;
            }
        }
        return true;
    }

    /// <summary>A pivot on the south (min-Z) edge of a stall door's leaf (the leaf is thin in X and wide in Z), the leaf parented under it.</summary>
    private static Transform StallHinge(Transform leaf)
    {
        Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

        GameObject hinge = new GameObject("DoorHinge");
        hinge.transform.SetParent(leaf.parent, false);
        hinge.transform.position = new Vector3(b.center.x, b.center.y, b.min.z);
        hinge.transform.rotation = leaf.parent.rotation; // local identity: a swing is then a yaw about the hinge's own up axis, like every other hinge
        leaf.SetParent(hinge.transform, true);
        return hinge.transform;
    }

    // ---------------------------------------------------------------- opening doors (scene instance)

    /// <summary>Swings a pack door (its leaf already under a DoorHinge) open toward the side with more free room, like AsylumHubBuilder.SwingDoor. Returns the signed yaw applied.</summary>
    private static string OpenG9Door(string doorRoot, string leafName, float angle, string label)
    {
        Transform root = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == doorRoot);
        Transform leaf = Under(doorRoot, leafName);
        if (root == null || leaf == null || leaf.parent == null || leaf.parent.name != "DoorHinge") return $"{label}: not found / no hinge";
        Transform hinge = leaf.parent;
        if (Mathf.Abs(YawOf(hinge)) > 1f) return $"{label}: already open ({YawOf(hinge):F0})";

        PackDoor door = new PackDoor { Root = root };
        door.Leaves.Add(leaf);
        Vector3 axis = root.forward;
        Vector3 centre = DoorCentre(door);
        float front = FreeDistance(centre, axis), back = FreeDistance(centre, -axis);
        float sign = front >= back ? 1f : -1f;
        float yaw = -sign * angle; // the leaf hinges on its min-X edge: the same convention SwingDoor uses for a left/single leaf
        hinge.localRotation = Quaternion.Euler(0f, yaw, 0f) * hinge.localRotation;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hinge);
        return $"{label}: swung {yaw:F0} degrees (free room {front:F2} m ahead, {back:F2} m behind)";
    }

    /// <summary>Opens a stall door toward the room (the side with more free room along X), by rotating its hinge.</summary>
    private static string OpenG9Stall(string wallName, string leafName, float angle, string label)
    {
        Transform leaf = Under(wallName, leafName);
        if (leaf == null || leaf.parent == null || leaf.parent.name != "DoorHinge") return $"{label}: not found / no hinge";
        Transform hinge = leaf.parent;
        if (Mathf.Abs(YawOf(hinge)) > 1f) return $"{label}: already open ({YawOf(hinge):F0})";
        float yaw = StallYaw(leaf, hinge, angle, out float room, out float stall);
        hinge.localRotation = Quaternion.Euler(0f, yaw, 0f) * hinge.localRotation;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hinge);
        return $"{label}: swung {yaw:F0} degrees (free {room:F2} m toward the room, {stall:F2} m into the stall)";
    }

    /// <summary>The signed yaw that opens a stall door `angle` degrees toward the side (along X) with more free distance.</summary>
    private static float StallYaw(Transform leaf, Transform hinge, float angle, out float freeA, out float freeB)
    {
        Vector3 centre = RendererBoundsOf(leaf).center;
        freeA = FreeDistance(centre, Vector3.left);   // -X: toward the room in the toilets (the stalls are on the east wall)
        freeB = FreeDistance(centre, Vector3.right);  // +X: into the stall
        Vector3 want = freeA >= freeB ? Vector3.left : Vector3.right;
        // Which sign of yaw moves the leaf's centre toward `want`?
        Vector3 fromPivot = centre - hinge.position;
        Vector3 test = Quaternion.Euler(0f, 10f, 0f) * fromPivot;
        float positive = Vector3.Dot(test - fromPivot, want);
        return positive > 0f ? angle : -angle;
    }
}
