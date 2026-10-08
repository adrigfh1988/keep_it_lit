using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F93: the human Mixamo bodies. Copied from the user's library into Assets/SourceFiles/Characters/Mixamo (Humanoid, textures and materials
/// extracted next to the FBX, URP Lit, smoothness &lt;= 0.35, textures capped at 1024). Tomas is Pete, the pursuer is "Char in black clothes",
/// the Director's finale memory is the doctor. Hilde and Elias stay on Crowd_LOD0 until a body exists for them - every patient body goes
/// through <see cref="SwapPatientBody"/>, so the next patch swaps them with one call each.
/// </summary>
public static partial class AsylumHubBuilder
{
    public const string CharacterDir = "Assets/SourceFiles/Characters/Mixamo/";
    public const string TomasBodyFbx = CharacterDir + "Pete_Tomas.fbx";
    public const string PursuerBodyFbx = CharacterDir + "Char_Subject.fbx";
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
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PursuerBodyFbx) != null && pursuer != null && pursuer.Find(BodyName(PursuerBodyFbx)) == null) missing.Add("Pursuer body (Char)");
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
        if (pursuer != null && AssetDatabase.LoadAssetAtPath<GameObject>(PursuerBodyFbx) != null && pursuer.Find(BodyName(PursuerBodyFbx)) == null)
            report.AppendLine(SwapPursuerBody(pursuer, PursuerBodyFbx));

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

    private static string SwapPursuerBody(Transform holder, string fbxPath)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        Transform old = holder.Find("Body");
        GameObject nb = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
        nb.name = BodyName(fbxPath);
        nb.transform.localPosition = Vector3.zero;
        nb.transform.localRotation = Quaternion.identity;
        StripPhysics(nb);
        Animator animator = nb.GetComponent<Animator>();
        if (animator == null) animator = nb.AddComponent<Animator>();
        animator.runtimeAnimatorController = PursuerController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Darker than the figure Mixamo made: a shape in the dark with two red eyes.
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
                    string path = $"Assets/SourceFiles/Materials/Asylum/Subject_{mats[i].name}.mat";
                    copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (copy == null)
                    {
                        copy = new Material(mats[i]) { name = "Subject_" + mats[i].name };
                        copy.SetColor("_BaseColor", new Color(0.012f, 0.012f, 0.014f, 1f));
                        copy.SetFloat("_Smoothness", 0f);
                        copy.SetFloat("_SpecularHighlights", 0f);
                        copy.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                        copy.SetFloat("_EnvironmentReflections", 0f); // near-black albedo still reads grey under the torch through the sky reflection otherwise
                        copy.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                        copy.DisableKeyword("_EMISSION");
                        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
                        AssetDatabase.CreateAsset(copy, path);
                    }
                    copies[mats[i]] = copy;
                }
                mats[i] = copy;
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        bool holderWasActive = holder.gameObject.activeSelf;
        holder.gameObject.SetActive(true); // Animator.Rebind/Update need an active object
        AddRedEyes(animator, nb);
        holder.gameObject.SetActive(holderWasActive);

        HubStalker stalker = holder.GetComponent<HubStalker>();
        SerializedObject so = new SerializedObject(stalker);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (old != null) Object.DestroyImmediate(old.gameObject);
        return "Pursuer: body swapped to " + System.IO.Path.GetFileName(fbxPath) + " (darkened), red eye beads on the head bone";
    }

    private static void AddRedEyes(Animator animator, GameObject body)
    {
        Material eyes = FindMaterial("Intake_Eyes");
        // Mixamo "Standing Up" FBXs rest in a lying pose: measure in the avatar's own T-pose, then give the controller back.
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        animator.runtimeAnimatorController = null;
        animator.Rebind();
        animator.Update(0f);
        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null || eyes == null) { animator.runtimeAnimatorController = controller; return; }
        foreach (float side in new[] { -1f, 1f })
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere); // an eye bead, not decor
            eye.name = side < 0f ? "EyeL" : "EyeR";
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.transform.SetParent(head, false);
            eye.GetComponent<Renderer>().sharedMaterial = eyes;
            eye.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            eye.transform.position = head.position + body.transform.forward * 0.125f + body.transform.up * 0.075f + body.transform.right * (0.04f * side);
            Vector3 parent = head.lossyScale;
            eye.transform.localScale = new Vector3(0.03f / parent.x, 0.03f / parent.y, 0.03f / parent.z);
        }
        animator.runtimeAnimatorController = controller;
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
