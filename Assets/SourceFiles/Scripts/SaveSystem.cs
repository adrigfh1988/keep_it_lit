using System;
using UnityEngine;

/// <summary>
/// F83: one campaign checkpoint, flattened to what JsonUtility can serialise (no Dictionary/HashSet - those
/// become parallel int arrays). Each static owner (PlayerWallet, PlayerInventory, LoreArchive, DreadDirector)
/// fills and reads its own slice through a CaptureTo/RestoreFrom pair, so no field has to be made public.
/// ShopItem values are stored as ints: the enum may be appended to but never reordered or trimmed.
/// </summary>
[Serializable]
public class SaveData
{
    /// <summary>SaveSystem.Version when written; an older one is migrated by SaveMigrations, a newer one is set aside.</summary>
    public int version;
    /// <summary>F118: Application.version at write time, for bug reports. Missing in an older save (empty = unknown), so no Version bump.</summary>
    public string writtenBy;
    /// <summary>The floor to resume (1..FinalFloor). With inShop, the floor that was just cleared.</summary>
    public int floor;
    /// <summary>Resume inside the shop after clearing <see cref="floor"/> rather than at the start of it.</summary>
    public bool inShop;
    /// <summary>GameFlow.RolledObjectives, the whole array.</summary>
    public int[] rolledObjectives;

    // PlayerWallet
    public int shards, totalEarned;
    public int[] mazeShardsEarnedOnFloor, consolationPaidOnFloor;

    // PlayerInventory (heldItems/heldCounts are parallel)
    public int[] heldItems, heldCounts;
    public int[] pendingModifiers, activeModifiers;
    public int torchColourIndex, hudTintIndex, ownedTorchColours, ownedHudTints;

    // LoreArchive
    public int[] readNoteIds;

    // DreadDirector
    public bool ambushSubtitleShown;

    // F87 ContractState. Ids are stored +1 so a save written before F87 (fields missing, read as 0) means "none".
    public int contractActivePlus1, contractFloor, contractOfferAPlus1, contractOfferBPlus1;

    // F105 C6 HunterMemory: a level (0..3) per habit, and the lessons not yet announced as a bitmask of (1 << Habit).
    // Missing in a save from before F105 (read as 0 = a hunter that has learned nothing), so SaveSystem.Version was not bumped.
    public int hunterLockers, hunterThrows, hunterDark, hunterSprint, hunterPendingMask;

    // F120 HunterMemory.Habit.Voice: a level (0..3). Missing in an older save (0 = it has not learned your voice), so no Version bump (P1.1).
    public int hunterVoice;

    // F105 C4 LoopCorridor.UsedThisCampaign: the looping corridor has already appeared on a cleared floor. Missing in an older save (false = it may still appear), so no Version bump.
    public bool loopCorridorUsed;

    // F114 HubCaches.SearchedMask: bit n = hub room find n has been searched this campaign. Missing in an older save (0 = nothing searched), so no Version bump.
    public int hubCachesSearched;

    // F117 HubState.MealFloor: the floor whose free hot meal has been eaten (0 = none). Missing in an older save (0), so no Version bump.
    public int hubMealFloor;

    // F121 HubState.BreachDone: the hub breach (the hunter in the asylum, floor 5) has been survived this campaign. Missing in an older save (false = it has not happened yet), so no Version bump (P1.1).
    public bool hubBreachDone;

    // F122 CampaignRules (the difficulty preset, assists, Oaths and starting kit) + FloorGrade (this campaign's grade and deaths per floor, index = floor).
    // Missing in an older save (0 / null = STANDARD, no assists, no Oaths, no grades), so no Version bump (P1.1). The enums are saved as ints: append only.
    public int campaignPreset, campaignAssists, campaignOaths, campaignKit;
    public bool campaignAssistedSticky;
    public int[] campaignGrades, floorDeaths;

    // NightNurse.SeenThisCampaign: true once Sister Agnes has appeared on a floor this campaign. Missing in an older save (false), so no Version bump (P1.1).
    public bool nightNurseSeen;
}

/// <summary>
/// F83: save/resume for the campaign, whole floors only - never mid-maze. There are two save points:
/// arriving in the shop after a floor clear (and every purchase there), and walking through the shop door
/// into the next floor (GameFlow.NextFloor). Death never saves; the final Win deletes it (GameOutcome.Win);
/// GameFlow.ReturnToMenu leaves it alone, which is what puts CONTINUE on the title screen. CONTINUE always
/// builds a fresh maze (seed 0), so reloading can't be used to scout a layout.
///
/// F118: storage is a file (persistentDataPath/save.json, atomic write, .bak) on desktop and PlayerPrefs on WebGL, behind
/// <see cref="ISaveStore"/>. An older save is migrated, an unreadable one is set aside as save.unreadable.json. Holds no run state.
/// </summary>
public static class SaveSystem
{
    /// <summary>
    /// Bump when SaveData's meaning changes in a way an older save cannot express (pattern P1.2 in plannings/f118-release-foundations-plan.md):
    /// add a case to <see cref="SaveMigrations"/> and a fixture test. A save with an older version is migrated, never discarded.
    /// </summary>
    public const int Version = 2;

    /// <summary>Namespaced: itch.io serves many games from shared origins. Also the legacy PlayerPrefs key (desktop saves moved to a file in F118).</summary>
    public const string Key = "keepitlit.save.v1";

    /// <summary>Why a stored save could not be used.</summary>
    public enum LoadProblem { None, Corrupt, TooNew, NoMigration, OutOfRange }

    // ---------------------------------------------------------------- storage

    private static ISaveStore _store;
    private static string _directoryOverride;
    private static bool _loggedWrittenBy;

    /// <summary>Tests only: save somewhere else (setting it drops the cached store). Cleared on Play entry.</summary>
    public static string DirectoryOverride
    {
        get => _directoryOverride;
        set { _directoryOverride = value; _store = null; }
    }

    /// <summary>Tests only: the PlayerPrefs key the one-time file migration reads. Null with a DirectoryOverride = skip it, so a test never touches the developer's real save.</summary>
    public static string LegacyKeyOverride
    {
        get => _legacyKeyOverride;
        set { _legacyKeyOverride = value; _store = null; }
    }
    private static string _legacyKeyOverride;

    /// <summary>Set by <see cref="TryLoad"/> when it had to set a save aside; shown once by the title screen.</summary>
    public static LoadProblem PendingProblem { get; private set; }

    /// <summary>Returns and clears <see cref="PendingProblem"/>.</summary>
    public static LoadProblem ConsumeProblem()
    {
        LoadProblem p = PendingProblem;
        PendingProblem = LoadProblem.None;
        return p;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _store = null;
        _directoryOverride = null;
        _legacyKeyOverride = null;
        PendingProblem = LoadProblem.None;
        _loggedWrittenBy = false;
    }

    private static ISaveStore Store
    {
        get
        {
            if (_store != null) return _store;
            ISaveStore created;
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                created = new SavePrefsStore(Key);
            }
            else
            {
                created = new SaveFileStore(_directoryOverride ?? Application.persistentDataPath, "save.json");
                MoveLegacyPlayerPrefsSave(created);
            }
            Debug.Log("SaveSystem: saves in " + created.Describe());
            _store = created;
            return _store;
        }
    }

    /// <summary>
    /// One-time move of a pre-F118 PlayerPrefs save into the file store. The JSON is copied unchanged (the version
    /// migration happens on load like for any save); PlayerPrefs is cleared only once the file reads back identical.
    /// </summary>
    private static void MoveLegacyPlayerPrefsSave(ISaveStore store)
    {
        string legacyKey = _legacyKeyOverride ?? (_directoryOverride == null ? Key : null);
        if (legacyKey == null) return;
        try
        {
            if (!store.IsEmpty() || !PlayerPrefs.HasKey(legacyKey)) return;
            string json = PlayerPrefs.GetString(legacyKey, "");
            if (string.IsNullOrEmpty(json)) return;
            store.Write(json);
            if (store.Read() != json) return;
            PlayerPrefs.DeleteKey(legacyKey);
            PlayerPrefs.SetInt(legacyKey == Key ? "keepitlit.save.movedToFile" : legacyKey + ".moved", 1);
            PlayerPrefs.Save();
            Debug.Log("SaveSystem: moved the PlayerPrefs save to " + store.Describe());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: could not move the PlayerPrefs save to a file ({e.Message}); it stays where it is.");
        }
    }

    // ---------------------------------------------------------------- queries

    /// <summary>True if a usable save exists (migrated / restored from the backup as needed).</summary>
    public static bool HasSave => TryLoad(out _);

    /// <summary>
    /// False if there is no save or it cannot be used. A readable older save is migrated and written back (the old text becomes the
    /// backup); an unreadable main file falls back to the backup; if both fail the main file is SET ASIDE (never deleted) and
    /// <see cref="PendingProblem"/> says why.
    /// </summary>
    public static bool TryLoad(out SaveData data)
    {
        data = null;
        try
        {
            ISaveStore store = Store;
            string json = store.Read();

            LoadProblem mainProblem = LoadProblem.None;
            if (!string.IsNullOrEmpty(json))
            {
                if (TryParse(json, out SaveData d, out mainProblem, out bool migrated))
                {
                    if (migrated)
                    {
                        Debug.Log($"SaveSystem: migrated save v{ReadVersion(json)} -> v{Version}");
                        store.Write(JsonUtility.ToJson(d)); // the pre-migration text becomes the .bak
                    }
                    LogWrittenBy(d);
                    data = d;
                    return true;
                }
            }

            bool hasBackup = store.TryReadBackup(out string bak);
            if (hasBackup)
            {
                if (TryParse(bak, out SaveData b, out LoadProblem bakProblem, out bool bakMigrated))
                {
                    Debug.LogWarning($"SaveSystem: the save was unreadable ({(string.IsNullOrEmpty(json) ? "missing" : mainProblem.ToString())}); restored the backup.");
                    if (!string.IsNullOrEmpty(json)) store.SetAside("replaced by the backup");
                    store.Write(bakMigrated ? JsonUtility.ToJson(b) : bak);
                    LogWrittenBy(b);
                    data = b;
                    return true;
                }
                if (string.IsNullOrEmpty(json)) mainProblem = bakProblem;
            }

            if (mainProblem != LoadProblem.None)
            {
                string why = mainProblem == LoadProblem.TooNew
                    ? "it was written by a newer version of the game; reinstall that version and rename the file back to recover it"
                    : "it could not be read: " + mainProblem;
                if (!string.IsNullOrEmpty(json)) store.SetAside(why);
                if (hasBackup) store.SetAsideBackup();
                PendingProblem = mainProblem;
            }
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: could not read the save ({e.Message}).");
            return false;
        }
    }

    private static void LogWrittenBy(SaveData d)
    {
        if (_loggedWrittenBy) return;
        _loggedWrittenBy = true;
        Debug.Log($"SaveSystem: save v{d.version} written by '{(string.IsNullOrEmpty(d.writtenBy) ? "unknown" : d.writtenBy)}'");
    }

    private static int ReadVersion(string json)
    {
        try { return JsonUtility.FromJson<SaveData>(json).version; } catch (Exception) { return -1; }
    }

    /// <summary>
    /// Pure: JsonUtility parse, version check, migration and the range rules. No I/O, no statics touched (the tests call it).
    /// <paramref name="migrated"/> is true when the data was brought forward from an older version.
    /// </summary>
    public static bool TryParse(string json, out SaveData data, out LoadProblem problem, out bool migrated)
    {
        data = null;
        migrated = false;
        problem = LoadProblem.None;

        SaveData d;
        try { d = JsonUtility.FromJson<SaveData>(json); }
        catch (Exception) { problem = LoadProblem.Corrupt; return false; }

        if (d == null || d.version <= 0) { problem = LoadProblem.Corrupt; return false; }
        if (d.version > Version) { problem = LoadProblem.TooNew; return false; }
        if (d.version < Version)
        {
            if (!SaveMigrations.Run(d)) { problem = LoadProblem.NoMigration; return false; }
            migrated = true;
        }

        if (d.floor < 1 || d.floor > FloorProfile.FinalFloor) { problem = LoadProblem.OutOfRange; return false; }
        // The shop only follows floors 1..FinalFloor-1; the last floor ends in Win.
        if (d.inShop && d.floor >= FloorProfile.FinalFloor) { problem = LoadProblem.OutOfRange; return false; }

        data = d;
        return true;
    }

    // ---------------------------------------------------------------- writing

    /// <summary>
    /// Snapshots the statics. <paramref name="floor"/> is the floor to resume, or with inShop the floor just cleared.
    /// A failed write only logs - saving must never break the game.
    /// </summary>
    public static void WriteCheckpoint(int floor, bool inShop)
    {
        if (GameFlow.IsDescent) return; // F107: a descent never touches the story save
        try
        {
            SaveData d = new SaveData
            {
                version = Version,
                writtenBy = Application.version,
                floor = Mathf.Clamp(floor, 1, FloorProfile.FinalFloor),
                inShop = inShop,
                rolledObjectives = (int[])GameFlow.RolledObjectives.Clone()
            };

            PlayerWallet.CaptureTo(d);
            PlayerInventory.CaptureTo(d);
            LoreArchive.CaptureTo(d);
            DreadDirector.CaptureTo(d);
            ContractState.CaptureTo(d);
            HunterMemory.CaptureTo(d);
            LoopCorridor.CaptureTo(d);
            HubCaches.CaptureTo(d);
            HubState.CaptureTo(d);
            CampaignRules.CaptureTo(d); // F122
            NightNurse.CaptureTo(d);

            Store.Write(JsonUtility.ToJson(d));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: could not write the save ({e.Message}).");
        }
    }

    public static void Delete()
    {
        if (GameFlow.IsDescent) return; // F107: nor deletes it
        try { Store.Delete(); }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: could not delete the save ({e.Message}).");
        }
    }

    // ---------------------------------------------------------------- resuming

    /// <summary>
    /// CONTINUE: restores every static from the save and reloads into a fresh maze (PendingSeed 0). A save
    /// taken in the shop sets GameFlow.ResumeInShop so MainMenu.Start enters the shop instead of the run.
    /// Returns false (changing nothing) if there is no usable save.
    /// </summary>
    public static bool ResumeCampaign()
    {
        if (!TryLoad(out SaveData d)) return false;

        PlayerWallet.RestoreFrom(d);
        PlayerInventory.RestoreFrom(d);
        LoreArchive.RestoreFrom(d);
        DreadDirector.RestoreFrom(d);
        ContractState.RestoreFrom(d);
        HunterMemory.RestoreFrom(d);
        LoopCorridor.RestoreFrom(d);
        HubCaches.RestoreFrom(d);
        HubState.RestoreFrom(d);
        CampaignRules.RestoreFrom(d); // F122 (also derives the HubBreach / HunterGrab switches)
        NightNurse.RestoreFrom(d);
        PatientRecord.ResetShardBaseline(PlayerWallet.TotalEarned); // F122: what the restored wallet already holds is not new earnings

        GameFlow.CurrentFloor = d.floor;
        CopyRolledObjectives(d);
        GameFlow.ResumeInShop = d.inShop;

        GameFlow.Restart(false, 0, !d.inShop); // F92: a floor-start save opens the floor in the Asylum hub (when the scene has one)
        return true;
    }

    /// <summary>
    /// Shop resume only: the rebuilt background maze rolls its mechanics pair again from its fresh seed and
    /// overwrites RolledObjectives[floor]; this puts the pair the player actually played back, so the next
    /// floor still excludes it.
    /// </summary>
    public static void ReapplyRolledObjectives()
    {
        if (TryLoad(out SaveData d)) CopyRolledObjectives(d);
    }

    private static void CopyRolledObjectives(SaveData d)
    {
        int[] target = GameFlow.RolledObjectives;
        Array.Clear(target, 0, target.Length);
        if (d.rolledObjectives != null) Array.Copy(d.rolledObjectives, target, Mathf.Min(d.rolledObjectives.Length, target.Length));
    }
}
