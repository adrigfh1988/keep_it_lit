using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Web build size: 94 % of the WebGL build was textures (1.4 GB uncompressed, ~790 MB built), most of them pack textures imported at
/// 2048 and not crunched. This gives every texture the build scenes depend on a **WebGL-only** platform override - a size cap by
/// category plus crunch compression - and never touches the source files, the Default settings or another platform's override:
///  - 1024: big surfaces and characters (asylum walls/floors, the PBR surface sets, characters, the Morgue room shell);
///  - 512: everything else (props, decals, small items);
///  - 128: textures that never read in this game (the day skybox the game blacks out, height maps, the Alchemist refraction map).
/// A texture whose override already matches is skipped, so re-running changes nothing. Sprites, UI, cookies, lightmaps and single-channel
/// textures are left alone. Run from ProjectSetup's piece "Web texture budget" (Build Missing Pieces) or <see cref="Apply"/>; crunching
/// hundreds of textures the first time takes a while (the result is cached in Library/, later builds don't redo it).
/// </summary>
public static class WebTextureBudget
{
    private const string Platform = "WebGL";
    private const int Quality = 50;

    private static readonly string[] LargeFolders =
    {
        "Assets/Abandoned_Asylum/Textures/Tiled/",
        "Assets/SourceFiles/Textures/PBR/",
        "Assets/GreenMossGames/",
        "Assets/Morgue Room PBR/Models/wall/",
        "Assets/Morgue Room PBR/Models/floor/",
        "Assets/SourceFiles/Characters/",
        "Assets/SourceFiles/TimmyRobot/",
    };

    /// <summary>The WebGL cap for one texture.</summary>
    public static int CapFor(string path)
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(path);
        string lower = file.ToLowerInvariant();
        if (path.EndsWith("/SkyDay.png") || lower.Contains("refraction") || lower.EndsWith("_h") || lower.Contains("height") || lower.Contains("_disp"))
            return 128;
        foreach (string folder in LargeFolders) if (path.StartsWith(folder)) return 1024;
        return 512;
    }

    private struct Entry { public string Path; public TextureImporter Importer; public int Cap; public int Source; }

    private static List<Entry> Collect()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var list = new List<Entry>();
        foreach (string dep in AssetDatabase.GetDependencies(scenes, true))
        {
            if (!(AssetImporter.GetAtPath(dep) is TextureImporter importer)) continue;
            if (importer.textureType != TextureImporterType.Default && importer.textureType != TextureImporterType.NormalMap) continue;
            if (!dep.StartsWith("Assets/")) continue;
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            list.Add(new Entry { Path = dep, Importer = importer, Cap = CapFor(dep), Source = Mathf.Max(w, h) });
        }
        return list;
    }

    private static bool UpToDate(Entry e)
    {
        TextureImporterPlatformSettings s = e.Importer.GetPlatformTextureSettings(Platform);
        return s.overridden && s.maxTextureSize == e.Cap && s.crunchedCompression && s.compressionQuality == Quality
            && s.format == TextureImporterFormat.Automatic && s.textureCompression != TextureImporterCompression.Uncompressed;
    }

    /// <summary>Check Project: null when every build texture has its WebGL override.</summary>
    public static string Problem()
    {
        int missing = Collect().Count(e => !UpToDate(e));
        return missing == 0 ? null : $"{missing} build textures have no WebGL size cap / crunch override";
    }

    /// <summary>A read-only estimate: how many textures change and the rough uncompressed size before and after (DXT-class, with mips).</summary>
    public static string Report()
    {
        List<Entry> all = Collect();
        double before = 0, after = 0;
        var byCap = new Dictionary<int, int>();
        foreach (Entry e in all)
        {
            int current = e.Importer.GetPlatformTextureSettings(Platform).overridden ? e.Importer.GetPlatformTextureSettings(Platform).maxTextureSize : e.Importer.maxTextureSize;
            int nowSide = Mathf.Min(e.Source, current), newSide = Mathf.Min(e.Source, e.Cap);
            before += Bytes(nowSide);
            after += Bytes(newSide);
            byCap[e.Cap] = byCap.TryGetValue(e.Cap, out int n) ? n + 1 : 1;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"build textures: {all.Count}, needing a change: {all.Count(e => !UpToDate(e))}");
        foreach (var kv in byCap.OrderByDescending(k => k.Key)) sb.AppendLine($"  cap {kv.Key}: {kv.Value}");
        sb.AppendLine($"rough GPU size before {before / 1048576.0:F0} MB -> after {after / 1048576.0:F0} MB (crunch shrinks the download further)");
        return sb.ToString();
    }

    private static double Bytes(int side) => side * (double)side * 1.0 * 1.333; // ~8 bpp block compression, with mips

    /// <summary>Applies the WebGL overrides to every build texture that lacks them. Returns a one-line summary.</summary>
    public static string Apply()
    {
        List<Entry> todo = Collect().Where(e => !UpToDate(e)).ToList();
        if (todo.Count == 0) return "Web texture budget: nothing to change.";
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < todo.Count; i++)
            {
                Entry e = todo[i];
                EditorUtility.DisplayProgressBar("Web texture budget", e.Path, i / (float)todo.Count);
                TextureImporterPlatformSettings s = e.Importer.GetPlatformTextureSettings(Platform);
                s.name = Platform;
                s.overridden = true;
                s.maxTextureSize = e.Cap;
                s.format = TextureImporterFormat.Automatic;
                s.textureCompression = TextureImporterCompression.Compressed;
                s.crunchedCompression = true;
                s.compressionQuality = Quality;
                e.Importer.SetPlatformTextureSettings(s);
                e.Importer.SaveAndReimport();
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }
        AssetDatabase.SaveAssets();
        string summary = $"Web texture budget: {todo.Count} textures got a WebGL override (1024 / 512 / 128 caps, crunch {Quality}).";
        Debug.Log(summary);
        return summary;
    }
}
