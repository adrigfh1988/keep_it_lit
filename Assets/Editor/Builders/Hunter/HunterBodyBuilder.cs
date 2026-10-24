using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// KEEP IT LIT &gt; Build &gt; Hunter Body (F47, redone as F102). The hunter is Timmy again - the player's own robot
/// (PlayerRobot.prefab, unpacked and stripped to its Animator + mesh like the Stalker and the hub pursuer) - in the
/// "Hospital orderly" skin made by <see cref="HunterSkinBuilder"/>. Adam (Adam Character Pack, F47) is retired as the
/// hunter body.
///
/// Locomotion is Timmy's own Starter Assets Idle / Walk_N / Run_N (their ground speeds are measured and written to
/// AIFollower.locomotionWalkSpeed/RunSpeed); the pose states (look / lurk / stun / reach, F50) keep the Mixamo clips from
/// Assets/SourceFiles/Animation/Hunter/, retargeted onto Timmy's Humanoid avatar. Hunter.controller is generated data and is
/// rebuilt from scratch every run. AIFollower.UpdateAnimator drives the Locomotion tree by gait (Speed 0 idle / 1 walk / 2 run).
/// See plannings/hunter-timmy-orderly-plan.md.
///
/// Nothing here runs in a build; it is a one-shot scene edit, same pattern as ShopRoomBuilder.
/// </summary>
public static partial class HunterBodyBuilder
{
    private const string RobotPrefabPath = "Assets/Prefabs/PlayerRobot.prefab";
    private const string StarterClipFolder = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations";
    private const string StarterIdlePath = StarterClipFolder + "/Stand--Idle.anim.fbx";
    private const string StarterWalkPath = StarterClipFolder + "/Locomotion--Walk_N.anim.fbx";
    private const string StarterRunPath = StarterClipFolder + "/Locomotion--Run_N.anim.fbx";
    private const string StarterIdleName = "Idle";
    private const string StarterWalkName = "Walk_N";
    private const string StarterRunName = "Run_N";
    /// <summary>Scale of the hunter's robot, so it is not exactly the player's own size.</summary>
    private const float BodyScale = 1.05f;

    private const string ClipFolder = "Assets/SourceFiles/Animation/Hunter";
    private const string AnimationFolder = "Assets/SourceFiles/Animation";
    private const string ControllerPath = AnimationFolder + "/Hunter.controller";
    internal const string BodyChildName = "HunterBody";
    private const string PlayerRobotFileName = "PlayerRobot.prefab";

    private const float DefaultWalkSpeed = 2.3f;   // fallback: measured stance speed of Starter Assets Walk_N on the robot (F102)
    private const float DefaultRunSpeed = 6.2f;    // fallback: measured stance speed of Run_N on the robot (F102)
    private const float DefaultEyeHeight = 1.6f;
    private const float DefaultMaxWalkPlayback = 1.75f;
    private const float EyeHeightPadding = 0.10f;

    // F50: written into AIFollower alongside animator/eyeHeight/modelFacingYaw below. The scene's
    // Weeping_Angel is a pre-existing object with its own serialized values baked into the scene YAML
    // (acceleration 14, turnSpeed 540, animationBlendRate 10) - a new C# field default only applies to an
    // object that has never had that field serialized before, so decision 10's "mannequin" fix has to be
    // written the same way eyeHeight already is, or it never takes effect on this scene.
    private const float DefaultAcceleration = 6f;
    private const float DefaultSpeedRampRate = 14f;
    private const float DefaultAnimationBlendRate = 6f;
    private const float DefaultTurnSpeedMoving = 200f;
    private const float DefaultTurnSpeedStationary = 120f;

    // F50: suffix the mirrored copy of a turn take gets, so BuildController's 2D tree can always find
    // "<take>_Mirror" for the left-turn child next to the un-mirrored right-turn one.
    private const string MirrorSuffix = "_Mirror";

    // F50 decision 9: the clip's contact frame is meant to land right as GameOutcome.CaptureSequence's
    // blackout starts. reachContactSeconds after capture, reachContactNormalizedTime through the clip -
    // the Reach state's playback speed is scaled to land there; the clip itself is never trimmed.
    private const float ReachContactSeconds = 0.9f;
    private const float ReachContactNormalizedTime = 0.55f;
    private const float PoseCrossfadeSeconds = 0.25f;

    [MenuItem("KEEP IT LIT/Build/Hunter Body", priority = 105)]
    public static void Build() => Build(true);

    /// <summary>Dialog-free rebuild: replaces an existing HunterBody / leftover robot child without asking.</summary>
    public static void BuildSilently() => Build(false);

    private static void Build(bool dialogs)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            if (dialogs) EditorUtility.DisplayDialog("Build Hunter Body", "Open the game scene first.", "OK");
            return;
        }

        GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        if (robotPrefab == null)
        {
            Debug.LogError($"HunterBodyBuilder: {RobotPrefabPath} not found.");
            return;
        }

        AIFollower aiFollower = UnityEngine.Object.FindAnyObjectByType<AIFollower>();
        if (aiFollower == null)
        {
            if (dialogs) EditorUtility.DisplayDialog("Build Hunter Body", "No AIFollower found in the scene (looked for Weeping_Angel).", "OK");
            else Debug.LogError("HunterBodyBuilder: no AIFollower found in the scene.");
            return;
        }
        Transform aiRoot = aiFollower.transform;

        // ---------------------------------------------------------------- clips

        EnsureFolder(ClipFolder);

        // Timmy's own Starter Assets clips, loaded and never re-imported (the player and the other robots use the same files).
        AnimationClip idleClip = LoadClip(StarterIdlePath, StarterIdleName);
        AnimationClip walkClip = LoadClip(StarterWalkPath, StarterWalkName);
        AnimationClip runClip = LoadClip(StarterRunPath, StarterRunName);
        if (idleClip == null || walkClip == null || runClip == null)
        {
            Debug.LogError($"HunterBodyBuilder: a Starter Assets locomotion clip is missing (idle={idleClip != null}, walk={walkClip != null}, run={runClip != null}).");
            return;
        }

        // The Starter clips are in place (root-locked, no travel to measure), so these are the Starter Assets design speeds; the hunter robot is
        // scaled by BodyScale, which does not change the stride a humanoid clip covers (measured: the same stance speed at scale 1.0 and 1.05).
        // Measured on the body that will wear them: the cyborg's legs are not the robot's (see HunterBodyBuilder.Cyborg.cs).
        bool slender = CurrentLook == BodyLook.Slender;
        bool cyborg = CurrentLook == BodyLook.Cyborg;
        GameObject slenderModel = slender ? EnsureSlenderModel() : null;
        if (slender && slenderModel == null)
        {
            Debug.LogError($"HunterBodyBuilder: the Slender Hunter body is selected but {SlenderModelPath} is missing or not a valid Humanoid; nothing was changed.");
            return;
        }
        GameObject cyborgModel = cyborg ? EnsureCyborgModel() : null;
        if (cyborg && cyborgModel == null)
        {
            Debug.LogError($"HunterBodyBuilder: the cyborg body is selected but {CyborgModelPath} is missing or not a valid Humanoid; nothing was changed.");
            return;
        }
        GameObject measureModel = slender ? slenderModel : (cyborg ? cyborgModel : robotPrefab);
        float walkSpeed = GetClipSpeed(walkClip, DefaultWalkSpeed, measureModel);
        float runSpeed = GetClipSpeed(runClip, DefaultRunSpeed, measureModel);

        // F50: the optional pose clips. Each degrades to "missing" - no state, Pose maps to Idle - when no clip resolves.
        AnimationClip turnClip = ResolveTurnClip(out AnimationClip turnMirroredClip, out string turnSource);
        AnimationClip lookClip = ResolveOptionalClip("look", true, out string lookSource);
        AnimationClip lurkClip = ResolveOptionalClip("lurk", true, out string lurkSource);
        AnimationClip stunClip = ResolveOptionalClip("stun", true, out string stunSource);
        AnimationClip tantrumClip = ResolveOptionalClip("tantrum", false, out string tantrumSource);
        AnimationClip reachClip = ResolveOptionalClip("reach", false, out string reachSource);

        // ---------------------------------------------------------------- skin + controller (rebuilt every run)

        Material skin = (slender || cyborg) ? null : HunterSkinBuilder.EnsureSkin();
        AnimatorController controller = BuildController(idleClip, walkClip, runClip, turnClip, turnMirroredClip,
            lookClip, lurkClip, stunClip, tantrumClip, reachClip);

        // ---------------------------------------------------------------- scene swap, one undo step

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Hunter Body");

        Transform existingBody = aiRoot.Find(BodyChildName);
        if (existingBody != null)
        {
            if (dialogs && !EditorUtility.DisplayDialog("Build Hunter Body",
                    "Weeping_Angel already has a HunterBody child. Replace it?", "Replace", "Cancel"))
            {
                return;
            }
            Undo.DestroyObjectImmediate(existingBody.gameObject);
        }
        for (int i = aiRoot.childCount - 1; i >= 0; i--)
        {
            Transform ch = aiRoot.GetChild(i);
            if (ch.name == BodyChildName || ch.name.StartsWith("Hunter_Slender"))
            {
                Undo.DestroyObjectImmediate(ch.gameObject);
            }
        }

        Transform robotChild = FindRobotChild(aiRoot);
        if (robotChild != null)
        {
            if (dialogs && !EditorUtility.DisplayDialog("Build Hunter Body",
                    $"Remove the plain robot body ('{robotChild.name}') from Weeping_Angel? It will be replaced by the stripped Orderly robot.",
                    "Remove", "Cancel"))
            {
                return;
            }
            Undo.DestroyObjectImmediate(robotChild.gameObject);
        }

        bool aiWasActive = aiRoot.gameObject.activeSelf;
        aiRoot.gameObject.SetActive(true); // Animator.Rebind / Update need an active object

        GameObject bodyInstance;
        if (slender)
        {
            bodyInstance = InstantiateSlender(slenderModel, aiRoot);
            Undo.RegisterCreatedObjectUndo(bodyInstance, "Build Hunter Body");
        }
        else if (cyborg)
        {
            bodyInstance = InstantiateCyborg(cyborgModel, aiRoot);
            Undo.RegisterCreatedObjectUndo(bodyInstance, "Build Hunter Body");
        }
        else
        {
            bodyInstance = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab, aiRoot);
            Undo.RegisterCreatedObjectUndo(bodyInstance, "Build Hunter Body");
            InteractableKitBuilder.StripRobotToVisuals(bodyInstance); // unpacks the instance; PlayerRobot.prefab itself is never touched
            bodyInstance.transform.localScale = Vector3.one * BodyScale;

            foreach (Renderer renderer in bodyInstance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = skin;
                renderer.sharedMaterials = mats;
            }

            HunterSkinBuilder.AttachChestStencil(bodyInstance);
        }
        bodyInstance.name = BodyChildName;
        bodyInstance.transform.localPosition = Vector3.zero;
        bodyInstance.transform.localRotation = Quaternion.identity;

        Animator bodyAnimator = bodyInstance.GetComponentInChildren<Animator>();
        float eyeHeight = DefaultEyeHeight;
        if (bodyAnimator != null)
        {
            bodyAnimator.runtimeAnimatorController = controller;
            bodyAnimator.applyRootMotion = false;
            // The hunter is very often just out of view; a culled animator freezes the skeleton the
            // eyes and the face light hang from.
            bodyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            bodyAnimator.Rebind();
            bodyAnimator.Update(0f);
            bodyInstance.GetComponent<HunterChainPhysics>()?.PoseAtRest();
            Transform head = bodyAnimator.isHuman ? bodyAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
            float rise = head != null ? head.position.y - aiRoot.position.y : 0f;
            if (rise > 0.5f) eyeHeight = rise + EyeHeightPadding;
            else Debug.LogWarning($"HunterBodyBuilder: could not measure the Head bone on the robot; eyeHeight falls back to {DefaultEyeHeight:F2} m.", bodyInstance);
        }
        else
        {
            Debug.LogWarning("HunterBodyBuilder: the instantiated body has no Animator.", bodyInstance);
        }
        aiRoot.gameObject.SetActive(aiWasActive);

        HunterGaze gaze = Undo.AddComponent<HunterGaze>(bodyInstance);
        SerializedObject gazeSo = new SerializedObject(gaze);
        gazeSo.FindProperty("follower").objectReferenceValue = aiFollower;
        gazeSo.ApplyModifiedProperties();

        AIPresence presence = aiRoot.GetComponent<AIPresence>();
        if (presence != null)
        {
            SerializedObject presenceSo = new SerializedObject(presence);
            presenceSo.FindProperty("eyeColor").colorValue = slender ? Color.white : new Color(1f, 0.05f, 0.03f);
            presenceSo.FindProperty("eyeSize").floatValue = slender ? 0.018f : 0.035f;
            presenceSo.ApplyModifiedProperties();
        }

        SerializedObject so = new SerializedObject(aiFollower);
        so.FindProperty("animator").objectReferenceValue = bodyAnimator;
        so.FindProperty("eyeHeight").floatValue = eyeHeight;
        // The robot's Animator sits on a child node that is yawed from the root; AIFollower.VisualForward already adds that node yaw,
        // and the mesh itself faces the node's +Z, so there is nothing more to add.
        so.FindProperty("modelFacingYaw").floatValue = 0f;
        // F50 decision 10: the scene's Weeping_Angel carries its own serialized acceleration/turnSpeed/animationBlendRate values, so the
        // "mannequin" fix is written here, the same way eyeHeight is, or a new C# field default never takes effect.
        so.FindProperty("acceleration").floatValue = DefaultAcceleration;
        so.FindProperty("speedRampRate").floatValue = DefaultSpeedRampRate;
        so.FindProperty("animationBlendRate").floatValue = DefaultAnimationBlendRate;
        so.FindProperty("locomotionWalkSpeed").floatValue = walkSpeed;
        so.FindProperty("locomotionRunSpeed").floatValue = runSpeed;
        // F102: the walk clip carries the chase up to about walk x 1.75 (see AIFollower.WalkToRunSpeed); the run clip is for anything faster.
        so.FindProperty("maxWalkPlayback").floatValue = DefaultMaxWalkPlayback;
        so.FindProperty("turnSpeedMoving").floatValue = DefaultTurnSpeedMoving;
        so.FindProperty("turnSpeedStationary").floatValue = DefaultTurnSpeedStationary;
        // F50 decision 8: true (the body keeps spinning during a search dwell) whenever no look clip resolved.
        so.FindProperty("bodySweepsDuringSearch").boolValue = lookClip == null;
        so.ApplyModifiedProperties();

        string skinLabel = slender ? "Slender" : (cyborg ? "Cyborg" : skin.name);
        Debug.Log($"HunterBodyBuilder: idle={StarterIdleName}, walk={StarterWalkName}, run={StarterRunName} " +
            $"(walkSpeed={walkSpeed:F2}, runSpeed={runSpeed:F2}), turn={turnSource}, " +
            $"look={lookSource}, lurk={lurkSource}, stun={stunSource}, tantrum={tantrumSource}, reach={reachSource}, " +
            $"eyeHeight={eyeHeight:F2} m, skin={skinLabel}, bodySweepsDuringSearch={(lookClip == null)}.", bodyInstance);

        Undo.CollapseUndoOperations(undoGroup);

        if (dialogs)
        {
            Selection.activeGameObject = bodyInstance;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("Hunter body built. Save the scene (Ctrl+S) to keep it.", bodyInstance);
    }

    /// <summary>True when the scene hunter wears the body selected by Build > Hunter Body Look.</summary>
    internal static bool HasOrderlyBody(Transform aiRoot)
    {
        Transform body = aiRoot.Find(BodyChildName);
        if (body == null) return false;
        Renderer renderer = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (renderer == null || renderer.sharedMaterial == null) return false;
        if (CurrentLook == BodyLook.Slender) return renderer.sharedMaterial.name.StartsWith(SlenderMaterialPrefix);
        return CurrentLook == BodyLook.Cyborg
            ? renderer.sharedMaterial.name.StartsWith(CyborgMaterialPrefix)
            : renderer.sharedMaterial.name == HunterSkinBuilder.MaterialName;
    }

    // ---------------------------------------------------------------- clips

    /// <summary>
    /// F50: a keyword with no pack fallback - look/lurk/stun/reach. Missing (no file, or no non-preview
    /// clip in it) degrades to null, exactly like a missing pack-backed keyword would if the pack itself
    /// were incomplete: the caller maps that to Pose.Idle and the builder skips the state entirely.
    /// </summary>
    private static AnimationClip ResolveOptionalClip(string keyword, bool loop, out string source)
    {
        string fbxPath = FindClipFbx(keyword);
        if (fbxPath == null)
        {
            source = "missing";
            return null;
        }

        PrepareClipFbx(fbxPath, loop);
        AnimationClip clip = LoadClip(fbxPath, null);
        if (clip == null)
        {
            source = "missing (no clip found in file)";
            return null;
        }

        source = Path.GetFileNameWithoutExtension(fbxPath);
        return clip;
    }

    /// <summary>
    /// F50 decision 7: the turn keyword additionally needs a mirrored copy of the same take for the left
    /// turn - PrepareTurnClipFbx writes both a normal and a "&lt;take&gt;_Mirror" entry into the importer,
    /// and this reads them back explicitly by name rather than trusting LoadClip's "first non-preview
    /// clip" fallback, which has no reason to prefer the un-mirrored one.
    /// </summary>
    private static AnimationClip ResolveTurnClip(out AnimationClip mirroredClip, out string source)
    {
        mirroredClip = null;

        string fbxPath = FindClipFbx("turn");
        if (fbxPath == null)
        {
            source = "missing";
            return null;
        }

        PrepareTurnClipFbx(fbxPath);

        AnimationClip original = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
        {
            if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;

            if (clip.name.EndsWith(MirrorSuffix, StringComparison.Ordinal))
            {
                if (mirroredClip == null) mirroredClip = clip;
            }
            else if (original == null)
            {
                original = clip;
            }
        }

        if (original == null)
        {
            source = "missing (no clip found in file)";
            mirroredClip = null;
            return null;
        }

        source = Path.GetFileNameWithoutExtension(fbxPath);
        return original;
    }

    /// <summary>Finds an FBX model file under ClipFolder whose file name contains `keyword` (case-insensitive). First match wins.</summary>
    private static string FindClipFbx(string keyword)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ClipFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            if (!(AssetImporter.GetAtPath(path) is ModelImporter)) continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return path;
        }
        return null;
    }

    /// <summary>
    /// Sets a Mixamo clip FBX's importer to Humanoid/CreateFromThisModel with root-locked clips, as
    /// Decision 2 of the plan requires, looping according to `loop` (F50 decision 4: everything loops
    /// except the reach clip - without this it would restart under the capture blackout every cycle,
    /// until Release() moves the hunter on from Captured). Only touches the importer, and only reimports,
    /// if something actually differs, so a rebuild does not thrash a file the user already prepared.
    /// </summary>
    private static void PrepareClipFbx(string path, bool loop)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            dirty = true;
        }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }
        if (!importer.importAnimation)
        {
            importer.importAnimation = true;
            dirty = true;
        }

        ModelImporterClipAnimation[] current = importer.clipAnimations;
        ModelImporterClipAnimation[] baseline = current != null && current.Length > 0 ? current : importer.defaultClipAnimations;
        ModelImporterClipAnimation[] configured = new ModelImporterClipAnimation[baseline.Length];
        bool clipsDiffer = current == null || current.Length != baseline.Length;

        for (int i = 0; i < baseline.Length; i++)
        {
            ModelImporterClipAnimation clip = baseline[i];
            if (clip.loopTime != loop || !clip.lockRootRotation || !clip.lockRootHeightY || !clip.lockRootPositionXZ
                || !clip.keepOriginalOrientation || !clip.keepOriginalPositionY || !clip.keepOriginalPositionXZ)
            {
                clipsDiffer = true;
            }
            clip.loopTime = loop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            configured[i] = clip;
        }

        if (clipsDiffer)
        {
            importer.clipAnimations = configured;
            dirty = true;
        }

        if (dirty)
        {
            importer.SaveAndReimport();
        }
    }

    /// <summary>
    /// F50 decision 7: like PrepareClipFbx, but writes two entries per take - the original, and a
    /// "&lt;take&gt;_Mirror" copy with `mirror = true` for the left turn. Always re-derives from
    /// `importer.defaultClipAnimations` (never from the importer's current, possibly-already-mirrored
    /// clipAnimations), so a rebuild can never compound into "..._Mirror_Mirror" entries.
    /// </summary>
    private static void PrepareTurnClipFbx(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            dirty = true;
        }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }
        if (!importer.importAnimation)
        {
            importer.importAnimation = true;
            dirty = true;
        }

        ModelImporterClipAnimation[] existing = importer.clipAnimations;
        ModelImporterClipAnimation[] baseline = importer.defaultClipAnimations;

        ModelImporterClipAnimation[] configured = new ModelImporterClipAnimation[baseline.Length * 2];
        for (int i = 0; i < baseline.Length; i++)
        {
            configured[i] = CloneWithLocks(baseline[i], false);
            ModelImporterClipAnimation mirrored = CloneWithLocks(baseline[i], true);
            mirrored.name = baseline[i].name + MirrorSuffix;
            configured[baseline.Length + i] = mirrored;
        }

        bool clipsDiffer = existing == null || existing.Length != configured.Length;
        if (!clipsDiffer)
        {
            for (int i = 0; i < configured.Length; i++)
            {
                if (existing[i].name != configured[i].name || existing[i].mirror != configured[i].mirror
                    || existing[i].loopTime != configured[i].loopTime)
                {
                    clipsDiffer = true;
                    break;
                }
            }
        }

        if (clipsDiffer)
        {
            importer.clipAnimations = configured;
            dirty = true;
        }

        if (dirty)
        {
            importer.SaveAndReimport();
        }
    }

    /// <summary>
    /// A fresh clip-animation entry copying `source`'s take/frame range, with the standard loop + root
    /// locks applied and mirrored if requested. Always a new object rather than a mutated `source` -
    /// ModelImporterClipAnimation is a reference type, and PrepareTurnClipFbx needs the original and the
    /// mirrored copy to end up as two independent entries, not two variables aliasing one.
    /// </summary>
    private static ModelImporterClipAnimation CloneWithLocks(ModelImporterClipAnimation source, bool mirror)
    {
        ModelImporterClipAnimation clip = new ModelImporterClipAnimation
        {
            name = source.name,
            takeName = source.takeName,
            firstFrame = source.firstFrame,
            lastFrame = source.lastFrame,
            mirror = mirror
        };
        clip.loopTime = true;
        clip.lockRootRotation = true;
        clip.lockRootHeightY = true;
        clip.lockRootPositionXZ = true;
        clip.keepOriginalOrientation = true;
        clip.keepOriginalPositionY = true;
        clip.keepOriginalPositionXZ = true;
        return clip;
    }
    /// <summary>First non-preview AnimationClip in the model's asset representations, preferring one named `preferredName` if given.</summary>
    private static AnimationClip LoadClip(string path, string preferredName)
    {
        AnimationClip fallback = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
        {
            if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
            if (preferredName != null && clip.name == preferredName) return clip;
            if (fallback == null) fallback = clip;
        }
        return fallback;
    }

    private static float GetClipSpeed(AnimationClip clip, float fallback, GameObject robotPrefab = null)
    {
        if (clip == null) return fallback;
        float magnitude = clip.averageSpeed.magnitude;
        if (magnitude >= 0.05f) return magnitude;
        // The clips are imported root-locked (PrepareClipFbx), which zeroes averageSpeed - read the ground speed from the
        // humanoid root curves instead, or every in-place clip falls back to the default and the hunter glides.
        float measured = RootGroundSpeed(clip);
        if (measured >= 0.05f) return measured;
        // In-place clip (the Starter Assets ones): the speed the stance foot travels backwards through the body is the ground speed it covers.
        float stance = robotPrefab != null ? StanceGroundSpeed(robotPrefab, clip) : 0f;
        return stance >= 0.3f ? stance : fallback;
    }

    /// <summary>
    /// Ground speed (m/s, robot at scale 1) of an in-place locomotion clip: a temporary instance of the robot is posed through
    /// the clip and the toes that are on the floor (lowest centimetre of their travel) are followed in the body's own frame;
    /// they move backwards at exactly the speed the clip "walks". Returns 0 when the clip cannot be sampled.
    /// </summary>
    private static float StanceGroundSpeed(GameObject robotPrefab, AnimationClip clip)
    {
        if (clip == null || clip.length < 0.1f) return 0f;
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab);
        try
        {
            instance.transform.SetPositionAndRotation(new Vector3(0f, 1000f, 0f), Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return 0f;
            Transform[] toes = { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };
            if (toes[0] == null || toes[1] == null) return 0f;
            animator.runtimeAnimatorController = null;
            animator.Rebind();
            Vector3 forward = Vector3.ProjectOnPlane(animator.transform.forward, Vector3.up).normalized;

            const int frames = 120;
            float dt = clip.length / frames;
            float[,] along = new float[2, frames + 1];
            float[,] height = new float[2, frames + 1];
            float[] minHeight = { float.MaxValue, float.MaxValue };
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

            // Each stretch of frames with a toe on the floor is one stance: its net backwards travel over its duration is that
            // step's ground speed (the toe rolls through a stance, so single frames are noisy); the median over stances is returned.
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
            if (speeds.Count < 2) return 0f;
            speeds.Sort();
            return speeds[speeds.Count / 2];
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    /// <summary>Metres per second the clip's humanoid root (RootT x/z) travels from its first to its last frame.</summary>
    private static float RootGroundSpeed(AnimationClip clip)
    {
        if (clip.length <= 0.0001f) return 0f;
        float dx = 0f, dz = 0f;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.propertyName != "RootT.x" && binding.propertyName != "RootT.z") continue;
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) continue;
            float delta = curve.Evaluate(clip.length) - curve.Evaluate(0f);
            if (binding.propertyName == "RootT.x") dx = delta; else dz = delta;
        }
        return Mathf.Sqrt(dx * dx + dz * dz) / clip.length;
    }

    // ---------------------------------------------------------------- controller

    /// <summary>
    /// Hunter.controller is generated data, not authored content: this method deletes and recreates it
    /// from scratch on every run of KEEP IT LIT &gt; Build &gt; Hunter Body, wiring whatever clips resolved this
    /// run into a fixed shape - a Locomotion blend tree (1D on Speed, or 2D on Speed/Turn once a turn clip
    /// exists), one state each for LookAround/Lurk/Stunned when their clip resolved, and a Reach one-shot
    /// from Any State when a reach clip resolved. Nothing here is meant
    /// to survive being hand-edited in the Animator window - a state added, a transition retimed, a
    /// parameter renamed by hand is silently wiped the next time this runs, same as it already was before
    /// F50 for the single-state version. Treat the asset itself as disposable; this method is the only
    /// source of truth for its contents.
    /// </summary>
    private static AnimatorController BuildController(
        AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip,
        AnimationClip turnClip, AnimationClip turnMirroredClip,
        AnimationClip lookClip, AnimationClip lurkClip, AnimationClip stunClip, AnimationClip tantrumClip, AnimationClip reachClip)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        EnsureFolder(AnimationFolder);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        // F50: always added, whether or not any of the states below end up existing - AIFollower's own
        // Start() guard is what makes writing to these safe on a controller that lacks them (the Timmy
        // fallback), not their absence here.
        controller.AddParameter("Pose", AnimatorControllerParameterType.Int);
        controller.AddParameter("Turn", AnimatorControllerParameterType.Float);
        controller.AddParameter("Reach", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine root = controller.layers[0].stateMachine;

        AnimatorState locomotionState = BuildLocomotionState(controller, idleClip, walkClip, runClip,
            turnClip, turnMirroredClip);
        root.defaultState = locomotionState;

        AddPoseState(root, locomotionState, "LookAround", lookClip, AIFollower.Pose.LookAround);
        AddPoseState(root, locomotionState, "Lurk", lurkClip, AIFollower.Pose.Lurk);
        AddPoseState(root, locomotionState, "Stunned", stunClip, AIFollower.Pose.Stunned);
        AddPoseState(root, locomotionState, "Tantrum", tantrumClip, AIFollower.Pose.Tantrum);
        AddReachState(root, locomotionState, reachClip);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    /// <summary>
    /// The Locomotion blend tree, driven by gait (AIFollower.UpdateAnimator): Speed 0 = idle, 1 = walk, 2 = run, so a tree
    /// time-syncs only the two gaits it is crossfading and the feet are never blended between a long and a short stride.
    /// A 1D tree on Speed alone unless a turn clip (and its mirrored copy) resolved - then a 2D freeform-cartesian tree on
    /// (Speed, Turn), with the turn clip at (0, +1) and its mirror at (0, -1). Explicit thresholds throughout, not automatic.
    /// The state's own speed parameter is MotionSpeed = body speed / clip ground speed, written by AIFollower.
    /// </summary>
    private static AnimatorState BuildLocomotionState(AnimatorController controller,
        AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip,
        AnimationClip turnClip, AnimationClip turnMirroredClip)
    {
        AnimatorState locomotionState = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        locomotionState.speedParameter = "MotionSpeed";
        locomotionState.speedParameterActive = true;

        if (turnClip != null && turnMirroredClip != null)
        {
            tree.blendType = BlendTreeType.FreeformCartesian2D;
            tree.blendParameter = "Speed";
            tree.blendParameterY = "Turn";

            tree.AddChild(idleClip, new Vector2(0f, 0f));
            tree.AddChild(walkClip, new Vector2(1f, 0f));
            tree.AddChild(runClip, new Vector2(2f, 0f));
            tree.AddChild(turnClip, new Vector2(0f, 1f));
            tree.AddChild(turnMirroredClip, new Vector2(0f, -1f));
        }
        else
        {
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;

            tree.AddChild(idleClip, 0f);
            tree.AddChild(walkClip, 1f);
            tree.AddChild(runClip, 2f);
        }

        return locomotionState;
    }

    /// <summary>
    /// F50 decision 2/11: a state that exists only when its clip resolved, entered/exited purely on Pose
    /// equality - a 0.25s crossfade each way, no exit time, so the transition starts the instant
    /// AIFollower's UpdateAnimator writes the new Pose rather than waiting for Locomotion to loop round.
    /// </summary>
    private static void AddPoseState(AnimatorStateMachine root, AnimatorState locomotionState, string stateName,
        AnimationClip clip, AIFollower.Pose pose)
    {
        if (clip == null) return;

        AnimatorState state = root.AddState(stateName);
        state.motion = clip;

        AnimatorStateTransition toState = locomotionState.AddTransition(state);
        toState.hasExitTime = false;
        toState.duration = PoseCrossfadeSeconds;
        toState.AddCondition(AnimatorConditionMode.Equals, (float)(int)pose, "Pose");

        AnimatorStateTransition toLocomotion = state.AddTransition(locomotionState);
        toLocomotion.hasExitTime = false;
        toLocomotion.duration = PoseCrossfadeSeconds;
        toLocomotion.AddCondition(AnimatorConditionMode.NotEqual, (float)(int)pose, "Pose");
    }

    /// <summary>
    /// F50 decision 9: only added when a reach clip resolved (the Reach parameter itself always exists -
    /// see BuildController - so a missing clip just means the trigger fires and nothing plays). Entered
    /// from Any State on the Reach trigger, canTransitionToSelf false so the trigger re-firing mid-clip
    /// (it does not, AIFollower only sets it once in EnterCaptured, but Any State transitions are shared
    /// machinery) can never restart it. Exits to Locomotion on exit time 1.0, not "no exit": Release()
    /// (Second Wind) can take the hunter from Captured back to Wander mid-reach, and without this exit the
    /// animator would sit on the clip's last frame for the whole stun that follows.
    /// </summary>
    private static void AddReachState(AnimatorStateMachine root, AnimatorState locomotionState, AnimationClip reachClip)
    {
        if (reachClip == null) return;

        AnimatorState state = root.AddState("Reach");
        state.motion = reachClip;
        if (reachClip.length > 0.0001f)
        {
            state.speed = (ReachContactNormalizedTime * reachClip.length) / ReachContactSeconds;
        }

        AnimatorStateTransition fromAny = root.AddAnyStateTransition(state);
        fromAny.hasExitTime = false;
        fromAny.duration = 0f;
        fromAny.canTransitionToSelf = false;
        fromAny.AddCondition(AnimatorConditionMode.If, 0f, "Reach");

        AnimatorStateTransition toLocomotion = state.AddTransition(locomotionState);
        toLocomotion.hasExitTime = true;
        toLocomotion.exitTime = 1.0f;
        toLocomotion.duration = PoseCrossfadeSeconds;
    }

    // ---------------------------------------------------------------- scene lookup

    /// <summary>
    /// The direct child of Weeping_Angel whose prefab source is PlayerRobot.prefab (matched by filename,
    /// not by a hardcoded GUID - the project has two copies of the prefab at different paths). Skips the
    /// NavMesh Link child. Returns null if there is none (e.g. the swap already ran).
    /// </summary>
    private static Transform FindRobotChild(Transform aiRoot)
    {
        for (int i = 0; i < aiRoot.childCount; i++)
        {
            Transform child = aiRoot.GetChild(i);
            if (child.GetComponent<NavMeshLink>() != null) continue;

            UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
            if (source == null) continue;

            string assetPath = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(assetPath) && Path.GetFileName(assetPath) == PlayerRobotFileName)
            {
                return child;
            }
        }
        return null;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
