using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// F97 (plannings/audio-wiring-plan.md): creates Assets/SourceFiles/Audio/GameAudioLibrary.asset, fills every EMPTY slot from the table below
/// (never overwrites a slot the user has changed), applies the import settings of D4 to every clip it references, and assigns the asset to
/// the scene's MazeGenerator. No menu item: run by KEEP IT LIT > Build Missing Pieces through the "Audio library" piece in ProjectSetup.
/// Never saves the scene - it only marks it dirty.
/// </summary>
public static class GameAudioLibraryBuilder
{
    public const string AssetPath = "Assets/SourceFiles/Audio/GameAudioLibrary.asset";

    private const string FP = "Assets/Free Pack/";
    private const string HE = "Assets/Horror Elements/";
    private const string SB = "Assets/Libraries/Soundbits_freeSFX_2025/Sounds/";
    private const string SF = "Assets/Scary Sound FX/";

    private struct Row
    {
        public string slot;          // field name on GameAudioLibrary
        public string[] clips;       // asset paths (without the .wav)
        public float volume, jitter, offset, maxSeconds, lowPass;
        public bool mono;            // 3D one-shots; loops and 2D stereo material stay stereo
        public bool stream;          // long loops
    }

    private static Row R(string slot, string[] clips, float volume, float jitter = 0f, float offset = 0f, float maxSeconds = 0f, float lowPass = 0f, bool mono = true, bool stream = false)
        => new Row { slot = slot, clips = clips, volume = volume, jitter = jitter, offset = offset, maxSeconds = maxSeconds, lowPass = lowPass, mono = mono, stream = stream };

    // Trims come from the measured RMS of each file (plannings/audio-wiring-plan.md D3 + the report): quiet files are lifted, loud ones pulled down,
    // so every slot lands in the same loudness region before the call site's own volume is applied.
    private static Row[] Table() => new[]
    {
        // ---- hub ambience
        R("roomTone", new[] { HE + "Ambient/Amb_darkroom" }, 4f, mono: false, stream: true),
        R("machinery", new[] { HE + "Ambient/Amb_Rumble" }, 3f, stream: true),
        R("drip", new[] { SB + "jc_drops_13" }, 1.2f),
        R("farScream", new[] { HE + "Misc/Misc_horrific", HE + "Misc/Misc_Cry", SB + "sotd_2013-10-30_female_scream", SB + "ss2-h_female_scream_070", SB + "ss2-h_child_scream_028", SB + "ss2-h_male_scream_032" }, 0.65f, 0.05f, 0f, 9f, 3500f),
        R("farSlam", new[] { HE + "Hits/Hit_upstair_boom", HE + "Hits/Hit_upstair_boom_2", FP + "Heavy Object Impact 4" }, 0.6f, 0.05f, 0f, 0f, 1500f),
        R("pipeClank", new[] { FP + "Metal impact 5", FP + "Metal Impact 7" }, 0.6f, 0.08f),
        R("farRumble", new[] { HE + "Ambient/Amb_Deep_impacts" }, 1f),
        R("buildingSettle", new[] { SB + "ucns_metal_creak_08", SB + "ucns_dry-ice_creak_squeak_30" }, 1.3f, 0f, 0f, 6f, 2500f),
        R("floorCreak", new[] { SB + "ucns_woodenbed_creaks_04", SB + "ucns_woodencupboard_01_squeaks_07" }, 1.5f),
        R("farDrag", new[] { SB + "ds_woodencrate_sliding_on_parquet_07_01", SB + "ds_tallfridge_sliding_on_concrete_02_07" }, 1f, 0f, 0f, 0f, 900f),
        // ---- hub set pieces and story
        R("timmyStep", new[] { SB + "ji-e01_impact_163", SB + "ji-e01_impact_167", SB + "ji-e01_impact_171" }, 1.3f, 0.12f),
        R("timmySpot", new[] { SB + "jw4_metallic-dystopian-029" }, 1.5f),
        R("caught", new[] { FP + "Bloody punch" }, 0.8f, mono: false),
        R("caughtLayer", new[] { HE + "Hits/Hit_Dark" }, 0.6f, 0f, 0f, 4f, mono: false),
        R("liftGrind", new[] { SB + "jm-ss_metal_scrape-0026", SB + "jm-ss_metal_scrape-0067", SB + "jm-ss_metal_scrape-0091" }, 0.9f, 0.04f, mono: false),
        R("liftBang", new[] { HE + "Hits/Hit_Metal", HE + "Hits/Hit_Anvil" }, 1f, 0.04f, 0f, 3.5f, mono: false),
        R("liftThud", new[] { FP + "Heavy Object Impact 4" }, 0.6f, 0.1f),
        R("liftJamRumble", new[] { FP + "Earthquake 3 - Big" }, 0.35f, 0f, 0f, 6f, mono: false),
        R("bulbPop", new[] { SB + "csfx_short-006" }, 1.4f, 0.08f),
        R("keyJingle", new[] { SB + "jc_pull-hanging-jingle_21" }, 2.5f, 0.04f, 0.05f, 2.2f),
        R("lockedRattle", new[] { SB + "antiques_lock_wardrobe_01", SB + "ji-e2_metal_jolt_rattle_scrape-054" }, 1.2f, 0.05f, 0f, 1.8f),
        R("exitDoorCreak", new[] { SB + "sotd_2013-10-27_doorsqueaks" }, 0.8f, 0f, 0.2f, 7f),
        R("masterLever", new[] { SB + "levers_sliders_139" }, 1.2f, 0f, 0.35f, 3.5f),
        R("powerDown", new[] { SB + "sotd_2013-10-28_electro-mechanical" }, 1f, 0f, 0f, 4f),
        R("deskDrawer", new[] { SB + "oc_drawer-heavy-woodmetal-01-02" }, 1.8f, 0f, 0.12f, 3f),
        R("whisperEcho", new[] { HE + "Misc/Misc_Whisper" }, 4f, 0f, 0.25f, 6f),
        R("patientSound", new[] { HE + "Misc/Misc_Cry_solo", HE + "Misc/Misc_Laugh" }, 1.3f, 0.05f, 0f, 7f),
        R("flickerCrackle", new[] { SB + "glitchfx_01_dirt_sfx_011", SB + "glitchfx_01_dirt_sfx_019", FP + "Magic Spell_Electricity Spell_1" }, 1f, 0.1f, 0f, 1f),
        // ---- mazes
        R("shutterRoll", new[] { FP + "Secret door" }, 0.7f, 0.05f, 0f, 3.2f),
        R("shutterSlam", new[] { FP + "Metal Impact 7", HE + "Hits/Hit_metalic" }, 0.6f, 0.06f, 0f, 2.5f),
        R("fuseClunk", new[] { SB + "levers_sliders_026" }, 2f, 0f, 0.05f, 2.5f),
        R("fuseHum", new[] { SB + "sotd_2013-10-28_electro-mechanical" }, 0.8f, 0f, 0f, 3f),
        R("wallButton", new[] { SB + "buttons_switches_037" }, 3f, 0.03f, 0.24f, 1.2f),
        R("hatchOpen", new[] { FP + "Secret door" }, 0.7f, 0f, 0f, 3.5f),
        R("shardChime", new[] { FP + "Magic Spell_Coins_2" }, 0.8f, 0.05f, 0.65f, 2.5f),
        R("glassStep", new[] { SB + "cs-d_glass_crash_designed_013" }, 0.35f, 0.1f, 0.1f, 0.9f),
        R("bottleShatter", new[] { SB + "cs-d_glass_crash_designed_037" }, 0.9f, 0.05f, 0.05f, 2.4f),
        R("canClatter", new[] { SB + "cs-d_metal_crash_designed_011" }, 0.9f, 0.05f, 0.1f, 2.2f),
        R("steamValve", new[] { FP + "Chemistry reaction 3 - Big" }, 0.7f, 0f, 0f, 5f),
        R("terminalBeep", new[] { SB + "csfx_short-040" }, 1f),
        R("candleSnuff", new[] { SB + "jw3_whoosh_fire-008" }, 1f),
        R("stanTick", new[] { SB + "glitchfx_02_percussion_019" }, 0.6f, 0.1f),
        R("stanSkitter", new[] { SB + "jw4_friction-organic-030" }, 1f, 0.06f),
        R("relocationCue", new[] { FP + "Ghost Manifestation 1", FP + "Ghost Manifestation 2" }, 0.7f, 0f, 0f, 6f, mono: false),
        R("phantomWhisper", new[] { HE + "Misc/Misc_Whisper", HE + "Misc/Misc_Shh" }, 3f, 0f, 0.3f, 5f),
        R("captureSting", new[] { HE + "Hits/Hit_Drama" }, 0.9f, mono: false),
        R("hunterAlert", new[] { SB + "jm-sm_metal_squeak-050", SB + "jm-sm_metal_squeak-082", SB + "ji-e2_metal_jolt_rattle_scrape-054" }, 1f, 0.06f, 0f, 1.6f),
        R("spottedSting", new[] { SF + "Impacts & Hits/H_Screamer_Bell_Short", SF + "Impacts & Hits/H_Impact_Low_Short", HE + "Hits/Hit_Suspens", SB + "cht2_cinematic_hit_156" }, 1f, mono: false),
        R("hunterScrape", new[] { SB + "jm-ss_metal_scrape-0026", SB + "jm-ss_metal_scrape-0067" }, 0.8f, 0.08f, 0f, 1.2f),
        R("mazeRoomTone", new[] { SF + "Ambiences/ATMO - A3 - MASTER - LOOP", SF + "Ambiences/ATMO - A4 - MASTER - LOOP", FP + "Cavern Atmosphere - Loop" }, 1f, mono: false, stream: true),
        R("escapeAlarm", new[] { HE + "Misc/Misc_Emergency", SB + "csfx-2_alarms_15" }, 0.7f, mono: true),
        // ---- player, UI, music
        R("torchClick", new[] { SB + "buttons_switches_048" }, 2.5f, 0.04f, 0.12f, 0.5f, mono: false),
        R("lockerDoor", new[] { SB + "oc_door-metal-02-01" }, 1.5f, 0.05f, 0.15f, 1.6f),
        R("lockerBreath", new[] { HE + "Misc/Misc_breath" }, 2f, 0.03f, mono: false),
        R("paper", new[] { SB + "etw_3006_movement_paper-releae_04", SB + "oc_book-01-12" }, 2.5f, 0.05f, 0f, 1.5f),
        R("shopTick", new[] { SB + "csfx-2_menu-close_17" }, 2.5f, 0f, 0.19f, mono: false),
        R("chaseMusic", new[] { HE + "Ambient/Amb_Run_2" }, 1f, mono: false, stream: true),
        R("chaseEnd", new[] { HE + "Ambient/Amb_Run_End" }, 1f, mono: false),
        R("shopMusic", new[] { HE + "Save Room/SR_We_are_safe" }, 0.6f, mono: false, stream: true),
    };

    /// <summary>Null when the library exists, is assigned to the scene's MazeGenerator and has every mapped slot filled; otherwise what is wrong.</summary>
    public static string Problem()
    {
        GameAudioLibrary lib = AssetDatabase.LoadAssetAtPath<GameAudioLibrary>(AssetPath);
        if (lib == null) return "Assets/SourceFiles/Audio/GameAudioLibrary.asset is missing";
        MazeGenerator maze = Object.FindAnyObjectByType<MazeGenerator>(FindObjectsInactive.Include);
        if (maze != null)
        {
            SerializedProperty p = new SerializedObject(maze).FindProperty("audioLibrary");
            if (p == null || p.objectReferenceValue == null) return "MazeGenerator.audioLibrary is not assigned";
        }
        SerializedObject so = new SerializedObject(lib);
        List<string> empty = new List<string>();
        foreach (Row row in Table())
        {
            SerializedProperty clips = so.FindProperty(row.slot + ".clips");
            if (clips == null || clips.arraySize > 0) continue;
            if (AssetDatabase.LoadAssetAtPath<AudioClip>(row.clips[0] + ".wav") != null) empty.Add(row.slot);
        }
        if (empty.Count > 0) return "empty slots that have a clip to fill them: " + string.Join(", ", empty);
        foreach (Row row in Table())
        {
            foreach (string c in row.clips)
            {
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(c + ".wav") != null && GameAudioImport.NeedsSettings(c + ".wav", row.mono, row.stream))
                    return "import settings are not applied to " + c;
            }
        }
        return null;
    }

    public static void BuildSilently()
    {
        GameAudioLibrary lib = AssetDatabase.LoadAssetAtPath<GameAudioLibrary>(AssetPath);
        if (lib == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Audio")) AssetDatabase.CreateFolder("Assets/SourceFiles", "Audio");
            lib = ScriptableObject.CreateInstance<GameAudioLibrary>();
            AssetDatabase.CreateAsset(lib, AssetPath);
        }

        SerializedObject so = new SerializedObject(lib);
        int filled = 0, missing = 0;
        foreach (Row row in Table())
        {
            List<AudioClip> found = new List<AudioClip>();
            foreach (string c in row.clips)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(c + ".wav");
                if (clip == null) { Debug.LogWarning($"GameAudioLibraryBuilder: {c}.wav not found (slot {row.slot})"); missing++; continue; }
                found.Add(clip);
                GameAudioImport.Apply(c + ".wav", row.mono, row.stream);
            }

            SerializedProperty clips = so.FindProperty(row.slot + ".clips");
            if (clips == null) { Debug.LogWarning("GameAudioLibraryBuilder: no slot field named " + row.slot); continue; }
            if (clips.arraySize > 0 || found.Count == 0) continue; // never overwrite what the user set
            clips.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            so.FindProperty(row.slot + ".volume").floatValue = row.volume;
            so.FindProperty(row.slot + ".pitchJitter").floatValue = row.jitter;
            so.FindProperty(row.slot + ".startOffset").floatValue = row.offset;
            so.FindProperty(row.slot + ".maxSeconds").floatValue = row.maxSeconds;
            so.FindProperty(row.slot + ".lowPassHz").floatValue = row.lowPass;
            filled++;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();

        MazeGenerator maze = Object.FindAnyObjectByType<MazeGenerator>(FindObjectsInactive.Include);
        if (maze != null)
        {
            SerializedObject ms = new SerializedObject(maze);
            SerializedProperty p = ms.FindProperty("audioLibrary");
            if (p != null && p.objectReferenceValue != lib)
            {
                p.objectReferenceValue = lib;
                ms.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(maze.gameObject.scene); // never auto-save: the user saves with Ctrl+S
            }
        }
        Debug.Log($"GameAudioLibraryBuilder: {filled} slot(s) filled, {missing} clip(s) missing. Save the scene (Ctrl+S) to keep the MazeGenerator assignment.");
    }
}
