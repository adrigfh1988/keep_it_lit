using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the Elias ("The Frost" / Subject #5) template for <see cref="InteractableKit.Elias"/> (slot <c>elias</c>).
/// Uses a stripped <c>PlayerRobot.prefab</c> placeholder body in frost-bitten cyan/slate with emissive ice-blue eye sockets,
/// a subtle cyan frost point light (<c>EliasFrostLight</c>), and <c>Assets/SourceFiles/Animation/Descent/Elias.controller</c>.
/// </summary>
public static partial class InteractableKitBuilder
{
    private const string EliasControllerPath = ListenerAnimDir + "/Elias.controller";

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Elias (TimmyRobot Placeholder)", priority = 143)]
    public static void RebuildEliasTemplate()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null)
        {
            Build();
            return;
        }

        SerializedObject so = new SerializedObject(kit);
        SerializedProperty prop = so.FindProperty("elias");
        if (prop != null)
        {
            Component old = prop.objectReferenceValue as Component;
            prop.objectReferenceValue = null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Build();
    }

    private static Elias BuildEliasTemplate(Transform parent, Vector3 localPosition)
    {
        GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        if (robotPrefab == null)
        {
            Debug.LogError($"InteractableKitBuilder: {RobotPrefabPath} not found - the Elias template was skipped.");
            return null;
        }

        Transform orphan = parent.Find("Elias");
        if (orphan != null) Object.DestroyImmediate(orphan.gameObject);

        GameObject root = new GameObject("Elias");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = Quaternion.identity;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab, root.transform);
        body.name = "Body_Elias";
        body.tag = "Untagged";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = new Vector3(1.06f, 1.12f, 1.06f);

        StripRobotToVisuals(body);

        Material bodyMat = LoadOrCreateMat(
            "Elias_PlaceholderBody",
            new Color(0.14f, 0.22f, 0.28f),
            new Color(0.03f, 0.08f, 0.12f),
            0.22f);

        Material eyeMat = LoadOrCreateMat(
            "Elias_EyeGlow",
            new Color(0.55f, 0.88f, 1.0f),
            new Color(0.45f, 0.85f, 1.0f) * 2.4f,
            0.9f);

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string mName = mats[i] != null ? mats[i].name : "";
                mats[i] = mName.Contains("Eye") || r.name.Contains("Eye") ? eyeMat : bodyMat;
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        GameObject frostLightGo = new GameObject("EliasFrostLight");
        frostLightGo.transform.SetParent(root.transform, false);
        frostLightGo.transform.localPosition = new Vector3(0f, 1.35f, 0.15f);
        Light frostLight = frostLightGo.AddComponent<Light>();
        frostLight.type = LightType.Point;
        frostLight.color = new Color(0.35f, 0.78f, 1.0f);
        frostLight.range = 4.2f;
        frostLight.intensity = 0.35f;
        frostLight.shadows = LightShadows.None;

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        animator.runtimeAnimatorController = BuildOrLoadEliasController(robotPrefab);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Elias elias = root.AddComponent<Elias>();
        root.SetActive(false);
        return elias;
    }

    private static RuntimeAnimatorController BuildOrLoadEliasController(GameObject robotPrefab)
    {
        if (!AssetDatabase.IsValidFolder(ListenerAnimDir))
        {
            AssetDatabase.CreateFolder("Assets/SourceFiles/Animation", "Descent");
        }

        AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(EliasControllerPath);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(EliasControllerPath);
        }

        AnimationClip idleClip = LoadNightNurseClip(NightNurseIdlePath, "Idle");
        AnimationClip walkClip = LoadNightNurseClip(NightNurseWalkPath, "Walk_N");
        AnimationClip runClip = LoadNightNurseClip(NightNurseRunPath, "Run_N");

        if (idleClip == null || walkClip == null || runClip == null)
        {
            Animator robotAnim = robotPrefab != null ? robotPrefab.GetComponentInChildren<Animator>(true) : null;
            if (robotAnim != null && robotAnim.runtimeAnimatorController != null)
                return robotAnim.runtimeAnimatorController;
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(EliasControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Pose", AnimatorControllerParameterType.Int);
        controller.AddParameter("Reach", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState locomotionState = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        locomotionState.speedParameter = "MotionSpeed";
        locomotionState.speedParameterActive = true;
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(idleClip, 0f);
        tree.AddChild(walkClip, 2f);
        tree.AddChild(runClip, 6f);
        machine.defaultState = locomotionState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }
}
