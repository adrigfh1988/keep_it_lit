/// <summary>
/// F107 D1: which entities a level gets. A pure function of (mode, floor or depth, seed), so a Retry of the same level keeps its set.
/// MazeGenerator.debugEntities replaces the answer outright when it is not None (the user's way to test any entity on any floor).
/// </summary>
public static class EntityRoster
{
    /// <summary>First depth with the Stalker in a descent, the same as the story's floor 4.</summary>
    public const int StalkerFrom = 4;
    /// <summary>From here a descent level has exactly one of the Stalker and the Listener.</summary>
    public const int OneOfFrom = 6;
    /// <summary>From here a descent level has both.</summary>
    public const int BothFrom = 12;
    /// <summary>First depth with an occupied locker.</summary>
    public const int TenantFrom = 3;
    /// <summary>First depth where a body may be alive (the roll itself is MazeGenerator.BuildEntities's, on the +25 stream).</summary>
    public const int LivingBodyFrom = 5;

    /// <summary>"LSTN": salts the Stalker-or-Listener pick apart from every other roll of the seed.</summary>
    private const int PickSalt = 0x4C53544E;

    /// <summary>
    /// The set for a story floor (`descent` false: the Stalker on floors 4-5 and nothing else, as before) or a descent level
    /// (`floorOrDepth` is the real depth). At most one killer besides the hunter: the Listener is the only one, Stan never captures.
    /// </summary>
    public static EntityKind For(bool descent, int floorOrDepth, int seed)
    {
        if (!descent) return floorOrDepth >= StalkerFrom ? EntityKind.Stalker : EntityKind.None;

        EntityKind set = EntityKind.None;
        if (floorOrDepth >= BothFrom) set |= EntityKind.Stalker | EntityKind.Listener;
        else if (floorOrDepth >= OneOfFrom) set |= PickListener(seed, floorOrDepth) ? EntityKind.Listener : EntityKind.Stalker;
        else if (floorOrDepth >= StalkerFrom) set |= EntityKind.Stalker;

        if (floorOrDepth >= TenantFrom) set |= EntityKind.Tenant;
        if (floorOrDepth >= LivingBodyFrom) set |= EntityKind.LivingBody;
        return set;
    }

    /// <summary>True for the kinds that can end the run themselves (only the Listener today).</summary>
    public static bool IsKiller(EntityKind kind) => (kind & EntityKind.Listener) != 0;

    private static bool PickListener(int seed, int depth) => (RunRules.HashSeedFloor(seed ^ PickSalt, depth) & 1) == 1;
}
