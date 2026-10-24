using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the Sister Agnes ("The Night Nurse") template for <see cref="InteractableKit.NightNurse"/> (slot <c>nightNurse</c>)
/// without modifying <c>NurseAgnes_Model.fbx</c> or its existing materials in <c>Assets/SourceFiles/Materials/Asylum/Npcs</c>.
/// Uses the same Starter Assets in-place humanoid locomotion BlendTree (<c>Idle</c> / <c>Walk_N</c> / <c>Run_N</c> driven by
/// <c>Speed</c> gait 0/1/2 and <c>MotionSpeed = planarSpeed / clipGroundSpeed</c>) and stance-speed measurement as
/// <see cref="HunterBodyBuilder"/> so her feet match her ground travel speed without sliding.
/// </summary>
public static partial class InteractableKitBuilder
{
    private const string ListenerModelPath = "Assets/SourceFiles/Characters/Mixamo/Char_Subject.fbx";
    private const string ListenerAnimDir = "Assets/SourceFiles/Animation/Descent";
    private const string MixamoClipDir = "Assets/SourceFiles/Animation/Mixamo/";

    private const string NightNurseModelPath = "Assets/SourceFiles/Models/NurseAgnes/NurseAgnes_Model.fbx";
    private const string NightNurseMatFolder = "Assets/SourceFiles/Materials/Asylum/Npcs";
    private const string NightNurseControllerPath = ListenerAnimDir + "/NightNurse.controller";

    private const string NightNurseStarterClipDir = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations";
    private const string NightNurseIdlePath = NightNurseStarterClipDir + "/Stand--Idle.anim.fbx";
    private const string NightNurseWalkPath = NightNurseStarterClipDir + "/Locomotion--Walk_N.anim.fbx";
    private const string NightNurseRunPath = NightNurseStarterClipDir + "/Locomotion--Run_N.anim.fbx";
    private const float DefaultNurseWalkSpeed = 1.89f;
    private const float DefaultNurseRunSpeed = 5.15f;

    private static readonly (string state, string clip)[] NightNursePoseStates =
    {
        ("Warn", "Look_Around"),
        ("Scream", "Zombie_Scream"),
        ("Pin", "Zombie_Attack"),
    };

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Night Nurse (Agnes)", priority = 152)]
    public static void RebuildNightNurseTemplate()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null)
        {
            Build();
            return;
        }

        SerializedObject so = new SerializedObject(kit);
        SerializedProperty prop = so.FindProperty("nightNurse");
        if (prop != null)
        {
            Component old = prop.objectReferenceValue as Component;
            prop.objectReferenceValue = null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Build();
    }

    private static NightNurse BuildNightNurseTemplate(Transform parent, Vector3 localPosition)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(NightNurseModelPath);
        if (model == null)
        {
            model = AssetDatabase.LoadAssetAtPath<GameObject>(ListenerModelPath);
        }
        if (model == null)
        {
            Debug.LogError($"InteractableKitBuilder: {NightNurseModelPath} not found - the NightNurse template was skipped.");
            return null;
        }

        AnimationClip idleClip = LoadNightNurseClip(NightNurseIdlePath, "Idle");
        AnimationClip walkClip = LoadNightNurseClip(NightNurseWalkPath, "Walk_N");
        AnimationClip runClip  = LoadNightNurseClip(NightNurseRunPath, "Run_N");
        float walkSpeed = MeasureNightNurseStanceSpeed(model, walkClip, DefaultNurseWalkSpeed);
        float runSpeed  = MeasureNightNurseStanceSpeed(model, runClip, DefaultNurseRunSpeed);

        GameObject root = new GameObject("NightNurse");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = "Body_NurseAgnes";
        body.tag = "Untagged";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;

        foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody rb in body.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Light l in body.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);

        Material bodyMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NightNurseMatFolder}/NurseAgnes_Body.mat");
        Material headMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NightNurseMatFolder}/NurseAgnes_Head.mat");
        Material eyesMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NightNurseMatFolder}/NurseAgnes_Eyes.mat");
        Material hairMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NightNurseMatFolder}/NurseAgnes_Hair.mat");
        Material hatMat   = AssetDatabase.LoadAssetAtPath<Material>($"{NightNurseMatFolder}/NurseAgnes_Hat.mat");
        Material apronMat = AssetDatabase.LoadAssetAtPath<Material>($"{NightNurseMatFolder}/NurseAgnes_Apron.mat");

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
            string avatarPath = AssetDatabase.GetAssetPath(model);
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(avatarPath).OfType<Avatar>().FirstOrDefault();
        }
        animator.runtimeAnimatorController = NightNurseController(idleClip, walkClip, runClip);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (body.GetComponent<NurseAgnesPhysics>() == null)
        {
            body.AddComponent<NurseAgnesPhysics>();
        }

        NightNurse nurse = root.AddComponent<NightNurse>();
        SerializedObject nurseSo = new SerializedObject(nurse);
        SerializedProperty walkProp = nurseSo.FindProperty("locomotionWalkSpeed");
        SerializedProperty runProp = nurseSo.FindProperty("locomotionRunSpeed");
        if (walkProp != null) walkProp.floatValue = walkSpeed;
        if (runProp != null) runProp.floatValue = runSpeed;

        SerializedProperty chaseProp = nurseSo.FindProperty("franticChaseClip");
        if (chaseProp != null)
        {
            chaseProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Horror Elements/Ambient/Amb_Run_2.wav");
        }

        nurseSo.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);
        return nurse;
    }

    private static AnimatorController NightNurseController(AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip)
    {
        if (!AssetDatabase.IsValidFolder(ListenerAnimDir))
        {
            AssetDatabase.CreateFolder("Assets/SourceFiles/Animation", "Descent");
        }
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(NightNurseControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(NightNurseControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(NightNurseControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Turn", AnimatorControllerParameterType.Float);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        if (idleClip == null) idleClip = LoadNightNurseMixamoClip("Zombie_Idle");
        if (walkClip == null) walkClip = LoadNightNurseMixamoClip("Walking");
        if (runClip == null)  runClip  = LoadNightNurseMixamoClip("Zombie_Run");

        AnimatorState locomotionState = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        locomotionState.speedParameter = "MotionSpeed";
        locomotionState.speedParameterActive = true;
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(idleClip, 0f);
        tree.AddChild(walkClip, 1f);
        tree.AddChild(runClip, 2f);
        machine.defaultState = locomotionState;

        AnimatorState patrolState = machine.AddState("Patrol");
        patrolState.motion = walkClip;
        patrolState.speedParameter = "MotionSpeed";
        patrolState.speedParameterActive = true;

        AnimatorState rushState = machine.AddState("Rush");
        rushState.motion = runClip;
        rushState.speedParameter = "MotionSpeed";
        rushState.speedParameterActive = true;

        foreach ((string state, string clip) in NightNursePoseStates)
        {
            AnimatorState added = machine.AddState(state);
            added.motion = LoadNightNurseMixamoClip(clip);
            if (added.motion == null) Debug.LogWarning($"InteractableKitBuilder: Mixamo clip {clip} not found - NightNurse state {state} is empty.");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimationClip LoadNightNurseClip(string path, string preferredName)
    {
        AnimationClip fallback = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
        {
            if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
            if (preferredName != null && clip.name == preferredName) return clip;
            if (fallback == null) fallback = clip;
        }
        return fallback;
    }

    private static AnimationClip LoadNightNurseMixamoClip(string clipName)
    {
        return AssetDatabase.LoadAllAssetsAtPath(MixamoClipDir + clipName + ".fbx")
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    /// <summary>
    /// Measures the ground speed (m/s) that <paramref name="clip"/>'s planted stance toes travel backward through
    /// <paramref name="modelPrefab"/>, using the exact same method as <see cref="HunterBodyBuilder"/>.
    /// </summary>
    private static float MeasureNightNurseStanceSpeed(GameObject modelPrefab, AnimationClip clip, float fallback)
    {
        if (modelPrefab == null || clip == null || clip.length < 0.1f) return fallback;
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        try
        {
            instance.transform.SetPositionAndRotation(new Vector3(0f, 1000f, 0f), Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            if (animator.avatar == null)
            {
                string avatarPath = AssetDatabase.GetAssetPath(modelPrefab);
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(avatarPath).OfType<Avatar>().FirstOrDefault();
            }
            if (!animator.isHuman) return fallback;

            Transform leftToe = animator.GetBoneTransform(HumanBodyBones.LeftToes) ?? animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightToe = animator.GetBoneTransform(HumanBodyBones.RightToes) ?? animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (leftToe == null || rightToe == null) return fallback;

            animator.runtimeAnimatorController = null;
            animator.Rebind();
            Vector3 forward = Vector3.ProjectOnPlane(animator.transform.forward, Vector3.up).normalized;

            const int frames = 120;
            float dt = clip.length / frames;
            float[,] along = new float[2, frames + 1];
            float[,] height = new float[2, frames + 1];
            float[] minHeight = { float.MaxValue, float.MaxValue };
            Transform[] toes = { leftToe, rightToe };
            for (int f = 0; f <= frames; f++)
            {
                clip.SampleAnimation(animator.gameObject, f * dt);
                for (int k = 0; k < 2; k++)
                {
                    along[k, f] = Vector3.Dot(toes[k].position, forward);
                    height[k, f] = toes[k].position.y;
                    minHeight[k] = Mathf.Min(minHeight[k], height[k, f]);
                }
            }

            List<float> speeds = new List<float>();
            for (int k = 0; k < 2; k++)
            {
                int start = -1;
                for (int f = 0; f <= frames + 1; f++)
                {
                    bool planted = f <= frames && height[k, f] - minHeight[k] < 0.01f;
                    if (planted && start < 0) start = f;
                    if (!planted && start >= 0)
                    {
                        int end = f - 1;
                        if (end - start >= 3) speeds.Add(-(along[k, end] - along[k, start]) / ((end - start) * dt));
                        start = -1;
                    }
                }
            }
            if (speeds.Count < 2) return fallback;
            speeds.Sort();
            float median = speeds[speeds.Count / 2];
            return median >= 0.3f ? median : fallback;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
