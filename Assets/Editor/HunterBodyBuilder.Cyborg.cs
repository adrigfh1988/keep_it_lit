using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The hunter's second look: the "Sci-Fi Cyborg" (Assets/Cyborg, a git-ignored pack folder like the other art packs; its katana and
/// pistol are left out - the pistol is not even skinned to a bone). A Mixamo rig imported as Humanoid, so the hunter's Starter Assets
/// locomotion and Mixamo pose clips drive it exactly as they drive Timmy. Its PBR sets become URP Lit materials under
/// Assets/Cyborg/Materials (roughness baked into the metallic map's alpha, red emissive on the gear and headgear).
///
/// Which body the hunter wears is an Editor preference (<see cref="CurrentLook"/>), switched by KEEP IT LIT &gt; Build &gt; Hunter Body Look,
/// which rebuilds the hunter at once; Build Hunter Body and Check Project follow the selected look. The F121 breach hunter in the asylum hub
/// wears the same look, but only after Build Missing Pieces (switching the look leaves it stale; Check Project reports it). The cyborg is the default (11 Oct 2026).
/// </summary>
public static partial class HunterBodyBuilder
{
    public enum BodyLook { Orderly, Cyborg }

    private const string LookPref = "KeepItLit.HunterBodyLook";
    internal const string CyborgModelPath = "Assets/Cyborg/Cyborg_APose.fbx";
    private const string CyborgTextureFolder = "Assets/Cyborg/Textures";
    private const string CyborgMaterialFolder = "Assets/Cyborg/Materials";
    internal const string CyborgMaterialPrefix = "Cyborg_";
    private const float CyborgScale = 1f; // 1.94 m as modelled: a head taller than the player
    private static readonly string[] CyborgDropped = { "Katana", "base" }; // "base" is the pistol (mag, slide and trigger are its children)

    /// <summary>The look Build Hunter Body uses (Editor preference, default Cyborg since 11 Oct 2026).</summary>
    public static BodyLook CurrentLook
    {
        get => (BodyLook)EditorPrefs.GetInt(LookPref, (int)BodyLook.Cyborg);
        set => EditorPrefs.SetInt(LookPref, (int)value);
    }

    [MenuItem("KEEP IT LIT/Build/Hunter Body Look/Cyborg (test)", priority = 110)]
    public static void UseCyborg() { CurrentLook = BodyLook.Cyborg; BuildSilently(); }

    [MenuItem("KEEP IT LIT/Build/Hunter Body Look/Orderly robot", priority = 111)]
    public static void UseOrderly() { CurrentLook = BodyLook.Orderly; BuildSilently(); }

    [MenuItem("KEEP IT LIT/Build/Hunter Body Look/Cyborg (test)", true)]
    private static bool UseCyborgValidate() { Menu.SetChecked("KEEP IT LIT/Build/Hunter Body Look/Cyborg (test)", CurrentLook == BodyLook.Cyborg); return true; }

    [MenuItem("KEEP IT LIT/Build/Hunter Body Look/Orderly robot", true)]
    private static bool UseOrderlyValidate() { Menu.SetChecked("KEEP IT LIT/Build/Hunter Body Look/Orderly robot", CurrentLook == BodyLook.Orderly); return true; }

    /// <summary>The cyborg model imported as a valid Humanoid, with its materials made; null when the pack is missing or the avatar is not valid.</summary>
    internal static GameObject EnsureCyborgModel()
    {
        ModelImporter importer = AssetImporter.GetAtPath(CyborgModelPath) as ModelImporter;
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CyborgModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(CyborgModelPath)) if (o is Avatar a) avatar = a;
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"HunterBodyBuilder: {CyborgModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        PrepareCyborgTextures();
        return model;
    }

    /// <summary>The cyborg under <paramref name="parent"/>: unpacked, weapons removed, every Body slot on its URP material, on the avatar.</summary>
    internal static GameObject InstantiateCyborg(GameObject model, Transform parent)
    {
        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.transform.localScale = Vector3.one * CyborgScale;
        foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && System.Array.IndexOf(CyborgDropped, t.name) >= 0) Object.DestroyImmediate(t.gameObject);
        }
        foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = CyborgMaterial(mats[i].name) ?? mats[i];
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(CyborgModelPath)) if (o is Avatar a) animator.avatar = a;
        }
        return body;
    }

    // ---------------------------------------------------------------- materials

    /// <summary>Texture-file prefix for each of the model's material slots.</summary>
    private static readonly Dictionary<string, string> CyborgSets = new Dictionary<string, string>
    {
        { "Body", "Body" }, { "headgear", "headgear" }, { "gear", "gear" }, { "Gear 2", "Gear_2" },
        { "Vest and Belt", "Vest_and_Belt" }, { "Hood", "hood_Hood" }, { "Boot", "Boot" },
    };

    private static void PrepareCyborgTextures()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CyborgTextureFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            string file = Path.GetFileNameWithoutExtension(path);
            bool normal = file.Contains("Normal");
            bool data = normal || file.Contains("Metallic") || file.Contains("Roughness") || file.Contains("MetalSmooth");
            bool changed = false;
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
            if (!normal && ti.sRGBTexture == data) { ti.sRGBTexture = !data; changed = true; }
            if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; changed = true; }
            if (changed) ti.SaveAndReimport();
        }
    }

    private static Material CyborgMaterial(string slot)
    {
        if (!CyborgSets.TryGetValue(slot, out string prefix)) return null;
        if (!AssetDatabase.IsValidFolder(CyborgMaterialFolder)) AssetDatabase.CreateFolder("Assets/Cyborg", "Materials");
        string path = $"{CyborgMaterialFolder}/{CyborgMaterialPrefix}{prefix}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = CyborgMaterialPrefix + prefix };
        Texture2D albedo = CyborgTexture(prefix, "Base");
        Texture2D normal = CyborgTexture(prefix, "Normal");
        Texture2D emissive = CyborgTexture(prefix, "Emissive");
        Texture2D metalSmooth = BakeCyborgMetalSmooth(prefix);
        if (albedo != null) { mat.SetTexture("_BaseMap", albedo); mat.SetTexture("_MainTex", albedo); }
        mat.SetColor("_BaseColor", Color.white);
        if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_BumpScale", 1f); }
        if (metalSmooth != null)
        {
            mat.SetTexture("_MetallicGlossMap", metalSmooth);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
        }
        else mat.SetFloat("_Smoothness", 0.35f);
        if (emissive != null)
        {
            mat.SetTexture("_EmissionMap", emissive);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.white * 2f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>The set's texture whose name contains <paramref name="kind"/> (Base, Normal, Emissive, Metallic, Roughness), or null.</summary>
    private static Texture2D CyborgTexture(string prefix, string kind)
    {
        foreach (string guid in AssetDatabase.FindAssets(prefix + " t:Texture2D", new[] { CyborgTextureFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = Path.GetFileNameWithoutExtension(path);
            if (!file.StartsWith(prefix + "_") || !file.Contains(kind) || file.Contains("MetalSmooth")) continue;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return null;
    }

    /// <summary>R = metallic, A = 1 - roughness, at most 2048 px, written once as {prefix}_MetalSmooth.png (linear).</summary>
    private static Texture2D BakeCyborgMetalSmooth(string prefix)
    {
        string outPath = $"{CyborgTextureFolder}/{prefix}_MetalSmooth.png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        if (existing != null) return existing;
        Texture2D metal = CyborgTexture(prefix, "Metallic"), rough = CyborgTexture(prefix, "Roughness");
        if (metal == null && rough == null) return null;
        Texture2D m = metal != null ? ReadPng(AssetDatabase.GetAssetPath(metal)) : null;
        Texture2D r = rough != null ? ReadPng(AssetDatabase.GetAssetPath(rough)) : null;
        int size = Mathf.Min(2048, Mathf.Max(m != null ? m.width : 0, r != null ? r.width : 0));
        Color[] outPx = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float metallic = m != null ? m.GetPixelBilinear(u, v).r : 0f;
                float roughness = r != null ? r.GetPixelBilinear(u, v).r : 0.6f;
                outPx[y * size + x] = new Color(metallic, 0f, 0f, 1f - roughness);
            }
        }
        if (m != null) Object.DestroyImmediate(m);
        if (r != null) Object.DestroyImmediate(r);
        Texture2D bake = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        bake.SetPixels(outPx);
        bake.Apply();
        File.WriteAllBytes(outPath, bake.EncodeToPNG());
        Object.DestroyImmediate(bake);
        AssetDatabase.ImportAsset(outPath);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
        ti.sRGBTexture = false;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    private static Texture2D ReadPng(string assetPath)
    {
        Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        return t.LoadImage(File.ReadAllBytes(assetPath)) ? t : null;
    }
}
