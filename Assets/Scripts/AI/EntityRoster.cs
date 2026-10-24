/// <summary>
/// F107 D1: which entities a level gets. A pure function of (mode, floor or depth, seed), so a Retry of the same level keeps its set.
/// MazeGenerator.debugEntities replaces the answer outright when it is not None (the user's way to test any entity on any floor).
/// </summary>
public static class EntityRoster
{
    /// <summary>First depth with the Stalker in a descent, the same as the story's floor 4.</summary>
    public const int StalkerFrom = 4;
    /// <summary>First depth with an occupied locker.</summary>
    public const int TenantFrom = 3;
    /// <summary>First floor/depth where Sister Agnes ("The Night Nurse") can patrol the halls.</summary>
    public const int NightNurseFrom = 2;
    /// <summary>First floor/depth where Cook Bruno ("The Butcher") can claim Chopping Block stations.</summary>
    public const int ButcherFrom = 2;
    /// <summary>First floor/depth where Elias ("The Frost" / Subject #5) can take over as the solo blackout predator.</summary>
    public const int EliasFrom = 4;

    /// <summary>"LSTN": salts the entity pool pick apart from every other roll of the seed.</summary>
    private const int PickSalt = 0x4C53544E;
    /// <summary>"AGNS": salts the Night Nurse roster roll apart from every other roll of the seed.</summary>
    private const int NightNursePickSalt = 0x41474E53;
    /// <summary>"BRUN": salts Dark Bruno's roster roll apart from every other roll of the seed.</summary>
    private const int ButcherPickSalt = 0x4252554E;
    /// <summary>"ELIS": salts Elias's roster roll apart from every other roll of the seed.</summary>
    private const int EliasPickSalt = 0x454C4953;

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

            // Story floors 4-5: at most 1 extra entity alongside the Hunter (or Elias solo).
            if (floorOrDepth == 4 && !NightNurse.SeenThisCampaign) return EntityKind.NightNurse;
            if (floorOrDepth == 5 && !DarkBruno.SeenThisCampaign) return EntityKind.Butcher;

            System.Span<EntityKind> storyPool = stackalloc EntityKind[4];
            int storyCount = 0;
            storyPool[storyCount++] = EntityKind.Stalker;
            if (RollsNightNurse(false, floorOrDepth, seed)) storyPool[storyCount++] = EntityKind.NightNurse;
            if (RollsButcher(false, floorOrDepth, seed)) storyPool[storyCount++] = EntityKind.Butcher;
            if (RollsElias(false, floorOrDepth, seed)) storyPool[storyCount++] = EntityKind.Elias;
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
                : RollsNightNurse(true, floorOrDepth, seed) ? EntityKind.NightNurse
                : RollsButcher(true, floorOrDepth, seed) ? EntityKind.Butcher
                : EntityKind.Tenant;
        }

        if (floorOrDepth == 4 && !NightNurse.SeenThisCampaign) return EntityKind.NightNurse;
        if (floorOrDepth == 5 && !DarkBruno.SeenThisCampaign) return EntityKind.Butcher;
        if (floorOrDepth == 6 && !Elias.SeenThisCampaign) return EntityKind.Elias;

        // Build candidate list of single entities eligible at this depth and pick exactly 1 deterministically.
        System.Span<EntityKind> pool = stackalloc EntityKind[5];
        int count = 0;
        if (floorOrDepth >= StalkerFrom) pool[count++] = EntityKind.Stalker;
        if (floorOrDepth >= TenantFrom) pool[count++] = EntityKind.Tenant;
        if (RollsNightNurse(true, floorOrDepth, seed)) pool[count++] = EntityKind.NightNurse;
        if (RollsButcher(true, floorOrDepth, seed)) pool[count++] = EntityKind.Butcher;
        if (RollsElias(true, floorOrDepth, seed)) pool[count++] = EntityKind.Elias;

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

    /// <summary>
    /// True when Elias ("The Frost" / Subject #5) is eligible to take over a floor/depth as the solo blackout predator:
    /// never below floor/depth 4, guaranteed on DESCENT depth 6 if not yet seen in this campaign, and 35% on floors/depths >= 4.
    /// </summary>
    public static bool RollsElias(bool descent, int floorOrDepth, int seed)
    {
        if (floorOrDepth < EliasFrom) return false;
        if (descent && floorOrDepth == 6 && !Elias.SeenThisCampaign) return true;
        int roll = (RunRules.HashSeedFloor(seed ^ EliasPickSalt, floorOrDepth) & 0x7fffffff) % 100;
        return roll < 35;
    }

    /// <summary>True for the kinds that can end the run themselves (the Butcher, Elias).</summary>
    public static bool IsKiller(EntityKind kind) => (kind & (EntityKind.Butcher | EntityKind.Elias)) != 0;
}
