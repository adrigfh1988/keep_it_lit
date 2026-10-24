using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F93 G5: the set pieces between the wings - the lift (floor 2), the dark hall (floor 3, still called FloodSet) and the chase (floor 4). Added to the existing hub
/// by <see cref="AddG5"/> (called from AddStoryLayer), build-missing-only: each piece exists once its named child under AsylumHub/Life does.
/// The pack's lift car, shaft doors and stairs are probed by name and bounds, never by assumed pivots.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string PackModels = "Assets/Abandoned_Asylum/Models/";

    private static List<string> G5Missing(Transform life)
    {
        List<string> missing = new List<string>();
        foreach (string n in new[] { "LiftSet", "FloodSet", "ChaseSet", "LiftChaseSet" })
        {
            if (life == null || life.Find(n) == null) missing.Add("Life/" + n);
        }
        return missing;
    }

    private static void AddG5(AsylumHub hub, Transform life, Transform pursuer, System.Text.StringBuilder report)
    {
        Physics.SyncTransforms();
        if (life.Find("LiftSet") == null) BuildLiftSet(hub, life, report);
        if (life.Find("FloodSet") == null) BuildFloodSet(hub, life, report);
        if (life.Find("ChaseSet") == null && pursuer != null) BuildChaseSet(hub, life, pursuer, report);
        if (life.Find("LiftChaseSet") == null && pursuer != null) BuildLiftChaseSet(hub, life, pursuer, report);
    }

    private static Transform FindBuilding(string name) => _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    // ---------------------------------------------------------------- lift

    /// <summary>A wire-mesh panel model fitted to width x height (thin side along the local Z), seated on the floor at `position`.</summary>
    private static GameObject Panel(Transform parent, string name, Vector3 position, float yaw, float width, float height)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(PackModels + "Window_Barrier.fbx");
        if (model == null) { Debug.LogError("Asylum story layer: Window_Barrier.fbx is missing; the gate has no mesh."); return new GameObject(name); }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        go.name = name;
        StripPhysics(go);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        Bounds b = RendBounds(go);
        bool thinX = b.size.x < b.size.z;
        if (thinX) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        b = RendBounds(go);
        Vector3 scale = new Vector3(width / Mathf.Max(0.01f, b.size.x), height / Mathf.Max(0.01f, b.size.y), 1f);
        go.transform.localScale = Vector3.Scale(go.transform.localScale, scale);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
        b = RendBounds(go);
        go.transform.position += position - new Vector3(b.center.x, b.min.y, b.center.z);
        return go;
    }

    private static void BuildLiftSet(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        Transform car = FindBuilding("Elevator");
        Transform neon = FindBuilding("NeonLift");
        Transform upper = FindBuilding("Elevator_Door");
        Transform lower = FindBuilding("Elevator_Door (1)");
        if (car == null || upper == null || lower == null)
        {
            Debug.LogError("Asylum story layer: the pack lift (Elevator, Elevator_Door, Elevator_Door (1)) was not found; no lift set piece.");
            return;
        }

        Transform set = Group(life, "LiftSet");
        float groundY = hub.mainEntrance.position.y - 0.07f;

        // The closed shaft door at ground level: a clone of the upper-floor door.
        GameObject ground = Object.Instantiate(upper.gameObject, set);
        ground.name = "GroundShaftDoor";
        ground.transform.position = new Vector3(upper.position.x, groundY - 0.05f, upper.position.z);
        ground.transform.rotation = upper.rotation;
        ground.transform.localScale = upper.lossyScale;

        // The call point in front of it.
        GameObject call = new GameObject("CallPoint");
        call.transform.SetParent(set, false);
        call.transform.position = new Vector3(ground.transform.position.x, groundY + 1.1f, ground.transform.position.z - 0.65f);
        BoxCollider cb = call.AddComponent<BoxCollider>();
        cb.isTrigger = true;
        cb.size = new Vector3(1.8f, 2.3f, 1.3f);
        LiftPoint callPoint = call.AddComponent<LiftPoint>();
        callPoint.role = LiftPoint.Role.Call;

        // The point to force the stuck basement doors (inside the car, in front of them).
        GameObject pryGo = new GameObject("PryPoint");
        pryGo.transform.SetParent(set, false);
        pryGo.transform.position = new Vector3(lower.position.x, lower.position.y + 1.2f, lower.position.z - 0.55f);
        BoxCollider pb = pryGo.AddComponent<BoxCollider>();
        pb.isTrigger = true;
        pb.size = new Vector3(1.8f, 2.3f, 1.4f);
        LiftPoint pryPoint = pryGo.AddComponent<LiftPoint>();
        pryPoint.role = LiftPoint.Role.Pry;

        // The red emergency light inside the car (off; under Life so HubDecay leaves it alone; the piece moves it with the car).
        GameObject redGo = new GameObject("RedLight");
        redGo.transform.SetParent(set, false);
        redGo.transform.position = new Vector3(car.position.x, groundY + 2.1f, car.position.z + 0.2f);
        Light red = redGo.AddComponent<Light>();
        red.type = LightType.Point;
        red.color = new Color(1f, 0.08f, 0.04f);
        red.intensity = 1.8f;
        red.range = 5f;
        red.shadows = LightShadows.None;
        red.enabled = false;

        // The gate across the mouth of the basement stairs (floor 2) and its toppled variant (floor 3+).
        Vector3 mouth = new Vector3(117.35f, groundY, -56.7f);
        GameObject gate = new GameObject("Gate");
        gate.transform.SetParent(set, false);
        gate.transform.position = mouth;
        for (int i = 0; i < 3; i++) Panel(gate.transform, "Panel" + i, mouth + new Vector3(-0.85f + i * 0.85f, 0f, 0f), 0f, 0.86f, 2.1f);
        GameObject pipe = AssetDatabase.LoadAssetAtPath<GameObject>(PackModels + "Pipe.fbx");
        if (pipe != null)
        {
            foreach (float y in new[] { 0.55f, 1.55f })
            {
                GameObject bar = (GameObject)PrefabUtility.InstantiatePrefab(pipe, gate.transform);
                bar.name = "Bar";
                StripPhysics(bar);
                bar.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                Bounds pbnd = RendBounds(bar);
                bar.transform.localScale = new Vector3(1f, 2.6f / Mathf.Max(0.01f, pbnd.size.x), 1f) * 0.5f;
                bar.transform.localScale = new Vector3(bar.transform.localScale.x, bar.transform.localScale.y, bar.transform.localScale.z);
                pbnd = RendBounds(bar);
                bar.transform.position += new Vector3(mouth.x, groundY + y, mouth.z + 0.06f) - pbnd.center;
            }
        }
        BoxCollider gc = gate.AddComponent<BoxCollider>();
        gc.center = new Vector3(0f, 1.1f, 0f);
        gc.size = new Vector3(2.7f, 2.2f, 0.3f);
        LiftPoint gatePoint = gate.AddComponent<LiftPoint>();
        gatePoint.role = LiftPoint.Role.Gate;

        GameObject broken = new GameObject("GateBroken");
        broken.transform.SetParent(set, false);
        broken.transform.position = mouth;
        Panel(broken.transform, "Fallen", mouth + new Vector3(-0.2f, 0.02f, 0.55f), 20f, 0.86f, 2.1f).transform.rotation *= Quaternion.Euler(90f, 0f, 0f);
        Panel(broken.transform, "Leaning", mouth + new Vector3(1.0f, 0f, 0.1f), 0f, 0.86f, 2.1f).transform.rotation *= Quaternion.Euler(0f, 0f, 25f);
        // the fallen one: reseat on the floor after the tipping
        foreach (Transform t in broken.transform)
        {
            Bounds tb = RendBounds(t.gameObject);
            t.position += new Vector3(0f, groundY - tb.min.y + 0.01f, 0f);
        }

        LiftSetPiece piece = set.gameObject.AddComponent<LiftSetPiece>();
        piece.floor = 2;
        SerializedObject so = new SerializedObject(piece);
        so.FindProperty("car").objectReferenceValue = car;
        SerializedProperty parts = so.FindProperty("carParts");
        parts.arraySize = neon != null ? 1 : 0;
        if (neon != null) parts.GetArrayElementAtIndex(0).objectReferenceValue = neon;
        so.FindProperty("groundDoor").objectReferenceValue = ground.transform;
        so.FindProperty("basementDoor").objectReferenceValue = lower;
        so.FindProperty("gate").objectReferenceValue = gate;
        so.FindProperty("gateBroken").objectReferenceValue = broken;
        so.FindProperty("redLight").objectReferenceValue = red;
        so.FindProperty("callPoint").objectReferenceValue = callPoint;
        so.FindProperty("pryPoint").objectReferenceValue = pryPoint;
        so.ApplyModifiedPropertiesWithoutUndo();
        foreach (LiftPoint p in new[] { callPoint, pryPoint, gatePoint })
        {
            SerializedObject ps = new SerializedObject(p);
            ps.FindProperty("owner").objectReferenceValue = piece;
            ps.ApplyModifiedPropertiesWithoutUndo();
        }

        gate.SetActive(false);
        broken.SetActive(false);
        report.AppendLine($"LiftSet: car {car.position}, shaft door at ground {ground.transform.position}, gate at {mouth}");
    }

    // ---------------------------------------------------------------- flood (dry since 8 Oct 2026: only the hall zone is left)

    private static void BuildFloodSet(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        Bounds hall = RoomBounds(_building, "HallUnderground", 0f);
        if (hall.size == Vector3.zero) { Debug.LogError("Asylum story layer: HallUnderground was not found; no flood set piece."); return; }
        float floorY = FloorHeight("HallUnderground", hall);
        Transform set = Group(life, "FloodSet");

        // The zone: the hall's footprint, floor to head height.
        GameObject zoneGo = new GameObject("Zone");
        zoneGo.transform.SetParent(set, false);
        zoneGo.transform.position = new Vector3(hall.center.x, floorY + 1.0f, hall.center.z);
        BoxCollider zone = zoneGo.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(hall.size.x, 2.4f, hall.size.z);

        FloodSetPiece piece = set.gameObject.AddComponent<FloodSetPiece>();
        piece.floor = 3;
        SerializedObject so = new SerializedObject(piece);
        so.FindProperty("zone").objectReferenceValue = zone;
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine($"FloodSet: hall zone {hall.size.x:F1} x {hall.size.z:F1} m at floor y {floorY}");
    }

    // ---------------------------------------------------------------- chase

    private static void BuildChaseSet(AsylumHub hub, Transform life, Transform pursuer, System.Text.StringBuilder report)
    {
        Transform set = Group(life, "ChaseSet");
        float groundY = hub.mainEntrance.position.y;

        GameObject top = new GameObject("TopZone");
        top.transform.SetParent(set, false);
        top.transform.position = new Vector3(117.35f, groundY + 1.1f, -55.5f);
        BoxCollider tz = top.AddComponent<BoxCollider>();
        tz.isTrigger = true;
        tz.size = new Vector3(3.2f, 2.4f, 2.2f);

        Bounds surgery = RoomBounds(_building, "Surgery", 0f);
        GameObject end = new GameObject("EndZone");
        end.transform.SetParent(set, false);
        end.transform.position = new Vector3(surgery.center.x, surgery.min.y + 1.4f, surgery.center.z);
        BoxCollider ez = end.AddComponent<BoxCollider>();
        ez.isTrigger = true;
        ez.size = new Vector3(Mathf.Max(2f, surgery.size.x - 1.8f), 2.8f, Mathf.Max(2f, surgery.size.z - 1.6f));

        Vector3 riseWanted = new Vector3(117.2f, 1.2f, -64.6f);
        Vector3 rise = SampleHub(riseWanted, 3f, out Vector3 onMesh) ? onMesh : riseWanted;
        Transform riseSpot = Point(set, "RiseSpot", rise, 0f); // faces +Z, up the stairs

        ChaseSetPiece piece = set.gameObject.AddComponent<ChaseSetPiece>();
        piece.floor = 4;
        SerializedObject so = new SerializedObject(piece);
        so.FindProperty("pursuer").objectReferenceValue = pursuer.GetComponent<HubStalker>();
        so.FindProperty("riseSpot").objectReferenceValue = riseSpot;
        so.FindProperty("topZone").objectReferenceValue = tz;
        so.FindProperty("endZone").objectReferenceValue = ez;
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine($"ChaseSet: rise spot {rise}, top zone {top.transform.position}, surgery zone {end.transform.position}");
    }

    // ---------------------------------------------------------------- lift chase (F113)

    /// <summary>
    /// F113: floor 2's lift chase. The pursuer stands on the basement stairs BELOW the chained gate (the gate has a collider but no NavMeshObstacle,
    /// so an agent above it would walk straight through), the steps are heard at three points down the flight, and a caught player wakes at the
    /// lift exit facing the Boiler Room. Needs the LiftSet (built first) and the Pursuer.
    /// </summary>
    private static void BuildLiftChaseSet(AsylumHub hub, Transform life, Transform pursuer, System.Text.StringBuilder report)
    {
        Transform liftSet = life.Find("LiftSet");
        LiftSetPiece lift = liftSet != null ? liftSet.GetComponent<LiftSetPiece>() : null;
        if (lift == null) { Debug.LogError("Asylum story layer: LiftChaseSet needs Life/LiftSet; it is not built."); return; }

        Vector3 spawnWanted = new Vector3(117.2f, 3.2f, -60.0f);
        if (!SampleHub(spawnWanted, 1.5f, out Vector3 spawn)) { Debug.LogError($"Asylum story layer: LiftChaseSet: no Hub navmesh near the stair spawn {spawnWanted}."); return; }
        if (spawn.y >= 4.6f) { Debug.LogError($"Asylum story layer: LiftChaseSet: the stair spawn {spawn} is not below the chained gate (y must be < 4.6)."); return; }

        SerializedObject liftSo = new SerializedObject(lift);
        Transform car = liftSo.FindProperty("car").objectReferenceValue as Transform;
        Transform basementDoor = liftSo.FindProperty("basementDoor").objectReferenceValue as Transform;
        if (car == null || basementDoor == null) { Debug.LogError("Asylum story layer: LiftChaseSet: the lift has no car or basement door."); return; }

        Transform set = Group(life, "LiftChaseSet");
        Transform stairSpawn = Point(set, "StairSpawn", spawn, 180f); // faces -Z, down the stairs
        Transform[] sounds =
        {
            Point(set, "StairSound0", new Vector3(117.2f, 4.4f, -57.6f), 0f),
            Point(set, "StairSound1", new Vector3(117.2f, 3.0f, -60.8f), 0f),
            Point(set, "StairSound2", new Vector3(117.2f, 1.6f, -63.8f), 0f),
        };
        Vector3 wakeWanted = new Vector3(car.position.x, basementDoor.position.y + 0.1f, basementDoor.position.z - 1.7f); // LiftSetPiece.WalkOut's target
        Vector3 wake = SampleHub(wakeWanted, 1f, out Vector3 wakeOnMesh) ? wakeOnMesh : wakeWanted;
        Transform wakePoint = Point(set, "WakePoint", wake, 270f); // faces -X, west, toward the Boiler Room

        LiftChaseSetPiece piece = set.gameObject.AddComponent<LiftChaseSetPiece>();
        piece.floor = 2;
        SerializedObject so = new SerializedObject(piece);
        so.FindProperty("lift").objectReferenceValue = lift;
        so.FindProperty("pursuer").objectReferenceValue = pursuer.GetComponent<HubStalker>();
        so.FindProperty("stairSpawn").objectReferenceValue = stairSpawn;
        so.FindProperty("wakePoint").objectReferenceValue = wakePoint;
        SerializedProperty list = so.FindProperty("stairSounds");
        list.arraySize = sounds.Length;
        for (int i = 0; i < sounds.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = sounds[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine($"LiftChaseSet: stair spawn {spawn}, wake {wake}");
    }
}
