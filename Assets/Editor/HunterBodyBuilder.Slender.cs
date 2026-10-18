using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The custom tall, gaunt "Slender Hunter" body (<c>Assets/SourceFiles/Characters/Hunter/Hunter_Slender.fbx</c>):
/// 2.67 m tall humanoid in a weathered Variant C pinstripe asylum coat with forged wrist chains, cranial surgical
/// staples, dorsal vertebral clamps, and glowing pure-white eyes inside deep pitch-black sockets that turn
/// completely OFF when unlit via <see cref="AIPresence"/>.
/// </summary>
public static partial class HunterBodyBuilder
{
    internal const string SlenderModelPath = "Assets/SourceFiles/Characters/Hunter/Hunter_Slender.fbx";
    private const string SlenderTextureFolder = "Assets/SourceFiles/Characters/Hunter/Textures";
    private const string SlenderMaterialFolder = "Assets/SourceFiles/Characters/Hunter/Materials";
    internal const string SlenderMaterialPrefix = "HunterSlender_";
    private const float SlenderScale = 1f; // 2.67 m tall as authored in Blender

    [MenuItem("KEEP IT LIT/Build/Hunter Body Look/Slender Hunter (default)", priority = 109)]
    public static void UseSlender() { CurrentLook = BodyLook.Slender; BuildSilently(); }

    [MenuItem("KEEP IT LIT/Build/Hunter Body Look/Slender Hunter (default)", true)]
    private static bool UseSlenderValidate()
    {
        Menu.SetChecked("KEEP IT LIT/Build/Hunter Body Look/Slender Hunter (default)", CurrentLook == BodyLook.Slender);
        return true;
    }

    internal static GameObject EnsureSlenderModel()
    {
        ModelImporter importer = AssetImporter.GetAtPath(SlenderModelPath) as ModelImporter;
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human
            || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
            || importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(SlenderModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(SlenderModelPath))
        {
            if (o is Avatar a) avatar = a;
        }
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"HunterBodyBuilder: {SlenderModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        PrepareSlenderTextures();
        return model;
    }

    internal static GameObject InstantiateSlender(GameObject model, Transform parent)
    {
        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.transform.localScale = Vector3.one * SlenderScale;

        foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(c);
        }

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string slotName = mats[i] != null ? mats[i].name : r.gameObject.name;
                mats[i] = SlenderMaterial(slotName, r.gameObject.name) ?? mats[i];
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(SlenderModelPath))
            {
                if (o is Avatar a) animator.avatar = a;
            }
        }

        HunterChainPhysics chainPhysics = body.GetComponent<HunterChainPhysics>();
        if (chainPhysics == null) chainPhysics = body.AddComponent<HunterChainPhysics>();
        chainPhysics.PoseAtRest();

        return body;
    }

    private struct SlenderMatSpec
    {
        public string TextureFile;
        public float Smoothness;
        public float Metallic;
        public bool IsEmissiveEyes;
    }

    private static readonly Dictionary<string, SlenderMatSpec> SlenderSpecs = new Dictionary<string, SlenderMatSpec>
    {
        { "Hunter_Head",      new SlenderMatSpec { TextureFile = "Hunter_Head_Base.png",      Smoothness = 0.24f, Metallic = 0.00f } },
        { "Hunter_Skin",      new SlenderMatSpec { TextureFile = "Hunter_Skin_Base.png",      Smoothness = 0.24f, Metallic = 0.00f } },
        { "Hunter_Coat",      new SlenderMatSpec { TextureFile = "Hunter_Coat_Base.png",      Smoothness = 0.14f, Metallic = 0.00f } },
        { "Hunter_Leather",   new SlenderMatSpec { TextureFile = "Hunter_Leather_Base.png",   Smoothness = 0.38f, Metallic = 0.05f } },
        { "Hunter_Metal",     new SlenderMatSpec { TextureFile = "Hunter_Metal_Base.png",     Smoothness = 0.56f, Metallic = 0.85f } },
        { "Hunter_BoneSpine", new SlenderMatSpec { TextureFile = "Hunter_BoneSpine_Base.png", Smoothness = 0.32f, Metallic = 0.00f } },
        { "Hunter_VeinScar",  new SlenderMatSpec { TextureFile = "Hunter_VeinScar_Base.png",  Smoothness = 0.65f, Metallic = 0.00f } },
        { "Hunter_Eyes",      new SlenderMatSpec { TextureFile = null,                        Smoothness = 0.00f, Metallic = 0.00f, IsEmissiveEyes = true } },
    };

    private static void PrepareSlenderTextures()
    {
        if (!AssetDatabase.IsValidFolder(SlenderTextureFolder)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SlenderTextureFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            bool changed = false;
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; changed = true; }
            if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; changed = true; }
            if (changed) ti.SaveAndReimport();
        }
    }

    private static Material SlenderMaterial(string slotName, string meshName)
    {
        string key = slotName;
        if (!SlenderSpecs.ContainsKey(key))
        {
            if (meshName.Contains("Eye")) key = "Hunter_Eyes";
            else if (meshName.Contains("Head")) key = "Hunter_Head";
            else if (meshName.Contains("Hand")) key = "Hunter_Skin";
            else if (meshName.Contains("Coat")) key = "Hunter_Coat";
            else if (meshName.Contains("Leather")) key = "Hunter_Leather";
            else if (meshName.Contains("Metal")) key = "Hunter_Metal";
            else if (meshName.Contains("Spine")) key = "Hunter_BoneSpine";
            else if (meshName.Contains("Vein")) key = "Hunter_VeinScar";
        }
        if (!SlenderSpecs.TryGetValue(key, out SlenderMatSpec spec)) return null;

        EnsureFolder(SlenderMaterialFolder);
        string path = $"{SlenderMaterialFolder}/{SlenderMaterialPrefix}{key}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = SlenderMaterialPrefix + key };
            AssetDatabase.CreateAsset(mat, path);
        }

        if (spec.IsEmissiveEyes)
        {
            // Pitch-black base color so when AIPresence scales _EmissionColor to 0, the eyes vanish completely into the dark sockets.
            mat.SetColor("_BaseColor", Color.black);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.white * 4f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            Texture2D albedo = !string.IsNullOrEmpty(spec.TextureFile)
                ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{SlenderTextureFolder}/{spec.TextureFile}")
                : null;
            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTexture("_MainTex", albedo);
            }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", spec.Smoothness);
            mat.SetFloat("_Metallic", spec.Metallic);
            mat.SetInt("_Cull", 0); // Render both sides on open coat tails and straps
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }
}
