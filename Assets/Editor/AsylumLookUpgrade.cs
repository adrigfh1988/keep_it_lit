using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F95: ages the hub's structural surfaces. The pack's plaster and tiles read too white and too clean under the torch, so the
/// wall / ceiling / floor-tile materials of the Karlheinz Asylum and the Morgue Room PBR pack get a darker, slightly greyer base
/// colour, less gloss on plaster (a little kept on tiles so the reflection probes give them a wet sheen) and a tiling grime
/// detail layer (URP Lit Detail Inputs: albedo x2 + normal).
///
/// The grime is generated once, procedurally (tileable fBm, no downloads), as <c>Grime_Detail.png</c> and
/// <c>Grime_DetailNormal.png</c> under Assets/SourceFiles/Textures/Asylum. Table-driven like <see cref="ThemeSurfaceUpgrade"/>.
/// Idempotent: a material whose detail albedo already is the grime texture counts as aged and is never touched again, so a hand
/// tweak survives. Materials also used by the scene outside the hub (the maze / shop / Intake) are skipped. No menu item: it runs
/// from AsylumHubBuilder.LookRepair and Check Project flags it. The pack folders are git-ignored; only force-added .mat files
/// show up as modified (see the F95 notes in CLAUDE.md).
/// </summary>
public static class AsylumLookUpgrade
{
    public const string TextureFolder = "Assets/SourceFiles/Textures/Asylum";
    public const string GrimeAlbedoPath = TextureFolder + "/Grime_Detail.png";
    public const string GrimeNormalPath = TextureFolder + "/Grime_DetailNormal.png";
    private const int GrimeSize = 512;

    private enum Kind { Plaster, Tile }

    private struct Row
    {
        public string Name;       // material asset name (unique inside its pack folder)
        public Kind Kind;
        public float DetailTile;  // repeats of the grime over the material's own UV 0..1
    }

    private static readonly string[] Folders = { "Assets/Abandoned_Asylum/Materials", "Assets/Morgue Room PBR/Models" };

    // Plaster: base x 0.72, saturation -15 %, smoothness <= 0.2. Tile: base x 0.8, saturation -15 %, smoothness 0.4.
    // Detail tiling is set against each material's own base tiling so the grime is coarser than the texture it sits on.
    private static readonly Row[] Rows =
    {
        P("Wall_1", 1.6f), P("Wall_2", 2f), P("Wall_3", 2f), P("Wall_4", 2f), P("Wall_6", 4f), P("Wall_7", 1.6f),
        P("wall_piece_mat01", 1f), P("wall_piece_mat02", 1f), P("ceiling_01_mat", 1f),
        T("Tiles_1", 1.6f), T("Tiles_2", 5f), T("Tiles_3", 5f), T("Tiles_4", 1.6f), T("Tiles_5", 3f), T("Tiles_6", 1.6f),
        T("Tiles_7", 1f), T("Tiles_8", 1.6f), T("Tiles_9", 1.6f), T("Tiles_10", 2.5f), T("Tiles_11", 1f), T("Tiles_12", 1.6f),
        T("Tiles_13", 1.6f), T("floor_01_mat", 1f),
    };

    private static Row P(string name, float tile) => new Row { Name = name, Kind = Kind.Plaster, DetailTile = tile };
    private static Row T(string name, float tile) => new Row { Name = name, Kind = Kind.Tile, DetailTile = tile };

    /// <summary>True when at least one hub material in the table has not been aged yet (or the grime textures are missing).</summary>
    public static bool NeedsAging()
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(GrimeAlbedoPath) == null) return true;
        Texture2D grime = AssetDatabase.LoadAssetAtPath<Texture2D>(GrimeAlbedoPath);
        foreach (Material m in TargetMaterials(null))
        {
            if (m.GetTexture("_DetailAlbedoMap") != grime) return true;
        }
        return false;
    }

    /// <summary>Count of table materials not aged yet (Check Project's report).</summary>
    public static int UnagedCount()
    {
        Texture2D grime = AssetDatabase.LoadAssetAtPath<Texture2D>(GrimeAlbedoPath);
        int n = 0;
        foreach (Material m in TargetMaterials(null))
        {
            if (grime == null || m.GetTexture("_DetailAlbedoMap") != grime) n++;
        }
        return n;
    }

    /// <summary>Idempotent. Returns how many materials it aged.</summary>
    public static int Apply(Transform hubRoot)
    {
        EnsureGrimeTextures();
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(GrimeAlbedoPath);
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(GrimeNormalPath);
        if (albedo == null || normal == null) { Debug.LogError("AsylumLookUpgrade: the grime textures could not be created."); return 0; }

        HashSet<Material> outside = MaterialsOutsideHub(hubRoot);
        int aged = 0;
        int skippedShared = 0;
        foreach (KeyValuePair<Material, Row> pair in Targets())
        {
            Material m = pair.Key;
            Row row = pair.Value;
            if (m.GetTexture("_DetailAlbedoMap") == albedo) continue; // already aged
            if (outside.Contains(m)) { skippedShared++; Debug.LogWarning($"AsylumLookUpgrade: {m.name} is also used outside the hub, skipped.", m); continue; }

            Color c = m.GetColor("_BaseColor");
            Color.RGBToHSV(c, out float h, out float s, out float v);
            float value = v * (row.Kind == Kind.Tile ? 0.8f : 0.72f);
            Color aged2 = Color.HSVToRGB(h, s * 0.85f, value);
            aged2.a = c.a;
            m.SetColor("_BaseColor", aged2);

            float smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.3f;
            m.SetFloat("_Smoothness", row.Kind == Kind.Tile ? 0.4f : Mathf.Min(smooth, 0.2f));

            m.SetTexture("_DetailAlbedoMap", albedo);
            m.SetTexture("_DetailNormalMap", normal);
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(row.DetailTile, row.DetailTile));
            m.SetFloat("_DetailAlbedoMapScale", 1f);
            m.SetFloat("_DetailNormalMapScale", 0.6f);
            m.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(m);
            aged++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"AsylumLookUpgrade: aged {aged} materials ({skippedShared} skipped because they are used outside the hub).");
        return aged;
    }

    // ---------------------------------------------------------------- targets

    private static IEnumerable<Material> TargetMaterials(HashSet<Material> exclude)
    {
        foreach (KeyValuePair<Material, Row> pair in Targets())
        {
            if (exclude == null || !exclude.Contains(pair.Key)) yield return pair.Key;
        }
    }

    private static IEnumerable<KeyValuePair<Material, Row>> Targets()
    {
        foreach (Row row in Rows)
        {
            foreach (string guid in AssetDatabase.FindAssets(row.Name + " t:Material", Folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != row.Name) continue;
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline/Lit")) continue;
                yield return new KeyValuePair<Material, Row>(m, row);
            }
        }
    }

    /// <summary>Every material on a renderer in the open scenes that is not under the hub (maze rows, shop, Intake...).</summary>
    private static HashSet<Material> MaterialsOutsideHub(Transform hubRoot)
    {
        HashSet<Material> set = new HashSet<Material>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (hubRoot != null && r.transform.IsChildOf(hubRoot)) continue;
            foreach (Material m in r.sharedMaterials) if (m != null) set.Add(m);
        }
        return set;
    }

    // ---------------------------------------------------------------- grime textures

    /// <summary>Generates the two tileable grime textures when missing. Albedo is a mid-grey fBm (0.5 = unchanged under Multiply x2); the normal is its gradient.</summary>
    public static void EnsureGrimeTextures()
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(GrimeAlbedoPath) != null && AssetDatabase.LoadAssetAtPath<Texture2D>(GrimeNormalPath) != null) return;
        if (!AssetDatabase.IsValidFolder(TextureFolder)) AssetDatabase.CreateFolder("Assets/SourceFiles/Textures", "Asylum");

        float[,] height = new float[GrimeSize, GrimeSize];
        float[,] coarse = Fbm(GrimeSize, 4, 5, 11f);
        float[,] fine = Fbm(GrimeSize, 16, 4, 71f);
        float[,] streaks = Fbm(GrimeSize, 6, 3, 33f);
        for (int y = 0; y < GrimeSize; y++)
        {
            for (int x = 0; x < GrimeSize; x++)
            {
                // Blotchy stains plus fine pitting; stretched in v so some of it reads as water runs. Contrast pushed so it is not a flat haze.
                float v = coarse[x, y] * 0.6f + fine[x, y] * 0.25f + streaks[x, (y * 3) % GrimeSize] * 0.15f;
                height[x, y] = Mathf.Clamp01((v - 0.5f) * 2.2f + 0.5f);
            }
        }

        Color32[] albedoPixels = new Color32[GrimeSize * GrimeSize];
        Color32[] normalPixels = new Color32[GrimeSize * GrimeSize];
        for (int y = 0; y < GrimeSize; y++)
        {
            for (int x = 0; x < GrimeSize; x++)
            {
                float hgt = height[x, y];
                // Detail Multiply x2: 0.5 leaves the base alone, below darkens. Range 0.26..0.56 = roughly -48 % .. +12 % (mean about -15 %).
                float g = Mathf.Lerp(0.56f, 0.26f, hgt * hgt);
                byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f);
                // A faint warm cast in the dirt.
                albedoPixels[y * GrimeSize + x] = new Color32(b, (byte)(b * 0.96f), (byte)(b * 0.88f), 255);

                float dx = height[(x + 1) % GrimeSize, y] - height[(x - 1 + GrimeSize) % GrimeSize, y];
                float dy = height[x, (y + 1) % GrimeSize] - height[x, (y - 1 + GrimeSize) % GrimeSize];
                Vector3 n = new Vector3(-dx * 6f, -dy * 6f, 1f).normalized;
                normalPixels[y * GrimeSize + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
            }
        }

        WritePng(GrimeAlbedoPath, albedoPixels);
        WritePng(GrimeNormalPath, normalPixels);
        AssetDatabase.ImportAsset(GrimeAlbedoPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(GrimeNormalPath, ImportAssetOptions.ForceSynchronousImport);
        ConfigureImporter(GrimeAlbedoPath, false);
        ConfigureImporter(GrimeNormalPath, true);
        Debug.Log("AsylumLookUpgrade: generated " + GrimeAlbedoPath + " and " + GrimeNormalPath);
    }

    private static void WritePng(string path, Color32[] pixels)
    {
        Texture2D t = new Texture2D(GrimeSize, GrimeSize, TextureFormat.RGBA32, false);
        t.SetPixels32(pixels);
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    private static void ConfigureImporter(string path, bool normalMap)
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return;
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
    }

    /// <summary>Tileable fractal noise in 0..1: each octave is Perlin sampled on a torus (four shifted samples blended), so the texture wraps.</summary>
    private static float[,] Fbm(int size, int baseCells, int octaves, float seed)
    {
        float[,] result = new float[size, size];
        float amp = 1f, total = 0f;
        int cells = baseCells;
        for (int o = 0; o < octaves; o++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    result[x, y] += TilingPerlin(x / (float)size, y / (float)size, cells, seed + o * 17.3f) * amp;
                }
            }
            total += amp;
            amp *= 0.5f;
            cells *= 2;
        }
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) result[x, y] /= total;
        return result;
    }

    private static float TilingPerlin(float u, float v, int cells, float seed)
    {
        float x = u * cells, y = v * cells;
        float a = Mathf.PerlinNoise(seed + x, seed + y);
        float b = Mathf.PerlinNoise(seed + x - cells, seed + y);
        float c = Mathf.PerlinNoise(seed + x, seed + y - cells);
        float d = Mathf.PerlinNoise(seed + x - cells, seed + y - cells);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }
}
