using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// F92: the Karlheinz Asylum pack and the Morgue Room PBR pack ship on Built-in Standard shaders (and Standard
/// (Specular setup) for a handful), which render magenta in URP. This converts them in place, once - modelled on
/// <see cref="AlchemistMaterialUpgrade"/>. Only materials under the two pack folders are ever touched, anything already
/// on a URP shader is skipped, so running it again changes nothing. No menu item: AsylumHubBuilder calls Convert()
/// when NeedsConversion(), and Check Project flags it.
///
/// CapTextures gives everything the hub depends on a 1024 px import ceiling (web build size), except the tiled
/// floor/wall textures. The packs are third-party and git-ignored.
/// </summary>
public static class AsylumMaterialUpgrade
{
    public const string AsylumFolder = "Assets/Abandoned_Asylum";
    public const string MorgueFolder = "Assets/Morgue Room PBR";

    // The torch is live in the hub and a glossy surface flares straight back down the beam.
    private const float MaxSmoothness = 0.35f;
    private const int TextureCap = 1024;
    private const string TiledFolder = "Assets/Abandoned_Asylum/Textures/Tiled/";
    private const int TiledCap = 2048;
    private static int CapFor(string path) => path.StartsWith(TiledFolder) ? TiledCap : TextureCap;

    public static bool NeedsConversion() => NonUrpCount() > 0;

    /// <summary>Count of materials under the two packs that are not on a URP shader (the Audit reports this).</summary>
    public static int NonUrpCount()
    {
        int n = 0;
        foreach (Material material in PackMaterials())
        {
            if (material.shader == null || !material.shader.name.StartsWith("Universal Render Pipeline/")) n++;
        }
        return n;
    }

    /// <summary>Idempotent. Returns how many materials it changed.</summary>
    public static int Convert()
    {
        int converted = 0;
        int skipped = 0;
        StandardUpgrader standard = new StandardUpgrader("Standard");
        StandardUpgrader specular = new StandardUpgrader("Standard (Specular setup)");

        foreach (Material material in PackMaterials())
        {
            string shaderName = material.shader != null ? material.shader.name : "";
            if (shaderName.StartsWith("Universal Render Pipeline/")) { skipped++; continue; }

            if (shaderName != "Standard" && shaderName != "Standard (Specular setup)")
            {
                Debug.LogWarning($"AsylumMaterialUpgrade: no conversion for shader '{shaderName}' on {material.name}, skipped.", material);
                skipped++;
                continue;
            }

            // Read before the swap; the URP shader has no _Mode.
            float mode = material.HasProperty("_Mode") ? material.GetFloat("_Mode") : 0f;
            float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;

            (shaderName == "Standard" ? standard : specular).Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", Mathf.Min(material.GetFloat("_Smoothness"), MaxSmoothness));

            if (mode == 1f)
            {
                material.SetFloat("_Surface", 0f);
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", cutoff);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else if (mode >= 2f)
            {
                MakeTransparent(material);
            }
            if (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f) NoPreserveSpecular(material);

            EditorUtility.SetDirty(material);
            converted++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Asylum / Morgue materials: converted {converted}, skipped {skipped}.");
        return converted;
    }

    /// <summary>Texture import ceiling (1024) for each texture the given assets depend on that lives under the two packs. Idempotent.</summary>
    public static int CapTextures(IEnumerable<string> rootAssetPaths)
    {
        HashSet<string> seen = new HashSet<string>();
        int changed = 0;
        foreach (string root in rootAssetPaths)
        {
            foreach (string dep in AssetDatabase.GetDependencies(root, true))
            {
                if (!IsPackPath(dep) || !seen.Add(dep)) continue;
                if (!(AssetImporter.GetAtPath(dep) is TextureImporter importer)) continue;
                if (importer.maxTextureSize <= CapFor(dep)) continue;
                importer.maxTextureSize = CapFor(dep);
                importer.SaveAndReimport();
                changed++;
            }
        }
        Debug.Log($"Asylum / Morgue textures: capped {changed} (1024, tiled 2048).");
        return changed;
    }

    /// <summary>
    /// The pack ships 2-4k PNGs (1.6 GB for what the hub uses) and every one is imported at 1024 anyway, so the source files
    /// are downsized in place to 1024 (box-filtered on a linear render target, so normal and metallic maps keep their raw
    /// values). Idempotent: a file already at 1024 or below is skipped; the tiled floor/wall textures keep their size.
    /// Returns how many files it rewrote, within [skip, skip + take) of the dependency list.
    /// </summary>
    public static int ShrinkSources(string rootAssetPath, int skip, int take, out int total)
    {
        List<string> files = new List<string>();
        foreach (string dep in AssetDatabase.GetDependencies(rootAssetPath, true))
        {
            if (!IsPackPath(dep) || !(dep.EndsWith(".png") || dep.EndsWith(".tga"))) continue;
            files.Add(dep);
        }
        total = files.Count;

        int rewritten = 0;
        for (int i = skip; i < Mathf.Min(files.Count, skip + take); i++)
        {
            string path = files[i];
            if (path.EndsWith(".tga")) { if (ShrinkTga(path, CapFor(path))) rewritten++; continue; }
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!source.LoadImage(bytes)) { Object.DestroyImmediate(source); continue; }
            int cap = CapFor(path);
            if (source.width <= cap && source.height <= cap) { Object.DestroyImmediate(source); continue; }

            int w = Mathf.Min(source.width, cap), h = Mathf.Min(source.height, cap);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            source.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D small = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            small.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(path, small.EncodeToPNG());
            Object.DestroyImmediate(small);
            Object.DestroyImmediate(source);
            rewritten++;
        }
        return rewritten;
    }

    /// <summary>Box-filters an uncompressed true-colour TGA down to the cap in place (the Morgue Room PBR pack ships 2048 px TGAs). Keeps the .meta and GUID; false when untouched.</summary>
    private static bool ShrinkTga(string path, int cap)
    {
        byte[] d = System.IO.File.ReadAllBytes(path);
        if (d.Length < 18) return false;
        int idLen = d[0], type = d[2], w = d[12] | (d[13] << 8), h = d[14] | (d[15] << 8), bpp = d[16];
        if (d[1] != 0 || type != 2 || (bpp != 24 && bpp != 32)) { Debug.LogWarning($"Asylum: {path} is not an uncompressed true-colour TGA, left as it is."); return false; }
        if (w <= cap && h <= cap) return false;
        int px = bpp / 8, nw = Mathf.Min(w, cap), nh = Mathf.Min(h, cap);
        if (w % nw != 0 || h % nh != 0) return false;
        int fx = w / nw, fy = h / nh, start = 18 + idLen;
        if (d.Length < start + w * h * px) return false;
        byte[] o = new byte[18 + nw * nh * px];
        System.Array.Copy(d, o, 18);
        o[0] = 0; o[12] = (byte)(nw & 255); o[13] = (byte)(nw >> 8); o[14] = (byte)(nh & 255); o[15] = (byte)(nh >> 8);
        for (int y = 0; y < nh; y++)
        {
            for (int x = 0; x < nw; x++)
            {
                for (int c = 0; c < px; c++)
                {
                    int sum = 0;
                    for (int by = 0; by < fy; by++) { for (int bx = 0; bx < fx; bx++) sum += d[start + ((y * fy + by) * w + (x * fx + bx)) * px + c]; }
                    o[18 + (y * nw + x) * px + c] = (byte)((sum + fx * fy / 2) / (fx * fy));
                }
            }
        }
        System.IO.File.WriteAllBytes(path, o);
        return true;
    }

    /// <summary>Number of textures the dependencies of `root` still import above the cap (outside the tiled folder).</summary>
    public static int UncappedTextures(string root)
    {
        int n = 0;
        foreach (string dep in AssetDatabase.GetDependencies(root, true))
        {
            if (!IsPackPath(dep)) continue;
            if (AssetImporter.GetAtPath(dep) is TextureImporter importer && importer.maxTextureSize > CapFor(dep)) n++;
        }
        return n;
    }

    public static bool IsPackPath(string path) => path.StartsWith(AsylumFolder + "/") || path.StartsWith(MorgueFolder + "/");

    // ---------------------------------------------------------------- pieces

    /// <summary>The property set the URP material inspector writes for an alpha-blended surface.</summary>
    private static void MakeTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.renderQueue = (int)RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
    }

    /// <summary>Project gotcha: URP re-validates a transparent material to premultiplied alpha unless this is 0, and the torch specular then lights a whole decal quad as a pale rectangle.</summary>
    private static void NoPreserveSpecular(Material material)
    {
        if (material.HasProperty("_BlendModePreserveSpecular")) material.SetFloat("_BlendModePreserveSpecular", 0f);
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
    }

    private static IEnumerable<Material> PackMaterials()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { AsylumFolder, MorgueFolder }))
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material != null) yield return material;
        }
    }
}
