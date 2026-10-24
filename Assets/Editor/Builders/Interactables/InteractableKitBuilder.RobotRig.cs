using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class InteractableKitBuilder
{
    /// <summary>True when the kit's Stalker template uses the dedicated sculpted Stan_Model.fbx (Stan_Body mesh).</summary>
    public static bool HasStanModelBody(Stalker stalker)
    {
        if (stalker == null) return false;
        return stalker.transform.Find("Stan_Body") != null;
    }

    // ---------------------------------------------------------------- F75 template (the Stalker)

    private const string StanModelPath = "Assets/SourceFiles/Models/Stan/Stan_Model.fbx";

    private static GameObject EnsureStanModel()
    {
        ModelImporter importer = AssetImporter.GetAtPath(StanModelPath) as ModelImporter;
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(StanModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(StanModelPath))
        {
            if (o is Avatar a) avatar = a;
        }
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"InteractableKitBuilder: {StanModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        return model;
    }

    private static Material LoadOrCreateStanMat(string name, Color color, Color? emission, float smoothness, float metallic)
    {
        Material mat = LoadOrCreateMat(name, color, emission, smoothness);
        if (mat != null)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", smoothness > 0.05f ? 1f : 0f);
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", smoothness > 0.05f ? 1f : 0f);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    /// <summary>
    /// Builds the Stalker (Stan) template from the dedicated sculpted Humanoid model (Assets/SourceFiles/Models/Stan/Stan_Model.fbx)
    /// when present, wired to PlayerRobot.prefab's locomotion AnimatorController and the 5 URP materials (Shell, Joint, Socket, Teeth,
    /// Stalker_Eyes). The body is pitch-black and matte (no white specular glare when lit); only the emissive white eyes and smile glow.
    /// Falls back to the blackened PlayerRobot only if Stan_Model.fbx is missing.
    /// </summary>
    private static Stalker BuildStalkerTemplate(Transform parent, Vector3 localPosition)
    {
        GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        GameObject stanModel = EnsureStanModel();
        if (stanModel != null)
        {
            GameObject stanRoot = (GameObject)PrefabUtility.InstantiatePrefab(stanModel, parent);
            PrefabUtility.UnpackPrefabInstance(stanRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            stanRoot.name = "Stalker";
            stanRoot.transform.SetParent(parent, false);
            stanRoot.transform.localPosition = localPosition;
            stanRoot.transform.localRotation = Quaternion.identity;
            stanRoot.transform.localScale = Vector3.one;

            DestroyAllOfType<Collider>(stanRoot);

            Animator anim = stanRoot.GetComponent<Animator>();
            if (anim == null) anim = stanRoot.AddComponent<Animator>();
            if (robotPrefab != null)
            {
                Animator robotAnim = robotPrefab.GetComponentInChildren<Animator>(true);
                if (robotAnim != null) anim.runtimeAnimatorController = robotAnim.runtimeAnimatorController;
            }
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Material shellMat = LoadOrCreateStanMat("Stan_Mat_Shell", new Color(0.14f, 0.145f, 0.155f), null, 0.25f, 0f);
            Material jointMat = LoadOrCreateStanMat("Stan_Mat_Joint", new Color(0.06f, 0.062f, 0.068f), null, 0.35f, 0f);
            Material socketMat = LoadOrCreateStanMat("Stan_Mat_Socket", new Color(0.02f, 0.02f, 0.025f), null, 0.10f, 0f);
            Material teethMat = LoadOrCreateStanMat("Stan_Mat_Teeth", Color.white, Color.white * 3.5f, 0.95f, 0.05f);
            Material eyeMat = LoadOrCreateMat("Stalker_Eyes", Color.white, Color.white * 4.5f, 0.95f);

            foreach (Renderer renderer in stanRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.Contains("Eye_Glow"))
                {
                    renderer.sharedMaterial = eyeMat;
                }
                else
                {
                    Material[] mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string mName = mats[i] != null ? mats[i].name : "";
                        if (mName.Contains("Socket")) mats[i] = socketMat;
                        else if (mName.Contains("Joint")) mats[i] = jointMat;
                        else if (mName.Contains("Teeth")) mats[i] = teethMat;
                        else mats[i] = shellMat;
                    }
                    renderer.sharedMaterials = mats;
                }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (renderer is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            Stalker stanStalker = stanRoot.AddComponent<Stalker>();
            StanPhraseBookBuilder.AssignTo(stanStalker);
            stanRoot.SetActive(false);
            return stanStalker;
        }

        if (robotPrefab == null)
        {
            Debug.LogError($"InteractableKitBuilder: {RobotPrefabPath} not found - the stalker template was skipped.");
            return null;
        }

        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab, parent);
        root.name = "Stalker";
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 1.15f;

        StripRobotToVisuals(root);

        Material bodyMat = LoadOrCreateMat("Stalker_Body", new Color(0.005f, 0.005f, 0.006f), null, 0f);
        if (bodyMat.HasProperty("_SpecularHighlights")) bodyMat.SetFloat("_SpecularHighlights", 0f);
        if (bodyMat.HasProperty("_EnvironmentReflections")) bodyMat.SetFloat("_EnvironmentReflections", 0f);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = bodyMat;
            renderer.sharedMaterials = mats;
        }

        BuildStalkerEyes(root);

        Stalker stalker = root.AddComponent<Stalker>();
        StanPhraseBookBuilder.AssignTo(stalker); // F82: keep the phrase-book link across a full rebuild (no-op if the asset doesn't exist yet)
        // An expensive skinned mesh + animator should not sit active (and animating/rendering) in the
        // gallery - MazeGenerator.BuildStalker's Instantiate + SetActive(true) is what brings a clone
        // to life at runtime, same pattern as CreateDoor.
        root.SetActive(false);
        return stalker;
    }

    /// <summary>
    /// Unpacks a PlayerRobot.prefab instance and strips everything that is not visual (movement/input, the player's stealth/stamina
    /// components, every camera/listener/CinemachineBrain/virtual camera), keeping the Animator, its controller and the skinned meshes.
    /// Shared by the Stalker template and the asylum hub's pursuer (AsylumHubBuilder.SwapPursuerBody).
    /// </summary>
    internal static void StripRobotToVisuals(GameObject root)
    {
        // Breaks the prefab connection so the strip below can freely remove components/children -
        // PlayerRobot.prefab itself is never modified.
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // Order matters: ThirdPersonController's [RequireComponent] on CharacterController/PlayerInput
        // would block removing those two first.
        DestroyAllOfType<StarterAssets.ThirdPersonController>(root);
        DestroyAllOfType<StarterAssets.StarterAssetsInputs>(root);
        DestroyAllOfType<FirstPersonRig>(root);
        DestroyAllOfType<PlayerStealthState>(root);
        DestroyAllOfType<PlayerStamina>(root);
        DestroyAllOfType<UnityEngine.InputSystem.PlayerInput>(root);
        DestroyAllOfType<CharacterController>(root);

        // The whole camera child goes, not just the Camera component - its MainCamera tag is exactly
        // what would give the scene a second MainCamera once this template is active.
        foreach (Camera camera in new List<Camera>(root.GetComponentsInChildren<Camera>(true)))
        {
            if (camera != null) Object.DestroyImmediate(camera.gameObject);
        }
        DestroyAllOfType<AudioListener>(root);
        DestroyAllOfType<Unity.Cinemachine.CinemachineBrain>(root);

        // The prefab also carries the third-person PlayerFollowCamera (a Cinemachine virtual camera, no
        // Camera component, so the loop above misses it). Every stalker would drag one along.
        foreach (Unity.Cinemachine.CinemachineCamera vcam in new List<Unity.Cinemachine.CinemachineCamera>(root.GetComponentsInChildren<Unity.Cinemachine.CinemachineCamera>(true)))
        {
            if (vcam != null) Object.DestroyImmediate(vcam.gameObject);
        }

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.tag = "Untagged";
        }
    }

    /// <summary>
    /// A stripped PlayerRobot.prefab reduced to its Animator node (the prefab's Robot child: Geometry + Skeleton, whose forward is the
    /// mesh's visual facing), parented to <paramref name="parent"/> at local zero with the wrapper deleted. Null when the prefab is missing.
    /// Shared by the hub NPCs, the patients, the morgue body and the Intake silhouette. The prefab asset itself is never touched.
    /// </summary>
    internal static Transform InstantiateRobotNode(Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        if (prefab == null) return null;
        GameObject wrapper = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        StripRobotToVisuals(wrapper);
        Animator animator = wrapper.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            Object.DestroyImmediate(wrapper);
            return null;
        }
        Transform robot = animator.transform;
        robot.SetParent(parent, false);
        Object.DestroyImmediate(wrapper);
        robot.localPosition = Vector3.zero;
        robot.localRotation = Quaternion.identity;
        robot.localScale = Vector3.one;
        foreach (Transform t in robot.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return robot;
    }

    private static void DestroyAllOfType<T>(GameObject root) where T : Component
    {
        foreach (T component in root.GetComponentsInChildren<T>(true))
        {
            if (component != null) Object.DestroyImmediate(component);
        }
    }

    /// <summary>
    /// Two tiny white emissive sphere eyes on the head bone - same bone lookup AIPresence.BuildEyes uses
    /// (a Humanoid Head mapping, else a "Head"-named node), and the same world-space placement +
    /// WorldToLocalScale technique (a bone can carry any scale in its own local frame; placing/sizing in
    /// bone-local space directly could make the eyes invisible or metres wide).
    /// </summary>
    private static void BuildStalkerEyes(GameObject root)
    {
        Animator animator = root.GetComponentInChildren<Animator>();
        Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) head = FindDeep(root.transform, "Head");
        if (head == null)
        {
            Debug.LogWarning("InteractableKitBuilder: no Head bone found on the robot rig - the stalker has no eyes.", root);
            return;
        }

        // The Animator node itself (not the eyes' own bone) sits 90 degrees off the root on this rig -
        // AIFollower.ComputeModelYawOffset's exact note - so the face direction comes from its forward.
        Vector3 forward = animator != null ? animator.transform.forward : root.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = root.transform.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        const float eyeForward = 0.09f;
        const float eyeUp = 0.02f;
        const float eyeSpacing = 0.045f;
        const float eyeWorldSize = 0.03f;

        Vector3 leftWorld = head.position + forward * eyeForward + Vector3.up * eyeUp - right * eyeSpacing;
        Vector3 rightWorld = head.position + forward * eyeForward + Vector3.up * eyeUp + right * eyeSpacing;

        Material eyeMat = LoadOrCreateMat("Stalker_Eyes", Color.white, Color.white * 3f, 0.1f);
        BuildEyeSphere(head, "LeftEye_Glow", leftWorld, eyeMat, eyeWorldSize);
        BuildEyeSphere(head, "RightEye_Glow", rightWorld, eyeMat, eyeWorldSize);
    }

    private static void BuildEyeSphere(Transform parent, string name, Vector3 worldPosition, Material material, float worldSize)
    {
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = name;
        Collider collider = eye.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
        eye.transform.SetPositionAndRotation(worldPosition, parent.rotation);
        eye.transform.SetParent(parent, true);
        eye.transform.localScale = WorldToLocalScale(parent, worldSize);
        eye.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>Local scale that gives a uniform world size of `worldSize` under `parent`, whatever the parent chain's scale is - same technique as AIPresence.WorldToLocalScale.</summary>
    private static Vector3 WorldToLocalScale(Transform parent, float worldSize)
    {
        Vector3 lossy = parent.lossyScale;
        return new Vector3(
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            worldSize / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }
}
