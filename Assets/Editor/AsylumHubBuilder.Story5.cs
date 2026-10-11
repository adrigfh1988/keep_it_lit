using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F93: the human Mixamo bodies. Copied from the user's library into Assets/SourceFiles/Characters/Mixamo (Humanoid, textures and materials
/// extracted next to the FBX, URP Lit, smoothness &lt;= 0.35, textures capped at 1024). Tomas is Pete, the pursuer is Timmy the robot (not Mixamo),
/// the Director's finale memory is the doctor. Hilde and Elias are Timmy robots (AsylumHubBuilder.NpcBodies.cs); a human patient body goes
/// through <see cref="SwapPatientBody"/>, so the next patch swaps them with one call each.
/// </summary>
public static partial class AsylumHubBuilder
{
    public const string CharacterDir = "Assets/SourceFiles/Characters/Mixamo/";
    public const string TomasBodyFbx = CharacterDir + "Pete_Tomas.fbx";
    public const string PursuerRobotPrefab = "Assets/Prefabs/PlayerRobot.prefab";
    private const float PursuerRobotScale = 1.1f;
    private static readonly Color PursuerRobotTint = new Color(0.24f, 0.23f, 0.22f, 1f);
    public const string DirectorBodyFbx = CharacterDir + "Doctor_Director.fbx";

    private static string BodyName(string fbxPath) => "Body_" + System.IO.Path.GetFileNameWithoutExtension(fbxPath);

    private static List<string> G7Missing(Transform life)
    {
        List<string> missing = new List<string>();
        if (life == null) return missing;
        Transform tomas = life.Find($"{PatientsName}/Tomas");
        HubPatient t = tomas != null ? tomas.GetComponent<HubPatient>() : null;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(TomasBodyFbx) != null && t != null && (t.body == null || t.body.name != BodyName(TomasBodyFbx))) missing.Add("Tomas body (Pete)");
        Transform pursuer = life.Find("Pursuer");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PursuerRobotPrefab) != null && pursuer != null && pursuer.Find(BodyName(PursuerRobotPrefab)) == null) missing.Add("Pursuer body (Timmy robot)");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(DirectorBodyFbx) != null && life.Find("Finale") != null && life.Find("Finale/Director") == null) missing.Add("Finale/Director");
        return missing;
    }

    private static void AddG7(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        Transform tomas = life.Find($"{PatientsName}/Tomas");
        HubPatient tp = tomas != null ? tomas.GetComponent<HubPatient>() : null;
        if (tp != null && AssetDatabase.LoadAssetAtPath<GameObject>(TomasBodyFbx) != null && (tp.body == null || tp.body.name != BodyName(TomasBodyFbx)))
            report.AppendLine(SwapPatientBody(tp, TomasBodyFbx, new Color(0.74f, 0.71f, 0.66f)));

        Transform pursuer = life.Find("Pursuer");
        if (pursuer != null && AssetDatabase.LoadAssetAtPath<GameObject>(PursuerRobotPrefab) != null && pursuer.Find(BodyName(PursuerRobotPrefab)) == null)
            report.AppendLine(SwapPursuerBody(pursuer, PursuerRobotPrefab));

        Transform finale = life.Find("Finale");
        if (finale != null && finale.Find("Director") == null && AssetDatabase.LoadAssetAtPath<GameObject>(DirectorBodyFbx) != null)
            report.AppendLine(BuildDirectorMemory(hub, finale));
    }

    // ---------------------------------------------------------------- patients

    /// <summary>
    /// Replaces a patient's body with the humanoid model at `fbxPath`: the Animator gets the HubPeople controller, a fitted capsule goes on,
    /// the HubPatient points at it and the old body is deleted. Anchors, voice and poses are untouched (they are on the holder).
    /// `tint` (optional) multiplies the clothes' base colour in per-patient material copies, to dull a too-clean look.
    /// </summary>
    public static string SwapPatientBody(HubPatient patient, string fbxPath, Color? tint = null)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model == null) return $"SwapPatientBody: {fbxPath} not found";
        Transform old = patient.body;
        Vector3 pos = old != null ? old.position : patient.transform.position;
        Quaternion rot = old != null ? old.rotation : patient.transform.rotation;

        GameObject nb = (GameObject)PrefabUtility.InstantiatePrefab(model, patient.transform);
        nb.name = BodyName(fbxPath);
        nb.transform.SetPositionAndRotation(pos, rot);
        StripPhysics(nb);
        Animator animator = nb.GetComponent<Animator>();
        if (animator == null) animator = nb.AddComponent<Animator>();
        animator.runtimeAnimatorController = PeopleController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (tint.HasValue) TintBody(nb, tint.Value, patient.name);
        foreach (Renderer r in nb.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        CapsuleCollider capsule = nb.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.radius = 0.3f;
        capsule.height = 1.8f;

        patient.body = nb.transform;
        patient.controller = PeopleController();
        if (old != null) Object.DestroyImmediate(old.gameObject);
        EditorUtility.SetDirty(patient);
        patient.Refresh();
        return $"{patient.name}: body swapped to {System.IO.Path.GetFileName(fbxPath)}";
    }

    /// <summary>Per-patient copies of the body's materials with the base colour multiplied by `tint` (the shared character materials stay as they are).</summary>
    private static void TintBody(GameObject body, Color tint, string who)
    {
        string dir = PatientFolder;
        EnsureFolderPath(dir);
        Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (!copies.TryGetValue(mats[i], out Material copy))
                {
                    string path = $"{dir}/{who}_{mats[i].name}.mat";
                    copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (copy == null)
                    {
                        copy = new Material(mats[i]) { name = $"{who}_{mats[i].name}" };
                        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", copy.GetColor("_BaseColor") * tint);
                        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", Mathf.Min(0.2f, copy.GetFloat("_Smoothness")));
                        AssetDatabase.CreateAsset(copy, path);
                    }
                    copies[mats[i]] = copy;
                }
                mats[i] = copy;
            }
            r.sharedMaterials = mats;
        }
    }

    // ---------------------------------------------------------------- pursuer

    /// <summary>
    /// The pursuer is Timmy, the player's own robot (PlayerRobot.prefab, unpacked and stripped to its Animator and meshes like the maze
    /// Stalker), dulled, with two red eye beads, on its own Starter Assets locomotion controller (HubStalker drives Speed from the agent, so
    /// the feet match the ground). The body is turned so the mesh's visual facing (the Animator node, 90 degrees off the robot's root) is the
    /// holder's forward, which is what HubStalker steers. Every older Body* child is deleted.
    /// </summary>
    private static string SwapPursuerBody(Transform holder, string prefabPath)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (model == null) return $"SwapPursuerBody: {prefabPath} not found";
        List<GameObject> old = new List<GameObject>();
        foreach (Transform child in holder) if (child.name.StartsWith("Body")) old.Add(child.gameObject);

        bool holderWasActive = holder.gameObject.activeSelf;
        holder.gameObject.SetActive(true); // Animator.Rebind/Update need an active object
        GameObject nb = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
        InteractableKitBuilder.StripRobotToVisuals(nb);
        nb.name = BodyName(prefabPath);
        nb.transform.localPosition = Vector3.zero;
        nb.transform.localRotation = Quaternion.identity;
        nb.transform.localScale = Vector3.one * PursuerRobotScale;
        StripPhysics(nb);
        Animator animator = nb.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Object.DestroyImmediate(nb);
            holder.gameObject.SetActive(holderWasActive);
            return "SwapPursuerBody: the robot has no Animator";
        }
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Vector3 visual = Vector3.ProjectOnPlane(animator.transform.forward, Vector3.up);
        float nodeYaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(holder.forward, Vector3.up), visual, Vector3.up);
        nb.transform.localRotation = Quaternion.Euler(0f, -nodeYaw, 0f);

        // Timmy, but grimy and unlit: the colours dulled, nothing of his own glowing (only the red eyes do).
        EnsureFolderPath("Assets/SourceFiles/Materials/Asylum");
        Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
        foreach (Renderer r in nb.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (!copies.TryGetValue(mats[i], out Material copy))
                {
                    string path = $"Assets/SourceFiles/Materials/Asylum/Pursuer_{mats[i].name}.mat";
                    copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (copy == null)
                    {
                        copy = new Material(mats[i]) { name = "Pursuer_" + mats[i].name };
                        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", copy.GetColor("_BaseColor") * PursuerRobotTint);
                        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", Mathf.Min(0.3f, copy.GetFloat("_Smoothness")));
                        copy.DisableKeyword("_EMISSION");
                        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
                        AssetDatabase.CreateAsset(copy, path);
                    }
                    copies[mats[i]] = copy;
                }
                mats[i] = copy;
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        AddRedEyes(animator, 0.205f, 0.2f, 0.06f, 0.045f); // on the TV-screen face, which sits 0.2 m in front of the head joint
        holder.gameObject.SetActive(holderWasActive);

        HubStalker stalker = holder.GetComponent<HubStalker>();
        SerializedObject so = new SerializedObject(stalker);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();
        foreach (GameObject o in old) Object.DestroyImmediate(o);
        return "Pursuer: body swapped to Timmy (" + System.IO.Path.GetFileName(prefabPath) + ", dulled), red eye beads on the head bone";
    }

    /// <summary>Two red eye beads on the head bone, `forward`/`up` in front of and above the joint and `spacing` either side, `size` across (world metres).</summary>
    internal static void AddRedEyes(Animator animator, float forward, float up, float spacing, float size)
    {
        Material eyes = FindMaterial("Intake_Eyes");
        // Measure in the avatar's own T-pose, then give the controller back.
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        animator.runtimeAnimatorController = null;
        animator.Rebind();
        animator.Update(0f);
        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null || eyes == null) { animator.runtimeAnimatorController = controller; return; }
        Vector3 face = Vector3.ProjectOnPlane(animator.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, face);
        foreach (float side in new[] { -1f, 1f })
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere); // an eye bead, not decor
            eye.name = side < 0f ? "EyeL" : "EyeR";
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.transform.SetParent(head, false);
            eye.GetComponent<Renderer>().sharedMaterial = eyes;
            eye.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            eye.transform.position = head.position + face * forward + Vector3.up * up + right * (spacing * side);
            Vector3 parent = head.lossyScale;
            eye.transform.localScale = new Vector3(size / parent.x, size / parent.y, size / parent.z);
        }
        animator.runtimeAnimatorController = controller;
        animator.Rebind();
    }

    // ---------------------------------------------------------------- the Director, a finale memory

    private static string BuildDirectorMemory(AsylumHub hub, Transform finale)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(DirectorBodyFbx);
        Transform deskPoint = hub.points != null ? hub.points.Find("Director_Desk") : null;
        Vector3 deskPos = deskPoint != null ? deskPoint.position : hub.transform.position;
        float floorY = deskPos.y - 0.05f;
        // By the desk, on the side the player comes from, a few steps away from where they arrive.
        Vector3 around = new Vector3(106.6f, floorY, -58.6f);
        if (!FreeSpotNear(around, 0f, 2.4f, floorY, deskPos, out Vector3 spot)) { Debug.LogWarning("Asylum story layer: no free spot for the Director near his desk."); spot = around; }

        Transform holder = Group(finale, "Director");
        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
        body.name = BodyName(DirectorBodyFbx);
        StripPhysics(body);
        body.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, YawTo(spot, deskPos), 0f));
        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        animator.runtimeAnimatorController = PeopleController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        HubMemoryFigure figure = holder.gameObject.AddComponent<HubMemoryFigure>();
        SerializedObject so = new SerializedObject(figure);
        so.FindProperty("body").objectReferenceValue = body.transform;
        so.FindProperty("controller").objectReferenceValue = PeopleController();
        so.ApplyModifiedPropertiesWithoutUndo();
        body.SetActive(false);
        return $"Finale/Director: the doctor body stands at {spot}";
    }
}
