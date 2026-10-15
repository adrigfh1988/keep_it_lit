using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F105 C6: what the hunter has learned about the player's habits, across the whole campaign. Five habits (F120 added Voice), each a level
/// 0..<see cref="MaxLevel"/>. <see cref="HabitTracker"/> counts a floor's habits and, at floor clear, <see cref="Learn"/>s
/// every one over its threshold; AIFollower reads <see cref="Level"/> for its per-level effects. It never forgets: levels only
/// go up within a campaign. Each lesson is queued as a pending announcement, shown as world text when the next floor starts.
///
/// Campaign-scoped statics, same lifetime as PlayerWallet: reset at SubsystemRegistration and in GameFlow.ReturnToMenu, and
/// saved (SaveData.hunter*) through <see cref="CaptureTo"/>/<see cref="RestoreFrom"/>. The save fields were added without
/// bumping SaveSystem.Version: a save from before F105 reads them as 0, which is exactly "a hunter that has learned nothing".
/// </summary>
public static class HunterMemory
{
    /// <summary>The habits it can learn. Append only - a pending announcement is saved as a bitmask of (1 &lt;&lt; value).</summary>
    public enum Habit { Lockers, Throws, Dark, Sprint, Voice }

    /// <summary>The highest level any habit reaches.</summary>
    public const int MaxLevel = 3;

    private static readonly int HabitCount = System.Enum.GetValues(typeof(Habit)).Length;
    private static readonly int[] Levels = new int[HabitCount];
    private static int _pendingMask;

    /// <summary>How well it has learned <paramref name="habit"/>, 0 (not at all) to <see cref="MaxLevel"/>.</summary>
    public static int Level(Habit habit) => Levels[(int)habit];

    /// <summary>
    /// Raises <paramref name="habit"/> by one level (capped) and queues the announcement. Returns false, changing nothing,
    /// when it is already at <see cref="MaxLevel"/>.
    /// </summary>
    public static bool Learn(Habit habit)
    {
        int index = (int)habit;
        if (Levels[index] >= MaxLevel) return false;

        Levels[index]++;
        _pendingMask |= 1 << index;
        return true;
    }

    /// <summary>True when any habit has been learned at least once (the pause menu's lessons panel shows only then).</summary>
    public static bool HasLearnedAny
    {
        get
        {
            for (int i = 0; i < HabitCount; i++) if (Levels[i] > 0) return true;
            return false;
        }
    }

    /// <summary>
    /// The level as a bar, "▮▮▯" for level 2: ■□ when <paramref name="font"/> lacks ▮▯ (LiberationSans does), #- when it lacks
    /// both. Shared by the lesson card (HabitTracker), the pause menu's lessons panel and the descent end screen.
    /// </summary>
    public static string LevelBar(Habit habit, TMPro.TMP_FontAsset font)
    {
        char on = '#', off = '-';
        if (Has(font, '▮') && Has(font, '▯')) { on = '▮'; off = '▯'; }
        else if (Has(font, '■') && Has(font, '□')) { on = '■'; off = '□'; }

        int level = Level(habit);
        System.Text.StringBuilder bar = new System.Text.StringBuilder(MaxLevel);
        for (int i = 0; i < MaxLevel; i++) bar.Append(i < level ? on : off);
        return bar.ToString();
    }

    private static bool Has(TMPro.TMP_FontAsset font, char glyph) => font != null && font.HasCharacter(glyph, true, true);

    /// <summary>The habit's short name: LOCKERS, THROWS, DARK, SPRINT.</summary>
    public static string NameText(Habit habit) => L10n.T("ui.descent.habit." + Key(habit));

    /// <summary>The lesson as the game says it: IT REMEMBERS THE LOCKERS.</summary>
    public static string LessonText(Habit habit) => L10n.T("sub.hunter.learned_" + Key(habit));

    /// <summary>What it does differently now: IT CHECKS LOCKERS AND WAITS OUTSIDE LONGER.</summary>
    public static string EffectText(Habit habit) => L10n.T("ui.hunter.effect." + Key(habit));

    private static string Key(Habit habit) => habit.ToString().ToLowerInvariant();

    /// <summary>The lessons learned since they were last announced, each once; clears the queue.</summary>
    public static List<Habit> TakePendingAnnouncements()
    {
        List<Habit> taken = new List<Habit>();
        for (int i = 0; i < HabitCount; i++)
        {
            if ((_pendingMask & (1 << i)) != 0) taken.Add((Habit)i);
        }
        _pendingMask = 0;
        return taken;
    }

    /// <summary>True while a lesson is waiting to be announced.</summary>
    public static bool HasPending => _pendingMask != 0;

    public static void ResetCampaign()
    {
        System.Array.Clear(Levels, 0, Levels.Length);
        _pendingMask = 0;
    }

    public static void CaptureTo(SaveData data)
    {
        data.hunterLockers = Levels[(int)Habit.Lockers];
        data.hunterThrows = Levels[(int)Habit.Throws];
        data.hunterDark = Levels[(int)Habit.Dark];
        data.hunterSprint = Levels[(int)Habit.Sprint];
        data.hunterVoice = Levels[(int)Habit.Voice];
        data.hunterPendingMask = _pendingMask;
    }

    public static void RestoreFrom(SaveData data)
    {
        ResetCampaign();
        Levels[(int)Habit.Lockers] = Mathf.Clamp(data.hunterLockers, 0, MaxLevel);
        Levels[(int)Habit.Throws] = Mathf.Clamp(data.hunterThrows, 0, MaxLevel);
        Levels[(int)Habit.Dark] = Mathf.Clamp(data.hunterDark, 0, MaxLevel);
        Levels[(int)Habit.Sprint] = Mathf.Clamp(data.hunterSprint, 0, MaxLevel);
        Levels[(int)Habit.Voice] = Mathf.Clamp(data.hunterVoice, 0, MaxLevel);
        _pendingMask = data.hunterPendingMask & ((1 << HabitCount) - 1);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}
