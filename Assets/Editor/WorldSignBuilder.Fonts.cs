using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>F109: static TMP font assets (one per family) and the lit text material of each. See plannings/world-signs-plan.md D2/D3.</summary>
public static partial class WorldSignBuilder
{
    public enum SignFont { Barlow, Stencil, RockSalt, Reenie, IMFell, Cinzel }

    private const string FontFolder = "Assets/SourceFiles/Fonts/";
    private const string MaterialFolder = "Assets/SourceFiles/Materials/Signs/";
    private const string LitGraphPath = "Assets/TextMesh Pro/Shaders/TMP_SDF-URP Lit.shadergraph";
    private const string LiberationPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    private static readonly (SignFont font, string folder, string file, string asset)[] FontTable =
    {
        (SignFont.Barlow, "BarlowCondensed", "BarlowCondensed-SemiBold.ttf", "Barlow Condensed SDF"),
        (SignFont.Stencil, "SairaStencilOne", "SairaStencilOne-Regular.ttf", "Saira Stencil One SDF"),
        (SignFont.RockSalt, "RockSalt", "RockSalt-Regular.ttf", "Rock Salt SDF"),
        (SignFont.Reenie, "ReenieBeanie", "ReenieBeanie.ttf", "Reenie Beanie SDF"),
        (SignFont.IMFell, "IMFellEnglish", "IMFeENrm28P.ttf", "IM Fell English SDF"),
        (SignFont.Cinzel, "Cinzel", "Cinzel-VariableFont_wght.ttf", "Cinzel SDF"),
    };

    private static string CharacterSet()
    {
        var sb = new System.Text.StringBuilder();
        for (int c = 32; c <= 126; c++) sb.Append((char)c);
        for (int c = 160; c <= 255; c++) sb.Append((char)c);
        sb.Append("\u0152\u0153\u2018\u2019\u201C\u201D\u2013\u2014\u2026\u00B7");
        return sb.ToString();
    }

    private static string FontAssetPath(SignFont f)
    {
        foreach (var e in FontTable) if (e.font == f) return FontFolder + e.folder + "/" + e.asset + ".asset";
        return null;
    }

    private static string MaterialPath(SignFont f) => MaterialFolder + "SignText_" + f + ".mat";

    /// <summary>True when every font asset and its lit text material exist.</summary>
    public static bool FontsPresent()
    {
        foreach (var e in FontTable)
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath(e.font)) == null) return false;
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(e.font)) == null) return false;
        }
        return true;
    }

    /// <summary>Creates the missing static font assets and lit text materials. Idempotent.</summary>
    public static void EnsureFonts()
    {
        var liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationPath);
        foreach (var e in FontTable)
        {
            string path = FontAssetPath(e.font);
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) == null)
            {
                Font ttf = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + e.folder + "/" + e.file);
                if (ttf == null) { Debug.LogError("[WorldSigns] missing font file " + e.file); continue; }
                TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
                fa.name = e.asset;
                fa.TryAddCharacters(CharacterSet(), out string missing);
                fa.atlasPopulationMode = AtlasPopulationMode.Static;
                if (liberation != null)
                {
                    fa.fallbackFontAssetTable = new List<TMP_FontAsset> { liberation };
                }
                AssetDatabase.CreateAsset(fa, path);
                fa.material.name = e.asset + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
                foreach (Texture2D tex in fa.atlasTextures)
                {
                    tex.name = e.asset + " Atlas";
                    AssetDatabase.AddObjectToAsset(tex, fa);
                }
                EditorUtility.SetDirty(fa);
                AssetDatabase.SaveAssets();
                Debug.Log("[WorldSigns] font " + e.asset + ": glyphs " + fa.characterTable.Count + (string.IsNullOrEmpty(missing) ? "" : ", missing '" + missing + "' (fallback: LiberationSans)"));
            }
            EnsureTextMaterial(e.font);
        }
        AssetDatabase.SaveAssets();
    }

    public static TMP_FontAsset FontAsset(SignFont f) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath(f));

    public static Material TextMaterial(SignFont f) => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(f));

    private static void EnsureTextMaterial(SignFont f)
    {
        string path = MaterialPath(f);
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            if (existing.GetFloat("_ScaleRatioA") < 0.01f) { existing.SetFloat("_ScaleRatioA", 0.9f); EditorUtility.SetDirty(existing); }
            if (existing.GetTexture("_FaceTex") == null) { existing.SetTexture("_FaceTex", GrimeFaceTexture()); existing.SetVector("_FaceTex_ST", new Vector4(0.6f, 0.6f, 0f, 0f)); EditorUtility.SetDirty(existing); }
            return;
        }
        TMP_FontAsset fa = FontAsset(f);
        Shader lit = AssetDatabase.LoadAssetAtPath<Shader>(LitGraphPath);
        if (fa == null || lit == null) { Debug.LogError("[WorldSigns] cannot make text material for " + f); return; }
        Material m = new Material(lit) { name = "SignText_" + f };
        m.SetTexture("_MainTex", fa.atlasTextures[0]);
        m.SetFloat("_GradientScale", fa.atlasPadding + 1);
        m.SetFloat("_ScaleRatioA", (fa.atlasPadding + 1 - 1f) / (fa.atlasPadding + 1)); // TMP computes this in its material editors only; 0 makes the text invisible
        m.SetColor("_FaceColor", Color.white);
        Texture2D grime = GrimeFaceTexture();
        if (grime != null && m.HasProperty("_FaceTex"))
        {
            m.SetTexture("_FaceTex", grime);
            m.SetVector("_FaceTex_ST", new Vector4(0.6f, 0.6f, 0f, 0f));
        }
        AssetDatabase.CreateAsset(m, path);
    }
}
