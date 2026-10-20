using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the Cook Bruno ("The Butcher" / Dark Bruno) template for <see cref="InteractableKit.DarkBruno"/> (slot <c>darkBruno</c>)
/// from <c>Assets/SourceFiles/Models/CookBruno/CookBruno_Model.fbx</c> and its URP materials in <c>Assets/SourceFiles/Materials/Asylum/Npcs</c>.
/// Uses the same Starter Assets in-place humanoid locomotion BlendTree (<c>Idle</c> / <c>Walk_N</c> / <c>Run_N</c> driven by
/// <c>Speed</c> gait 0/1/2 and <c>MotionSpeed = planarSpeed / clipGroundSpeed</c>) and stance-speed measurement as
/// <see cref="HunterBodyBuilder"/> and <c>InteractableKitBuilder.NightNurse</c> so his feet match his ground travel speed without sliding.
/// </summary>
public static partial class InteractableKitBuilder
{
    private const string DarkBrunoModelPath = "Assets/SourceFiles/Models/CookBruno/CookBruno_Model.fbx";
    private const string DarkBrunoMatFolder = "Assets/SourceFiles/Materials/Asylum/Npcs";
    private const string DarkBrunoControllerPath = ListenerAnimDir + "/DarkBruno.controller";

    private const float DefaultBrunoWalkSpeed = 2.16f;
    private const float DefaultBrunoRunSpeed = 5.92f;

    private static readonly (string state, string clip)[] DarkBrunoPoseStates =
    {
        ("Chop", "Zombie_Idle"),
        ("Warn", "Look_Around"),
        ("Roar", "Zombie_Scream"),
        ("Pin", "Zombie_Attack"),
    };

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Dark Bruno (Maze Butcher)", priority = 153)]
    public static void RebuildDarkBrunoTemplate()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null)
        {
            Build();
            return;
        }

        SerializedObject so = new SerializedObject(kit);
        SerializedProperty prop = so.FindProperty("darkBruno");
        if (prop != null)
        {
            Component old = prop.objectReferenceValue as Component;
            prop.objectReferenceValue = null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        Build();
    }

    private static DarkBruno BuildDarkBrunoTemplate(Transform parent, Vector3 localPosition)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(DarkBrunoModelPath);
        if (model == null)
        {
            model = AssetDatabase.LoadAssetAtPath<GameObject>(ListenerModelPath);
        }
        if (model == null)
        {
            Debug.LogError($"InteractableKitBuilder: {DarkBrunoModelPath} not found - the DarkBruno template was skipped.");
            return null;
        }

        AnimationClip idleClip = LoadNightNurseClip(NightNurseIdlePath, "Idle");
        AnimationClip walkClip = LoadNightNurseClip(NightNurseWalkPath, "Walk_N");
        AnimationClip runClip  = LoadNightNurseClip(NightNurseRunPath, "Run_N");
        float walkSpeed = MeasureNightNurseStanceSpeed(model, walkClip, DefaultBrunoWalkSpeed);
        float runSpeed  = MeasureNightNurseStanceSpeed(model, runClip, DefaultBrunoRunSpeed);

        GameObject root = new GameObject("DarkBruno");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = "Body_CookBruno";
        body.tag = "Untagged";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;

        foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody rb in body.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Light l in body.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);

        Material headMat   = AssetDatabase.LoadAssetAtPath<Material>($"{DarkBrunoMatFolder}/CookBruno_Head.mat");
        Material bodyMat   = AssetDatabase.LoadAssetAtPath<Material>($"{DarkBrunoMatFolder}/CookBruno_Body.mat");
        Material cBladeMat = AssetDatabase.LoadAssetAtPath<Material>($"{DarkBrunoMatFolder}/CookBruno_Cleaver_Blade.mat");
        Material cHandMat  = AssetDatabase.LoadAssetAtPath<Material>($"{DarkBrunoMatFolder}/CookBruno_Cleaver_Handle.mat");
        Material cRivMat   = AssetDatabase.LoadAssetAtPath<Material>($"{DarkBrunoMatFolder}/CookBruno_Cleaver_Rivets.mat");
        Material ladleMat  = AssetDatabase.LoadAssetAtPath<Material>($"{DarkBrunoMatFolder}/CookBruno_Ladle.mat");

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material targetMat = bodyMat;
            string rName = r.gameObject.name;
            if (rName.Contains("Head") && headMat != null) targetMat = headMat;
            else if (rName.Contains("Cleaver_Blade") && cBladeMat != null) targetMat = cBladeMat;
            else if (rName.Contains("Cleaver_Handle") && cHandMat != null) targetMat = cHandMat;
            else if (rName.Contains("Cleaver_Rivets") && cRivMat != null) targetMat = cRivMat;
            else if (rName.Contains("Ladle") && ladleMat != null) targetMat = ladleMat;

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
        animator.runtimeAnimatorController = DarkBrunoController(idleClip, walkClip, runClip);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (body.GetComponent<CookBrunoPhysics>() == null)
        {
            body.AddComponent<CookBrunoPhysics>();
        }

        DarkBruno butcher = root.AddComponent<DarkBruno>();
        SerializedObject butcherSo = new SerializedObject(butcher);
        SerializedProperty walkProp = butcherSo.FindProperty("locomotionWalkSpeed");
        SerializedProperty runProp = butcherSo.FindProperty("locomotionRunSpeed");
        if (walkProp != null) walkProp.floatValue = walkSpeed;
        if (runProp != null) runProp.floatValue = runSpeed;

        AssignBrunoClipArray(butcherSo, "chopMutterClips", "bruno_chop_01", "bruno_chop_02", "bruno_chop_03");
        AssignBrunoClipArray(butcherSo, "rotateClips", "bruno_rotate_01", "bruno_rotate_02", "bruno_rotate_03");
        AssignBrunoClipArray(butcherSo, "investigateClips", "bruno_investigate_01", "bruno_investigate_02", "bruno_investigate_03");
        AssignBrunoClipArray(butcherSo, "warnClips", "bruno_warn_01", "bruno_warn_02", "bruno_warn_03");
        AssignBrunoClipArray(butcherSo, "roarIntrusionClips", "bruno_roar_intrude_01", "bruno_roar_intrude_02", "bruno_roar_intrude_03");
        AssignBrunoClipArray(butcherSo, "roarStareClips", "bruno_roar_stare_01", "bruno_roar_stare_02", "bruno_roar_stare_03");
        AssignBrunoClipArray(butcherSo, "pinClips", "bruno_pin_01", "bruno_pin_02", "bruno_pin_03");

        SerializedProperty chaseProp = butcherSo.FindProperty("franticChaseClip");
        if (chaseProp != null)
        {
            chaseProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Horror Elements/Ambient/Amb_Run_2.wav");
        }

        butcherSo.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);
        return butcher;
    }

    private const string BrunoVoiceFolder = "Assets/SourceFiles/Audio/Generated/Voices/Bruno";

    private static void AssignBrunoClipArray(SerializedObject so, string propName, params string[] names)
    {
        SerializedProperty prop = so.FindProperty(propName);
        if (prop == null || names == null) return;
        List<AudioClip> found = new List<AudioClip>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            string path = $"{BrunoVoiceFolder}/{names[i]}.wav";
            GameAudioImport.Apply(path, mono: true, stream: false);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) found.Add(clip);
        }
        prop.arraySize = found.Count;
        for (int i = 0; i < found.Count; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }
    }

    private static AnimatorController DarkBrunoController(AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip)
    {
        if (!AssetDatabase.IsValidFolder(ListenerAnimDir))
        {
            AssetDatabase.CreateFolder("Assets/SourceFiles/Animation", "Descent");
        }
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(DarkBrunoControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(DarkBrunoControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(DarkBrunoControllerPath);
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

        foreach ((string state, string clip) in DarkBrunoPoseStates)
        {
            AnimatorState added = machine.AddState(state);
            // Chop, Roar and Pin all use the clean humanoid Idle base clip so CookBrunoPhysics drives the cleaver/torso/jaw without A-pose armpit tearing
            bool useCleanIdleBase = (state == "Chop" || state == "Roar" || state == "Pin") && idleClip != null;
            added.motion = useCleanIdleBase ? idleClip : LoadNightNurseMixamoClip(clip);
            if (added.motion == null) Debug.LogWarning($"InteractableKitBuilder: Mixamo clip {clip} not found - DarkBruno state {state} is empty.");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }
}
