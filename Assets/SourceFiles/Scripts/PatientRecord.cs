using System;
using UnityEngine;

/// <summary>F122: what a Patient Record milestone unlocks (see <see cref="PatientRecord.IsUnlocked"/>). Append only; names are L10n slugs.</summary>
public enum Unlock
{
    Codex, KitScavenger, CardStethoscope, ContractUnseen, Oaths, ColourBone, KitRunner,
    CardArchivist, CardDynamo, KitCartographer, ContractsSkill, ColourOathRed, ColourWard
}

/// <summary>F122: codex pages, one bit each in <see cref="PatientRecordData.metMask"/>. Append only.</summary>
public enum CodexEntry { Hunter = 0, Stan = 1, Visitor = 2, Listener = 3, Tenant = 4, LivingBody = 5 }

/// <summary>The persisted record (one JSON blob). Every field is zero-safe, "best" values are stored +1 where 0 must mean none. Never rename or remove a field.</summary>
[Serializable]
public class PatientRecordData
{
    public int version = 1;
    public int campaignsStarted, campaignsFinished;
    /// <summary>Bit 0 = ending A (Discharged), bit 1 = ending B (Lights Out). The plain no-hub final Win counts as Discharged.</summary>
    public int endingsMask;
    public int bestOathDischargedPlus1, bestOathLightsOutPlus1;
    public int totalShards;
    public int floorsCleared, highestFloorCleared;
    /// <summary>Index = floor (1..5). Grade ints, the Oath rank it was set with, run seconds (+1 so 0 = none).</summary>
    public int[] bestGrade = new int[FloorProfile.FinalFloor + 1];
    public int[] bestGradeOath = new int[FloorProfile.FinalFloor + 1];
    public int[] bestSecondsPlus1 = new int[FloorProfile.FinalFloor + 1];
    public int sGrades;
    public int metMask;
    /// <summary>Highest level ever seen per HunterMemory.Habit.</summary>
    public int[] habitMax = new int[5];
    public int seenUnlockMask;
    public int lastPreset, lastAssists, lastOaths, lastKit, lastTorch;
}

/// <summary>
/// F122 D11: the Patient Record - a machine-wide profile OUTSIDE the campaign save (PlayerPrefs key <see cref="DefaultKey"/>, one JSON blob, every
/// read and write wrapped like <see cref="DescentRecords"/>). Never touched by GameFlow.ReturnToMenu, SaveSystem.Delete or a descent's resets. Unlocks are a pure
/// predicate over the data (<see cref="IsUnlocked"/>), so nothing can desync; "new" is an unlock not yet in <c>seenUnlockMask</c>.
/// Tests set <see cref="KeyOverride"/> so the real record is never read or written (SubsystemRegistration drops only the cache, not the override).
/// </summary>
public static class PatientRecord
{
    public const string DefaultKey = "keepitlit.record.v1";

    /// <summary>Tests only: another PlayerPrefs key (setting it drops the cache). NOT cleared on Play entry, so an eval can set it before Play.</summary>
    public static string KeyOverride
    {
        get => _keyOverride;
        set { _keyOverride = value; _data = null; }
    }
    private static string _keyOverride;

    public static string Key => string.IsNullOrEmpty(_keyOverride) ? DefaultKey : _keyOverride;

    private static PatientRecordData _data;
    private static int _shardBaseline;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _data = null;
        _shardBaseline = 0;
    }

    /// <summary>The cached record (loaded from PlayerPrefs on first use; an empty one when missing or unreadable).</summary>
    public static PatientRecordData Data
    {
        get
        {
            if (_data == null) _data = Load();
            return _data;
        }
    }

    /// <summary>Drops the cache; the next read reloads from PlayerPrefs.</summary>
    public static void Reload() => _data = null;

    private static PatientRecordData Load()
    {
        PatientRecordData d = null;
        try
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                d = JsonUtility.FromJson<PatientRecordData>(json);
                if (d == null) throw new FormatException("empty record");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PatientRecord: the record is unreadable ({e.Message}); starting an empty one (the old text is kept under '{Key}.unreadable').");
            try { PlayerPrefs.SetString(Key + ".unreadable", PlayerPrefs.GetString(Key, "")); } catch { /* best effort */ }
            d = null;
        }
        d = d ?? new PatientRecordData();
        Normalise(d);
        return d;
    }

    private static void Normalise(PatientRecordData d)
    {
        d.bestGrade = Fit(d.bestGrade, FloorProfile.FinalFloor + 1);
        d.bestGradeOath = Fit(d.bestGradeOath, FloorProfile.FinalFloor + 1);
        d.bestSecondsPlus1 = Fit(d.bestSecondsPlus1, FloorProfile.FinalFloor + 1);
        d.habitMax = Fit(d.habitMax, 5);
    }

    private static int[] Fit(int[] source, int length)
    {
        int[] result = new int[length];
        if (source != null) Array.Copy(source, result, Math.Min(source.Length, length));
        return result;
    }

    /// <summary>Writes the cache to PlayerPrefs (best effort).</summary>
    public static void Save()
    {
        try
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PatientRecord: could not save the record ({e.Message}).");
        }
    }

    /// <summary>KEEP IT LIT &gt; Debug &gt; Delete Patient Record, and tests.</summary>
    public static void Delete()
    {
        try
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.DeleteKey(Key + ".unreadable");
            PlayerPrefs.Save();
        }
        catch { /* best effort */ }
        _data = null;
    }

    public static bool IsEmpty => Data.campaignsStarted == 0 && Data.floorsCleared == 0 && Data.campaignsFinished == 0 && Data.metMask == 0 && Data.totalShards == 0;

    // ---------------------------------------------------------------- unlocks (pure over the data)

    public static bool IsUnlocked(Unlock u) => IsUnlocked(u, Data);

    public static bool IsUnlocked(Unlock u, PatientRecordData d)
    {
        switch (u)
        {
            case Unlock.Codex: return d.floorsCleared >= 1;
            case Unlock.KitScavenger: return d.highestFloorCleared >= 2;
            case Unlock.CardStethoscope:
            case Unlock.ContractUnseen: return d.highestFloorCleared >= 4;
            case Unlock.Oaths:
            case Unlock.ColourBone: return d.endingsMask != 0;
            case Unlock.KitRunner: return d.campaignsFinished >= 2;
            case Unlock.CardArchivist: return (d.endingsMask & 3) == 3;
            case Unlock.CardDynamo: return d.totalShards >= 1500;
            case Unlock.KitCartographer: return d.campaignsFinished >= 3;
            case Unlock.ContractsSkill: return d.sGrades >= 1;
            case Unlock.ColourOathRed: return Math.Max(d.bestOathDischargedPlus1, d.bestOathLightsOutPlus1) - 1 >= 5;
            case Unlock.ColourWard: return (d.metMask & AllCodexMask) == AllCodexMask;
            default: return false;
        }
    }

    public static readonly int AllCodexMask = (1 << Enum.GetValues(typeof(CodexEntry)).Length) - 1;

    public static int UnlockMask(PatientRecordData d)
    {
        int mask = 0;
        foreach (Unlock u in Enum.GetValues(typeof(Unlock))) if (IsUnlocked(u, d)) mask |= 1 << (int)u;
        return mask;
    }

    /// <summary>Unlocked but not yet viewed on the record screen.</summary>
    public static int NewCount => CountBits(UnlockMask(Data) & ~Data.seenUnlockMask);

    public static bool HasNew => NewCount > 0;

    public static bool IsNew(Unlock u) => ((UnlockMask(Data) & ~Data.seenUnlockMask) & (1 << (int)u)) != 0;

    /// <summary>The record screen marks everything it showed as seen.</summary>
    public static void MarkUnlocksSeen()
    {
        int mask = UnlockMask(Data);
        if ((Data.seenUnlockMask | mask) == Data.seenUnlockMask) return;
        Data.seenUnlockMask |= mask;
        Save();
    }

    private static int CountBits(int v)
    {
        int n = 0;
        while (v != 0) { n += v & 1; v = (int)((uint)v >> 1); }
        return n;
    }

    public static bool IsKitUnlocked(StartingKit kit)
    {
        switch (kit)
        {
            case StartingKit.Scavenger: return IsUnlocked(Unlock.KitScavenger);
            case StartingKit.Runner: return IsUnlocked(Unlock.KitRunner);
            case StartingKit.Cartographer: return IsUnlocked(Unlock.KitCartographer);
            default: return true;
        }
    }

    /// <summary>Torch palette entries 4 (Bone), 5 (Ward Green), 6 (Oath Red) are never sold: they are earned here.</summary>
    public static bool IsTorchColourUnlocked(int paletteIndex)
    {
        switch (paletteIndex)
        {
            case 4: return IsUnlocked(Unlock.ColourBone);
            case 5: return IsUnlocked(Unlock.ColourWard);
            case 6: return IsUnlocked(Unlock.ColourOathRed);
            default: return paletteIndex >= 0 && paletteIndex <= ShopCatalogue.ShopTorchSwatches;
        }
    }


    /// <summary>Contract ids 8 UNSEEN, 9 HANDS TO YOURSELF, 10 CUT IT CLOSE are earned here (0-7 are always offered).</summary>
    public static bool IsContractUnlocked(int id)
    {
        switch (id)
        {
            case ContractState.Unseen: return IsUnlocked(Unlock.ContractUnseen);
            case ContractState.HandsToYourself:
            case ContractState.CutItClose: return IsUnlocked(Unlock.ContractsSkill);
            default: return true;
        }
    }

    // ---------------------------------------------------------------- commit points

    /// <summary>BEGIN on the new-campaign screen.</summary>
    public static void NoteCampaignStarted(DifficultyPreset preset, Assist assists, Oath oaths, StartingKit kit, int torch)
    {
        PatientRecordData d = Data;
        d.campaignsStarted++;
        d.lastPreset = (int)preset;
        d.lastAssists = (int)assists;
        d.lastOaths = (int)oaths;
        d.lastKit = (int)kit;
        d.lastTorch = torch;
        Save();
    }

    /// <summary>
    /// A floor was cleared (story only). Counts always; the bests only when the campaign is not assisted: better = a higher grade, then a higher Oath
    /// rank, then a faster time. Returns true when it set a new best for the floor.
    /// </summary>
    public static bool NoteFloorCleared(int floor, Grade grade, float runSeconds, bool assisted, int oathRank)
    {
        PatientRecordData d = Data;
        d.floorsCleared++;
        d.highestFloorCleared = Math.Max(d.highestFloorCleared, floor);
        if (grade == Grade.S) d.sGrades++;

        bool best = false;
        if (!assisted && floor >= 1 && floor < d.bestGrade.Length)
        {
            int g = (int)grade;
            int secondsPlus1 = Mathf.Max(1, Mathf.RoundToInt(runSeconds) + 1);
            bool better = g > d.bestGrade[floor]
                || (g == d.bestGrade[floor] && oathRank > d.bestGradeOath[floor])
                || (g == d.bestGrade[floor] && oathRank == d.bestGradeOath[floor] && (d.bestSecondsPlus1[floor] == 0 || secondsPlus1 < d.bestSecondsPlus1[floor]));
            if (better && g > 0)
            {
                d.bestGrade[floor] = g;
                d.bestGradeOath[floor] = oathRank;
                d.bestSecondsPlus1[floor] = secondsPlus1;
                best = true;
            }
        }
        Save();
        return best;
    }

    /// <summary>An ending was completed. The best Oath per ending only counts for an unassisted campaign.</summary>
    public static void NoteEnding(bool discharged, bool assisted, int oathRank)
    {
        PatientRecordData d = Data;
        d.campaignsFinished++;
        d.endingsMask |= discharged ? 1 : 2;
        if (!assisted)
        {
            if (discharged) d.bestOathDischargedPlus1 = Math.Max(d.bestOathDischargedPlus1, oathRank + 1);
            else d.bestOathLightsOutPlus1 = Math.Max(d.bestOathLightsOutPlus1, oathRank + 1);
        }
        Save();
    }

    /// <summary>The campaign's shard earnings since the last commit go into the lifetime total.</summary>
    public static void CommitShards()
    {
        if (GameFlow.IsDescent) return;
        int total = PlayerWallet.TotalEarned;
        int delta = total - _shardBaseline;
        _shardBaseline = total;
        if (delta <= 0) return;
        Data.totalShards = (int)Math.Min((long)int.MaxValue, (long)Data.totalShards + delta);
        Save();
    }

    public static void ResetShardBaseline(int totalEarned) => _shardBaseline = Math.Max(0, totalEarned);

    /// <summary>The hunter learned something: remember the highest level of each habit ever reached.</summary>
    public static void NoteHabits()
    {
        PatientRecordData d = Data;
        bool changed = false;
        foreach (HunterMemory.Habit h in Enum.GetValues(typeof(HunterMemory.Habit)))
        {
            int i = (int)h;
            int level = HunterMemory.Level(h);
            if (i < d.habitMax.Length && level > d.habitMax[i]) { d.habitMax[i] = level; changed = true; }
        }
        if (changed) Save();
    }

    /// <summary>
    /// What the player met on a finished level (cleared or died), counted only when the level lasted 20 s or more: the hunter always, plus whatever the
    /// level's roster held, plus the loop's Visitor. Story and DESCENT.
    /// </summary>
    public static void NoteMet(EntityKind set, bool visitor, float runSeconds)
    {
        if (runSeconds < 20f) return;
        int mask = 1 << (int)CodexEntry.Hunter;
        if ((set & EntityKind.Stalker) != 0) mask |= 1 << (int)CodexEntry.Stan;
        if ((set & EntityKind.Listener) != 0) mask |= 1 << (int)CodexEntry.Listener;
        if ((set & EntityKind.Tenant) != 0) mask |= 1 << (int)CodexEntry.Tenant;
        if ((set & EntityKind.LivingBody) != 0) mask |= 1 << (int)CodexEntry.LivingBody;
        if (visitor) mask |= 1 << (int)CodexEntry.Visitor;
        if ((Data.metMask | mask) == Data.metMask) return;
        Data.metMask |= mask;
        Save();
    }

    public static bool Met(CodexEntry e) => (Data.metMask & (1 << (int)e)) != 0;

    // ---------------------------------------------------------------- debug

    /// <summary>KEEP IT LIT &gt; Debug &gt; Patient Record: Unlock Everything (dev only).</summary>
    public static void DebugUnlockEverything()
    {
        PatientRecordData d = Data;
        d.campaignsStarted = Math.Max(d.campaignsStarted, 5);
        d.campaignsFinished = Math.Max(d.campaignsFinished, 3);
        d.endingsMask = 3;
        d.bestOathDischargedPlus1 = Math.Max(d.bestOathDischargedPlus1, 7);
        d.bestOathLightsOutPlus1 = Math.Max(d.bestOathLightsOutPlus1, 6);
        d.totalShards = Math.Max(d.totalShards, 2000);
        d.floorsCleared = Math.Max(d.floorsCleared, 15);
        d.highestFloorCleared = FloorProfile.FinalFloor;
        d.sGrades = Math.Max(d.sGrades, 1);
        d.metMask = AllCodexMask;
        Save();
    }
}
