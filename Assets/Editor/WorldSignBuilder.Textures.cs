using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>F109: textures generated in code (enamel, brass, paper, slate, wood, steel, light panel, grime, drips, overspray ...) and the URP materials made from them.</summary>
public static partial class WorldSignBuilder
{
    private const string TextureFolder = "Assets/SourceFiles/Textures/Signs/";

    // ---------------------------------------------------------------- noise

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static float VNoise(float x, float y, int px, int py, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int xa = ((x0 % px) + px) % px, xb = (((x0 + 1) % px) + px) % px;
        int ya = ((y0 % py) + py) % py, yb = (((y0 + 1) % py) + py) % py;
        float a = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
        float b = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    /// <summary>Tileable fbm; (u,v) in 0..1, basePeriod lattice cells across.</summary>
    private static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        int p = basePeriod;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * VNoise(u * p, v * p, p, p, seed + o * 17);
            norm += amp;
            amp *= 0.5f;
            p *= 2;
        }
        return sum / norm;
    }

    // ---------------------------------------------------------------- texture IO

    private static Texture2D MakeTexture(string name, int w, int h, System.Func<float, float, Color> pixel, bool alpha = false, bool srgb = true)
    {
        string path = TextureFolder + name + ".png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(TextureFolder);
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / h);
        t.SetPixels(px);
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.sRGBTexture = srgb;
        ti.alphaIsTransparency = alpha;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Bilinear;
        ti.anisoLevel = 4;
        ti.maxTextureSize = 512;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color Rgb(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

    public static Texture2D GrimeFaceTexture()
    {
        return MakeTexture("SignFaceGrime", 256, 256, (u, v) =>
        {
            float n = Fbm(u, v, 6, 4, 3);
            float speck = Mathf.Clamp01((Fbm(u, v, 24, 2, 9) - 0.62f) * 6f);
            float g = Mathf.Lerp(0.8f, 1f, Mathf.SmoothStep(0.25f, 0.8f, n)) * (1f - 0.3f * speck);
            return Rgb(g, g, g * 0.96f);
        });
    }

    private static Texture2D EnamelTex()
    {
        return MakeTexture("Sign_Enamel", 512, 512, (u, v) =>
        {
            float n = Fbm(u, v, 4, 4, 11);
            Color c = Rgb(0.88f, 0.88f, 0.83f) * (0.9f + 0.12f * n);
            float stain = Mathf.Clamp01((Fbm(u, v, 3, 3, 21) - 0.55f) * 4f);
            c = Color.Lerp(c, Rgb(0.55f, 0.46f, 0.3f), stain * 0.3f);
            float chip = Mathf.Clamp01((Fbm(u, v, 40, 2, 31) - 0.7f) * 10f) * Mathf.Clamp01((Fbm(u, v, 3, 2, 41) - 0.5f) * 5f);
            c = Color.Lerp(c, Rgb(0.26f, 0.2f, 0.17f), chip);
            float rust = Mathf.Clamp01((Fbm(u, v, 12, 2, 51) - 0.7f) * 8f);
            c = Color.Lerp(c, Rgb(0.45f, 0.24f, 0.1f), rust * 0.3f);
            c.a = 1f;
            return c;
        });
    }

    private static Texture2D SteelTex()
    {
        return MakeTexture("Sign_Steel", 256, 256, (u, v) =>
        {
            float brushed = VNoise(u * 4, v * 128, 4, 128, 5);
            float n = Fbm(u, v, 4, 3, 6);
            float g = 0.34f + 0.12f * brushed + 0.1f * n;
            float rust = Mathf.Clamp01((Fbm(u, v, 8, 3, 7) - 0.62f) * 5f);
            Color c = Color.Lerp(Rgb(g, g * 1.02f, g * 1.05f), Rgb(0.4f, 0.2f, 0.09f), rust * 0.7f);
            c.a = 1f;
            return c;
        });
    }

    private static Texture2D BrassTex()
    {
        return MakeTexture("Sign_Brass", 512, 512, (u, v) =>
        {
            float brushed = VNoise(u * 2, v * 96, 2, 96, 15);
            float n = Fbm(u, v, 3, 4, 16);
            Color c = Rgb(0.6f, 0.44f, 0.18f) * (0.72f + 0.2f * brushed + 0.25f * n);
            float tarnish = Mathf.Clamp01((Fbm(u, v, 5, 3, 17) - 0.5f) * 3f);
            c = Color.Lerp(c, Rgb(0.2f, 0.15f, 0.08f), tarnish * 0.55f);
            float verd = Mathf.Clamp01((Fbm(u, v, 4, 3, 18) - 0.64f) * 7f);
            c = Color.Lerp(c, Rgb(0.2f, 0.5f, 0.4f), verd * 0.8f);
            c.a = 1f;
            return c;
        });
    }

    private static Texture2D PaperTex()
    {
        return MakeTexture("Sign_Paper", 512, 512, (u, v) =>
        {
            float n = Fbm(u, v, 5, 4, 25);
            Color c = Rgb(0.86f, 0.8f, 0.63f) * (0.9f + 0.15f * n);
            float stain = Mathf.Clamp01((Fbm(u, v, 3, 3, 26) - 0.5f) * 3f);
            c = Color.Lerp(c, Rgb(0.55f, 0.43f, 0.25f), stain * 0.35f);
            float crease = Mathf.Exp(-Mathf.Pow((v - 0.5f) * 160f, 2f)) * 0.2f + Mathf.Exp(-Mathf.Pow((u - 0.33f) * 120f, 2f)) * 0.12f;
            c *= 1f - crease * 0f;
            c.a = 1f;
            return c;
        });
    }

    private static Texture2D SlateTex()
    {
        return MakeTexture("Sign_Slate", 512, 512, (u, v) =>
        {
            float n = Fbm(u, v, 6, 4, 35);
            float g = 0.1f + 0.06f * n;
            float smudge = Mathf.Clamp01((Fbm(u, v, 3, 3, 36) - 0.5f) * 2.5f) * 0.1f;
            float scratch = Mathf.Clamp01((VNoise(u * 40, v * 3, 40, 3, 37) - 0.86f) * 6f) * 0.08f;
            g += smudge + scratch;
            return Rgb(g, g * 1.02f, g);
        });
    }

    private static Texture2D WoodTex()
    {
        return MakeTexture("Sign_Wood", 512, 512, (u, v) =>
        {
            float warp = Fbm(u, v, 3, 3, 45) * 3f;
            float ring = Mathf.Abs(Mathf.Sin((v * 14f + warp) * Mathf.PI));
            float fine = VNoise(u * 3, v * 120, 3, 120, 46);
            Color dark = Rgb(0.2f, 0.12f, 0.06f), light = Rgb(0.4f, 0.26f, 0.13f);
            Color c = Color.Lerp(dark, light, 0.25f + 0.45f * ring * (0.6f + 0.4f * fine));
            float dirt = Mathf.Clamp01((Fbm(u, v, 4, 3, 47) - 0.55f) * 3f);
            c = Color.Lerp(c, Rgb(0.08f, 0.07f, 0.06f), dirt * 0.5f);
            c.a = 1f;
            return c;
        });
    }

    private static Texture2D LightPanelTex()
    {
        return MakeTexture("Sign_LightPanel", 256, 256, (u, v) =>
        {
            float n = Fbm(u, v, 4, 3, 55);
            Color c = Rgb(1f, 0.93f, 0.72f) * (0.78f + 0.3f * n);
            float dirt = Mathf.Clamp01((Fbm(u, v, 5, 3, 56) - 0.52f) * 3f);
            c = Color.Lerp(c, Rgb(0.7f, 0.55f, 0.3f), dirt * 0.3f);
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            c *= Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 0.12f, edge));
            c.a = 1f;
            return c;
        });
    }

    private static Texture2D OversprayTex()
    {
        return MakeTexture("Sign_Overspray", 256, 256, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float d = Mathf.Sqrt(dx * dx * 0.55f + dy * dy * 1.6f);
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (0.55f + 0.7f * Fbm(u, v, 24, 2, 65));
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }, true);
    }

    private static Texture2D DripsTex()
    {
        // 16 drip columns hanging from the top edge (v = 1), random length, round tips.
        float[] len = new float[16], off = new float[16];
        var rng = new System.Random(77);
        for (int i = 0; i < 16; i++) { len[i] = 0.12f + (float)rng.NextDouble() * 0.6f; off[i] = (float)rng.NextDouble() * 0.02f; }
        return MakeTexture("Sign_Drips", 256, 256, (u, v) =>
        {
            float top = 1f - v;
            int col = Mathf.Clamp(Mathf.FloorToInt(u * 16f), 0, 15);
            float cx = (col + 0.5f + (off[col] - 0.01f) * 16f) / 16f;
            float w = 0.006f + 0.004f * (col % 3);
            float dxx = Mathf.Abs(u - cx);
            float a = 0f;
            if (top < len[col] && dxx < w) a = 1f;
            else if (top >= len[col] && top < len[col] + w * 2.2f)
            {
                float rr = (top - len[col]) / (w * 2.2f);
                a = dxx < w * Mathf.Sqrt(Mathf.Max(0f, 1f - rr * rr)) ? 1f : 0f;
            }
            float band = Mathf.Clamp01((0.06f - top) / 0.06f);
            a = Mathf.Max(a * 0.9f, band * 0.7f * (0.6f + 0.4f * Fbm(u, v, 12, 2, 75)));
            return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }, true);
    }

    private static Texture2D RustStreakTex()
    {
        return MakeTexture("Sign_RustStreak", 64, 128, (u, v) =>
        {
            float top = 1f - v;
            float dx = Mathf.Abs(u - 0.5f);
            float w = Mathf.Lerp(0.18f, 0.06f, top) * (0.7f + 0.6f * VNoise(0f, top * 6f, 1, 6, 85));
            float a = Mathf.Clamp01(1f - dx / w) * Mathf.Clamp01(1.1f - top);
            a *= 0.8f;
            return new Color(0.55f, 0.27f, 0.1f, a * a);
        }, true);
    }

    private static Texture2D TapeTex()
    {
        return MakeTexture("Sign_Tape", 64, 32, (u, v) =>
        {
            float edge = Mathf.Min(Mathf.Min(u, 1f - u) * 6f, 1f);
            float n = VNoise(u * 8, v * 8, 8, 8, 95);
            return new Color(0.96f, 0.94f, 0.82f, (0.7f + 0.1f * n) * edge);
        }, true);
    }

    // ---------------------------------------------------------------- materials

    private static Material MakeMat(string name, Texture2D albedo, Color tint, float smoothness, float metallic, bool transparent = false, Texture2D emission = null, Color? emissionColor = null)
    {
        string path = MaterialFolder + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(MaterialFolder);
        Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        if (albedo != null) { m.SetTexture("_BaseMap", albedo); m.SetTexture("_MainTex", albedo); }
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", 5f);
            m.SetFloat("_DstBlend", 10f);
            m.SetFloat("_SrcBlendAlpha", 1f);
            m.SetFloat("_DstBlendAlpha", 10f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            m.SetShaderPassEnabled("ShadowCaster", false);
            if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
        }
        if (emission != null)
        {
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", emission);
            m.SetColor("_EmissionColor", emissionColor ?? Color.white);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    public static Material EnamelMat => MakeMat("Sign_Enamel", EnamelTex(), Color.white, 0.55f, 0f);
    public static Material EnamelEdgeMat => MakeMat("Sign_EnamelEdge", SteelTex(), new Color(0.6f, 0.6f, 0.6f), 0.35f, 0.6f);
    public static Material SteelMat => MakeMat("Sign_Steel", SteelTex(), Color.white, 0.4f, 0.7f);
    public static Material BrassMat => MakeMat("Sign_Brass", BrassTex(), Color.white, 0.5f, 0.75f);
    public static Material BrassEdgeMat => MakeMat("Sign_BrassEdge", BrassTex(), new Color(0.5f, 0.45f, 0.4f), 0.35f, 0.7f);
    public static Material PaperMat => MakeMat("Sign_Paper", PaperTex(), Color.white, 0.08f, 0f);
    public static Material SlateMat => MakeMat("Sign_Slate", SlateTex(), Color.white, 0.12f, 0f);
    public static Material WoodMat => MakeMat("Sign_Wood", WoodTex(), Color.white, 0.18f, 0f);
    public static Material WoodDarkMat => MakeMat("Sign_WoodDark", WoodTex(), new Color(0.6f, 0.55f, 0.5f), 0.15f, 0f);
    public static Material LightPanelMat => MakeMat("Sign_LightPanel", LightPanelTex(), Color.white, 0.2f, 0f, false, LightPanelTex(), new Color(1.7f, 1.45f, 0.95f));
    public static Material RustStreakMat => MakeMat("Sign_RustStreak", RustStreakTex(), Color.white, 0f, 0f, true);
    public static Material TapeMat => MakeMat("Sign_Tape", TapeTex(), Color.white, 0f, 0f, true);

    public static Material OversprayMat(string colourName, Color c) => MakeMat("Sign_Overspray_" + colourName, OversprayTex(), new Color(c.r, c.g, c.b, 0.45f), 0f, 0f, true);
    public static Material DripsMat(string colourName, Color c) => MakeMat("Sign_Drips_" + colourName, DripsTex(), new Color(c.r, c.g, c.b, 0.9f), 0f, 0f, true);
    public static Material PatchMat => MakeMat("Sign_Patch", PatchTex(), new Color(0.5f, 0.55f, 0.5f, 1f), 0f, 0f, true);
}

public static partial class WorldSignBuilder
{
    /// <summary>An opaque rounded patch with a ragged edge (wall paint covering a letter).</summary>
    private static Texture2D PatchTex()
    {
        return MakeTexture("Sign_PatchRect", 128, 128, (u, v) =>
        {
            float dx = Mathf.Abs(u - 0.5f) * 2f, dy = Mathf.Abs(v - 0.5f) * 2f;
            float d = Mathf.Max(dx, dy);
            float n = Fbm(u, v, 8, 2, 105);
            float a = Mathf.Clamp01((0.92f - d + (n - 0.5f) * 0.3f) * 8f);
            float shade = 0.85f + 0.15f * Fbm(u, v, 6, 3, 106);
            return new Color(shade, shade, shade, a);
        }, true);
    }
}
