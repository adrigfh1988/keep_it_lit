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
    /// <summary>The name of Sister Agnes's custom 5-part 3D humanoid nurse body when <see cref="NurseAgnesModelPath"/> is present.</summary>
    public const string NurseAgnesBodyName = "Body_NurseAgnes";
    public const string NurseAgnesModelDir = "Assets/SourceFiles/Models/NurseAgnes";
    public const string NurseAgnesModelPath = NurseAgnesModelDir + "/NurseAgnes_Model.fbx";
    private const string NurseAgnesMatFolder = "Assets/SourceFiles/Materials/Asylum/Npcs";

    private static string ExpectedNpcBodyName(HubNpc npc) =>
        npc != null && npc.who == HubNpc.Who.Agnes && System.IO.File.Exists(NurseAgnesModelPath)
            ? NurseAgnesBodyName
            : NpcRobotBodyName;

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Sister Agnes (Nurse)", priority = 137)]
    public static void MenuSwapSisterAgnes()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null)
        {
            Debug.LogError("Sister Agnes: the hub has no Life/Npcs in the open scene.");
            return;
        }
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            if (npc.who != HubNpc.Who.Agnes) continue;
            string msg = SwapNpcBody(npc);
            if (Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include) != null)
            {
                InteractableKitBuilder.RebuildNightNurseTemplate();
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
            ApplyHubToPrefab(hub);
            Debug.Log(msg + " Save the scene (Ctrl+S) to keep it.", npc);
            return;
        }
    }

    /// <summary>What still has the wrong NPC body (null = all six match their expected body, or no hub / no NPCs yet - the F98 piece reports that).</summary>
    public static string NpcRobotProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null) return null;
        List<string> missing = new List<string>();
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            string expected = ExpectedNpcBodyName(npc);
            if (npc.body == null || npc.body.name != expected) missing.Add(npc.name);
        }
        return missing.Count == 0 ? null : "NPC body needs update: " + string.Join(", ", missing);
    }

    /// <summary>ProjectSetup's fix: swap every outdated NPC body in the scene hub and apply it to the prefab. Marks the scene dirty.</summary>
    public static void NpcRobotRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null) { Debug.LogError("Asylum NPC bodies: the hub has no Life/Npcs (run Build Missing Pieces for the F98 NPCs first)."); return; }

        System.Text.StringBuilder report = new System.Text.StringBuilder();
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            string expected = ExpectedNpcBodyName(npc);
            if (npc.body != null && npc.body.name == expected) continue;
            report.AppendLine(SwapNpcBody(npc));
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum NPC bodies:\n" + report.ToString().Trim());
    }

    /// <summary>
    /// Replaces the NPC's body with Sister Agnes's 5-part custom 3D humanoid nurse model (<see cref="NurseAgnesModelPath"/>) when
    /// <paramref name="npc"/> is <see cref="HubNpc.Who.Agnes"/>, or Timmy in its own skin otherwise.
    /// </summary>
    public static string SwapNpcBody(HubNpc npc)
    {
        if (npc.who == HubNpc.Who.Agnes && System.IO.File.Exists(NurseAgnesModelPath))
        {
            Transform oldAgnes = npc.body;
            Transform agnes = NurseAgnesBodyLike(npc.transform, oldAgnes);
            if (agnes != null)
            {
                npc.body = agnes;
                npc.controller = PeopleController();
                if (oldAgnes != null) Object.DestroyImmediate(oldAgnes.gameObject);
                EditorUtility.SetDirty(npc);
                return $"{npc.name}: body swapped to 5-part 3D NurseAgnes_Model.fbx (Body, Head, Hair, Hat, Apron + NurseAgnesPhysics)";
            }
        }

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

    private static GameObject EnsureNurseAgnesModel()
    {
        if (!System.IO.File.Exists(NurseAgnesModelPath)) return null;
        ModelImporter importer = AssetImporter.GetAtPath(NurseAgnesModelPath) as ModelImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(NurseAgnesModelPath, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(NurseAgnesModelPath) as ModelImporter;
        }
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            importer.importAnimation ||
            !importer.importBlendShapes)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importBlendShapes = true;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(NurseAgnesModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(NurseAgnesModelPath))
        {
            if (o is Avatar a) avatar = a;
        }
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"AsylumHubBuilder: {NurseAgnesModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        EnsureNurseAgnesMaterials();
        return model;
    }

    private static Material EnsureNursePbrMaterial(string matName, string matPath, string diffFile, string normFile, float smoothness, float metallic = 0f, bool doubleSided = false)
    {
        if (!string.IsNullOrEmpty(diffFile))
        {
            string diffPath = $"{NurseAgnesModelDir}/{diffFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath) == null && System.IO.File.Exists(diffPath))
                AssetDatabase.ImportAsset(diffPath, ImportAssetOptions.ForceUpdate);
        }
        if (!string.IsNullOrEmpty(normFile))
        {
            string normPath = $"{NurseAgnesModelDir}/{normFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(normPath) == null && System.IO.File.Exists(normPath))
                AssetDatabase.ImportAsset(normPath, ImportAssetOptions.ForceUpdate);
            TextureImporter normImp = AssetImporter.GetAtPath(normPath) as TextureImporter;
            if (normImp != null && normImp.textureType != TextureImporterType.NormalMap)
            {
                normImp.textureType = TextureImporterType.NormalMap;
                normImp.SaveAndReimport();
            }
        }

        Texture2D diff = !string.IsNullOrEmpty(diffFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{NurseAgnesModelDir}/{diffFile}") : null;
        Texture2D nrm  = !string.IsNullOrEmpty(normFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{NurseAgnesModelDir}/{normFile}") : null;

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = matName };
            AssetDatabase.CreateAsset(mat, matPath);
        }

        if (diff != null)
        {
            mat.SetTexture("_BaseMap", diff);
            mat.SetTexture("_MainTex", diff);
        }
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Cull", doubleSided ? 0f : 2f);
        if (nrm != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.0f);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void EnsureNurseAgnesMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials"))
            AssetDatabase.CreateFolder("Assets/SourceFiles", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials/Asylum"))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Asylum");
        if (!AssetDatabase.IsValidFolder(NurseAgnesMatFolder))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials/Asylum", "Npcs");

        EnsureNursePbrMaterial("NurseAgnes_Body",  $"{NurseAgnesMatFolder}/NurseAgnes_Body.mat",  "NurseAgnes_Body_Diffuse.png",  "NurseAgnes_Body_Normal.png",  0.22f, 0f, false);
        EnsureNursePbrMaterial("NurseAgnes_Head",  $"{NurseAgnesMatFolder}/NurseAgnes_Head.mat",  "NurseAgnes_Head_Diffuse.png",  "NurseAgnes_Head_Normal.png",  0.28f, 0f, false);
        EnsureNursePbrMaterial("NurseAgnes_Eyes",  $"{NurseAgnesMatFolder}/NurseAgnes_Eyes.mat",  "NurseAgnes_Eyes_Diffuse.png",  "NurseAgnes_Eyes_Normal.png",  0.92f, 0f, false);
        EnsureNursePbrMaterial("NurseAgnes_Hair",  $"{NurseAgnesMatFolder}/NurseAgnes_Hair.mat",  "NurseAgnes_Hair_Diffuse.png",  "NurseAgnes_Hair_Normal.png",  0.20f, 0f, true);
        EnsureNursePbrMaterial("NurseAgnes_Hat",   $"{NurseAgnesMatFolder}/NurseAgnes_Hat.mat",   "NurseAgnes_Hat_Diffuse.png",   "NurseAgnes_Hat_Normal.png",   0.18f, 0f, true);
        EnsureNursePbrMaterial("NurseAgnes_Apron", $"{NurseAgnesMatFolder}/NurseAgnes_Apron.mat", "NurseAgnes_Apron_Diffuse.png", "NurseAgnes_Apron_Normal.png", 0.16f, 0f, true);
        AssetDatabase.SaveAssets();
    }

    private static Transform NurseAgnesBodyLike(Transform holder, Transform old)
    {
        GameObject customModel = EnsureNurseAgnesModel();
        if (customModel == null) return null;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(customModel, holder);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = NurseAgnesBodyName;
        body.tag = "Untagged";
        body.transform.SetPositionAndRotation(old != null ? old.position : holder.position, old != null ? old.rotation : holder.rotation);
        body.transform.localScale = Vector3.one * (old != null ? old.localScale.x : 1f);

        Material bodyMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Body.mat");
        Material headMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Head.mat");
        Material eyesMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Eyes.mat");
        Material hairMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Hair.mat");
        Material hatMat   = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Hat.mat");
        Material apronMat = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Apron.mat");

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material targetMat = bodyMat;
            string rName = r.gameObject.name;
            if (rName.Contains("Head") && headMat != null) targetMat = headMat;
            else if (rName.Contains("Eyes") && eyesMat != null) targetMat = eyesMat;
            else if (rName.Contains("Hair") && hairMat != null) targetMat = hairMat;
            else if (rName.Contains("Hat") && hatMat != null) targetMat = hatMat;
            else if (rName.Contains("Apron") && apronMat != null) targetMat = apronMat;

            if (targetMat != null)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = targetMat;
                r.sharedMaterials = mats;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(NurseAgnesModelPath))
            {
                if (o is Avatar a) animator.avatar = a;
            }
        }
        animator.runtimeAnimatorController = PeopleController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (body.GetComponent<NurseAgnesPhysics>() == null)
        {
            body.AddComponent<NurseAgnesPhysics>();
        }

        body.SetActive(old == null || old.gameObject.activeSelf);
        return body.transform;
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
