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
    /// <summary>First floor/depth where Sister Agnes ("The Night Nurse") can patrol the halls.</summary>
    public const int NightNurseFrom = 2;
    /// <summary>First floor/depth where Cook Bruno ("The Butcher") can claim Chopping Block stations.</summary>
    public const int ButcherFrom = 2;

    /// <summary>"LSTN": salts the Stalker-or-Listener pick apart from every other roll of the seed.</summary>
    private const int PickSalt = 0x4C53544E;
    /// <summary>"AGNS": salts the Night Nurse roster roll apart from every other roll of the seed.</summary>
    private const int NightNursePickSalt = 0x41474E53;
    /// <summary>"BRUN": salts Dark Bruno's roster roll apart from every other roll of the seed.</summary>
    private const int ButcherPickSalt = 0x4252554E;

    /// <summary>
    /// The set for a story floor (`descent` false) or a descent level (`floorOrDepth` is the real depth).
    /// HARD CAP: at most ONE extra entity besides the Hunter on any floor or DESCENT depth (max 2 entities total at the same time).
    /// </summary>
    public static EntityKind For(bool descent, int floorOrDepth, int seed)
    {
        if (floorOrDepth <= 1) return EntityKind.None;

        if (!descent)
        {
            if (floorOrDepth < StalkerFrom)
            {
                if (floorOrDepth == GuaranteedEarlyFloor(seed)) return EntityKind.NightNurse;
                if (floorOrDepth == GuaranteedEarlyButcherFloor(seed)) return EntityKind.Butcher;
                return EntityKind.None;
            }

            // Story floors 4-5: at most 1 extra entity alongside the Hunter.
            if (floorOrDepth == 4 && !NightNurse.SeenThisCampaign) return EntityKind.NightNurse;
            if (floorOrDepth == 5 && !DarkBruno.SeenThisCampaign) return EntityKind.Butcher;

            System.Span<EntityKind> storyPool = stackalloc EntityKind[3];
            int storyCount = 0;
            storyPool[storyCount++] = EntityKind.Stalker;
            if (RollsNightNurse(false, floorOrDepth, seed)) storyPool[storyCount++] = EntityKind.NightNurse;
            if (RollsButcher(false, floorOrDepth, seed)) storyPool[storyCount++] = EntityKind.Butcher;
            int sh = RunRules.HashSeedFloor(seed ^ PickSalt, floorOrDepth) & 0x7fffffff;
            return storyPool[sh % storyCount];
        }

        // DESCENT mode: at most 1 extra entity alongside the Hunter on any depth.
        if (floorOrDepth < TenantFrom)
        {
            if (RollsNightNurse(true, floorOrDepth, seed)) return EntityKind.NightNurse;
            return RollsButcher(true, floorOrDepth, seed) ? EntityKind.Butcher : EntityKind.None;
        }

        if (floorOrDepth == 3)
        {
            return GuaranteedEarlyFloor(seed) == 3 ? EntityKind.NightNurse
                : PickListener(seed, floorOrDepth) && RollsNightNurse(true, floorOrDepth, seed) ? EntityKind.NightNurse
                : RollsButcher(true, floorOrDepth, seed) ? EntityKind.Butcher
                : EntityKind.Tenant;
        }

        if (floorOrDepth == 4 && !NightNurse.SeenThisCampaign) return EntityKind.NightNurse;
        if (floorOrDepth == 5 && !DarkBruno.SeenThisCampaign) return EntityKind.Butcher;

        // Build candidate list of single entities eligible at this depth and pick exactly 1 deterministically.
        System.Span<EntityKind> pool = stackalloc EntityKind[6];
        int count = 0;
        if (floorOrDepth >= StalkerFrom) pool[count++] = EntityKind.Stalker;
        if (floorOrDepth >= OneOfFrom) pool[count++] = EntityKind.Listener;
        if (floorOrDepth >= TenantFrom) pool[count++] = EntityKind.Tenant;
        if (floorOrDepth >= LivingBodyFrom) pool[count++] = EntityKind.LivingBody;
        if (RollsNightNurse(true, floorOrDepth, seed)) pool[count++] = EntityKind.NightNurse;
        if (RollsButcher(true, floorOrDepth, seed)) pool[count++] = EntityKind.Butcher;

        if (count == 0) return EntityKind.None;
        int h = RunRules.HashSeedFloor(seed ^ PickSalt, floorOrDepth) & 0x7fffffff;
        return pool[h % count];
    }

    /// <summary>
    /// Deterministic floor in {2, 3} guaranteed to roll Sister Agnes for a given seed, alongside the campaign-state
    /// fallback (<see cref="NightNurse.SeenThisCampaign"/>) when each floor rolls a fresh seed.
    /// </summary>
    public static int GuaranteedEarlyFloor(int seed)
    {
        int h = RunRules.HashSeedFloor(seed ^ NightNursePickSalt, 0) & 0x7fffffff;
        return 2 + (h % 2);
    }

    /// <summary>
    /// Deterministic floor in {2, 3} (the complement of <see cref="GuaranteedEarlyFloor"/>) where Cook Bruno is eligible early.
    /// </summary>
    public static int GuaranteedEarlyButcherFloor(int seed) => 5 - GuaranteedEarlyFloor(seed);

    /// <summary>
    /// True when Sister Agnes ("The Night Nurse") is eligible for this floor/depth:
    /// never on Floor/Depth 1, guaranteed on at least one of Floors/Depths 2-3, fallback on Floor/Depth 4 if not yet seen,
    /// and 50% on other floors/depths >= 2.
    /// </summary>
    public static bool RollsNightNurse(bool descent, int floorOrDepth, int seed)
    {
        if (floorOrDepth < NightNurseFrom) return false;
        if (floorOrDepth == 4 && !NightNurse.SeenThisCampaign) return true;
        if (floorOrDepth >= 2 && floorOrDepth <= 3 && floorOrDepth == GuaranteedEarlyFloor(seed)) return true;
        return (RunRules.HashSeedFloor(seed ^ NightNursePickSalt, floorOrDepth) & 1) == 1;
    }

    /// <summary>
    /// True when Cook Bruno ("The Butcher") is eligible for this floor/depth:
    /// never on Floor/Depth 1, guaranteed on the complementary early floor in {2, 3} when Agnes doesn't take it,
    /// fallback on Floor 5 if not yet seen, and 50% on other floors/depths >= 2.
    /// </summary>
    public static bool RollsButcher(bool descent, int floorOrDepth, int seed)
    {
        if (floorOrDepth < ButcherFrom) return false;
        if (floorOrDepth == 5 && !DarkBruno.SeenThisCampaign) return true;
        if (floorOrDepth >= 2 && floorOrDepth <= 3 && floorOrDepth == GuaranteedEarlyButcherFloor(seed)) return true;
        return (RunRules.HashSeedFloor(seed ^ ButcherPickSalt, floorOrDepth) & 1) == 1;
    }

    /// <summary>True for the kinds that can end the run themselves (the Listener and the Butcher).</summary>
    public static bool IsKiller(EntityKind kind) => (kind & (EntityKind.Listener | EntityKind.Butcher)) != 0;

    private static bool PickListener(int seed, int depth) => (RunRules.HashSeedFloor(seed ^ PickSalt, depth) & 1) == 1;
}
