using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tier 129 integration stage (plannings/tier129-maze-scare-assets-plan.md, sections 6 and 7b): brings the locally
/// generated maze scare props from the 3D stage's staging folders into the project and builds their pack pieces.
///
/// The 3D stage exports to plannings/concepts/maze_props_b1/&lt;asset&gt;/export/ (outside Assets/, never touched by
/// it): Gen_&lt;Asset&gt;.fbx plus Gen_&lt;Asset&gt;_Albedo / _Normal / _MetalSmooth (R metallic, A smoothness) / optional
/// _Emission PNGs. Per asset this:
///   1. copies them (without the Gen_ prefix) into Assets/SourceFiles/Models/MazeProps/&lt;Asset&gt;/, overwriting in place
///      so the .meta files - and every GUID - survive a re-export;
///   2. sets the importers: maps by kind (normal map type, linear MetalSmooth, D7 max size), the FBX with no
///      animation / camera / light / collider and the Blender axis conversion baked in;
///   3. writes &lt;Stem&gt;_URP.mat (URP Lit: base, normal, metallic + smoothness from its alpha, emission when there is an
///      _Emission map - the furnace hatch, static, D8), updated in place when it exists;
///   4. saves a model-only &lt;Stem&gt;.prefab (no colliders, materials assigned) - FloorThemeBuilder's Gen_ pack defs
///      compose from these;
///   5. recomposes that asset's Gen_ pack prefabs (FloorThemeBuilder.RebuildGeneratedPacks) and lists any new one in
///      the gallery rows (PackDressing.Apply - marks the scene dirty, never saves).
/// Extra FBX files of an asset (Gen_Coffin_Lid.fbx) become children of its main prefab at their exported origin;
/// the mannequins are variants instead, one prefab per pose (Gen_MannequinA / _B / _C, any suffix after the letter).
/// A map set named &lt;Stem&gt;_&lt;Part&gt; (Gen_MeatHooks_Chain_Albedo.png) goes on the renderers whose object or source
/// material name contains &lt;Part&gt;. Idempotent: re-running with unchanged exports changes nothing but the pack prefabs.
///
/// Entry points: KEEP IT LIT &gt; Build &gt; Maze Props (Import Generated), or from the CLI
///   unity command eval --code 'return MazePropImport.ImportAll();'
///   unity command eval --code 'return MazePropImport.Import("Wheelchair");'
///   unity command eval --code 'return MazePropImport.BuildScrawls();'
/// </summary>
public static class MazePropImport
{
    /// <summary>The 3D stage's staging root, relative to the project folder.</summary>
    public const string ExportRoot = "plannings/concepts/maze_props_b1";

    private static readonly string[] MapKinds = { "Albedo", "Normal", "MetalSmooth", "Emission" };
    /// <summary>D8: the furnace hatch's ember glow, HDR multiplier on the emission map.</summary>
    private const float EmissionIntensity = 1.5f;

    private sealed class Spec
    {
        public string Asset;          // Unity folder and main stem
        public string[] Aliases = new string[0]; // other export names accepted for it (without Gen_)
        public int MaxSize;           // D7 import max size
        public bool Variants;         // every FBX is its own model prefab (the mannequin poses)
    }

    private static readonly Spec[] Specs =
    {
        new Spec { Asset = "Wheelchair", MaxSize = 1024 },
        new Spec { Asset = "RestraintBed", MaxSize = 2048 },
        new Spec { Asset = "TreatmentChair", Aliases = new[] { "LobotomyChair" }, MaxSize = 1024 }, // pack def Gen_LobotomyChair
        new Spec { Asset = "Straitjacket", MaxSize = 1024 },
        new Spec { Asset = "Furnace", MaxSize = 2048 },
        new Spec { Asset = "MeatHooks", Aliases = new[] { "MeatHook" }, MaxSize = 1024 },
        new Spec { Asset = "BodyBag", MaxSize = 1024 },
        new Spec { Asset = "Coffin", MaxSize = 2048 },
        new Spec { Asset = "Mannequin", MaxSize = 2048, Variants = true },
    };

    /// <summary>One exported file, mapped to its Unity name.</summary>
    private sealed class ExportFile
    {
        public string Source;  // absolute path in the staging folder
        public string Stem;    // canonical Unity stem (Wheelchair, Coffin_Lid, MannequinB)
        public string Map;     // null for an FBX, else one of MapKinds
        public string UnityName => Map == null ? $"{Stem}.fbx" : $"{Stem}_{Map}.png";
    }

    // ---------------------------------------------------------------- entry points

    [MenuItem("KEEP IT LIT/Build/Maze Props (Import Generated)", priority = 101)]
    private static void ImportAllMenu()
    {
        string summary = ImportAll();
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("Maze Props (Import Generated)", summary, "OK");
    }

    /// <summary>Every asset that has an export, plus the scrawl decals; then PackDressing.Apply once. Returns a summary (also logged).</summary>
    public static string ImportAll()
    {
        StringBuilder sb = new StringBuilder("Maze props - ");
        foreach (Spec spec in Specs) sb.Append(ImportOne(spec)).Append("; ");
        sb.Append(RebuildScrawlPacks()).Append("; ");
        sb.Append(PackDressing.Apply());
        Debug.Log(sb.ToString());
        return sb.ToString();
    }

    /// <summary>One asset by name (Wheelchair, RestraintBed, TreatmentChair or LobotomyChair, Straitjacket, Furnace, MeatHooks, BodyBag, Coffin, Mannequin); then PackDressing.Apply.</summary>
    public static string Import(string asset)
    {
        Spec spec = Specs.FirstOrDefault(s => string.Equals(s.Asset, asset, StringComparison.OrdinalIgnoreCase)
                                              || s.Aliases.Any(a => string.Equals(a, asset, StringComparison.OrdinalIgnoreCase)));
        if (spec == null) return $"MazePropImport: unknown asset '{asset}' (one of {string.Join(", ", Specs.Select(s => s.Asset))}).";
        string summary = ImportOne(spec) + "; " + PackDressing.Apply();
        Debug.Log(summary);
        return summary;
    }

    /// <summary>The Gen_Scrawl_* wall decals only (their PNGs come from decals/key_scrawls.py); then PackDressing.Apply.</summary>
    public static string BuildScrawls()
    {
        string summary = RebuildScrawlPacks() + "; " + PackDressing.Apply();
        Debug.Log(summary);
        return summary;
    }

    private static string RebuildScrawlPacks()
    {
        AssetDatabase.Refresh(); // the keying script writes the PNGs from outside the Editor
        if (!AssetDatabase.IsValidFolder(FloorThemeBuilder.ScrawlFolder)) return "scrawls: none (run decals/key_scrawls.py)";
        List<string> built = FloorThemeBuilder.RebuildGeneratedPacks(FloorThemeBuilder.ScrawlFolder);
        return $"scrawls: {built.Count} built";
    }

    // ---------------------------------------------------------------- one asset

    private static string ImportOne(Spec spec)
    {
        List<ExportFile> files = FindExports(spec);
        if (!files.Any(f => f.Map == null)) return $"{spec.Asset}: not exported yet";

        string folder = $"{FloorThemeBuilder.MazePropsRoot}/{spec.Asset}";
        EditorAssetUtil.EnsureFolder(folder);

        int copied = 0;
        foreach (ExportFile file in files)
        {
            string assetPath = $"{folder}/{file.UnityName}";
            if (CopyIfChanged(file.Source, assetPath)) copied++;
        }
        if (copied > 0) AssetDatabase.Refresh();

        // Importers.
        foreach (ExportFile file in files)
        {
            string assetPath = $"{folder}/{file.UnityName}";
            if (file.Map == null) ConfigureModel(assetPath);
            else ConfigureTexture(assetPath, file.Map, spec.MaxSize);
        }

        // Materials: one per map set (a stem that has an Albedo).
        Dictionary<string, Material> materials = new Dictionary<string, Material>();
        foreach (string stem in files.Where(f => f.Map == "Albedo").Select(f => f.Stem).Distinct())
        {
            materials[stem] = WriteMaterial(folder, stem);
        }

        // Model-only prefabs.
        List<string> models = files.Where(f => f.Map == null).Select(f => f.Stem).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        List<string> prefabs = new List<string>();
        if (spec.Variants)
        {
            foreach (string stem in models) prefabs.Add(WriteModelPrefab(folder, stem, new List<string> { stem }, materials, spec.Asset));
        }
        else
        {
            string main = models.Contains(spec.Asset) ? spec.Asset : models[0];
            List<string> group = new List<string> { main };
            group.AddRange(models.Where(m => m != main));
            prefabs.Add(WriteModelPrefab(folder, spec.Asset, group, materials, spec.Asset));
        }
        AssetDatabase.SaveAssets();

        List<string> packs = FloorThemeBuilder.RebuildGeneratedPacks(folder);
        return $"{spec.Asset}: {copied} file(s) copied, {materials.Count} material(s), prefabs {string.Join("/", prefabs)}, packs [{string.Join(", ", packs)}]";
    }

    // ---------------------------------------------------------------- finding + copying exports

    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    /// <summary>Every Gen_ FBX / map PNG in any export/ folder under ExportRoot that belongs to spec (newest wins when two folders hold the same name).</summary>
    private static List<ExportFile> FindExports(Spec spec)
    {
        Dictionary<string, ExportFile> byName = new Dictionary<string, ExportFile>(StringComparer.OrdinalIgnoreCase);
        string root = Path.Combine(ProjectRoot, ExportRoot);
        if (!Directory.Exists(root)) return new List<ExportFile>();

        foreach (string path in Directory.GetFiles(root, "Gen_*", SearchOption.AllDirectories))
        {
            string dir = Path.GetFileName(Path.GetDirectoryName(path));
            if (!string.Equals(dir, "export", StringComparison.OrdinalIgnoreCase)) continue;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            string raw = Path.GetFileNameWithoutExtension(path).Substring(4); // drop Gen_
            string map = null;
            if (ext == ".png")
            {
                map = MapKinds.FirstOrDefault(k => raw.EndsWith("_" + k, StringComparison.OrdinalIgnoreCase));
                if (map == null) continue; // previews and anything else that is not a map
                raw = raw.Substring(0, raw.Length - map.Length - 1);
            }
            else if (ext != ".fbx") continue;

            string stem = Canonical(spec, raw);
            if (stem == null) continue;

            ExportFile file = new ExportFile { Source = path, Stem = stem, Map = map };
            if (byName.TryGetValue(file.UnityName, out ExportFile other) && File.GetLastWriteTimeUtc(other.Source) >= File.GetLastWriteTimeUtc(path)) continue;
            byName[file.UnityName] = file;
        }
        return byName.Values.ToList();
    }

    /// <summary>
    /// The Unity stem for an export name (Gen_ already dropped), or null if it is not this asset's. Aliases become the
    /// asset name (LobotomyChair -> TreatmentChair); a suffix becomes _Part (CoffinLid / Coffin_Lid -> Coffin_Lid); for
    /// the mannequins the pose letter is kept and anything after it dropped (Mannequin_B_Slumped -> MannequinB).
    /// </summary>
    private static string Canonical(Spec spec, string raw)
    {
        foreach (string name in new[] { spec.Asset }.Concat(spec.Aliases))
        {
            if (!raw.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = raw.Substring(name.Length);
            if (rest.Length == 0) return spec.Asset;
            if (spec.Variants)
            {
                Match m = Regex.Match(rest, @"^[_\-]?([A-Da-d])(?:[_\-].*)?$");
                return m.Success ? spec.Asset + m.Groups[1].Value.ToUpperInvariant() : spec.Asset + "_" + rest.TrimStart('_', '-');
            }
            return spec.Asset + "_" + rest.TrimStart('_', '-');
        }
        return null;
    }

    /// <summary>Overwrites the project file in place (its .meta - and GUID - stay) when the bytes differ. True if it wrote.</summary>
    private static bool CopyIfChanged(string source, string assetPath)
    {
        string dest = Path.Combine(ProjectRoot, assetPath);
        if (File.Exists(dest))
        {
            FileInfo a = new FileInfo(source), b = new FileInfo(dest);
            if (a.Length == b.Length && File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(dest))) return false;
        }
        File.Copy(source, dest, true);
        return true;
    }

    // ---------------------------------------------------------------- importers

    private static void ConfigureTexture(string path, string map, int maxSize)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogError($"MazePropImport: {path} did not import as a texture."); return; }

        bool dirty = false;
        void Set<T>(Func<T> get, Action<T> set, T value)
        {
            if (EqualityComparer<T>.Default.Equals(get(), value)) return;
            set(value);
            dirty = true;
        }

        bool normal = map == "Normal", linear = map == "MetalSmooth";
        Set(() => ti.textureType, v => ti.textureType = v, normal ? TextureImporterType.NormalMap : TextureImporterType.Default);
        if (!normal) Set(() => ti.sRGBTexture, v => ti.sRGBTexture = v, !linear);
        Set(() => ti.alphaSource, v => ti.alphaSource = v, linear ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None);
        Set(() => ti.alphaIsTransparency, v => ti.alphaIsTransparency = v, false);
        Set(() => ti.mipmapEnabled, v => ti.mipmapEnabled = v, true);
        Set(() => ti.maxTextureSize, v => ti.maxTextureSize = v, maxSize);
        if (dirty) ti.SaveAndReimport();
    }

    private static void ConfigureModel(string path)
    {
        ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) { Debug.LogError($"MazePropImport: {path} did not import as a model."); return; }

        bool dirty = false;
        void Set<T>(Func<T> get, Action<T> set, T value)
        {
            if (EqualityComparer<T>.Default.Equals(get(), value)) return;
            set(value);
            dirty = true;
        }

        Set(() => mi.globalScale, v => mi.globalScale = v, 1f);
        Set(() => mi.useFileScale, v => mi.useFileScale = v, true);
        Set(() => mi.bakeAxisConversion, v => mi.bakeAxisConversion = v, true);
        Set(() => mi.importCameras, v => mi.importCameras = v, false);
        Set(() => mi.importLights, v => mi.importLights = v, false);
        Set(() => mi.importAnimation, v => mi.importAnimation = v, false);
        Set(() => mi.animationType, v => mi.animationType = v, ModelImporterAnimationType.None);
        Set(() => mi.addCollider, v => mi.addCollider = v, false);
        Set(() => mi.isReadable, v => mi.isReadable = v, true); // RuntimeNavMeshBuilder (RenderMeshes) requires Read/Write enabled
        Set(() => mi.importNormals, v => mi.importNormals = v, ModelImporterNormals.Import);
        Set(() => mi.importTangents, v => mi.importTangents = v, ModelImporterTangents.CalculateMikk);
        if (dirty) mi.SaveAndReimport();
    }

    // ---------------------------------------------------------------- material + prefab

    /// <summary>&lt;stem&gt;_URP.mat, URP Lit, created or updated in place (GUID kept).</summary>
    private static Material WriteMaterial(string folder, string stem)
    {
        string path = $"{folder}/{stem}_URP.mat";
        Texture2D Map(string kind) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{stem}_{kind}.png");
        Texture2D albedo = Map("Albedo"), normal = Map("Normal"), metal = Map("MetalSmooth"), emission = Map("Emission");

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = mat == null;
        if (create) mat = new Material(lit) { name = $"{stem}_URP" };
        else if (mat.shader != lit) mat.shader = lit;

        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BaseMap", albedo);
        mat.SetTexture("_MainTex", albedo);

        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 1f);
        if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");

        mat.SetTexture("_MetallicGlossMap", metal);
        mat.SetFloat("_SmoothnessTextureChannel", 0f); // smoothness from the metallic map's alpha
        if (metal != null)
        {
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f); // scales the map's alpha
        }
        else
        {
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.3f);
        }

        mat.SetTexture("_EmissionMap", emission);
        if (emission != null)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.white * EmissionIntensity);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        if (create) AssetDatabase.CreateAsset(mat, path);
        else EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// &lt;name&gt;.prefab: the FBX of every stem in group (the first one the main model, the rest at their exported origin)
    /// unpacked under one root, colliders stripped, materials assigned. Saved over the existing prefab, GUID kept.
    /// </summary>
    private static string WriteModelPrefab(string folder, string name, List<string> group, Dictionary<string, Material> materials, string assetStem)
    {
        GameObject root = new GameObject(name);
        foreach (string stem in group)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{stem}.fbx");
            if (model == null) { Debug.LogError($"MazePropImport: {folder}/{stem}.fbx did not load."); continue; }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = stem;
            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);

            Material main = materials.TryGetValue(stem, out Material own) ? own
                : materials.TryGetValue(assetStem, out Material shared) ? shared
                : materials.Count == 1 ? materials.Values.First() : null;
            // Part sets: <stem>_<Part> (or <asset>_<Part>) that are not models themselves.
            List<KeyValuePair<string, Material>> parts = materials
                .Where(kv => !group.Contains(kv.Key) && (kv.Key.StartsWith(stem + "_") || kv.Key.StartsWith(assetStem + "_")))
                .Select(kv => new KeyValuePair<string, Material>(kv.Key.Substring(kv.Key.IndexOf('_') + 1), kv.Value))
                .ToList();
            if (main == null && parts.Count == 0) Debug.LogWarning($"MazePropImport: no map set for {stem} - it keeps the FBX's own materials.");

            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string source = slots[i] != null ? slots[i].name : "";
                    Material pick = parts.FirstOrDefault(p => r.gameObject.name.IndexOf(p.Key, StringComparison.OrdinalIgnoreCase) >= 0
                                                              || source.IndexOf(p.Key, StringComparison.OrdinalIgnoreCase) >= 0).Value ?? main;
                    if (pick != null) slots[i] = pick;
                }
                r.sharedMaterials = slots;
            }
        }

        string path = $"{folder}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return name;
    }
}
