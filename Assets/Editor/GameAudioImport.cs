using UnityEditor;
using UnityEngine;

/// <summary>
/// F97 (D4): the import settings every library clip gets - Vorbis ~50 %, Compressed In Memory (long loops stream), mono for 3D one-shots,
/// preload on for short one-shots. Idempotent; called by GameAudioLibraryBuilder (no menu item).
/// </summary>
public static class GameAudioImport
{
    private const float Quality = 0.5f;

    public static bool NeedsSettings(string path, bool mono, bool stream)
    {
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return false;
        return !Matches(importer, Wanted(importer, stream, ClipSeconds(path)), mono);
    }

    public static void Apply(string path, bool mono, bool stream)
    {
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;
        AudioImporterSampleSettings wanted = Wanted(importer, stream, ClipSeconds(path));
        if (Matches(importer, wanted, mono)) return;

        importer.forceToMono = mono;
        importer.loadInBackground = true;
        importer.defaultSampleSettings = wanted;
        importer.ClearSampleSettingOverride("WebGL"); // the default (Vorbis, compressed in memory) is what the web build wants
        importer.SaveAndReimport();
    }

    private static AudioImporterSampleSettings Wanted(AudioImporter importer, bool stream, float seconds)
    {
        AudioImporterSampleSettings s = importer.defaultSampleSettings;
        s.loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = Quality;
        s.preloadAudioData = !stream && seconds < 3f;
        s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        return s;
    }

    private static bool Matches(AudioImporter importer, AudioImporterSampleSettings wanted, bool mono)
    {
        AudioImporterSampleSettings s = importer.defaultSampleSettings;
        return importer.forceToMono == mono && s.loadType == wanted.loadType && s.compressionFormat == wanted.compressionFormat && Mathf.Abs(s.quality - Quality) < 0.01f;
    }

    private static float ClipSeconds(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        return clip != null ? clip.length : 0f;
    }
}
