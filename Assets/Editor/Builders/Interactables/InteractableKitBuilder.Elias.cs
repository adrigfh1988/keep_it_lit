using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the Elias Vogt ("The Frost" / Patient #5 / Subject #5) template for <see cref="InteractableKit.Elias"/> (slot <c>elias</c>)
/// from <c>Assets/SourceFiles/Models/EliasVogt/EliasVogt_Model.fbx</c> and its URP materials in <c>Assets/SourceFiles/Materials/Asylum/Npcs</c>.
/// Uses the Starter Assets in-place humanoid locomotion BlendTree (<c>Idle</c> / <c>Walk_N</c> / <c>Run_N</c> driven by
/// <c>Speed</c> gait 0/1/2 and <c>MotionSpeed = planarSpeed / clipGroundSpeed</c>) and stance-speed measurement
/// (<see cref="MeasureNightNurseStanceSpeed"/>) so his bare frostbitten feet never slide, plus dedicated states for
/// overhead lamp-snuffing (<c>Harvest</c> / <c>Reach_Overhead.fbx</c>), convulsive cold surge (<c>Surge</c>),
/// focused-beam recoil (<c>Recoil</c>), first-catch frost shove (<c>Shove</c> / <c>Frost_Shove.fbx</c>), and lethal capture (<c>Pin</c>).
/// </summary>
public static partial class InteractableKitBuilder
{
    private const string EliasModelPath = "Assets/SourceFiles/Models/EliasVogt/EliasVogt_Model.fbx";
    private const string EliasMatFolder = "Assets/SourceFiles/Materials/Asylum/Npcs";
    private const string EliasControllerPath = ListenerAnimDir + "/Elias.controller";
    private const string EliasChaseMusicPath = "Assets/SourceFiles/Audio/Generated/Elias_FrostChase.wav";

    private const float DefaultEliasWalkSpeed = 1.98f;
    private const float DefaultEliasRunSpeed = 5.45f;

    private static readonly (string state, string clip)[] EliasPoseStates =
    {
        ("Harvest", "Reach_Overhead"),
        ("Surge",   "Zombie_Scream"),
        ("Recoil",  "Nervous_Look_Around"),
        ("Shove",   "Frost_Shove"),
        ("Pin",     "Zombie_Attack"),
    };

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
        AsylumHubBuilder.EnsureEliasVogtModel();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(EliasModelPath);
        bool usingCustomModel = model != null;
        if (!usingCustomModel)
        {
            model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        }
        if (model == null)
        {
            Debug.LogError($"InteractableKitBuilder: {EliasModelPath} (and fallback {RobotPrefabPath}) not found - the Elias template was skipped.");
            return null;
        }

        AnimationClip idleClip = LoadNightNurseClip(NightNurseIdlePath, "Idle");
        AnimationClip walkClip = LoadNightNurseClip(NightNurseWalkPath, "Walk_N");
        AnimationClip runClip  = LoadNightNurseClip(NightNurseRunPath, "Run_N");
        float walkSpeed = MeasureNightNurseStanceSpeed(model, walkClip, DefaultEliasWalkSpeed);
        float runSpeed  = MeasureNightNurseStanceSpeed(model, runClip, DefaultEliasRunSpeed);

        Transform orphan = parent.Find("Elias");
        if (orphan != null) Object.DestroyImmediate(orphan.gameObject);

        GameObject root = new GameObject("Elias");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = Quaternion.identity;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        if (usingCustomModel)
        {
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = AsylumHubBuilder.EliasVogtBodyName;
            body.tag = "Untagged";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;

            foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Rigidbody rb in body.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (Light l in body.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);

            Material headMat = AssetDatabase.LoadAssetAtPath<Material>($"{EliasMatFolder}/EliasVogt_Head.mat");
            Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>($"{EliasMatFolder}/EliasVogt_Body.mat");
            Material wbMat   = AssetDatabase.LoadAssetAtPath<Material>($"{EliasMatFolder}/EliasVogt_Wristband.mat");
            Material tagMat  = AssetDatabase.LoadAssetAtPath<Material>($"{EliasMatFolder}/EliasVogt_MorgueTag.mat");

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                Material targetMat = bodyMat;
                string rName = r.gameObject.name;
                if (rName.Contains("Head") && headMat != null) targetMat = headMat;
                else if (rName.Contains("Wristband") && wbMat != null) targetMat = wbMat;
                else if (rName.Contains("MorgueTag") && tagMat != null) targetMat = tagMat;

                if (targetMat != null)
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = targetMat;
                    r.sharedMaterials = mats;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            if (body.GetComponent<EliasPhysics>() == null)
            {
                body.AddComponent<EliasPhysics>();
            }
        }
        else
        {
            body.name = "Body_Elias";
            body.tag = "Untagged";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = new Vector3(1.06f, 1.12f, 1.06f);
            StripRobotToVisuals(body);
        }

        GameObject frostLightGo = new GameObject("EliasFrostLight");
        frostLightGo.transform.SetParent(root.transform, false);
        frostLightGo.transform.localPosition = new Vector3(0f, 1.32f, 0.22f);
        Light frostLight = frostLightGo.AddComponent<Light>();
        frostLight.type = LightType.Point;
        frostLight.color = new Color(0.35f, 0.78f, 1.0f);
        frostLight.range = 3.8f;
        frostLight.intensity = 0.22f;
        frostLight.shadows = LightShadows.None;

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null)
        {
            string avatarPath = AssetDatabase.GetAssetPath(model);
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(avatarPath).OfType<Avatar>().FirstOrDefault();
        }
        animator.runtimeAnimatorController = BuildOrLoadEliasController(idleClip, walkClip, runClip);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Elias elias = root.AddComponent<Elias>();
        SerializedObject eliasSo = new SerializedObject(elias);
        SerializedProperty walkProp = eliasSo.FindProperty("locomotionWalkSpeed");
        SerializedProperty runProp  = eliasSo.FindProperty("locomotionRunSpeed");
        if (walkProp != null) walkProp.floatValue = walkSpeed;
        if (runProp != null) runProp.floatValue = runSpeed;

        SerializedProperty chaseProp = eliasSo.FindProperty("franticChaseClip");
        if (chaseProp != null)
        {
            if (System.IO.File.Exists(EliasChaseMusicPath))
            {
                GameAudioImport.Apply(EliasChaseMusicPath, mono: false, stream: true);
            }
            AudioClip chaseClip = AssetDatabase.LoadAssetAtPath<AudioClip>(EliasChaseMusicPath);
            if (chaseClip == null)
            {
                chaseClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Horror Elements/Ambient/Amb_Run_2.wav");
            }
            chaseProp.objectReferenceValue = chaseClip;
        }

        AssignEliasClipArray(eliasSo, "patrolMutterClips", "elias_patrol_01", "elias_patrol_02", "elias_patrol_03", "elias_patrol_04");
        AssignEliasClipArray(eliasSo, "harvestLampClips", "elias_harvest_01", "elias_harvest_02", "elias_harvest_03", "elias_harvest_04");
        AssignEliasClipArray(eliasSo, "breathAndNoiseClips", "elias_breath_01", "elias_breath_02", "elias_breath_03", "elias_breath_04");
        AssignEliasClipArray(eliasSo, "spottedShoutClips", "elias_spotted_01", "elias_spotted_02", "elias_spotted_03", "elias_spotted_04");
        AssignEliasClipArray(eliasSo, "coldSurgeClips", "elias_surge_01", "elias_surge_02", "elias_surge_03");
        AssignEliasClipArray(eliasSo, "beamRecoilClips", "elias_recoil_01", "elias_recoil_02", "elias_recoil_03");
        AssignEliasClipArray(eliasSo, "lostSightClips", "elias_lost_01", "elias_lost_02", "elias_lost_03");
        AssignEliasClipArray(eliasSo, "captureClips", "elias_capture_01", "elias_capture_02", "elias_capture_03");

        eliasSo.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);
        return elias;
    }

    private const string EliasVoiceFolder = "Assets/SourceFiles/Audio/Generated/Voices/Elias";

    private static void AssignEliasClipArray(SerializedObject so, string propName, params string[] names)
    {
        SerializedProperty prop = so.FindProperty(propName);
        if (prop == null || names == null) return;
        prop.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            string path = $"{EliasVoiceFolder}/{names[i]}.wav";
            if (System.IO.File.Exists(path))
            {
                GameAudioImport.Apply(path, mono: true, stream: false);
            }
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            prop.GetArrayElementAtIndex(i).objectReferenceValue = clip;
        }
    }

    private static RuntimeAnimatorController BuildOrLoadEliasController(AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip)
    {
        if (!AssetDatabase.IsValidFolder(ListenerAnimDir))
        {
            AssetDatabase.CreateFolder("Assets/SourceFiles/Animation", "Descent");
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(EliasControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(EliasControllerPath);
        }

        if (idleClip == null) idleClip = LoadNightNurseMixamoClip("Zombie_Idle");
        if (walkClip == null) walkClip = LoadNightNurseMixamoClip("Walking");
        if (runClip == null)  runClip  = LoadNightNurseMixamoClip("Zombie_Run");

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(EliasControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Turn", AnimatorControllerParameterType.Float);
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

        foreach ((string state, string clip) in EliasPoseStates)
        {
            AnimatorState added = machine.AddState(state);
            AnimationClip loaded = LoadNightNurseMixamoClip(clip);
            if (loaded == null) loaded = idleClip;
            added.motion = loaded;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }
}
