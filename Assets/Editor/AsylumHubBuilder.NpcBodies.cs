using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The hub's people are Timmy robots: the six NPCs, the patients Hilde and Elias and the sheeted body in the morgue (the Adam Character
/// Pack they were built from was removed from the project for licence reasons, 11 Oct 2026). Each body is the Animator node of a stripped
/// PlayerRobot.prefab (<see cref="InteractableKitBuilder.InstantiateRobotNode"/>), in its own painted skin (HunterSkinBuilder.Npcs.cs).
/// Only the body changes: the holder, its HubNpc / HubPatient, anchors, colliders, voice and mission links stay. Build-missing-only, never
/// regenerates the hub. Tomas (Pete) and the finale Director (the Mixamo doctor) keep their human bodies.
/// </summary>
public static partial class AsylumHubBuilder
{
    /// <summary>The name of a robot NPC / patient body; the Problem checks key on it.</summary>
    public const string NpcRobotBodyName = "Body_Timmy";

    /// <summary>What still has a non-robot body (null = all six are robots, or no hub / no NPCs yet - the F98 piece reports that).</summary>
    public static string NpcRobotProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null) return null;
        List<string> missing = new List<string>();
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            if (npc.body == null || npc.body.name != NpcRobotBodyName) missing.Add(npc.name);
        }
        return missing.Count == 0 ? null : "not a robot yet: " + string.Join(", ", missing);
    }

    /// <summary>ProjectSetup's fix: swap every non-robot NPC body in the scene hub and apply it to the prefab. Marks the scene dirty.</summary>
    public static void NpcRobotRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null) { Debug.LogError("Asylum NPC robots: the hub has no Life/Npcs (run Build Missing Pieces for the F98 NPCs first)."); return; }

        System.Text.StringBuilder report = new System.Text.StringBuilder();
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            if (npc.body != null && npc.body.name == NpcRobotBodyName) continue;
            report.AppendLine(SwapNpcBody(npc));
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum NPC robots:\n" + report.ToString().Trim());
    }

    /// <summary>
    /// Replaces the NPC's body with Timmy in its own skin. The robot's Animator node (whose forward is the mesh's visual facing) becomes
    /// the body itself - HubNpc reads the Animator straight off <see cref="HubNpc.body"/> - and takes over the old body's position,
    /// rotation and scale, so each NPC still stands, sits or lies where it did, facing the same way.
    /// </summary>
    public static string SwapNpcBody(HubNpc npc)
    {
        Material skin = HunterSkinBuilder.EnsureNpcSkin(npc.who);
        if (skin == null) return $"{npc.name}: no skin was painted";

        Transform old = npc.body;
        Transform robot = RobotBodyLike(npc.transform, old, skin);
        if (robot == null) return $"{npc.name}: {PursuerRobotPrefab} is missing or has no Animator";

        npc.body = robot;
        npc.controller = PeopleController();
        if (old != null) Object.DestroyImmediate(old.gameObject);
        EditorUtility.SetDirty(npc);
        return $"{npc.name}: body swapped to Timmy in the {skin.name} skin";
    }

    /// <summary>
    /// Timmy in <paramref name="skin"/> under <paramref name="holder"/>, named <see cref="NpcRobotBodyName"/>, on the HubPeople controller,
    /// taking <paramref name="old"/>'s position, rotation, uniform scale and active state (the holder's own pose when there is no old body).
    /// </summary>
    private static Transform RobotBodyLike(Transform holder, Transform old, Material skin)
    {
        Transform robot = InteractableKitBuilder.InstantiateRobotNode(holder);
        if (robot == null) return null;
        robot.name = NpcRobotBodyName;
        robot.SetPositionAndRotation(old != null ? old.position : holder.position, old != null ? old.rotation : holder.rotation);
        robot.localScale = Vector3.one * (old != null ? old.localScale.x : 1f);
        StripPhysics(robot.gameObject);
        robot.GetComponent<Animator>().runtimeAnimatorController = PeopleController();
        foreach (Renderer r in robot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = skin;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        robot.gameObject.SetActive(old == null || old.gameObject.activeSelf);
        return robot;
    }

    // ---------------------------------------------------------------- patients and the morgue body

    private const string MorgueRobotName = "Body_Sheeted_Timmy";
    private static readonly string[] RobotPatients = { "Hilde", "Elias" };

    private static HubPatient FindPatient(Transform life, string who)
    {
        Transform patients = life != null ? life.Find(PatientsName) : null;
        Transform t = patients != null ? patients.Find(who) : null;
        return t != null ? t.GetComponent<HubPatient>() : null;
    }

    /// <summary>ProjectSetup: the patients Hilde and Elias and the morgue's sheeted body are Timmy robots (null = done, or no hub).</summary>
    public static string PatientRobotProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.life == null) return null;
        List<string> missing = new List<string>();
        foreach (string who in RobotPatients)
        {
            HubPatient p = FindPatient(hub.life, who);
            if (p != null && (p.body == null || p.body.name != NpcRobotBodyName)) missing.Add(who);
        }
        Transform morgue = hub.life.Find(EliasMorgueName);
        if (morgue != null && morgue.Find(MorgueRobotName) == null) missing.Add("the morgue body");
        return missing.Count == 0 ? null : "not a robot yet: " + string.Join(", ", missing);
    }

    /// <summary>ProjectSetup's fix: swaps what <see cref="PatientRobotProblem"/> lists and applies the hub to its prefab. Marks the scene dirty.</summary>
    public static void PatientRobotRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.life == null) { Debug.LogError("Asylum robot patients: no hub with a Life group in the scene."); return; }
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true); // renderer bounds (the gurney) need active objects
        Physics.SyncTransforms();
        foreach (string who in RobotPatients)
        {
            HubPatient p = FindPatient(hub.life, who);
            if (p != null && (p.body == null || p.body.name != NpcRobotBodyName)) report.AppendLine(SwapPatientToRobot(p));
        }
        Transform morgue = hub.life.Find(EliasMorgueName);
        if (morgue != null && morgue.Find(MorgueRobotName) == null) report.AppendLine(SwapMorgueBody(morgue));
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        if (!wasActive) hub.SetContentActive(false);
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum robot patients:\n" + report.ToString().Trim());
    }

    /// <summary>A patient's body becomes Timmy in that patient's skin, on the HubPeople controller (it has the Lie state), with the fitted capsule the human body had.</summary>
    public static string SwapPatientToRobot(HubPatient patient)
    {
        Material skin = HunterSkinBuilder.EnsureNpcSkin(patient.name);
        if (skin == null) return $"{patient.name}: no skin was painted";
        Transform old = patient.body;
        Transform robot = RobotBodyLike(patient.transform, old, skin);
        if (robot == null) return $"{patient.name}: {PursuerRobotPrefab} is missing or has no Animator";
        AddPatientCapsule(robot.gameObject);

        patient.body = robot;
        patient.controller = PeopleController();
        if (old != null) Object.DestroyImmediate(old.gameObject);
        EditorUtility.SetDirty(patient);
        patient.Refresh();
        return $"{patient.name}: body swapped to Timmy in the {skin.name} skin";
    }

    private static void AddPatientCapsule(GameObject body)
    {
        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.radius = 0.3f;
        capsule.height = 1.8f;
    }

    /// <summary>
    /// The sheeted body on the morgue gurney becomes Timmy under the sheet: the old body's pose (lying on its back along the gurney), every
    /// renderer in the sheet material, centred on the gurney. The old body is deleted.
    /// </summary>
    private static string SwapMorgueBody(Transform morgue)
    {
        Transform old = morgue.Find("Body_Sheeted");
        Transform robot = BuildMorgueRobot(morgue, old != null ? old.position : morgue.position, old != null ? old.rotation : morgue.rotation);
        if (robot == null) return "EliasMorgue: the robot could not be built";
        if (old != null) Object.DestroyImmediate(old.gameObject);
        return "EliasMorgue: the sheeted body is Timmy";
    }

    private static Transform BuildMorgueRobot(Transform morgue, Vector3 position, Quaternion rotation)
    {
        bool morgueWasActive = morgue.gameObject.activeSelf;
        morgue.gameObject.SetActive(true); // the fit below needs live renderer bounds
        Transform robot = InteractableKitBuilder.InstantiateRobotNode(morgue);
        if (robot == null) { morgue.gameObject.SetActive(morgueWasActive); return null; }
        robot.name = MorgueRobotName;
        robot.SetPositionAndRotation(position, rotation);
        robot.localScale = Vector3.one;
        StripPhysics(robot.gameObject);
        robot.GetComponent<Animator>().runtimeAnimatorController = IdleController(); // arms at the sides under the sheet
        Material sheet = AssetDatabase.LoadAssetAtPath<Material>("Assets/SourceFiles/Materials/Asylum/Asylum_Sheet.mat");
        foreach (Renderer r in robot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = sheet;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        FitOnGurney(robot);
        morgue.gameObject.SetActive(morgueWasActive);
        return robot;
    }

    /// <summary>Centres the lying body on the morgue gurney (its renderer bounds over the gurney's, resting on its top). No gurney = left as placed.</summary>
    private static void FitOnGurney(Transform robot)
    {
        if (_building == null) return;
        Renderer bed = NearestRenderer("MorgueWheelBed", new Vector3(126.4f, 1.6f, -52.6f), 2.0f);
        if (bed == null) return;
        Bounds bb = bed.bounds, rb = default;
        bool any = false;
        foreach (Renderer r in robot.GetComponentsInChildren<Renderer>(true)) { if (!any) { rb = r.bounds; any = true; } else rb.Encapsulate(r.bounds); }
        if (!any) return;
        robot.position += new Vector3(bb.center.x - rb.center.x, bb.max.y + 0.02f - rb.min.y, bb.center.z - rb.center.z);
    }
}
