using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the Sister Agnes ("The Night Nurse") template for <see cref="InteractableKit.NightNurse"/> (slot <c>nightNurse</c>)
/// without modifying <c>NurseAgnes_Model.fbx</c> or its existing materials in <c>Assets/SourceFiles/Materials/Asylum/Npcs</c>.
/// </summary>
public static partial class InteractableKitBuilder
{
    private const string NightNurseModelPath = "Assets/SourceFiles/Models/NurseAgnes/NurseAgnes_Model.fbx";
    private const string NightNurseMatFolder = "Assets/SourceFiles/Materials/Asylum/Npcs";
    private const string NightNurseControllerPath = ListenerAnimDir + "/NightNurse.controller";

    private static readonly (string state, string clip)[] NightNurseStates =
    {
        ("Patrol", "Walking"),
        ("Warn", "Look_Around"),
        ("Scream", "Zombie_Scream"),
        ("Rush", "Zombie_Run"),
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
        animator.runtimeAnimatorController = NightNurseController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (body.GetComponent<NurseAgnesPhysics>() == null)
        {
            body.AddComponent<NurseAgnesPhysics>();
        }

        NightNurse nurse = root.AddComponent<NightNurse>();
        root.SetActive(false);
        return nurse;
    }

    private static AnimatorController NightNurseController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(NightNurseControllerPath);
        if (controller == null)
        {
            if (!AssetDatabase.IsValidFolder(ListenerAnimDir)) AssetDatabase.CreateFolder("Assets/SourceFiles/Animation", "Descent");
            controller = AnimatorController.CreateAnimatorControllerAtPath(NightNurseControllerPath);
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach ((string state, string clip) in NightNurseStates)
        {
            if (machine.states.Any(s => s.state.name == state)) continue;

            AnimatorState added = machine.AddState(state);
            added.motion = AssetDatabase.LoadAllAssetsAtPath(MixamoClipDir + clip + ".fbx")
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (added.motion == null) Debug.LogWarning($"InteractableKitBuilder: Mixamo clip {clip} not found - NightNurse state {state} is empty.");
            if (machine.defaultState == null || state == NightNurseStates[0].state) machine.defaultState = added;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }
}
