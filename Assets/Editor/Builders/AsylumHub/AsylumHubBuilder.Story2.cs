using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F93 G4: the decay, the floor-5 patrol and key run, the hub lockers, Stan's echo, and the Mixamo animation controllers.
/// Everything is added by <see cref="AddG4"/> (called from AddStoryLayer) only when its named child under AsylumHub/Life is absent;
/// nothing existing is rebuilt. The Mixamo clips live in Assets/SourceFiles/Animation/Mixamo (copied from the user's library; the
/// originals are never touched).
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string MixamoDir = "Assets/SourceFiles/Animation/Mixamo/";
    private const string HubAnimDir = "Assets/SourceFiles/Animation/Hub";
    public const string PeopleControllerPath = HubAnimDir + "/HubPeople.controller";
    public const string PursuerControllerPath = HubAnimDir + "/HubPursuer.controller";

    private static AnimationClip MixamoClip(string name)
    {
        string path = MixamoDir + name + ".fbx";
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null) Debug.LogWarning($"Asylum story layer: Mixamo clip {path} not found.");
        return clip;
    }

    /// <summary>One AnimatorController with a state per (stateName, clip); no transitions - the scripts CrossFade by state name.</summary>
    private static AnimatorController EnsureController(string path, (string state, string clip)[] states)
    {
        AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing != null)
        {
            // Build-missing-only at state level: add states a newer version needs.
            AnimatorStateMachine sm = existing.layers[0].stateMachine;
            bool changed = false;
            foreach ((string state, string clip) in states)
            {
                if (sm.states.Any(s => s.state.name == state)) continue;
                AnimatorState added = sm.AddState(state);
                added.motion = MixamoClip(clip);
                changed = true;
            }
            if (changed) { EditorUtility.SetDirty(existing); AssetDatabase.SaveAssets(); }
            return existing;
        }
        EnsureFolderPath(HubAnimDir);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        bool first = true;
        foreach ((string state, string clip) in states)
        {
            AnimatorState s = machine.AddState(state);
            s.motion = MixamoClip(clip);
            if (first) { machine.defaultState = s; first = false; }
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    public static AnimatorController PeopleController() => EnsureController(PeopleControllerPath, new[]
    {
        ("Look", "Look_Around"), ("Nervous", "Nervous_Look_Around"), ("Lie", "Laying_Idle"), ("SitIdle", "Sitting_Idle"),
        ("SitDisbelief", "Sitting_Disbelief"), ("Talking", "Talking"), ("StandUp", "Standing_Up"), ("StoopIdle", "Zombie_Idle"),
        ("Walk", "Walking"), ("Run", "Zombie_Run"), ("Typing", "Typing"),
        ("Patrol", "Walking"), ("Warn", "Look_Around"), ("Scream", "Zombie_Scream"), ("Rush", "Zombie_Run"), ("Pin", "Zombie_Attack"),
    });

    /// <summary>The Intake's hide-step silhouette: one looping walk (IntakeRoom only toggles animator.speed, so the default state must walk).</summary>
    public static AnimatorController IntakeSilhouetteController() => EnsureController(HubAnimDir + "/IntakeSilhouette.controller", new[]
    {
        ("Walk", "Zombie_Walk"),
    });

    public static AnimatorController PursuerController() => EnsureController(PursuerControllerPath, new[]
    {
        ("Idle", "Zombie_Idle"), ("Walk", "Zombie_Walk"), ("Run", "Zombie_Run"), ("Scream", "Zombie_Scream"), ("Attack", "Zombie_Attack"),
        ("StandUp", "Standing_Up"), ("Crawl", "Zombie_Crawl"), ("RunCrawl", "Zombie_RunningCrawl"),
    });

    // ---------------------------------------------------------------- G4

    private static List<string> G4Missing(AsylumHub hub, Transform life)
    {
        List<string> missing = new List<string>();
        foreach (string n in new[] { "Pursuer", "PatrolRoute", "Lockers", "DirectorKey", "Decay", "KeyRun", "EchoRoute", "StanEcho" })
        {
            if (life == null || life.Find(n) == null) missing.Add("Life/" + n);
        }
        return missing;
    }

    /// <summary>The Mixamo controller on every patient body (idempotent), plus the lie offsets Elias needs.</summary>
    private static void UpgradePeople(Transform life)
    {
        AnimatorController people = PeopleController();
        Transform patients = life.Find(PatientsName);
        if (patients == null) return;
        foreach (HubPatient p in patients.GetComponentsInChildren<HubPatient>(true))
        {
            if (p.controller == null) { p.controller = people; EditorUtility.SetDirty(p); }
            Animator a = p.body != null ? p.body.GetComponent<Animator>() : null;
            if (a != null && a.runtimeAnimatorController != people) { a.runtimeAnimatorController = people; EditorUtility.SetDirty(a); }
        }
    }

    private static void AddG4(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        UpgradePeople(life);
        Physics.SyncTransforms();

        Transform pursuerT = life.Find("Pursuer") ?? BuildPursuer(hub, life, report);
        Transform route = life.Find("PatrolRoute") ?? BuildPatrolRoute(hub, life, report);
        if (life.Find("Lockers") == null) BuildHubLockers(hub, life, route, report);
        Transform key = life.Find("DirectorKey") ?? BuildDirectorKey(hub, life, report);

        if (life.Find("Decay") == null)
        {
            Transform decay = Group(life, "Decay");
            HubDecay d = decay.gameObject.AddComponent<HubDecay>();
            SerializedObject so = new SerializedObject(d);
            so.FindProperty("hub").objectReferenceValue = hub;
            so.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine("Decay added");
        }

        if (life.Find("KeyRun") == null && pursuerT != null && route != null)
        {
            Transform kr = Group(life, "KeyRun");
            KeyRunSetPiece piece = kr.gameObject.AddComponent<KeyRunSetPiece>();
            piece.floor = 5;
            SerializedObject so = new SerializedObject(piece);
            so.FindProperty("pursuer").objectReferenceValue = pursuerT.GetComponent<HubStalker>();
            SerializedProperty arr = so.FindProperty("route");
            arr.arraySize = route.childCount;
            for (int i = 0; i < route.childCount; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = route.GetChild(i);
            so.FindProperty("key").objectReferenceValue = key != null ? key.GetComponent<HubKey>() : null;
            so.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine($"KeyRun wired ({route.childCount} waypoints)");
        }

        Transform echoRoute = life.Find("EchoRoute") ?? BuildEchoRoute(hub, life, report);
        if (life.Find("StanEcho") == null && echoRoute != null) BuildStanEcho(hub, life, echoRoute, report);
        AddG5(hub, life, pursuerT, report);
        AddG6(hub, life, report);
        AddG7(hub, life, report);
    }

    // ---- the pursuer

    private static Material FindMaterial(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Material"))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && m.name == name) return m;
        }
        return null;
    }

    private static Transform BuildPursuer(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        // The pursuer is Timmy (SwapPursuerBody); it used to be the Adam Character Pack's Adam, removed for licence reasons.
        Transform holder = Group(life, "Pursuer");
        holder.gameObject.AddComponent<HubStalker>(); // SwapPursuerBody wires its animator
        NavMeshAgent agent = holder.gameObject.AddComponent<NavMeshAgent>();
        int type = HubNavMesh.FindAgentType();
        if (type != -1) agent.agentTypeID = type;
        agent.radius = 0.3f;
        agent.height = 1.8f;
        agent.enabled = false; // the prefab is saved with it off; HubStalker.Place enables it on the navmesh
        report.AppendLine(SwapPursuerBody(holder, PursuerRobotPrefab));
        holder.gameObject.SetActive(false);
        report.AppendLine("Pursuer built (Timmy, dulled, red eyes)");
        return holder;
    }

    // ---- the patrol route

    private static bool SampleHub(Vector3 p, float radius, out Vector3 onMesh)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = HubNavMesh.FindAgentType(), areaMask = NavMesh.AllAreas };
        bool ok = NavMesh.SamplePosition(p, out NavMeshHit hit, radius, filter);
        onMesh = hit.position;
        return ok;
    }

    private static Transform BuildPatrolRoute(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        Transform group = Group(life, "PatrolRoute");
        WingDoor director = hub.wingDoors.Length > 4 ? hub.wingDoors[4] : null;
        WingDoor surgery = hub.wingDoors.Length > 3 ? hub.wingDoors[3] : null;
        Bounds records = RoomBounds(_building, "OfficeA", 0f);
        List<Vector3> entrances = RoomEntrances("OfficeA");
        Vector3 recordsDoor = entrances.Count > 0 ? entrances.OrderBy(e => Vector3.Distance(e, hub.mainEntrance.position)).First() : records.center;
        recordsDoor.y = records.min.y + 0.4f;
        float g = hub.mainEntrance.position.y, u = director != null ? director.arrivalPoint.position.y : 8.82f;
        Vector3[] wanted =
        {
            director != null ? director.arrivalPoint.position : new Vector3(109f, u, -62f),
            new Vector3(110f, u, -54.5f),
            new Vector3(109.8f, 7.4f, -47.3f),   // right-hand main stairs, half way up
            new Vector3(104f, g, -54.5f),
            recordsDoor,
            new Vector3(96f, g, -54.5f),
            new Vector3(90.6f, 7.4f, -47.3f),    // left-hand main stairs
            new Vector3(90f, u, -54.5f),
            surgery != null ? surgery.arrivalPoint.position : new Vector3(99.5f, u, -62.2f),
        };
        string[] names = { "P0_DirectorDoor", "P1_UpperHallEast", "P2_StairsRight", "P3_GroundHall", "P4_RecordsDoor", "P5_GroundHallWest", "P6_StairsLeft", "P7_UpperHallWest", "P8_SurgeryDoor" };
        for (int i = 0; i < wanted.Length; i++)
        {
            if (!SampleHub(wanted[i], 3.5f, out Vector3 p))
            {
                Debug.LogWarning($"Asylum story layer: patrol waypoint {names[i]} near {wanted[i]} is not on the Hub navmesh; skipped.");
                continue;
            }
            Point(group, names[i], p, 0f);
        }

        // Every consecutive pair must have a complete Hub path.
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = HubNavMesh.FindAgentType(), areaMask = NavMesh.AllAreas };
        NavMeshPath path = new NavMeshPath();
        int bad = 0;
        for (int i = 0; i < group.childCount; i++)
        {
            Vector3 a = group.GetChild(i).position, b = group.GetChild((i + 1) % group.childCount).position;
            if (!(NavMesh.CalculatePath(a, b, filter, path) && path.status == NavMeshPathStatus.PathComplete)) { bad++; Debug.LogWarning($"Asylum story layer: patrol leg {i} has no complete path."); }
        }
        report.AppendLine($"PatrolRoute: {group.childCount} waypoints, {bad} broken legs");
        return group;
    }

    // ---- lockers

    private static bool FindLockerSpot(Vector3 centre, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.forward;
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        float bestD = float.MaxValue;
        bool found = false;
        for (float dx = -6f; dx <= 6f; dx += 1f)
        {
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.forward })
            {
                Vector3 from = centre + axis * dx + Vector3.up * 1.0f;
                foreach (Vector3 dir in dirs)
                {
                    if (!Physics.Raycast(from, dir, out RaycastHit hit, 4.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(hit.normal.y) > 0.1f || Vector3.Dot(hit.normal, -dir) < 0.96f) continue;
                    Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                    Quaternion rot = Quaternion.LookRotation(n);
                    if (!Physics.Raycast(new Vector3(hit.point.x, centre.y + 0.6f, hit.point.z) + n * 0.4f, Vector3.down, out RaycastHit floor, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float fy = floor.point.y;
                    // the locker volume and the standing space in front of its door must be empty
                    if (Physics.CheckBox(new Vector3(hit.point.x, fy + 1.05f, hit.point.z) + n * 0.4f, new Vector3(0.45f, 1.0f, 0.38f), rot, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.CheckBox(new Vector3(hit.point.x, fy + 1.0f, hit.point.z) + n * 1.3f, new Vector3(0.45f, 0.9f, 0.4f), rot, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float d = Vector3.Distance(from, centre);
                    if (d < bestD) { bestD = d; point = new Vector3(hit.point.x, floor.point.y, hit.point.z); normal = n; found = true; }
                }
            }
        }
        return found;
    }

    private static void BuildHubLockers(AsylumHub hub, Transform life, Transform route, System.Text.StringBuilder report)
    {
        FloorThemeSet themes = Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        FloorTheme ward = themes != null ? themes.ForFloor(1) : null;
        if (ward == null || ward.Locker == null)
        {
            Debug.LogError("Asylum story layer: no Floor Themes row with a locker - the hub lockers were not built. Run KEEP IT LIT > Build > Floor Themes first.");
            return;
        }
        Transform group = Group(life, "Lockers");
        float g = hub.mainEntrance.position.y;
        float u = hub.wingDoors.Length > 4 ? hub.wingDoors[4].arrivalPoint.position.y : 8.82f;
        (string name, Vector3 around)[] wanted =
        {
            ("Locker_Ground", new Vector3(104f, g, -55f)),
            ("Locker_Upper", new Vector3(106f, u, -55.5f)),
        };
        foreach ((string name, Vector3 around) in wanted)
        {
            if (!FindLockerSpot(around, out Vector3 point, out Vector3 normal))
            {
                Debug.LogError($"Asylum story layer: no free wall for {name} near {around}.");
                continue;
            }
            Locker locker = Object.Instantiate(ward.Locker, group);
            locker.gameObject.SetActive(true);
            locker.name = name;
            locker.transform.position = point;
            locker.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
            report.AppendLine($"{name} at {point} facing {normal}");
        }
    }

    // ---- the key

    private static Transform BuildDirectorKey(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        Transform board = life.Find(RecordsBoardName);
        if (kit == null || kit.KeyPickup == null || board == null)
        {
            Debug.LogError("Asylum story layer: the key template (KEEP IT LIT > Build > Interactables) or the records board is missing; the Director's key was not built.");
            return null;
        }
        KeyPickup clone = Object.Instantiate(kit.KeyPickup, life);
        clone.gameObject.SetActive(true);
        clone.name = "DirectorKey";
        Object.DestroyImmediate(clone);
        Transform t = life.Find("DirectorKey");
        t.position = board.position + board.up * 1.05f + board.forward * 0.06f + board.right * 0.0f;
        t.rotation = board.rotation;
        // The kit key hangs on a hook with its own bracket; keep the model, add a trigger the interactor can find.
        BoxCollider box = t.gameObject.GetComponent<BoxCollider>();
        if (box == null) box = t.gameObject.AddComponent<BoxCollider>();
        Bounds b = RendBounds(t.gameObject);
        box.isTrigger = true;
        box.center = t.InverseTransformPoint(b.center);
        box.size = Vector3.Max(b.size, new Vector3(0.3f, 0.3f, 0.3f));
        t.gameObject.AddComponent<HubKey>();
        t.gameObject.SetActive(false); // KeyRunSetPiece shows it on floor 5
        report.AppendLine($"DirectorKey at {t.position}");
        return t;
    }

    // ---- Stan's echo

    private static Transform BuildEchoRoute(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        Transform group = Group(life, "EchoRoute");
        float u = hub.wingDoors.Length > 4 ? hub.wingDoors[4].arrivalPoint.position.y : 8.82f;
        // The far end of the upper hall, four steps toward the main stairs.
        Vector3[] wanted = { new Vector3(126f, u, -54.5f), new Vector3(120f, u, -54.5f), new Vector3(114f, u, -54.5f), new Vector3(108f, u, -54.5f) };
        for (int i = 0; i < wanted.Length; i++)
        {
            if (SampleHub(wanted[i], 3f, out Vector3 p)) Point(group, "E" + i, p, 270f);
            else Debug.LogWarning($"Asylum story layer: echo point {i} near {wanted[i]} is off the Hub navmesh; skipped.");
        }
        report.AppendLine($"EchoRoute: {group.childCount} points");
        return group;
    }

    private static void BuildStanEcho(AsylumHub hub, Transform life, Transform route, System.Text.StringBuilder report)
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null || kit.Stalker == null || route.childCount < 2)
        {
            Debug.LogError("Asylum story layer: the Stalker template (KEEP IT LIT > Build > Interactables) is missing; Stan's echo was not built.");
            return;
        }
        Transform holder = Group(life, "StanEcho");
        Stalker clone = Object.Instantiate(kit.Stalker, holder);
        clone.gameObject.SetActive(true);
        clone.name = "Body";
        // Visuals only: the Animator stays (frozen by StanEcho), everything that thinks, moves, talks or collides goes.
        foreach (Component c in clone.GetComponentsInChildren<Component>(true).ToArray())
        {
            if (c == null || c is Transform || c is Renderer || c is MeshFilter || c is Animator || c is SkinnedMeshRenderer) continue;
            if (c is Stalker || c is NavMeshAgent || c is Collider || c is AudioSource || c is Light || c is MonoBehaviour || c is Rigidbody) Object.DestroyImmediate(c);
        }
        clone = null;
        Transform body = holder.Find("Body");
        body.gameObject.SetActive(false);

        StanEcho echo = holder.gameObject.AddComponent<StanEcho>();
        echo.floor = 4;
        SerializedObject so = new SerializedObject(echo);
        so.FindProperty("body").objectReferenceValue = body;
        SerializedProperty arr = so.FindProperty("points");
        arr.arraySize = route.childCount;
        for (int i = 0; i < route.childCount; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = route.GetChild(i);
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine("StanEcho built from the Stalker template");
    }
}
