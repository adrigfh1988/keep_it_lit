using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// F107 D2: the Listener template of the InteractableKit (slot `listener`), part of LIGHTS OUT &gt; Build &gt; Interactables (build-missing-only).
/// The body is the hub pursuer's Mixamo subject (Char_Subject.fbx) stripped to an Animator and renderers, in a pale, gaunt, non-emissive
/// material, driving Animation/Descent/Listener.controller (clips copied from the Mixamo library, no transitions: Listener.cs cross-fades by
/// state name). No eyes and no AIPresence: it is blind.
/// </summary>
public static partial class InteractableKitBuilder
{
    private const string ListenerModelPath = "Assets/SourceFiles/Characters/Mixamo/Char_Subject.fbx";
    private const string ListenerAnimDir = "Assets/SourceFiles/Animation/Descent";
    private const string ListenerControllerPath = ListenerAnimDir + "/Listener.controller";
    private const string MixamoClipDir = "Assets/SourceFiles/Animation/Mixamo/";

    /// <summary>A gaunt frame: the subject, a little narrower.</summary>
    private static readonly Vector3 ListenerBodyScale = new Vector3(0.92f, 1f, 0.92f);

    /// <summary>Animator state -> Mixamo clip. Dormant is the lying idle, so a Listener left in a corridor reads as a body.</summary>
    private static readonly (string state, string clip)[] ListenerStates =
    {
        ("Dormant", "Laying_Idle"), ("Crawl", "Zombie_Crawl"), ("Idle", "Zombie_Idle"), ("Scream", "Zombie_Scream"), ("RunCrawl", "Zombie_RunningCrawl"),
    };

    /// <summary>Builds the template under `parent` (inactive, like the Stalker's: a skinned body must not animate in the gallery). Null if the model is missing.</summary>
    private static Listener BuildListenerTemplate(Transform parent, Vector3 localPosition)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ListenerModelPath);
        if (model == null)
        {
            Debug.LogError($"InteractableKitBuilder: {ListenerModelPath} not found - the Listener template was skipped.");
            return null;
        }

        GameObject root = new GameObject("Listener");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = "Body";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = ListenerBodyScale;

        foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody rb in body.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Light l in body.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);

        Material skin = ListenerMaterial(body);
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = skin;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null) animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ListenerModelPath).OfType<Avatar>().FirstOrDefault();
        animator.runtimeAnimatorController = ListenerController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Listener listener = root.AddComponent<Listener>();
        root.SetActive(false);
        return listener;
    }

    /// <summary>A pale, grey-blue, matte copy of the subject's material: no texture, no emission, no highlights, no environment reflections (F93 gotcha 4: a near-black albedo reads grey through sky reflections).</summary>
    private static Material ListenerMaterial(GameObject body)
    {
        string path = $"{MaterialFolder}/Listener_Body.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        EnsureFolder();
        Renderer first = body.GetComponentInChildren<Renderer>(true);
        Material source = first != null ? first.sharedMaterial : null;
        Material material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.name = "Listener_Body";
        foreach (string tex in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_ParallaxMap", "_DetailAlbedoMap", "_DetailNormalMap" })
        {
            if (material.HasProperty(tex)) material.SetTexture(tex, null);
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.46f, 0.49f, 0.5f));
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
        if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.DisableKeyword("_EMISSION");
        material.DisableKeyword("_NORMALMAP");
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>Listener.controller: one state per ListenerStates entry, no transitions, no parameters. Adds a missing state to an existing controller and leaves the rest alone.</summary>
    private static AnimatorController ListenerController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ListenerControllerPath);
        if (controller == null)
        {
            if (!AssetDatabase.IsValidFolder(ListenerAnimDir)) AssetDatabase.CreateFolder("Assets/SourceFiles/Animation", "Descent");
            controller = AnimatorController.CreateAnimatorControllerAtPath(ListenerControllerPath);
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach ((string state, string clip) in ListenerStates)
        {
            if (machine.states.Any(s => s.state.name == state)) continue;

            AnimatorState added = machine.AddState(state);
            added.motion = AssetDatabase.LoadAllAssetsAtPath(MixamoClipDir + clip + ".fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (added.motion == null) Debug.LogWarning($"InteractableKitBuilder: Mixamo clip {clip} not found - Listener state {state} is empty.");
            if (machine.defaultState == null || state == ListenerStates[0].state) machine.defaultState = added;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }
}
