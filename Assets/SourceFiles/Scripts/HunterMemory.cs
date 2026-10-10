using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F105 C6: what the hunter has learned about the player's habits, across the whole campaign. Four habits, each a level
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
    public enum Habit { Lockers, Throws, Dark, Sprint }

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
        data.hunterPendingMask = _pendingMask;
    }

    public static void RestoreFrom(SaveData data)
    {
        ResetCampaign();
        Levels[(int)Habit.Lockers] = Mathf.Clamp(data.hunterLockers, 0, MaxLevel);
        Levels[(int)Habit.Throws] = Mathf.Clamp(data.hunterThrows, 0, MaxLevel);
        Levels[(int)Habit.Dark] = Mathf.Clamp(data.hunterDark, 0, MaxLevel);
        Levels[(int)Habit.Sprint] = Mathf.Clamp(data.hunterSprint, 0, MaxLevel);
        _pendingMask = data.hunterPendingMask & ((1 << HabitCount) - 1);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}
