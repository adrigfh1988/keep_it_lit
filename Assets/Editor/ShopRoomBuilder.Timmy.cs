using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The shop's salesman is Timmy, the player's own robot (a PlayerRobot.prefab instance stripped to its visuals by
/// InteractableKitBuilder.StripRobotToVisuals - the prefab asset is never touched), on ShopTimmy.controller: Idle
/// (Starter Assets Stand--Idle), Talk (the Mixamo Talking clip, also used by the hub NPCs) and Wave (Mixamo
/// Waving.fbx, optional - copied from the shared Mixamo library when it is there; without it a greeting is a short
/// burst of talking). ShopkeeperAnimator plays them; ShopRoom calls it.
///
/// He is a hand-editable prefab, Assets/Prefabs/ShopSalesman.prefab: the first build generates it, after that every
/// build only places it (edit him in Prefab Mode; deleting the prefab is the reset). <see cref="SwapSalesmanToTimmy"/>
/// brings an existing room there without rebuilding anything else (the ProjectSetup piece "Shop salesman (Timmy)").
/// Re-running it after Waving.fbx was added only adds the Wave state.
/// </summary>
public static partial class ShopRoomBuilder
{
    private const string RobotPrefabPath = "Assets/Prefabs/PlayerRobot.prefab";
    /// <summary>The hand-editable salesman (Timmy + blocker + ShopkeeperAnimator). Builders only place it once it exists.</summary>
    public const string SalesmanPrefabPath = "Assets/Prefabs/ShopSalesman.prefab";
    private const string ShopAnimDir = "Assets/SourceFiles/Animation/Shop";
    private const string ShopkeeperControllerPath = ShopAnimDir + "/ShopTimmy.controller";
    private const string IdleClipPath = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx";
    private const string TalkingClipPath = "Assets/SourceFiles/Animation/Mixamo/Talking.fbx";
    private const string WavingClipPath = "Assets/SourceFiles/Animation/Mixamo/Waving.fbx";
    // The shared Mixamo library outside the project: copy from it when a clip is needed, never move.
    private const string WavingLibraryPath = "D:/Unity/essentials/Assets/Mixamo/Waving.fbx";

    /// <summary>
    /// Non-null when the open scene's ShopRoom still has the old salesman, its salesman is not an instance of
    /// ShopSalesman.prefab, or Timmy lacks a Wave the project now has.
    /// </summary>
    public static string TimmySalesmanProblem()
    {
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) return null; // reported by "Shop room"
        Transform salesman = shop.transform.Find("Salesman");
        if (salesman == null) return "the shop has no Salesman";

        SerializedObject so = new SerializedObject(shop);
        if (so.FindProperty("shopkeeper").objectReferenceValue == null) return "the salesman is still the Guard (no Timmy / ShopkeeperAnimator wired)";
        if (!IsSalesmanPrefabInstance(salesman)) return $"the salesman is not an instance of {SalesmanPrefabPath}";
        if (WaveAvailable() && !ControllerHasWave()) return "Waving.fbx is available but Timmy's controller has no Wave state";
        return null;
    }

    /// <summary>
    /// Dialog-free migration. An old Guard is replaced by the ShopSalesman prefab (generated first if it does not exist);
    /// a Timmy that is not a prefab instance yet is saved as the prefab (or, if the prefab already exists, replaced by an
    /// instance of it, so a hand-edited prefab always wins). Also adds a newly available Wave state. Idempotent.
    /// </summary>
    public static bool SwapSalesmanToTimmy()
    {
        Scene scene = SceneManager.GetActiveScene();
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) return false;
        Transform salesman = shop.transform.Find("Salesman");
        EnsureShopkeeperController();

        SerializedObject so = new SerializedObject(shop);
        bool isTimmy = salesman != null && so.FindProperty("shopkeeper").objectReferenceValue != null;
        if (isTimmy && IsSalesmanPrefabInstance(salesman)) return false;

        bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(SalesmanPrefabPath) != null;
        if (isTimmy && !prefabExists)
        {
            // Today's Timmy (with any hand tweaks) becomes the prefab, connected in place.
            PrefabUtility.SaveAsPrefabAssetAndConnect(salesman.gameObject, SalesmanPrefabPath, InteractionMode.AutomatedAction);
        }
        else
        {
            // The Guard (and the eye beads on its head bone) or a stray copy goes; the prefab takes its place.
            if (salesman != null) Undo.DestroyObjectImmediate(salesman.gameObject);
            salesman = PlaceSalesman(shop.transform);
        }

        Transform head = SalesmanParts(salesman, out ShopkeeperAnimator shopkeeper);
        so.FindProperty("headPivot").objectReferenceValue = head;
        so.FindProperty("shopkeeper").objectReferenceValue = shopkeeper;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Shop salesman is Timmy, an instance of {SalesmanPrefabPath}{(ControllerHasWave() ? "" : " (no Waving.fbx yet: he greets by talking)")}. Save the scene (Ctrl+S) to keep it.", shop);
        return true;
    }

    /// <summary>
    /// An instance of ShopSalesman.prefab behind the counter. The first call generates the prefab (Timmy, his blocker
    /// collider and ShopkeeperAnimator); after that the prefab is the source of truth and is only placed.
    /// </summary>
    private static Transform PlaceSalesman(Transform room)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SalesmanPrefabPath);
        Transform salesman;
        if (prefab != null)
        {
            salesman = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, room)).transform;
            Undo.RegisterCreatedObjectUndo(salesman.gameObject, "Shop Salesman");
        }
        else
        {
            salesman = Group(room, "Salesman");
            // This collider is what stops the player walking through the salesman; the robot itself has none.
            CapsuleCollider blocker = salesman.gameObject.AddComponent<CapsuleCollider>();
            blocker.center = new Vector3(0f, 0.9f, 0f);
            blocker.radius = 0.3f;
            blocker.height = 1.8f;
            BuildTimmy(salesman, out _);
        }

        salesman.name = "Salesman";
        salesman.localPosition = new Vector3(0f, 0f, 5.1f);
        salesman.localRotation = Quaternion.Euler(0f, 180f, 0f);
        if (prefab == null) PrefabUtility.SaveAsPrefabAssetAndConnect(salesman.gameObject, SalesmanPrefabPath, InteractionMode.AutomatedAction);
        return salesman;
    }

    /// <summary>The ShopkeeperAnimator on a placed salesman, and the Head bone ShopRoom turns (null without a Humanoid body).</summary>
    private static Transform SalesmanParts(Transform salesman, out ShopkeeperAnimator shopkeeper)
    {
        shopkeeper = salesman.GetComponent<ShopkeeperAnimator>();
        Animator animator = salesman.GetComponentInChildren<Animator>();
        Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) Debug.LogWarning("ShopRoomBuilder: the salesman has no Humanoid Head bone, so his head will not follow the player.", salesman);
        return head;
    }

    private static bool IsSalesmanPrefabInstance(Transform salesman) =>
        PrefabUtility.IsPartOfPrefabInstance(salesman.gameObject) &&
        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(salesman.gameObject) == SalesmanPrefabPath;

    /// <summary>Timmy under <paramref name="salesman"/>, idling, with a ShopkeeperAnimator on the group. Returns his Head bone.</summary>
    private static Transform BuildTimmy(Transform salesman, out ShopkeeperAnimator shopkeeper)
    {
        shopkeeper = null;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"ShopRoomBuilder: {RobotPrefabPath} is missing, so the shop has no salesman body.");
            return null;
        }

        GameObject timmy = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(timmy, "Shop Timmy");
        InteractableKitBuilder.StripRobotToVisuals(timmy);
        timmy.name = "Timmy";
        timmy.tag = "Untagged"; // the prefab root is tagged Player; a second Player would confuse every trigger
        timmy.transform.SetParent(salesman, false);
        timmy.transform.localPosition = Vector3.zero;
        timmy.transform.localRotation = Quaternion.identity;
        timmy.transform.localScale = Vector3.one;
        MakeMovable(timmy.transform); // the room is static; a skinned, animated body must not be

        Animator animator = timmy.GetComponentInChildren<Animator>();
        if (animator == null) animator = timmy.AddComponent<Animator>();
        // PlayerRobot.prefab's Robot child is turned 90 degrees, which would leave him facing sideways at the counter.
        animator.transform.localRotation = Quaternion.identity;
        animator.runtimeAnimatorController = EnsureShopkeeperController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Pose him now, so the room shows him idling in the Editor instead of in his bind pose.
        AnimationClip idle = FirstClip(IdleClipPath);
        if (idle != null)
        {
            idle.SampleAnimation(animator.gameObject, 0f);
            timmy.transform.localPosition = Vector3.zero;
            timmy.transform.localRotation = Quaternion.identity;
            animator.transform.localRotation = Quaternion.identity;
        }

        shopkeeper = salesman.GetComponent<ShopkeeperAnimator>();
        if (shopkeeper == null) shopkeeper = Undo.AddComponent<ShopkeeperAnimator>(salesman.gameObject);
        SerializedObject so = new SerializedObject(shopkeeper);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();

        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) Debug.LogWarning("ShopRoomBuilder: Timmy has no Humanoid Head bone, so the salesman's head will not follow the player.", timmy);
        return head;
    }

    /// <summary>ShopTimmy.controller with Idle (default), Talk and, once Waving.fbx exists, Wave (which returns to Idle). Adds missing states only.</summary>
    private static AnimatorController EnsureShopkeeperController()
    {
        EnsureWavingClip();

        if (!AssetDatabase.IsValidFolder(ShopAnimDir)) AssetDatabase.CreateFolder("Assets/SourceFiles/Animation", "Shop");
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ShopkeeperControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ShopkeeperControllerPath);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idle = EnsureState(sm, "Idle", FirstClip(IdleClipPath));
        EnsureState(sm, "Talk", FirstClip(TalkingClipPath));
        sm.defaultState = idle;

        AnimationClip waving = FirstClip(WavingClipPath);
        if (waving != null)
        {
            AnimatorState wave = EnsureState(sm, "Wave", waving);
            if (!wave.transitions.Any(t => t.destinationState == idle))
            {
                AnimatorStateTransition back = wave.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.25f;
                back.hasFixedDuration = true;
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimatorState EnsureState(AnimatorStateMachine sm, string name, Motion motion)
    {
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        if (state == null) state = sm.AddState(name);
        if (motion != null) state.motion = motion;
        else Debug.LogWarning($"ShopRoomBuilder: no clip for Timmy's {name} state.");
        return state;
    }

    /// <summary>Copies Waving.fbx from the Mixamo library when the project lacks it, and imports it Humanoid, not looping, in place.</summary>
    private static void EnsureWavingClip()
    {
        if (!File.Exists(WavingClipPath))
        {
            if (!File.Exists(WavingLibraryPath)) return;
            File.Copy(WavingLibraryPath, WavingClipPath);
            AssetDatabase.ImportAsset(WavingClipPath, ImportAssetOptions.ForceUpdate);
        }

        ModelImporter importer = AssetImporter.GetAtPath(WavingClipPath) as ModelImporter;
        if (importer == null) return;
        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; dirty = true; }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips.Length > 0 && (clips[0].loopTime || clips[0].name != "Waving" || !clips[0].keepOriginalPositionXZ))
        {
            clips[0].name = "Waving";
            clips[0].loopTime = false;
            clips[0].keepOriginalOrientation = true;
            clips[0].keepOriginalPositionY = true;
            clips[0].keepOriginalPositionXZ = true;
            importer.clipAnimations = clips;
            dirty = true;
        }
        if (dirty) importer.SaveAndReimport();
    }

    private static bool WaveAvailable() => File.Exists(WavingClipPath) || File.Exists(WavingLibraryPath);

    private static bool ControllerHasWave()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ShopkeeperControllerPath);
        return controller != null && controller.layers[0].stateMachine.states.Any(s => s.state.name == "Wave" && s.state.motion != null);
    }

    private static AnimationClip FirstClip(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
}
