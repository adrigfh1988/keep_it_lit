using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F93 (E12): the asylum story layer, added to the EXISTING hub non-destructively. The user hand-edited Assets/Prefabs/AsylumHub.prefab
/// (commit 6fd32f9), so the hub is never regenerated for this: <see cref="AddStoryLayer"/> only adds each piece when its named child is
/// absent (Navigation, Life/RecordsBoard, Life/Patients/Hilde|Tomas|Elias, Life/EliasMorgue) to the scene instance, and
/// <see cref="StoryRepair"/> then applies the instance to the prefab with PrefabUtility.ApplyPrefabInstance (additions only).
/// Everything is positioned by probing the existing hub (renderer bounds, capsule checks, the walk routes), never by assumed pivots.
/// Later phases (G4-G7) add their own pieces to the same function.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string LifeName = "Life";
    private const string PatientsName = "Patients";
    private const string RecordsBoardName = "RecordsBoard";
    private const string EliasMorgueName = "EliasMorgue";

    private const string CrowdModelPath = "Assets/UnityTechnologies/Adam Character Pack/Adam/Crowd_LOD0.FBX";
    private const string IdleClipFbx = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx";
    private const string PatientFolder = "Assets/SourceFiles/Materials/Asylum/Patients";
    private const string PatientControllerPath = "Assets/SourceFiles/Animation/Patients/Patient_Idle.controller";
    private const string MorgueNotebook = "Assets/Morgue Room PBR/Prefabs/props/notebook.prefab";
    private const string PackBoard = "Assets/Morgue Room PBR/Prefabs/props/writing_board.prefab";
    private const string PackPaper = "Assets/Abandoned_Asylum/Prefabs/Paper.prefab";

    // ---------------------------------------------------------------- ProjectSetup entry points

    /// <summary>The story pieces that are missing from the hub in the scene (null = complete, or no hub at all).</summary>
    public static string StoryProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null; // the "Asylum hub" piece reports that
        List<string> missing = StoryMissing(hub);
        return missing.Count == 0 ? null : "missing: " + string.Join(", ", missing);
    }

    private static List<string> StoryMissing(AsylumHub hub)
    {
        List<string> missing = new List<string>();
        if (hub.transform.Find(HubNavMesh.NavigationName) == null) missing.Add("Navigation (hub NavMesh)");
        Transform life = hub.transform.Find(LifeName);
        if (life == null || hub.life != life) missing.Add(LifeName);
        foreach (string patient in new[] { "Hilde", "Tomas", "Elias" })
        {
            if (life == null || life.Find($"{PatientsName}/{patient}") == null) missing.Add($"Life/Patients/{patient}");
        }
        if (life == null || life.Find(RecordsBoardName) == null) missing.Add("Life/" + RecordsBoardName);
        if (life == null || life.Find(EliasMorgueName) == null) missing.Add("Life/" + EliasMorgueName);
        missing.AddRange(G4Missing(hub, life));
        missing.AddRange(G5Missing(life));
        missing.AddRange(G6Missing(life));
        missing.AddRange(G7Missing(life));
        missing.AddRange(G8Missing(hub));
        return missing;
    }

    /// <summary>ProjectSetup's fix: add what is missing to the scene hub and apply the additions to the prefab. Mark the scene dirty; the caller saves.</summary>
    public static void StoryRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum story layer: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        AddStoryLayer(hub);
        ApplyHubToPrefab(hub);
    }

    /// <summary>Applies the scene instance to Assets/Prefabs/AsylumHub.prefab (additions and the story fields only; never regenerates).</summary>
    public static void ApplyHubToPrefab(AsylumHub hub)
    {
        if (!PrefabUtility.IsPartOfPrefabInstance(hub.gameObject)) { Debug.LogWarning("Asylum story layer: the hub is not a prefab instance, nothing to apply."); return; }
        PrefabUtility.ApplyPrefabInstance(hub.gameObject, InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // ---------------------------------------------------------------- the layer

    /// <summary>Build-missing-only. Returns a report. Needs the hub content active (it switches it on and leaves it as the prefab is saved).</summary>
    public static string AddStoryLayer(AsylumHub hub)
    {
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        EnsureFolderPath(PatientFolder);

        Transform life = hub.transform.Find(LifeName);
        if (life == null) life = Group(hub.transform, LifeName);
        hub.life = life;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hub); // a direct field write on a prefab instance is otherwise lost at the next domain reload

        Transform patients = life.Find(PatientsName) ?? Group(life, PatientsName);
        _routes = RoutePaths(hub);

        Transform hilde = patients.Find("Hilde"), tomas = patients.Find("Tomas"), elias = patients.Find("Elias");
        if (hilde == null) report.AppendLine(BuildHilde(hub, patients));
        if (tomas == null) report.AppendLine(BuildTomas(hub, patients));
        if (elias == null) report.AppendLine(BuildElias(hub, patients));
        if (life.Find(EliasMorgueName) == null) report.AppendLine(BuildEliasMorgue(hub, life));
        if (life.Find(RecordsBoardName) == null) report.AppendLine(BuildRecordsBoard(hub, life));
        WireElias(hub, life);
        foreach (HubPatient settled in patients.GetComponentsInChildren<HubPatient>(true)) settled.Refresh(); // bodies rest at their floor-1 anchors in the saved prefab

        int before = life.childCount;
        AddG4(hub, life, report);
        AddG8(hub, report);
        _rebakeNeeded = before != life.childCount;
        // The hub NavMesh last: it is baked from the colliders in place. Skipped when it already exists.
        if (hub.transform.Find(HubNavMesh.NavigationName) == null) report.AppendLine("navmesh: " + HubNavMesh.Bake(hub));
        else if (_rebakeNeeded) report.AppendLine("navmesh rebake: " + HubNavMesh.Bake(hub));

        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        string text = report.ToString().Trim();
        Debug.Log("Asylum story layer: " + (text.Length == 0 ? "nothing to add." : "\n" + text));
        return text;
    }

    private static List<Vector3[]> _routes = new List<Vector3[]>();
    private static bool _rebakeNeeded;

    // ---------------------------------------------------------------- patients

    private static string BuildHilde(AsylumHub hub, Transform patients)
    {
        Bounds day = RoomBounds(_building, "DayRoom", 0f);
        float floorY = FloorHeight("DayRoom", day);
        // At the middle east window, looking out of it.
        Vector3 window = new Vector3(129.2f, floorY, -72.7f);
        Transform win = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Window_DayRoom (1)");
        if (win != null && win.GetComponent<Renderer>() != null) { Vector3 c = win.GetComponent<Renderer>().bounds.center; window = new Vector3(c.x, floorY, c.z); }

        GameObject holder = NewPatient(patients, "Hilde", HubState.Patient.Hilde, new Color(0.74f, 0.79f, 0.84f), out HubPatient patient);
        if (!FreeSpotNear(window, 0.9f, 2.6f, floorY, window, out Vector3 spot)) { Debug.LogError("Asylum story layer: no free spot for Hilde at the day room window."); spot = window - new Vector3(1.4f, 0f, 0f); }
        patient.defaultAnchor = Anchor(holder.transform, "Anchor_Default", spot, YawTo(spot, window));
        patient.finaleAnchor = FinaleAnchor(holder.transform, hub, new Vector3(96.4f, 0f, -58.0f), 0);
        return $"Hilde at {spot} (window {window})";
    }

    private static string BuildTomas(AsylumHub hub, Transform patients)
    {
        // Near the Boiler Room door, facing the nearest pipe: the player passes him on the way into the basement hall.
        Bounds machine = RoomBounds(_building, "MachineRoom", 0f);
        float floorY = FloorHeight("MachineRoom", machine);
        WingDoor boiler = hub.wingDoors.Length > 1 ? hub.wingDoors[1] : null;
        Vector3 reference = boiler != null && boiler.arrivalPoint != null ? boiler.arrivalPoint.position : machine.center;

        Transform best = null;
        float bestScore = float.MaxValue;
        foreach (Transform t in hub.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Pipe") || t.GetComponent<Renderer>() == null) continue;
            Vector3 c = t.GetComponent<Renderer>().bounds.center;
            if (!machine.Contains(new Vector3(c.x, machine.center.y, c.z))) continue;
            float score = Vector3.Distance(new Vector3(c.x, 0f, c.z), new Vector3(reference.x, 0f, reference.z));
            if (score < 2.5f) score += 20f; // not right on top of the arrival point
            if (score < bestScore) { bestScore = score; best = t; }
        }
        Vector3 pipe = best != null ? best.GetComponent<Renderer>().bounds.center : machine.center;
        pipe.y = floorY;

        GameObject holder = NewPatient(patients, "Tomas", HubState.Patient.Tomas, new Color(0.62f, 0.61f, 0.56f), out HubPatient patient);
        if (!FreeSpotNear(pipe, 0.75f, 2.4f, floorY, pipe, out Vector3 spot)) { Debug.LogError("Asylum story layer: no free spot for Tomas at the pipes."); spot = pipe + new Vector3(1.0f, 0f, 0f); }
        patient.defaultAnchor = Anchor(holder.transform, "Anchor_Default", spot, YawTo(spot, pipe));
        patient.finaleAnchor = FinaleAnchor(holder.transform, hub, new Vector3(103.6f, 0f, -58.0f), 1);
        return $"Tomas at {spot} facing {(best != null ? best.name : "?")} at {pipe}";
    }

    private static string BuildElias(AsylumHub hub, Transform patients)
    {
        Bounds bedroom = RoomBounds(_building, "Bedroom", 0f);
        float floorY = FloorHeight("Bedroom", bedroom);
        // The bed in the west row nearest the south-west corner: its mattress and pillow tell which end is the head.
        Renderer mattress = NearestRenderer("Mattress", new Vector3(74.4f, 9.3f, -78.0f), 0.9f);
        Renderer pillow = NearestRenderer("Pillow", new Vector3(73.6f, 9.4f, -77.9f), 0.9f);
        GameObject holder = NewPatient(patients, "Elias", HubState.Patient.Elias, new Color(0.84f, 0.82f, 0.78f), out HubPatient patient);
        if (mattress == null)
        {
            Debug.LogError("Asylum story layer: no bed found for Elias in the Bedroom.");
            return "Elias: no bed";
        }

        Bounds mb = mattress.bounds;
        bool alongX = mb.size.x >= mb.size.z;
        Vector3 headDir = alongX ? new Vector3(Mathf.Sign((pillow != null ? pillow.bounds.center.x : mb.center.x - 1f) - mb.center.x), 0f, 0f)
                                 : new Vector3(0f, 0f, Mathf.Sign((pillow != null ? pillow.bounds.center.z : mb.center.z - 1f) - mb.center.z));
        float length = alongX ? mb.size.x : mb.size.z;
        // Feet near the far end, so the 1.8 m body ends at the pillow. The lying pose points the model's up along -forward.
        Vector3 feet = new Vector3(mb.center.x, 0f, mb.center.z) - headDir * (Mathf.Min(length * 0.5f - 0.1f, 0.95f));
        feet.y = mb.max.y + 0.13f;
        Quaternion lie = Quaternion.LookRotation(-headDir, Vector3.up);
        patient.bedAnchor = Anchor(holder.transform, "Anchor_Bed", feet, lie.eulerAngles.y);

        // Floor 3: at the foot of the bed, facing the south wall.
        Vector3 foot = new Vector3(mb.center.x, floorY, mb.center.z) - headDir * (length * 0.5f + 0.6f);
        Vector3 wall = new Vector3(foot.x, floorY, bedroom.min.z);
        if (!FreeSpotNear(foot, 0.0f, 1.6f, floorY, wall, out Vector3 stand)) { Debug.LogError("Asylum story layer: no free spot at the foot of Elias's bed."); stand = foot; }
        patient.standAnchor = Anchor(holder.transform, "Anchor_Stand", stand, YawTo(stand, wall));
        patient.finaleAnchor = FinaleAnchor(holder.transform, hub, new Vector3(100f, 0f, -55.6f), 2);
        return $"Elias bed anchor {feet} (mattress {mb.center}, head {headDir}); stand anchor {stand}";
    }

    /// <summary>Elias from floor 4: a sheeted body on the morgue's gurney, a wristband tag on it, and the parts of drawer five.</summary>
    private static string BuildEliasMorgue(AsylumHub hub, Transform life)
    {
        Transform group = Group(life, EliasMorgueName);
        GameObject crowd = AssetDatabase.LoadAssetAtPath<GameObject>(CrowdModelPath);
        Renderer bed = NearestRenderer("MorgueWheelBed", new Vector3(126.4f, 1.6f, -52.6f), 2.0f);
        if (crowd == null || bed == null)
        {
            Debug.LogError($"Asylum story layer: the morgue gurney or {CrowdModelPath} is missing; Elias's body was not built.");
            return "EliasMorgue: skipped";
        }

        Bounds bb = bed.bounds;
        bool alongZ = bb.size.z >= bb.size.x;
        Vector3 along = alongZ ? Vector3.forward : Vector3.right;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(crowd, group);
        body.name = "Body_Sheeted";
        StripPhysics(body);
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "RootMotion") { r.enabled = false; continue; }
            Material sheet = AssetDatabase.LoadAssetAtPath<Material>("Assets/SourceFiles/Materials/Asylum/Asylum_Sheet.mat");
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = sheet;
            r.sharedMaterials = mats;
        }
        Animator anim = body.GetComponent<Animator>();
        if (anim != null) { anim.runtimeAnimatorController = IdleController(); anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; } // idle pose: arms at the sides under the sheet
        // Lying on its back, head toward -along, thickened so it reads as a body under a sheet.
        Vector3 feetPos = new Vector3(bb.center.x, bb.max.y + 0.13f, bb.center.z) + along * 0.85f;
        body.transform.rotation = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);
        body.transform.position = feetPos;
        body.transform.localScale = new Vector3(1.3f, 1f, 1.9f);

        // Wristband tag: a pack paper sheet with a name on it, lying on the sheet by the chest.
        GameObject tag = new GameObject("Wristband");
        tag.transform.SetParent(group, true);
        Vector3 chest = new Vector3(bb.center.x, bb.max.y + 0.3f, bb.center.z) - along * 0.1f + (alongZ ? Vector3.right : Vector3.forward) * 0.25f;
        tag.transform.position = chest;
        GameObject paper = AssetDatabase.LoadAssetAtPath<GameObject>(PackPaper) != null ? (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PackPaper), tag.transform) : null;
        if (paper != null)
        {
            StripPhysics(paper);
            paper.transform.rotation = Quaternion.identity;
            paper.transform.localScale *= 1.6f;
            Bounds pb = RendBounds(paper);
            paper.transform.position += tag.transform.position - new Vector3(pb.center.x, pb.min.y, pb.center.z);
        }
        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(tag.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        textGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        TextMeshPro tmp = textGo.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.fontSize = 0.7f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.12f, 0.08f, 0.06f);
        tmp.rectTransform.sizeDelta = new Vector2(0.4f, 0.4f);
        tmp.text = "ELIAS VOGT\nPATIENT 5";
        BoxCollider bc = tag.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 0.05f, 0f);
        bc.size = new Vector3(0.45f, 0.35f, 0.5f);
        bc.isTrigger = true; // the interactor spherecasts in Collide mode; nothing should ever stop the player on it
        tag.AddComponent<PatientWristband>();

        group.gameObject.SetActive(false);
        return $"EliasMorgue on the gurney at {bb.center} (long axis {(alongZ ? "z" : "x")})";
    }

    /// <summary>Wires the morgue group and drawer five onto Elias's HubPatient.</summary>
    private static void WireElias(AsylumHub hub, Transform life)
    {
        Transform elias = life.Find($"{PatientsName}/Elias");
        if (elias == null) return;
        HubPatient patient = elias.GetComponent<HubPatient>();
        Transform morgue = life.Find(EliasMorgueName);
        if (patient.morgueGroup == null) patient.morgueGroup = morgue;
        if (patient.drawerParts != null && patient.drawerParts.Length > 0 && patient.drawerParts[0] != null) return;

        // Drawer five of the pack's two MorgueBox cabinets (4 trays each): the first tray of the second cabinet, with its front.
        // (pack coordinates in world space with the hub at (100, 5, -50): cabinet 2 stands at x 128.1, its upper-left tray and front at x 127.6)
        Transform tray = NearestTransform("MorgueBox_Bed", new Vector3(127.6f, 3.0f, -48.1f), 0.45f);
        Transform front = NearestTransform("MorgueBox_Door", new Vector3(127.5f, 3.3f, -49.5f), 0.5f);
        if (tray == null || front == null) Debug.LogWarning("Asylum story layer: drawer five of the morgue was not found (the pack cabinet was moved?); the drawer stays shut.");
        List<Transform> parts = new List<Transform>();
        if (tray != null) parts.Add(tray);
        if (front != null) parts.Add(front);
        patient.drawerParts = parts.ToArray();
        EditorUtility.SetDirty(patient);
    }

    /// <summary>A patient holder: HubPatient + PatientVoice, the Crowd body on the idle controller in a gown, a fitted capsule.</summary>
    private static GameObject NewPatient(Transform patients, string name, HubState.Patient who, Color gownTint, out HubPatient patient)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(patients, false);
        patient = holder.AddComponent<HubPatient>();
        patient.patient = who;
        holder.AddComponent<PatientVoice>();

        GameObject crowd = AssetDatabase.LoadAssetAtPath<GameObject>(CrowdModelPath);
        if (crowd == null)
        {
            Debug.LogError($"Asylum story layer: {CrowdModelPath} is missing, patient {name} has no body.");
            return holder;
        }
        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(crowd, holder.transform);
        body.name = "Body";
        StripPhysics(body);
        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        animator.runtimeAnimatorController = IdleController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Material gown = PatientMaterial("Patient_Gown_" + name, "Crowd_LOD0_Mat_Torso", gownTint, 0.2f);
        Material skin = PatientMaterial("Patient_Skin_" + name, "Crowd_LOD0_Mat_Limbs", new Color(0.74f, 0.7f, 0.66f), 0.15f);
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "RootMotion") { r.enabled = false; continue; }
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mats[i] != null && mats[i].name.Contains("Torso") ? gown : skin;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.radius = 0.3f;
        capsule.height = 1.8f;
        patient.body = body.transform;
        return holder;
    }

    private static Material PatientMaterial(string name, string sourceName, Color tint, float smoothness)
    {
        string path = $"{PatientFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Material source = null;
        foreach (string guid in AssetDatabase.FindAssets(sourceName + " t:Material", new[] { "Assets/UnityTechnologies" }))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && m.name == sourceName) { source = m; break; }
        }
        Material copy = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        copy.name = name;
        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", tint);
        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", smoothness);
        if (copy.HasProperty("_Metallic")) copy.SetFloat("_Metallic", 0f);
        // A plain hospital gown / bare limbs: the Crowd texture is a sci-fi vest, so the textures are dropped and only the tint stays.
        foreach (string tex in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_ParallaxMap", "_DetailAlbedoMap", "_DetailNormalMap" })
        {
            if (copy.HasProperty(tex)) copy.SetTexture(tex, null);
        }
        copy.DisableKeyword("_EMISSION");
        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    /// <summary>One idle state on the project's humanoid Stand--Idle clip (created once).</summary>
    private static RuntimeAnimatorController IdleController()
    {
        AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(PatientControllerPath);
        if (existing != null) return existing;
        EnsureFolderPath(System.IO.Path.GetDirectoryName(PatientControllerPath).Replace('\\', '/'));
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(IdleClipFbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(PatientControllerPath);
        AnimatorState state = controller.layers[0].stateMachine.AddState("Idle");
        state.motion = clip;
        controller.layers[0].stateMachine.defaultState = state;
        if (clip == null) Debug.LogWarning($"Asylum story layer: no animation clip found in {IdleClipFbx}; the patients will stand in a bind pose.");
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static void StripPhysics(GameObject go)
    {
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
    }

    // ---------------------------------------------------------------- the records board

    private static string BuildRecordsBoard(AsylumHub hub, Transform life)
    {
        Bounds room = RoomBounds(_building, "OfficeA", 0f);
        float floorY = FloorHeight("OfficeA", room);
        if (!FindBoardSpot(room, floorY, RoomEntrances("OfficeA"), 1.9f, out Vector3 point, out Vector3 normal))
        {
            Debug.LogError($"Asylum story layer: no free wall in OfficeA for the records board (not flat {_bsFlat}, too narrow {_bsWide}, blocked box {_bsBox}, blocked front {_bsFront}).");
            return "RecordsBoard: no free wall";
        }

        GameObject root = new GameObject(RecordsBoardName);
        root.transform.SetParent(life, false);
        root.transform.position = point;
        root.transform.rotation = Quaternion.LookRotation(normal, Vector3.up); // +Z into the room
        RecordsBoard board = root.AddComponent<RecordsBoard>();

        // Backing: the Morgue pack's writing board (2.03 x 1.03 m, 3 cm thick at identity rotation), hung on the wall.
        GameObject boardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackBoard);
        float width = 1.9f, boardHeight = 1.05f;
        if (boardPrefab != null)
        {
            GameObject backing = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, root.transform);
            backing.name = "Backing";
            StripPhysics(backing);
            backing.transform.rotation = root.transform.rotation;
            backing.transform.localScale *= 0.95f;
            Bounds b = RendBounds(backing);
            Vector3 target = point + Vector3.up * (0.95f + b.extents.y) + normal * (b.extents.z + 0.01f);
            backing.transform.position += target - b.center;
            width = b.size.x > b.size.z ? b.size.x : b.size.z;
            boardHeight = b.size.y;
        }

        // Header.
        GameObject headerGo = new GameObject("Header");
        headerGo.transform.SetParent(root.transform, false);
        headerGo.transform.localPosition = new Vector3(0f, 0.95f + boardHeight - 0.1f, 0.05f);
        headerGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        TextMeshPro header = headerGo.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) header.font = TMP_Settings.defaultFontAsset;
        header.fontSize = 1.9f;
        header.characterSpacing = 4f;
        header.alignment = TextAlignmentOptions.Center;
        header.color = new Color(0.9f, 0.86f, 0.78f);
        header.rectTransform.sizeDelta = new Vector2(width, 0.3f);
        header.text = "CASE FILES  0 / 5";
        board.header = header;

        // Five folders, hidden until their file is read.
        GameObject notebook = AssetDatabase.LoadAssetAtPath<GameObject>(MorgueNotebook);
        Material tint = AssetDatabase.LoadAssetAtPath<Material>("Assets/SourceFiles/Materials/Interactables/Interactable_PatientFileBook.mat");
        board.slots = new RecordsSlot[PatientFiles.MazeFileCount];
        float pitch = (width - 0.3f) / PatientFiles.MazeFileCount;
        for (int i = 0; i < PatientFiles.MazeFileCount; i++)
        {
            GameObject slot = new GameObject($"Slot_{i}");
            slot.transform.SetParent(root.transform, false);
            slot.transform.localPosition = new Vector3((width - 0.3f) * 0.5f - pitch * (i + 0.5f), 0.95f + boardHeight * 0.5f, 0.07f); // local +X is the viewer's left
            RecordsSlot rs = slot.AddComponent<RecordsSlot>();
            rs.slotIndex = i;
            BoxCollider box = slot.AddComponent<BoxCollider>();
            box.size = new Vector3(pitch * 0.9f, 0.5f, 0.3f);
            box.isTrigger = true;
            if (notebook != null)
            {
                GameObject folder = (GameObject)PrefabUtility.InstantiatePrefab(notebook, slot.transform);
                folder.name = "Folder";
                StripPhysics(folder);
                folder.transform.localScale *= 0.7f;
                folder.transform.rotation = root.transform.rotation * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 0f);
                // Flat cover-up lies in XZ; tipped -90 about X it faces the room (+Z of the board).
                folder.transform.rotation = root.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
                if (tint != null) foreach (Renderer r in folder.GetComponentsInChildren<Renderer>(true)) { Material[] m = new Material[r.sharedMaterials.Length]; for (int k = 0; k < m.Length; k++) m[k] = tint; r.sharedMaterials = m; }
                Bounds fb = RendBounds(folder);
                folder.transform.position += slot.transform.position - fb.center;
                rs.folder = folder;
            }
            // Name under the folder.
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(slot.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, -0.27f, 0.0f);
            labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMeshPro label = labelGo.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 0.34f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.85f, 0.8f, 0.7f);
            label.rectTransform.sizeDelta = new Vector2(pitch * 0.95f, 0.3f);
            // S9: builders always write English (the localized sign refreshes it at runtime)
            label.text = (L10n.TIn("en", "lore.file." + (PatientFiles.FirstId + i) + ".label") ?? PatientFiles.Label(PatientFiles.FirstId + i)).Replace(" · ", "\n");
            board.slots[i] = rs;
        }
        return $"RecordsBoard on the OfficeA wall at {point}, facing {normal}, {width:F1} m wide";
    }

    /// <summary>The widest free flat stretch of wall in the room (like FindWallSpot, but a 2.3 x 1.4 x 0.6 m volume in front has to be empty of everything).</summary>
    private static int _bsFlat, _bsWide, _bsBox, _bsFront;
    private static bool FindBoardSpot(Bounds room, float floorY, List<Vector3> entrances, float width, out Vector3 point, out Vector3 normal)
    {
        _bsFlat = _bsWide = _bsBox = _bsFront = 0;
        point = default;
        normal = Vector3.forward;
        bool found = false;
        float bestScore = float.NegativeInfinity;
        Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        Physics.SyncTransforms();
        foreach (Vector3 n in normals)
        {
            bool tangentIsX = Mathf.Abs(n.z) > 0.5f;
            Vector3 tangent = tangentIsX ? Vector3.right : Vector3.forward;
            float half = tangentIsX ? room.extents.x : room.extents.z;
            float depthHalf = tangentIsX ? room.extents.z : room.extents.x;
            for (float s = -half + width * 0.5f + 0.3f; s <= half - width * 0.5f - 0.3f; s += 0.2f)
            {
                Vector3 probe = new Vector3(room.center.x, floorY, room.center.z) + tangent * s;
                if (!FlatWall(probe, n, tangent, floorY, depthHalf + 1.0f, out Vector3 wall)) { _bsFlat++; continue; }
                // the whole width must be flat wall
                bool flat = true;
                foreach (float o in new[] { -width * 0.5f, width * 0.5f })
                {
                    if (!FlatWall(probe + tangent * o, n, tangent, floorY, depthHalf + 1.0f, out Vector3 edge) || Mathf.Abs(Vector3.Dot(edge - wall, n)) > 0.15f) { flat = false; break; }
                }
                if (!flat) { _bsWide++; continue; }
                Vector3 centre = wall + n * 0.25f;
                centre.y = floorY + 1.65f;
                Vector3 ext = tangentIsX ? new Vector3(width * 0.5f, 0.55f, 0.2f) : new Vector3(0.2f, 0.55f, width * 0.5f);
                if (Physics.CheckBox(centre, ext, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) { _bsBox++; continue; }
                // a free walking space in front too
                Vector3 front = wall + n * 1.2f;
                front.y = floorY + 1.0f;
                if (Physics.CheckBox(front, new Vector3(0.3f, 0.8f, 0.3f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) { _bsFront++; continue; }
                float nearest = entrances.Count == 0 ? 6f : entrances.Min(e => Vector3.Distance(new Vector3(e.x, 0f, e.z), new Vector3(wall.x, 0f, wall.z)));
                float score = Mathf.Min(nearest, 7f) - 0.1f * Mathf.Abs(s);
                if (found && score <= bestScore) continue;
                found = true;
                bestScore = score;
                point = new Vector3(wall.x, floorY, wall.z);
                normal = n;
            }
        }
        return found;
    }

    // ---------------------------------------------------------------- probing helpers

    private static Transform Anchor(Transform parent, string name, Vector3 world, float yaw)
    {
        Transform t = Group(parent, name);
        t.position = world;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        return t;
    }

    private static float YawTo(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    /// <summary>The finale memory spot: a free place near `around` (y found by probing the floor), facing the route's middle.</summary>
    private static Transform FinaleAnchor(Transform holder, AsylumHub hub, Vector3 around, int index)
    {
        float floorY = hub.mainEntrance != null ? hub.mainEntrance.position.y : 5f;
        around.y = floorY;
        Vector3 faceTo = new Vector3(100f, floorY, around.z);
        if (!FreeSpotNear(around, 0f, 2.2f, floorY, faceTo, out Vector3 spot)) { Debug.LogWarning($"Asylum story layer: no free finale spot near {around}."); spot = around; }
        return Anchor(holder, "Anchor_Finale", spot, YawTo(spot, faceTo));
    }

    /// <summary>
    /// The nearest free standing spot to `center` between rMin and rMax: floor within 0.2 m of floorY, room for a 0.32 m / 1.8 m capsule,
    /// at least 0.9 m from every walk route, and (if `lookAt` is given) a clear view of it (nothing solid within 0.6 m in front).
    /// </summary>
    private static bool FreeSpotNear(Vector3 center, float rMin, float rMax, float floorY, Vector3 lookAt, out Vector3 spot)
    {
        spot = default;
        Physics.SyncTransforms();
        for (float r = rMin; r <= rMax + 0.001f; r += 0.2f)
        {
            int steps = r < 0.01f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r * 14f));
            float bestDist = float.MaxValue;
            bool any = false;
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                Vector3 p = new Vector3(center.x + Mathf.Cos(a) * r, floorY, center.z + Mathf.Sin(a) * r);
                if (!Physics.Raycast(new Vector3(p.x, floorY + 0.6f, p.z), Vector3.down, out RaycastHit hit, 1.0f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - floorY) > 0.2f) continue;
                p.y = hit.point.y + 0.03f;
                if (Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.55f, 0.34f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (_routes.Any(route => DistanceToPath(p, route) < 0.9f)) continue;
                Vector3 look = (lookAt - p); look.y = 0f;
                if (look.sqrMagnitude > 0.01f && Physics.Raycast(p + Vector3.up * 1.0f, look.normalized, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                float d = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(lookAt.x, 0f, lookAt.z));
                if (d < bestDist) { bestDist = d; spot = p; any = true; }
            }
            if (any) return true;
        }
        return false;
    }

    private static Renderer NearestRenderer(string name, Vector3 around, float maxDistance)
    {
        Renderer best = null;
        float bestD = maxDistance;
        foreach (Renderer r in _building.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name != name && !r.name.StartsWith(name + " ")) continue;
            float d = Vector3.Distance(r.bounds.center, around);
            if (d < bestD) { bestD = d; best = r; }
        }
        return best;
    }

    private static Transform NearestTransform(string name, Vector3 around, float maxDistance)
    {
        Transform best = null;
        float bestD = maxDistance;
        foreach (Transform t in _building.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != name) continue;
            Renderer r = t.GetComponent<Renderer>();
            Vector3 c = r != null ? r.bounds.center : t.position;
            float d = Vector3.Distance(c, around);
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    private static Transform FindChildNamed(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }
}
