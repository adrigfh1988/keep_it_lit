using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared URP Lit material creation and texture loading helpers used across KEEP IT LIT editor builders.
/// </summary>
public static class BuilderMaterials
{
    public static Texture2D Texture(string path)
    {
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Loads a texture and marks its importer as a normal map if it isn't already.</summary>
    public static Texture2D NormalTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>
    /// Loads a material at <c>{folder}/Materials/{name}.mat</c> if it already exists, or creates a URP Lit material with optional texture/normal/tiling.
    /// </summary>
    public static Material LoadOrCreateTiledMat(string folder, string name, Color color, Texture2D texture, Texture2D normal, Vector2 tiling, Color? emission, float smoothness = 0.25f)
    {
        string matFolder = $"{folder}/Materials";
        EditorAssetUtil.EnsureFolder(matFolder);
        string path = $"{matFolder}/{name}.mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

        if (texture != null)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", tiling);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", tiling);
            }
        }

        if (normal != null && material.HasProperty("_BumpMap"))
        {
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BumpMap", tiling);
        }

        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// Loads a material at <c>{materialFolder}/{name}.mat</c> if it already exists, or creates a URP Lit material.
    /// </summary>
    public static Material LoadOrCreateMat(string materialFolder, string name, Color color, Color? emission, float smoothness)
    {
        EditorAssetUtil.EnsureFolder(materialFolder);
        string path = $"{materialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
