using UnityEngine;

/// <summary>What ended a DESCENT. Appended to, never reordered (F107 D adds the roster's kills under Entity).</summary>
public enum DescentCause { Hunter, Visitor, Timer, Entity }

/// <summary>
/// F107 B1: the state of the DESCENT run in progress (the endless mode outside the story). Campaign-scoped statics with the usual rules:
/// reset at SubsystemRegistration and in GameFlow.ReturnToMenu; deliberately NOT reset by a scene reload, because the run lives across the
/// reload between levels. Nothing here is saved - leaving a descent loses it (the record in <see cref="DescentRecords"/> aside).
/// </summary>
public static class DescentRun
{
    /// <summary>A depth no real level can have, so "never had a loop / Second Wind" reads as "long ago".</summary>
    public const int Never = -100;

    /// <summary>1-based level the player is on. GameFlow.CurrentFloor follows min(Depth, FloorProfile.FinalFloor) so every story-floor lookup stays in range.</summary>
    public static int Depth { get; set; } = 1;

    /// <summary>Net run clock of every finished level plus the one being played when it ended (Time.time stands still in menus and pause).</summary>
    public static float TotalRunSeconds { get; private set; }

    /// <summary>Stars taken over the whole descent.</summary>
    public static int StarsTaken { get; private set; }

    /// <summary>The UsedSeed of the level now built (shown on the end screen).</summary>
    public static int LevelSeed { get; set; }

    /// <summary>The previous level's rolled mechanics pair (a FloorProfile.Objective mask). GameFlow.RolledObjectives stops at floor 5, so a descent keeps its own.</summary>
    public static int PreviousObjectives { get; set; }

    /// <summary>F124: the previous level's floor footprint shape, so two consecutive DESCENT levels never share the same void footprint.</summary>
    public static FloorProfile.FloorShape PreviousShape { get; set; } = FloorProfile.FloorShape.Rectangle;

    /// <summary>F127: the previous level's spatial morphology, so two consecutive DESCENT levels never share the same morphology.</summary>
    public static FloorProfile.MazeMorphology PreviousMorphology { get; set; } = FloorProfile.MazeMorphology.Standard;

    /// <summary>Depth of the last level that had the looping corridor (set where the story sets LoopCorridor.UsedThisCampaign).</summary>
    public static int LastLoopDepth { get; set; } = Never;

    /// <summary>Depth at which a Second Wind was last found in a cache, so they cannot come too close together.</summary>
    public static int LastSecondWindDepth { get; set; } = Never;

    /// <summary>What ended the descent (read by the end screen).</summary>
    public static DescentCause Cause { get; set; } = DescentCause.Hunter;

    /// <summary>F107 D2: which entity it was when <see cref="Cause"/> is Entity (the end screen names it: Listener = HEARD). None otherwise.</summary>
    public static EntityKind CauseEntity { get; set; } = EntityKind.None;

    /// <summary>A new descent: everything back to depth 1, and the campaign statics reset the way GameFlow.ReturnToMenu does. HunterMemory is reset here and then persists across the levels of this descent, which is the point of it.</summary>
    public static void Begin()
    {
        CampaignLifecycle.ResetAllCampaignState();
    }

    /// <summary>A level ended (cleared, or the player died in it): its run time and stars join the totals.</summary>
    public static void CommitLevel(float runSeconds, int stars)
    {
        TotalRunSeconds += Mathf.Max(0f, runSeconds);
        StarsTaken += Mathf.Max(0, stars);
    }

    /// <summary>Own fields only. Called by Begin, by GameFlow.ReturnToMenu and at SubsystemRegistration.</summary>
    public static void ResetState()
    {
        Depth = 1;
        TotalRunSeconds = 0f;
        StarsTaken = 0;
        LevelSeed = 0;
        PreviousObjectives = 0;
        PreviousShape = FloorProfile.FloorShape.Rectangle;
        PreviousMorphology = FloorProfile.MazeMorphology.Standard;
        LastLoopDepth = Never;
        LastSecondWindDepth = Never;
        Cause = DescentCause.Hunter;
        CauseEntity = EntityKind.None;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetState();
}
